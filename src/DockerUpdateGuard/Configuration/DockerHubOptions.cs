using System.ComponentModel.DataAnnotations;

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
    [Range(1, 300)]
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum logical request parallelism
    /// </summary>
    [Range(1, 32)]
    public int MaxParallelRequests { get; set; } = 4;

    #endregion // Properties
}