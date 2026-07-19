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
    #region Fields

    /// <summary>
    /// Singleton instance
    /// </summary>
    internal static readonly NullRegistryMetadataClient Instance = new();

    #endregion // Fields

    #region IRegistryMetadataClient

    /// <inheritdoc/>
    public bool CanHandle(string registry)
    {
        return false;
    }

    /// <inheritdoc/>
    public Task<ExternalOperationResult<DockerHubTagData>> GetTagAsync(ImageReference imageReference,
                                                                       string? operatingSystem = null,
                                                                       string? architecture = null,
                                                                       CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public Task<ExternalOperationResult<IReadOnlyList<DockerHubTagData>>> GetTagsAsync(string registry,
                                                                                       string repository,
                                                                                       string? operatingSystem = null,
                                                                                       string? architecture = null,
                                                                                       RegistryTagQueryOptions? queryOptions = null,
                                                                                       CancellationToken cancellationToken = default)
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

    #endregion // IRegistryMetadataClient
}