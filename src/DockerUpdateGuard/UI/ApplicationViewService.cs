using DockerUpdateGuard.Data;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Data.Queries;
using DockerUpdateGuard.Images;

using Microsoft.EntityFrameworkCore;

namespace DockerUpdateGuard.UI;

/// <summary>
/// Default UI query service
/// </summary>
public class ApplicationViewService : IApplicationViewService
{
    #region Fields

    private readonly DockerUpdateGuardDbContext _dbContext;
    private readonly IImageReferenceParser _imageReferenceParser;
    private readonly ISharedBaseImageQueryService _sharedBaseImageQueryService;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="dbContext">Database context</param>
    /// <param name="imageReferenceParser">Image reference parser</param>
    /// <param name="sharedBaseImageQueryService">Shared base image query service</param>
    public ApplicationViewService(DockerUpdateGuardDbContext dbContext,
                                  IImageReferenceParser imageReferenceParser,
                                  ISharedBaseImageQueryService sharedBaseImageQueryService)
    {
        _dbContext = dbContext;
        _imageReferenceParser = imageReferenceParser;
        _sharedBaseImageQueryService = sharedBaseImageQueryService;
    }

    #endregion // Constructors

    #region Methods

    /// <inheritdoc/>
    public async Task<DashboardViewData> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var recentScans = await GetScanHistoryAsync(10, cancellationToken).ConfigureAwait(false);
        var sharedBaseImages = await _sharedBaseImageQueryService.GetSharedBaseImagesAsync(cancellationToken).ConfigureAwait(false);
        var observedImageCount = await _dbContext.ObservedImages.CountAsync(cancellationToken).ConfigureAwait(false);
        var dockerInstanceCount = await _dbContext.DockerInstances.CountAsync(cancellationToken).ConfigureAwait(false);
        var runtimeContainers = await GetRuntimeContainersAsync(cancellationToken).ConfigureAwait(false);
        var activeUpdateFindingCount = await _dbContext.UpdateFindings.CountAsync(entity => entity.IsActive, cancellationToken).ConfigureAwait(false);
        var activeVulnerabilityFindingCount = await _dbContext.VulnerabilityFindings.CountAsync(entity => entity.IsActive, cancellationToken).ConfigureAwait(false);

        return new DashboardViewData
               {
                   ObservedImageCount = observedImageCount,
                   DockerInstanceCount = dockerInstanceCount,
                   RuntimeContainerCount = runtimeContainers.Count,
                   SharedBaseImageCount = sharedBaseImages.Count,
                   ActiveUpdateFindingCount = activeUpdateFindingCount,
                   ActiveVulnerabilityFindingCount = activeVulnerabilityFindingCount,
                   RecentScans = recentScans,
               };
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ObservedImageListItemData>> GetObservedImagesAsync(CancellationToken cancellationToken = default)
    {
        var observedImages = await _dbContext.ObservedImages.Include(entity => entity.CurrentImageVersion)
                                                            .ThenInclude(entity => entity.RegistryRepository)
                                                            .AsNoTracking()
                                                            .OrderBy(entity => entity.Name)
                                                            .ToListAsync(cancellationToken)
                                                            .ConfigureAwait(false);

        return observedImages.Select(entity => new ObservedImageListItemData
                                               {
                                                   Id = entity.Id,
                                                   Name = entity.Name,
                                                   Description = entity.Description,
                                                   ImageReference = _imageReferenceParser.Format(entity.CurrentImageVersion),
                                                   LatestScanStatus = GetLatestObservedScanStatus(entity.Id),
                                                   LatestScanMessage = GetLatestObservedScanMessage(entity.Id),
                                                   ActiveUpdateFindingCount = _dbContext.UpdateFindings.Count(finding => finding.ObservedImageId == entity.Id && finding.IsActive),
                                                   ActiveVulnerabilityFindingCount = _dbContext.VulnerabilityFindings.Count(finding => finding.ImageVersionId == entity.CurrentImageVersionId && finding.IsActive),
                                                   VulnerabilityStatus = FormatVulnerabilityAssessmentStatus(entity.CurrentImageVersion.VulnerabilityAssessmentStatus),
                                                   VulnerabilityMessage = entity.CurrentImageVersion.VulnerabilityAssessmentMessage,
                                               })
                             .ToList();
    }

