using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Data;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Data.Queries;
using DockerUpdateGuard.Images.Data;
using DockerUpdateGuard.Images.Helper;
using DockerUpdateGuard.Images.Interfaces;
using DockerUpdateGuard.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DockerUpdateGuard.UI;

/// <summary>
/// Default UI query service
/// </summary>
public sealed class ApplicationViewService : IApplicationViewService, IDisposable
{
    #region Fields

    /// <summary>
    /// Maximum number of resource samples shown in runtime list sparklines
    /// </summary>
    private const int RuntimeListResourceHistoryCount = 12;

    /// <summary>
    /// Maximum number of container action runs listed in the history of a runtime container
    /// </summary>
    private const int ContainerActionHistoryLimit = 20;

    /// <summary>
    /// Minimum number of users (enabled observed images plus current containers) that makes a base image shared
    /// </summary>
    private const int SharedBaseImageMinimumUsageCount = 2;

    /// <summary>
    /// Resource-history window
    /// </summary>
    private static readonly TimeSpan _resourceHistoryWindow = TimeSpan.FromHours(24);

    /// <summary>
    /// Database-access gate
    /// </summary>
    private readonly SemaphoreSlim _dbContextLock = new(1, 1);

    /// <summary>
    /// Database context
    /// </summary>
    private readonly DockerUpdateGuardDbContext _dbContext;

    /// <summary>
    /// Image-reference parser
    /// </summary>
    private readonly IImageReferenceParser _imageReferenceParser;

    /// <summary>
    /// Application options monitor
    /// </summary>
    private readonly IOptionsMonitor<DockerUpdateGuardOptions> _optionsMonitor;

    /// <summary>
    /// Shared-base-image query service
    /// </summary>
    private readonly ISharedBaseImageQueryService _sharedBaseImageQueryService;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="dbContext">Database context</param>
    /// <param name="imageReferenceParser">Image reference parser</param>
    /// <param name="optionsMonitor">Application options monitor</param>
    /// <param name="sharedBaseImageQueryService">Shared base image query service</param>
    public ApplicationViewService(DockerUpdateGuardDbContext dbContext,
                                  IImageReferenceParser imageReferenceParser,
                                  IOptionsMonitor<DockerUpdateGuardOptions> optionsMonitor,
                                  ISharedBaseImageQueryService sharedBaseImageQueryService)
    {
        _dbContext = dbContext;
        _imageReferenceParser = imageReferenceParser;
        _optionsMonitor = optionsMonitor;
        _sharedBaseImageQueryService = sharedBaseImageQueryService;
    }

    #endregion // Constructors

    #region Static methods

    /// <summary>
    /// Map a Docker-instance resource sample
    /// </summary>
    /// <param name="entity">Docker-instance resource sample</param>
    /// <returns>Resource usage point</returns>
    private static ResourceUsagePointViewData MapResourceUsage(DockerInstanceResourceSample entity)
    {
        return new ResourceUsagePointViewData
               {
                   CpuPercent = entity.CpuPercent,
                   MemoryUsageBytes = entity.MemoryUsageBytes,
                   MemoryLimitBytes = entity.MemoryLimitBytes,
                   NetworkRxBytesPerSecond = entity.NetworkRxBytesPerSecond,
                   NetworkTxBytesPerSecond = entity.NetworkTxBytesPerSecond,
                   RecordedAtUtc = entity.RecordedAtUtc,
               };
    }

    /// <summary>
    /// Map a runtime-container resource sample
    /// </summary>
    /// <param name="entity">Runtime-container resource sample</param>
    /// <returns>Resource usage point</returns>
    private static ResourceUsagePointViewData MapResourceUsage(RuntimeContainerResourceSample entity)
    {
        return new ResourceUsagePointViewData
               {
                   CpuPercent = entity.CpuPercent,
                   MemoryUsageBytes = entity.MemoryUsageBytes,
                   MemoryLimitBytes = entity.MemoryLimitBytes,
                   NetworkRxBytesPerSecond = entity.NetworkRxBytesPerSecond,
                   NetworkTxBytesPerSecond = entity.NetworkTxBytesPerSecond,
                   RecordedAtUtc = entity.RecordedAtUtc,
               };
    }

    /// <summary>
    /// Format an image reference from its parts
    /// </summary>
    /// <param name="registry">Registry host</param>
    /// <param name="repository">Repository path</param>
    /// <param name="tag">Tag name</param>
    /// <param name="digest">Optional digest</param>
    /// <returns>Formatted image reference</returns>
    private static string FormatImageReference(string registry, string repository, string tag, string? digest)
    {
        return string.IsNullOrWhiteSpace(digest)
                   ? $"{registry}/{repository}:{tag}"
                   : $"{registry}/{repository}:{tag}@{digest}";
    }

    /// <summary>
    /// Create a normalized repository key
    /// </summary>
    /// <param name="entity">Container snapshot entity</param>
    /// <returns>Repository key</returns>
    private static string CreateRepositoryKey(ContainerSnapshot entity)
    {
        return CreateRepositoryKey(entity.ImageVersion);
    }

