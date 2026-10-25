namespace DockerUpdateGuard.Portainer.Data;

/// <summary>
/// Result of resolving the Portainer environment of a Docker instance; exactly one of the two values is set
/// </summary>
/// <param name="EndpointId">Identifier of the resolved Portainer environment, or null when none could be used</param>
/// <param name="FailureMessage">Message explaining why no environment could be used, or null when one was resolved</param>
internal sealed record PortainerEndpointResolution(string? EndpointId, string? FailureMessage);