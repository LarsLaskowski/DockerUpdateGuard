using DockerUpdateGuard.Data.Entities;

using Microsoft.EntityFrameworkCore;

namespace DockerUpdateGuard.Data.Repositories;

/// <summary>
/// Repository for normalized image catalog data
/// </summary>
public class ImageCatalogRepository : IImageCatalogRepository
{
    #region Fields

    private readonly DockerUpdateGuardDbContext _dbContext;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="dbContext">Database context</param>
    public ImageCatalogRepository(DockerUpdateGuardDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    #endregion // Constructors

    #region Methods

    /// <inheritdoc/>
    public async Task<ImageVersion?> FindImageVersionAsync(string registry,
                                                           string repository,
                                                           string tag,
                                                           string? digest,
                                                           CancellationToken cancellationToken = default)
    {
        ValidateRegistryRepository(registry, repository);
        ValidateTag(tag);
        var normalizedDigest = NormalizeDigest(digest);

        return await _dbContext.ImageVersions
                               .Include(entity => entity.RegistryRepository)
                               .SingleOrDefaultAsync(entity => entity.RegistryRepository.Registry == registry
                                                               && entity.RegistryRepository.Repository == repository
                                                               && entity.Tag == tag
                                                               && entity.Digest == normalizedDigest,
                                                     cancellationToken)
                               .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<RegistryRepository> GetOrCreateRegistryRepositoryAsync(string registry,
                                                                             string repository,
                                                                             CancellationToken cancellationToken = default)
    {
        ValidateRegistryRepository(registry, repository);

        var existingRepository = await _dbContext.RegistryRepositories
                                                 .SingleOrDefaultAsync(entity => entity.Registry == registry
                                                                                 && entity.Repository == repository,
                                                                       cancellationToken)
                                                 .ConfigureAwait(false);

        if (existingRepository is not null)
        {
            return existingRepository;
        }

        var now = DateTimeOffset.UtcNow;
        var newRepository = new RegistryRepository
                            {
                                Registry = registry,
                                Repository = repository,
                                CreatedAtUtc = now,
                                UpdatedAtUtc = now,
                            };

        _dbContext.RegistryRepositories.Add(newRepository);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken)
                            .ConfigureAwait(false);

            return newRepository;
        }
        catch (DbUpdateException)
        {
            _dbContext.Entry(newRepository).State = EntityState.Detached;

            return await _dbContext.RegistryRepositories
                                   .SingleAsync(entity => entity.Registry == registry
                                                          && entity.Repository == repository,
                                                cancellationToken)
                                   .ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task<ImageVersion> GetOrCreateImageVersionAsync(string registry,
                                                                 string repository,
                                                                 string tag,
                                                                 string? digest,
                                                                 DateTimeOffset? publishedAtUtc = null,
                                                                 string? metadataJson = null,
                                                                 CancellationToken cancellationToken = default)
    {
        ValidateRegistryRepository(registry, repository);
        ValidateTag(tag);
        var normalizedDigest = NormalizeDigest(digest);

        var existingVersion = await FindImageVersionAsync(registry,
                                                          repository,
                                                          tag,
                                                          normalizedDigest,
                                                          cancellationToken).ConfigureAwait(false);

        if (existingVersion is not null)
        {
            return existingVersion;
        }

        var registryRepository = await GetOrCreateRegistryRepositoryAsync(registry,
                                                                          repository,
                                                                          cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        var newVersion = new ImageVersion
                         {
                             RegistryRepositoryId = registryRepository.Id,
                             RegistryRepository = registryRepository,
                             Tag = tag,
                             Digest = normalizedDigest,
                             PublishedAtUtc = publishedAtUtc,
                             MetadataJson = metadataJson,
                             CreatedAtUtc = now,
                             UpdatedAtUtc = now,
                         };

        _dbContext.ImageVersions.Add(newVersion);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken)
                            .ConfigureAwait(false);

            return newVersion;
        }
        catch (DbUpdateException)
        {
            _dbContext.Entry(newVersion).State = EntityState.Detached;

            return await _dbContext.ImageVersions
                                   .Include(entity => entity.RegistryRepository)
                                   .SingleAsync(entity => entity.RegistryRepositoryId == registryRepository.Id
                                                          && entity.Tag == tag
                                                          && entity.Digest == normalizedDigest,
                                                cancellationToken)
                                   .ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task<ObservedImage> AddObservedImageAsync(ObservedImage observedImage, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observedImage);

        _dbContext.ObservedImages.Add(observedImage);

        await _dbContext.SaveChangesAsync(cancellationToken)
                        .ConfigureAwait(false);

        return observedImage;
    }

    /// <inheritdoc/>
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Validate registry and repository values
    /// </summary>
    /// <param name="registry">Registry name</param>
    /// <param name="repository">Repository path</param>
    private static void ValidateRegistryRepository(string registry, string repository)
    {
        if (string.IsNullOrWhiteSpace(registry))
        {
            throw new ArgumentException("Registry must be provided", nameof(registry));
        }

        if (string.IsNullOrWhiteSpace(repository))
        {
            throw new ArgumentException("Repository must be provided", nameof(repository));
        }
    }

    /// <summary>
    /// Validate tag value
    /// </summary>
    /// <param name="tag">Tag</param>
    private static void ValidateTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            throw new ArgumentException("Tag must be provided", nameof(tag));
        }
    }

    /// <summary>
    /// Normalize an optional digest for persistence and unique lookups
    /// </summary>
    /// <param name="digest">Raw digest</param>
    /// <returns>Normalized digest</returns>
    private static string NormalizeDigest(string? digest)
    {
        return string.IsNullOrWhiteSpace(digest) ? string.Empty : digest.Trim();
    }

    #endregion // Methods
}