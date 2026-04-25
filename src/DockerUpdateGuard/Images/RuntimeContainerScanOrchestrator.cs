using System.Diagnostics;

using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Data;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Data.Repositories;
using DockerUpdateGuard.Docker;
using DockerUpdateGuard.DockerHub;
using DockerUpdateGuard.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DockerUpdateGuard.Images;

/// <summary>
/// Runtime container scan orchestrator
/// </summary>
public class RuntimeContainerScanOrchestrator : IRuntimeContainerScanOrchestrator
{
    #region Fields

    private readonly ApplicationTelemetry _applicationTelemetry;
    private readonly DockerUpdateGuardDbContext _dbContext;
    private readonly IDockerInstanceClient _dockerInstanceClient;
    private readonly IDockerHubClient _dockerHubClient;
    private readonly IImageCatalogRepository _imageCatalogRepository;
    private readonly IImageReferenceParser _imageReferenceParser;
    private readonly IInstanceDiscoveryService _instanceDiscoveryService;
    private readonly ILogger<RuntimeContainerScanOrchestrator> _logger;
    private readonly IOptionsMonitor<DockerUpdateGuardOptions> _optionsMonitor;
    private readonly IUpdateDetectionService _updateDetectionService;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    public RuntimeContainerScanOrchestrator(ApplicationTelemetry applicationTelemetry,
                                            DockerUpdateGuardDbContext dbContext,
                                            IDockerInstanceClient dockerInstanceClient,
                                            IDockerHubClient dockerHubClient,
                                            IImageCatalogRepository imageCatalogRepository,
                                            IImageReferenceParser imageReferenceParser,
                                            IInstanceDiscoveryService instanceDiscoveryService,
                                            ILogger<RuntimeContainerScanOrchestrator> logger,
                                            IOptionsMonitor<DockerUpdateGuardOptions> optionsMonitor,
                                            IUpdateDetectionService updateDetectionService)
    {
        _applicationTelemetry = applicationTelemetry;
        _dbContext = dbContext;
        _dockerInstanceClient = dockerInstanceClient;
        _dockerHubClient = dockerHubClient;
        _imageCatalogRepository = imageCatalogRepository;
        _imageReferenceParser = imageReferenceParser;
        _instanceDiscoveryService = instanceDiscoveryService;
        _logger = logger;
        _optionsMonitor = optionsMonitor;
        _updateDetectionService = updateDetectionService;
    }

    #endregion // Constructors

    #region Methods

    /// <inheritdoc/>
    public async Task ScanAllAsync(ScanTriggerSource triggerSource, CancellationToken cancellationToken = default)
    {
        await _instanceDiscoveryService.SynchronizeConfiguredInstancesAsync(cancellationToken)
                                       .ConfigureAwait(false);

        var optionsByName = _optionsMonitor.CurrentValue.DockerInstances.ToDictionary(entity => entity.Name, StringComparer.OrdinalIgnoreCase);
        var dockerInstances = await _dbContext.DockerInstances.Where(entity => entity.IsEnabled)
                                                              .ToListAsync(cancellationToken)
                                                              .ConfigureAwait(false);

        var skippedInstanceCount = 0;

        if (dockerInstances.Count == 0)
        {
            _logger.RuntimeContainerScanBatchSkipped(triggerSource);

            return;
        }

        _logger.RuntimeContainerScanBatchStarted(triggerSource, dockerInstances.Count);

        foreach (var dockerInstance in dockerInstances)
        {
            if (optionsByName.TryGetValue(dockerInstance.Name, out var configuredInstance) == false)
            {
                skippedInstanceCount++;

                _logger.RuntimeContainerScanSkippedConfigurationMissing(dockerInstance.Name);

                continue;
            }

            if (configuredInstance.Enabled == false)
            {
                skippedInstanceCount++;

                _logger.RuntimeContainerScanSkippedConfigurationDisabled(dockerInstance.Name);

                continue;
            }

            await ScanInstanceAsync(dockerInstance,
                                    configuredInstance,
                                    triggerSource,
                                    cancellationToken).ConfigureAwait(false);
        }

        _logger.RuntimeContainerScanBatchCompleted(triggerSource,
                                                   dockerInstances.Count - skippedInstanceCount,
                                                   skippedInstanceCount);
    }

