namespace DockerUpdateGuard.Images.Interfaces;

/// <summary>
/// Docker Hub account image discovery contract
/// </summary>
public interface IDockerHubAccountImageDiscoveryService
{
    #region Methods

    /// <summary>
    /// Synchronize Docker Hub account repositories into observed images
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    Task SynchronizeAccountImagesAsync(CancellationToken cancellationToken = default);

    #endregion // Methods
}