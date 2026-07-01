using System.Diagnostics;
using System.Text.Json;

using DockerUpdateGuard.Data;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Data.Repositories;
using DockerUpdateGuard.Docker;
using DockerUpdateGuard.DockerHub;
using DockerUpdateGuard.Images.Data;
using DockerUpdateGuard.Images.Enums;
using DockerUpdateGuard.Images.Helper;
using DockerUpdateGuard.Images.Interfaces;
using DockerUpdateGuard.Infrastructure;

using Microsoft.EntityFrameworkCore;

namespace DockerUpdateGuard.Images;

/// <summary>
/// Observed image scan orchestrator
/// </summary>
public class ImageScanOrchestrator : IImageScanOrchestrator
{
    #region Const fields

    /// <summary>
    /// Maximum number of tags to inspect while resolving an exact base-image tag
    /// </summary>
    private const int MaxBaseImageTagScanCount = 150;

    #endregion // Const fields

    #region Fields

    /// <summary>
    /// Synchronization root guarding per-observed-image scan lock bookkeeping
    /// </summary>
    private static readonly object _observedImageScanLockRegistrySync = new();

    /// <summary>
    /// Per-observed-image scan locks shared across orchestrator scopes, retained only while referenced
    /// </summary>
    private static readonly Dictionary<Guid, ObservedImageScanLockEntry> _observedImageScanLocks = new();

    /// <summary>
    /// Application telemetry
    /// </summary>
    private readonly ApplicationTelemetry _applicationTelemetry;

    /// <summary>
    /// Base-image resolver
    /// </summary>
    private readonly IBaseImageResolver _baseImageResolver;

    /// <summary>
    /// Database context
    /// </summary>
    private readonly DockerUpdateGuardDbContext _dbContext;

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
    /// Logger
    /// </summary>
    private readonly ILogger<ImageScanOrchestrator> _logger;

    /// <summary>
    /// Registry-metadata service
    /// </summary>
    private readonly IRegistryMetadataService _registryMetadataService;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="applicationTelemetry">Application telemetry</param>
    /// <param name="baseImageResolver">Base image resolver</param>
    /// <param name="dbContext">Database context</param>
    /// <param name="derivedBaseRuntimeDetector">Derived base-runtime detector</param>
    /// <param name="dotNetReleaseMetadataService">.NET release metadata service</param>
    /// <param name="nginxReleaseMetadataService">NGINX release metadata service</param>
    /// <param name="imageCatalogRepository">Image catalog repository</param>
    /// <param name="imageReferenceParser">Image reference parser</param>
    /// <param name="logger">Logger</param>
    /// <param name="registryMetadataService">Registry metadata service</param>
    public ImageScanOrchestrator(ApplicationTelemetry applicationTelemetry,
                                 IBaseImageResolver baseImageResolver,
                                 DockerUpdateGuardDbContext dbContext,
                                 IDerivedBaseRuntimeDetector derivedBaseRuntimeDetector,
                                 IDotNetReleaseMetadataService dotNetReleaseMetadataService,
                                 INginxReleaseMetadataService nginxReleaseMetadataService,
                                 IImageCatalogRepository imageCatalogRepository,
                                 IImageReferenceParser imageReferenceParser,
                                 ILogger<ImageScanOrchestrator> logger,
                                 IRegistryMetadataService registryMetadataService)
    {
        _applicationTelemetry = applicationTelemetry;
        _baseImageResolver = baseImageResolver;
        _dbContext = dbContext;
        _derivedBaseRuntimeDetector = derivedBaseRuntimeDetector;
        _dotNetReleaseMetadataService = dotNetReleaseMetadataService;
        _nginxReleaseMetadataService = nginxReleaseMetadataService;
        _imageCatalogRepository = imageCatalogRepository;
        _imageReferenceParser = imageReferenceParser;
        _logger = logger;
        _registryMetadataService = registryMetadataService;
    }

    #endregion // Constructors

