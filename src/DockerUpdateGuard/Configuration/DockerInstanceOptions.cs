using System.ComponentModel.DataAnnotations;

namespace DockerUpdateGuard.Configuration;

/// <summary>
/// Docker instance configuration
/// </summary>
public class DockerInstanceOptions
{
    #region Properties

    /// <summary>
    /// Display name
    /// </summary>
    [Required]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Configured Docker base URL
    /// </summary>
    [Required]
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Indicates whether the instance is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Indicates whether TLS should be used for tcp endpoints
    /// </summary>
    public bool UseTls { get; set; }

    /// <summary>
    /// Indicates whether server certificate validation is skipped
    /// </summary>
    public bool SkipCertificateValidation { get; set; }

    /// <summary>
    /// Optional client certificate path
    /// </summary>
    public string? CertificatePath { get; set; }

    /// <summary>
    /// Docker engine request timeout in seconds
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// Optional Portainer configuration
    /// </summary>
    public PortainerOptions Portainer { get; set; } = new();

    #endregion // Properties
}