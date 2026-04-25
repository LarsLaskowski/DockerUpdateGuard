namespace DockerUpdateGuard.UI;

/// <summary>
/// Docker instance detail view data
/// </summary>
public class DockerInstanceDetailViewData
{
    #region Properties

    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string EndpointUri { get; set; } = string.Empty;

    public string ConnectionKind { get; set; } = string.Empty;

    public bool PortainerEnabled { get; set; }

    public string LatestScanStatus { get; set; } = "NotScanned";

    public DateTimeOffset? LatestScanCompletedAtUtc { get; set; }

    public int RuntimeContainerCount { get; set; }

    public ResourceUsagePointViewData? CurrentResourceUsage { get; set; }

    public IReadOnlyList<ResourceUsagePointViewData> ResourceUsageHistory { get; set; } = [];

    public IReadOnlyList<RuntimeContainerListItemData> RuntimeContainers { get; set; } = [];

    #endregion // Properties
}