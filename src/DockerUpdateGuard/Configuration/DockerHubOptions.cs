namespace DockerUpdateGuard.Configuration;

/// <summary>
/// Docker Hub integration options
/// </summary>
public class DockerHubOptions
{
    #region Properties

    /// <summary>
    /// Registry host name
    /// </summary>
    public string Registry { get; set; } = "docker.io";

    /// <summary>
    /// Base address of the Docker Hub API used for registries served by Docker Hub
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
    /// Maximum logical request parallelism
    /// </summary>
    public int MaxParallelRequests { get; set; } = 4;

    #endregion // Properties
}