namespace DockerUpdateGuard.UI;

/// <summary>
/// Base image relationship view data
/// </summary>
public class BaseImageRelationshipData
{
    #region Properties

    /// <summary>
    /// Image reference
    /// </summary>
    public string ImageReference { get; set; } = string.Empty;

    /// <summary>
    /// Relationship depth
    /// </summary>
    public int Depth { get; set; }

    /// <summary>
    /// Source image reference
    /// </summary>
    public string? SourceReference { get; set; }

    #endregion // Properties
}