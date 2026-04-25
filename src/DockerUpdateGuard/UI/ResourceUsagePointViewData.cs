namespace DockerUpdateGuard.UI;

/// <summary>
/// Resource usage point for list and history views
/// </summary>
public class ResourceUsagePointViewData
{
    #region Properties

    public decimal CpuPercent { get; set; }

    public long MemoryUsageBytes { get; set; }

    public long MemoryLimitBytes { get; set; }

    public decimal NetworkRxBytesPerSecond { get; set; }

    public decimal NetworkTxBytesPerSecond { get; set; }

    public DateTimeOffset RecordedAtUtc { get; set; }

    #endregion // Properties
}