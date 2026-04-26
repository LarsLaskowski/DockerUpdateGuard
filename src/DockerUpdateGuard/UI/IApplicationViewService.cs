namespace DockerUpdateGuard.UI;

/// <summary>
/// UI query contract
/// </summary>
public interface IApplicationViewService
{
    #region Methods

    /// <summary>
    /// Read dashboard data for the main overview
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dashboard view data</returns>
    Task<DashboardViewData> GetDashboardAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Read all observed images for the overview page
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Observed image list items</returns>
    Task<IReadOnlyList<ObservedImageListItemData>> GetObservedImagesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Read the detail view for a single observed image
    /// </summary>
    /// <param name="observedImageId">ID of the observed image</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Observed image detail view data or null</returns>
    Task<ObservedImageDetailViewData?> GetObservedImageDetailAsync(Guid observedImageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Read all runtime containers for the overview page
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Runtime container list items</returns>
    Task<IReadOnlyList<RuntimeContainerListItemData>> GetRuntimeContainersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Read the detail view for a runtime container
    /// </summary>
    /// <param name="dockerInstanceId">ID of the Docker instance</param>
    /// <param name="containerId">ID of the container</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Runtime container detail view data or null</returns>
    Task<RuntimeContainerDetailViewData?> GetRuntimeContainerDetailAsync(Guid dockerInstanceId, string containerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Read all Docker instances for the overview page
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Docker instance list items</returns>
    Task<IReadOnlyList<DockerInstanceListItemData>> GetDockerInstancesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Read the detail view for a single Docker instance
    /// </summary>
    /// <param name="dockerInstanceId">ID of the Docker instance</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Docker instance detail view data or null</returns>
    Task<DockerInstanceDetailViewData?> GetDockerInstanceDetailAsync(Guid dockerInstanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Read shared base images for the overview page
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Shared base image list items</returns>
    Task<IReadOnlyList<SharedBaseImageListItemData>> GetSharedBaseImagesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Read scan history entries for the activity overview
    /// </summary>
    /// <param name="take">Maximum number of entries</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Scan history entries</returns>
    Task<IReadOnlyList<ScanHistoryItemData>> GetScanHistoryAsync(int take = 50, CancellationToken cancellationToken = default);

    #endregion // Methods
}