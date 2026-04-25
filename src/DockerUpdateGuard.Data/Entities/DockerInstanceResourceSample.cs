namespace DockerUpdateGuard.Data.Entities;

/// <summary>
/// Timestamped aggregated resource sample for a Docker instance
/// </summary>
public class DockerInstanceResourceSample
{
    #region Properties

    /// <summary>
    /// Entity identifier
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Related Docker instance identifier
    /// </summary>
    public Guid DockerInstanceId { get; set; }

    /// <summary>
    /// Number of containers represented by the sample
    /// </summary>
    public int ContainerCount { get; set; }

    /// <summary>
    /// Aggregated CPU usage percentage
    /// </summary>
    public decimal CpuPercent { get; set; }

    /// <summary>
    /// Aggregated memory usage in bytes
    /// </summary>
    public long MemoryUsageBytes { get; set; }

    /// <summary>
    /// Aggregated memory limit in bytes
    /// </summary>
    public long MemoryLimitBytes { get; set; }

    /// <summary>
    /// Aggregated cumulative network receive bytes
    /// </summary>
    public long NetworkRxBytesTotal { get; set; }

    /// <summary>
    /// Aggregated cumulative network transmit bytes
    /// </summary>
    public long NetworkTxBytesTotal { get; set; }

    /// <summary>
    /// Aggregated network receive rate in bytes per second
    /// </summary>
    public decimal NetworkRxBytesPerSecond { get; set; }

    /// <summary>
    /// Aggregated network transmit rate in bytes per second
    /// </summary>
    public decimal NetworkTxBytesPerSecond { get; set; }

    /// <summary>
    /// Sample timestamp
    /// </summary>
    public DateTimeOffset RecordedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Related Docker instance
    /// </summary>
    public DockerInstance DockerInstance { get; set; } = null!;

    #endregion // Properties
}