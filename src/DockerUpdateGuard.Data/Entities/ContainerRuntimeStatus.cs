namespace DockerUpdateGuard.Data.Entities;

/// <summary>
/// Runtime status of a container
/// </summary>
public enum ContainerRuntimeStatus
{
    /// <summary>
    /// No status set
    /// </summary>
    NotSet = 0,

    /// <summary>
    /// Running container
    /// </summary>
    Running = 1,

    /// <summary>
    /// Paused container
    /// </summary>
    Paused = 2,

    /// <summary>
    /// Restarting container
    /// </summary>
    Restarting = 3,

    /// <summary>
    /// Exited container
    /// </summary>
    Exited = 4,

    /// <summary>
    /// Dead container
    /// </summary>
    Dead = 5
}