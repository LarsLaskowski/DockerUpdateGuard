namespace DockerUpdateGuard.Data.Entities;

/// <summary>
/// Trigger source for a persisted scan run
/// </summary>
public enum ScanTriggerSource
{
    /// <summary>
    /// No trigger source set
    /// </summary>
    NotSet = 0,

    /// <summary>
    /// Manual trigger
    /// </summary>
    Manual = 1,

    /// <summary>
    /// Scheduled trigger
    /// </summary>
    Scheduled = 2,

    /// <summary>
    /// Startup trigger
    /// </summary>
    Startup = 3,

    /// <summary>
    /// Discovery trigger
    /// </summary>
    Discovery = 4
}