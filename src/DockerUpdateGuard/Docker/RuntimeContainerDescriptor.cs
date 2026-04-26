using DockerUpdateGuard.Data.Entities;

namespace DockerUpdateGuard.Docker;

/// <summary>
/// Runtime container descriptor from a Docker engine
/// </summary>
public class RuntimeContainerDescriptor
{
    #region Properties

    /// <summary>
    /// Container identifier
    /// </summary>
    public string ContainerId { get; set; } = string.Empty;

    /// <summary>
    /// Container name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Reported image reference
    /// </summary>
    public string ImageReference { get; set; } = string.Empty;

    /// <summary>
    /// Optional repository digest
    /// </summary>
    public string? ImageDigest { get; set; }

    /// <summary>
    /// Docker engine image identifier for local lookups
    /// </summary>
    public string? LocalImageId { get; set; }

    /// <summary>
    /// Optional compose project name
    /// </summary>
    public string? ComposeProject { get; set; }

    /// <summary>
    /// Optional stack name
    /// </summary>
    public string? StackName { get; set; }

    /// <summary>
    /// Optional service name
    /// </summary>
    public string? ServiceName { get; set; }

    /// <summary>
    /// Runtime status
    /// </summary>
    public ContainerRuntimeStatus RuntimeStatus { get; set; }

    /// <summary>
    /// Indicates whether the container is running
    /// </summary>
    public bool IsRunning { get; set; }

    #endregion // Properties
}