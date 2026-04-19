namespace DockerUpdateGuard.UI;

/// <summary>
/// Base image relationship view data
/// </summary>
public class BaseImageRelationshipData
{
    #region Properties

    public string ImageReference { get; set; } = string.Empty;

    public int Depth { get; set; }

    public string? SourceReference { get; set; }

    #endregion // Properties
}