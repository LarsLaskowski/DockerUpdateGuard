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

    /// <summary>
    /// Total time the startup migration waits for the migration advisory lock held by another instance in seconds
    /// </summary>
    public int MigrationLockTimeoutSeconds { get; set; } = 900;

    /// <summary>
    /// Command timeout in seconds of each statement the startup migration runs
    /// </summary>
    public int MigrationCommandTimeoutSeconds { get; set; } = 900;

    #endregion // Properties
}