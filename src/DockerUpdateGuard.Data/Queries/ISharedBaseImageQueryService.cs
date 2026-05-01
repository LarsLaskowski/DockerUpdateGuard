namespace DockerUpdateGuard.Data.Queries;

/// <summary>
/// Query service for shared base image scenarios
/// </summary>
public interface ISharedBaseImageQueryService
{
    #region Methods

    /// <summary>
    /// Get all resolved base images for the overview page
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Base images</returns>
    Task<IReadOnlyList<SharedBaseImageUsageData>> GetBaseImagesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get observed images that depend on a base image
    /// </summary>
    /// <param name="baseImageVersionId">Base image version identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Observed images using the base image</returns>
    Task<IReadOnlyList<ObservedImageReferenceData>> GetObservedImagesByBaseImageAsync(Guid baseImageVersionId, CancellationToken cancellationToken = default);

    #endregion // Methods
}