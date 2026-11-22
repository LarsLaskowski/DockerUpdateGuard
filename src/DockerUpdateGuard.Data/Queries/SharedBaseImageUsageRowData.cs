namespace DockerUpdateGuard.Data.Queries;

/// <summary>
/// Flat row of the shared base image usage query
/// </summary>
internal sealed class SharedBaseImageUsageRowData
{
    #region Properties

    /// <summary>
    /// Base image version identifier
    /// </summary>
    public Guid BaseImageVersionId { get; init; }

    /// <summary>
    /// Registry name
    /// </summary>
    public string Registry { get; init; } = string.Empty;

    /// <summary>
    /// Repository path
    /// </summary>
    public string Repository { get; init; } = string.Empty;

    /// <summary>
    /// Tag value
    /// </summary>
    public string Tag { get; init; } = string.Empty;

    /// <summary>
    /// Digest value
    /// </summary>
    public string? Digest { get; init; }

    /// <summary>
    /// Optional source reference
    /// </summary>
    public string? SourceReference { get; init; }

    /// <summary>
    /// Observed image identifier
    /// </summary>
    public Guid ObservedImageId { get; init; }

    /// <summary>
    /// Current image version identifier
    /// </summary>
    public Guid CurrentImageVersionId { get; init; }

    #endregion // Properties
}