    /// <summary>
    /// Scan a single Docker instance
    /// </summary>
    /// <param name="dockerInstance">Persisted Docker instance</param>
    /// <param name="configuredInstance">Configured Docker instance options</param>
    /// <param name="triggerSource">Trigger source</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    private async Task ScanInstanceAsync(DockerInstance dockerInstance,
                                         DockerInstanceOptions configuredInstance,
                                         ScanTriggerSource triggerSource,
                                         CancellationToken cancellationToken)
    {
        var processedContainerCount = 0;
        var scanRun = new ScanRun
                      {
                          Type = ScanRunType.RuntimeContainer,
                          Status = ScanRunStatus.Running,
                          TriggerSource = triggerSource,
                          DockerInstanceId = dockerInstance.Id,
                          StartedAtUtc = DateTimeOffset.UtcNow,
                      };
        var stopwatch = Stopwatch.StartNew();
        var statusMessages = new List<string>();
        var finalStatus = ScanRunStatus.Succeeded;

        _logger.RuntimeContainerScanStarted(dockerInstance.Name, triggerSource);

        _dbContext.ScanRuns.Add(scanRun);

        await _dbContext.SaveChangesAsync(cancellationToken)
                        .ConfigureAwait(false);

        await DeactivateRuntimeFindingsAsync(dockerInstance.Id, cancellationToken).ConfigureAwait(false);

        try
        {
            var discoveryResult = await _dockerInstanceClient.DiscoverContainersAsync(configuredInstance, cancellationToken)
                                                             .ConfigureAwait(false);

            if (discoveryResult.Status != ExternalOperationStatus.Succeeded || discoveryResult.Data is null)
            {
                finalStatus = discoveryResult.Status == ExternalOperationStatus.Failed ? ScanRunStatus.Failed : ScanRunStatus.Partial;

                statusMessages.Add(discoveryResult.Message ?? $"Container discovery failed for '{dockerInstance.Name}'");

                _logger.RuntimeContainerDiscoveryIncomplete(dockerInstance.Name,
                                                            discoveryResult.Status,
                                                            discoveryResult.Message);
            }
            else
            {
                processedContainerCount = discoveryResult.Data.Count;

                if (processedContainerCount == 0)
                {
                    _logger.RuntimeContainerScanFoundNoContainers(dockerInstance.Name);
                }

                foreach (var container in discoveryResult.Data)
                {
                    var parsedReference = _imageReferenceParser.Parse(container.ImageReference);
                    var imageVersion = await _imageCatalogRepository.GetOrCreateImageVersionAsync(parsedReference.Registry,
                                                                                                  parsedReference.Repository,
                                                                                                  parsedReference.Tag,
                                                                                                  parsedReference.Digest,
                                                                                                  cancellationToken: cancellationToken)
                                                                    .ConfigureAwait(false);

                    imageVersion.Source = ImageVersionSource.RuntimeContainer;

                    var snapshot = new ContainerSnapshot
                                   {
                                       DockerInstanceId = dockerInstance.Id,
                                       ImageVersionId = imageVersion.Id,
                                       ScanRunId = scanRun.Id,
                                       ContainerId = container.ContainerId,
                                       Name = container.Name,
                                       ComposeProject = container.ComposeProject,
                                       StackName = container.StackName,
                                       ServiceName = container.ServiceName,
                                       Status = container.RuntimeStatus,
                                       IsRunning = container.IsRunning,
                                       RecordedAtUtc = DateTimeOffset.UtcNow,
                                   };

                    _dbContext.ContainerSnapshots.Add(snapshot);

                    var tagsResult = await _dockerHubClient.GetTagsAsync(parsedReference.Registry,
                                                                         parsedReference.Repository,
                                                                         cancellationToken)
                                                           .ConfigureAwait(false);

                    if (tagsResult.Status == ExternalOperationStatus.Succeeded && tagsResult.Data is not null)
                    {
                        var evaluation = _updateDetectionService.Evaluate(parsedReference, tagsResult.Data);

                        if (evaluation.Status == UpdateEvaluationStatus.UpdateAvailable
                            || evaluation.Status == UpdateEvaluationStatus.NeedsReview)
                        {
                            await CreateRuntimeFindingAsync(scanRun,
                                                            snapshot,
                                                            imageVersion,
                                                            evaluation,
                                                            cancellationToken).ConfigureAwait(false);
                        }
                    }
                    else if (tagsResult.Status != ExternalOperationStatus.NotFound
                             && tagsResult.Status != ExternalOperationStatus.Unsupported)
                    {
                        finalStatus = ScanRunStatus.Partial;

                        statusMessages.Add(tagsResult.Message ?? $"Unable to evaluate runtime image '{container.ImageReference}'");

                        _logger.RuntimeContainerRegistryEvaluationIncomplete(dockerInstance.Name,
                                                                             container.ImageReference,
                                                                             tagsResult.Status,
                                                                             tagsResult.Message);
                    }
                    else
                    {
                        finalStatus = ScanRunStatus.Partial;

                        statusMessages.Add(tagsResult.Message ?? $"Runtime image '{container.ImageReference}' cannot be evaluated by the current Docker Hub adapter");

                        _logger.RuntimeContainerRegistryEvaluationUnsupported(dockerInstance.Name,
                                                                              container.ImageReference,
                                                                              tagsResult.Status,
                                                                              tagsResult.Message);
                    }
                }
            }
        }
        catch (Exception exception)
        {
            finalStatus = ScanRunStatus.Failed;

            statusMessages.Add(exception.Message);

            _logger.RuntimeContainerScanFailed(exception, dockerInstance.Name);
        }

        scanRun.Status = finalStatus;
        scanRun.CompletedAtUtc = DateTimeOffset.UtcNow;
        scanRun.ErrorMessage = statusMessages.Count == 0 ? null : string.Join(Environment.NewLine, statusMessages.Distinct());

        await _dbContext.SaveChangesAsync(cancellationToken)
                        .ConfigureAwait(false);

        await _applicationTelemetry.RefreshInventoryMetricsAsync(_dbContext, cancellationToken)
                                   .ConfigureAwait(false);

        _applicationTelemetry.RecordScanRun(ScanRunType.RuntimeContainer,
                                            finalStatus,
                                            stopwatch.Elapsed);

        _logger.RuntimeContainerScanCompleted(dockerInstance.Name,
                                              finalStatus,
                                              processedContainerCount,
                                              statusMessages.Count,
                                              stopwatch.ElapsedMilliseconds);
    }

    /// <summary>
    /// Resolve existing active runtime findings
    /// </summary>
    /// <param name="dockerInstanceId">Docker instance identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    private async Task DeactivateRuntimeFindingsAsync(Guid dockerInstanceId, CancellationToken cancellationToken)
    {
        var activeFindings = await _dbContext.UpdateFindings.Include(entity => entity.ContainerSnapshot)
                                                            .Where(entity => entity.ContainerSnapshot != null
                                                                             && entity.ContainerSnapshot.DockerInstanceId == dockerInstanceId
                                                                             && entity.IsActive)
                                                            .ToListAsync(cancellationToken)
                                                            .ConfigureAwait(false);

        foreach (var activeFinding in activeFindings)
        {
            activeFinding.IsActive = false;
            activeFinding.ResolvedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Create a runtime update finding
    /// </summary>
    /// <param name="scanRun">Scan run</param>
    /// <param name="snapshot">Container snapshot</param>
    /// <param name="subjectImageVersion">Subject image version</param>
    /// <param name="evaluation">Update evaluation</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    private async Task CreateRuntimeFindingAsync(ScanRun scanRun,
                                                 ContainerSnapshot snapshot,
                                                 ImageVersion subjectImageVersion,
                                                 UpdateEvaluationResult evaluation,
                                                 CancellationToken cancellationToken)
    {
        await EnsureRegistryRepositoryLoadedAsync(subjectImageVersion, cancellationToken).ConfigureAwait(false);

        Guid? recommendedImageVersionId = null;

        if (string.IsNullOrWhiteSpace(evaluation.RecommendedTag) == false)
        {
            var recommendedVersion = await _imageCatalogRepository.GetOrCreateImageVersionAsync(subjectImageVersion.RegistryRepository.Registry,
                                                                                                subjectImageVersion.RegistryRepository.Repository,
                                                                                                evaluation.RecommendedTag,
                                                                                                evaluation.RecommendedDigest,
                                                                                                cancellationToken: cancellationToken)
                                                                  .ConfigureAwait(false);
            recommendedImageVersionId = recommendedVersion.Id;
        }

        var finding = new UpdateFinding
                      {
                          ScanRunId = scanRun.Id,
                          ContainerSnapshotId = snapshot.Id,
                          SubjectImageVersionId = subjectImageVersion.Id,
                          RecommendedImageVersionId = recommendedImageVersionId,
                          Type = evaluation.Status == UpdateEvaluationStatus.UpdateAvailable
                                     ? UpdateFindingType.RuntimeImageUpdate
                                     : UpdateFindingType.TagRecommendation,
                          Summary = evaluation.Summary,
                          Details = evaluation.Details,
                      };

        foreach (var candidate in evaluation.Candidates.Select((value, index) => new
                                                                                 {
                                                                                     Value = value,
                                                                                     Index = index
                                                                                 }))
        {
            finding.TagCandidates.Add(new TagCandidate
                                      {
                                          Tag = candidate.Value.Tag,
                                          Digest = candidate.Value.Digest,
                                          Rank = candidate.Index + 1,
                                          IsRecommended = string.Equals(candidate.Value.Tag,
                                                                        evaluation.RecommendedTag,
                                                                        StringComparison.OrdinalIgnoreCase),
                                          PublishedAtUtc = candidate.Value.PublishedAtUtc,
                                          Reason = evaluation.Summary,
                                      });
        }

        _dbContext.UpdateFindings.Add(finding);
    }

    /// <summary>
    /// Ensure the registry repository navigation is available on an image version
    /// </summary>
    /// <param name="imageVersion">Image version</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    private async Task EnsureRegistryRepositoryLoadedAsync(ImageVersion imageVersion, CancellationToken cancellationToken)
    {
        if (imageVersion.RegistryRepository is not null)
        {
            return;
        }

        var registryRepository = await _dbContext.RegistryRepositories
                                                 .SingleOrDefaultAsync(entity => entity.Id == imageVersion.RegistryRepositoryId, cancellationToken)
                                                 .ConfigureAwait(false);

        if (registryRepository is null)
        {
            throw new InvalidOperationException($"Registry repository '{imageVersion.RegistryRepositoryId}' was not found for image version '{imageVersion.Id}'");
        }

        imageVersion.RegistryRepository = registryRepository;
    }

    #endregion // Methods
}