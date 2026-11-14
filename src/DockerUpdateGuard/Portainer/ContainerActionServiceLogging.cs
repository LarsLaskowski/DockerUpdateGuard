using DockerUpdateGuard.Data.Entities;

namespace DockerUpdateGuard.Portainer;

/// <summary>
/// Source-generated logging contracts for <see cref="ContainerActionService"/>
/// </summary>
internal static partial class ContainerActionServiceLogging
{
    #region Methods

    /// <summary>
    /// Log that a container action was requested
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="actionType">Action type</param>
    /// <param name="containerName">Container name</param>
    /// <param name="dockerInstanceName">Docker instance name</param>
    [LoggerMessage(EventId = 3320,
                   Level = LogLevel.Information,
                   Message = "Container action {ActionType} requested for container {ContainerName} on Docker instance {DockerInstanceName}")]
    public static partial void ContainerActionRequested(this ILogger logger,
                                                        ContainerActionType actionType,
                                                        string containerName,
                                                        string dockerInstanceName);

    /// <summary>
    /// Log that a container action finished
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="actionType">Action type</param>
    /// <param name="containerName">Container name</param>
    /// <param name="dockerInstanceName">Docker instance name</param>
    /// <param name="status">Run status</param>
    [LoggerMessage(EventId = 3321,
                   Level = LogLevel.Information,
                   Message = "Container action {ActionType} on container {ContainerName} of Docker instance {DockerInstanceName} finished with status {Status}")]
    public static partial void ContainerActionFinished(this ILogger logger,
                                                       ContainerActionType actionType,
                                                       string containerName,
                                                       string dockerInstanceName,
                                                       ContainerActionStatus status);

    /// <summary>
    /// Log that a container action was rejected by a check before Portainer was contacted
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="actionType">Action type</param>
    /// <param name="containerName">Container name</param>
    /// <param name="dockerInstanceName">Docker instance name</param>
    /// <param name="check">Name of the check that rejected the action</param>
    [LoggerMessage(EventId = 3322,
                   Level = LogLevel.Warning,
                   Message = "Container action {ActionType} on container {ContainerName} of Docker instance {DockerInstanceName} rejected: {Check}")]
    public static partial void ContainerActionRejected(this ILogger logger,
                                                       ContainerActionType actionType,
                                                       string containerName,
                                                       string dockerInstanceName,
                                                       string check);

    /// <summary>
    /// Log that the terminal status of a container action run could not be written
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="actionType">Action type</param>
    /// <param name="containerName">Container name</param>
    /// <param name="dockerInstanceName">Docker instance name</param>
    /// <param name="status">Run status that could not be written</param>
    /// <param name="exception">Exception</param>
    [LoggerMessage(EventId = 3323,
                   Level = LogLevel.Error,
                   Message = "Container action {ActionType} on container {ContainerName} of Docker instance {DockerInstanceName} ended {Status}, but the run could not be finalized")]
    public static partial void ContainerActionRunNotFinalized(this ILogger logger,
                                                              ContainerActionType actionType,
                                                              string containerName,
                                                              string dockerInstanceName,
                                                              ContainerActionStatus status,
                                                              Exception exception);

    /// <summary>
    /// Log that container action runs interrupted by an application stop were reconciled
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="count">Number of reconciled runs</param>
    [LoggerMessage(EventId = 3324,
                   Level = LogLevel.Information,
                   Message = "Reconciled {Count} container action runs interrupted by an application stop")]
    public static partial void ContainerActionRunsReconciled(this ILogger logger, int count);

    #endregion // Methods
}