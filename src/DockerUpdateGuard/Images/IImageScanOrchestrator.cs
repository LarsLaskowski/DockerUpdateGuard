using DockerUpdateGuard.Data.Entities;

namespace DockerUpdateGuard.Images;

/// <summary>
/// Observed image scan orchestrator contract
/// </summary>
public interface IImageScanOrchestrator
{
    #region Methods

    /// <summary>
    /// Scan all enabled observed images
    /// </summary>
    /// <param name="triggerSource">Trigger source</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    Task ScanAllAsync(ScanTriggerSource triggerSource, CancellationToken cancellationToken = default);

    /// <summary>
    /// Scan one observed image
    /// </summary>
    /// <param name="observedImageId">Observed image identifier</param>
    /// <param name="triggerSource">Trigger source</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    Task ScanAsync(Guid observedImageId,
                   ScanTriggerSource triggerSource,
                   CancellationToken cancellationToken = default);

    #endregion // Methods
}