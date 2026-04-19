namespace DockerUpdateGuard.Data.Entities;

/// <summary>
/// Portainer resource type for an action
/// </summary>
public enum PortainerResourceType
{
    /// <summary>
    /// No resource type set
    /// </summary>
    NotSet = 0,

    /// <summary>
    /// Container resource
    /// </summary>
    Container = 1,

    /// <summary>
    /// Service resource
    /// </summary>
    Service = 2,

    /// <summary>
    /// Stack resource
    /// </summary>
    Stack = 3
}