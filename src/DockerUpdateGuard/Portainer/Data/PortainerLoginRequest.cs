using System.Text.Json.Serialization;

namespace DockerUpdateGuard.Portainer.Data;

/// <summary>
/// Login request body for Portainer JWT authentication
/// </summary>
/// <param name="Username">Username for Portainer login</param>
/// <param name="Password">Password for Portainer login</param>
internal sealed record PortainerLoginRequest([property: JsonPropertyName("username")] string Username,
                                             [property: JsonPropertyName("password")] string Password)
{
    #region Object

    /// <inheritdoc/>
    public override string ToString()
    {
        return $"{nameof(PortainerLoginRequest)} {{ {nameof(Username)} = {Username} }}";
    }

    #endregion // Object
}