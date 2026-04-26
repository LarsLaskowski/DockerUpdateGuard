namespace DockerUpdateGuard.UI;

/// <summary>
/// Linked runtime container summary for an observed image
/// </summary>
public class LinkedRuntimeContainerViewData
{
    #region Properties

    /// <summary>
    /// Docker instance identifier
    /// </summary>
    public Guid DockerInstanceId { get; set; }

    /// <summary>
    /// Container identifier
    /// </summary>
    public string ContainerId { get; set; } = string.Empty;

    /// <summary>
    /// Docker instance name
    /// </summary>
    public string DockerInstanceName { get; set; } = string.Empty;

    /// <summary>
    /// Container name
    /// </summary>
    public string ContainerName { get; set; } = string.Empty;

    /// <summary>
    /// Runtime status
    /// </summary>
    public string RuntimeStatus { get; set; } = string.Empty;

    /// <summary>
    /// Image reference
    /// </summary>
    public string ImageReference { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when the link was recorded
    /// </summary>
    public DateTimeOffset RecordedAtUtc { get; set; }

    #endregion // Properties
}