using DockerUpdateGuard.Images;
using DockerUpdateGuard.Infrastructure;

namespace DockerUpdateGuard.DockerHub;

/// <summary>
/// Docker Hub adapter contract
/// </summary>
public interface IDockerHubClient
{
    #region Methods

    /// <summary>
    /// Read the current authenticated Docker Hub user
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Authenticated user result</returns>
    Task<ExternalOperationResult<DockerHubAuthenticatedUserData>> GetCurrentUserAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Read repositories for a Docker Hub account
    /// </summary>
    /// <param name="accountName">Docker Hub account name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Repository list result</returns>
    Task<ExternalOperationResult<IReadOnlyList<DockerHubRepositoryData>>> GetRepositoriesAsync(string accountName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Read repository metadata
    /// </summary>
    /// <param name="imageReference">Image reference</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Repository metadata result</returns>
    Task<ExternalOperationResult<DockerHubRepositoryData>> GetRepositoryAsync(ImageReference imageReference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Read a concrete tag metadata document
    /// </summary>
    /// <param name="imageReference">Image reference</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Tag metadata result</returns>
    Task<ExternalOperationResult<DockerHubTagData>> GetTagAsync(ImageReference imageReference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Read tags for a repository
    /// </summary>
    /// <param name="registry">Registry name</param>
    /// <param name="repository">Repository path</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Tag list result</returns>
    Task<ExternalOperationResult<IReadOnlyList<DockerHubTagData>>> GetTagsAsync(string registry,
                                                                                 string repository,
                                                                                 CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempt to resolve base images for an observed image
    /// </summary>
    /// <param name="imageReference">Image reference</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Base image result</returns>
    Task<ExternalOperationResult<IReadOnlyList<BaseImageDescriptor>>> ResolveBaseImagesAsync(ImageReference imageReference, CancellationToken cancellationToken = default);

    #endregion // Methods
}