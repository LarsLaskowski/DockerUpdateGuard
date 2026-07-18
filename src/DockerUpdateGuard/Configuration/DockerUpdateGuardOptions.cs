namespace DockerUpdateGuard.Configuration;

/// <summary>
/// Root application options
/// </summary>
public class DockerUpdateGuardOptions
{
    #region Const fields

    /// <summary>
    /// Root configuration section name
    /// </summary>
    public const string SectionName = "DockerUpdateGuard";

    #endregion // Const fields

    #region Properties

    /// <summary>
    /// Optional inline connection string
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Named connection string entry
    /// </summary>
    public string ConnectionStringName { get; set; } = "DockerUpdateGuard";

    /// <summary>
    /// Database connection and startup migration configuration
    /// </summary>
    public DatabaseOptions Database { get; set; } = new();

    /// <summary>
    /// Docker Hub configuration
    /// </summary>
    public DockerHubOptions DockerHub { get; set; } = new();

    /// <summary>
    /// Upstream release metadata feed configuration
    /// </summary>
    public ReleaseMetadataOptions ReleaseMetadata { get; set; } = new();

    /// <summary>
    /// Vulnerability provider configuration
    /// </summary>
    public VulnerabilityOptions Vulnerabilities { get; set; } = new();

    /// <summary>
    /// Background scanning configuration
    /// </summary>
    public ScanningOptions Scanning { get; set; } = new();

    /// <summary>
    /// Configured Docker instances
    /// </summary>
    public IList<DockerInstanceOptions> DockerInstances { get; set; } = [];

    #endregion // Properties
}