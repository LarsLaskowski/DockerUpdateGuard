namespace DockerUpdateGuard.Configuration;

/// <summary>
/// Database connection and startup migration resilience options
/// </summary>
public class DatabaseOptions
{
    #region Properties

    /// <summary>
    /// Maximum number of transient failure retries the Npgsql execution strategy performs
    /// </summary>
    public int MaxConnectionRetryCount { get; set; } = 8;

    /// <summary>
    /// Maximum delay between transient failure retries of the Npgsql execution strategy in seconds
    /// </summary>
    public int MaxConnectionRetryDelaySeconds { get; set; } = 30;

    /// <summary>
    /// Total time the startup migration waits for the database to become reachable in seconds
    /// </summary>
    public int MigrationStartupTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Delay between database availability probes during startup migration in seconds
    /// </summary>
    public int MigrationRetryDelaySeconds { get; set; } = 5;

    #endregion // Properties
}
