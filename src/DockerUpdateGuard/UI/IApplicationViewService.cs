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
    /// Read the lightweight dashboard summary for the application top bar
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dashboard summary view data</returns>
    Task<DashboardSummaryViewData> GetDashboardSummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Read all observed images for the overview page
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Observed image list items</returns>
    Task<IReadOnlyList<ObservedImageListItemData>> GetObservedImagesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Read manually-registered observed images for the manual images page
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Manually-registered observed image list items</returns>
    Task<IReadOnlyList<ObservedImageListItemData>> GetManualObservedImagesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Read discovery-owned observed images for the own images page
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Discovery-owned observed image list items</returns>
    Task<IReadOnlyList<ObservedImageListItemData>> GetDiscoveryObservedImagesAsync(CancellationToken cancellationToken = default);

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
    /// Determine whether any base images are available for UI navigation
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True when at least one base image is available</returns>
    Task<bool> HasBaseImagesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Read base images for the overview page
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Base image list items</returns>
    Task<IReadOnlyList<SharedBaseImageListItemData>> GetBaseImagesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Read scan history entries for the activity overview
    /// </summary>
    /// <param name="take">Maximum number of entries</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Scan history entries</returns>
    Task<IReadOnlyList<ScanHistoryItemData>> GetScanHistoryAsync(int take = 20, CancellationToken cancellationToken = default);

    /// <summary>
    /// Read the fleet-wide vulnerability overview grouped by advisory
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Vulnerability overview items sorted by severity, CVSS score, and affected image count</returns>
    Task<IReadOnlyList<VulnerabilityOverviewItemData>> GetVulnerabilityOverviewAsync(CancellationToken cancellationToken = default);

    #endregion // Methods
}