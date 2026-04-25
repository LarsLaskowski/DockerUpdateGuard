namespace DockerUpdateGuard.Docker;

/// <summary>
/// Runtime container resource sample returned by a Docker engine
/// </summary>
public class RuntimeContainerResourceDescriptor
{
    #region Properties

    /// <summary>
    /// Container identifier
    /// </summary>
    public string ContainerId { get; set; } = string.Empty;

    /// <summary>
    /// Container name
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
    /// Sample timestamp
    /// </summary>
    public DateTimeOffset RecordedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    #endregion // Properties
}