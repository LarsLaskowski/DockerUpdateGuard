using System.Text.Json.Serialization;

namespace DockerUpdateGuard.Vulnerabilities.Data;

/// <summary>
/// Trivy scan response payload
/// </summary>
internal sealed record TrivyScanResponse
{
    #region Properties

    /// <summary>
    /// Scan results
    /// </summary>
    [JsonPropertyName("results")]
    public List<TrivyResult>? Results { get; init; }

    #endregion // Properties
}