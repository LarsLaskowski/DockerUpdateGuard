namespace DockerUpdateGuard.UI;

/// <summary>
/// UI query contract
/// </summary>
public interface IApplicationViewService
{
    #region Methods

    Task<DashboardViewData> GetDashboardAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ObservedImageListItemData>> GetObservedImagesAsync(CancellationToken cancellationToken = default);

    Task<ObservedImageDetailViewData?> GetObservedImageDetailAsync(Guid observedImageId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RuntimeContainerListItemData>> GetRuntimeContainersAsync(CancellationToken cancellationToken = default);

    Task<RuntimeContainerDetailViewData?> GetRuntimeContainerDetailAsync(Guid dockerInstanceId, string containerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DockerInstanceListItemData>> GetDockerInstancesAsync(CancellationToken cancellationToken = default);

    Task<DockerInstanceDetailViewData?> GetDockerInstanceDetailAsync(Guid dockerInstanceId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SharedBaseImageListItemData>> GetSharedBaseImagesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ScanHistoryItemData>> GetScanHistoryAsync(int take = 50, CancellationToken cancellationToken = default);

    #endregion // Methods
}