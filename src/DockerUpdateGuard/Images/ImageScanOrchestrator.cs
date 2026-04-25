using System.Diagnostics;
using System.Text.Json;

using DockerUpdateGuard.Data;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Data.Repositories;
using DockerUpdateGuard.DockerHub;
using DockerUpdateGuard.Infrastructure;

using Microsoft.EntityFrameworkCore;

namespace DockerUpdateGuard.Images;

/// <summary>
/// Observed image scan orchestrator
/// </summary>
public class ImageScanOrchestrator : IImageScanOrchestrator
{
    #region Fields

    private readonly ApplicationTelemetry _applicationTelemetry;
    private readonly IBaseImageResolver _baseImageResolver;
    private readonly DockerUpdateGuardDbContext _dbContext;
    private readonly IImageCatalogRepository _imageCatalogRepository;
    private readonly IImageReferenceParser _imageReferenceParser;
    private readonly ILogger<ImageScanOrchestrator> _logger;
    private readonly IRegistryMetadataService _registryMetadataService;
    private readonly IUpdateDetectionService _updateDetectionService;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="applicationTelemetry">Application telemetry</param>
    /// <param name="baseImageResolver">Base image resolver</param>
    /// <param name="dbContext">Database context</param>
    /// <param name="imageCatalogRepository">Image catalog repository</param>
    /// <param name="imageReferenceParser">Image reference parser</param>
    /// <param name="logger">Logger</param>
    /// <param name="registryMetadataService">Registry metadata service</param>
    /// <param name="updateDetectionService">Update detection service</param>
    public ImageScanOrchestrator(ApplicationTelemetry applicationTelemetry,
                                 IBaseImageResolver baseImageResolver,
                                 DockerUpdateGuardDbContext dbContext,
                                 IImageCatalogRepository imageCatalogRepository,
                                 IImageReferenceParser imageReferenceParser,
                                 ILogger<ImageScanOrchestrator> logger,
                                 IRegistryMetadataService registryMetadataService,
                                 IUpdateDetectionService updateDetectionService)
    {
        _applicationTelemetry = applicationTelemetry;
        _baseImageResolver = baseImageResolver;
        _dbContext = dbContext;
        _imageCatalogRepository = imageCatalogRepository;
        _imageReferenceParser = imageReferenceParser;
        _logger = logger;
        _registryMetadataService = registryMetadataService;
        _updateDetectionService = updateDetectionService;
    }

    #endregion // Constructors

    #region Methods

    /// <inheritdoc/>
    public async Task ScanAllAsync(ScanTriggerSource triggerSource, CancellationToken cancellationToken = default)
    {
        var observedImages = await _dbContext.ObservedImages.Where(entity => entity.IsEnabled)
                                                            .Select(entity => entity.Id)
                                                            .ToListAsync(cancellationToken)
                                                            .ConfigureAwait(false);

        if (observedImages.Count == 0)
        {
            _logger.ObservedImageScanBatchSkipped(triggerSource);

            return;
        }

        _logger.ObservedImageScanBatchStarted(triggerSource, observedImages.Count);

        foreach (var observedImageId in observedImages)
        {
            await ScanAsync(observedImageId,
                            triggerSource,
                            cancellationToken).ConfigureAwait(false);
        }

        _logger.ObservedImageScanBatchCompleted(triggerSource, observedImages.Count);
    }

