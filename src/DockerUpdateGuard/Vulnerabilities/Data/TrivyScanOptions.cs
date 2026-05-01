using System.Text.Json.Serialization;

namespace DockerUpdateGuard.Vulnerabilities.Data;

/// <summary>
/// Trivy scan options payload
/// </summary>
internal sealed record TrivyScanOptions
{
    #region Properties

    /// <summary>
    /// Enabled scanners
    /// </summary>
    [JsonPropertyName("scanners")]
    public List<string> Scanners { get; init; } = [];

    /// <summary>
    /// Vulnerability types
    /// </summary>
    [JsonPropertyName("vulnType")]
    public List<string> VulnerabilityTypes { get; init; } = [];

    #endregion // Properties
}