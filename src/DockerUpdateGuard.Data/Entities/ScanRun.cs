namespace DockerUpdateGuard.Data.Entities;

/// <summary>
/// Persisted scan run
/// </summary>
public class ScanRun
{
    #region Properties

    /// <summary>
    /// Entity identifier
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Scan type
    /// </summary>
    public ScanRunType Type { get; set; }

    /// <summary>
    /// Scan status
    /// </summary>
    public ScanRunStatus Status { get; set; }

    /// <summary>
    /// Scan trigger source
    /// </summary>
    public ScanTriggerSource TriggerSource { get; set; }

    /// <summary>
    /// Optional observed image identifier
    /// </summary>
    public Guid? ObservedImageId { get; set; }

    /// <summary>
    /// Optional Docker instance identifier
    /// </summary>
    public Guid? DockerInstanceId { get; set; }

    /// <summary>
    /// Scan start timestamp
    /// </summary>
    public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Optional completion timestamp
    /// </summary>
    public DateTimeOffset? CompletedAtUtc { get; set; }

    /// <summary>
    /// Optional correlation identifier
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Optional error message
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Optional detailed diagnostic payload
    /// </summary>
    public string? DiagnosticJson { get; set; }

    /// <summary>
    /// Related observed image
    /// </summary>
    public ObservedImage? ObservedImage { get; set; }

    /// <summary>
    /// Related Docker instance
    /// </summary>
    public DockerInstance? DockerInstance { get; set; }

    /// <summary>
    /// Related container snapshots
    /// </summary>
    public ICollection<ContainerSnapshot> ContainerSnapshots { get; } = [];

    /// <summary>
    /// Related image relationships
    /// </summary>
    public ICollection<ImageRelationship> ImageRelationships { get; } = [];

    /// <summary>
    /// Related update findings
    /// </summary>
    public ICollection<UpdateFinding> UpdateFindings { get; } = [];

    /// <summary>
    /// Related vulnerability findings
    /// </summary>
    public ICollection<VulnerabilityFinding> VulnerabilityFindings { get; } = [];

    #endregion // Properties
}