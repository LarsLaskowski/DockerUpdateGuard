namespace DockerUpdateGuard;

/// <summary>
/// Source-generated logging contracts for host initialization and telemetry flows
/// </summary>
internal static partial class HostLoggingExtensions
{
    #region Methods

    /// <summary>
    /// Log that application initialization has started
    /// </summary>
    /// <param name="logger">Logger</param>
    [LoggerMessage(EventId = 1000,
                   Level = LogLevel.Information,
                   Message = "Starting DockerUpdateGuard application initialization")]
    public static partial void ApplicationInitializationStarted(this ILogger logger);

    /// <summary>
    /// Log that database migration has completed
    /// </summary>
    /// <param name="logger">Logger</param>
    [LoggerMessage(EventId = 1001,
                   Level = LogLevel.Information,
                   Message = "Completed DockerUpdateGuard database migration")]
    public static partial void ApplicationDatabaseMigrated(this ILogger logger);

    /// <summary>
    /// Log that a database connection attempt failed and will be retried
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="attempt">Attempt number</param>
    /// <param name="delaySeconds">Delay before the next attempt in seconds</param>
    /// <param name="exception">Connection failure</param>
    [LoggerMessage(EventId = 1003,
                   Level = LogLevel.Warning,
                   Message = "DockerUpdateGuard database connection attempt {Attempt} failed; retrying in {DelaySeconds} seconds")]
    public static partial void ApplicationDatabaseConnectionRetrying(this ILogger logger,
                                                                     int attempt,
                                                                     double delaySeconds,
                                                                     Exception exception);

    /// <summary>
    /// Log that the database never became reachable within the configured startup timeout
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="attempts">Number of attempts performed</param>
    /// <param name="exception">Last connection failure</param>
    [LoggerMessage(EventId = 1004,
                   Level = LogLevel.Critical,
                   Message = "DockerUpdateGuard database did not become reachable after {Attempts} attempts within the configured startup timeout")]
    public static partial void ApplicationDatabaseUnavailable(this ILogger logger,
                                                              int attempts,
                                                              Exception exception);

    /// <summary>
    /// Log that the database migration failed
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="exception">Migration failure</param>
    [LoggerMessage(EventId = 1005,
                   Level = LogLevel.Critical,
                   Message = "DockerUpdateGuard database migration failed")]
    public static partial void ApplicationDatabaseMigrationFailed(this ILogger logger, Exception exception);

    /// <summary>
    /// Log that releasing the migration advisory lock failed
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="exception">Advisory lock release failure</param>
    [LoggerMessage(EventId = 1006,
                   Level = LogLevel.Warning,
                   Message = "DockerUpdateGuard failed to release the migration advisory lock; it will be released when the connection closes")]
    public static partial void ApplicationDatabaseAdvisoryLockReleaseFailed(this ILogger logger, Exception exception);

    /// <summary>
    /// Log that the migration advisory lock is held by another instance and the wait continues
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="attempt">Number of the failed attempt</param>
    /// <param name="elapsedSeconds">Seconds waited so far</param>
    /// <param name="delaySeconds">Seconds until the next attempt</param>
    [LoggerMessage(EventId = 1007,
                   Level = LogLevel.Information,
                   Message = "DockerUpdateGuard migration advisory lock is held by another instance; attempt {Attempt} after {ElapsedSeconds} seconds, retrying in {DelaySeconds} seconds")]
    public static partial void ApplicationDatabaseMigrationLockWaiting(this ILogger logger,
                                                                       int attempt,
                                                                       double elapsedSeconds,
                                                                       double delaySeconds);

    /// <summary>
    /// Log that the migration advisory lock was not acquired within the configured timeout
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="timeoutSeconds">Configured timeout in seconds</param>
    /// <param name="attempts">Number of attempts performed</param>
    [LoggerMessage(EventId = 1008,
                   Level = LogLevel.Error,
                   Message = "DockerUpdateGuard did not acquire the migration advisory lock within {TimeoutSeconds} seconds after {Attempts} attempts; raise 'DockerUpdateGuard:Database:MigrationLockTimeoutSeconds' if another instance needs longer to migrate")]
    public static partial void ApplicationDatabaseMigrationLockTimedOut(this ILogger logger, int timeoutSeconds, int attempts);

    /// <summary>
    /// Log that tracked image references were moved to canonical catalog entries
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="count">Number of moved tracked image references</param>
    [LoggerMessage(EventId = 1009,
                   Level = LogLevel.Information,
                   Message = "Moved {Count} tracked image references to canonical catalog entries")]
    public static partial void TrackedImageReferencesCanonicalized(this ILogger logger, int count);

    /// <summary>
    /// Log that application initialization has completed
    /// </summary>
    /// <param name="logger">Logger</param>
    [LoggerMessage(EventId = 1002,
                   Level = LogLevel.Information,
                   Message = "Completed DockerUpdateGuard application initialization")]
    public static partial void ApplicationInitializationCompleted(this ILogger logger);

    /// <summary>
    /// Log refreshed inventory metrics
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="observedImages">Observed image count</param>
    /// <param name="runtimeContainers">Runtime container count</param>
    /// <param name="deduplicatedBaseImages">Base image count</param>
    /// <param name="activeUpdateFindings">Active update finding count</param>
    /// <param name="activeCveFindings">Active vulnerability finding count</param>
    /// <param name="needsReviewFindings">Needs review finding count</param>
    [LoggerMessage(EventId = 1010,
                   Level = LogLevel.Information,
                   Message = "Refreshed inventory metrics with {ObservedImages} observed images, {RuntimeContainers} runtime containers, {DeduplicatedBaseImages} base images, {ActiveUpdateFindings} active update findings, {ActiveCveFindings} active vulnerability findings, and {NeedsReviewFindings} needs-review findings")]
    public static partial void InventoryMetricsRefreshed(this ILogger logger,
                                                         long observedImages,
                                                         long runtimeContainers,
                                                         long deduplicatedBaseImages,
                                                         long activeUpdateFindings,
                                                         long activeCveFindings,
                                                         long needsReviewFindings);

    #endregion // Methods
}