namespace DockerUpdateGuard.Portainer;

/// <summary>
/// Source-generated logging contracts for <see cref="PortainerClient"/>
/// </summary>
internal static partial class PortainerClientLogging
{
    #region Methods

    /// <summary>
    /// Log that Portainer is not configured for a Docker instance
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="dockerInstanceName">Docker instance name</param>
    [LoggerMessage(EventId = 3300,
                   Level = LogLevel.Information,
                   Message = "Portainer is not configured for Docker instance {DockerInstanceName}")]
    public static partial void PortainerCapabilityNotConfigured(this ILogger logger, string dockerInstanceName);

    /// <summary>
    /// Log that Portainer authentication failed
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="baseUrl">Portainer base URL</param>
    [LoggerMessage(EventId = 3303,
                   Level = LogLevel.Warning,
                   Message = "Portainer authentication failed for {BaseUrl}")]
    public static partial void PortainerAuthFailed(this ILogger logger, string baseUrl);

    /// <summary>
    /// Log that Portainer capability was resolved successfully
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="dockerInstanceName">Docker instance name</param>
    /// <param name="endpointId">Resolved endpoint identifier</param>
    [LoggerMessage(EventId = 3304,
                   Level = LogLevel.Information,
                   Message = "Portainer capability resolved for Docker instance {DockerInstanceName}, endpoint {EndpointId}")]
    public static partial void PortainerCapabilityResolved(this ILogger logger, string dockerInstanceName, string endpointId);

    /// <summary>
    /// Log that Portainer connectivity check returned a non-success status code
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="dockerInstanceName">Docker instance name</param>
    /// <param name="statusCode">HTTP status code</param>
    [LoggerMessage(EventId = 3305,
                   Level = LogLevel.Warning,
                   Message = "Portainer connectivity check failed for Docker instance {DockerInstanceName}: HTTP {StatusCode}")]
    public static partial void PortainerCapabilityConnectFailed(this ILogger logger, string dockerInstanceName, int statusCode);

    /// <summary>
    /// Log that Portainer capability check threw an exception
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="dockerInstanceName">Docker instance name</param>
    /// <param name="exception">Exception</param>
    [LoggerMessage(EventId = 3306,
                   Level = LogLevel.Error,
                   Message = "Portainer capability check failed with an exception for Docker instance {DockerInstanceName}")]
    public static partial void PortainerCapabilityException(this ILogger logger, string dockerInstanceName, Exception exception);

    /// <summary>
    /// Log that a container was not found in Portainer
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="containerName">Container name</param>
    /// <param name="dockerInstanceName">Docker instance name</param>
    [LoggerMessage(EventId = 3307,
                   Level = LogLevel.Warning,
                   Message = "Container {ContainerName} not found via Portainer on Docker instance {DockerInstanceName}")]
    public static partial void PortainerContainerNotFound(this ILogger logger, string containerName, string dockerInstanceName);

    /// <summary>
    /// Log that a Portainer action was executed successfully
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="actionName">Action name</param>
    /// <param name="resourceName">Resource name</param>
    /// <param name="dockerInstanceName">Docker instance name</param>
    [LoggerMessage(EventId = 3308,
                   Level = LogLevel.Information,
                   Message = "Portainer action {ActionName} on {ResourceName} executed successfully for Docker instance {DockerInstanceName}")]
    public static partial void PortainerActionExecuted(this ILogger logger,
                                                       string actionName,
                                                       string resourceName,
                                                       string dockerInstanceName);

    /// <summary>
    /// Log that a Portainer action returned a non-success HTTP status
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="actionName">Action name</param>
    /// <param name="resourceName">Resource name</param>
    /// <param name="dockerInstanceName">Docker instance name</param>
    /// <param name="statusCode">HTTP status code</param>
    [LoggerMessage(EventId = 3309,
                   Level = LogLevel.Warning,
                   Message = "Portainer action {ActionName} on {ResourceName} failed for Docker instance {DockerInstanceName}: HTTP {StatusCode}")]
    public static partial void PortainerActionFailed(this ILogger logger,
                                                     string actionName,
                                                     string resourceName,
                                                     string dockerInstanceName,
                                                     int statusCode);

    /// <summary>
    /// Log that a Portainer action threw an exception
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="actionName">Action name</param>
    /// <param name="resourceName">Resource name</param>
    /// <param name="dockerInstanceName">Docker instance name</param>
    /// <param name="exception">Exception</param>
    [LoggerMessage(EventId = 3310,
                   Level = LogLevel.Error,
                   Message = "Portainer action {ActionName} on {ResourceName} failed with an exception for Docker instance {DockerInstanceName}")]
    public static partial void PortainerActionException(this ILogger logger,
                                                        string actionName,
                                                        string resourceName,
                                                        string dockerInstanceName,
                                                        Exception exception);

    #endregion // Methods
}