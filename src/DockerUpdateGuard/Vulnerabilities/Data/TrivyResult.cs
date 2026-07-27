using System.Text.Json.Serialization;

namespace DockerUpdateGuard.Vulnerabilities.Data;

/// <summary>
/// Trivy scan result payload
/// </summary>
internal sealed record TrivyResult
{
    #region Properties

    /// <summary>
    /// Scan target the result block belongs to
    /// </summary>
    [JsonPropertyName("Target")]
    public string? Target { get; init; }

    /// <summary>
    /// Package class of the result block (e.g. os-pkgs)
    /// </summary>
    [JsonPropertyName("Class")]
    public string? Class { get; init; }

    /// <summary>
    /// Package type of the result block (e.g. alpine)
    /// </summary>
    [JsonPropertyName("Type")]
    public string? Type { get; init; }

    /// <summary>
    /// Vulnerabilities
    /// </summary>
    [JsonPropertyName("Vulnerabilities")]
    public List<TrivyVulnerability>? Vulnerabilities { get; init; }

    #endregion // Properties
}