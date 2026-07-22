namespace DockerUpdateGuard.Data.Queries;

/// <summary>
/// Query service that resolves the image versions currently relevant to the fleet
/// </summary>
public interface ILiveImageInventoryQueryService
{
    #region Methods

    /// <summary>
    /// Resolve the identifiers of image versions that are the current version of an observed image, run in the latest container scan, or serve as a base image of such an image
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Identifiers of the live image versions</returns>
    Task<IReadOnlySet<Guid>> GetLiveImageVersionIdsAsync(CancellationToken cancellationToken = default);

    #endregion // Methods
}