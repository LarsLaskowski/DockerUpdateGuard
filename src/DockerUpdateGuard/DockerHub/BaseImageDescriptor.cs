namespace DockerUpdateGuard.DockerHub;

/// <summary>
/// Base image descriptor
/// </summary>
public class BaseImageDescriptor
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
    public string Tag { get; set; } = string.Empty;

    /// <summary>
    /// Optional digest value
    /// </summary>
    public string? Digest { get; set; }

    /// <summary>
    /// Relationship depth
    /// </summary>
    public int Depth { get; set; } = 1;

    /// <summary>
    /// Optional source reference
    /// </summary>
    public string? SourceReference { get; set; }

    #endregion // Properties
}