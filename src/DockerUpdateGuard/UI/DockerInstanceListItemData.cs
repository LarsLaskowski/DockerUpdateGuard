namespace DockerUpdateGuard.UI;

/// <summary>
/// Docker instance list item
/// </summary>
public class DockerInstanceListItemData
{
    #region Properties

    /// <summary>
    /// Docker instance identifier
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Docker instance name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Docker endpoint URI
    /// </summary>
    public string EndpointUri { get; set; } = string.Empty;

    /// <summary>
    /// Connection kind
    /// </summary>
    public string ConnectionKind { get; set; } = string.Empty;

    /// <summary>
    /// Whether Portainer integration is enabled
    /// </summary>
    public bool PortainerEnabled { get; set; }

    /// <summary>
    /// Latest scan status
    /// </summary>
    public string LatestScanStatus { get; set; } = "NotScanned";

    /// <summary>
    /// Timestamp when the latest scan completed
    /// </summary>
    public DateTimeOffset? LatestScanCompletedAtUtc { get; set; }

    /// <summary>
    /// Number of runtime containers
    /// </summary>
    public int RuntimeContainerCount { get; set; }

    /// <summary>
    /// Current resource usage
    /// </summary>
    public ResourceUsagePointViewData? CurrentResourceUsage { get; set; }

    #endregion // Properties
}