namespace DockerUpdateGuard.UI;

/// <summary>
/// Shared base image view data
/// </summary>
public class SharedBaseImageListItemData
{
    #region Properties

    /// <summary>
    /// Base image version identifier
    /// </summary>
    public Guid BaseImageVersionId { get; set; }

    /// <summary>
    /// Image reference
    /// </summary>
    public string ImageReference { get; set; } = string.Empty;

    /// <summary>
    /// Number of observed images
    /// </summary>
    public int ObservedImageCount { get; set; }

    /// <summary>
    /// Number of active findings
    /// </summary>
    public int ActiveFindingCount { get; set; }

    #endregion // Properties
}