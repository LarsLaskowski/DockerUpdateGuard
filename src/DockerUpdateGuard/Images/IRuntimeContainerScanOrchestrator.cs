using DockerUpdateGuard.Data.Entities;

namespace DockerUpdateGuard.Images;

/// <summary>
/// Runtime container scan orchestrator contract
/// </summary>
public interface IRuntimeContainerScanOrchestrator
{
    #region Methods

    /// <summary>
    /// Scan all enabled Docker instances
    /// </summary>
    /// <param name="triggerSource">Trigger source</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    Task ScanAllAsync(ScanTriggerSource triggerSource, CancellationToken cancellationToken = default);

    #endregion // Methods
}