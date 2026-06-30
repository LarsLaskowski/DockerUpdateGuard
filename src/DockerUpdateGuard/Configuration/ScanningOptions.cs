namespace DockerUpdateGuard.Configuration;

/// <summary>
/// Scan scheduling options
/// </summary>
public class ScanningOptions
{
    #region Properties

    /// <summary>
    /// Docker instance synchronization interval in minutes
    /// </summary>
    public int DiscoveryIntervalMinutes { get; set; } = 15;

    /// <summary>
    /// Docker Hub account discovery interval in minutes
    /// </summary>
    public int DockerHubAccountDiscoveryIntervalMinutes { get; set; } = 60;

    /// <summary>
    /// Observed image base scan interval in minutes
    /// </summary>
    public int OwnImageBaseScanIntervalMinutes { get; set; } = 60;

    /// <summary>
    /// Docker Hub request quota window for scheduled observed-image refreshes in hours
    /// </summary>
    public int DockerHubRequestLimitWindowHours { get; set; } = 6;

    /// <summary>
    /// Docker Hub request quota for scheduled observed-image refreshes per window
    /// </summary>
    public int DockerHubRequestLimitPerWindow { get; set; } = 200;

    /// <summary>
    /// Reserved Docker Hub requests per window for manual scans and ad-hoc activity
    /// </summary>
    public int DockerHubReservedManualRequestsPerWindow { get; set; } = 40;

    /// <summary>
    /// Runtime container refresh interval in minutes
    /// </summary>
    public int RuntimeImageUpdateScanIntervalMinutes { get; set; } = 30;

    /// <summary>
    /// Resource statistics sampling interval in minutes
    /// </summary>
    public int ResourceStatisticsIntervalMinutes { get; set; } = 5;

    /// <summary>
    /// Vulnerability refresh interval in minutes
    /// </summary>
    public int VulnerabilityRefreshIntervalMinutes { get; set; } = 180;

    /// <summary>
    /// Cleanup interval in minutes
    /// </summary>
    public int CleanupIntervalMinutes { get; set; } = 720;

    /// <summary>
    /// Retry count for transient failures
    /// </summary>
    public int RetryCount { get; set; } = 2;

    /// <summary>
    /// Number of days to retain completed scan history
    /// </summary>
    public int RetainScanRunsDays { get; set; } = 30;

    #endregion // Properties
}