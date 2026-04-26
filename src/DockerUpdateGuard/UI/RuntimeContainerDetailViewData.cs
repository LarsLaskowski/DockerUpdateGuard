namespace DockerUpdateGuard.UI;

/// <summary>
/// Runtime container detail view data
/// </summary>
public class RuntimeContainerDetailViewData
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
    /// Compose project name
    /// </summary>
    public string? ComposeProject { get; set; }

    /// <summary>
    /// Stack name
    /// </summary>
    public string? StackName { get; set; }

    /// <summary>
    /// Service name
    /// </summary>
    public string? ServiceName { get; set; }

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
    /// Update status
    /// </summary>
    public string UpdateStatus { get; set; } = "Not evaluated";

    /// <summary>
    /// Update message
    /// </summary>
    public string? UpdateMessage { get; set; }

    /// <summary>
    /// Manually selected image reference
    /// </summary>
    public string? ManualSelectionImage { get; set; }

    /// <summary>
    /// Timestamp when the manual selection was made
    /// </summary>
    public DateTimeOffset? ManualSelectionAtUtc { get; set; }

    /// <summary>
    /// Available tag candidates
    /// </summary>
    public IReadOnlyList<TagCandidateViewData> AvailableTagCandidates { get; set; } = [];

    /// <summary>
    /// Update findings
    /// </summary>
    public IReadOnlyList<UpdateFindingViewData> UpdateFindings { get; set; } = [];

    /// <summary>
    /// Vulnerability assessment summary
    /// </summary>
    public VulnerabilityAssessmentViewData VulnerabilityAssessment { get; set; } = new();

    /// <summary>
    /// Vulnerability findings
    /// </summary>
    public IReadOnlyList<VulnerabilityFindingViewData> VulnerabilityFindings { get; set; } = [];

    /// <summary>
    /// Current resource usage
    /// </summary>
    public ResourceUsagePointViewData? CurrentResourceUsage { get; set; }

    /// <summary>
    /// Resource usage history
    /// </summary>
    public IReadOnlyList<ResourceUsagePointViewData> ResourceUsageHistory { get; set; } = [];

    /// <summary>
    /// Scan history
    /// </summary>
    public IReadOnlyList<ScanHistoryItemData> ScanHistory { get; set; } = [];

    #endregion // Properties
}