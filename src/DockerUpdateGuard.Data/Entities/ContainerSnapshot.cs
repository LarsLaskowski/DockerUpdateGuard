namespace DockerUpdateGuard.Data.Entities;

/// <summary>
/// Snapshot of a running or discovered container
/// </summary>
public class ContainerSnapshot
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
    /// Related normalized image version identifier
    /// </summary>
    public Guid ImageVersionId { get; set; }

    /// <summary>
    /// Optional scan run identifier
    /// </summary>
    public Guid? ScanRunId { get; set; }

    /// <summary>
    /// Container identifier reported by the runtime
    /// </summary>
    public string ContainerId { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the container
    /// </summary>
    public string Name { get; set; } = string.Empty;

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
    public ContainerRuntimeStatus Status { get; set; }

    /// <summary>
    /// Indicates whether the container is running
    /// </summary>
    public bool IsRunning { get; set; }

    /// <summary>
    /// Update assessment status for this snapshot
    /// </summary>
    public UpdateAssessmentStatus UpdateAssessmentStatus { get; set; }

    /// <summary>
    /// Optional update assessment message
    /// </summary>
    public string? UpdateAssessmentMessage { get; set; }

    /// <summary>
    /// Snapshot timestamp
    /// </summary>
    public DateTimeOffset RecordedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Optional container start timestamp
    /// </summary>
    public DateTimeOffset? StartedAtUtc { get; set; }

    /// <summary>
    /// Related Docker instance
    /// </summary>
    public DockerInstance DockerInstance { get; set; } = null!;

    /// <summary>
    /// Related normalized image version
    /// </summary>
    public ImageVersion ImageVersion { get; set; } = null!;

    /// <summary>
    /// Related scan run
    /// </summary>
    public ScanRun? ScanRun { get; set; }

    /// <summary>
    /// Related update findings
    /// </summary>
    public ICollection<UpdateFinding> UpdateFindings { get; } = [];

    /// <summary>
    /// Related action runs
    /// </summary>
    public ICollection<ContainerActionRun> ContainerActionRuns { get; } = [];

    #endregion // Properties
}