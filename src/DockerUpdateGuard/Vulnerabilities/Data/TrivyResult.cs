using System.Text.Json.Serialization;

namespace DockerUpdateGuard.Vulnerabilities.Data;

/// <summary>
/// Trivy scan result payload
/// </summary>
internal sealed record TrivyResult
{
    #region Properties

    /// <summary>
    /// Scanned target the result block belongs to
    /// </summary>
    [JsonPropertyName("Target")]
    public string? Target { get; init; }

    /// <summary>
    /// Package class of the result block (e.g. os-pkgs, lang-pkgs)
    /// </summary>
    [JsonPropertyName("Class")]
    public string? Class { get; init; }

    /// <summary>
    /// Distribution or ecosystem of the result block (e.g. alpine, gobinary)
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