    /// <inheritdoc/>
    public async Task<ObservedImageDetailViewData?> GetObservedImageDetailAsync(Guid observedImageId, CancellationToken cancellationToken = default)
    {
        var observedImage = await _dbContext.ObservedImages.Include(entity => entity.CurrentImageVersion)
                                                           .ThenInclude(entity => entity.RegistryRepository)
                                                           .AsNoTracking()
                                                           .SingleOrDefaultAsync(entity => entity.Id == observedImageId, cancellationToken)
                                                           .ConfigureAwait(false);

        if (observedImage is null)
        {
            return null;
        }

        var baseImages = await _dbContext.ImageRelationships.Include(entity => entity.BaseImageVersion)
                                                            .ThenInclude(entity => entity.RegistryRepository)
                                                            .Where(entity => entity.ChildImageVersionId == observedImage.CurrentImageVersionId
                                                                             && entity.RelationshipType == ImageRelationshipType.BaseImage)
                                                            .OrderBy(entity => entity.Depth)
                                                            .AsNoTracking()
                                                            .ToListAsync(cancellationToken)
                                                            .ConfigureAwait(false);
        var updateFindings = await _dbContext.UpdateFindings.Include(entity => entity.TagCandidates)
                                                            .Where(entity => entity.ObservedImageId == observedImage.Id)
                                                            .OrderByDescending(entity => entity.DetectedAtUtc)
                                                            .AsNoTracking()
                                                            .ToListAsync(cancellationToken)
                                                            .ConfigureAwait(false);
        var recommendedImageVersions = await LoadRecommendedImageVersionsAsync(updateFindings, cancellationToken).ConfigureAwait(false);
        var vulnerabilityFindings = await _dbContext.VulnerabilityFindings.Where(entity => entity.ImageVersionId == observedImage.CurrentImageVersionId)
                                                                          .OrderByDescending(entity => entity.DetectedAtUtc)
                                                                          .AsNoTracking()
                                                                          .ToListAsync(cancellationToken)
                                                                          .ConfigureAwait(false);
        var scanHistory = await GetObservedImageScanHistoryAsync(observedImage.Id, cancellationToken).ConfigureAwait(false);

        return new ObservedImageDetailViewData
               {
                   Id = observedImage.Id,
                   Name = observedImage.Name,
                   Description = observedImage.Description,
                   ImageReference = _imageReferenceParser.Format(observedImage.CurrentImageVersion),
                   LatestScanStatus = GetLatestObservedScanStatus(observedImage.Id),
                   LatestScanMessage = GetLatestObservedScanMessage(observedImage.Id),
                   BaseImages = baseImages.Select(entity => new BaseImageRelationshipData
                                                            {
                                                                ImageReference = _imageReferenceParser.Format(entity.BaseImageVersion),
                                                                Depth = entity.Depth,
                                                                SourceReference = entity.SourceReference,
                                                            })
                                          .ToList(),
                   UpdateFindings = updateFindings.Select(entity => MapUpdateFinding(entity, recommendedImageVersions, manualSelection: null))
                                                  .ToList(),
                   VulnerabilityAssessment = CreateVulnerabilityAssessment(observedImage.CurrentImageVersion,
                                                                           vulnerabilityFindings.Count(entity => entity.IsActive)),
                   VulnerabilityFindings = vulnerabilityFindings.Select(MapVulnerabilityFinding)
                                                                .ToList(),
                   ScanHistory = scanHistory,
               };
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RuntimeContainerListItemData>> GetRuntimeContainersAsync(CancellationToken cancellationToken = default)
    {
        var latestSnapshots = await _dbContext.ContainerSnapshots.Include(entity => entity.DockerInstance)
                                                                 .ThenInclude(entity => entity.PortainerEndpoint)
                                                                 .Include(entity => entity.ImageVersion)
                                                                 .ThenInclude(entity => entity.RegistryRepository)
                                                                 .AsNoTracking()
                                                                 .OrderByDescending(entity => entity.RecordedAtUtc)
                                                                 .ToListAsync(cancellationToken)
                                                                 .ConfigureAwait(false);

        return latestSnapshots.GroupBy(entity => new
                                                 {
                                                     entity.DockerInstanceId,
                                                     entity.ContainerId,
                                                 })
                              .Select(group =>
                                      {
                                          var latestSnapshot = group.First();
                                          var dockerInstance = latestSnapshot.DockerInstance;
                                          var imageVersion = latestSnapshot.ImageVersion;

                                          ArgumentNullException.ThrowIfNull(dockerInstance);
                                          ArgumentNullException.ThrowIfNull(imageVersion);

                                          return new RuntimeContainerListItemData
                                                 {
                                                     DockerInstanceId = latestSnapshot.DockerInstanceId,
                                                     ContainerId = latestSnapshot.ContainerId,
                                                     ContainerName = latestSnapshot.Name,
                                                     DockerInstanceName = dockerInstance.Name,
                                                     ImageReference = _imageReferenceParser.Format(imageVersion),
                                                     RuntimeStatus = latestSnapshot.Status.ToString(),
                                                     UpdateState = FormatUpdateAssessmentStatus(latestSnapshot.UpdateAssessmentStatus),
                                                     UpdateSummary = latestSnapshot.UpdateAssessmentMessage,
                                                     PortainerAvailable = dockerInstance.PortainerEndpoint is not null && dockerInstance.PortainerEndpoint.IsEnabled,
                                                     ActiveVulnerabilityFindingCount = _dbContext.VulnerabilityFindings.Count(finding => finding.ImageVersionId == latestSnapshot.ImageVersionId && finding.IsActive),
                                                     VulnerabilityStatus = FormatVulnerabilityAssessmentStatus(imageVersion.VulnerabilityAssessmentStatus),
                                                     VulnerabilitySummary = imageVersion.VulnerabilityAssessmentMessage,
                                                     RecordedAtUtc = latestSnapshot.RecordedAtUtc,
                                                 };
                                      })
                              .OrderBy(entity => entity.DockerInstanceName)
                              .ThenBy(entity => entity.ContainerName)
                              .ToList();
    }

    /// <inheritdoc/>
    public async Task<RuntimeContainerDetailViewData?> GetRuntimeContainerDetailAsync(Guid dockerInstanceId,
                                                                                      string containerId,
                                                                                      CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);

        var latestSnapshot = await _dbContext.ContainerSnapshots.Include(entity => entity.DockerInstance)
                                                                .ThenInclude(entity => entity.PortainerEndpoint)
                                                                .Include(entity => entity.ImageVersion)
                                                                .ThenInclude(entity => entity.RegistryRepository)
                                                                .AsNoTracking()
                                                                .OrderByDescending(entity => entity.RecordedAtUtc)
                                                                .FirstOrDefaultAsync(entity => entity.DockerInstanceId == dockerInstanceId
                                                                                               && entity.ContainerId == containerId,
                                                                                     cancellationToken)
                                                                .ConfigureAwait(false);

        if (latestSnapshot is null)
        {
            return null;
        }

        var updateFindings = await _dbContext.UpdateFindings.Include(entity => entity.TagCandidates)
                                                            .Where(entity => entity.ContainerSnapshotId == latestSnapshot.Id)
                                                            .OrderByDescending(entity => entity.DetectedAtUtc)
                                                            .AsNoTracking()
                                                            .ToListAsync(cancellationToken)
                                                            .ConfigureAwait(false);
        var recommendedImageVersions = await LoadRecommendedImageVersionsAsync(updateFindings, cancellationToken).ConfigureAwait(false);
        var manualSelection = await _dbContext.RuntimeContainerTagSelections.AsNoTracking()
                                                                            .SingleOrDefaultAsync(entity => entity.DockerInstanceId == dockerInstanceId
                                                                                                            && entity.ContainerId == containerId
                                                                                                            && entity.RegistryRepositoryId == latestSnapshot.ImageVersion.RegistryRepositoryId,
                                                                                                  cancellationToken)
                                                                            .ConfigureAwait(false);
        var vulnerabilityFindings = await _dbContext.VulnerabilityFindings.Where(entity => entity.ImageVersionId == latestSnapshot.ImageVersionId)
                                                                          .OrderByDescending(entity => entity.DetectedAtUtc)
                                                                          .AsNoTracking()
                                                                          .ToListAsync(cancellationToken)
                                                                          .ConfigureAwait(false);
        var mappedUpdateFindings = updateFindings.Select(entity => MapUpdateFinding(entity, recommendedImageVersions, manualSelection))
                                                 .ToList();
        var availableTagCandidates = mappedUpdateFindings.SelectMany(entity => entity.TagCandidates)
                                                         .OrderByDescending(entity => entity.IsRecommended)
                                                         .ThenBy(entity => entity.Tag)
                                                         .ToList();
        var registryRepository = latestSnapshot.ImageVersion.RegistryRepository;

        ArgumentNullException.ThrowIfNull(registryRepository);

        return new RuntimeContainerDetailViewData
               {
                   DockerInstanceId = dockerInstanceId,
                   ContainerId = containerId,
                   ContainerName = latestSnapshot.Name,
                   DockerInstanceName = latestSnapshot.DockerInstance.Name,
                   ImageReference = _imageReferenceParser.Format(latestSnapshot.ImageVersion),
                   RuntimeStatus = latestSnapshot.Status.ToString(),
                   ComposeProject = latestSnapshot.ComposeProject,
                   StackName = latestSnapshot.StackName,
                   ServiceName = latestSnapshot.ServiceName,
                   RecordedAtUtc = latestSnapshot.RecordedAtUtc,
                   UpdateStatus = FormatUpdateAssessmentStatus(latestSnapshot.UpdateAssessmentStatus),
                   UpdateMessage = latestSnapshot.UpdateAssessmentMessage,
                   ManualSelectionImage = manualSelection is null
                                              ? null
                                              : FormatImageReference(registryRepository.Registry,
                                                                     registryRepository.Repository,
                                                                     manualSelection.Tag,
                                                                     manualSelection.Digest),
                   ManualSelectionAtUtc = manualSelection?.SelectedAtUtc,
                   AvailableTagCandidates = availableTagCandidates,
                   UpdateFindings = mappedUpdateFindings,
                   VulnerabilityAssessment = CreateVulnerabilityAssessment(latestSnapshot.ImageVersion,
                                                                           vulnerabilityFindings.Count(entity => entity.IsActive)),
                   VulnerabilityFindings = vulnerabilityFindings.Select(MapVulnerabilityFinding)
                                                                .ToList(),
                   ScanHistory = await GetRuntimeContainerScanHistoryAsync(dockerInstanceId, containerId, cancellationToken).ConfigureAwait(false),
               };
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DockerInstanceListItemData>> GetDockerInstancesAsync(CancellationToken cancellationToken = default)
    {
        var instances = await _dbContext.DockerInstances.Include(entity => entity.PortainerEndpoint)
                                                        .AsNoTracking()
                                                        .OrderBy(entity => entity.Name)
                                                        .ToListAsync(cancellationToken)
                                                        .ConfigureAwait(false);

        return instances.Select(entity => new DockerInstanceListItemData
                                          {
                                              Name = entity.Name,
                                              EndpointUri = entity.EndpointUri,
                                              ConnectionKind = entity.ConnectionKind.ToString(),
                                              PortainerEnabled = entity.PortainerEndpoint is not null && entity.PortainerEndpoint.IsEnabled,
                                              LatestScanStatus = GetLatestRuntimeScanStatus(entity.Id),
                                              LatestScanCompletedAtUtc = _dbContext.ScanRuns
                                                                                   .Where(scan => scan.DockerInstanceId == entity.Id
                                                                                                  && scan.Type == ScanRunType.RuntimeContainer)
                                                                                   .OrderByDescending(scan => scan.StartedAtUtc)
                                                                                   .Select(scan => scan.CompletedAtUtc)
                                                                                   .FirstOrDefault(),
                                              RuntimeContainerCount = _dbContext.ContainerSnapshots
                                                                                .Where(snapshot => snapshot.DockerInstanceId == entity.Id)
                                                                                .Select(snapshot => snapshot.ContainerId)
                                                                                .Distinct()
                                                                                .Count(),
                                          })
                        .ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SharedBaseImageListItemData>> GetSharedBaseImagesAsync(CancellationToken cancellationToken = default)
    {
        var sharedBaseImages = await _sharedBaseImageQueryService.GetSharedBaseImagesAsync(cancellationToken).ConfigureAwait(false);

        return sharedBaseImages.Select(MapSharedBaseImage)
                               .ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ScanHistoryItemData>> GetScanHistoryAsync(int take = 50, CancellationToken cancellationToken = default)
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
    /// Load recommended image versions for a finding set
    /// </summary>
    /// <param name="updateFindings">Update findings</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Recommended image version lookup</returns>
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
    /// Read observed image scan history
    /// </summary>
    /// <param name="observedImageId">Observed image identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Observed image scan history</returns>
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
    /// <param name="dockerInstanceId">Docker instance identifier</param>
    /// <param name="containerId">Container identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Runtime container scan history</returns>
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
    /// Map a scan run to view data
    /// </summary>
    /// <param name="scanRun">Scan run</param>
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
    /// Map a vulnerability finding to view data
    /// </summary>
    /// <param name="entity">Vulnerability finding</param>
    /// <returns>Vulnerability finding view data</returns>
    private static VulnerabilityFindingViewData MapVulnerabilityFinding(VulnerabilityFinding entity)
    {
        return new VulnerabilityFindingViewData
               {
                   AdvisoryId = entity.AdvisoryId,
                   Title = entity.Title,
                   Severity = entity.Severity.ToString(),
                   Source = entity.Source.ToString(),
                   Summary = entity.Summary,
                   AffectedPackage = entity.AffectedPackage,
                   FixedVersion = entity.FixedVersion,
                   CvssScore = entity.CvssScore,
                   ReferenceUrl = entity.ReferenceUrl,
                   IsActive = entity.IsActive,
                   DetectedAtUtc = entity.DetectedAtUtc,
               };
    }

    /// <summary>
    /// Map a shared base image entry to view data
    /// </summary>
    /// <param name="entity">Shared base image entry</param>
    /// <returns>Shared base image view data</returns>
    private SharedBaseImageListItemData MapSharedBaseImage(SharedBaseImageUsageData entity)
    {
        return new SharedBaseImageListItemData
               {
                   BaseImageVersionId = entity.BaseImageVersionId,
                   ImageReference = FormatImageReference(entity.Registry, entity.Repository, entity.Tag, entity.Digest),
                   ObservedImageCount = entity.ObservedImageCount,
                   ActiveFindingCount = _dbContext.UpdateFindings.Count(finding => finding.SubjectImageVersionId == entity.BaseImageVersionId && finding.IsActive),
               };
    }

    /// <summary>
    /// Map an update finding to view data
    /// </summary>
    /// <param name="entity">Update finding</param>
    /// <param name="recommendedImageVersions">Recommended image version lookup</param>
    /// <param name="manualSelection">Optional manual selection</param>
    /// <returns>Update finding view data</returns>
    private UpdateFindingViewData MapUpdateFinding(UpdateFinding entity,
                                                   IReadOnlyDictionary<Guid, ImageVersion> recommendedImageVersions,
                                                   RuntimeContainerTagSelection? manualSelection)
    {
        ImageVersion? recommendedImageVersion = null;

        if (entity.RecommendedImageVersionId is not null)
        {
            recommendedImageVersions.TryGetValue(entity.RecommendedImageVersionId.GetValueOrDefault(), out recommendedImageVersion);
        }

        return new UpdateFindingViewData
               {
                   Type = entity.Type.ToString(),
                   Summary = entity.Summary,
                   Details = entity.Details,
                   RecommendedImage = recommendedImageVersion is null ? null : _imageReferenceParser.Format(recommendedImageVersion),
                   IsActive = entity.IsActive,
                   DetectedAtUtc = entity.DetectedAtUtc,
                   TagCandidates = entity.TagCandidates.OrderBy(candidate => candidate.Rank)
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
                                                                            })
                                                       .ToList(),
               };
    }

    /// <summary>
    /// Create vulnerability assessment view data for an image version
    /// </summary>
    /// <param name="imageVersion">Image version</param>
    /// <param name="activeFindingCount">Active vulnerability finding count</param>
    /// <returns>Vulnerability assessment view data</returns>
    private static VulnerabilityAssessmentViewData CreateVulnerabilityAssessment(ImageVersion imageVersion, int activeFindingCount)
    {
        return new VulnerabilityAssessmentViewData
               {
                   Status = FormatVulnerabilityAssessmentStatus(imageVersion.VulnerabilityAssessmentStatus),
                   Source = FormatVulnerabilitySource(imageVersion.VulnerabilityAssessmentSource),
                   Message = imageVersion.VulnerabilityAssessmentMessage,
                   CheckedAtUtc = imageVersion.VulnerabilityAssessmentCheckedAtUtc,
                   ActiveFindingCount = activeFindingCount,
               };
    }

    /// <summary>
    /// Format an image reference from its parts
    /// </summary>
    /// <param name="registry">Registry name</param>
    /// <param name="repository">Repository path</param>
    /// <param name="tag">Tag</param>
    /// <param name="digest">Digest</param>
    /// <returns>Formatted image reference</returns>
    private static string FormatImageReference(string registry, string repository, string tag, string? digest)
    {
        return string.IsNullOrWhiteSpace(digest)
                   ? $"{registry}/{repository}:{tag}"
                   : $"{registry}/{repository}:{tag}@{digest}";
    }

    /// <summary>
    /// Read the latest observed-image scan status
    /// </summary>
    /// <param name="observedImageId">Observed image identifier</param>
    /// <returns>Latest observed-image scan status</returns>
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
    /// <param name="observedImageId">Observed image identifier</param>
    /// <returns>Latest observed-image scan message</returns>
    private string? GetLatestObservedScanMessage(Guid observedImageId)
    {
        return _dbContext.ScanRuns.Where(entity => entity.ObservedImageId == observedImageId && entity.Type == ScanRunType.ObservedImage)
                                  .OrderByDescending(entity => entity.StartedAtUtc)
                                  .Select(entity => entity.ErrorMessage)
                                  .FirstOrDefault();
    }

    /// <summary>
    /// Read the latest runtime scan status for a Docker instance
    /// </summary>
    /// <param name="dockerInstanceId">Docker instance identifier</param>
    /// <returns>Latest runtime scan status</returns>
    private string GetLatestRuntimeScanStatus(Guid dockerInstanceId)
    {
        return _dbContext.ScanRuns.Where(entity => entity.DockerInstanceId == dockerInstanceId && entity.Type == ScanRunType.RuntimeContainer)
                                  .OrderByDescending(entity => entity.StartedAtUtc)
                                  .Select(entity => entity.Status.ToString())
                                  .FirstOrDefault() ?? "NotScanned";
    }

    /// <summary>
    /// Format an update assessment status for the UI
    /// </summary>
    /// <param name="status">Update assessment status</param>
    /// <returns>Formatted status label</returns>
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
    /// Format a vulnerability assessment status for the UI
    /// </summary>
    /// <param name="status">Vulnerability assessment status</param>
    /// <returns>Formatted status label</returns>
    private static string FormatVulnerabilityAssessmentStatus(VulnerabilityAssessmentStatus status)
    {
        return status switch
               {
                   VulnerabilityAssessmentStatus.NoFindings => "No findings",
                   VulnerabilityAssessmentStatus.FindingsDetected => "Findings detected",
                   VulnerabilityAssessmentStatus.NotConfigured => "Not configured",
                   VulnerabilityAssessmentStatus.Unsupported => "Unsupported",
                   VulnerabilityAssessmentStatus.Failed => "Failed",
                   _ => "Not scanned",
               };
    }

    /// <summary>
    /// Format a vulnerability source for the UI
    /// </summary>
    /// <param name="source">Vulnerability source</param>
    /// <returns>Formatted source label</returns>
    private static string FormatVulnerabilitySource(VulnerabilitySource source)
    {
        return source switch
               {
                   VulnerabilitySource.DockerScout => "Docker Scout",
                   VulnerabilitySource.Trivy => "Trivy",
                   VulnerabilitySource.RegistryAdvisory => "Registry advisory",
                   VulnerabilitySource.Manual => "Manual",
                   _ => "Not set",
               };
    }

    #endregion // Methods
}