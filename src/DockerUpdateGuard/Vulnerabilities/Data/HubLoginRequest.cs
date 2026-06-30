using System.Text.Json.Serialization;

namespace DockerUpdateGuard.Vulnerabilities.Data;

/// <summary>
/// Docker Hub login request payload for Docker Scout authentication
/// </summary>
internal sealed record HubLoginRequest
{
    #region Properties

    /// <summary>
    /// Docker Hub user name
    /// </summary>
    [JsonPropertyName("username")]
    public string Username { get; init; } = string.Empty;

    /// <summary>
    /// Docker Hub password or personal access token
    /// </summary>
    [JsonPropertyName("password")]
    public string Password { get; init; } = string.Empty;

    #endregion // Properties

    #region Object

    /// <inheritdoc/>
    public override string ToString()
    {
        return $"{nameof(HubLoginRequest)} {{ {nameof(Username)} = {Username} }}";
    }

    #endregion // Object
}