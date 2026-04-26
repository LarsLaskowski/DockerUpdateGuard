namespace DockerUpdateGuard.UI;

/// <summary>
/// Resource usage point for list and history views
/// </summary>
public class ResourceUsagePointViewData
{
    #region Properties

    /// <summary>
    /// CPU usage percentage
    /// </summary>
    public decimal CpuPercent { get; set; }

    /// <summary>
    /// Memory usage in bytes
    /// </summary>
    public long MemoryUsageBytes { get; set; }

    /// <summary>
    /// Memory limit in bytes
    /// </summary>
    public long MemoryLimitBytes { get; set; }

    /// <summary>
    /// Received network bytes per second
    /// </summary>
    public decimal NetworkRxBytesPerSecond { get; set; }

    /// <summary>
    /// Transmitted network bytes per second
    /// </summary>
    public decimal NetworkTxBytesPerSecond { get; set; }

    /// <summary>
    /// Timestamp when the usage was recorded
    /// </summary>
    public DateTimeOffset RecordedAtUtc { get; set; }

    #endregion // Properties
}