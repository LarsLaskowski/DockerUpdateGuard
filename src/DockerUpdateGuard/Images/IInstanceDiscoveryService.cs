namespace DockerUpdateGuard.Images;

/// <summary>
/// Instance discovery contract
/// </summary>
public interface IInstanceDiscoveryService
{
    #region Methods

    /// <summary>
    /// Synchronize configured instances into the database
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    Task SynchronizeConfiguredInstancesAsync(CancellationToken cancellationToken = default);

    #endregion // Methods
}