using DockerUpdateGuard.DockerHub;
using DockerUpdateGuard.Infrastructure;

namespace DockerUpdateGuard.Images;

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
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="operatingSystem">Preferred operating system</param>
    /// <param name="architecture">Preferred architecture</param>
    /// <returns>Tag metadata result</returns>
    Task<ExternalOperationResult<DockerHubTagData>> GetTagAsync(ImageReference imageReference,
                                                                CancellationToken cancellationToken = default,
                                                                string? operatingSystem = null,
                                                                string? architecture = null);

    /// <summary>
    /// Read tags for a repository
    /// </summary>
    /// <param name="registry">Registry name</param>
    /// <param name="repository">Repository path</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="operatingSystem">Preferred operating system</param>
    /// <param name="architecture">Preferred architecture</param>
    /// <returns>Tag list result</returns>
    Task<ExternalOperationResult<IReadOnlyList<DockerHubTagData>>> GetTagsAsync(string registry,
                                                                                string repository,
                                                                                CancellationToken cancellationToken = default,
                                                                                string? operatingSystem = null,
                                                                                string? architecture = null);

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