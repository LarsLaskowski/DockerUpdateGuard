using DockerUpdateGuard.DockerHub;
using DockerUpdateGuard.Images.Data;
using DockerUpdateGuard.Images.Interfaces;
using DockerUpdateGuard.Infrastructure;

namespace DockerUpdateGuard.Images;

/// <summary>
/// Null object adapter used when no registry client matches
/// </summary>
internal sealed class NullRegistryMetadataClient : IRegistryMetadataClient
{
    #region Properties

    /// <summary>
    /// Singleton instance
    /// </summary>
    internal static readonly NullRegistryMetadataClient Instance = new();

    #endregion // Properties

    #region Methods

    /// <inheritdoc/>
    public bool CanHandle(string registry)
    {
        return false;
    }

    /// <inheritdoc/>
    public Task<ExternalOperationResult<DockerHubTagData>> GetTagAsync(ImageReference imageReference,
                                                                       CancellationToken cancellationToken = default,
                                                                       string? operatingSystem = null,
                                                                       string? architecture = null)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public Task<ExternalOperationResult<IReadOnlyList<DockerHubTagData>>> GetTagsAsync(string registry,
                                                                                       string repository,
                                                                                       CancellationToken cancellationToken = default,
                                                                                       string? operatingSystem = null,
                                                                                       string? architecture = null,
                                                                                       RegistryTagQueryOptions? queryOptions = null)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public Task<ExternalOperationResult<IReadOnlyList<BaseImageDescriptor>>> ResolveBaseImagesAsync(ImageReference imageReference, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public Task<ExternalOperationResult<RegistryImageConfigurationData>> GetImageConfigurationAsync(ImageReference imageReference, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    #endregion // Methods
}