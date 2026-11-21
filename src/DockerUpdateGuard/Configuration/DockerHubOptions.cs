namespace DockerUpdateGuard.Configuration;

/// <summary>
/// Docker Hub integration options
/// </summary>
public class DockerHubOptions
{
    #region Properties

    /// <summary>
    /// Docker Hub registry host used for account discovery, one of docker.io, index.docker.io, registry-1.docker.io, hub.docker.com, docker.com or www.docker.com; it never changes the address of the web API requests
    /// </summary>
    public string Registry { get; set; } = "docker.io";

    /// <summary>
    /// Base address of the Docker Hub web API used for registries served by Docker Hub; the only setting that changes where the web API requests and the login go
    /// </summary>
    public string ApiBaseUrl { get; set; } = "https://hub.docker.com/";

    /// <summary>
    /// Docker Hub user name used for authenticated API requests
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// Optional personal access token
    /// </summary>
    public string? Pat { get; set; }

    /// <summary>
    /// Outbound timeout in seconds
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum number of Docker Hub requests in flight at once for the whole process
    /// </summary>
    public int MaxParallelRequests { get; set; } = 4;

    #endregion // Properties
}