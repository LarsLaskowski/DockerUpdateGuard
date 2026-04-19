namespace DockerUpdateGuard.UI;

/// <summary>
/// Dashboard view data
/// </summary>
public class DashboardViewData
{
    #region Properties

    /// <summary>
    /// Observed image count
    /// </summary>
    public int ObservedImageCount { get; set; }

    /// <summary>
    /// Docker instance count
    /// </summary>
    public int DockerInstanceCount { get; set; }

    /// <summary>
    /// Runtime container count
    /// </summary>
    public int RuntimeContainerCount { get; set; }

    /// <summary>
    /// Shared base image count
    /// </summary>
    public int SharedBaseImageCount { get; set; }

    /// <summary>
    /// Active update finding count
    /// </summary>
    public int ActiveUpdateFindingCount { get; set; }

    /// <summary>
    /// Active vulnerability finding count
    /// </summary>
    public int ActiveVulnerabilityFindingCount { get; set; }

    /// <summary>
    /// Recent scan list
    /// </summary>
    public IReadOnlyList<ScanHistoryItemData> RecentScans { get; set; } = [];

    #endregion // Properties
}