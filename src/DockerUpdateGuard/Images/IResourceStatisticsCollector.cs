namespace DockerUpdateGuard.Images;

/// <summary>
/// Resource statistics collection contract
/// </summary>
public interface IResourceStatisticsCollector
{
    #region Methods

    /// <summary>
    /// Collect Docker-instance and runtime-container resource usage samples
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    Task CollectAsync(CancellationToken cancellationToken = default);

    #endregion // Methods
}