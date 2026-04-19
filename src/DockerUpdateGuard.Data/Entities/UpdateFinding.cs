namespace DockerUpdateGuard.Data.Entities;

/// <summary>
/// Persisted update finding
/// </summary>
public class UpdateFinding
{
    #region Properties

    /// <summary>
    /// Entity identifier
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Related scan run identifier
    /// </summary>
    public Guid ScanRunId { get; set; }

    /// <summary>
    /// Image version the finding is about
    /// </summary>
    public Guid SubjectImageVersionId { get; set; }

    /// <summary>
    /// Optional recommended image version identifier
    /// </summary>
    public Guid? RecommendedImageVersionId { get; set; }

    /// <summary>
    /// Optional observed image identifier
    /// </summary>
    public Guid? ObservedImageId { get; set; }

    /// <summary>
    /// Optional runtime container snapshot identifier
    /// </summary>
    public Guid? ContainerSnapshotId { get; set; }

    /// <summary>
    /// Finding type
    /// </summary>
    public UpdateFindingType Type { get; set; }

    /// <summary>
    /// Indicates whether the finding is active
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Short summary
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Optional details
    /// </summary>
    public string? Details { get; set; }

    /// <summary>
    /// Detection timestamp
    /// </summary>
    public DateTimeOffset DetectedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Optional resolution timestamp
    /// </summary>
    public DateTimeOffset? ResolvedAtUtc { get; set; }

    /// <summary>
    /// Related scan run
    /// </summary>
    public ScanRun ScanRun { get; set; } = null!;

    /// <summary>
    /// Related subject image version
    /// </summary>
    public ImageVersion SubjectImageVersion { get; set; } = null!;

    /// <summary>
    /// Related recommended image version
    /// </summary>
    public ImageVersion? RecommendedImageVersion { get; set; }

    /// <summary>
    /// Related observed image
    /// </summary>
    public ObservedImage? ObservedImage { get; set; }

    /// <summary>
    /// Related container snapshot
    /// </summary>
    public ContainerSnapshot? ContainerSnapshot { get; set; }

    /// <summary>
    /// Tag candidates for the finding
    /// </summary>
    public ICollection<TagCandidate> TagCandidates { get; } = [];

    #endregion // Properties
}