    #region Methods

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
    /// Acquire a reference to the shared scan lock for an observed image, creating it on first use
    /// </summary>
    /// <param name="observedImageId">Observed image identifier</param>
    /// <returns>Shared scan lock</returns>
    private static SemaphoreSlim AcquireObservedImageScanLock(Guid observedImageId)
    {
        lock (_observedImageScanLockRegistrySync)
        {
            if (_observedImageScanLocks.TryGetValue(observedImageId, out var entry) == false)
            {
                entry = new ObservedImageScanLockEntry
                        {
                            Semaphore = new SemaphoreSlim(1, 1),
                        };

                _observedImageScanLocks[observedImageId] = entry;
            }

            entry.ReferenceCount++;

            return entry.Semaphore;
        }
    }

    /// <summary>
    /// Release a reference to the shared scan lock for an observed image, disposing it once unreferenced
    /// </summary>
    /// <param name="observedImageId">Observed image identifier</param>
    /// <param name="observedImageScanLock">Scan lock previously obtained via <see cref="AcquireObservedImageScanLock"/></param>
    private static void ReleaseObservedImageScanLockReference(Guid observedImageId, SemaphoreSlim observedImageScanLock)
    {
        lock (_observedImageScanLockRegistrySync)
        {
            if (_observedImageScanLocks.TryGetValue(observedImageId, out var entry) == false
                || ReferenceEquals(entry.Semaphore, observedImageScanLock) == false)
            {
                return;
            }

            entry.ReferenceCount--;

            if (entry.ReferenceCount <= 0)
            {
                _observedImageScanLocks.Remove(observedImageId);
                entry.Semaphore.Dispose();
            }
        }
    }

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
            try
            {
                await ScanAsync(observedImageId,
                                triggerSource,
                                cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.ObservedImageScanBatchItemFailed(exception, observedImageId);
            }
        }

