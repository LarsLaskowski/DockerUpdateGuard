using DockerUpdateGuard.Data.Entities;

using Microsoft.EntityFrameworkCore;

namespace DockerUpdateGuard.Data.Queries;

/// <summary>
/// Shared definition of the currently known container snapshots: per Docker instance the snapshots of its most recent
/// runtime-container scan run that recorded snapshots, otherwise the latest snapshot of each container
/// </summary>
public static class CurrentContainerSnapshotQuery
{
    #region Static methods

    /// <summary>
    /// Resolve the currently known container snapshots of all Docker instances over the given context
    /// </summary>
    /// <param name="dbContext">Database context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Key rows of the currently known container snapshots</returns>
    public static async Task<IReadOnlyList<CurrentContainerSnapshotData>> GetCurrentContainerSnapshotsAsync(DockerUpdateGuardDbContext dbContext, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        var runtimeScanReferences = await dbContext.ContainerSnapshots.AsNoTracking()
                                                                      .Where(entity => entity.ScanRunId != null
                                                                                       && entity.ScanRun!.Type == ScanRunType.RuntimeContainer)
                                                                      .Select(entity => new
                                                                                        {
                                                                                            entity.DockerInstanceId,
                                                                                            ScanRunId = entity.ScanRunId!.Value,
                                                                                            entity.ScanRun!.StartedAtUtc,
                                                                                        })
                                                                      .Distinct()
                                                                      .ToListAsync(cancellationToken)
                                                                      .ConfigureAwait(false);

        var targetScanRunByInstance = runtimeScanReferences.GroupBy(reference => reference.DockerInstanceId)
                                                           .ToDictionary(group => group.Key,
                                                                         group => group.OrderByDescending(reference => reference.StartedAtUtc)
                                                                                       .ThenByDescending(reference => reference.ScanRunId)
                                                                                       .First()
                                                                                       .ScanRunId);

        var currentSnapshots = new List<CurrentContainerSnapshotData>();

        await AddRuntimeScanSnapshotsAsync(dbContext, targetScanRunByInstance, currentSnapshots, cancellationToken).ConfigureAwait(false);
        await AddFallbackSnapshotsAsync(dbContext, targetScanRunByInstance.Keys.ToList(), currentSnapshots, cancellationToken).ConfigureAwait(false);

        return currentSnapshots;
    }

    /// <summary>
    /// Add the snapshots of the target runtime scan run of each Docker instance
    /// </summary>
    /// <param name="dbContext">Database context</param>
    /// <param name="targetScanRunByInstance">Target runtime scan run identifier per Docker instance</param>
    /// <param name="currentSnapshots">Result list to add to</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    private static async Task AddRuntimeScanSnapshotsAsync(DockerUpdateGuardDbContext dbContext,
                                                           Dictionary<Guid, Guid> targetScanRunByInstance,
                                                           List<CurrentContainerSnapshotData> currentSnapshots,
                                                           CancellationToken cancellationToken)
    {
        if (targetScanRunByInstance.Count == 0)
        {
            return;
        }

        var targetScanRunIds = targetScanRunByInstance.Values.Select(scanRunId => (Guid?)scanRunId).ToList();
        var runtimeSnapshots = await dbContext.ContainerSnapshots.AsNoTracking()
                                                                 .Where(entity => targetScanRunIds.Contains(entity.ScanRunId))
                                                                 .Select(entity => new
                                                                                   {
                                                                                       entity.Id,
                                                                                       entity.DockerInstanceId,
                                                                                       entity.ContainerId,
                                                                                       entity.ImageVersionId,
                                                                                       entity.ScanRunId,
                                                                                   })
                                                                 .ToListAsync(cancellationToken)
                                                                 .ConfigureAwait(false);

        currentSnapshots.AddRange(runtimeSnapshots.Where(entity => targetScanRunByInstance.TryGetValue(entity.DockerInstanceId, out var scanRunId)
                                                                   && scanRunId == entity.ScanRunId)
                                                  .Select(entity => CreateData(entity.Id,
                                                                               entity.DockerInstanceId,
                                                                               entity.ContainerId,
                                                                               entity.ImageVersionId)));
    }

    /// <summary>
    /// Add the latest snapshot of each container for the Docker instances without a runtime scan snapshot
    /// </summary>
    /// <param name="dbContext">Database context</param>
    /// <param name="runtimeInstanceIds">Docker instances that have a runtime scan snapshot</param>
    /// <param name="currentSnapshots">Result list to add to</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    private static async Task AddFallbackSnapshotsAsync(DockerUpdateGuardDbContext dbContext,
                                                        List<Guid> runtimeInstanceIds,
                                                        List<CurrentContainerSnapshotData> currentSnapshots,
                                                        CancellationToken cancellationToken)
    {
        var fallbackSnapshots = await dbContext.ContainerSnapshots.AsNoTracking()
                                                                  .Where(entity => runtimeInstanceIds.Contains(entity.DockerInstanceId) == false)
                                                                  .Select(entity => new
                                                                                    {
                                                                                        entity.Id,
                                                                                        entity.DockerInstanceId,
                                                                                        entity.ContainerId,
                                                                                        entity.ImageVersionId,
                                                                                        entity.RecordedAtUtc,
                                                                                    })
                                                                  .ToListAsync(cancellationToken)
                                                                  .ConfigureAwait(false);

        currentSnapshots.AddRange(fallbackSnapshots.GroupBy(entity => (entity.DockerInstanceId, entity.ContainerId))
                                                   .Select(group => group.OrderByDescending(entity => entity.RecordedAtUtc)
                                                                         .ThenByDescending(entity => entity.Id)
                                                                         .First())
                                                   .Select(entity => CreateData(entity.Id,
                                                                                entity.DockerInstanceId,
                                                                                entity.ContainerId,
                                                                                entity.ImageVersionId)));
    }

    /// <summary>
    /// Create a result row
    /// </summary>
    /// <param name="snapshotId">Snapshot identifier</param>
    /// <param name="dockerInstanceId">Docker instance identifier</param>
    /// <param name="containerId">Container identifier</param>
    /// <param name="imageVersionId">Image version identifier</param>
    /// <returns>Result row</returns>
    private static CurrentContainerSnapshotData CreateData(Guid snapshotId,
                                                           Guid dockerInstanceId,
                                                           string containerId,
                                                           Guid imageVersionId)
    {
        return new CurrentContainerSnapshotData
               {
                   SnapshotId = snapshotId,
                   DockerInstanceId = dockerInstanceId,
                   ContainerId = containerId,
                   ImageVersionId = imageVersionId,
               };
    }

    #endregion // Static methods
}