namespace DockerUpdateGuard.Telemetry;

/// <summary>
/// Metric names used across DockerUpdateGuard telemetry
/// </summary>
public static class TelemetryMetricNames
{
    #region Const fields

    /// <summary>
    /// Number of observed images
    /// </summary>
    public const string ObservedImages = "dockerupdateguard.observed_images";

    /// <summary>
    /// Number of runtime containers
    /// </summary>
    public const string RuntimeContainers = "dockerupdateguard.runtime_containers";

    /// <summary>
    /// Number of deduplicated base images
    /// </summary>
    public const string DeduplicatedBaseImages = "dockerupdateguard.base_images.deduplicated";

    /// <summary>
    /// Number of scan runs per source
    /// </summary>
    public const string ScanRuns = "dockerupdateguard.scans.total";

    /// <summary>
    /// Number of scan failures per source
    /// </summary>
    public const string ScanFailures = "dockerupdateguard.scans.failed";

    /// <summary>
    /// Duration of scan runs
    /// </summary>
    public const string ScanDuration = "dockerupdateguard.scans.duration";

    /// <summary>
    /// Number of active update findings
    /// </summary>
    public const string ActiveUpdateFindings = "dockerupdateguard.findings.update.active";

    /// <summary>
    /// Number of active CVE findings
    /// </summary>
    public const string ActiveCveFindings = "dockerupdateguard.findings.cve.active";

    /// <summary>
    /// Number of findings that require review
    /// </summary>
    public const string NeedsReviewFindings = "dockerupdateguard.findings.needs_review.active";

    #endregion // Const fields
}