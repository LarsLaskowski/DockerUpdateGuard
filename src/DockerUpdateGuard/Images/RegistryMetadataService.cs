using DockerUpdateGuard.DockerHub;
using DockerUpdateGuard.Infrastructure;

namespace DockerUpdateGuard.Images;

/// <summary>
/// Registry metadata adapter dispatcher
/// </summary>
public class RegistryMetadataService : IRegistryMetadataService
{
    #region Fields

    /// <summary>
    /// Registered registry-metadata clients
    /// </summary>
    private readonly IReadOnlyList<IRegistryMetadataClient> _clients;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="clients">Registry metadata clients</param>
    public RegistryMetadataService(IEnumerable<IRegistryMetadataClient> clients)
    {
        _clients = clients.ToList();
    }

    #endregion // Constructors

    #region Methods

    /// <inheritdoc/>
    public Task<ExternalOperationResult<DockerHubTagData>> GetTagAsync(ImageReference imageReference,
                                                                       CancellationToken cancellationToken = default,
                                                                       string? operatingSystem = null,
                                                                       string? architecture = null)
    {
        ArgumentNullException.ThrowIfNull(imageReference);

        if (TryGetClient(imageReference.Registry, out var client) == false)
        {
            return Task.FromResult(ExternalOperationResult<DockerHubTagData>.Unsupported(CreateUnsupportedMessage(imageReference.Registry)));
        }

        return client.GetTagAsync(imageReference, cancellationToken, operatingSystem, architecture);
    }

    /// <inheritdoc/>
    public Task<ExternalOperationResult<IReadOnlyList<DockerHubTagData>>> GetTagsAsync(string registry,
                                                                                       string repository,
                                                                                       CancellationToken cancellationToken = default,
                                                                                       string? operatingSystem = null,
                                                                                       string? architecture = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);

        if (TryGetClient(registry, out var client) == false)
        {
            return Task.FromResult(ExternalOperationResult<IReadOnlyList<DockerHubTagData>>.Unsupported(CreateUnsupportedMessage(registry)));
        }

        return client.GetTagsAsync(registry, repository, cancellationToken, operatingSystem, architecture);
    }

    /// <inheritdoc/>
    public Task<ExternalOperationResult<IReadOnlyList<BaseImageDescriptor>>> ResolveBaseImagesAsync(ImageReference imageReference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageReference);

        if (TryGetClient(imageReference.Registry, out var client) == false)
        {
            return Task.FromResult(ExternalOperationResult<IReadOnlyList<BaseImageDescriptor>>.Unsupported(CreateUnsupportedMessage(imageReference.Registry)));
        }

        return client.ResolveBaseImagesAsync(imageReference, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<ExternalOperationResult<RegistryImageConfigurationData>> GetImageConfigurationAsync(ImageReference imageReference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageReference);

        if (TryGetClient(imageReference.Registry, out var client) == false)
        {
            return Task.FromResult(ExternalOperationResult<RegistryImageConfigurationData>.Unsupported(CreateUnsupportedMessage(imageReference.Registry)));
        }

        return client.GetImageConfigurationAsync(imageReference, cancellationToken);
    }

    /// <summary>
    /// Create an unsupported registry message
    /// </summary>
    /// <param name="registry">Registry host</param>
    /// <returns>Unsupported registry message</returns>
    private static string CreateUnsupportedMessage(string registry)
    {
        return $"Registry '{registry}' is not supported by the current registry adapters";
    }

    /// <summary>
    /// Resolve the matching client for a registry
    /// </summary>
    /// <param name="registry">Registry host</param>
    /// <param name="client">Resolved client</param>
    /// <returns>True when a client was found</returns>
    private bool TryGetClient(string registry, out IRegistryMetadataClient client)
    {
        client = _clients.FirstOrDefault(entity => entity.CanHandle(registry)) ?? NullRegistryMetadataClient.Instance;

        return ReferenceEquals(client, NullRegistryMetadataClient.Instance) == false;
    }

    #endregion // Methods

    #region Helper types

    /// <summary>
    /// Null object adapter used when no registry client matches
    /// </summary>
    private sealed class NullRegistryMetadataClient : IRegistryMetadataClient
    {
        /// <summary>
        /// Singleton instance
        /// </summary>
        internal static readonly NullRegistryMetadataClient Instance = new();

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
                                                                                           string? architecture = null)
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
    }

    #endregion // Helper types
}