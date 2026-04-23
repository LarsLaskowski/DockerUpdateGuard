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

    public async Task<DashboardViewData> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var recentScansTask = GetScanHistoryAsync(10, cancellationToken);
        var recentScans = await recentScansTask.ConfigureAwait(false);
        var sharedBaseImagesTask = _sharedBaseImageQueryService.GetSharedBaseImagesAsync(cancellationToken);
        var sharedBaseImages = await sharedBaseImagesTask.ConfigureAwait(false);
        var observedImages = _dbContext.ObservedImages;
        var dockerInstances = _dbContext.DockerInstances;
        var updateFindings = _dbContext.UpdateFindings;
        var vulnerabilityFindings = _dbContext.VulnerabilityFindings;
        var observedImageCountTask = observedImages.CountAsync(cancellationToken);
        var observedImageCount = await observedImageCountTask.ConfigureAwait(false);
        var dockerInstanceCountTask = dockerInstances.CountAsync(cancellationToken);
        var dockerInstanceCount = await dockerInstanceCountTask.ConfigureAwait(false);
        var runtimeContainersTask = GetRuntimeContainersAsync(cancellationToken);
        var runtimeContainers = await runtimeContainersTask.ConfigureAwait(false);
        var runtimeContainerCount = runtimeContainers.Count;
        var activeUpdateFindingCountTask = updateFindings.CountAsync(entity => entity.IsActive, cancellationToken);
        var activeUpdateFindingCount = await activeUpdateFindingCountTask.ConfigureAwait(false);
        var activeVulnerabilityFindingCountTask = vulnerabilityFindings.CountAsync(entity => entity.IsActive, cancellationToken);
        var activeVulnerabilityFindingCount = await activeVulnerabilityFindingCountTask.ConfigureAwait(false);

        return new DashboardViewData
               {
                   ObservedImageCount = observedImageCount,
                   DockerInstanceCount = dockerInstanceCount,
                   RuntimeContainerCount = runtimeContainerCount,
                   SharedBaseImageCount = sharedBaseImages.Count,
                   ActiveUpdateFindingCount = activeUpdateFindingCount,
                   ActiveVulnerabilityFindingCount = activeVulnerabilityFindingCount,
                   RecentScans = recentScans,
               };
    }

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
                                               })
                             .ToList();
    }

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
        var updateFindings = await _dbContext.UpdateFindings.Where(entity => entity.ObservedImageId == observedImage.Id)
                                                            .OrderByDescending(entity => entity.DetectedAtUtc)
                                                            .AsNoTracking()
                                                            .ToListAsync(cancellationToken)
                                                            .ConfigureAwait(false);
        var recommendedImageVersionIds = updateFindings.Where(entity => entity.RecommendedImageVersionId is not null)
                                                       .Select(entity => entity.RecommendedImageVersionId.GetValueOrDefault())
                                                       .Distinct()
                                                       .ToList();
        var recommendedImageVersions = recommendedImageVersionIds.Count == 0
                                           ? new Dictionary<Guid, ImageVersion>()
                                           : await _dbContext.ImageVersions.Include(entity => entity.RegistryRepository)
                                                                           .Where(entity => recommendedImageVersionIds.Contains(entity.Id))
                                                                           .AsNoTracking()
                                                                           .ToDictionaryAsync(entity => entity.Id, cancellationToken)
                                                                           .ConfigureAwait(false);
        var updateFindingViewData = updateFindings.Select(entity =>
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
                                                                     };
                                                          })
                                                  .ToList();
        var vulnerabilityFindings = await _dbContext.VulnerabilityFindings
                                                    .Where(entity => entity.ImageVersionId == observedImage.CurrentImageVersionId)
                                                    .OrderByDescending(entity => entity.DetectedAtUtc)
                                                    .AsNoTracking()
                                                    .ToListAsync(cancellationToken)
                                                    .ConfigureAwait(false);
        var scanHistory = await GetObservedImageScanHistoryAsync(observedImage.Id, cancellationToken).ConfigureAwait(false);
        var baseImageViewData = baseImages.Select(entity => new BaseImageRelationshipData
                                                            {
                                                                ImageReference = _imageReferenceParser.Format(entity.BaseImageVersion),
                                                                Depth = entity.Depth,
                                                                SourceReference = entity.SourceReference,
                                                            })
                                          .ToList();
        var vulnerabilityFindingViewData = vulnerabilityFindings.Select(MapVulnerabilityFinding)
                                                                .ToList();

        return new ObservedImageDetailViewData
               {
                   Id = observedImage.Id,
                   Name = observedImage.Name,
                   Description = observedImage.Description,
                   ImageReference = _imageReferenceParser.Format(observedImage.CurrentImageVersion),
                   LatestScanStatus = GetLatestObservedScanStatus(observedImage.Id),
                   LatestScanMessage = GetLatestObservedScanMessage(observedImage.Id),
                   BaseImages = baseImageViewData,
                   UpdateFindings = updateFindingViewData,
                   VulnerabilityFindings = vulnerabilityFindingViewData,
                   ScanHistory = scanHistory,
               };
    }

    public async Task<IReadOnlyList<RuntimeContainerListItemData>> GetRuntimeContainersAsync(CancellationToken cancellationToken = default)
    {
        var latestSnapshots = await _dbContext
        .ContainerSnapshots
        .Include(entity => entity.DockerInstance)
        .ThenInclude(entity => entity.PortainerEndpoint)
        .Include(entity => entity.ImageVersion)
        .ThenInclude(entity => entity.RegistryRepository)
        .AsNoTracking()
        .OrderByDescending(entity => entity.RecordedAtUtc)
        .ToListAsync(cancellationToken)
        .ConfigureAwait(false);
        var runtimeContainers = latestSnapshots.GroupBy(entity => new
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

                                                           var portainerEndpoint = dockerInstance.PortainerEndpoint;

                                                           return new RuntimeContainerListItemData
                                                                  {
                                                                      ContainerName = latestSnapshot.Name,
                                                                      DockerInstanceName = dockerInstance.Name,
                                                                      ImageReference = _imageReferenceParser.Format(imageVersion),
                                                                      RuntimeStatus = latestSnapshot.Status.ToString(),
                                                                      UpdateState = GetRuntimeContainerState(latestSnapshot.Id),
                                                                      UpdateSummary = GetRuntimeContainerSummary(latestSnapshot.Id),
                                                                      PortainerAvailable = portainerEndpoint is not null && portainerEndpoint.IsEnabled,
                                                                      ActiveVulnerabilityFindingCount = _dbContext.VulnerabilityFindings.Count(finding => finding.ImageVersionId == latestSnapshot.ImageVersionId && finding.IsActive),
                                                                      RecordedAtUtc = latestSnapshot.RecordedAtUtc,
                                                                  };
                                                       })
                                               .OrderBy(entity => entity.DockerInstanceName)
                                               .ThenBy(entity => entity.ContainerName)
                                               .ToList();

        return runtimeContainers;
    }

    public async Task<IReadOnlyList<DockerInstanceListItemData>> GetDockerInstancesAsync(CancellationToken cancellationToken = default)
    {
        var instances = await _dbContext
        .DockerInstances
        .Include(entity => entity.PortainerEndpoint)
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

    public async Task<IReadOnlyList<SharedBaseImageListItemData>> GetSharedBaseImagesAsync(CancellationToken cancellationToken = default)
    {
        var sharedBaseImages = await _sharedBaseImageQueryService.GetSharedBaseImagesAsync(cancellationToken)
                                                                 .ConfigureAwait(false);

        return sharedBaseImages.Select(MapSharedBaseImage)
                               .ToList();
    }

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

    private static VulnerabilityFindingViewData MapVulnerabilityFinding(VulnerabilityFinding entity)
    {
        return new VulnerabilityFindingViewData
               {
                   AdvisoryId = entity.AdvisoryId,
                   Title = entity.Title,
                   Severity = entity.Severity.ToString(),
                   Summary = entity.Summary,
                   ReferenceUrl = entity.ReferenceUrl,
                   IsActive = entity.IsActive,
               };
    }

    private SharedBaseImageListItemData MapSharedBaseImage(SharedBaseImageUsageData entity)
    {
        return new SharedBaseImageListItemData
               {
                   BaseImageVersionId = entity.BaseImageVersionId,
                   ImageReference = string.IsNullOrWhiteSpace(entity.Digest)
                                        ? $"{entity.Registry}/{entity.Repository}:{entity.Tag}"
                                        : $"{entity.Registry}/{entity.Repository}:{entity.Tag}@{entity.Digest}",
                   ObservedImageCount = entity.ObservedImageCount,
                   ActiveFindingCount = _dbContext.UpdateFindings.Count(finding => finding.SubjectImageVersionId == entity.BaseImageVersionId && finding.IsActive),
               };
    }

    private string GetLatestObservedScanStatus(Guid observedImageId)
    {
        return _dbContext.ScanRuns.Where(entity => entity.ObservedImageId == observedImageId && entity.Type == ScanRunType.ObservedImage)
                                  .OrderByDescending(entity => entity.StartedAtUtc)
                                  .Select(entity => entity.Status.ToString())
                                  .FirstOrDefault() ?? "NotScanned";
    }

    private string? GetLatestObservedScanMessage(Guid observedImageId)
    {
        return _dbContext.ScanRuns.Where(entity => entity.ObservedImageId == observedImageId && entity.Type == ScanRunType.ObservedImage)
                                  .OrderByDescending(entity => entity.StartedAtUtc)
                                  .Select(entity => entity.ErrorMessage)
                                  .FirstOrDefault();
    }

    private string GetRuntimeContainerState(Guid snapshotId)
    {
        return _dbContext.UpdateFindings.Where(entity => entity.ContainerSnapshotId == snapshotId && entity.IsActive)
                                        .OrderByDescending(entity => entity.DetectedAtUtc)
                                        .Select(entity => entity.Type.ToString())
                                        .FirstOrDefault() ?? "Unknown";
    }

    private string? GetRuntimeContainerSummary(Guid snapshotId)
    {
        return _dbContext.UpdateFindings.Where(entity => entity.ContainerSnapshotId == snapshotId && entity.IsActive)
                                        .OrderByDescending(entity => entity.DetectedAtUtc)
                                        .Select(entity => entity.Summary)
                                        .FirstOrDefault();
    }

    private string GetLatestRuntimeScanStatus(Guid dockerInstanceId)
    {
        return _dbContext.ScanRuns.Where(entity => entity.DockerInstanceId == dockerInstanceId && entity.Type == ScanRunType.RuntimeContainer)
                                  .OrderByDescending(entity => entity.StartedAtUtc)
                                  .Select(entity => entity.Status.ToString())
                                  .FirstOrDefault() ?? "NotScanned";
    }

    #endregion // Methods
}