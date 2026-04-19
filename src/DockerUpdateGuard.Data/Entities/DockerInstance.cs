namespace DockerUpdateGuard.Data.Entities;

/// <summary>
/// Configured Docker runtime instance
/// </summary>
public class DockerInstance
{
    #region Properties

    /// <summary>
    /// Entity identifier
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Instance name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Engine endpoint URI
    /// </summary>
    public string EndpointUri { get; set; } = string.Empty;

    /// <summary>
    /// Connection kind
    /// </summary>
    public DockerConnectionKind ConnectionKind { get; set; }

    /// <summary>
    /// Indicates whether the instance is enabled
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Registration source
    /// </summary>
    public RegistrationSource Source { get; set; } = RegistrationSource.ConfigurationFile;

    /// <summary>
    /// Indicates whether certificate validation is skipped
    /// </summary>
    public bool SkipCertificateValidation { get; set; }

    /// <summary>
    /// Creation timestamp
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Last update timestamp
    /// </summary>
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Optional Portainer endpoint
    /// </summary>
    public PortainerEndpoint? PortainerEndpoint { get; set; }

    /// <summary>
    /// Observed container snapshots
    /// </summary>
    public ICollection<ContainerSnapshot> ContainerSnapshots { get; } = [];

    /// <summary>
    /// Related scan runs
    /// </summary>
    public ICollection<ScanRun> ScanRuns { get; } = [];

    /// <summary>
    /// Related action runs
    /// </summary>
    public ICollection<ContainerActionRun> ContainerActionRuns { get; } = [];

    #endregion // Properties
}