using DockerUpdateGuard.DockerHub;
using DockerUpdateGuard.Infrastructure;

namespace DockerUpdateGuard.Images;

/// <summary>
/// Registry-backed base image resolver
/// </summary>
public class RegistryBaseImageResolver : IBaseImageResolver
{
    #region Fields

    private readonly IRegistryMetadataService _registryMetadataService;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="registryMetadataService">Registry metadata service</param>
    public RegistryBaseImageResolver(IRegistryMetadataService registryMetadataService)
    {
        _registryMetadataService = registryMetadataService;
    }

    #endregion // Constructors

    #region Methods

    /// <inheritdoc/>
    public Task<ExternalOperationResult<IReadOnlyList<BaseImageDescriptor>>> ResolveAsync(ImageReference imageReference, CancellationToken cancellationToken = default)
    {
        return _registryMetadataService.ResolveBaseImagesAsync(imageReference, cancellationToken);
    }

    #endregion // Methods
}