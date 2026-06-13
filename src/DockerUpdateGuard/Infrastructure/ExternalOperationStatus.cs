namespace DockerUpdateGuard.Infrastructure;

/// <summary>
/// External operation state
/// </summary>
public enum ExternalOperationStatus
{
    /// <summary>
    /// No status available
    /// </summary>
    NotSet = 0,

    /// <summary>
    /// Operation completed successfully
    /// </summary>
    Succeeded = 1,

    /// <summary>
    /// Required configuration is missing or disabled
    /// </summary>
    NotConfigured = 2,

    /// <summary>
    /// Capability is not supported yet
    /// </summary>
    Unsupported = 3,

    /// <summary>
    /// Target object was not found
    /// </summary>
    NotFound = 4,

    /// <summary>
    /// Operation failed
    /// </summary>
    Failed = 5,

    /// <summary>
    /// Operation produced an inconclusive result
    /// </summary>
    Unknown = 6
}