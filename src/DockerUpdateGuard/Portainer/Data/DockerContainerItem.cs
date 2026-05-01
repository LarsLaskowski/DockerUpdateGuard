using System.Text.Json.Serialization;

namespace DockerUpdateGuard.Portainer.Data;

/// <summary>
/// Docker container item containing identifier and names, as returned by the Portainer Docker API when filtering containers
/// </summary>
/// <param name="Id">Identifier of the Docker container</param>
/// <param name="Names">Names of the Docker container</param>
internal sealed record DockerContainerItem([property: JsonPropertyName("Id")] string? Id,
                                           [property: JsonPropertyName("Names")] string[]? Names);