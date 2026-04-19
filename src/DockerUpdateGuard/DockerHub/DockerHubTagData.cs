namespace DockerUpdateGuard.DockerHub;

/// <summary>
/// Docker Hub tag metadata
/// </summary>
public class DockerHubTagData
{
    #region Properties

    /// <summary>
    /// Tag value
    /// </summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>
    /// Optional digest value
    /// </summary>
    public string? Digest { get; set; }

    /// <summary>
    /// Optional publication timestamp
    /// </summary>
    public DateTimeOffset? PublishedAtUtc { get; set; }

    #endregion // Properties
}