    /// <inheritdoc/>
    public async Task ScanAsync(Guid observedImageId,
                                ScanTriggerSource triggerSource,
                                CancellationToken cancellationToken = default)
    {
        var resolvedBaseImageCount = 0;
        var observedImage = await _dbContext.ObservedImages.Include(entity => entity.CurrentImageVersion)
                                                           .ThenInclude(entity => entity.RegistryRepository)
                                                           .SingleAsync(entity => entity.Id == observedImageId, cancellationToken)
                                                           .ConfigureAwait(false);
        var scanRun = new ScanRun
                      {
                          Type = ScanRunType.ObservedImage,
                          Status = ScanRunStatus.Running,
                          TriggerSource = triggerSource,
                          ObservedImageId = observedImage.Id,
                          StartedAtUtc = DateTimeOffset.UtcNow,
                      };

        var stopwatch = Stopwatch.StartNew();
        var statusMessages = new List<string>();
        var finalStatus = ScanRunStatus.Succeeded;

        _logger.ObservedImageScanStarted(observedImage.Name, triggerSource);

        _dbContext.ScanRuns.Add(scanRun);

        await _dbContext.SaveChangesAsync(cancellationToken)
                        .ConfigureAwait(false);

        await DeactivateObservedFindingsAsync(observedImage.Id, cancellationToken).ConfigureAwait(false);

        try
        {
            var imageReference = _imageReferenceParser.Parse(_imageReferenceParser.Format(observedImage.CurrentImageVersion));
            var tagResult = await _registryMetadataService.GetTagAsync(imageReference, cancellationToken)
                                                          .ConfigureAwait(false);

            if (tagResult.Status == ExternalOperationStatus.Succeeded && tagResult.Data is not null)
            {
                observedImage.CurrentImageVersion.MetadataJson = JsonSerializer.Serialize(tagResult.Data);
                observedImage.CurrentImageVersion.PublishedAtUtc = tagResult.Data.PublishedAtUtc;
                observedImage.CurrentImageVersion.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }
            else
            {
                finalStatus = tagResult.Status == ExternalOperationStatus.Failed ? ScanRunStatus.Failed : ScanRunStatus.Partial;

                statusMessages.Add(tagResult.Message ?? "Unable to refresh registry tag metadata");

                _logger.ObservedImageMetadataRefreshIncomplete(observedImage.Name,
                                                               tagResult.Status,
                                                               tagResult.Message);
            }

            var baseImagesResult = await _baseImageResolver.ResolveAsync(imageReference, cancellationToken)
                                                           .ConfigureAwait(false);

            if (baseImagesResult.Status == ExternalOperationStatus.Succeeded && baseImagesResult.Data is not null)
            {
                resolvedBaseImageCount = baseImagesResult.Data.Count;

                var existingRelationships = await _dbContext.ImageRelationships.Where(entity => entity.ChildImageVersionId == observedImage.CurrentImageVersionId
                                                                                                && entity.RelationshipType == ImageRelationshipType.BaseImage)
                                                                               .ToListAsync(cancellationToken)
                                                                               .ConfigureAwait(false);

                _dbContext.ImageRelationships.RemoveRange(existingRelationships);

                foreach (var baseImage in baseImagesResult.Data)
                {
                    var baseImageReference = $"{baseImage.Registry}/{baseImage.Repository}:{baseImage.Tag}";

                    var baseImageVersion = await _imageCatalogRepository.GetOrCreateImageVersionAsync(baseImage.Registry,
                                                                                                      baseImage.Repository,
                                                                                                      baseImage.Tag,
                                                                                                      baseImage.Digest,
                                                                                                      cancellationToken: cancellationToken)
                                                                        .ConfigureAwait(false);

                    baseImageVersion.Source = ImageVersionSource.BaseImageResolution;

                    _dbContext.ImageRelationships.Add(new ImageRelationship
                                                      {
                                                          ChildImageVersionId = observedImage.CurrentImageVersionId,
                                                          BaseImageVersionId = baseImageVersion.Id,
                                                          ScanRunId = scanRun.Id,
                                                          RelationshipType = ImageRelationshipType.BaseImage,
                                                          Depth = baseImage.Depth,
                                                          SourceReference = baseImage.SourceReference,
                                                      });

                    var baseTagResult = await _registryMetadataService.GetTagsAsync(baseImage.Registry,
                                                                                    baseImage.Repository,
                                                                                    cancellationToken)
                                                                      .ConfigureAwait(false);

                    if (baseTagResult.Status == ExternalOperationStatus.Succeeded && baseTagResult.Data is not null)
                    {
                        var evaluation = _updateDetectionService.Evaluate(new ImageReference
                                                                          {
                                                                              Registry = baseImage.Registry,
                                                                              Repository = baseImage.Repository,
                                                                              Tag = baseImage.Tag,
                                                                              Digest = baseImage.Digest,
                                                                          },
                                                                          baseTagResult.Data);

                        if (evaluation.Status == UpdateEvaluationStatus.UpdateAvailable
                            || evaluation.Status == UpdateEvaluationStatus.NeedsReview)
                        {
                            var findingType = evaluation.Status == UpdateEvaluationStatus.UpdateAvailable
                                                  ? UpdateFindingType.BaseImageUpdate
                                                  : UpdateFindingType.TagRecommendation;

                            await CreateObservedFindingAsync(scanRun,
                                                             observedImage,
                                                             baseImageVersion,
                                                             findingType,
                                                             evaluation,
                                                             cancellationToken).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        finalStatus = ScanRunStatus.Partial;

                        statusMessages.Add(baseTagResult.Message ?? $"Unable to evaluate base image '{baseImage.Repository}:{baseImage.Tag}'");

                        _logger.ObservedImageBaseImageEvaluationIncomplete(observedImage.Name,
                                                                           baseImageReference,
                                                                           baseTagResult.Status,
                                                                           baseTagResult.Message);
                    }
                }

                if (baseImagesResult.Data.Count == 0)
                {
                    finalStatus = ScanRunStatus.Partial;

                    statusMessages.Add("Base image resolution returned no results for the observed image");

                    _logger.ObservedImageBaseImagesMissing(observedImage.Name);
                }
            }
            else
            {
                if (finalStatus != ScanRunStatus.Failed)
                {
                    finalStatus = baseImagesResult.Status == ExternalOperationStatus.Failed ? ScanRunStatus.Failed : ScanRunStatus.Partial;
                }

                statusMessages.Add(baseImagesResult.Message ?? "Base image resolution is unavailable");

                _logger.ObservedImageBaseImageResolutionIncomplete(observedImage.Name,
                                                                   baseImagesResult.Status,
                                                                   baseImagesResult.Message);
            }
        }
        catch (Exception exception)
        {
            finalStatus = ScanRunStatus.Failed;

            statusMessages.Add(exception.Message);

            _logger.ObservedImageScanFailed(exception, observedImage.Name);
        }

        scanRun.Status = finalStatus;
        scanRun.CompletedAtUtc = DateTimeOffset.UtcNow;
        scanRun.ErrorMessage = statusMessages.Count == 0 ? null : string.Join(Environment.NewLine, statusMessages.Distinct());

        await _dbContext.SaveChangesAsync(cancellationToken)
                        .ConfigureAwait(false);

        await _applicationTelemetry.RefreshInventoryMetricsAsync(_dbContext, cancellationToken)
                                   .ConfigureAwait(false);

        _applicationTelemetry.RecordScanRun(ScanRunType.ObservedImage,
                                            finalStatus,
                                            stopwatch.Elapsed);

        _logger.ObservedImageScanCompleted(observedImage.Name,
                                           finalStatus,
                                           resolvedBaseImageCount,
                                           statusMessages.Count,
                                           stopwatch.ElapsedMilliseconds);
    }

    /// <summary>
    /// Resolve existing active observed image findings
    /// </summary>
    /// <param name="observedImageId">Observed image identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    private async Task DeactivateObservedFindingsAsync(Guid observedImageId, CancellationToken cancellationToken)
    {
        var activeFindings = await _dbContext.UpdateFindings.Where(entity => entity.ObservedImageId == observedImageId && entity.IsActive)
                                                            .ToListAsync(cancellationToken)
                                                            .ConfigureAwait(false);

        foreach (var activeFinding in activeFindings)
        {
            activeFinding.IsActive = false;
            activeFinding.ResolvedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Create a persisted observed image finding
    /// </summary>
    /// <param name="scanRun">Scan run</param>
    /// <param name="observedImage">Observed image</param>
    /// <param name="subjectImageVersion">Subject image version</param>
    /// <param name="findingType">Finding type</param>
    /// <param name="evaluation">Update evaluation</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    private async Task CreateObservedFindingAsync(ScanRun scanRun,
                                                  ObservedImage observedImage,
                                                  ImageVersion subjectImageVersion,
                                                  UpdateFindingType findingType,
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
                          ObservedImageId = observedImage.Id,
                          SubjectImageVersionId = subjectImageVersion.Id,
                          RecommendedImageVersionId = recommendedImageVersionId,
                          Type = findingType,
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
                                          Reason = evaluation.Summary
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