using System.Text.Json.Serialization;

namespace DockerUpdateGuard.Portainer.Data;

/// <summary>
/// Portainer endpoint item containing identifier and name, as returned by the /api/endpoints endpoint
/// </summary>
/// <param name="Id">Identifier of the Portainer endpoint</param>
/// <param name="Name">Name of the Portainer endpoint</param>
internal sealed record PortainerEndpointItem([property: JsonPropertyName("Id")] int Id,
                                             [property: JsonPropertyName("Name")] string? Name);