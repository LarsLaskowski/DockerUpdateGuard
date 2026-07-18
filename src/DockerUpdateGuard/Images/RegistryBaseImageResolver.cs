using DockerUpdateGuard.DockerHub;
using DockerUpdateGuard.Images.Data;
using DockerUpdateGuard.Images.Interfaces;
using DockerUpdateGuard.Infrastructure;

namespace DockerUpdateGuard.Images;

/// <summary>
/// Registry-backed base image resolver
/// </summary>
public class RegistryBaseImageResolver : IBaseImageResolver
{
    #region Constants

    /// <summary>
    /// Maximum supported base-image depth
    /// </summary>
    private const int MaxBaseImageDepth = 5;

    /// <summary>
    /// OCI base-image name label
    /// </summary>
    private const string OciBaseImageNameLabel = "org.opencontainers.image.base.name";

    /// <summary>
    /// OCI base-image digest label
    /// </summary>
    private const string OciBaseImageDigestLabel = "org.opencontainers.image.base.digest";

    #endregion // Constants

    #region Fields

    /// <summary>
    /// Registry-metadata service
    /// </summary>
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

    #region Static methods

    /// <summary>
    /// Map an image-configuration failure into a base-image resolution result
    /// </summary>
    /// <param name="imageReference">Image reference</param>
    /// <param name="status">Operation status</param>
    /// <param name="message">Optional status message</param>
    /// <returns>Mapped base-image resolution result</returns>
    private static ExternalOperationResult<IReadOnlyList<BaseImageDescriptor>> MapConfigurationFailure(ImageReference imageReference,
                                                                                                       ExternalOperationStatus status,
                                                                                                       string? message)
    {
        var fallbackMessage = message ?? $"Base image resolution failed for '{imageReference.FullReference}'";

        return status switch
               {
                   ExternalOperationStatus.Unsupported => ExternalOperationResult<IReadOnlyList<BaseImageDescriptor>>.Unsupported(fallbackMessage),
                   ExternalOperationStatus.NotFound => ExternalOperationResult<IReadOnlyList<BaseImageDescriptor>>.NotFound(fallbackMessage),
                   ExternalOperationStatus.NotConfigured => ExternalOperationResult<IReadOnlyList<BaseImageDescriptor>>.NotConfigured(fallbackMessage),
                   ExternalOperationStatus.Unknown => ExternalOperationResult<IReadOnlyList<BaseImageDescriptor>>.Unknown(fallbackMessage),
                   _ => ExternalOperationResult<IReadOnlyList<BaseImageDescriptor>>.Failed(fallbackMessage),
               };
    }

    /// <summary>
    /// Parse the direct base-image reference from registry configuration metadata
    /// </summary>
    /// <param name="imageConfiguration">Registry image configuration</param>
    /// <returns>Parsed base-image reference or null when no OCI base-image labels exist</returns>
    private static ImageReference? TryParseBaseImageReference(RegistryImageConfigurationData imageConfiguration)
    {
        if (imageConfiguration.Labels.TryGetValue(OciBaseImageNameLabel, out var baseImageName) == false
            || string.IsNullOrWhiteSpace(baseImageName))
        {
            return null;
        }

        var parser = new ImageReferenceParser();

        var parsedReference = parser.Parse(baseImageName);

        if (string.IsNullOrWhiteSpace(parsedReference.Digest)
            && imageConfiguration.Labels.TryGetValue(OciBaseImageDigestLabel, out var baseImageDigest)
            && string.IsNullOrWhiteSpace(baseImageDigest) == false)
        {
            parsedReference.Digest = ImageReferenceParser.NormalizeDigest(baseImageDigest);
        }

        return parsedReference;
    }

    #endregion // Static methods

    #region Methods

    /// <summary>
    /// Append discovered base images recursively using registry-aware configuration lookup
    /// </summary>
    /// <param name="imageReference">Current image reference</param>
    /// <param name="imageConfiguration">Current image configuration</param>
    /// <param name="results">Accumulated base-image results</param>
    /// <param name="depth">Current base-image depth</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    private async Task AppendBaseImagesAsync(ImageReference imageReference,
                                             RegistryImageConfigurationData imageConfiguration,
                                             List<BaseImageDescriptor> results,
                                             int depth,
                                             CancellationToken cancellationToken)
    {
        if (depth > MaxBaseImageDepth)
        {
            return;
        }

        var baseImageReference = TryParseBaseImageReference(imageConfiguration);

        if (baseImageReference is null)
        {
            return;
        }

        results.Add(new BaseImageDescriptor
                    {
                        Registry = baseImageReference.Registry,
                        Repository = baseImageReference.Repository,
                        Tag = baseImageReference.Tag,
                        Digest = baseImageReference.Digest,
                        Depth = depth,
                        SourceReference = imageReference.FullReference,
                    });

        if (depth == MaxBaseImageDepth)
        {
            return;
        }

        var baseImageConfigurationResult = await _registryMetadataService.GetImageConfigurationAsync(baseImageReference, cancellationToken)
                                                                         .ConfigureAwait(false);

        if (baseImageConfigurationResult.Status != ExternalOperationStatus.Succeeded
            || baseImageConfigurationResult.Data is null)
        {
            return;
        }

        await AppendBaseImagesAsync(baseImageReference,
                                    baseImageConfigurationResult.Data,
                                    results,
                                    depth + 1,
                                    cancellationToken).ConfigureAwait(false);
    }

    #endregion // Methods

    #region IBaseImageResolver

    /// <inheritdoc/>
    public async Task<ExternalOperationResult<IReadOnlyList<BaseImageDescriptor>>> ResolveAsync(ImageReference imageReference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageReference);

        var imageConfigurationResult = await _registryMetadataService.GetImageConfigurationAsync(imageReference, cancellationToken)
                                                                     .ConfigureAwait(false);

        if (imageConfigurationResult.Status != ExternalOperationStatus.Succeeded || imageConfigurationResult.Data is null)
        {
            return MapConfigurationFailure(imageReference,
                                           imageConfigurationResult.Status,
                                           imageConfigurationResult.Message);
        }

        var results = new List<BaseImageDescriptor>();

        await AppendBaseImagesAsync(imageReference,
                                    imageConfigurationResult.Data,
                                    results,
                                    depth: 1,
                                    cancellationToken).ConfigureAwait(false);

        return ExternalOperationResult<IReadOnlyList<BaseImageDescriptor>>.Succeeded(results);
    }

    #endregion // IBaseImageResolver
}