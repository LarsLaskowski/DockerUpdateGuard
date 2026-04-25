namespace DockerUpdateGuard.Data.Entities;

/// <summary>
/// Unique image version normalized by repository, tag and digest
/// </summary>
public class ImageVersion
{
    #region Properties

    /// <summary>
    /// Entity identifier
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Related registry repository identifier
    /// </summary>
    public Guid RegistryRepositoryId { get; set; }

    /// <summary>
    /// Tag value
    /// </summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>
    /// Optional digest value
    /// </summary>
    public string? Digest { get; set; }

    /// <summary>
    /// Optional publication timestamp
    /// </summary>
    public DateTimeOffset? PublishedAtUtc { get; set; }

    /// <summary>
    /// Version source
    /// </summary>
    public ImageVersionSource Source { get; set; }

    /// <summary>
    /// Creation timestamp
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Last update timestamp
    /// </summary>
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Optional raw metadata payload
    /// </summary>
    public string? MetadataJson { get; set; }

    /// <summary>
    /// Vulnerability assessment status
    /// </summary>
    public VulnerabilityAssessmentStatus VulnerabilityAssessmentStatus { get; set; }

    /// <summary>
    /// Vulnerability provider/source used for the latest assessment
    /// </summary>
    public VulnerabilitySource VulnerabilityAssessmentSource { get; set; }

    /// <summary>
    /// Optional vulnerability assessment message
    /// </summary>
    public string? VulnerabilityAssessmentMessage { get; set; }

    /// <summary>
    /// Latest vulnerability assessment timestamp
    /// </summary>
    public DateTimeOffset? VulnerabilityAssessmentCheckedAtUtc { get; set; }

    /// <summary>
    /// Related registry repository
    /// </summary>
    public RegistryRepository RegistryRepository { get; set; } = null!;

    /// <summary>
    /// Observed images pointing to this version
    /// </summary>
    public ICollection<ObservedImage> ObservedImages { get; } = [];

    /// <summary>
    /// Runtime snapshots pointing to this version
    /// </summary>
    public ICollection<ContainerSnapshot> ContainerSnapshots { get; } = [];

    /// <summary>
    /// Relationships where this image is the child
    /// </summary>
    public ICollection<ImageRelationship> ChildRelationships { get; } = [];

    /// <summary>
    /// Relationships where this image is the base
    /// </summary>
    public ICollection<ImageRelationship> BaseRelationships { get; } = [];

    /// <summary>
    /// Update findings for this image as subject
    /// </summary>
    public ICollection<UpdateFinding> SubjectUpdateFindings { get; } = [];

    /// <summary>
    /// Update findings recommending this image
    /// </summary>
    public ICollection<UpdateFinding> RecommendedByUpdateFindings { get; } = [];

    /// <summary>
    /// Vulnerability findings for this image
    /// </summary>
    public ICollection<VulnerabilityFinding> VulnerabilityFindings { get; } = [];

    #endregion // Properties
}