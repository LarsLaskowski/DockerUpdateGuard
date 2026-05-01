using System.Text.Json.Serialization;

namespace DockerUpdateGuard.Vulnerabilities.Data;

/// <summary>
/// Docker Hub login response containing the authentication token
/// </summary>
internal sealed record HubLoginResponse
{
    #region Properties

    /// <summary>
    /// Authentication token
    /// </summary>
    [JsonPropertyName("token")]
    public string? Token { get; init; }

    #endregion // Properties
}