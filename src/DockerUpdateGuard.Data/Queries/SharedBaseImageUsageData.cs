namespace DockerUpdateGuard.Data.Queries;

/// <summary>
/// Shared base image usage summary
/// </summary>
public class SharedBaseImageUsageData
{
    #region Properties

    /// <summary>
    /// Base image version identifier
    /// </summary>
    public Guid BaseImageVersionId { get; set; }

    /// <summary>
    /// Grouped base-image version identifiers
    /// </summary>
    public IReadOnlyList<Guid> BaseImageVersionIds { get; set; } = [];

    /// <summary>
    /// Registry name
    /// </summary>
    public string Registry { get; set; } = string.Empty;

    /// <summary>
    /// Repository path
    /// </summary>
    public string Repository { get; set; } = string.Empty;

    /// <summary>
    /// Tag value
    /// </summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>
    /// Optional digest value
    /// </summary>
    public string? Digest { get; set; }

    /// <summary>
    /// Source references used to disambiguate unresolved base images
    /// </summary>
    public IReadOnlyList<string> SourceReferences { get; set; } = [];

    /// <summary>
    /// Number of observed images using the base image
    /// </summary>
    public int ObservedImageCount { get; set; }

    #endregion // Properties
}