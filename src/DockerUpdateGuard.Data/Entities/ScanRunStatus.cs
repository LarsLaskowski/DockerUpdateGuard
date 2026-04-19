namespace DockerUpdateGuard.Data.Entities;

/// <summary>
/// Status of a persisted scan run
/// </summary>
public enum ScanRunStatus
{
    /// <summary>
    /// No status set
    /// </summary>
    NotSet = 0,

    /// <summary>
    /// Queued scan
    /// </summary>
    Pending = 1,

    /// <summary>
    /// Running scan
    /// </summary>
    Running = 2,

    /// <summary>
    /// Successful scan
    /// </summary>
    Succeeded = 3,

    /// <summary>
    /// Partial scan result
    /// </summary>
    Partial = 4,

    /// <summary>
    /// Failed scan
    /// </summary>
    Failed = 5
}