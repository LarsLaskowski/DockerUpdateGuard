namespace DockerUpdateGuard.UI;

/// <summary>
/// Dashboard view data
/// </summary>
public class DashboardViewData
{
    #region Properties

    /// <summary>
    /// Manually-registered observed image count
    /// </summary>
    public int ObservedImageCount { get; set; }

    /// <summary>
    /// Discovery-owned observed image count
    /// </summary>
    public int MyImageCount { get; set; }

    /// <summary>
    /// Docker instance count
    /// </summary>
    public int DockerInstanceCount { get; set; }

    /// <summary>
    /// Runtime container count
    /// </summary>
    public int RuntimeContainerCount { get; set; }

    /// <summary>
    /// Base image count
    /// </summary>
    public int BaseImageCount { get; set; }

    /// <summary>
    /// Active update finding count
    /// </summary>
    public int ActiveUpdateFindingCount { get; set; }

    /// <summary>
    /// Active derived base-runtime warnings for own images
    /// </summary>
    public int OwnImageBaseRuntimeWarningCount { get; set; }

    /// <summary>
    /// Active vulnerability finding count
    /// </summary>
    public int ActiveVulnerabilityFindingCount { get; set; }

    /// <summary>
    /// Active vulnerability finding counts per severity
    /// </summary>
    public VulnerabilitySeveritySummaryViewData VulnerabilitySeveritySummary { get; set; } = new();

    /// <summary>
    /// Recent scan list
    /// </summary>
    public IReadOnlyList<ScanHistoryItemData> RecentScans { get; set; } = [];

    #endregion // Properties
}