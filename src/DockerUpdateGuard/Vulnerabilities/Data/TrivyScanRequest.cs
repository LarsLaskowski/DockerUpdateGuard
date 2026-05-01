using System.Text.Json.Serialization;

namespace DockerUpdateGuard.Vulnerabilities.Data;

/// <summary>
/// Trivy scan request payload
/// </summary>
internal sealed record TrivyScanRequest
{
    #region Properties

    /// <summary>
    /// Artifact to scan
    /// </summary>
    [JsonPropertyName("artifact")]
    public TrivyArtifact Artifact { get; init; } = new();

    /// <summary>
    /// Scan options
    /// </summary>
    [JsonPropertyName("options")]
    public TrivyScanOptions Options { get; init; } = new();

    #endregion // Properties
}