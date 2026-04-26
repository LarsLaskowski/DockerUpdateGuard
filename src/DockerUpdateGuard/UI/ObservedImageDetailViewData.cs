namespace DockerUpdateGuard.UI;

/// <summary>
/// Observed image detail view data
/// </summary>
public class ObservedImageDetailViewData
{
    #region Properties

    /// <summary>
    /// Observed image identifier
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Observed image name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Observed image description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Image reference
    /// </summary>
    public string ImageReference { get; set; } = string.Empty;

    /// <summary>
    /// Latest scan status
    /// </summary>
    public string LatestScanStatus { get; set; } = "NotScanned";

    /// <summary>
    /// Latest scan message
    /// </summary>
    public string? LatestScanMessage { get; set; }

    /// <summary>
    /// Whether the image is owned by this environment
    /// </summary>
    public bool IsOwnImage { get; set; }

    /// <summary>
    /// Base image relationships
    /// </summary>
    public IReadOnlyList<BaseImageRelationshipData> BaseImages { get; set; } = [];

    /// <summary>
    /// Update findings
    /// </summary>
    public IReadOnlyList<UpdateFindingViewData> UpdateFindings { get; set; } = [];

    /// <summary>
    /// Base-runtime alert summary
    /// </summary>
    public string? BaseRuntimeAlertSummary { get; set; }

    /// <summary>
    /// Base-runtime alert details
    /// </summary>
    public string? BaseRuntimeAlertDetails { get; set; }

    /// <summary>
    /// Vulnerability assessment summary
    /// </summary>
    public VulnerabilityAssessmentViewData VulnerabilityAssessment { get; set; } = new();

    /// <summary>
    /// Vulnerability findings
    /// </summary>
    public IReadOnlyList<VulnerabilityFindingViewData> VulnerabilityFindings { get; set; } = [];

    /// <summary>
    /// Linked runtime containers
    /// </summary>
    public IReadOnlyList<LinkedRuntimeContainerViewData> LinkedRuntimeContainers { get; set; } = [];

    /// <summary>
    /// Scan history
    /// </summary>
    public IReadOnlyList<ScanHistoryItemData> ScanHistory { get; set; } = [];

    #endregion // Properties
}