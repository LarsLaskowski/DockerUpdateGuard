namespace DockerUpdateGuard.Docker;

/// <summary>
/// Source-generated logging contracts for <see cref="DockerInstanceClient"/>
/// </summary>
internal static partial class DockerInstanceClientLogging
{
    #region Methods

    /// <summary>
    /// Log that Docker discovery was skipped because the instance is disabled
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="dockerInstanceName">Docker instance name</param>
    [LoggerMessage(EventId = 3100,
                   Level = LogLevel.Information,
                   Message = "Docker instance {DockerInstanceName} is disabled; skipping container discovery")]
    public static partial void DockerInstanceDiscoverySkippedDisabled(this ILogger logger, string dockerInstanceName);

    /// <summary>
    /// Log that Docker discovery could not start because the endpoint is unsupported
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="dockerInstanceName">Docker instance name</param>
    /// <param name="baseUrl">Configured endpoint</param>
    [LoggerMessage(EventId = 3101,
                   Level = LogLevel.Warning,
                   Message = "Docker instance {DockerInstanceName} uses unsupported endpoint {BaseUrl}")]
    public static partial void DockerInstanceEndpointUnsupported(this ILogger logger,
                                                                 string dockerInstanceName,
                                                                 string? baseUrl);

    /// <summary>
    /// Log that Docker discovery returned a non-success status code
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="dockerInstanceName">Docker instance name</param>
    /// <param name="statusCode">HTTP status code</param>
    [LoggerMessage(EventId = 3102,
                   Level = LogLevel.Warning,
                   Message = "Docker container discovery failed for {DockerInstanceName} with status code {StatusCode}")]
    public static partial void DockerInstanceDiscoveryResponseFailed(this ILogger logger,
                                                                     string dockerInstanceName,
                                                                     int statusCode);

    /// <summary>
    /// Log that Docker discovery succeeded
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="dockerInstanceName">Docker instance name</param>
    /// <param name="containerCount">Discovered container count</param>
    [LoggerMessage(EventId = 3103,
                   Level = LogLevel.Information,
                   Message = "Docker container discovery succeeded for {DockerInstanceName} with {ContainerCount} containers")]
    public static partial void DockerInstanceDiscoverySucceeded(this ILogger logger,
                                                                string dockerInstanceName,
                                                                int containerCount);

    /// <summary>
    /// Log that Docker discovery failed with an exception
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="exception">Failure exception</param>
    /// <param name="dockerInstanceName">Docker instance name</param>
    [LoggerMessage(EventId = 3104,
                   Level = LogLevel.Warning,
                   Message = "Docker container discovery failed for {DockerInstanceName}")]
    public static partial void DockerInstanceDiscoveryFailed(this ILogger logger,
                                                             Exception exception,
                                                             string dockerInstanceName);

    /// <summary>
    /// Log that Docker discovery timed out without emitting an exception stack
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="dockerInstanceName">Docker instance name</param>
    /// <param name="requestTimeoutSeconds">Configured request timeout in seconds</param>
    [LoggerMessage(EventId = 3105,
                   Level = LogLevel.Warning,
                   Message = "Docker container discovery timed out for {DockerInstanceName} after {RequestTimeoutSeconds} seconds")]
    public static partial void DockerInstanceDiscoveryTimedOut(this ILogger logger,
                                                               string dockerInstanceName,
                                                               int requestTimeoutSeconds);

    #endregion // Methods
}