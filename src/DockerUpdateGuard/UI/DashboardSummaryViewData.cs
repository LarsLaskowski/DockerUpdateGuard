namespace DockerUpdateGuard.UI;

/// <summary>
/// Lightweight dashboard summary consumed by the application top bar, avoiding the full dashboard aggregation on every navigation
/// </summary>
public class DashboardSummaryViewData
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
    /// Runtime container count
    /// </summary>
    public int RuntimeContainerCount { get; set; }

    /// <summary>
    /// Most recent scan, or null when no scan has been recorded yet
    /// </summary>
    public ScanHistoryItemData? LatestScan { get; set; }

    #endregion // Properties
}