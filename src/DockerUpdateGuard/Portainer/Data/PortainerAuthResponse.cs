using System.Text.Json.Serialization;

namespace DockerUpdateGuard.Portainer.Data;

/// <summary>
/// Portainer authentication request response containing the JWT token
/// </summary>
/// <param name="Jwt">JWT token returned by Portainer authentication</param>
internal sealed record PortainerAuthResponse([property: JsonPropertyName("jwt")] string? Jwt)
{
    /// <summary>
    /// Returns a string representation that omits the JWT to prevent token leakage
    /// </summary>
    /// <returns>String representation without the JWT</returns>
    public override string ToString()
    {
        return $"{nameof(PortainerAuthResponse)} {{ }}";
    }
}