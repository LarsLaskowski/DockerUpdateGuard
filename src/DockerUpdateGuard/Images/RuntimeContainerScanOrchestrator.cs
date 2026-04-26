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

    /// <summary>
    /// Application telemetry
    /// </summary>
    private readonly ApplicationTelemetry _applicationTelemetry;

    /// <summary>
    /// Database context
    /// </summary>
    private readonly DockerUpdateGuardDbContext _dbContext;

    /// <summary>
    /// Docker-instance client
    /// </summary>
    private readonly IDockerInstanceClient _dockerInstanceClient;

    /// <summary>
    /// Derived base-runtime detector
    /// </summary>
    private readonly IDerivedBaseRuntimeDetector _derivedBaseRuntimeDetector;

    /// <summary>
    /// .NET release metadata service
    /// </summary>
    private readonly IDotNetReleaseMetadataService _dotNetReleaseMetadataService;

    /// <summary>
    /// NGINX release metadata service
    /// </summary>
    private readonly INginxReleaseMetadataService _nginxReleaseMetadataService;

    /// <summary>
    /// Image-catalog repository
    /// </summary>
    private readonly IImageCatalogRepository _imageCatalogRepository;

    /// <summary>
    /// Image-reference parser
    /// </summary>
    private readonly IImageReferenceParser _imageReferenceParser;

    /// <summary>
    /// Instance-discovery service
    /// </summary>
    private readonly IInstanceDiscoveryService _instanceDiscoveryService;

    /// <summary>
    /// Logger
    /// </summary>
    private readonly ILogger<RuntimeContainerScanOrchestrator> _logger;

    /// <summary>
    /// Options monitor
    /// </summary>
    private readonly IOptionsMonitor<DockerUpdateGuardOptions> _optionsMonitor;

    /// <summary>
    /// Registry-metadata service
    /// </summary>
    private readonly IRegistryMetadataService _registryMetadataService;

    /// <summary>
    /// Update-detection service
    /// </summary>
    private readonly IUpdateDetectionService _updateDetectionService;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="applicationTelemetry">Application telemetry</param>
    /// <param name="dbContext">Database context</param>
    /// <param name="dockerInstanceClient">Docker instance client</param>
    /// <param name="derivedBaseRuntimeDetector">Derived base-runtime detector</param>
    /// <param name="dotNetReleaseMetadataService">.NET release metadata service</param>
    /// <param name="nginxReleaseMetadataService">NGINX release metadata service</param>
    /// <param name="imageCatalogRepository">Image catalog repository</param>
    /// <param name="imageReferenceParser">Image reference parser</param>
    /// <param name="instanceDiscoveryService">Instance discovery service</param>
    /// <param name="logger">Logger</param>
    /// <param name="optionsMonitor">Application options monitor</param>
    /// <param name="registryMetadataService">Registry metadata service</param>
    /// <param name="updateDetectionService">Update detection service</param>
    public RuntimeContainerScanOrchestrator(ApplicationTelemetry applicationTelemetry,
                                            DockerUpdateGuardDbContext dbContext,
                                            IDockerInstanceClient dockerInstanceClient,
                                            IDerivedBaseRuntimeDetector derivedBaseRuntimeDetector,
                                            IDotNetReleaseMetadataService dotNetReleaseMetadataService,
                                            INginxReleaseMetadataService nginxReleaseMetadataService,
                                            IImageCatalogRepository imageCatalogRepository,
                                            IImageReferenceParser imageReferenceParser,
                                            IInstanceDiscoveryService instanceDiscoveryService,
                                            ILogger<RuntimeContainerScanOrchestrator> logger,
                                            IOptionsMonitor<DockerUpdateGuardOptions> optionsMonitor,
                                            IRegistryMetadataService registryMetadataService,
                                            IUpdateDetectionService updateDetectionService)
    {
        _applicationTelemetry = applicationTelemetry;
        _dbContext = dbContext;
        _dockerInstanceClient = dockerInstanceClient;
        _derivedBaseRuntimeDetector = derivedBaseRuntimeDetector;
        _dotNetReleaseMetadataService = dotNetReleaseMetadataService;
        _nginxReleaseMetadataService = nginxReleaseMetadataService;
        _imageCatalogRepository = imageCatalogRepository;
        _imageReferenceParser = imageReferenceParser;
        _instanceDiscoveryService = instanceDiscoveryService;
        _logger = logger;
        _optionsMonitor = optionsMonitor;
        _registryMetadataService = registryMetadataService;
        _updateDetectionService = updateDetectionService;
    }

    #endregion // Constructors

    #region Static methods

    /// <summary>
    /// Map an update evaluation result to the persisted runtime assessment
    /// </summary>
    /// <param name="snapshot">Runtime snapshot</param>
    /// <param name="evaluation">Update evaluation</param>
    private static void ApplyUpdateAssessment(ContainerSnapshot snapshot, UpdateEvaluationResult evaluation)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(evaluation);

        snapshot.UpdateAssessmentStatus = evaluation.Status switch
                                          {
                                              UpdateEvaluationStatus.UpToDate => UpdateAssessmentStatus.UpToDate,
                                              UpdateEvaluationStatus.UpdateAvailable => UpdateAssessmentStatus.UpdateAvailable,
                                              UpdateEvaluationStatus.NeedsReview => UpdateAssessmentStatus.ManualReviewRequired,
                                              UpdateEvaluationStatus.Unknown => UpdateAssessmentStatus.NoTagData,
                                              _ => UpdateAssessmentStatus.NotEvaluated,
                                          };
        snapshot.UpdateAssessmentMessage = string.IsNullOrWhiteSpace(evaluation.Details)
                                               ? evaluation.Summary
                                               : $"{evaluation.Summary} {evaluation.Details}";
    }

    /// <summary>
    /// Create a normalized repository key
    /// </summary>
    /// <param name="imageVersion">Image version</param>
    /// <returns>Repository key</returns>
    private static string CreateRepositoryKey(ImageVersion imageVersion)
    {
        ArgumentNullException.ThrowIfNull(imageVersion.RegistryRepository);

        return CreateRepositoryKey(imageVersion.RegistryRepository.Registry, imageVersion.RegistryRepository.Repository);
    }

    /// <summary>
    /// Create a normalized repository key
    /// </summary>
    /// <param name="registry">Registry host</param>
    /// <param name="repository">Repository path</param>
    /// <returns>Repository key</returns>
    private static string CreateRepositoryKey(string registry, string repository)
    {
        return $"{registry.Trim().ToLowerInvariant()}|{repository.Trim().ToLowerInvariant()}";
    }

    /// <summary>
    /// Format a runtime version
    /// </summary>
    /// <param name="version">Version value</param>
    /// <returns>Formatted version</returns>
    private static string FormatVersion(Version version)
    {
        return version.Build >= 0 ? version.ToString(3) : version.ToString();
    }

    /// <summary>
    /// Determine whether a newer patch exists in the same channel
    /// </summary>
    /// <param name="currentVersion">Current runtime version</param>
    /// <param name="latestVersion">Latest runtime version</param>
    /// <returns>True when a newer patch is available</returns>
    private static bool IsPatchBehind(Version currentVersion, Version latestVersion)
    {
        return latestVersion.Major == currentVersion.Major
               && latestVersion.Minor == currentVersion.Minor
               && latestVersion > currentVersion;
    }

    /// <summary>
    /// Build a derived base-runtime finding details string
    /// </summary>
    /// <param name="dockerInstanceName">Docker instance name</param>
    /// <param name="snapshot">Container snapshot</param>
    /// <param name="runtimeDescriptor">Derived runtime descriptor</param>
    /// <param name="channelRelease">Latest channel release</param>
    /// <param name="isOwnImage">Whether the image is owned</param>
    /// <returns>Details text</returns>
    private static string BuildDotNetDerivedBaseRuntimeDetails(string dockerInstanceName,
                                                               ContainerSnapshot snapshot,
                                                               DerivedBaseRuntimeDescriptor runtimeDescriptor,
                                                               DotNetChannelReleaseData channelRelease,
                                                               bool isOwnImage)
    {
        ArgumentNullException.ThrowIfNull(runtimeDescriptor.RuntimeVersion);

        var message = $"Container '{snapshot.Name}' on '{dockerInstanceName}' appears to use .NET {FormatVersion(runtimeDescriptor.RuntimeVersion)}. Latest .NET {channelRelease.ChannelVersion} runtime is {FormatVersion(channelRelease.LatestRuntimeVersion)}.";

        if (channelRelease.IsSecurityRelease)
        {
            message = $"{message} The latest channel release includes security fixes.";
        }

        if (string.Equals(channelRelease.SupportPhase, "eol", StringComparison.OrdinalIgnoreCase)
            || (channelRelease.EndOfLifeDateUtc is not null && channelRelease.EndOfLifeDateUtc <= DateTimeOffset.UtcNow))
        {
            message = $"{message} This channel is no longer supported.";
        }

        return isOwnImage
                   ? $"{message} Rebuild and republish the image to pick up the newer .NET base runtime."
                   : $"{message} This image must be refreshed by its upstream publisher before the newer .NET base runtime can be consumed.";
    }

    /// <summary>
    /// Build a derived NGINX base-runtime finding details string
    /// </summary>
    /// <param name="dockerInstanceName">Docker instance name</param>
    /// <param name="snapshot">Container snapshot</param>
    /// <param name="runtimeDescriptor">Derived runtime descriptor</param>
    /// <param name="channelRelease">Latest channel release</param>
    /// <param name="isOwnImage">Whether the image is owned</param>
    /// <returns>Details text</returns>
    private static string BuildNginxDerivedBaseRuntimeDetails(string dockerInstanceName,
                                                              ContainerSnapshot snapshot,
                                                              DerivedBaseRuntimeDescriptor runtimeDescriptor,
                                                              NginxChannelReleaseData channelRelease,
                                                              bool isOwnImage)
    {
        ArgumentNullException.ThrowIfNull(runtimeDescriptor.RuntimeVersion);

        var message = $"Container '{snapshot.Name}' on '{dockerInstanceName}' appears to use NGINX {FormatVersion(runtimeDescriptor.RuntimeVersion)}. Latest NGINX {channelRelease.ChannelVersion} release is {FormatVersion(channelRelease.LatestVersion)}.";

        return isOwnImage
                   ? $"{message} Rebuild and republish the image to pick up the newer NGINX base runtime."
                   : $"{message} This image must be refreshed by its upstream publisher before the newer NGINX base runtime can be consumed.";
    }

    #endregion // Static methods

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
            var ownRepositoryKeys = await LoadOwnRepositoryKeysAsync(cancellationToken).ConfigureAwait(false);
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
                    ContainerSnapshot? snapshot = null;

                    try
                    {
                        var parsedReference = _imageReferenceParser.Parse(container.ImageReference);

                        if (string.IsNullOrWhiteSpace(parsedReference.Digest)
                            && string.IsNullOrWhiteSpace(container.ImageDigest) == false)
                        {
                            parsedReference.Digest = container.ImageDigest.Trim().ToLowerInvariant();
                        }

                        var imageVersion = await _imageCatalogRepository.GetOrCreateImageVersionAsync(parsedReference.Registry,
                                                                                                      parsedReference.Repository,
                                                                                                      parsedReference.Tag,
                                                                                                      parsedReference.Digest,
                                                                                                      cancellationToken: cancellationToken)
                                                                        .ConfigureAwait(false);

                        imageVersion.Source = ImageVersionSource.RuntimeContainer;

                        snapshot = new ContainerSnapshot
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
                                       UpdateAssessmentStatus = UpdateAssessmentStatus.NotEvaluated,
                                       RecordedAtUtc = DateTimeOffset.UtcNow,
                                   };

                        _dbContext.ContainerSnapshots.Add(snapshot);

                        var tagsResult = await _registryMetadataService.GetTagsAsync(parsedReference.Registry,
                                                                                     parsedReference.Repository,
                                                                                     cancellationToken)
                                                                       .ConfigureAwait(false);

                        if (tagsResult.Status == ExternalOperationStatus.Succeeded && tagsResult.Data is not null)
                        {
                            var availableTags = await MergeCurrentTagMetadataAsync(parsedReference,
                                                                                   tagsResult.Data,
                                                                                   cancellationToken).ConfigureAwait(false);
                            var evaluation = _updateDetectionService.Evaluate(parsedReference, availableTags);

                            ApplyUpdateAssessment(snapshot, evaluation);

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
                            snapshot.UpdateAssessmentStatus = UpdateAssessmentStatus.Failed;
                            snapshot.UpdateAssessmentMessage = tagsResult.Message ?? $"Unable to evaluate runtime image '{container.ImageReference}'";
                            finalStatus = ScanRunStatus.Partial;

                            statusMessages.Add(tagsResult.Message ?? $"Unable to evaluate runtime image '{container.ImageReference}'");

                            _logger.RuntimeContainerRegistryEvaluationIncomplete(dockerInstance.Name,
                                                                                 container.ImageReference,
                                                                                 tagsResult.Status,
                                                                                 tagsResult.Message);
                        }
                        else
                        {
                            snapshot.UpdateAssessmentStatus = tagsResult.Status == ExternalOperationStatus.NotFound
                                                                  ? UpdateAssessmentStatus.NoTagData
                                                                  : UpdateAssessmentStatus.Unsupported;
                            snapshot.UpdateAssessmentMessage = tagsResult.Message ?? $"Runtime image '{container.ImageReference}' cannot be evaluated by the current registry adapters";
                            finalStatus = ScanRunStatus.Partial;

                            statusMessages.Add(tagsResult.Message ?? $"Runtime image '{container.ImageReference}' cannot be evaluated by the current registry adapters");

                            _logger.RuntimeContainerRegistryEvaluationUnsupported(dockerInstance.Name,
                                                                                  container.ImageReference,
                                                                                  tagsResult.Status,
                                                                                  tagsResult.Message);
                        }

                        await CreateDerivedBaseRuntimeFindingAsync(scanRun,
                                                                   snapshot,
                                                                   imageVersion,
                                                                   container,
                                                                   dockerInstance.Name,
                                                                   configuredInstance,
                                                                   ownRepositoryKeys,
                                                                   cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        finalStatus = ScanRunStatus.Partial;

                        if (snapshot is not null)
                        {
                            snapshot.UpdateAssessmentStatus = UpdateAssessmentStatus.Failed;
                            snapshot.UpdateAssessmentMessage = exception.Message;
                        }

                        statusMessages.Add($"Container '{container.Name}' could not be processed: {exception.Message}");

                        _logger.RuntimeContainerProcessingFailed(exception,
                                                                 dockerInstance.Name,
                                                                 container.Name,
                                                                 container.ImageReference);
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
                                          Digest = UpdateFindingPersistenceHelper.NormalizeCandidateDigest(candidate.Value.Digest),
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
    /// Create a derived base-runtime finding when the local image uses an outdated .NET runtime
    /// </summary>
    /// <param name="scanRun">Scan run</param>
    /// <param name="snapshot">Container snapshot</param>
    /// <param name="subjectImageVersion">Subject image version</param>
    /// <param name="container">Runtime container descriptor</param>
    /// <param name="dockerInstanceName">Docker instance name</param>
    /// <param name="configuredInstance">Configured Docker instance</param>
    /// <param name="ownRepositoryKeys">Known own repository keys</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    private async Task CreateDerivedBaseRuntimeFindingAsync(ScanRun scanRun,
                                                            ContainerSnapshot snapshot,
                                                            ImageVersion subjectImageVersion,
                                                            RuntimeContainerDescriptor container,
                                                            string dockerInstanceName,
                                                            DockerInstanceOptions configuredInstance,
                                                            IReadOnlySet<string> ownRepositoryKeys,
                                                            CancellationToken cancellationToken)
    {
        await EnsureRegistryRepositoryLoadedAsync(subjectImageVersion, cancellationToken).ConfigureAwait(false);

        var imageReferenceOrId = string.IsNullOrWhiteSpace(container.LocalImageId)
                                     ? container.ImageReference
                                     : container.LocalImageId!;
        var inspectResult = await _dockerInstanceClient.InspectImageAsync(configuredInstance,
                                                                          imageReferenceOrId,
                                                                          cancellationToken)
                                                       .ConfigureAwait(false);

        if (inspectResult.Status != ExternalOperationStatus.Succeeded || inspectResult.Data is null)
        {
            return;
        }

        var historyResult = await _dockerInstanceClient.GetImageHistoryAsync(configuredInstance,
                                                                             imageReferenceOrId,
                                                                             cancellationToken)
                                                       .ConfigureAwait(false);
        var historyEntries = historyResult.Status == ExternalOperationStatus.Succeeded && historyResult.Data is not null
                                 ? historyResult.Data
                                 : [];
        var runtimeDescriptor = _derivedBaseRuntimeDetector.Detect(inspectResult.Data, historyEntries);

        if (runtimeDescriptor?.RuntimeVersion is null
            || string.IsNullOrWhiteSpace(runtimeDescriptor.ChannelVersion))
        {
            return;
        }

        var isOwnImage = ownRepositoryKeys.Contains(CreateRepositoryKey(subjectImageVersion));

        switch (runtimeDescriptor.Kind)
        {
            case DerivedBaseRuntimeKind.DotNet:
                {
                    var releaseResult = await _dotNetReleaseMetadataService.GetChannelReleaseAsync(runtimeDescriptor.ChannelVersion, cancellationToken)
                                                                           .ConfigureAwait(false);

                    if (releaseResult.Status != ExternalOperationStatus.Succeeded
                        || releaseResult.Data is null
                        || IsPatchBehind(runtimeDescriptor.RuntimeVersion, releaseResult.Data.LatestRuntimeVersion) == false)
                    {
                        return;
                    }

                    _dbContext.UpdateFindings.Add(new UpdateFinding
                                                  {
                                                      ScanRunId = scanRun.Id,
                                                      ContainerSnapshotId = snapshot.Id,
                                                      SubjectImageVersionId = subjectImageVersion.Id,
                                                      Type = UpdateFindingType.DerivedBaseRuntimeUpdate,
                                                      Summary = isOwnImage
                                                                    ? "Own image uses an outdated .NET base runtime"
                                                                    : "A linked image uses an outdated .NET base runtime",
                                                      Details = BuildDotNetDerivedBaseRuntimeDetails(dockerInstanceName,
                                                                                                     snapshot,
                                                                                                     runtimeDescriptor,
                                                                                                     releaseResult.Data,
                                                                                                     isOwnImage),
                                                  });
                }
                break;

            case DerivedBaseRuntimeKind.Nginx:
                {
                    var releaseResult = await _nginxReleaseMetadataService.GetChannelReleaseAsync(runtimeDescriptor.ChannelVersion, cancellationToken)
                                                                          .ConfigureAwait(false);

                    if (releaseResult.Status != ExternalOperationStatus.Succeeded
                        || releaseResult.Data is null
                        || IsPatchBehind(runtimeDescriptor.RuntimeVersion, releaseResult.Data.LatestVersion) == false)
                    {
                        return;
                    }

                    _dbContext.UpdateFindings.Add(new UpdateFinding
                                                  {
                                                      ScanRunId = scanRun.Id,
                                                      ContainerSnapshotId = snapshot.Id,
                                                      SubjectImageVersionId = subjectImageVersion.Id,
                                                      Type = UpdateFindingType.DerivedBaseRuntimeUpdate,
                                                      Summary = isOwnImage
                                                                    ? "Own image uses an outdated NGINX base runtime"
                                                                    : "A linked image uses an outdated NGINX base runtime",
                                                      Details = BuildNginxDerivedBaseRuntimeDetails(dockerInstanceName,
                                                                                                    snapshot,
                                                                                                    runtimeDescriptor,
                                                                                                    releaseResult.Data,
                                                                                                    isOwnImage),
                                                  });
                }
                break;
        }
    }

    /// <summary>
    /// Merge the exact metadata for the current tag into the available tag set
    /// </summary>
    /// <param name="currentImage">Current image reference</param>
    /// <param name="availableTags">Available tags</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Merged tag set</returns>
    private async Task<IReadOnlyList<DockerHubTagData>> MergeCurrentTagMetadataAsync(ImageReference currentImage,
                                                                                     IReadOnlyList<DockerHubTagData> availableTags,
                                                                                     CancellationToken cancellationToken)
    {
        var currentTagResult = await _registryMetadataService.GetTagAsync(currentImage, cancellationToken)
                                                             .ConfigureAwait(false);

        if (currentTagResult.Status != ExternalOperationStatus.Succeeded || currentTagResult.Data is null)
        {
            return availableTags;
        }

        return availableTags.Where(tag => string.Equals(tag.Tag, currentImage.Tag, StringComparison.OrdinalIgnoreCase) == false)
                            .Append(currentTagResult.Data)
                            .ToList();
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

    /// <summary>
    /// Load normalized repository keys for own observed images
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Own repository keys</returns>
    private async Task<IReadOnlySet<string>> LoadOwnRepositoryKeysAsync(CancellationToken cancellationToken)
    {
        var observedImages = await _dbContext.ObservedImages.Include(entity => entity.CurrentImageVersion)
                                                            .ThenInclude(entity => entity.RegistryRepository)
                                                            .Where(entity => entity.Source == RegistrationSource.Discovery)
                                                            .ToListAsync(cancellationToken)
                                                            .ConfigureAwait(false);
        var repositoryKeys = observedImages.Select(entity => CreateRepositoryKey(entity.CurrentImageVersion.RegistryRepository.Registry,
                                                                                 entity.CurrentImageVersion.RegistryRepository.Repository))
                                           .ToList();

        return repositoryKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    #endregion // Methods
}