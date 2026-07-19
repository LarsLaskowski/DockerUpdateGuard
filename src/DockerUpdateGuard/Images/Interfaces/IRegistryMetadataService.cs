using DockerUpdateGuard.DockerHub;
using DockerUpdateGuard.Images.Data;
using DockerUpdateGuard.Infrastructure;

namespace DockerUpdateGuard.Images.Interfaces;

/// <summary>
/// Registry metadata orchestration contract
/// </summary>
public interface IRegistryMetadataService
{
    #region Methods

    /// <summary>
    /// Read a concrete tag metadata document
    /// </summary>
    /// <param name="imageReference">Image reference</param>
    /// <param name="operatingSystem">Preferred operating system</param>
    /// <param name="architecture">Preferred architecture</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Tag metadata result</returns>
    Task<ExternalOperationResult<DockerHubTagData>> GetTagAsync(ImageReference imageReference,
                                                                string? operatingSystem = null,
                                                                string? architecture = null,
                                                                CancellationToken cancellationToken = default);

    /// <summary>
    /// Read tags for a repository
    /// </summary>
    /// <param name="registry">Registry name</param>
    /// <param name="repository">Repository path</param>
    /// <param name="operatingSystem">Preferred operating system</param>
    /// <param name="architecture">Preferred architecture</param>
    /// <param name="queryOptions">Bounded tag-query options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Tag list result</returns>
    Task<ExternalOperationResult<IReadOnlyList<DockerHubTagData>>> GetTagsAsync(string registry,
                                                                                string repository,
                                                                                string? operatingSystem = null,
                                                                                string? architecture = null,
                                                                                RegistryTagQueryOptions? queryOptions = null,
                                                                                CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempt to resolve base images for an observed image
    /// </summary>
    /// <param name="imageReference">Image reference</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Base image result</returns>
    Task<ExternalOperationResult<IReadOnlyList<BaseImageDescriptor>>> ResolveBaseImagesAsync(ImageReference imageReference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Read reduced image configuration metadata for an image reference
    /// </summary>
    /// <param name="imageReference">Image reference</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Image configuration result</returns>
    Task<ExternalOperationResult<RegistryImageConfigurationData>> GetImageConfigurationAsync(ImageReference imageReference, CancellationToken cancellationToken = default);

    #endregion // Methods
}