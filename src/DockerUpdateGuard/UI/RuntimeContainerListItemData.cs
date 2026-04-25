namespace DockerUpdateGuard.UI;

/// <summary>
/// Runtime container list item
/// </summary>
public class RuntimeContainerListItemData
{
    #region Properties

    public Guid DockerInstanceId { get; set; }

    public string ContainerId { get; set; } = string.Empty;

    public string ContainerName { get; set; } = string.Empty;

    public string DockerInstanceName { get; set; } = string.Empty;

    public string ImageReference { get; set; } = string.Empty;

    public string RuntimeStatus { get; set; } = string.Empty;

    public string UpdateState { get; set; } = string.Empty;

    public string? UpdateSummary { get; set; }

    public bool PortainerAvailable { get; set; }

    public int ActiveVulnerabilityFindingCount { get; set; }

    public string VulnerabilityStatus { get; set; } = "Not scanned";

    public string? VulnerabilitySummary { get; set; }

    public DateTimeOffset RecordedAtUtc { get; set; }

    public Guid? LinkedObservedImageId { get; set; }

    public string? LinkedObservedImageName { get; set; }

    public ResourceUsagePointViewData? CurrentResourceUsage { get; set; }

    #endregion // Properties
}