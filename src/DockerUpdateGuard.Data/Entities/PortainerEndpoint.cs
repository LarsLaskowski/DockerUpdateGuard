namespace DockerUpdateGuard.Data.Entities;

/// <summary>
/// Optional Portainer endpoint for a Docker instance
/// </summary>
public class PortainerEndpoint
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
    /// Endpoint name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Portainer base URL
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Optional external Portainer endpoint identifier
    /// </summary>
    public string? ExternalEndpointId { get; set; }

    /// <summary>
    /// Indicates whether Portainer integration is enabled
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Creation timestamp
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Last update timestamp
    /// </summary>
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Related Docker instance
    /// </summary>
    public DockerInstance DockerInstance { get; set; } = null!;

    /// <summary>
    /// Related action runs
    /// </summary>
    public ICollection<ContainerActionRun> ContainerActionRuns { get; } = [];

    #endregion // Properties
}