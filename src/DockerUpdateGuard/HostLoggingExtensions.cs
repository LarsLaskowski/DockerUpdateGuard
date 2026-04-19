using Microsoft.Extensions.Logging;

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