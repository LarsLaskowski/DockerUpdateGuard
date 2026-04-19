using System.ComponentModel.DataAnnotations;

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
    [Range(1, 1440)]
    public int DiscoveryIntervalMinutes { get; set; } = 15;

    /// <summary>
    /// Docker Hub account discovery interval in minutes
    /// </summary>
    [Range(1, 10080)]
    public int DockerHubAccountDiscoveryIntervalMinutes { get; set; } = 60;

    /// <summary>
    /// Observed image base scan interval in minutes
    /// </summary>
    [Range(1, 10080)]
    public int OwnImageBaseScanIntervalMinutes { get; set; } = 60;

    /// <summary>
    /// Runtime container refresh interval in minutes
    /// </summary>
    [Range(1, 1440)]
    public int RuntimeImageUpdateScanIntervalMinutes { get; set; } = 30;

    /// <summary>
    /// Vulnerability refresh interval in minutes
    /// </summary>
    [Range(1, 10080)]
    public int VulnerabilityRefreshIntervalMinutes { get; set; } = 180;

    /// <summary>
    /// Cleanup interval in minutes
    /// </summary>
    [Range(1, 10080)]
    public int CleanupIntervalMinutes { get; set; } = 720;

    /// <summary>
    /// Retry count for transient failures
    /// </summary>
    [Range(0, 10)]
    public int RetryCount { get; set; } = 2;

    /// <summary>
    /// Number of days to retain completed scan history
    /// </summary>
    [Range(1, 3650)]
    public int RetainScanRunsDays { get; set; } = 30;

    #endregion // Properties
}