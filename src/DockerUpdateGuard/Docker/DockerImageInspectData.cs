namespace DockerUpdateGuard.Docker;

/// <summary>
/// Reduced Docker image inspect payload
/// </summary>
public class DockerImageInspectData
{
    #region Properties

    /// <summary>
    /// Local image identifier
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Repository tags
    /// </summary>
    public IReadOnlyList<string> RepoTags { get; set; } = [];

    /// <summary>
    /// Repository digests
    /// </summary>
    public IReadOnlyList<string> RepoDigests { get; set; } = [];

    /// <summary>
    /// Configured environment variables
    /// </summary>
    public IReadOnlyList<string> EnvironmentVariables { get; set; } = [];

    /// <summary>
    /// Root filesystem layers
    /// </summary>
    public IReadOnlyList<string> RootFsLayers { get; set; } = [];

    /// <summary>
    /// Creation timestamp
    /// </summary>
    public DateTimeOffset? CreatedAtUtc { get; set; }

    /// <summary>
    /// Reported operating system
    /// </summary>
    public string? OperatingSystem { get; set; }

    /// <summary>
    /// Reported architecture
    /// </summary>
    public string? Architecture { get; set; }

    #endregion // Properties
}