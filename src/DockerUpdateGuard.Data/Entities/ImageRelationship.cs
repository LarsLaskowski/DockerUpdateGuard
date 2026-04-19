namespace DockerUpdateGuard.Data.Entities;

/// <summary>
/// Relationship between a child image and a base image
/// </summary>
public class ImageRelationship
{
    #region Properties

    /// <summary>
    /// Entity identifier
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Child image version identifier
    /// </summary>
    public Guid ChildImageVersionId { get; set; }

    /// <summary>
    /// Base image version identifier
    /// </summary>
    public Guid BaseImageVersionId { get; set; }

    /// <summary>
    /// Optional originating scan run identifier
    /// </summary>
    public Guid? ScanRunId { get; set; }

    /// <summary>
    /// Relationship type
    /// </summary>
    public ImageRelationshipType RelationshipType { get; set; }

    /// <summary>
    /// Depth within the dependency chain
    /// </summary>
    public int Depth { get; set; }

    /// <summary>
    /// Optional source reference from the scan
    /// </summary>
    public string? SourceReference { get; set; }

    /// <summary>
    /// Creation timestamp
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Child image version
    /// </summary>
    public ImageVersion ChildImageVersion { get; set; } = null!;

    /// <summary>
    /// Base image version
    /// </summary>
    public ImageVersion BaseImageVersion { get; set; } = null!;

    /// <summary>
    /// Related scan run
    /// </summary>
    public ScanRun? ScanRun { get; set; }

    #endregion // Properties
}