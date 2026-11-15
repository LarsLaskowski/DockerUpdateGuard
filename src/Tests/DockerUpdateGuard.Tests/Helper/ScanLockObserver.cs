using DockerUpdateGuard.Images.Helper;

namespace DockerUpdateGuard.Tests.Helper;

/// <summary>
/// Observes a scan lock registry to tell when a run is waiting for a key, without any real-clock wait
/// </summary>
internal static class ScanLockObserver
{
    #region Static methods

    /// <summary>
    /// Yield until a second reference of the key exists (the observed run waits behind the holder) or the run has ended on its own
    /// </summary>
    /// <param name="scanLock">Scan lock registry of the service under test</param>
    /// <param name="key">Key the run waits for</param>
    /// <param name="run">Task of the run, which ends the loop when it completes without a wait</param>
    /// <param name="cancellationToken">Cancellation token that ends the loop for a test that hangs</param>
    /// <returns>Task</returns>
    public static async Task WaitUntilWaitingAsync(KeyedScanLock scanLock, Guid key, Task run, CancellationToken cancellationToken)
    {
        while (scanLock.GetReferenceCount(key) < 2 && run.IsCompleted == false)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await Task.Yield();
        }
    }

    #endregion // Static methods
}