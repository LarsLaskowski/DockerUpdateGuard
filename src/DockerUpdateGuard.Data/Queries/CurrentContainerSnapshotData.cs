namespace DockerUpdateGuard.Data.Queries;

/// <summary>
/// Key row of a currently known container snapshot
/// </summary>
public class CurrentContainerSnapshotData
{
    #region Properties

    /// <summary>
    /// Snapshot identifier
    /// </summary>
    public Guid SnapshotId { get; set; }

    /// <summary>
    /// Docker instance identifier
    /// </summary>
    public Guid DockerInstanceId { get; set; }

    /// <summary>
    /// Container identifier
    /// </summary>
    public string ContainerId { get; set; } = string.Empty;

    /// <summary>
    /// Image version identifier
    /// </summary>
    public Guid ImageVersionId { get; set; }

    #endregion // Properties
}