using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Infrastructure;

namespace DockerUpdateGuard.Docker;

/// <summary>
/// Docker engine adapter contract
/// </summary>
public interface IDockerInstanceClient
{
    #region Methods

    /// <summary>
    /// Discover containers from a configured Docker instance
    /// </summary>
    /// <param name="instanceOptions">Docker instance configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Container discovery result</returns>
    Task<ExternalOperationResult<IReadOnlyList<RuntimeContainerDescriptor>>> DiscoverContainersAsync(DockerInstanceOptions instanceOptions, CancellationToken cancellationToken = default);

    #endregion // Methods
}