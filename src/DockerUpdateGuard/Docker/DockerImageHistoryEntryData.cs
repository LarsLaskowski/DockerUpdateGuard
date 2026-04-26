namespace DockerUpdateGuard.Docker;

/// <summary>
/// Reduced Docker image history entry
/// </summary>
public class DockerImageHistoryEntryData
{
    #region Properties

    /// <summary>
    /// Creation timestamp
    /// </summary>
    public DateTimeOffset? CreatedAtUtc { get; set; }

    /// <summary>
    /// Creation command
    /// </summary>
    public string? CreatedBy { get; set; }

    /// <summary>
    /// Entry comment
    /// </summary>
    public string? Comment { get; set; }

    /// <summary>
    /// Reported tags
    /// </summary>
    public IReadOnlyList<string> Tags { get; set; } = [];

    #endregion // Properties
}