namespace DockerUpdateGuard.Data.Queries;

/// <summary>
/// Shared definition of the currently known container snapshots
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
        await Task.CompletedTask.ConfigureAwait(false);

        throw new InvalidOperationException("Skeleton: not implemented");
    }

    #endregion // Static methods
}