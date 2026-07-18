using System.Text.Json.Serialization;

namespace DockerUpdateGuard.Vulnerabilities.Data;

/// <summary>
/// Trivy JSON report payload produced by the Trivy CLI
/// </summary>
internal sealed record TrivyReport
{
    #region Properties

    /// <summary>
    /// Scan results
    /// </summary>
    [JsonPropertyName("Results")]
    public List<TrivyResult>? Results { get; init; }

    #endregion // Properties
}