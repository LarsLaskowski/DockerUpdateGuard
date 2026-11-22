using DockerUpdateGuard.Data.Entities;

namespace DockerUpdateGuard.Images;

/// <summary>
/// Completes scan runs with a final status
/// </summary>
internal static class ScanRunCompletion
{
    #region Methods

    /// <summary>
    /// Complete a scan run with its final status and joined status messages
    /// </summary>
    /// <param name="scanRun">Scan run to complete</param>
    /// <param name="finalStatus">Final status</param>
    /// <param name="statusMessages">Collected status messages</param>
    internal static void Complete(ScanRun scanRun, ScanRunStatus finalStatus, List<string> statusMessages)
    {
        throw new InvalidOperationException("Skeleton: not implemented");
    }

    #endregion // Methods
}