        _logger.ObservedImageScanBatchCompleted(triggerSource, observedImages.Count);
    }

    /// <inheritdoc/>
    public async Task ScanAsync(Guid observedImageId,
                                ScanTriggerSource triggerSource,
                                CancellationToken cancellationToken = default)
    {
        var resolvedBaseImageCount = 0;
        var observedImageScanLock = AcquireObservedImageScanLock(observedImageId);
        var observedImageScanLockAcquired = false;

        try
        {
            await observedImageScanLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            observedImageScanLockAcquired = true;

            var observedImage = await _dbContext.ObservedImages.Include(entity => entity.CurrentImageVersion)
                                                               .ThenInclude(entity => entity.RegistryRepository)
                                                               .SingleOrDefaultAsync(entity => entity.Id == observedImageId, cancellationToken)
                                                               .ConfigureAwait(false);

            if (observedImage is null)
            {
                _logger.ObservedImageScanSkippedMissing(observedImageId);

                return;
            }

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

                    await _dbContext.ImageRelationships.Where(entity => entity.ChildImageVersionId == observedImage.CurrentImageVersionId
                                                                        && entity.RelationshipType == ImageRelationshipType.BaseImage)
                                                       .ExecuteDeleteAsync(cancellationToken)
                                                       .ConfigureAwait(false);

                    foreach (var baseImage in baseImagesResult.Data)
                    {
                        var baseImageReference = new ImageReference
                                                 {
                                                     Registry = baseImage.Registry,
                                                     Repository = baseImage.Repository,
                                                     Tag = baseImage.Tag,
                                                     Digest = baseImage.Digest,
                                                 };

                        var baseTagResult = await _registryMetadataService.GetTagAsync(baseImageReference,
                                                                                       cancellationToken)
                                                                          .ConfigureAwait(false);
                        var storedBaseImageTag = await ResolveStoredBaseImageTagAsync(baseImageReference,
                                                                                      baseTagResult.Data,
                                                                                      cancellationToken).ConfigureAwait(false);

                        var baseImageVersion = await _imageCatalogRepository.GetOrCreateImageVersionAsync(baseImage.Registry,
                                                                                                          baseImage.Repository,
                                                                                                          storedBaseImageTag,
                                                                                                          baseImage.Digest,
                                                                                                          cancellationToken: cancellationToken)
                                                                            .ConfigureAwait(false);

                        baseImageVersion.Source = ImageVersionSource.BaseImageResolution;

                        if (baseTagResult.Status == ExternalOperationStatus.Succeeded && baseTagResult.Data is not null)
                        {
                            baseImageVersion.MetadataJson = JsonSerializer.Serialize(baseTagResult.Data);
                            baseImageVersion.PublishedAtUtc = baseTagResult.Data.PublishedAtUtc;
                            baseImageVersion.UpdatedAtUtc = DateTimeOffset.UtcNow;
                        }
                        else
                        {
                            finalStatus = ScanRunStatus.Partial;

                            statusMessages.Add(baseTagResult.Message ?? $"Unable to refresh exact base image '{baseImageReference.FullReference}'");

                            _logger.ObservedImageBaseImageMetadataRefreshIncomplete(observedImage.Name,
                                                                                    baseImageReference.FullReference,
                                                                                    baseTagResult.Status,
                                                                                    baseTagResult.Message);
                        }

                        _dbContext.ImageRelationships.Add(new ImageRelationship
                                                          {
                                                              ChildImageVersionId = observedImage.CurrentImageVersionId,
                                                              BaseImageVersionId = baseImageVersion.Id,
                                                              ScanRunId = scanRun.Id,
                                                              RelationshipType = ImageRelationshipType.BaseImage,
                                                              Depth = baseImage.Depth,
                                                              SourceReference = baseImage.SourceReference,
                                                          });
                    }

                    if (baseImagesResult.Data.Count == 0)
                    {
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

                await CreateDerivedBaseRuntimeFindingAsync(scanRun,
                                                           observedImage,
                                                           imageReference,
                                                           observedImage.CurrentImageVersion,
                                                           cancellationToken).ConfigureAwait(false);
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
        finally
        {
            if (observedImageScanLockAcquired)
            {
                observedImageScanLock.Release();
            }

            ReleaseObservedImageScanLockReference(observedImageId, observedImageScanLock);
        }
    }

    /// <summary>
    /// Resolve the stored base-image tag for display and persistence
    /// </summary>
    /// <param name="baseImageReference">Base image reference</param>
    /// <param name="currentTagData">Current base-image tag metadata</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Resolved tag value</returns>
    private async Task<string> ResolveStoredBaseImageTagAsync(ImageReference baseImageReference,
                                                              DockerHubTagData? currentTagData,
                                                              CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(baseImageReference);

        if (VersionTagResolutionHelper.IsDisplayableSpecificVersionTag(baseImageReference.Tag)
            || string.IsNullOrWhiteSpace(baseImageReference.Digest))
        {
            return baseImageReference.Tag;
        }

        var queryOptions = new RegistryTagQueryOptions
                           {
                               CurrentDigest = baseImageReference.Digest,
                               CurrentTag = baseImageReference.Tag,
                               MaximumTags = MaxBaseImageTagScanCount,
                               VersionLineTag = string.Equals(baseImageReference.Tag,
                                                              "latest",
                                                              StringComparison.OrdinalIgnoreCase)
                                                    ? null
                                                    : baseImageReference.Tag,
                               PublishedSinceUtc = currentTagData?.PublishedAtUtc,
                           };
        var baseTagsResult = await _registryMetadataService.GetTagsAsync(baseImageReference.Registry,
                                                                         baseImageReference.Repository,
                                                                         cancellationToken,
                                                                         queryOptions: queryOptions)
                                                           .ConfigureAwait(false);

        if (baseTagsResult.Status != ExternalOperationStatus.Succeeded || baseTagsResult.Data is null)
        {
            return baseImageReference.Tag;
        }

        var resolvedTag = VersionTagResolutionHelper.ResolveDisplayVersionTag(baseImageReference.Tag,
                                                                              baseImageReference.Digest,
                                                                              baseTagsResult.Data.Select(entity => new VersionTagCandidateData
                                                                                                                   {
                                                                                                                       Tag = entity.Tag,
                                                                                                                       Digest = entity.Digest,
                                                                                                                       PublishedAtUtc = entity.PublishedAtUtc,
                                                                                                                   }));

        return string.IsNullOrWhiteSpace(resolvedTag)
                   ? baseImageReference.Tag
                   : resolvedTag;
    }

    /// <summary>
    /// Delete existing active observed-image update findings superseded by a new scan
    /// </summary>
    /// <param name="observedImageId">Observed image identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    private async Task DeleteSupersededObservedFindingsAsync(Guid observedImageId, CancellationToken cancellationToken)
    {
        var supersededFindings = await _dbContext.UpdateFindings.Where(entity => entity.ObservedImageId == observedImageId && entity.IsActive)
                                                                .ToListAsync(cancellationToken)
                                                                .ConfigureAwait(false);

        _dbContext.UpdateFindings.RemoveRange(supersededFindings);
    }

    /// <summary>
    /// Create a derived base-runtime finding for an observed image when its .NET channel is behind
    /// </summary>
    /// <param name="scanRun">Scan run</param>
    /// <param name="observedImage">Observed image</param>
    /// <param name="imageReference">Image reference</param>
    /// <param name="subjectImageVersion">Subject image version</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    private async Task CreateDerivedBaseRuntimeFindingAsync(ScanRun scanRun,
                                                            ObservedImage observedImage,
                                                            ImageReference imageReference,
                                                            ImageVersion subjectImageVersion,
                                                            CancellationToken cancellationToken)
    {
        var imageConfigurationResult = await _registryMetadataService.GetImageConfigurationAsync(imageReference, cancellationToken)
                                                                     .ConfigureAwait(false);

        if (imageConfigurationResult.Status != ExternalOperationStatus.Succeeded || imageConfigurationResult.Data is null)
        {
            return;
        }

        var runtimeDescriptor = _derivedBaseRuntimeDetector.Detect(new DockerImageInspectData
                                                                   {
                                                                       EnvironmentVariables = imageConfigurationResult.Data.EnvironmentVariables,
                                                                       CreatedAtUtc = imageConfigurationResult.Data.CreatedAtUtc,
                                                                       OperatingSystem = imageConfigurationResult.Data.OperatingSystem,
                                                                       Architecture = imageConfigurationResult.Data.Architecture,
                                                                   },
                                                                   []);

        if (runtimeDescriptor?.RuntimeVersion is null
            || string.IsNullOrWhiteSpace(runtimeDescriptor.ChannelVersion))
        {
            await DeleteSupersededObservedFindingsAsync(observedImage.Id, cancellationToken).ConfigureAwait(false);

            return;
        }

        switch (runtimeDescriptor.Kind)
        {
            case DerivedBaseRuntimeKind.DotNet:
                {
                    var channelReleaseResult = await _dotNetReleaseMetadataService.GetChannelReleaseAsync(runtimeDescriptor.ChannelVersion, cancellationToken)
                                                                                  .ConfigureAwait(false);

                    if (channelReleaseResult.Status != ExternalOperationStatus.Succeeded || channelReleaseResult.Data is null)
                    {
                        return;
                    }

                    await DeleteSupersededObservedFindingsAsync(observedImage.Id, cancellationToken).ConfigureAwait(false);

                    if (IsPatchBehind(runtimeDescriptor.RuntimeVersion, channelReleaseResult.Data.LatestRuntimeVersion) == false)
                    {
                        return;
                    }

                    _dbContext.UpdateFindings.Add(new UpdateFinding
                                                  {
                                                      ScanRunId = scanRun.Id,
                                                      ObservedImageId = observedImage.Id,
                                                      SubjectImageVersionId = subjectImageVersion.Id,
                                                      Type = UpdateFindingType.DerivedBaseRuntimeUpdate,
                                                      Summary = observedImage.Source == RegistrationSource.Discovery
                                                                    ? "Own image uses an outdated .NET base runtime"
                                                                    : "Observed image uses an outdated .NET base runtime",
                                                      Details = BuildDotNetDerivedBaseRuntimeDetails(observedImage,
                                                                                                     runtimeDescriptor,
                                                                                                     channelReleaseResult.Data),
                                                  });
                }
                break;

            case DerivedBaseRuntimeKind.Nginx:
                {
                    var channelReleaseResult = await _nginxReleaseMetadataService.GetChannelReleaseAsync(runtimeDescriptor.ChannelVersion, cancellationToken)
                                                                                 .ConfigureAwait(false);

                    if (channelReleaseResult.Status != ExternalOperationStatus.Succeeded || channelReleaseResult.Data is null)
                    {
                        return;
                    }

                    await DeleteSupersededObservedFindingsAsync(observedImage.Id, cancellationToken).ConfigureAwait(false);

                    if (IsPatchBehind(runtimeDescriptor.RuntimeVersion, channelReleaseResult.Data.LatestVersion) == false)
                    {
                        return;
                    }

                    _dbContext.UpdateFindings.Add(new UpdateFinding
                                                  {
                                                      ScanRunId = scanRun.Id,
                                                      ObservedImageId = observedImage.Id,
                                                      SubjectImageVersionId = subjectImageVersion.Id,
                                                      Type = UpdateFindingType.DerivedBaseRuntimeUpdate,
                                                      Summary = observedImage.Source == RegistrationSource.Discovery
                                                                    ? "Own image uses an outdated NGINX base runtime"
                                                                    : "Observed image uses an outdated NGINX base runtime",
                                                      Details = BuildNginxDerivedBaseRuntimeDetails(observedImage,
                                                                                                    runtimeDescriptor,
                                                                                                    channelReleaseResult.Data),
                                                  });
                }
                break;
        }
    }

    /// <summary>
    /// Build derived base-runtime finding details for an observed image
    /// </summary>
    /// <param name="observedImage">Observed image</param>
    /// <param name="runtimeDescriptor">Runtime descriptor</param>
    /// <param name="channelRelease">Channel release metadata</param>
    /// <returns>Details text</returns>
    private string BuildDotNetDerivedBaseRuntimeDetails(ObservedImage observedImage,
                                                        DerivedBaseRuntimeDescriptor runtimeDescriptor,
                                                        DotNetChannelReleaseData channelRelease)
    {
        ArgumentNullException.ThrowIfNull(runtimeDescriptor.RuntimeVersion);

        var message = $"Observed image '{observedImage.Name}' appears to use .NET {FormatVersion(runtimeDescriptor.RuntimeVersion)}. Latest .NET {channelRelease.ChannelVersion} runtime is {FormatVersion(channelRelease.LatestRuntimeVersion)}.";

        if (channelRelease.IsSecurityRelease)
        {
            message = $"{message} The latest channel release includes security fixes.";
        }

        return observedImage.Source == RegistrationSource.Discovery
                   ? $"{message} Rebuild and republish the image so new deployments use the updated .NET base runtime."
                   : $"{message} The upstream image publisher must rebuild the image before the newer .NET base runtime can be consumed.";
    }

    /// <summary>
    /// Build NGINX derived base-runtime finding details for an observed image
    /// </summary>
    /// <param name="observedImage">Observed image</param>
    /// <param name="runtimeDescriptor">Runtime descriptor</param>
    /// <param name="channelRelease">Channel release metadata</param>
    /// <returns>Details text</returns>
    private string BuildNginxDerivedBaseRuntimeDetails(ObservedImage observedImage,
                                                       DerivedBaseRuntimeDescriptor runtimeDescriptor,
                                                       NginxChannelReleaseData channelRelease)
    {
        ArgumentNullException.ThrowIfNull(runtimeDescriptor.RuntimeVersion);

        var message = $"Observed image '{observedImage.Name}' appears to use NGINX {FormatVersion(runtimeDescriptor.RuntimeVersion)}. Latest NGINX {channelRelease.ChannelVersion} release is {FormatVersion(channelRelease.LatestVersion)}.";

        return observedImage.Source == RegistrationSource.Discovery
                   ? $"{message} Rebuild and republish the image so new deployments use the updated NGINX base runtime."
                   : $"{message} The upstream image publisher must rebuild the image before the newer NGINX base runtime can be consumed.";
    }

    #endregion // Methods
}