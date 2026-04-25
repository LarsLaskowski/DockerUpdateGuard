namespace DockerUpdateGuard.UI;

/// <summary>
/// Runtime container detail view data
/// </summary>
public class RuntimeContainerDetailViewData
{
    #region Properties

    public Guid DockerInstanceId { get; set; }

    public string ContainerId { get; set; } = string.Empty;

    public string ContainerName { get; set; } = string.Empty;

    public string DockerInstanceName { get; set; } = string.Empty;

    public string ImageReference { get; set; } = string.Empty;

    public string RuntimeStatus { get; set; } = string.Empty;

    public string? ComposeProject { get; set; }

    public string? StackName { get; set; }

    public string? ServiceName { get; set; }

    public DateTimeOffset RecordedAtUtc { get; set; }

    public Guid? LinkedObservedImageId { get; set; }

    public string? LinkedObservedImageName { get; set; }

    public string UpdateStatus { get; set; } = "Not evaluated";

    public string? UpdateMessage { get; set; }

    public string? ManualSelectionImage { get; set; }

    public DateTimeOffset? ManualSelectionAtUtc { get; set; }

    public IReadOnlyList<TagCandidateViewData> AvailableTagCandidates { get; set; } = [];

    public IReadOnlyList<UpdateFindingViewData> UpdateFindings { get; set; } = [];

    public VulnerabilityAssessmentViewData VulnerabilityAssessment { get; set; } = new();

    public IReadOnlyList<VulnerabilityFindingViewData> VulnerabilityFindings { get; set; } = [];

    public ResourceUsagePointViewData? CurrentResourceUsage { get; set; }

    public IReadOnlyList<ResourceUsagePointViewData> ResourceUsageHistory { get; set; } = [];

    public IReadOnlyList<ScanHistoryItemData> ScanHistory { get; set; } = [];

    #endregion // Properties
}