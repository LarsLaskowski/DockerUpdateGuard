using DockerUpdateGuard.Data.Entities;

using Microsoft.EntityFrameworkCore;

namespace DockerUpdateGuard.Data.Queries;

/// <summary>
/// Query service that resolves the image versions currently relevant to the fleet
/// </summary>
public class LiveImageInventoryQueryService : ILiveImageInventoryQueryService
{
    #region Fields

    /// <summary>
    /// DB context object
    /// </summary>
    private readonly DockerUpdateGuardDbContext _dbContext;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="dbContext">Database context</param>
    public LiveImageInventoryQueryService(DockerUpdateGuardDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    #endregion // Constructors

    #region Methods

    /// <summary>
    /// Resolve the image versions referenced by the most recent runtime-container scan of each Docker instance
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Identifiers of the currently running image versions</returns>
    private async Task<HashSet<Guid>> GetRunningImageVersionIdsAsync(CancellationToken cancellationToken)
    {
        var snapshots = await _dbContext.ContainerSnapshots.AsNoTracking()
                                                           .Select(entity => new
                                                                             {
                                                                                 entity.DockerInstanceId,
                                                                                 entity.ContainerId,
                                                                                 entity.ImageVersionId,
                                                                                 entity.ScanRunId,
                                                                                 entity.RecordedAtUtc,
                                                                                 ScanRunType = entity.ScanRun != null ? entity.ScanRun.Type : (ScanRunType?)null,
                                                                                 ScanRunStartedAtUtc = entity.ScanRun != null ? entity.ScanRun.StartedAtUtc : (DateTimeOffset?)null,
                                                                             })
                                                           .ToListAsync(cancellationToken)
                                                           .ConfigureAwait(false);

        var runningImageVersionIds = new HashSet<Guid>();

        foreach (var instanceGroup in snapshots.GroupBy(entity => entity.DockerInstanceId))
        {
            var latestRuntimeScanSnapshots = instanceGroup.Where(entity => entity.ScanRunType == ScanRunType.RuntimeContainer)
                                                          .GroupBy(entity => entity.ScanRunId)
                                                          .OrderByDescending(group => group.Max(item => item.ScanRunStartedAtUtc ?? item.RecordedAtUtc))
                                                          .FirstOrDefault();

            if (latestRuntimeScanSnapshots is not null)
            {
                runningImageVersionIds.UnionWith(latestRuntimeScanSnapshots.Select(entity => entity.ImageVersionId));

                continue;
            }

            var latestSnapshotPerContainer = instanceGroup.GroupBy(entity => entity.ContainerId)
                                                          .Select(group => group.OrderByDescending(item => item.RecordedAtUtc)
                                                                                .First());

            runningImageVersionIds.UnionWith(latestSnapshotPerContainer.Select(entity => entity.ImageVersionId));
        }

        return runningImageVersionIds;
    }

    #endregion // Methods

    #region ILiveImageInventoryQueryService

    /// <inheritdoc/>
    public async Task<IReadOnlySet<Guid>> GetLiveImageVersionIdsAsync(CancellationToken cancellationToken = default)
    {
        var observedImageVersionIds = await _dbContext.ObservedImages.Select(entity => entity.CurrentImageVersionId)
                                                                     .ToListAsync(cancellationToken)
                                                                     .ConfigureAwait(false);
        var runningImageVersionIds = await GetRunningImageVersionIdsAsync(cancellationToken).ConfigureAwait(false);
        var liveImageVersionIds = new HashSet<Guid>(observedImageVersionIds);

        liveImageVersionIds.UnionWith(runningImageVersionIds);

        var childImageVersionIds = liveImageVersionIds.ToList();
        var baseImageVersionIds = await _dbContext.ImageRelationships.Where(entity => entity.RelationshipType == ImageRelationshipType.BaseImage
                                                                                      && childImageVersionIds.Contains(entity.ChildImageVersionId))
                                                                     .Select(entity => entity.BaseImageVersionId)
                                                                     .ToListAsync(cancellationToken)
                                                                     .ConfigureAwait(false);

        liveImageVersionIds.UnionWith(baseImageVersionIds);

        return liveImageVersionIds;
    }

    #endregion // ILiveImageInventoryQueryService
}