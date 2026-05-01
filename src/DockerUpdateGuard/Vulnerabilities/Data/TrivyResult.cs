using System.Text.Json.Serialization;

namespace DockerUpdateGuard.Vulnerabilities.Data;

/// <summary>
/// Trivy scan result payload
/// </summary>
internal sealed record TrivyResult
{
    #region Properties

    /// <summary>
    /// Vulnerabilities
    /// </summary>
    [JsonPropertyName("vulnerabilities")]
    public List<TrivyVulnerability>? Vulnerabilities { get; init; }

    #endregion // Properties
}