using DockerUpdateGuard.Data.Entities;

namespace DockerUpdateGuard.Portainer.Data;

/// <summary>
/// Availability of container actions for a container
/// </summary>
public class ContainerActionAvailabilityData
{
    #region Properties

    /// <summary>
    /// Indicates whether the container actions section is shown
    /// </summary>
    public bool IsShown { get; set; }

    /// <summary>
    /// Indicates whether the container is current
    /// </summary>
    public bool IsCurrent { get; set; }

    /// <summary>
    /// Indicates whether actions are supported
    /// </summary>
    public bool IsSupported { get; set; }

    /// <summary>
    /// Indicates whether an action is in progress for the container
    /// </summary>
    public bool IsInProgress { get; set; }

    /// <summary>
    /// Indicates whether the container is the application's own container
    /// </summary>
    public bool IsOwnContainer { get; set; }

    /// <summary>
    /// Identifier of the Portainer environment
    /// </summary>
    public string? EndpointId { get; set; }

    /// <summary>
    /// Message explaining the availability
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Actions offered for the container
    /// </summary>
    public IReadOnlyList<ContainerActionType> OfferedActions { get; set; } = [];

    #endregion // Properties
}