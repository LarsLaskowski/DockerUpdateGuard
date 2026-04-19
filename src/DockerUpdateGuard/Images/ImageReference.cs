namespace DockerUpdateGuard.Images;

/// <summary>
/// Parsed Docker image reference
/// </summary>
public class ImageReference
{
    #region Properties

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
    public string Tag { get; set; } = "latest";

    /// <summary>
    /// Optional digest value
    /// </summary>
    public string? Digest { get; set; }

    /// <summary>
    /// Full normalized image reference
    /// </summary>
    public string FullReference => string.IsNullOrWhiteSpace(Digest) ? $"{Registry}/{Repository}:{Tag}" : $"{Registry}/{Repository}:{Tag}@{Digest}";

    #endregion // Properties
}