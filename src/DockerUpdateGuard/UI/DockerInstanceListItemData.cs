namespace DockerUpdateGuard.UI;

/// <summary>
/// Docker instance list item
/// </summary>
public class DockerInstanceListItemData
{
    #region Properties

    public string Name { get; set; } = string.Empty;

    public string EndpointUri { get; set; } = string.Empty;

    public string ConnectionKind { get; set; } = string.Empty;

    public bool PortainerEnabled { get; set; }

    public string LatestScanStatus { get; set; } = "NotScanned";

    public DateTimeOffset? LatestScanCompletedAtUtc { get; set; }

    public int RuntimeContainerCount { get; set; }

    #endregion // Properties
}