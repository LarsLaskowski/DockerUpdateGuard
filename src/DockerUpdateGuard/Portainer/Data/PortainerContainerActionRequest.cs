using DockerUpdateGuard.Data.Entities;

namespace DockerUpdateGuard.Portainer.Data;

/// <summary>
/// Portainer container action request
/// </summary>
public class PortainerContainerActionRequest
{
    #region Properties

    /// <summary>
    /// Action type
    /// </summary>
    public ContainerActionType ActionType { get; set; }

    /// <summary>
    /// Container identifier
    /// </summary>
    public string ContainerId { get; set; } = string.Empty;

    /// <summary>
    /// Container name
    /// </summary>
    public string ContainerName { get; set; } = string.Empty;

    #endregion // Properties
}