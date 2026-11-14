using System.Text.Json.Serialization;

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

    /// <summary>
    /// Digests of the platform images of the tag
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<string> PlatformDigests { get; set; } = [];

    #endregion // Properties
}