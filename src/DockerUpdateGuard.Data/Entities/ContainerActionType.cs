namespace DockerUpdateGuard.Data.Entities;

/// <summary>
/// Action type for a container action run
/// </summary>
public enum ContainerActionType
{
    /// <summary>
    /// No action type set
    /// </summary>
    NotSet = 0,

    /// <summary>
    /// Restart a container
    /// </summary>
    RestartContainer = 1,

    /// <summary>
    /// Redeploy a stack
    /// </summary>
    RedeployStack = 2,

    /// <summary>
    /// Update a service
    /// </summary>
    UpdateService = 3
}