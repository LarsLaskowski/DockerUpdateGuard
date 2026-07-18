using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Data.Repositories;

namespace DockerUpdateGuard.Tests.Helper;

/// <summary>
/// Test repository for registration scenarios that do not need full catalog persistence behavior
/// </summary>
internal sealed class TestImageCatalogRepository : IImageCatalogRepository
{
    #region Fields

    private readonly ImageVersion _imageVersion;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="imageVersion">Image version to return</param>
    public TestImageCatalogRepository(ImageVersion imageVersion)
    {
        _imageVersion = imageVersion;
    }

    #endregion // Constructors

    #region IImageCatalogRepository

    /// <inheritdoc/>
    public Task<ImageVersion?> FindImageVersionAsync(string registry,
                                                     string repository,
                                                     string tag,
                                                     string? digest,
                                                     CancellationToken cancellationToken = default)
    {
        return Task.FromResult<ImageVersion?>(_imageVersion);
    }

    /// <inheritdoc/>
    public Task<RegistryRepository> GetOrCreateRegistryRepositoryAsync(string registry,
                                                                       string repository,
                                                                       CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(_imageVersion.RegistryRepository);

        return Task.FromResult(_imageVersion.RegistryRepository);
    }

    /// <inheritdoc/>
    public Task<ImageVersion> GetOrCreateImageVersionAsync(string registry,
                                                           string repository,
                                                           string tag,
                                                           string? digest,
                                                           DateTimeOffset? publishedAtUtc = null,
                                                           string? metadataJson = null,
                                                           CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_imageVersion);
    }

    /// <inheritdoc/>
    public Task<ObservedImage> AddObservedImageAsync(ObservedImage observedImage, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(observedImage);
    }

    /// <inheritdoc/>
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(0);
    }

    #endregion // IImageCatalogRepository
}