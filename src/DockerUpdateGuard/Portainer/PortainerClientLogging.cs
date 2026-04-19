using Microsoft.Extensions.Logging;

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
    /// Log that Portainer is configured but actions remain disabled
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="dockerInstanceName">Docker instance name</param>
    [LoggerMessage(EventId = 3301,
                   Level = LogLevel.Information,
                   Message = "Portainer is configured for Docker instance {DockerInstanceName}, but actions are disabled in the first host iteration")]
    public static partial void PortainerCapabilityActionsDisabled(this ILogger logger, string dockerInstanceName);

    /// <summary>
    /// Log that a Portainer action was rejected
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="actionName">Action name</param>
    /// <param name="resourceName">Resource name</param>
    /// <param name="dockerInstanceName">Docker instance name</param>
    [LoggerMessage(EventId = 3302,
                   Level = LogLevel.Information,
                   Message = "Portainer action {ActionName} for {ResourceName} on Docker instance {DockerInstanceName} was rejected because actions are disabled in the first host iteration")]
    public static partial void PortainerActionRejected(this ILogger logger,
                                                       string actionName,
                                                       string resourceName,
                                                       string dockerInstanceName);

    #endregion // Methods
}