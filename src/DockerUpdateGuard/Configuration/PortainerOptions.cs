using System.ComponentModel.DataAnnotations;

namespace DockerUpdateGuard.Configuration;

/// <summary>
/// Portainer options
/// </summary>
public class PortainerOptions
{
    #region Properties

    /// <summary>
    /// Indicates whether Portainer integration is enabled
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Portainer base URL
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Optional username
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Optional password or token secret
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Optional external endpoint identifier
    /// </summary>
    public string? EndpointId { get; set; }

    /// <summary>
    /// Request timeout in seconds
    /// </summary>
    [Range(1, 300)]
    public int RequestTimeoutSeconds { get; set; } = 15;

    #endregion // Properties
}