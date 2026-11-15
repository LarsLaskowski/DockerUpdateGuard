using DockerUpdateGuard.Data;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Infrastructure;

using Microsoft.EntityFrameworkCore;

namespace DockerUpdateGuard.Images;

/// <summary>
/// Saves the pending changes of a scan and, when that fails, stores the scan run alone as failed
/// </summary>
internal static class ScanRunSaver
{
    #region Static methods

    /// <summary>
    /// Save the pending changes of the scan; when the save fails, discard everything the context tracks and save the
    /// scan run alone as failed, then rethrow the original exception
    /// </summary>
    /// <param name="dbContext">Database context</param>
    /// <param name="scanRun">Scan run</param>
    /// <param name="isRunStored">Is the run already stored?</param>
    /// <param name="statusMessages">Status messages gathered so far</param>
    /// <param name="messagePrefix">Prefix of the status message that reports the failed save</param>
    /// <param name="onRecoveryFailed">Callback that logs a failed recovery save</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    public static async Task SaveAsync(DockerUpdateGuardDbContext dbContext,
                                       ScanRun scanRun,
                                       bool isRunStored,
                                       List<string> statusMessages,
                                       string messagePrefix,
                                       Action<Exception> onRecoveryFailed,
                                       CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken)
                           .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            dbContext.ChangeTracker.Clear();

            statusMessages.Add($"{messagePrefix}: {FailureReason.FromException(exception, null, cancellationToken)}");

            scanRun.Status = ScanRunStatus.Failed;
            scanRun.CompletedAtUtc = DateTimeOffset.UtcNow;
            scanRun.ErrorMessage = string.Join(Environment.NewLine, statusMessages.Distinct());

            try
            {
                dbContext.Entry(scanRun).State = isRunStored ? EntityState.Modified : EntityState.Added;

                await dbContext.SaveChangesAsync(cancellationToken)
                               .ConfigureAwait(false);
            }
            catch (Exception recoveryException)
            {
                onRecoveryFailed(recoveryException);

                dbContext.ChangeTracker.Clear();
            }

            throw;
        }
    }

    #endregion // Static methods
}