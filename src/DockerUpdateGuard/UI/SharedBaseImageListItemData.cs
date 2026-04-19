namespace DockerUpdateGuard.UI;

/// <summary>
/// Shared base image view data
/// </summary>
public class SharedBaseImageListItemData
{
    #region Properties

    public Guid BaseImageVersionId { get; set; }

    public string ImageReference { get; set; } = string.Empty;

    public int ObservedImageCount { get; set; }

    public int ActiveFindingCount { get; set; }

    #endregion // Properties
}