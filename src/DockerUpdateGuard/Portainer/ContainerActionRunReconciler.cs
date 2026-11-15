using DockerUpdateGuard.Data;
using DockerUpdateGuard.Data.Entities;

using Microsoft.EntityFrameworkCore;

namespace DockerUpdateGuard.Portainer;

/// <summary>
/// Reconciles container action runs interrupted by a restart
/// </summary>
public static class ContainerActionRunReconciler
{
    #region Constants

    /// <summary>
    /// Message of a run that was still in progress when the application stopped
    /// </summary>
    internal const string InterruptedMessage = "Interrupted: the application stopped before the action completed";

    #endregion // Constants

    #region Static methods

    /// <summary>
    /// Mark runs that were still in progress at startup as interrupted
    /// </summary>
    /// <param name="dbContext">Database context</param>
    /// <param name="timeProvider">Time provider</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of reconciled runs</returns>
    public static async Task<int> ReconcileInterruptedRunsAsync(DockerUpdateGuardDbContext dbContext, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var openRuns = await dbContext.ContainerActionRuns.Where(run => run.Status == ContainerActionStatus.Requested || run.Status == ContainerActionStatus.Running)
                                                          .ToListAsync(cancellationToken)
                                                          .ConfigureAwait(false);

        if (openRuns.Count == 0)
        {
            return 0;
        }

        var completedAtUtc = timeProvider.GetUtcNow();

        foreach (var run in openRuns)
        {
            run.Status = ContainerActionStatus.Failed;
            run.ErrorMessage = InterruptedMessage;
            run.CompletedAtUtc = completedAtUtc;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return openRuns.Count;
    }

    #endregion // Static methods
}