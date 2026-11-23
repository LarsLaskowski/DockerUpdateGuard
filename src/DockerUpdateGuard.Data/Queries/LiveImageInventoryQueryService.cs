using DockerUpdateGuard.Data.Entities;

using Microsoft.EntityFrameworkCore;

namespace DockerUpdateGuard.Data.Queries;

/// <summary>
/// Query service that resolves the image versions currently relevant to the fleet
/// </summary>
public class LiveImageInventoryQueryService : ILiveImageInventoryQueryService
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
    public LiveImageInventoryQueryService(DockerUpdateGuardDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    #endregion // Constructors

    #region ILiveImageInventoryQueryService

    /// <inheritdoc/>
    public async Task<IReadOnlySet<Guid>> GetLiveImageVersionIdsAsync(CancellationToken cancellationToken = default)
    {
        var observedImageVersionIds = await _dbContext.ObservedImages.Select(entity => entity.CurrentImageVersionId)
                                                                     .ToListAsync(cancellationToken)
                                                                     .ConfigureAwait(false);
        var currentSnapshots = await CurrentContainerSnapshotQuery.GetCurrentContainerSnapshotsAsync(_dbContext, cancellationToken).ConfigureAwait(false);
        var liveImageVersionIds = new HashSet<Guid>(observedImageVersionIds);

        liveImageVersionIds.UnionWith(currentSnapshots.Select(entity => entity.ImageVersionId));

        var childImageVersionIds = liveImageVersionIds.ToList();
        var baseImageVersionIds = await _dbContext.ImageRelationships.Where(entity => entity.RelationshipType == ImageRelationshipType.BaseImage
                                                                                      && childImageVersionIds.Contains(entity.ChildImageVersionId))
                                                                     .Select(entity => entity.BaseImageVersionId)
                                                                     .ToListAsync(cancellationToken)
                                                                     .ConfigureAwait(false);

        liveImageVersionIds.UnionWith(baseImageVersionIds);

        return liveImageVersionIds;
    }

    #endregion // ILiveImageInventoryQueryService
}