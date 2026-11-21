using DockerUpdateGuard.Data;
using DockerUpdateGuard.Data.Repositories;
using DockerUpdateGuard.Images.Helper;

using Microsoft.EntityFrameworkCore;

namespace DockerUpdateGuard.Images;

/// <summary>
/// Moves tracked image state onto canonical catalog entries
/// </summary>
public static class ImageCatalogCanonicalizer
{
    #region Static methods

    /// <summary>
    /// Move tracked image references to their canonical catalog entries
    /// </summary>
    /// <param name="dbContext">Database context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of moved tracked image references</returns>
    public static async Task<int> CanonicalizeTrackedStateAsync(DockerUpdateGuardDbContext dbContext, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        var parser = new ImageReferenceParser();
        var catalog = new ImageCatalogRepository(dbContext);
        var aliases = ImageReferenceParser.DockerHubRegistryHosts.Where(host => string.Equals(host, ImageReferenceParser.DockerHubRegistry, StringComparison.Ordinal) == false)
                                                                 .ToList();
        var moved = 0;

        var observedImages = await dbContext.ObservedImages.Include(entity => entity.CurrentImageVersion.RegistryRepository)
                                                           .Where(entity => aliases.Contains(entity.CurrentImageVersion.RegistryRepository.Registry)
                                                                            || (entity.CurrentImageVersion.RegistryRepository.Registry == ImageReferenceParser.DockerHubRegistry
                                                                                && entity.CurrentImageVersion.RegistryRepository.Repository.Contains('/') == false))
                                                           .ToListAsync(cancellationToken)
                                                           .ConfigureAwait(false);

        foreach (var observedImage in observedImages)
        {
            var version = observedImage.CurrentImageVersion;
            var canonical = GetCanonicalPair(parser, version.RegistryRepository.Registry, version.RegistryRepository.Repository);

            if (canonical is null)
            {
                continue;
            }

            var canonicalVersion = await catalog.GetOrCreateImageVersionAsync(canonical.Value.Registry,
                                                                              canonical.Value.Repository,
                                                                              version.Tag,
                                                                              version.Digest,
                                                                              version.PublishedAtUtc,
                                                                              version.MetadataJson,
                                                                              cancellationToken)
                                                .ConfigureAwait(false);

            observedImage.CurrentImageVersionId = canonicalVersion.Id;
            observedImage.CurrentImageVersion = canonicalVersion;
            moved++;
        }

        var tagSelections = await dbContext.RuntimeContainerTagSelections.Include(entity => entity.RegistryRepository)
                                                                         .Where(entity => aliases.Contains(entity.RegistryRepository.Registry)
                                                                                          || (entity.RegistryRepository.Registry == ImageReferenceParser.DockerHubRegistry
                                                                                              && entity.RegistryRepository.Repository.Contains('/') == false))
                                                                         .ToListAsync(cancellationToken)
                                                                         .ConfigureAwait(false);

        foreach (var tagSelection in tagSelections)
        {
            var canonical = GetCanonicalPair(parser, tagSelection.RegistryRepository.Registry, tagSelection.RegistryRepository.Repository);

            if (canonical is null)
            {
                continue;
            }

            var canonicalRepository = await catalog.GetOrCreateRegistryRepositoryAsync(canonical.Value.Registry, canonical.Value.Repository, cancellationToken)
                                                   .ConfigureAwait(false);

            tagSelection.RegistryRepositoryId = canonicalRepository.Id;
            tagSelection.RegistryRepository = canonicalRepository;
            moved++;
        }

        if (moved > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken)
                           .ConfigureAwait(false);
        }

        return moved;
    }

    /// <summary>
    /// Compute the canonical registry and repository of a stored pair
    /// </summary>
    /// <param name="parser">Image reference parser</param>
    /// <param name="registry">Stored registry</param>
    /// <param name="repository">Stored repository</param>
    /// <returns>Canonical pair, or null when the row stays unchanged (already canonical or the canonical pair fails the reference rules)</returns>
    private static (string Registry, string Repository)? GetCanonicalPair(ImageReferenceParser parser, string registry, string repository)
    {
        try
        {
            var reference = parser.Parse($"{registry}/{repository}");

            if (string.Equals(reference.Registry, registry, StringComparison.Ordinal)
                && string.Equals(reference.Repository, repository, StringComparison.Ordinal))
            {
                return null;
            }

            if (RegistryReferenceValidator.IsValidRegistry(reference.Registry) == false
                || RegistryReferenceValidator.IsValidRepository(reference.Repository) == false)
            {
                return null;
            }

            return (reference.Registry, reference.Repository);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    #endregion // Static methods
}