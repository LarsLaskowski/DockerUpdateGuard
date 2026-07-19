using System.Text.Json.Serialization;

namespace DockerUpdateGuard.Vulnerabilities.Data;

/// <summary>
/// Trivy CVSS metric payload of a single vendor
/// </summary>
internal sealed record TrivyCvssData
{
    #region Properties

    /// <summary>
    /// CVSS v2 score
    /// </summary>
    [JsonPropertyName("V2Score")]
    public decimal? V2Score { get; init; }

    /// <summary>
    /// CVSS v3 score
    /// </summary>
    [JsonPropertyName("V3Score")]
    public decimal? V3Score { get; init; }

    #endregion // Properties
}