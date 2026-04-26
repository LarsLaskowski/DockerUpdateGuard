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

    /// <summary>
    /// Collect resource usage samples for running containers from a configured Docker instance
    /// </summary>
    /// <param name="instanceOptions">Docker instance configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Container resource sample result</returns>
    Task<ExternalOperationResult<IReadOnlyList<RuntimeContainerResourceDescriptor>>> CollectContainerResourceUsageAsync(DockerInstanceOptions instanceOptions, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inspect a local image from a configured Docker instance
    /// </summary>
    /// <param name="instanceOptions">Docker instance configuration</param>
    /// <param name="imageReferenceOrId">Image reference or local image identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Image inspect result</returns>
    Task<ExternalOperationResult<DockerImageInspectData>> InspectImageAsync(DockerInstanceOptions instanceOptions, string imageReferenceOrId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Read history for a local image from a configured Docker instance
    /// </summary>
    /// <param name="instanceOptions">Docker instance configuration</param>
    /// <param name="imageReferenceOrId">Image reference or local image identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Image history result</returns>
    Task<ExternalOperationResult<IReadOnlyList<DockerImageHistoryEntryData>>> GetImageHistoryAsync(DockerInstanceOptions instanceOptions, string imageReferenceOrId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Read the total host memory reported by a configured Docker instance
    /// </summary>
    /// <param name="instanceOptions">Docker instance configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Total host memory in bytes</returns>
    Task<ExternalOperationResult<long>> GetHostMemoryTotalAsync(DockerInstanceOptions instanceOptions, CancellationToken cancellationToken = default);

    #endregion // Methods
}