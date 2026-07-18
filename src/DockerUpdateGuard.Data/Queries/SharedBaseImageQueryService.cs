using DockerUpdateGuard.Data.Entities;

using Microsoft.EntityFrameworkCore;

namespace DockerUpdateGuard.Data.Queries;

/// <summary>
/// Query service for shared base image scenarios
/// </summary>
public class SharedBaseImageQueryService : ISharedBaseImageQueryService
{
    #region Fields

    /// <summary>
    /// DB context object
    /// </summary>
    private readonly DockerUpdateGuardDbContext _dbContext;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="dbContext">Database context</param>
    public SharedBaseImageQueryService(DockerUpdateGuardDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    #endregion // Constructors

    #region Methods

    /// <summary>
    /// Build a grouping key for the base-image overview
    /// </summary>
    /// <param name="registry">Registry name</param>
    /// <param name="repository">Repository path</param>
    /// <param name="tag">Tag value</param>
    /// <param name="digest">Digest value</param>
    /// <param name="sourceReference">Source-reference value</param>
    /// <returns>Grouping key</returns>
    private static string BuildGroupingKey(string registry,
                                           string repository,
                                           string tag,
                                           string? digest,
                                           string? sourceReference)
    {
        if (string.IsNullOrWhiteSpace(digest) == false)
        {
            return $"{registry}/{repository}@{digest}";
        }

        return $"{registry}/{repository}:{tag}|{sourceReference ?? string.Empty}";
    }

    #endregion // Methods

    #region ISharedBaseImageQueryService

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SharedBaseImageUsageData>> GetBaseImagesAsync(CancellationToken cancellationToken = default)
    {
        var flatResults = await _dbContext.ObservedImages.AsNoTracking()
                                                         .Join(_dbContext.ImageRelationships.AsNoTracking(),
                                                               observedImage => observedImage.CurrentImageVersionId,
                                                               imageRelationship => imageRelationship.ChildImageVersionId,
                                                               (observedImage, imageRelationship) => new
                                                                                                     {
                                                                                                         observedImage,
                                                                                                         imageRelationship
                                                                                                     })
                                                         .Join(_dbContext.ImageVersions.AsNoTracking(),
                                                               x => x.imageRelationship.BaseImageVersionId,
                                                               baseImageVersion => baseImageVersion.Id,
                                                               (x, baseImageVersion) => new
                                                                                        {
                                                                                            x.observedImage,
                                                                                            x.imageRelationship,
                                                                                            baseImageVersion
                                                                                        })
                                                         .Join(_dbContext.RegistryRepositories.AsNoTracking(),
                                                               x => x.baseImageVersion.RegistryRepositoryId,
                                                               registryRepository => registryRepository.Id,
                                                               (x, registryRepository) => new
                                                                                          {
                                                                                              x.observedImage,
                                                                                              x.imageRelationship,
                                                                                              x.baseImageVersion,
                                                                                              registryRepository
                                                                                          })
                                                         .Where(x => x.imageRelationship.RelationshipType == ImageRelationshipType.BaseImage)
                                                         .Select(x => new
                                                                      {
                                                                          x.imageRelationship.BaseImageVersionId,
                                                                          x.registryRepository.Registry,
                                                                          x.registryRepository.Repository,
                                                                          x.baseImageVersion.Tag,
                                                                          x.baseImageVersion.Digest,
                                                                          x.imageRelationship.SourceReference,
                                                                          ObservedImageId = x.observedImage.Id,
                                                                      })
                                                         .ToListAsync(cancellationToken)
                                                         .ConfigureAwait(false);

        return flatResults.GroupBy(entity => BuildGroupingKey(entity.Registry,
                                                              entity.Repository,
                                                              entity.Tag,
                                                              entity.Digest,
                                                              entity.SourceReference),
                                   StringComparer.OrdinalIgnoreCase)
                          .Select(group =>
                                  {
                                      var first = group.OrderBy(entity => entity.Registry, StringComparer.OrdinalIgnoreCase)
                                                       .ThenBy(entity => entity.Repository, StringComparer.OrdinalIgnoreCase)
                                                       .ThenBy(entity => entity.Tag, StringComparer.OrdinalIgnoreCase)
                                                       .ThenBy(entity => entity.SourceReference, StringComparer.OrdinalIgnoreCase)
                                                       .First();

                                      return new SharedBaseImageUsageData
                                             {
                                                 BaseImageVersionId = first.BaseImageVersionId,
                                                 BaseImageVersionIds = group.Select(entity => entity.BaseImageVersionId)
                                                                            .Distinct()
                                                                            .ToList(),
                                                 Registry = first.Registry,
                                                 Repository = first.Repository,
                                                 Tag = first.Tag,
                                                 Digest = first.Digest,
                                                 SourceReferences = group.Select(entity => entity.SourceReference ?? string.Empty)
                                                                         .Where(entity => string.IsNullOrWhiteSpace(entity) == false)
                                                                         .Distinct(StringComparer.OrdinalIgnoreCase)
                                                                         .OrderBy(entity => entity, StringComparer.OrdinalIgnoreCase)
                                                                         .ToList(),
                                                 ObservedImageCount = group.Select(entity => entity.ObservedImageId)
                                                                           .Distinct()
                                                                           .Count(),
                                             };
                                  })
                          .OrderByDescending(entity => entity.ObservedImageCount)
                          .ThenBy(entity => entity.Registry)
                          .ThenBy(entity => entity.Repository)
                          .ThenBy(entity => entity.Tag)
                          .ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ObservedImageReferenceData>> GetObservedImagesByBaseImageAsync(Guid baseImageVersionId, CancellationToken cancellationToken = default)
    {
        var flatResults = await _dbContext.ObservedImages.AsNoTracking()
                                                         .Join(_dbContext.ImageVersions.AsNoTracking(),
                                                               observedImage => observedImage.CurrentImageVersionId,
                                                               currentImageVersion => currentImageVersion.Id,
                                                               (observedImage, currentImageVersion) => new
                                                                                                       {
                                                                                                           observedImage,
                                                                                                           currentImageVersion
                                                                                                       })
                                                         .Join(_dbContext.RegistryRepositories.AsNoTracking(),
                                                               x => x.currentImageVersion.RegistryRepositoryId,
                                                               registryRepository => registryRepository.Id,
                                                               (x, registryRepository) => new
                                                                                          {
                                                                                              x.observedImage,
                                                                                              x.currentImageVersion,
                                                                                              registryRepository
                                                                                          })
                                                         .Join(_dbContext.ImageRelationships.AsNoTracking(),
                                                               x => x.observedImage.CurrentImageVersionId,
                                                               imageRelationship => imageRelationship.ChildImageVersionId,
                                                               (x, imageRelationship) => new
                                                                                         {
                                                                                             x.observedImage,
                                                                                             x.currentImageVersion,
                                                                                             x.registryRepository,
                                                                                             imageRelationship
                                                                                         })
                                                         .Where(x => x.imageRelationship.BaseImageVersionId == baseImageVersionId
                                                                     && x.imageRelationship.RelationshipType == ImageRelationshipType.BaseImage)
                                                         .Select(x => new ObservedImageReferenceData
                                                                      {
                                                                          ObservedImageId = x.observedImage.Id,
                                                                          ObservedImageName = x.observedImage.Name,
                                                                          CurrentImageVersionId = x.observedImage.CurrentImageVersionId,
                                                                          Registry = x.registryRepository.Registry,
                                                                          Repository = x.registryRepository.Repository,
                                                                          Tag = x.currentImageVersion.Tag,
                                                                          Digest = x.currentImageVersion.Digest,
                                                                      })
                                                         .ToListAsync(cancellationToken)
                                                         .ConfigureAwait(false);

        return flatResults.GroupBy(entity => entity.ObservedImageId)
                          .Select(group => group.First())
                          .OrderBy(entity => entity.ObservedImageName)
                          .ToList();
    }

    #endregion // ISharedBaseImageQueryService
}