namespace DockerUpdateGuard.Data.Queries;

/// <summary>
/// Observed image projection for shared base image views
/// </summary>
public class ObservedImageReferenceData
{
    #region Properties

    /// <summary>
    /// Observed image identifier
    /// </summary>
    public Guid ObservedImageId { get; set; }

    /// <summary>
    /// Observed image name
    /// </summary>
    public string ObservedImageName { get; set; } = string.Empty;

    /// <summary>
    /// Current image version identifier
    /// </summary>
    public Guid CurrentImageVersionId { get; set; }

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

    #endregion // Properties
}