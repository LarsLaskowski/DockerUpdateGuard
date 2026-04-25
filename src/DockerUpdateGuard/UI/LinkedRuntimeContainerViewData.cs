namespace DockerUpdateGuard.UI;

/// <summary>
/// Linked runtime container summary for an observed image
/// </summary>
public class LinkedRuntimeContainerViewData
{
    #region Properties

    public Guid DockerInstanceId { get; set; }

    public string ContainerId { get; set; } = string.Empty;

    public string DockerInstanceName { get; set; } = string.Empty;

    public string ContainerName { get; set; } = string.Empty;

    public string RuntimeStatus { get; set; } = string.Empty;

    public string ImageReference { get; set; } = string.Empty;

    public DateTimeOffset RecordedAtUtc { get; set; }

    #endregion // Properties
}