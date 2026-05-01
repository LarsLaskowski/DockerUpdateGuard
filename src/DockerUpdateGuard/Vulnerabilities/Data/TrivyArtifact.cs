using System.Text.Json.Serialization;

namespace DockerUpdateGuard.Vulnerabilities.Data;

/// <summary>
/// Trivy artifact descriptor payload
/// </summary>
internal sealed record TrivyArtifact
{
    #region Properties

    /// <summary>
    /// Artifact repository
    /// </summary>
    [JsonPropertyName("repository")]
    public string Repository { get; init; } = string.Empty;

    /// <summary>
    /// Artifact tag
    /// </summary>
    [JsonPropertyName("tag")]
    public string Tag { get; init; } = string.Empty;

    /// <summary>
    /// Artifact digest
    /// </summary>
    [JsonPropertyName("digest")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Digest { get; init; }

    #endregion // Properties
}