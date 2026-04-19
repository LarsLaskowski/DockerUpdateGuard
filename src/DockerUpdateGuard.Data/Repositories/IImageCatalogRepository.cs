using DockerUpdateGuard.Data.Entities;

namespace DockerUpdateGuard.Data.Repositories;

/// <summary>
/// Repository for normalized image catalog data
/// </summary>
public interface IImageCatalogRepository
{
    #region Methods

    /// <summary>
    /// Find an image version by normalized coordinates
    /// </summary>
    /// <param name="registry">Registry name</param>
    /// <param name="repository">Repository path</param>
    /// <param name="tag">Tag</param>
    /// <param name="digest">Optional digest</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Image version or null</returns>
    Task<ImageVersion?> FindImageVersionAsync(string registry,
                                              string repository,
                                              string tag,
                                              string? digest,
                                              CancellationToken cancellationToken = default);

    /// <summary>
    /// Get an existing repository or create it
    /// </summary>
    /// <param name="registry">Registry name</param>
    /// <param name="repository">Repository path</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Normalized repository</returns>
    Task<RegistryRepository> GetOrCreateRegistryRepositoryAsync(string registry,
                                                                string repository,
                                                                CancellationToken cancellationToken = default);

    /// <summary>
    /// Get an existing image version or create it
    /// </summary>
    /// <param name="registry">Registry name</param>
    /// <param name="repository">Repository path</param>
    /// <param name="tag">Tag</param>
    /// <param name="digest">Optional digest</param>
    /// <param name="publishedAtUtc">Optional publication time</param>
    /// <param name="metadataJson">Optional metadata payload</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Normalized image version</returns>
    Task<ImageVersion> GetOrCreateImageVersionAsync(string registry,
                                                    string repository,
                                                    string tag,
                                                    string? digest,
                                                    DateTimeOffset? publishedAtUtc = null,
                                                    string? metadataJson = null,
                                                    CancellationToken cancellationToken = default);

    /// <summary>
    /// Add a new observed image
    /// </summary>
    /// <param name="observedImage">Observed image to add</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Persisted observed image</returns>
    Task<ObservedImage> AddObservedImageAsync(ObservedImage observedImage, CancellationToken cancellationToken = default);

    /// <summary>
    /// Save pending changes
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of written rows</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    #endregion // Methods
}