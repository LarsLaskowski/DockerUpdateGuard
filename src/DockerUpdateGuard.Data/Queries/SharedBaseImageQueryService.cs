using DockerUpdateGuard.Data.Entities;

using Microsoft.EntityFrameworkCore;

namespace DockerUpdateGuard.Data.Queries;

/// <summary>
/// Query service for shared base image scenarios
/// </summary>
public class SharedBaseImageQueryService : ISharedBaseImageQueryService
{
    #region Fields

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

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SharedBaseImageUsageData>> GetSharedBaseImagesAsync(CancellationToken cancellationToken = default)
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
                                                                          ObservedImageId = x.observedImage.Id,
                                                                      })
                                                         .ToListAsync(cancellationToken)
                                                         .ConfigureAwait(false);

        var sharedBaseImages = flatResults.GroupBy(entity => new
                                                             {
                                                                 entity.BaseImageVersionId,
                                                                 entity.Registry,
                                                                 entity.Repository,
                                                                 entity.Tag,
                                                                 entity.Digest,
                                                             })
                                          .Select(group => new SharedBaseImageUsageData
                                                           {
                                                               BaseImageVersionId = group.Key.BaseImageVersionId,
                                                               Registry = group.Key.Registry,
                                                               Repository = group.Key.Repository,
                                                               Tag = group.Key.Tag,
                                                               Digest = group.Key.Digest,
                                                               ObservedImageCount = group.Select(entity => entity.ObservedImageId)
                                                                                         .Distinct()
                                                                                         .Count(),
                                                           })
                                          .Where(entity => entity.ObservedImageCount > 1)
                                          .OrderByDescending(entity => entity.ObservedImageCount)
                                          .ThenBy(entity => entity.Registry)
                                          .ThenBy(entity => entity.Repository)
                                          .ThenBy(entity => entity.Tag)
                                          .ToList();

        return sharedBaseImages;
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

    #endregion // Methods
}