namespace DockerUpdateGuard.Data.Entities;

/// <summary>
/// Persisted action run for a container or stack
/// </summary>
public class ContainerActionRun
{
    #region Properties

    /// <summary>
    /// Entity identifier
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Related Docker instance identifier
    /// </summary>
    public Guid DockerInstanceId { get; set; }

    /// <summary>
    /// Optional Portainer endpoint identifier
    /// </summary>
    public Guid? PortainerEndpointId { get; set; }

    /// <summary>
    /// Optional container snapshot identifier
    /// </summary>
    public Guid? ContainerSnapshotId { get; set; }

    /// <summary>
    /// Requested action type
    /// </summary>
    public ContainerActionType ActionType { get; set; }

    /// <summary>
    /// Target resource type
    /// </summary>
    public PortainerResourceType ResourceType { get; set; }

    /// <summary>
    /// Action status
    /// </summary>
    public ContainerActionStatus Status { get; set; }

    /// <summary>
    /// Resource name
    /// </summary>
    public string ResourceName { get; set; } = string.Empty;

    /// <summary>
    /// Optional requester name
    /// </summary>
    public string? RequestedBy { get; set; }

    /// <summary>
    /// Optional external Portainer task identifier
    /// </summary>
    public string? PortainerTaskId { get; set; }

    /// <summary>
    /// Optional error message
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Request timestamp
    /// </summary>
    public DateTimeOffset RequestedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Optional completion timestamp
    /// </summary>
    public DateTimeOffset? CompletedAtUtc { get; set; }

    /// <summary>
    /// Related Docker instance
    /// </summary>
    public DockerInstance DockerInstance { get; set; } = null!;

    /// <summary>
    /// Related Portainer endpoint
    /// </summary>
    public PortainerEndpoint? PortainerEndpoint { get; set; }

    /// <summary>
    /// Related container snapshot
    /// </summary>
    public ContainerSnapshot? ContainerSnapshot { get; set; }

    #endregion // Properties
}