    /// <summary>
    /// Create a normalized repository key
    /// </summary>
    /// <param name="entity">Image version entity</param>
    /// <returns>Repository key</returns>
    private static string CreateRepositoryKey(ImageVersion entity)
    {
        return CreateRepositoryKey(entity.RegistryRepository.Registry, entity.RegistryRepository.Repository);
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
    /// Create a normalized container key
    /// </summary>
    /// <param name="dockerInstanceId">ID of the Docker instance</param>
    /// <param name="containerId">ID of the container</param>
    /// <returns>Container key</returns>
    private static string CreateContainerKey(Guid dockerInstanceId, string containerId)
    {
        return $"{dockerInstanceId:D}|{containerId.Trim()}";
    }

    /// <summary>
    /// Return the most recent resource usage point or null when the history is empty
    /// </summary>
    /// <param name="history">Ordered resource usage history</param>
    /// <returns>Most recent resource usage point or null</returns>
    private static ResourceUsagePointViewData? GetCurrentResourceUsage(IReadOnlyList<ResourceUsagePointViewData>? history)
    {
        return history is { Count: > 0 } ? history[0] : null;
    }

    /// <summary>
    /// Return the count stored for a key or zero when the key is absent
    /// </summary>
    /// <param name="counts">Count lookup keyed by identifier</param>
    /// <param name="key">Identifier to resolve</param>
    /// <returns>Stored count or zero</returns>
    private static int GetCountOrZero(Dictionary<Guid, int> counts, Guid key)
    {
        return counts.TryGetValue(key, out var count) ? count : 0;
    }

    /// <summary>
    /// Format an update assessment status for the UI
    /// </summary>
    /// <param name="status">Update assessment status</param>
    /// <returns>Formatted status</returns>
    private static string FormatUpdateAssessmentStatus(UpdateAssessmentStatus status)
    {
        return status switch
               {
                   UpdateAssessmentStatus.UpToDate => "Up to date",
                   UpdateAssessmentStatus.UpdateAvailable => "Update available",
                   UpdateAssessmentStatus.ManualReviewRequired => "Manual review required",
                   UpdateAssessmentStatus.NoTagData => "No tag data",
                   UpdateAssessmentStatus.Unsupported => "Unsupported",
                   UpdateAssessmentStatus.Failed => "Failed",
                   _ => "Not evaluated",
               };
    }

    /// <summary>
    /// Format an update finding type for the UI
    /// </summary>
    /// <param name="type">Finding type</param>
    /// <returns>Formatted type</returns>
    private static string FormatUpdateFindingType(UpdateFindingType type)
    {
        return type switch
               {
                   UpdateFindingType.RuntimeImageUpdate => "Runtime image update",
                   UpdateFindingType.TagRecommendation => "Tag recommendation",
                   UpdateFindingType.DerivedBaseRuntimeUpdate => "Base runtime update",
                   _ => type.ToString(),
               };
    }

    /// <summary>
    /// Select the representative affected image for a set of image versions that share the same digest-less reference
    /// </summary>
    /// <param name="referenceGroup">Affected image versions resolving to the same registry, repository and tag</param>
    /// <returns>Representative affected image preferring a linkable, owned image</returns>
    private static VulnerabilityOverviewAffectedImageData SelectRepresentativeAffectedImage(IEnumerable<VulnerabilityOverviewAffectedImageData> referenceGroup)
    {
        return referenceGroup.OrderByDescending(entity => entity.IsOwnImage)
                             .ThenByDescending(entity => entity.ObservedImageId.HasValue)
                             .First();
    }

    /// <summary>
    /// Group the tag candidates of update findings by container snapshot
    /// </summary>
    /// <param name="latestFindings">Update findings of the current container snapshots</param>
    /// <returns>Tag candidate view data by container snapshot identifier</returns>
    private static Dictionary<Guid, List<TagCandidateViewData>> GroupTagCandidatesBySnapshot(IReadOnlyList<UpdateFinding> latestFindings)
    {
        return latestFindings.Where(entity => entity.ContainerSnapshotId is not null)
                             .GroupBy(entity => entity.ContainerSnapshotId!.Value)
                             .ToDictionary(group => group.Key,
                                           group => group.SelectMany(entity => entity.TagCandidates)
                                                         .OrderBy(candidate => candidate.Rank)
                                                         .Select(candidate => new TagCandidateViewData
                                                                              {
                                                                                  Tag = candidate.Tag,
                                                                                  Digest = candidate.Digest,
                                                                                  PublishedAtUtc = candidate.PublishedAtUtc,
                                                                                  Reason = candidate.Reason,
                                                                                  IsRecommended = candidate.IsRecommended,
                                                                              })
                                                         .ToList());
    }

    /// <summary>
    /// Create the lookup of the latest derived base runtime finding by repository key of the current containers
    /// </summary>
    /// <param name="derivedBaseRuntimeFindings">Derived base runtime findings</param>
    /// <param name="repositoryKeyBySnapshotId">Repository keys by container snapshot identifier</param>
    /// <returns>Latest finding by repository key</returns>
    private static Dictionary<string, UpdateFinding> CreateRuntimeAlertLookup(List<UpdateFinding> derivedBaseRuntimeFindings, Dictionary<Guid, string> repositoryKeyBySnapshotId)
    {
        return derivedBaseRuntimeFindings.Where(entity => entity.ContainerSnapshotId is not null
                                                          && repositoryKeyBySnapshotId.ContainsKey(entity.ContainerSnapshotId.Value))
                                         .GroupBy(entity => repositoryKeyBySnapshotId[entity.ContainerSnapshotId!.Value],
                                                  StringComparer.OrdinalIgnoreCase)
                                         .ToDictionary(group => group.Key,
                                                       group => group.OrderByDescending(entity => entity.DetectedAtUtc)
                                                                     .First(),
                                                       StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Create the lookup of the latest derived base runtime finding by observed image
    /// </summary>
    /// <param name="derivedBaseRuntimeFindings">Derived base runtime findings</param>
    /// <returns>Latest finding by observed image identifier</returns>
    private static Dictionary<Guid, UpdateFinding> CreateObservedAlertLookup(List<UpdateFinding> derivedBaseRuntimeFindings)
    {
        return derivedBaseRuntimeFindings.Where(entity => entity.ObservedImageId != null)
                                         .GroupBy(entity => entity.ObservedImageId!.Value)
                                         .ToDictionary(group => group.Key,
                                                       group => group.OrderByDescending(entity => entity.DetectedAtUtc)
                                                                     .First());
    }

    /// <summary>
    /// Select the current container snapshots that run an image of the same repository, ordered by instance and name
    /// </summary>
    /// <param name="latestSnapshots">Current container snapshots</param>
    /// <param name="imageVersion">Image version whose repository the containers must run</param>
    /// <returns>Linked container snapshots</returns>
    private static List<ContainerSnapshot> SelectLinkedSnapshots(IReadOnlyList<ContainerSnapshot> latestSnapshots, ImageVersion imageVersion)
    {
        return latestSnapshots.Where(entity => string.Equals(CreateRepositoryKey(entity),
                                                             CreateRepositoryKey(imageVersion),
                                                             StringComparison.OrdinalIgnoreCase))
                              .OrderBy(entity => entity.DockerInstance.Name)
                              .ThenBy(entity => entity.Name)
                              .ToList();
    }

    /// <summary>
    /// Select the newest active derived base runtime finding of an observed image, falling back to the runtime derived findings
    /// </summary>
    /// <param name="updateFindings">Active update findings of the observed image</param>
    /// <param name="runtimeDerivedFindings">Derived base runtime findings of the linked containers</param>
    /// <returns>Base runtime alert, or null when there is none</returns>
    private static UpdateFinding? SelectBaseRuntimeAlert(List<UpdateFinding> updateFindings, List<UpdateFinding> runtimeDerivedFindings)
    {
        return updateFindings.Where(entity => entity.IsActive
                                              && entity.Type == UpdateFindingType.DerivedBaseRuntimeUpdate)
                             .OrderByDescending(entity => entity.DetectedAtUtc)
                             .FirstOrDefault()
                   ?? runtimeDerivedFindings.FirstOrDefault();
    }

    /// <summary>
    /// Create the tag candidates that are available for the runtime image of a container snapshot
    /// </summary>
    /// <param name="mappedUpdateFindings">Mapped update findings of the container snapshot</param>
    /// <param name="latestSnapshot">Latest container snapshot</param>
    /// <returns>Available tag candidates</returns>
    private static List<TagCandidateViewData> CreateAvailableTagCandidates(List<UpdateFindingViewData> mappedUpdateFindings, ContainerSnapshot latestSnapshot)
    {
        return TagCandidateViewHelper.FilterRuntimeAvailableTagCandidates(TagCandidateViewHelper.FilterVisibleTagCandidates(mappedUpdateFindings.SelectMany(entity => entity.TagCandidates)
                                                                                                                                                .OrderByDescending(entity => entity.IsRecommended)
                                                                                                                                                .ThenBy(entity => entity.Tag, StringComparer.OrdinalIgnoreCase)),
                                                                          latestSnapshot.ImageVersion.Tag,
                                                                          latestSnapshot.ResolvedVersionTag,
                                                                          latestSnapshot.ImageVersion.PublishedAtUtc);
    }

    /// <summary>
    /// Apply the base image and vulnerability data of an image version to a runtime container detail
    /// </summary>
    /// <param name="detail">Runtime container detail</param>
    /// <param name="imageVersion">Image version of the container</param>
    /// <param name="baseImages">Base image relationships, if any</param>
    /// <param name="vulnerabilityFindings">Vulnerability findings of the image version, sorted for display</param>
    private static void ApplyImageVulnerabilityData(RuntimeContainerDetailViewData detail,
                                                    ImageVersion imageVersion,
                                                    IReadOnlyList<BaseImageRelationshipData>? baseImages,
                                                    List<VulnerabilityFinding> vulnerabilityFindings)
    {
        var baseImageVulnerabilitySummary = VulnerabilityViewMapper.SummarizeBaseImageVulnerabilities(baseImages);
        var vulnerabilityAssessmentDetail = VulnerabilityViewMapper.BuildVulnerabilityAssessmentDetail(imageVersion, vulnerabilityFindings);

        detail.BaseImages = baseImages ?? [];
        detail.ActiveBaseImageVulnerabilityFindingCount = baseImageVulnerabilitySummary.ActiveFindingCount;
        detail.BaseImageVulnerabilitySummary = baseImageVulnerabilitySummary.Summary;
        detail.VulnerabilityAssessment = vulnerabilityAssessmentDetail.Assessment;
        detail.VulnerabilityFindings = vulnerabilityAssessmentDetail.Findings;
    }

    #endregion // Static methods

    #region Methods

    /// <summary>
    /// Execute a serialized database read operation for the scoped UI service
    /// </summary>
    /// <typeparam name="T">Result type</typeparam>
    /// <param name="action">Read operation</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Operation result</returns>
    private async Task<T> ExecuteSerializedAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        await _dbContextLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await action().ConfigureAwait(false);
        }
        finally
        {
            _dbContextLock.Release();
        }
    }

    /// <summary>
    /// Read runtime containers without re-entering the service gate
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Runtime container list</returns>
    private async Task<IReadOnlyList<RuntimeContainerListItemData>> GetRuntimeContainersCoreAsync(CancellationToken cancellationToken)
    {
        var latestSnapshots = await GetLatestContainerSnapshotsAsync(cancellationToken).ConfigureAwait(false);
        var ownImagesByRepository = await LoadOwnImagesByRepositoryAsync(cancellationToken).ConfigureAwait(false);
        var resourceHistories = await GetRuntimeContainerResourceHistoriesAsync(latestSnapshots, cancellationToken).ConfigureAwait(false);
        var baseImageRelationshipsByChildVersion = await LoadBaseImageRelationshipsByChildVersionAsync(latestSnapshots.Select(entity => entity.ImageVersionId)
                                                                                                                      .Distinct()
                                                                                                                      .ToList(),
                                                                                                       cancellationToken).ConfigureAwait(false);
        var latestSnapshotIds = latestSnapshots.Select(entity => entity.Id)
                                               .ToList();

        var latestFindings = await LoadLatestUpdateFindingsAsync(latestSnapshotIds, cancellationToken).ConfigureAwait(false);
        var tagCandidatesBySnapshot = GroupTagCandidatesBySnapshot(latestFindings);

        var runtimeImageVersionIds = latestSnapshots.Select(entity => entity.ImageVersionId)
                                                    .Distinct()
                                                    .ToList();
        var activeVulnerabilityFindingLookup = await LoadActiveVulnerabilitySeveritySummariesAsync(runtimeImageVersionIds, cancellationToken).ConfigureAwait(false);

        return latestSnapshots.Select(entity => MapRuntimeContainerListItem(entity,
                                                                            ownImagesByRepository,
                                                                            resourceHistories,
                                                                            tagCandidatesBySnapshot,
                                                                            baseImageRelationshipsByChildVersion,
                                                                            activeVulnerabilityFindingLookup))
                              .OrderByDescending(entity => entity.LinkedObservedImageId.HasValue)
                              .ThenBy(entity => entity.DockerInstanceName)
                              .ThenBy(entity => entity.ContainerName)
                              .ToList();
    }

    /// <summary>
    /// Read the shared base images: entries used by at least two enabled observed images or current containers
    /// </summary>
    /// <param name="latestSnapshots">Current container snapshots</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Shared base image entries with the current containers running their consuming image versions</returns>
    private async Task<IReadOnlyList<(SharedBaseImageUsageData Entry, IReadOnlyList<ContainerSnapshot> Containers)>> GetSharedBaseImageEntriesAsync(IReadOnlyList<ContainerSnapshot> latestSnapshots,
                                                                                                                                                    CancellationToken cancellationToken)
    {
        var baseImages = await _sharedBaseImageQueryService.GetBaseImagesAsync(cancellationToken).ConfigureAwait(false);

        return baseImages.Select(entity =>
                                 {
                                     var consumingImageVersionIdSet = entity.ConsumingImageVersionIds.ToHashSet();
                                     IReadOnlyList<ContainerSnapshot> containers = latestSnapshots.Where(snapshot => consumingImageVersionIdSet.Contains(snapshot.ImageVersionId))
                                                                                                  .GroupBy(snapshot => CreateContainerKey(snapshot.DockerInstanceId, snapshot.ContainerId))
                                                                                                  .Select(group => group.First())
                                                                                                  .ToList();

                                     return (Entry: entity, Containers: containers);
                                 })
                         .Where(entity => entity.Entry.ObservedImageCount + entity.Containers.Count >= SharedBaseImageMinimumUsageCount)
                         .ToList();
    }

    /// <summary>
    /// Read base images without re-entering the service gate
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Base image list</returns>
    private async Task<IReadOnlyList<SharedBaseImageListItemData>> GetBaseImagesCoreAsync(CancellationToken cancellationToken)
    {
        var latestSnapshots = await GetLatestContainerSnapshotsAsync(cancellationToken).ConfigureAwait(false);
        var sharedEntries = await GetSharedBaseImageEntriesAsync(latestSnapshots, cancellationToken).ConfigureAwait(false);

        if (sharedEntries.Count == 0)
        {
            return [];
        }

        var groupedBaseImageVersionIds = sharedEntries.SelectMany(entity => entity.Entry.BaseImageVersionIds)
                                                      .Distinct()
                                                      .ToList();
        var imageVersionIds = sharedEntries.SelectMany(entity => entity.Entry.ConsumingImageVersionIds)
                                           .Concat(groupedBaseImageVersionIds)
                                           .Distinct()
                                           .ToList();
        var imageVersions = await _dbContext.ImageVersions.Include(entity => entity.RegistryRepository)
                                                          .Where(entity => imageVersionIds.Contains(entity.Id))
                                                          .AsNoTracking()
                                                          .ToDictionaryAsync(entity => entity.Id, cancellationToken)
                                                          .ConfigureAwait(false);
        var activeFindingLookup = await LoadActiveVulnerabilitySeveritySummariesAsync(groupedBaseImageVersionIds, cancellationToken).ConfigureAwait(false);

        return sharedEntries.Select(shared =>
                                    {
                                        var entity = shared.Entry;
                                        var relevantBaseImageVersions = entity.BaseImageVersionIds.Select(id => imageVersions[id])
                                                                                                  .ToList();
                                        var representativeBaseImage = relevantBaseImageVersions.OrderByDescending(baseImageVersion => baseImageVersion.VulnerabilityAssessmentCheckedAtUtc ?? DateTimeOffset.MinValue)
                                                                                               .ThenBy(baseImageVersion => baseImageVersion.Tag, StringComparer.OrdinalIgnoreCase)
                                                                                               .First();
                                        var worstSeveritySummary = relevantBaseImageVersions.Select(baseImageVersion => VulnerabilityViewMapper.GetSummaryOrEmpty(activeFindingLookup, baseImageVersion.Id))
                                                                                            .OrderByDescending(summary => summary.TotalCount)
                                                                                            .First();
                                        var runtimeContainers = shared.Containers.OrderBy(snapshot => snapshot.DockerInstance.Name, StringComparer.OrdinalIgnoreCase)
                                                                                 .ThenBy(snapshot => snapshot.Name, StringComparer.OrdinalIgnoreCase)
                                                                                 .Select(MapLinkedRuntimeContainer)
                                                                                 .ToList();

                                        return new SharedBaseImageListItemData
                                               {
                                                   BaseImageVersionId = entity.BaseImageVersionId,
                                                   BaseImageVersionIds = entity.BaseImageVersionIds,
                                                   ImageReference = FormatImageReference(entity.Registry, entity.Repository, entity.Tag, entity.Digest),
                                                   ObservedImageCount = entity.ObservedImageCount,
                                                   ParentImageReferences = entity.ConsumingImageVersionIds.Select(id => _imageReferenceParser.Format(imageVersions[id]))
                                                                                                          .Distinct(StringComparer.OrdinalIgnoreCase)
                                                                                                          .OrderBy(reference => reference, StringComparer.OrdinalIgnoreCase)
                                                                                                          .ToList(),
                                                   RuntimeContainers = runtimeContainers,
                                                   VulnerabilityAssessment = VulnerabilityViewMapper.CreateVulnerabilityAssessment(representativeBaseImage, worstSeveritySummary),
                                               };
                                    })
                            .OrderByDescending(entity => entity.RuntimeContainers.Count)
                            .ThenByDescending(entity => entity.ObservedImageCount)
                            .ThenBy(entity => entity.ImageReference, StringComparer.OrdinalIgnoreCase)
                            .ToList();
    }

    /// <summary>
    /// Read scan history without re-entering the service gate
    /// </summary>
    /// <param name="take">Maximum number of entries</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Scan history list</returns>
    private async Task<IReadOnlyList<ScanHistoryItemData>> GetScanHistoryCoreAsync(int take, CancellationToken cancellationToken)
    {
        var scanRuns = await _dbContext.ScanRuns.Include(entity => entity.ObservedImage)
                                                .Include(entity => entity.DockerInstance)
                                                .AsNoTracking()
                                                .OrderByDescending(entity => entity.StartedAtUtc)
                                                .Take(take)
                                                .ToListAsync(cancellationToken)
                                                .ConfigureAwait(false);

        return scanRuns.Select(MapScanRun)
                       .ToList();
    }

    /// <summary>
    /// Resolve the vulnerability configuration hint without re-entering the service gate
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Configuration hint kind</returns>
    private async Task<VulnerabilityConfigurationHintKind> GetVulnerabilityConfigurationHintCoreAsync(CancellationToken cancellationToken)
    {
        var vulnerabilityOptions = _optionsMonitor.CurrentValue.Vulnerabilities;

        if (vulnerabilityOptions.Enabled == false)
        {
            return VulnerabilityConfigurationHintKind.ScanningDisabled;
        }

        var latestRun = await _dbContext.ScanRuns.Where(entity => entity.Type == ScanRunType.Vulnerability
                                                                  && entity.CompletedAtUtc != null)
                                                 .OrderByDescending(entity => entity.StartedAtUtc)
                                                 .Select(entity => new
                                                                   {
                                                                       entity.Status,
                                                                       entity.ErrorMessage,
                                                                   })
                                                 .FirstOrDefaultAsync(cancellationToken)
                                                 .ConfigureAwait(false);

        return VulnerabilityConfigurationHintResolver.Resolve(vulnerabilityOptions, latestRun?.Status, latestRun?.ErrorMessage);
    }

    /// <summary>
    /// Estimate the next scheduled vulnerability refresh without re-entering the service gate
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Vulnerability schedule view data</returns>
    private async Task<VulnerabilityScheduleViewData> GetVulnerabilityScheduleCoreAsync(CancellationToken cancellationToken)
    {
        var options = _optionsMonitor.CurrentValue;

        if (options.Vulnerabilities.Enabled == false)
        {
            return new VulnerabilityScheduleViewData();
        }

        var lastStartedAtUtc = await _dbContext.ScanRuns.Where(entity => entity.Type == ScanRunType.Vulnerability)
                                                        .OrderByDescending(entity => entity.StartedAtUtc)
                                                        .Select(entity => (DateTimeOffset?)entity.StartedAtUtc)
                                                        .FirstOrDefaultAsync(cancellationToken)
                                                        .ConfigureAwait(false);

        if (lastStartedAtUtc is null)
        {
            return new VulnerabilityScheduleViewData
                   {
                       VulnerabilityScanningEnabled = true,
                   };
        }

        var refreshInterval = TimeSpan.FromMinutes(options.Scanning.VulnerabilityRefreshIntervalMinutes);

        return new VulnerabilityScheduleViewData
               {
                   VulnerabilityScanningEnabled = true,
                   NextVulnerabilityRefreshUtc = lastStartedAtUtc.GetValueOrDefault() + refreshInterval,
               };
    }

    /// <summary>
    /// Map a linked runtime container projection
    /// </summary>
    /// <param name="entity">Container snapshot entity</param>
    /// <returns>Linked runtime container view data</returns>
    private LinkedRuntimeContainerViewData MapLinkedRuntimeContainer(ContainerSnapshot entity)
    {
        return new LinkedRuntimeContainerViewData
               {
                   DockerInstanceId = entity.DockerInstanceId,
                   ContainerId = entity.ContainerId,
                   DockerInstanceName = entity.DockerInstance.Name,
                   ContainerName = entity.Name,
                   RuntimeStatus = entity.Status.ToString(),
                   ImageReference = _imageReferenceParser.Format(entity.ImageVersion),
                   RecordedAtUtc = entity.RecordedAtUtc,
               };
    }

    /// <summary>
    /// Load recommended image versions for a finding set
    /// </summary>
    /// <param name="updateFindings">Update findings to inspect</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Recommended image versions by identifier</returns>
    private async Task<Dictionary<Guid, ImageVersion>> LoadRecommendedImageVersionsAsync(IEnumerable<UpdateFinding> updateFindings,
                                                                                         CancellationToken cancellationToken)
    {
        var recommendedImageVersionIds = updateFindings.Where(entity => entity.RecommendedImageVersionId is not null)
                                                       .Select(entity => entity.RecommendedImageVersionId.GetValueOrDefault())
                                                       .Distinct()
                                                       .ToList();

        return recommendedImageVersionIds.Count == 0
                   ? []
                   : await _dbContext.ImageVersions.Include(entity => entity.RegistryRepository)
                                                   .Where(entity => recommendedImageVersionIds.Contains(entity.Id))
                                                   .AsNoTracking()
                                                   .ToDictionaryAsync(entity => entity.Id, cancellationToken)
                                                   .ConfigureAwait(false);
    }

    /// <summary>
    /// Load latest snapshots for each runtime container
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Latest snapshots</returns>
    private async Task<IReadOnlyList<ContainerSnapshot>> GetLatestContainerSnapshotsAsync(CancellationToken cancellationToken)
    {
        var runtimeScanReferences = await _dbContext.ContainerSnapshots.Where(entity => entity.ScanRunId != null
                                                                                        && entity.ScanRun!.Type == ScanRunType.RuntimeContainer)
                                                                       .Select(entity => new
                                                                                         {
                                                                                             entity.DockerInstanceId,
                                                                                             entity.ScanRunId,
                                                                                             entity.ScanRun!.StartedAtUtc,
                                                                                         })
                                                                       .Distinct()
                                                                       .ToListAsync(cancellationToken)
                                                                       .ConfigureAwait(false);

        var targetScanRunByInstance = runtimeScanReferences.GroupBy(reference => reference.DockerInstanceId)
                                                           .ToDictionary(group => group.Key,
                                                                         group => group.OrderByDescending(reference => reference.StartedAtUtc)
                                                                                       .First()
                                                                                       .ScanRunId);

        var currentSnapshots = new List<ContainerSnapshot>();

        if (targetScanRunByInstance.Count > 0)
        {
            var targetScanRunIds = targetScanRunByInstance.Values.ToList();
            var runtimeSnapshots = await _dbContext.ContainerSnapshots.Include(entity => entity.DockerInstance.PortainerEndpoint)
                                                                      .Include(entity => entity.ImageVersion.RegistryRepository)
                                                                      .Include(entity => entity.ScanRun)
                                                                      .Where(entity => targetScanRunIds.Contains(entity.ScanRunId))
                                                                      .AsNoTracking()
                                                                      .OrderByDescending(entity => entity.RecordedAtUtc)
                                                                      .ToListAsync(cancellationToken)
                                                                      .ConfigureAwait(false);

            currentSnapshots.AddRange(runtimeSnapshots.Where(entity => targetScanRunByInstance.TryGetValue(entity.DockerInstanceId, out var scanRunId)
                                                                       && scanRunId == entity.ScanRunId));
        }

        var runtimeInstanceIds = targetScanRunByInstance.Keys.ToList();
        var fallbackSnapshots = await _dbContext.ContainerSnapshots.Include(entity => entity.DockerInstance.PortainerEndpoint)
                                                                   .Include(entity => entity.ImageVersion.RegistryRepository)
                                                                   .Include(entity => entity.ScanRun)
                                                                   .Where(entity => runtimeInstanceIds.Contains(entity.DockerInstanceId) == false
                                                                                    && _dbContext.ContainerSnapshots.Any(other => other.DockerInstanceId == entity.DockerInstanceId
                                                                                                                                  && other.ContainerId == entity.ContainerId
                                                                                                                                  && other.RecordedAtUtc > entity.RecordedAtUtc) == false)
                                                                   .AsNoTracking()
                                                                   .OrderByDescending(entity => entity.RecordedAtUtc)
                                                                   .ToListAsync(cancellationToken)
                                                                   .ConfigureAwait(false);

        currentSnapshots.AddRange(fallbackSnapshots.GroupBy(entity => CreateContainerKey(entity.DockerInstanceId, entity.ContainerId))
                                                   .Select(group => group.First()));

        return currentSnapshots;
    }

    /// <summary>
    /// Load account-discovered own images keyed by repository
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Observed images by repository</returns>
    private async Task<Dictionary<string, ObservedImage>> LoadOwnImagesByRepositoryAsync(CancellationToken cancellationToken)
    {
        var observedImages = await _dbContext.ObservedImages.Include(entity => entity.CurrentImageVersion.RegistryRepository)
                                                            .Where(entity => entity.Source == RegistrationSource.Discovery)
                                                            .AsNoTracking()
                                                            .ToListAsync(cancellationToken)
                                                            .ConfigureAwait(false);

        return observedImages.GroupBy(entity => CreateRepositoryKey(entity.CurrentImageVersion), StringComparer.OrdinalIgnoreCase)
                             .ToDictionary(group => group.Key,
                                           group => group.OrderBy(entity => entity.Name, StringComparer.OrdinalIgnoreCase).First(),
                                           StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Load base-image relationships keyed by child image version
    /// </summary>
    /// <param name="childImageVersionIds">Child image version identifiers</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Base-image relationships by child image version</returns>
    private async Task<Dictionary<Guid, IReadOnlyList<BaseImageRelationshipData>>> LoadBaseImageRelationshipsByChildVersionAsync(List<Guid> childImageVersionIds,
                                                                                                                                 CancellationToken cancellationToken)
    {
        if (childImageVersionIds.Count == 0)
        {
            return [];
        }

        var relationships = await _dbContext.ImageRelationships.Include(entity => entity.BaseImageVersion.RegistryRepository)
                                                               .Where(entity => childImageVersionIds.Contains(entity.ChildImageVersionId)
                                                                                && entity.RelationshipType == ImageRelationshipType.BaseImage)
                                                               .OrderBy(entity => entity.ChildImageVersionId)
                                                               .ThenBy(entity => entity.Depth)
                                                               .AsNoTracking()
                                                               .ToListAsync(cancellationToken)
                                                               .ConfigureAwait(false);
        var baseImageVersionIds = relationships.Select(entity => entity.BaseImageVersionId)
                                               .Distinct()
                                               .ToList();
        var activeFindingLookup = await LoadActiveVulnerabilitySeveritySummariesAsync(baseImageVersionIds, cancellationToken).ConfigureAwait(false);

        return relationships.GroupBy(entity => entity.ChildImageVersionId)
                            .ToDictionary(group => group.Key,
                                          group => (IReadOnlyList<BaseImageRelationshipData>)group.Select(entity => new BaseImageRelationshipData
                                                                                                                    {
                                                                                                                        ImageReference = _imageReferenceParser.Format(entity.BaseImageVersion),
                                                                                                                        Depth = entity.Depth,
                                                                                                                        SourceReference = entity.SourceReference,
                                                                                                                        VulnerabilityAssessment = VulnerabilityViewMapper.CreateVulnerabilityAssessment(entity.BaseImageVersion,
                                                                                                                                                                                                        VulnerabilityViewMapper.GetSummaryOrEmpty(activeFindingLookup, entity.BaseImageVersionId)),
                                                                                                                    })
                                                                                                  .ToList());
    }

    /// <summary>
    /// Load active vulnerability severity summaries for a set of image versions
    /// </summary>
    /// <param name="imageVersionIds">Image version identifiers</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Severity summary by image version identifier</returns>
    private async Task<Dictionary<Guid, VulnerabilitySeveritySummaryViewData>> LoadActiveVulnerabilitySeveritySummariesAsync(List<Guid> imageVersionIds, CancellationToken cancellationToken)
    {
        if (imageVersionIds.Count == 0)
        {
            return [];
        }

        var activeFindingCounts = await _dbContext.VulnerabilityFindings.Where(entity => entity.IsActive
                                                                                         && imageVersionIds.Contains(entity.ImageVersionId))
                                                                        .GroupBy(entity => new
                                                                                           {
                                                                                               entity.ImageVersionId,
                                                                                               entity.Severity,
                                                                                           })
                                                                        .Select(group => new
                                                                                         {
                                                                                             group.Key.ImageVersionId,
                                                                                             group.Key.Severity,
                                                                                             ActiveFindingCount = group.Count(),
                                                                                         })
                                                                        .ToListAsync(cancellationToken)
                                                                        .ConfigureAwait(false);

        return activeFindingCounts.GroupBy(entity => entity.ImageVersionId)
                                  .ToDictionary(group => group.Key,
                                                group => VulnerabilityViewMapper.CreateSeveritySummary(group.Select(entity => new KeyValuePair<VulnerabilitySeverity, int>(entity.Severity,
                                                                                                                                                                           entity.ActiveFindingCount))));
    }

    /// <summary>
    /// Load active update finding counts for a set of observed images
    /// </summary>
    /// <param name="observedImageIds">Observed image identifiers</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Active update finding count by observed image identifier</returns>
    private async Task<Dictionary<Guid, int>> LoadActiveUpdateFindingCountsByObservedImageAsync(List<Guid> observedImageIds, CancellationToken cancellationToken)
    {
        if (observedImageIds.Count == 0)
        {
            return [];
        }

        var activeFindingCounts = await _dbContext.UpdateFindings.Where(entity => entity.IsActive
                                                                                  && entity.ObservedImageId != null
                                                                                  && observedImageIds.Contains(entity.ObservedImageId.Value))
                                                                 .GroupBy(entity => entity.ObservedImageId!.Value)
                                                                 .Select(group => new
                                                                                  {
                                                                                      ObservedImageId = group.Key,
                                                                                      ActiveFindingCount = group.Count(),
                                                                                  })
                                                                 .ToListAsync(cancellationToken)
                                                                 .ConfigureAwait(false);

        return activeFindingCounts.ToDictionary(entity => entity.ObservedImageId, entity => entity.ActiveFindingCount);
    }

    /// <summary>
    /// Load recent runtime container resource histories for the runtime list
    /// </summary>
    /// <param name="snapshots">Latest container snapshots</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Recent resource histories by container key</returns>
    private async Task<Dictionary<string, IReadOnlyList<ResourceUsagePointViewData>>> GetRuntimeContainerResourceHistoriesAsync(IReadOnlyList<ContainerSnapshot> snapshots,
                                                                                                                                CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        if (snapshots.Count == 0)
        {
            return [with(StringComparer.OrdinalIgnoreCase)];
        }

        var cutoff = DateTimeOffset.UtcNow.Subtract(_resourceHistoryWindow);
        var instanceIds = snapshots.Select(entity => entity.DockerInstanceId)
                                   .Distinct()
                                   .ToList();
        var containerIds = snapshots.Select(entity => entity.ContainerId)
                                    .Distinct(StringComparer.OrdinalIgnoreCase)
                                    .ToList();
        var expectedKeys = snapshots.Select(entity => CreateContainerKey(entity.DockerInstanceId, entity.ContainerId))
                                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var samples = await _dbContext.RuntimeContainerResourceSamples.Where(entity => instanceIds.Contains(entity.DockerInstanceId)
                                                                                       && containerIds.Contains(entity.ContainerId)
                                                                                       && entity.RecordedAtUtc >= cutoff)
                                                                      .OrderByDescending(entity => entity.RecordedAtUtc)
                                                                      .AsNoTracking()
                                                                      .ToListAsync(cancellationToken)
                                                                      .ConfigureAwait(false);

        return samples.Where(entity => expectedKeys.Contains(CreateContainerKey(entity.DockerInstanceId, entity.ContainerId)))
                      .GroupBy(entity => CreateContainerKey(entity.DockerInstanceId, entity.ContainerId))
                      .ToDictionary(group => group.Key,
                                    group => (IReadOnlyList<ResourceUsagePointViewData>)group.Take(RuntimeListResourceHistoryCount)
                                                                                             .Select(MapResourceUsage)
                                                                                             .ToList(),
                                    StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Load latest Docker instance resource samples
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Latest instance samples by instance identifier</returns>
    private async Task<Dictionary<Guid, DockerInstanceResourceSample>> GetLatestDockerInstanceResourceSamplesAsync(CancellationToken cancellationToken)
    {
        var samples = await _dbContext.DockerInstanceResourceSamples.AsNoTracking()
                                                                    .OrderByDescending(entity => entity.RecordedAtUtc)
                                                                    .ToListAsync(cancellationToken)
                                                                    .ConfigureAwait(false);

        return samples.GroupBy(entity => entity.DockerInstanceId)
                      .ToDictionary(group => group.Key, group => group.First());
    }

    /// <summary>
    /// Read observed image scan history
    /// </summary>
    /// <param name="observedImageId">ID of the observed image</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Scan history entries</returns>
    private async Task<IReadOnlyList<ScanHistoryItemData>> GetObservedImageScanHistoryAsync(Guid observedImageId, CancellationToken cancellationToken)
    {
        var scanRuns = await _dbContext.ScanRuns.Where(entity => entity.ObservedImageId == observedImageId)
                                                .AsNoTracking()
                                                .OrderByDescending(entity => entity.StartedAtUtc)
                                                .Take(20)
                                                .ToListAsync(cancellationToken)
                                                .ConfigureAwait(false);

        return scanRuns.Select(MapScanRun)
                       .ToList();
    }

    /// <summary>
    /// Read runtime-container-specific scan history
    /// </summary>
    /// <param name="dockerInstanceId">ID of the Docker instance</param>
    /// <param name="containerId">ID of the container</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Scan history entries</returns>
    private async Task<IReadOnlyList<ScanHistoryItemData>> GetRuntimeContainerScanHistoryAsync(Guid dockerInstanceId,
                                                                                               string containerId,
                                                                                               CancellationToken cancellationToken)
    {
        var snapshots = await _dbContext.ContainerSnapshots.Include(entity => entity.ScanRun)
                                                           .Where(entity => entity.DockerInstanceId == dockerInstanceId
                                                                            && entity.ContainerId == containerId)
                                                           .OrderByDescending(entity => entity.RecordedAtUtc)
                                                           .Take(20)
                                                           .AsNoTracking()
                                                           .ToListAsync(cancellationToken)
                                                           .ConfigureAwait(false);

        return snapshots.Select(entity => new ScanHistoryItemData
                                          {
                                              Id = entity.ScanRunId ?? entity.Id,
                                              Type = entity.ScanRun?.Type.ToString() ?? ScanRunType.RuntimeContainer.ToString(),
                                              Status = entity.ScanRun?.Status.ToString() ?? ScanRunStatus.NotSet.ToString(),
                                              TriggerSource = entity.ScanRun?.TriggerSource.ToString() ?? ScanTriggerSource.NotSet.ToString(),
                                              SubjectName = entity.Name,
                                              StartedAtUtc = entity.ScanRun?.StartedAtUtc ?? entity.RecordedAtUtc,
                                              CompletedAtUtc = entity.ScanRun?.CompletedAtUtc ?? entity.RecordedAtUtc,
                                              ErrorMessage = entity.ScanRun?.ErrorMessage ?? entity.UpdateAssessmentMessage,
                                          })
                        .ToList();
    }

    /// <summary>
    /// Read runtime-container resource history
    /// </summary>
    /// <param name="dockerInstanceId">ID of the Docker instance</param>
    /// <param name="containerId">ID of the container</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Resource history entries</returns>
    private async Task<IReadOnlyList<ResourceUsagePointViewData>> GetRuntimeContainerResourceHistoryAsync(Guid dockerInstanceId,
                                                                                                          string containerId,
                                                                                                          CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow.Subtract(_resourceHistoryWindow);
        var samples = await _dbContext.RuntimeContainerResourceSamples.Where(entity => entity.DockerInstanceId == dockerInstanceId
                                                                                       && entity.ContainerId == containerId
                                                                                       && entity.RecordedAtUtc >= cutoff)
                                                                      .OrderByDescending(entity => entity.RecordedAtUtc)
                                                                      .Take(48)
                                                                      .AsNoTracking()
                                                                      .ToListAsync(cancellationToken)
                                                                      .ConfigureAwait(false);

        return samples.Select(MapResourceUsage)
                      .ToList();
    }

    /// <summary>
    /// Read Docker-instance resource history
    /// </summary>
    /// <param name="dockerInstanceId">ID of the Docker instance</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Resource history entries</returns>
    private async Task<IReadOnlyList<ResourceUsagePointViewData>> GetDockerInstanceResourceHistoryAsync(Guid dockerInstanceId, CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow.Subtract(_resourceHistoryWindow);
        var samples = await _dbContext.DockerInstanceResourceSamples.Where(entity => entity.DockerInstanceId == dockerInstanceId
                                                                                     && entity.RecordedAtUtc >= cutoff)
                                                                    .OrderByDescending(entity => entity.RecordedAtUtc)
                                                                    .Take(48)
                                                                    .AsNoTracking()
                                                                    .ToListAsync(cancellationToken)
                                                                    .ConfigureAwait(false);

        return samples.Select(MapResourceUsage)
                      .ToList();
    }

    /// <summary>
    /// Map a scan run to view data
    /// </summary>
    /// <param name="scanRun">Scan run entity</param>
    /// <returns>Scan history item</returns>
    private ScanHistoryItemData MapScanRun(ScanRun scanRun)
    {
        return new ScanHistoryItemData
               {
                   Id = scanRun.Id,
                   Type = scanRun.Type.ToString(),
                   Status = scanRun.Status.ToString(),
                   TriggerSource = scanRun.TriggerSource.ToString(),
                   SubjectName = scanRun.ObservedImage?.Name ?? scanRun.DockerInstance?.Name,
                   StartedAtUtc = scanRun.StartedAtUtc,
                   CompletedAtUtc = scanRun.CompletedAtUtc,
                   ErrorMessage = scanRun.ErrorMessage,
               };
    }

    /// <summary>
    /// Map an update finding to view data
    /// </summary>
    /// <param name="entity">Update finding entity</param>
    /// <param name="recommendedImageVersions">Recommended image versions by identifier</param>
    /// <param name="manualSelection">Manual selection of container tag</param>
    /// <returns>Update finding view data</returns>
    private UpdateFindingViewData MapUpdateFinding(UpdateFinding entity,
                                                   Dictionary<Guid, ImageVersion> recommendedImageVersions,
                                                   RuntimeContainerTagSelection? manualSelection)
    {
        ImageVersion? recommendedImageVersion = null;

        if (entity.RecommendedImageVersionId is not null)
        {
            recommendedImageVersions.TryGetValue(entity.RecommendedImageVersionId.GetValueOrDefault(), out recommendedImageVersion);
        }

        var tagCandidates = TagCandidateViewHelper.FilterVisibleTagCandidates(entity.TagCandidates.OrderBy(candidate => candidate.Rank)
                                                                                                  .Select(candidate => new TagCandidateViewData
                                                                                                                       {
                                                                                                                           Tag = candidate.Tag,
                                                                                                                           Digest = candidate.Digest,
                                                                                                                           PublishedAtUtc = candidate.PublishedAtUtc,
                                                                                                                           Reason = candidate.Reason,
                                                                                                                           IsRecommended = candidate.IsRecommended,
                                                                                                                           IsSelected = manualSelection is not null
                                                                                                                                        && string.Equals(candidate.Tag, manualSelection.Tag, StringComparison.OrdinalIgnoreCase)
                                                                                                                                        && string.Equals(candidate.Digest ?? string.Empty,
                                                                                                                                                         manualSelection.Digest ?? string.Empty,
                                                                                                                                                         StringComparison.OrdinalIgnoreCase),
                                                                                                                       }));

        TagCandidateViewHelper.PopulateResolvedVersionTags(tagCandidates);

        return new UpdateFindingViewData
               {
                   Type = FormatUpdateFindingType(entity.Type),
                   Summary = entity.Summary,
                   Details = entity.Details,
                   RecommendedImage = recommendedImageVersion is null ? null : _imageReferenceParser.Format(recommendedImageVersion),
                   IsActive = entity.IsActive,
                   DetectedAtUtc = entity.DetectedAtUtc,
                   TagCandidates = tagCandidates,
               };
    }

    /// <summary>
    /// Read the latest observed-image scan status
    /// </summary>
    /// <param name="observedImageId">ID of the observed image</param>
    /// <returns>Scan status</returns>
    private string GetLatestObservedScanStatus(Guid observedImageId)
    {
        return _dbContext.ScanRuns.Where(entity => entity.ObservedImageId == observedImageId && entity.Type == ScanRunType.ObservedImage)
                                  .OrderByDescending(entity => entity.StartedAtUtc)
                                  .Select(entity => entity.Status.ToString())
                                  .FirstOrDefault() ?? "NotScanned";
    }

    /// <summary>
    /// Read the latest observed-image scan message
    /// </summary>
    /// <param name="observedImageId">ID of the observed image</param>
    /// <returns>Scan message</returns>
    private string? GetLatestObservedScanMessage(Guid observedImageId)
    {
        return _dbContext.ScanRuns.Where(entity => entity.ObservedImageId == observedImageId && entity.Type == ScanRunType.ObservedImage)
                                  .OrderByDescending(entity => entity.StartedAtUtc)
                                  .Select(entity => entity.ErrorMessage)
                                  .FirstOrDefault();
    }

    /// <summary>
    /// Load the most recent container action runs of a container on a Docker instance
    /// </summary>
    /// <param name="dockerInstanceId">ID of the Docker instance</param>
    /// <param name="containerId">Container identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Runs, newest first</returns>
    private async Task<IReadOnlyList<ContainerActionRunViewData>> GetContainerActionHistoryAsync(Guid dockerInstanceId, string containerId, CancellationToken cancellationToken)
    {
        var runs = await _dbContext.ContainerActionRuns.AsNoTracking()
                                                       .Where(entity => entity.DockerInstanceId == dockerInstanceId
                                                                        && entity.ContainerSnapshot != null
                                                                        && entity.ContainerSnapshot.ContainerId == containerId)
                                                       .OrderByDescending(entity => entity.RequestedAtUtc)
                                                       .Take(ContainerActionHistoryLimit)
                                                       .ToListAsync(cancellationToken)
                                                       .ConfigureAwait(false);

        return runs.Select(entity => new ContainerActionRunViewData
                                     {
                                         Action = ContainerActionDisplayNames.GetDisplayName(entity.ActionType),
                                         Status = entity.Status.ToString(),
                                         RequestedAtUtc = entity.RequestedAtUtc,
                                         CompletedAtUtc = entity.CompletedAtUtc,
                                         RequestedBy = entity.RequestedBy,
                                         Message = entity.ErrorMessage,
                                     })
                   .ToList();
    }

    /// <summary>
    /// Read the latest runtime scan status for a Docker instance
    /// </summary>
    /// <param name="dockerInstanceId">ID of the Docker instance</param>
    /// <returns>Scan status</returns>
    private string GetLatestRuntimeScanStatus(Guid dockerInstanceId)
    {
        return _dbContext.ScanRuns.Where(entity => entity.DockerInstanceId == dockerInstanceId && entity.Type == ScanRunType.RuntimeContainer)
                                  .OrderByDescending(entity => entity.StartedAtUtc)
                                  .Select(entity => entity.Status.ToString())
                                  .FirstOrDefault() ?? "NotScanned";
    }

    /// <summary>
    /// Core observed-image query, optionally filtered to a single registration source
    /// </summary>
    /// <param name="source">Registration source filter; null loads all sources</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Observed image list items</returns>
    private async Task<IReadOnlyList<ObservedImageListItemData>> GetObservedImagesCoreAsync(RegistrationSource? source, CancellationToken cancellationToken)
    {
        return await ExecuteSerializedAsync(() => LoadObservedImageListAsync(source, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Read the fleet-wide vulnerability overview without re-entering the service gate
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Vulnerability overview items grouped by advisory</returns>
    private async Task<IReadOnlyList<VulnerabilityOverviewItemData>> GetVulnerabilityOverviewCoreAsync(CancellationToken cancellationToken)
    {
        var activeFindings = await _dbContext.VulnerabilityFindings.Include(entity => entity.ImageVersion.RegistryRepository)
                                                                   .Include(entity => entity.ImageVersion.ObservedImages)
                                                                   .Where(entity => entity.IsActive)
                                                                   .AsNoTracking()
                                                                   .ToListAsync(cancellationToken)
                                                                   .ConfigureAwait(false);

        if (activeFindings.Count == 0)
        {
            return [];
        }

        var latestSnapshots = await GetLatestContainerSnapshotsAsync(cancellationToken).ConfigureAwait(false);
        var containerCountsByImageVersion = latestSnapshots.GroupBy(entity => entity.ImageVersionId)
                                                           .ToDictionary(group => group.Key, group => group.Count());

        return activeFindings.GroupBy(entity => entity.AdvisoryId, StringComparer.OrdinalIgnoreCase)
                             .OrderByDescending(group => group.Max(entity => entity.Severity))
                             .ThenByDescending(group => group.Max(entity => entity.CvssScore) ?? decimal.MinValue)
                             .ThenByDescending(group => group.Select(entity => entity.ImageVersionId).Distinct().Count())
                             .Select(group => CreateVulnerabilityOverviewItem(group, containerCountsByImageVersion))
                             .ToList();
    }

    /// <summary>
    /// Create a vulnerability overview item for an advisory group
    /// </summary>
    /// <param name="advisoryGroup">Active findings sharing the same advisory identifier</param>
    /// <param name="containerCountsByImageVersion">Running container counts by image version identifier</param>
    /// <returns>Vulnerability overview item</returns>
    private VulnerabilityOverviewItemData CreateVulnerabilityOverviewItem(IGrouping<string, VulnerabilityFinding> advisoryGroup,
                                                                          Dictionary<Guid, int> containerCountsByImageVersion)
    {
        var representativeFinding = advisoryGroup.OrderByDescending(entity => entity.Severity)
                                                 .ThenByDescending(entity => entity.CvssScore ?? decimal.MinValue)
                                                 .First();
        var affectedImageVersions = advisoryGroup.GroupBy(entity => entity.ImageVersionId)
                                                 .Select(imageGroup => CreateAffectedImage(imageGroup.First().ImageVersion))
                                                 .ToList();
        var affectedContainerCount = affectedImageVersions.Sum(entity => GetCountOrZero(containerCountsByImageVersion, entity.ImageVersionId));
        var affectedImages = affectedImageVersions.GroupBy(entity => ImageReferenceFormatter.GetReference(entity.ImageReference), StringComparer.OrdinalIgnoreCase)
                                                  .Select(referenceGroup => SelectRepresentativeAffectedImage(referenceGroup))
                                                  .OrderBy(entity => entity.ImageReference, StringComparer.OrdinalIgnoreCase)
                                                  .ToList();

        return new VulnerabilityOverviewItemData
               {
                   AdvisoryId = representativeFinding.AdvisoryId,
                   Title = representativeFinding.Title,
                   Severity = advisoryGroup.Max(entity => entity.Severity).ToString(),
                   CvssScore = advisoryGroup.Max(entity => entity.CvssScore),
                   ReferenceUrl = VulnerabilityViewMapper.SanitizeHttpUrl(representativeFinding.ReferenceUrl),
                   AffectedPackages = advisoryGroup.Select(entity => entity.AffectedPackage)
                                                   .Where(package => string.IsNullOrWhiteSpace(package) == false)
                                                   .Select(package => package!)
                                                   .Distinct(StringComparer.OrdinalIgnoreCase)
                                                   .OrderBy(package => package, StringComparer.OrdinalIgnoreCase)
                                                   .ToList(),
                   FixedVersions = advisoryGroup.Select(entity => entity.FixedVersion)
                                                .Where(fixedVersion => string.IsNullOrWhiteSpace(fixedVersion) == false)
                                                .Select(fixedVersion => fixedVersion!)
                                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                                .OrderBy(fixedVersion => fixedVersion, StringComparer.OrdinalIgnoreCase)
                                                .ToList(),
                   AffectedImages = affectedImages,
                   AffectedContainerCount = affectedContainerCount,
               };
    }

    /// <summary>
    /// Create an affected-image entry for a vulnerability overview item
    /// </summary>
    /// <param name="imageVersion">Image version entity</param>
    /// <returns>Affected image view data</returns>
    private VulnerabilityOverviewAffectedImageData CreateAffectedImage(ImageVersion imageVersion)
    {
        var observedImage = imageVersion.ObservedImages.OrderBy(entity => entity.Name, StringComparer.OrdinalIgnoreCase)
                                                       .FirstOrDefault();

        return new VulnerabilityOverviewAffectedImageData
               {
                   ImageVersionId = imageVersion.Id,
                   ImageReference = _imageReferenceParser.Format(imageVersion),
                   ObservedImageId = observedImage?.Id,
                   IsOwnImage = observedImage?.Source == RegistrationSource.Discovery,
               };
    }

    /// <summary>
    /// Map a current container snapshot to a runtime container list item
    /// </summary>
    /// <param name="entity">Current container snapshot</param>
    /// <param name="ownImagesByRepository">Observed own images by repository key</param>
    /// <param name="resourceHistories">Resource usage histories by container key</param>
    /// <param name="tagCandidatesBySnapshot">Tag candidates by container snapshot identifier</param>
    /// <param name="baseImageRelationshipsByChildVersion">Base image relationships by child image version identifier</param>
    /// <param name="activeVulnerabilityFindingLookup">Active vulnerability severity summaries by image version identifier</param>
    /// <returns>Runtime container list item</returns>
    private RuntimeContainerListItemData MapRuntimeContainerListItem(ContainerSnapshot entity,
                                                                     Dictionary<string, ObservedImage> ownImagesByRepository,
                                                                     Dictionary<string, IReadOnlyList<ResourceUsagePointViewData>> resourceHistories,
                                                                     Dictionary<Guid, List<TagCandidateViewData>> tagCandidatesBySnapshot,
                                                                     Dictionary<Guid, IReadOnlyList<BaseImageRelationshipData>> baseImageRelationshipsByChildVersion,
                                                                     Dictionary<Guid, VulnerabilitySeveritySummaryViewData> activeVulnerabilityFindingLookup)
    {
        var dockerInstance = entity.DockerInstance;
        var imageVersion = entity.ImageVersion;
        var repositoryKey = CreateRepositoryKey(entity);

        ArgumentNullException.ThrowIfNull(dockerInstance);
        ArgumentNullException.ThrowIfNull(imageVersion);

        ownImagesByRepository.TryGetValue(repositoryKey, out var linkedObservedImage);
        resourceHistories.TryGetValue(CreateContainerKey(entity.DockerInstanceId, entity.ContainerId), out var resourceUsageHistory);
        tagCandidatesBySnapshot.TryGetValue(entity.Id, out var tagCandidates);
        baseImageRelationshipsByChildVersion.TryGetValue(entity.ImageVersionId, out var baseImageRelationships);

        var currentResourceUsage = GetCurrentResourceUsage(resourceUsageHistory);
        var baseImageVulnerabilitySummary = VulnerabilityViewMapper.SummarizeBaseImageVulnerabilities(baseImageRelationships);
        var availableTagCandidates = tagCandidates ?? [];

        var resolvedVersionTag = string.IsNullOrWhiteSpace(entity.ResolvedVersionTag) == false
                                     ? entity.ResolvedVersionTag
                                     : TagCandidateViewHelper.ResolveResolvedVersionTag(imageVersion.Tag,
                                                                                        imageVersion.Digest,
                                                                                        availableTagCandidates);
        var availableUpdateVersionTag = string.IsNullOrWhiteSpace(entity.AvailableUpdateVersionTag) == false
                                            ? entity.AvailableUpdateVersionTag
                                            : TagCandidateViewHelper.ResolveAvailableUpdateVersionTag(availableTagCandidates,
                                                                                                      resolvedVersionTag ?? imageVersion.Tag);

        return new RuntimeContainerListItemData
               {
                   DockerInstanceId = entity.DockerInstanceId,
                   ContainerId = entity.ContainerId,
                   ContainerName = entity.Name,
                   DockerInstanceName = dockerInstance.Name,
                   ImageVersionId = entity.ImageVersionId,
                   ImageReference = _imageReferenceParser.Format(imageVersion),
                   CurrentTag = imageVersion.Tag,
                   ResolvedVersionTag = resolvedVersionTag,
                   RuntimeStatus = entity.Status.ToString(),
                   UpdateState = FormatUpdateAssessmentStatus(entity.UpdateAssessmentStatus),
                   UpdateSummary = entity.UpdateAssessmentMessage,
                   AvailableUpdateVersionTag = availableUpdateVersionTag,
                   PortainerAvailable = dockerInstance.PortainerEndpoint is not null && dockerInstance.PortainerEndpoint.IsEnabled,
                   ActiveVulnerabilityFindingCount = VulnerabilityViewMapper.GetSummaryOrEmpty(activeVulnerabilityFindingLookup, entity.ImageVersionId).TotalCount,
                   VulnerabilitySeveritySummary = VulnerabilityViewMapper.GetSummaryOrEmpty(activeVulnerabilityFindingLookup, entity.ImageVersionId),
                   VulnerabilityStatus = VulnerabilityViewMapper.FormatVulnerabilityAssessmentStatus(imageVersion.VulnerabilityAssessmentStatus),
                   VulnerabilitySummary = imageVersion.VulnerabilityAssessmentMessage,
                   VulnerabilityCheckedAtUtc = imageVersion.VulnerabilityAssessmentCheckedAtUtc,
                   ActiveBaseImageVulnerabilityFindingCount = baseImageVulnerabilitySummary.ActiveFindingCount,
                   BaseImageVulnerabilitySummary = baseImageVulnerabilitySummary.Summary,
                   RecordedAtUtc = entity.RecordedAtUtc,
                   LinkedObservedImageId = linkedObservedImage?.Id,
                   LinkedObservedImageName = linkedObservedImage?.Name,
                   CurrentResourceUsage = currentResourceUsage,
                   ResourceUsageHistory = resourceUsageHistory ?? [],
               };
    }

    /// <summary>
    /// Load the update findings of the current container snapshots including their tag candidates
    /// </summary>
    /// <param name="latestSnapshotIds">Identifiers of the current container snapshots</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Update findings of the snapshots</returns>
    private async Task<IReadOnlyList<UpdateFinding>> LoadLatestUpdateFindingsAsync(List<Guid> latestSnapshotIds, CancellationToken cancellationToken)
    {
        if (latestSnapshotIds.Count == 0)
        {
            return [];
        }

        return await _dbContext.UpdateFindings.Include(entity => entity.TagCandidates)
                                              .Where(entity => entity.ContainerSnapshotId != null
                                                               && latestSnapshotIds.Contains(entity.ContainerSnapshotId.Value))
                                              .AsNoTracking()
                                              .ToListAsync(cancellationToken)
                                              .ConfigureAwait(false);
    }

    /// <summary>
    /// Load the observed images, optionally filtered to a single registration source
    /// </summary>
    /// <param name="source">Registration source filter; null loads all sources</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Observed images</returns>
    private async Task<List<ObservedImage>> LoadObservedImagesAsync(RegistrationSource? source, CancellationToken cancellationToken)
    {
        var observedImagesQuery = _dbContext.ObservedImages.Include(entity => entity.CurrentImageVersion.RegistryRepository)
                                                           .AsNoTracking();

        if (source.HasValue)
        {
            observedImagesQuery = observedImagesQuery.Where(entity => entity.Source == source.Value);
        }

        var observedImages = await observedImagesQuery.ToListAsync(cancellationToken)
                                                      .ConfigureAwait(false);

        return observedImages;
    }

    /// <summary>
    /// Load the active derived base runtime findings of the observed images and the current container snapshots
    /// </summary>
    /// <param name="observedImageIds">Observed image identifiers</param>
    /// <param name="latestSnapshotIds">Identifiers of the current container snapshots</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Derived base runtime findings, newest first</returns>
    private async Task<List<UpdateFinding>> LoadDerivedBaseRuntimeFindingsAsync(List<Guid> observedImageIds, List<Guid> latestSnapshotIds, CancellationToken cancellationToken)
    {
        return await _dbContext.UpdateFindings.Where(entity => entity.IsActive
                                                               && entity.Type == UpdateFindingType.DerivedBaseRuntimeUpdate
                                                               && ((entity.ObservedImageId != null
                                                                    && observedImageIds.Contains(entity.ObservedImageId.Value))
                                                                   || (entity.ContainerSnapshotId != null
                                                                       && latestSnapshotIds.Contains(entity.ContainerSnapshotId.Value))))
                                              .OrderByDescending(entity => entity.DetectedAtUtc)
                                              .AsNoTracking()
                                              .ToListAsync(cancellationToken)
                                              .ConfigureAwait(false);
    }

    /// <summary>
    /// Map an observed image to a list item
    /// </summary>
    /// <param name="entity">Observed image</param>
    /// <param name="observedAlertLookup">Latest derived base runtime finding by observed image identifier</param>
    /// <param name="runtimeAlertLookup">Latest derived base runtime finding by repository key</param>
    /// <param name="baseImageRelationshipsByChildVersion">Base image relationships by child image version identifier</param>
    /// <param name="activeVulnerabilityFindingLookup">Active vulnerability severity summaries by image version identifier</param>
    /// <param name="activeUpdateFindingLookup">Active update finding counts by observed image identifier</param>
    /// <param name="runtimeLinkLookup">Linked runtime container counts by repository key</param>
    /// <returns>Observed image list item</returns>
    private ObservedImageListItemData MapObservedImageListItem(ObservedImage entity,
                                                               Dictionary<Guid, UpdateFinding> observedAlertLookup,
                                                               Dictionary<string, UpdateFinding> runtimeAlertLookup,
                                                               Dictionary<Guid, IReadOnlyList<BaseImageRelationshipData>> baseImageRelationshipsByChildVersion,
                                                               Dictionary<Guid, VulnerabilitySeveritySummaryViewData> activeVulnerabilityFindingLookup,
                                                               Dictionary<Guid, int> activeUpdateFindingLookup,
                                                               Dictionary<string, int> runtimeLinkLookup)
    {
        var repositoryKey = CreateRepositoryKey(entity.CurrentImageVersion);

        observedAlertLookup.TryGetValue(entity.Id, out var observedAlert);
        runtimeAlertLookup.TryGetValue(repositoryKey, out var runtimeAlert);

        var baseRuntimeAlert = observedAlert ?? runtimeAlert;

        baseImageRelationshipsByChildVersion.TryGetValue(entity.CurrentImageVersionId, out var baseImageRelationships);

        var baseImageVulnerabilitySummary = VulnerabilityViewMapper.SummarizeBaseImageVulnerabilities(baseImageRelationships);

        return new ObservedImageListItemData
               {
                   Id = entity.Id,
                   Name = entity.Name,
                   Description = entity.Description,
                   CurrentImageVersionId = entity.CurrentImageVersionId,
                   ImageReference = _imageReferenceParser.Format(entity.CurrentImageVersion),
                   LatestScanStatus = GetLatestObservedScanStatus(entity.Id),
                   LatestScanMessage = GetLatestObservedScanMessage(entity.Id),
                   ActiveUpdateFindingCount = GetCountOrZero(activeUpdateFindingLookup, entity.Id),
                   ActiveVulnerabilityFindingCount = VulnerabilityViewMapper.GetSummaryOrEmpty(activeVulnerabilityFindingLookup, entity.CurrentImageVersionId).TotalCount,
                   VulnerabilitySeveritySummary = VulnerabilityViewMapper.GetSummaryOrEmpty(activeVulnerabilityFindingLookup, entity.CurrentImageVersionId),
                   VulnerabilityStatus = VulnerabilityViewMapper.FormatVulnerabilityAssessmentStatus(entity.CurrentImageVersion.VulnerabilityAssessmentStatus),
                   VulnerabilityMessage = entity.CurrentImageVersion.VulnerabilityAssessmentMessage,
                   VulnerabilityCheckedAtUtc = entity.CurrentImageVersion.VulnerabilityAssessmentCheckedAtUtc,
                   ActiveBaseImageVulnerabilityFindingCount = baseImageVulnerabilitySummary.ActiveFindingCount,
                   BaseImageVulnerabilitySummary = baseImageVulnerabilitySummary.Summary,
                   IsOwnImage = entity.Source == RegistrationSource.Discovery,
                   LinkedRuntimeContainerCount = entity.Source == RegistrationSource.Discovery
                                                 && runtimeLinkLookup.TryGetValue(repositoryKey, out var linkedRuntimeContainerCount)
                                                     ? linkedRuntimeContainerCount
                                                     : 0,
                   BaseRuntimeAlertSummary = baseRuntimeAlert?.Summary,
                   BaseRuntimeAlertDetails = baseRuntimeAlert?.Details,
               };
    }

    /// <summary>
    /// Read the observed image list items, optionally filtered to a single registration source
    /// </summary>
    /// <param name="source">Registration source filter; null loads all sources</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Observed image list items</returns>
    private async Task<IReadOnlyList<ObservedImageListItemData>> LoadObservedImageListAsync(RegistrationSource? source, CancellationToken cancellationToken)
    {
        var observedImages = await LoadObservedImagesAsync(source, cancellationToken).ConfigureAwait(false);

        var latestSnapshots = await GetLatestContainerSnapshotsAsync(cancellationToken).ConfigureAwait(false);
        var latestSnapshotIds = latestSnapshots.Select(entity => entity.Id)
                                               .ToList();
        var repositoryKeyBySnapshotId = latestSnapshots.ToDictionary(entity => entity.Id,
                                                                     CreateRepositoryKey);
        var observedImageIds = observedImages.Select(entity => entity.Id)
                                             .ToList();
        var derivedBaseRuntimeFindings = await LoadDerivedBaseRuntimeFindingsAsync(observedImageIds, latestSnapshotIds, cancellationToken).ConfigureAwait(false);

        var runtimeLinkLookup = latestSnapshots.GroupBy(CreateRepositoryKey)
                                               .ToDictionary(group => group.Key,
                                                             group => group.Count(),
                                                             StringComparer.OrdinalIgnoreCase);
        var runtimeAlertLookup = CreateRuntimeAlertLookup(derivedBaseRuntimeFindings, repositoryKeyBySnapshotId);
        var observedAlertLookup = CreateObservedAlertLookup(derivedBaseRuntimeFindings);
        var baseImageRelationshipsByChildVersion = await LoadBaseImageRelationshipsByChildVersionAsync(observedImages.Select(entity => entity.CurrentImageVersionId)
                                                                                                                     .Distinct()
                                                                                                                     .ToList(),
                                                                                                       cancellationToken).ConfigureAwait(false);
        var observedImageVersionIds = observedImages.Select(entity => entity.CurrentImageVersionId)
                                                    .Distinct()
                                                    .ToList();
        var activeVulnerabilityFindingLookup = await LoadActiveVulnerabilitySeveritySummariesAsync(observedImageVersionIds, cancellationToken).ConfigureAwait(false);
        var activeUpdateFindingLookup = await LoadActiveUpdateFindingCountsByObservedImageAsync(observedImageIds, cancellationToken).ConfigureAwait(false);

        return observedImages.Select(entity => MapObservedImageListItem(entity,
                                                                        observedAlertLookup,
                                                                        runtimeAlertLookup,
                                                                        baseImageRelationshipsByChildVersion,
                                                                        activeVulnerabilityFindingLookup,
                                                                        activeUpdateFindingLookup,
                                                                        runtimeLinkLookup))
                             .OrderByDescending(entity => entity.IsOwnImage)
                             .ThenByDescending(entity => entity.LinkedRuntimeContainerCount)
                             .ThenBy(entity => entity.Name)
                             .ToList();
    }

    /// <summary>
    /// Load an observed image with its current image version and registry repository
    /// </summary>
    /// <param name="observedImageId">ID of the observed image</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Observed image, or null when it does not exist</returns>
    private async Task<ObservedImage?> LoadObservedImageAsync(Guid observedImageId, CancellationToken cancellationToken)
    {
        return await _dbContext.ObservedImages.Include(entity => entity.CurrentImageVersion.RegistryRepository)
                                              .AsNoTracking()
                                              .SingleOrDefaultAsync(entity => entity.Id == observedImageId, cancellationToken)
                                              .ConfigureAwait(false);
    }

    /// <summary>
    /// Load the active update findings of an observed image including their tag candidates
    /// </summary>
    /// <param name="observedImageId">ID of the observed image</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Active update findings, newest first</returns>
    private async Task<List<UpdateFinding>> LoadActiveObservedUpdateFindingsAsync(Guid observedImageId, CancellationToken cancellationToken)
    {
        return await _dbContext.UpdateFindings.Include(entity => entity.TagCandidates)
                                              .Where(entity => entity.ObservedImageId == observedImageId && entity.IsActive)
                                              .OrderByDescending(entity => entity.DetectedAtUtc)
                                              .AsNoTracking()
                                              .ToListAsync(cancellationToken)
                                              .ConfigureAwait(false);
    }

    /// <summary>
    /// Load the vulnerability findings of an image version sorted for display
    /// </summary>
    /// <param name="imageVersionId">ID of the image version</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Sorted vulnerability findings</returns>
    private async Task<List<VulnerabilityFinding>> LoadSortedVulnerabilityFindingsAsync(Guid imageVersionId, CancellationToken cancellationToken)
    {
        var vulnerabilityFindings = await _dbContext.VulnerabilityFindings.Where(entity => entity.ImageVersionId == imageVersionId)
                                                                          .AsNoTracking()
                                                                          .ToListAsync(cancellationToken)
                                                                          .ConfigureAwait(false);

        return VulnerabilityViewMapper.SortFindingsForDisplay(vulnerabilityFindings);
    }

    /// <summary>
    /// Load the active derived base runtime findings of the supplied container snapshots including their tag candidates
    /// </summary>
    /// <param name="linkedSnapshotIds">Identifiers of the container snapshots</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Derived base runtime findings, newest first</returns>
    private async Task<List<UpdateFinding>> LoadRuntimeDerivedFindingsAsync(List<Guid> linkedSnapshotIds, CancellationToken cancellationToken)
    {
        if (linkedSnapshotIds.Count == 0)
        {
            return [];
        }

        return await _dbContext.UpdateFindings.Include(entity => entity.TagCandidates)
                                              .Where(entity => entity.IsActive
                                                               && entity.Type == UpdateFindingType.DerivedBaseRuntimeUpdate
                                                               && entity.ContainerSnapshotId != null
                                                               && linkedSnapshotIds.Contains(entity.ContainerSnapshotId.Value))
                                              .OrderByDescending(entity => entity.DetectedAtUtc)
                                              .AsNoTracking()
                                              .ToListAsync(cancellationToken)
                                              .ConfigureAwait(false);
    }

    /// <summary>
    /// Read the observed image detail without re-entering the service gate
    /// </summary>
    /// <param name="observedImageId">ID of the observed image</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Observed image detail, or null when the observed image does not exist</returns>
    private async Task<ObservedImageDetailViewData?> GetObservedImageDetailCoreAsync(Guid observedImageId, CancellationToken cancellationToken)
    {
        var observedImage = await LoadObservedImageAsync(observedImageId, cancellationToken).ConfigureAwait(false);

        if (observedImage is null)
        {
            return null;
        }

        var baseImageRelationshipsByChildVersion = await LoadBaseImageRelationshipsByChildVersionAsync([observedImage.CurrentImageVersionId], cancellationToken).ConfigureAwait(false);

        var updateFindings = await LoadActiveObservedUpdateFindingsAsync(observedImage.Id, cancellationToken).ConfigureAwait(false);
        var vulnerabilityFindings = await LoadSortedVulnerabilityFindingsAsync(observedImage.CurrentImageVersionId, cancellationToken).ConfigureAwait(false);
        var latestSnapshots = await GetLatestContainerSnapshotsAsync(cancellationToken).ConfigureAwait(false);
        var linkedSnapshots = SelectLinkedSnapshots(latestSnapshots, observedImage.CurrentImageVersion);
        var linkedSnapshotIds = linkedSnapshots.Select(entity => entity.Id)
                                               .ToList();
        var runtimeDerivedFindings = await LoadRuntimeDerivedFindingsAsync(linkedSnapshotIds, cancellationToken).ConfigureAwait(false);
        var combinedUpdateFindings = updateFindings.Concat(runtimeDerivedFindings)
                                                   .OrderByDescending(entity => entity.DetectedAtUtc)
                                                   .ToList();
        var baseRuntimeAlert = SelectBaseRuntimeAlert(updateFindings, runtimeDerivedFindings);
        var recommendedImageVersions = await LoadRecommendedImageVersionsAsync(combinedUpdateFindings, cancellationToken).ConfigureAwait(false);

        baseImageRelationshipsByChildVersion.TryGetValue(observedImage.CurrentImageVersionId, out var baseImages);

        var baseImageVulnerabilitySummary = VulnerabilityViewMapper.SummarizeBaseImageVulnerabilities(baseImages);
        var vulnerabilityAssessmentDetail = VulnerabilityViewMapper.BuildVulnerabilityAssessmentDetail(observedImage.CurrentImageVersion, vulnerabilityFindings);

        return new ObservedImageDetailViewData
               {
                   Id = observedImage.Id,
                   Name = observedImage.Name,
                   Description = observedImage.Description,
                   ImageReference = _imageReferenceParser.Format(observedImage.CurrentImageVersion),
                   LatestScanStatus = GetLatestObservedScanStatus(observedImage.Id),
                   LatestScanMessage = GetLatestObservedScanMessage(observedImage.Id),
                   IsOwnImage = observedImage.Source == RegistrationSource.Discovery,
                   BaseImages = baseImages ?? [],
                   ActiveBaseImageVulnerabilityFindingCount = baseImageVulnerabilitySummary.ActiveFindingCount,
                   BaseImageVulnerabilitySummary = baseImageVulnerabilitySummary.Summary,
                   UpdateFindings = combinedUpdateFindings.Select(entity => MapUpdateFinding(entity, recommendedImageVersions, manualSelection: null))
                                                          .ToList(),
                   BaseRuntimeAlertSummary = baseRuntimeAlert?.Summary,
                   BaseRuntimeAlertDetails = baseRuntimeAlert?.Details,
                   VulnerabilityAssessment = vulnerabilityAssessmentDetail.Assessment,
                   VulnerabilityFindings = vulnerabilityAssessmentDetail.Findings,
                   LinkedRuntimeContainers = observedImage.Source == RegistrationSource.Discovery
                                                 ? linkedSnapshots.Select(MapLinkedRuntimeContainer)
                                                                  .ToList()
                                                 : [],
                   ScanHistory = await GetObservedImageScanHistoryAsync(observedImage.Id, cancellationToken).ConfigureAwait(false),
               };
    }

    /// <summary>
    /// Load the latest snapshot of a container including its Docker instance, Portainer endpoint and image version
    /// </summary>
    /// <param name="dockerInstanceId">ID of the Docker instance</param>
    /// <param name="containerId">Container identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Latest snapshot, or null when the container is unknown</returns>
    private async Task<ContainerSnapshot?> LoadLatestRuntimeSnapshotAsync(Guid dockerInstanceId, string containerId, CancellationToken cancellationToken)
    {
        return await _dbContext.ContainerSnapshots.Include(entity => entity.DockerInstance.PortainerEndpoint)
                                                  .Include(entity => entity.ImageVersion.RegistryRepository)
                                                  .AsNoTracking()
                                                  .OrderByDescending(entity => entity.RecordedAtUtc)
                                                  .FirstOrDefaultAsync(entity => entity.DockerInstanceId == dockerInstanceId
                                                                                 && entity.ContainerId == containerId,
                                                                       cancellationToken)
                                                  .ConfigureAwait(false);
    }

    /// <summary>
    /// Load the update findings of a container snapshot including their tag candidates
    /// </summary>
    /// <param name="containerSnapshotId">ID of the container snapshot</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Update findings, newest first</returns>
    private async Task<List<UpdateFinding>> LoadContainerSnapshotUpdateFindingsAsync(Guid containerSnapshotId, CancellationToken cancellationToken)
    {
        return await _dbContext.UpdateFindings.Include(entity => entity.TagCandidates)
                                              .Where(entity => entity.ContainerSnapshotId == containerSnapshotId)
                                              .OrderByDescending(entity => entity.DetectedAtUtc)
                                              .AsNoTracking()
                                              .ToListAsync(cancellationToken)
                                              .ConfigureAwait(false);
    }

    /// <summary>
    /// Load the manual tag selection of a container for the repository of its snapshot
    /// </summary>
    /// <param name="latestSnapshot">Latest container snapshot</param>
    /// <param name="dockerInstanceId">ID of the Docker instance</param>
    /// <param name="containerId">Container identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Manual tag selection, or null when none exists</returns>
    private async Task<RuntimeContainerTagSelection?> LoadManualTagSelectionAsync(ContainerSnapshot latestSnapshot,
                                                                                  Guid dockerInstanceId,
                                                                                  string containerId,
                                                                                  CancellationToken cancellationToken)
    {
        return await _dbContext.RuntimeContainerTagSelections.AsNoTracking()
                                                             .SingleOrDefaultAsync(entity => entity.DockerInstanceId == dockerInstanceId
                                                                                             && entity.ContainerId == containerId
                                                                                             && entity.RegistryRepositoryId == latestSnapshot.ImageVersion.RegistryRepositoryId,
                                                                                   cancellationToken)
                                                             .ConfigureAwait(false);
    }

    /// <summary>
    /// Load the newest active derived base runtime finding of the observed image linked to a container, unless the container's own findings hold one
    /// </summary>
    /// <param name="updateFindings">Update findings of the container snapshot</param>
    /// <param name="linkedObservedImage">Observed image linked to the container, if any</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Base runtime alert, or null when there is none</returns>
    private async Task<UpdateFinding?> LoadBaseRuntimeAlertAsync(List<UpdateFinding> updateFindings, ObservedImage? linkedObservedImage, CancellationToken cancellationToken)
    {
        var baseRuntimeAlert = updateFindings.FirstOrDefault(entity => entity.IsActive
                                                                       && entity.Type == UpdateFindingType.DerivedBaseRuntimeUpdate);

        if (baseRuntimeAlert is null && linkedObservedImage is not null)
        {
            baseRuntimeAlert = await _dbContext.UpdateFindings.Where(entity => entity.IsActive
                                                                               && entity.ObservedImageId == linkedObservedImage.Id
                                                                               && entity.Type == UpdateFindingType.DerivedBaseRuntimeUpdate)
                                                              .OrderByDescending(entity => entity.DetectedAtUtc)
                                                              .AsNoTracking()
                                                              .FirstOrDefaultAsync(cancellationToken)
                                                              .ConfigureAwait(false);
        }

        return baseRuntimeAlert;
    }

    /// <summary>
    /// Read the runtime container detail without re-entering the service gate
    /// </summary>
    /// <param name="dockerInstanceId">ID of the Docker instance</param>
    /// <param name="containerId">Container identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Runtime container detail, or null when the container is unknown</returns>
    private async Task<RuntimeContainerDetailViewData?> GetRuntimeContainerDetailCoreAsync(Guid dockerInstanceId, string containerId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);

        var latestSnapshot = await LoadLatestRuntimeSnapshotAsync(dockerInstanceId, containerId, cancellationToken).ConfigureAwait(false);

        if (latestSnapshot is null)
        {
            return null;
        }

        var detail = await BuildRuntimeContainerDetailAsync(latestSnapshot,
                                                            dockerInstanceId,
                                                            containerId,
                                                            cancellationToken).ConfigureAwait(false);

        detail.ScanHistory = await GetRuntimeContainerScanHistoryAsync(dockerInstanceId, containerId, cancellationToken).ConfigureAwait(false);
        detail.ActionHistory = await GetContainerActionHistoryAsync(dockerInstanceId, containerId, cancellationToken).ConfigureAwait(false);

        return detail;
    }

    /// <summary>
    /// Load the findings, selections and links of a container snapshot and map them to the runtime container detail without its histories
    /// </summary>
    /// <param name="latestSnapshot">Latest container snapshot</param>
    /// <param name="dockerInstanceId">ID of the Docker instance</param>
    /// <param name="containerId">Container identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Runtime container detail</returns>
    private async Task<RuntimeContainerDetailViewData> BuildRuntimeContainerDetailAsync(ContainerSnapshot latestSnapshot,
                                                                                        Guid dockerInstanceId,
                                                                                        string containerId,
                                                                                        CancellationToken cancellationToken)
    {
        var updateFindings = await LoadContainerSnapshotUpdateFindingsAsync(latestSnapshot.Id, cancellationToken).ConfigureAwait(false);
        var recommendedImageVersions = await LoadRecommendedImageVersionsAsync(updateFindings, cancellationToken).ConfigureAwait(false);
        var manualSelection = await LoadManualTagSelectionAsync(latestSnapshot,
                                                                dockerInstanceId,
                                                                containerId,
                                                                cancellationToken).ConfigureAwait(false);
        var vulnerabilityFindings = await LoadSortedVulnerabilityFindingsAsync(latestSnapshot.ImageVersionId, cancellationToken).ConfigureAwait(false);
        var baseImageRelationshipsByChildVersion = await LoadBaseImageRelationshipsByChildVersionAsync([latestSnapshot.ImageVersionId], cancellationToken).ConfigureAwait(false);
        var mappedUpdateFindings = updateFindings.Select(entity => MapUpdateFinding(entity, recommendedImageVersions, manualSelection))
                                                 .ToList();
        var registryRepository = latestSnapshot.ImageVersion.RegistryRepository;
        var ownImagesByRepository = await LoadOwnImagesByRepositoryAsync(cancellationToken).ConfigureAwait(false);
        var resourceUsageHistory = await GetRuntimeContainerResourceHistoryAsync(dockerInstanceId, containerId, cancellationToken).ConfigureAwait(false);

        ArgumentNullException.ThrowIfNull(registryRepository);

        ownImagesByRepository.TryGetValue(CreateRepositoryKey(latestSnapshot), out var linkedObservedImage);

        var baseRuntimeAlert = await LoadBaseRuntimeAlertAsync(updateFindings, linkedObservedImage, cancellationToken).ConfigureAwait(false);

        baseImageRelationshipsByChildVersion.TryGetValue(latestSnapshot.ImageVersionId, out var baseImages);

        var detail = CreateRuntimeContainerDetail(dockerInstanceId,
                                                  containerId,
                                                  latestSnapshot,
                                                  mappedUpdateFindings,
                                                  manualSelection,
                                                  linkedObservedImage,
                                                  baseRuntimeAlert);

        ApplyImageVulnerabilityData(detail,
                                    latestSnapshot.ImageVersion,
                                    baseImages,
                                    vulnerabilityFindings);

        detail.CurrentResourceUsage = GetCurrentResourceUsage(resourceUsageHistory);
        detail.ResourceUsageHistory = resourceUsageHistory;

        return detail;
    }

    /// <summary>
    /// Map a container snapshot, its findings and its links to the runtime container detail without vulnerability data, resource usage and histories
    /// </summary>
    /// <param name="dockerInstanceId">ID of the Docker instance</param>
    /// <param name="containerId">Container identifier</param>
    /// <param name="latestSnapshot">Latest container snapshot</param>
    /// <param name="mappedUpdateFindings">Mapped update findings of the container snapshot</param>
    /// <param name="manualSelection">Manual tag selection, if any</param>
    /// <param name="linkedObservedImage">Observed image linked to the container, if any</param>
    /// <param name="baseRuntimeAlert">Base runtime alert, if any</param>
    /// <returns>Runtime container detail</returns>
    private RuntimeContainerDetailViewData CreateRuntimeContainerDetail(Guid dockerInstanceId,
                                                                        string containerId,
                                                                        ContainerSnapshot latestSnapshot,
                                                                        List<UpdateFindingViewData> mappedUpdateFindings,
                                                                        RuntimeContainerTagSelection? manualSelection,
                                                                        ObservedImage? linkedObservedImage,
                                                                        UpdateFinding? baseRuntimeAlert)
    {
        var availableTagCandidates = CreateAvailableTagCandidates(mappedUpdateFindings, latestSnapshot);
        var resolvedVersionTag = string.IsNullOrWhiteSpace(latestSnapshot.ResolvedVersionTag) == false
                                     ? latestSnapshot.ResolvedVersionTag
                                     : TagCandidateViewHelper.ResolveResolvedVersionTag(latestSnapshot.ImageVersion.Tag,
                                                                                        latestSnapshot.ImageVersion.Digest,
                                                                                        availableTagCandidates);
        var availableUpdateVersionTag = string.IsNullOrWhiteSpace(latestSnapshot.AvailableUpdateVersionTag) == false
                                            ? latestSnapshot.AvailableUpdateVersionTag
                                            : TagCandidateViewHelper.ResolveAvailableUpdateVersionTag(availableTagCandidates,
                                                                                                      resolvedVersionTag ?? latestSnapshot.ImageVersion.Tag);

        return new RuntimeContainerDetailViewData
               {
                   DockerInstanceId = dockerInstanceId,
                   ContainerId = containerId,
                   ContainerName = latestSnapshot.Name,
                   DockerInstanceName = latestSnapshot.DockerInstance.Name,
                   ImageReference = _imageReferenceParser.Format(latestSnapshot.ImageVersion),
                   CurrentTag = latestSnapshot.ImageVersion.Tag,
                   ResolvedVersionTag = resolvedVersionTag,
                   RuntimeStatus = latestSnapshot.Status.ToString(),
                   ComposeProject = latestSnapshot.ComposeProject,
                   StackName = latestSnapshot.StackName,
                   ServiceName = latestSnapshot.ServiceName,
                   RecordedAtUtc = latestSnapshot.RecordedAtUtc,
                   LinkedObservedImageId = linkedObservedImage?.Id,
                   LinkedObservedImageName = linkedObservedImage?.Name,
                   UpdateStatus = FormatUpdateAssessmentStatus(latestSnapshot.UpdateAssessmentStatus),
                   UpdateMessage = latestSnapshot.UpdateAssessmentMessage,
                   AvailableUpdateVersionTag = availableUpdateVersionTag,
                   ManualSelectionImage = manualSelection is null
                                              ? null
                                              : FormatImageReference(latestSnapshot.ImageVersion.RegistryRepository.Registry,
                                                                     latestSnapshot.ImageVersion.RegistryRepository.Repository,
                                                                     manualSelection.Tag,
                                                                     manualSelection.Digest),
                   ManualSelectionAtUtc = manualSelection?.SelectedAtUtc,
                   AvailableTagCandidates = availableTagCandidates,
                   UpdateFindings = mappedUpdateFindings,
                   BaseRuntimeAlertSummary = baseRuntimeAlert?.Summary,
                   BaseRuntimeAlertDetails = baseRuntimeAlert?.Details,
               };
    }

    #endregion // Methods

    #region IApplicationViewService

    /// <inheritdoc/>
    public async Task<DashboardViewData> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteSerializedAsync(async () =>
                                            {
                                                var recentScans = await GetScanHistoryCoreAsync(10, cancellationToken).ConfigureAwait(false);
                                                var observedImageCount = await _dbContext.ObservedImages.CountAsync(entity => entity.Source == RegistrationSource.Manual, cancellationToken).ConfigureAwait(false);
                                                var myImageCount = await _dbContext.ObservedImages.CountAsync(entity => entity.Source == RegistrationSource.Discovery, cancellationToken).ConfigureAwait(false);
                                                var dockerInstanceCount = await _dbContext.DockerInstances.CountAsync(cancellationToken).ConfigureAwait(false);
                                                var latestRuntimeContainerSnapshots = await GetLatestContainerSnapshotsAsync(cancellationToken).ConfigureAwait(false);
                                                var sharedBaseImages = await GetSharedBaseImageEntriesAsync(latestRuntimeContainerSnapshots, cancellationToken).ConfigureAwait(false);
                                                var activeUpdateFindingCount = await _dbContext.UpdateFindings.CountAsync(entity => entity.IsActive, cancellationToken).ConfigureAwait(false);
                                                var ownImageBaseRuntimeWarningCount = await _dbContext.UpdateFindings.Join(_dbContext.ObservedImages.Where(entity => entity.Source == RegistrationSource.Discovery),
                                                                                                                           finding => finding.ObservedImageId,
                                                                                                                           observedImage => (Guid?)observedImage.Id,
                                                                                                                           (finding, observedImage) => new
                                                                                                                                                       {
                                                                                                                                                           finding.IsActive,
                                                                                                                                                           finding.Type,
                                                                                                                                                       })
                                                                                                                     .CountAsync(entity => entity.IsActive
                                                                                                                                           && entity.Type == UpdateFindingType.DerivedBaseRuntimeUpdate,
                                                                                                                                 cancellationToken)
                                                                                                                     .ConfigureAwait(false);
                                                var runtimeImageVersionIds = latestRuntimeContainerSnapshots.Select(entity => entity.ImageVersionId)
                                                                                                            .Distinct()
                                                                                                            .ToList();

                                                // Scoped to current runtime containers so the dashboard reflects live exposure; the fleet-wide
                                                // vulnerability inventory (including retired image versions) remains available on /vulnerabilities.
                                                var runtimeVulnerabilitySeveritySummaries = await LoadActiveVulnerabilitySeveritySummariesAsync(runtimeImageVersionIds, cancellationToken).ConfigureAwait(false);
                                                var vulnerabilitySeveritySummary = new VulnerabilitySeveritySummaryViewData
                                                                                   {
                                                                                       CriticalCount = runtimeVulnerabilitySeveritySummaries.Values.Sum(summary => summary.CriticalCount),
                                                                                       HighCount = runtimeVulnerabilitySeveritySummaries.Values.Sum(summary => summary.HighCount),
                                                                                       MediumCount = runtimeVulnerabilitySeveritySummaries.Values.Sum(summary => summary.MediumCount),
                                                                                       LowCount = runtimeVulnerabilitySeveritySummaries.Values.Sum(summary => summary.LowCount),
                                                                                       OtherCount = runtimeVulnerabilitySeveritySummaries.Values.Sum(summary => summary.OtherCount),
                                                                                   };
                                                var vulnerabilityConfigurationHint = await GetVulnerabilityConfigurationHintCoreAsync(cancellationToken).ConfigureAwait(false);

                                                return new DashboardViewData
                                                       {
                                                           ObservedImageCount = observedImageCount,
                                                           MyImageCount = myImageCount,
                                                           DockerInstanceCount = dockerInstanceCount,
                                                           RuntimeContainerCount = latestRuntimeContainerSnapshots.Count,
                                                           BaseImageCount = sharedBaseImages.Count,
                                                           ActiveUpdateFindingCount = activeUpdateFindingCount,
                                                           OwnImageBaseRuntimeWarningCount = ownImageBaseRuntimeWarningCount,
                                                           ActiveVulnerabilityFindingCount = vulnerabilitySeveritySummary.TotalCount,
                                                           VulnerabilitySeveritySummary = vulnerabilitySeveritySummary,
                                                           VulnerabilityScanningEnabled = _optionsMonitor.CurrentValue.Vulnerabilities.Enabled,
                                                           VulnerabilityConfigurationHint = vulnerabilityConfigurationHint,
                                                           RecentScans = recentScans,
                                                       };
                                            },
                                            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<DashboardSummaryViewData> GetDashboardSummaryAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteSerializedAsync(async () =>
                                            {
                                                var recentScans = await GetScanHistoryCoreAsync(1, cancellationToken).ConfigureAwait(false);
                                                var observedImageCount = await _dbContext.ObservedImages.CountAsync(entity => entity.Source == RegistrationSource.Manual, cancellationToken).ConfigureAwait(false);
                                                var myImageCount = await _dbContext.ObservedImages.CountAsync(entity => entity.Source == RegistrationSource.Discovery, cancellationToken).ConfigureAwait(false);
                                                var latestSnapshots = await GetLatestContainerSnapshotsAsync(cancellationToken).ConfigureAwait(false);

                                                return new DashboardSummaryViewData
                                                       {
                                                           ObservedImageCount = observedImageCount,
                                                           MyImageCount = myImageCount,
                                                           RuntimeContainerCount = latestSnapshots.Count,
                                                           LatestScan = recentScans.Count > 0 ? recentScans[0] : null,
                                                       };
                                            },
                                            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ObservedImageListItemData>> GetObservedImagesAsync(CancellationToken cancellationToken = default)
    {
        return GetObservedImagesCoreAsync(source: null, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ObservedImageListItemData>> GetManualObservedImagesAsync(CancellationToken cancellationToken = default)
    {
        return GetObservedImagesCoreAsync(RegistrationSource.Manual, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ObservedImageListItemData>> GetDiscoveryObservedImagesAsync(CancellationToken cancellationToken = default)
    {
        return GetObservedImagesCoreAsync(RegistrationSource.Discovery, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ObservedImageDetailViewData?> GetObservedImageDetailAsync(Guid observedImageId, CancellationToken cancellationToken = default)
    {
        return await ExecuteSerializedAsync(() => GetObservedImageDetailCoreAsync(observedImageId, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RuntimeContainerListItemData>> GetRuntimeContainersAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteSerializedAsync(() => GetRuntimeContainersCoreAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<RuntimeContainerDetailViewData?> GetRuntimeContainerDetailAsync(Guid dockerInstanceId,
                                                                                      string containerId,
                                                                                      CancellationToken cancellationToken = default)
    {
        return await ExecuteSerializedAsync(() => GetRuntimeContainerDetailCoreAsync(dockerInstanceId, containerId, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DockerInstanceListItemData>> GetDockerInstancesAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteSerializedAsync(async () =>
                                            {
                                                var instances = await _dbContext.DockerInstances.Include(entity => entity.PortainerEndpoint)
                                                                                                .AsNoTracking()
                                                                                                .OrderBy(entity => entity.Name)
                                                                                                .ToListAsync(cancellationToken)
                                                                                                .ConfigureAwait(false);
                                                var latestSnapshots = await GetLatestContainerSnapshotsAsync(cancellationToken).ConfigureAwait(false);
                                                var latestResourceSamples = await GetLatestDockerInstanceResourceSamplesAsync(cancellationToken).ConfigureAwait(false);

                                                return instances.Select(entity =>
                                                                        {
                                                                            latestResourceSamples.TryGetValue(entity.Id, out var currentResourceUsage);

                                                                            return new DockerInstanceListItemData
                                                                                   {
                                                                                       Id = entity.Id,
                                                                                       Name = entity.Name,
                                                                                       EndpointUri = UrlRedaction.RedactUserInfo(entity.EndpointUri),
                                                                                       ConnectionKind = entity.ConnectionKind.ToString(),
                                                                                       PortainerEnabled = entity.PortainerEndpoint is not null && entity.PortainerEndpoint.IsEnabled,
                                                                                       LatestScanStatus = GetLatestRuntimeScanStatus(entity.Id),
                                                                                       LatestScanCompletedAtUtc = _dbContext.ScanRuns
                                                                                                                            .Where(scan => scan.DockerInstanceId == entity.Id
                                                                                                                                           && scan.Type == ScanRunType.RuntimeContainer)
                                                                                                                            .OrderByDescending(scan => scan.StartedAtUtc)
                                                                                                                            .Select(scan => scan.CompletedAtUtc)
                                                                                                                            .FirstOrDefault(),
                                                                                       RuntimeContainerCount = latestSnapshots.Count(snapshot => snapshot.DockerInstanceId == entity.Id),
                                                                                       CurrentResourceUsage = currentResourceUsage is null ? null : MapResourceUsage(currentResourceUsage),
                                                                                   };
                                                                        })
                                                                .ToList();
                                            },
                                            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<DockerInstanceDetailViewData?> GetDockerInstanceDetailAsync(Guid dockerInstanceId, CancellationToken cancellationToken = default)
    {
        return await ExecuteSerializedAsync(async () =>
                                            {
                                                var dockerInstance = await _dbContext.DockerInstances.Include(entity => entity.PortainerEndpoint)
                                                                                                     .AsNoTracking()
                                                                                                     .SingleOrDefaultAsync(entity => entity.Id == dockerInstanceId, cancellationToken)
                                                                                                     .ConfigureAwait(false);

                                                if (dockerInstance is null)
                                                {
                                                    return null;
                                                }

                                                var runtimeContainers = await GetRuntimeContainersCoreAsync(cancellationToken).ConfigureAwait(false);
                                                var resourceHistory = await GetDockerInstanceResourceHistoryAsync(dockerInstanceId, cancellationToken).ConfigureAwait(false);
                                                var latestScanCompletedAtUtc = await _dbContext.ScanRuns.Where(scan => scan.DockerInstanceId == dockerInstanceId
                                                                                                                       && scan.Type == ScanRunType.RuntimeContainer)
                                                                                                        .OrderByDescending(scan => scan.StartedAtUtc)
                                                                                                        .Select(scan => scan.CompletedAtUtc)
                                                                                                        .FirstOrDefaultAsync(cancellationToken)
                                                                                                        .ConfigureAwait(false);

                                                return new DockerInstanceDetailViewData
                                                       {
                                                           Id = dockerInstance.Id,
                                                           Name = dockerInstance.Name,
                                                           EndpointUri = UrlRedaction.RedactUserInfo(dockerInstance.EndpointUri),
                                                           ConnectionKind = dockerInstance.ConnectionKind.ToString(),
                                                           PortainerEnabled = dockerInstance.PortainerEndpoint is not null && dockerInstance.PortainerEndpoint.IsEnabled,
                                                           LatestScanStatus = GetLatestRuntimeScanStatus(dockerInstanceId),
                                                           LatestScanCompletedAtUtc = latestScanCompletedAtUtc,
                                                           RuntimeContainerCount = runtimeContainers.Count(entity => entity.DockerInstanceId == dockerInstanceId),
                                                           CurrentResourceUsage = GetCurrentResourceUsage(resourceHistory),
                                                           ResourceUsageHistory = resourceHistory,
                                                           RuntimeContainers = runtimeContainers.Where(entity => entity.DockerInstanceId == dockerInstanceId)
                                                                                                .ToList(),
                                                       };
                                            },
                                            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<bool> HasBaseImagesAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteSerializedAsync(async () =>
                                            {
                                                var latestSnapshots = await GetLatestContainerSnapshotsAsync(cancellationToken).ConfigureAwait(false);
                                                var sharedBaseImages = await GetSharedBaseImageEntriesAsync(latestSnapshots, cancellationToken).ConfigureAwait(false);

                                                return sharedBaseImages.Count > 0;
                                            },
                                            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SharedBaseImageListItemData>> GetBaseImagesAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteSerializedAsync(() => GetBaseImagesCoreAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ScanHistoryItemData>> GetScanHistoryAsync(int take = 20, CancellationToken cancellationToken = default)
    {
        return await ExecuteSerializedAsync(() => GetScanHistoryCoreAsync(take, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<VulnerabilityScheduleViewData> GetVulnerabilityScheduleAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteSerializedAsync(() => GetVulnerabilityScheduleCoreAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<VulnerabilityOverviewItemData>> GetVulnerabilityOverviewAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteSerializedAsync(() => GetVulnerabilityOverviewCoreAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    #endregion // IApplicationViewService

    #region IDisposable

    /// <inheritdoc/>
    public void Dispose()
    {
        _dbContextLock.Dispose();
    }

    #endregion // IDisposable
}