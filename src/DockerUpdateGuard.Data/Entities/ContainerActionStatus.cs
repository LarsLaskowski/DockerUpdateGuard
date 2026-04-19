namespace DockerUpdateGuard.Data.Entities;

/// <summary>
/// Status of a container action run
/// </summary>
public enum ContainerActionStatus
{
    /// <summary>
    /// No action status set
    /// </summary>
    NotSet = 0,

    /// <summary>
    /// Requested action
    /// </summary>
    Requested = 1,

    /// <summary>
    /// Running action
    /// </summary>
    Running = 2,

    /// <summary>
    /// Successful action
    /// </summary>
    Succeeded = 3,

    /// <summary>
    /// Failed action
    /// </summary>
    Failed = 4,

    /// <summary>
    /// Cancelled action
    /// </summary>
    Cancelled = 5
}