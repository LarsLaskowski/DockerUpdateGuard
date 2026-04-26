using DockerUpdateGuard.DockerHub;
using DockerUpdateGuard.Infrastructure;

namespace DockerUpdateGuard.Images;

/// <summary>
/// Docker Hub backed base image resolver
/// </summary>
public class DockerHubBaseImageResolver : IBaseImageResolver
{
    #region Fields

    /// <summary>
    /// Docker Hub client
    /// </summary>
    private readonly IDockerHubClient _dockerHubClient;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="dockerHubClient">Docker Hub client</param>
    public DockerHubBaseImageResolver(IDockerHubClient dockerHubClient)
    {
        _dockerHubClient = dockerHubClient;
    }

    #endregion // Constructors

    #region Methods

    /// <inheritdoc/>
    public Task<ExternalOperationResult<IReadOnlyList<BaseImageDescriptor>>> ResolveAsync(ImageReference imageReference, CancellationToken cancellationToken = default)
    {
        return _dockerHubClient.ResolveBaseImagesAsync(imageReference, cancellationToken);
    }

    #endregion // Methods
}