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
        var flatResults = await (from observedImage in _dbContext.ObservedImages.AsNoTracking()
        join imageRelationship in _dbContext.ImageRelationships.AsNoTracking()
        on observedImage.CurrentImageVersionId equals imageRelationship.ChildImageVersionId
        join baseImageVersion in _dbContext.ImageVersions.AsNoTracking()
        on imageRelationship.BaseImageVersionId equals baseImageVersion.Id
        join registryRepository in _dbContext.RegistryRepositories.AsNoTracking()
        on baseImageVersion.RegistryRepositoryId equals registryRepository.Id
        where imageRelationship.RelationshipType == ImageRelationshipType.BaseImage
        select new
               {
                   imageRelationship.BaseImageVersionId,
                   registryRepository.Registry,
                   registryRepository.Repository,
                   baseImageVersion.Tag,
                   baseImageVersion.Digest,
                   ObservedImageId = observedImage.Id,
               }).ToListAsync(cancellationToken)
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
        var flatResults = await (from observedImage in _dbContext.ObservedImages.AsNoTracking()
        join currentImageVersion in _dbContext.ImageVersions.AsNoTracking()
        on observedImage.CurrentImageVersionId equals currentImageVersion.Id
        join registryRepository in _dbContext.RegistryRepositories.AsNoTracking()
        on currentImageVersion.RegistryRepositoryId equals registryRepository.Id
        join imageRelationship in _dbContext.ImageRelationships.AsNoTracking()
        on observedImage.CurrentImageVersionId equals imageRelationship.ChildImageVersionId
        where imageRelationship.BaseImageVersionId == baseImageVersionId
              && imageRelationship.RelationshipType == ImageRelationshipType.BaseImage
        select new ObservedImageReferenceData
               {
                   ObservedImageId = observedImage.Id,
                   ObservedImageName = observedImage.Name,
                   CurrentImageVersionId = observedImage.CurrentImageVersionId,
                   Registry = registryRepository.Registry,
                   Repository = registryRepository.Repository,
                   Tag = currentImageVersion.Tag,
                   Digest = currentImageVersion.Digest,
               }).ToListAsync(cancellationToken)
          .ConfigureAwait(false);

        return flatResults.GroupBy(entity => entity.ObservedImageId)
                          .Select(group => group.First())
                          .OrderBy(entity => entity.ObservedImageName)
                          .ToList();
    }

    #endregion // Methods
}