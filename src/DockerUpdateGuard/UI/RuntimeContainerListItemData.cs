namespace DockerUpdateGuard.UI;

/// <summary>
/// Runtime container list item
/// </summary>
public class RuntimeContainerListItemData
{
    #region Properties

    /// <summary>
    /// Docker instance identifier
    /// </summary>
    public Guid DockerInstanceId { get; set; }

    /// <summary>
    /// Container identifier
    /// </summary>
    public string ContainerId { get; set; } = string.Empty;

    /// <summary>
    /// Container name
    /// </summary>
    public string ContainerName { get; set; } = string.Empty;

    /// <summary>
    /// Docker instance name
    /// </summary>
    public string DockerInstanceName { get; set; } = string.Empty;

    /// <summary>
    /// Image reference
    /// </summary>
    public string ImageReference { get; set; } = string.Empty;

    /// <summary>
    /// Current image tag
    /// </summary>
    public string CurrentTag { get; set; } = string.Empty;

    /// <summary>
    /// Resolved version tag
    /// </summary>
    public string? ResolvedVersionTag { get; set; }

    /// <summary>
    /// Runtime status
    /// </summary>
    public string RuntimeStatus { get; set; } = string.Empty;

    /// <summary>
    /// Update state
    /// </summary>
    public string UpdateState { get; set; } = string.Empty;

    /// <summary>
    /// Update summary
    /// </summary>
    public string? UpdateSummary { get; set; }

    /// <summary>
    /// Available update version tag
    /// </summary>
    public string? AvailableUpdateVersionTag { get; set; }

    /// <summary>
    /// Whether Portainer is available
    /// </summary>
    public bool PortainerAvailable { get; set; }

    /// <summary>
    /// Number of active vulnerability findings
    /// </summary>
    public int ActiveVulnerabilityFindingCount { get; set; }

    /// <summary>
    /// Active vulnerability finding counts per severity
    /// </summary>
    public VulnerabilitySeveritySummaryViewData VulnerabilitySeveritySummary { get; set; } = new();

    /// <summary>
    /// Vulnerability status
    /// </summary>
    public string VulnerabilityStatus { get; set; } = "Not scanned";

    /// <summary>
    /// Vulnerability summary
    /// </summary>
    public string? VulnerabilitySummary { get; set; }

    /// <summary>
    /// Number of active base-image vulnerability findings
    /// </summary>
    public int ActiveBaseImageVulnerabilityFindingCount { get; set; }

    /// <summary>
    /// Base-image vulnerability summary
    /// </summary>
    public string? BaseImageVulnerabilitySummary { get; set; }

    /// <summary>
    /// Timestamp when the container was recorded
    /// </summary>
    public DateTimeOffset RecordedAtUtc { get; set; }

    /// <summary>
    /// Linked observed image identifier
    /// </summary>
    public Guid? LinkedObservedImageId { get; set; }

    /// <summary>
    /// Linked observed image name
    /// </summary>
    public string? LinkedObservedImageName { get; set; }

    /// <summary>
    /// Current resource usage
    /// </summary>
    public ResourceUsagePointViewData? CurrentResourceUsage { get; set; }

    /// <summary>
    /// Recent resource-usage history
    /// </summary>
    public IReadOnlyList<ResourceUsagePointViewData> ResourceUsageHistory { get; set; } = [];

    #endregion // Properties
}