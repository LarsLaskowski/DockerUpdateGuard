namespace DockerUpdateGuard.Data.Entities;

/// <summary>
/// Type of persisted scan run
/// </summary>
public enum ScanRunType
{
    /// <summary>
    /// No scan type set
    /// </summary>
    NotSet = 0,

    /// <summary>
    /// Scan for an observed image
    /// </summary>
    ObservedImage = 1,

    /// <summary>
    /// Scan for runtime containers
    /// </summary>
    RuntimeContainer = 2,

    /// <summary>
    /// Scan for vulnerabilities
    /// </summary>
    Vulnerability = 3
}