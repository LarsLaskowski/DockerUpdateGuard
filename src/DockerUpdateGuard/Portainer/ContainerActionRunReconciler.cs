using DockerUpdateGuard.Data;

namespace DockerUpdateGuard.Portainer;

/// <summary>
/// Reconciles container action runs interrupted by a restart
/// </summary>
public static class ContainerActionRunReconciler
{
    #region Static methods

    /// <summary>
    /// Mark runs that were still in progress at startup as interrupted
    /// </summary>
    /// <param name="dbContext">Database context</param>
    /// <param name="timeProvider">Time provider</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of reconciled runs</returns>
    public static Task<int> ReconcileInterruptedRunsAsync(DockerUpdateGuardDbContext dbContext, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("Skeleton: not implemented");
    }

    #endregion // Static methods
}