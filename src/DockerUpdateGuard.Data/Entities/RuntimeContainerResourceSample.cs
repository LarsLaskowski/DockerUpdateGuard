namespace DockerUpdateGuard.Data.Entities;

/// <summary>
/// Timestamped resource sample for a runtime container
/// </summary>
public class RuntimeContainerResourceSample
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
    /// Runtime container identifier
    /// </summary>
    public string ContainerId { get; set; } = string.Empty;

    /// <summary>
    /// Runtime container name
    /// </summary>
    public string ContainerName { get; set; } = string.Empty;

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
    /// Cumulative network receive bytes
    /// </summary>
    public long NetworkRxBytesTotal { get; set; }

    /// <summary>
    /// Cumulative network transmit bytes
    /// </summary>
    public long NetworkTxBytesTotal { get; set; }

    /// <summary>
    /// Network receive rate in bytes per second
    /// </summary>
    public decimal NetworkRxBytesPerSecond { get; set; }

    /// <summary>
    /// Network transmit rate in bytes per second
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