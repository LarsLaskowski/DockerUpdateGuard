namespace DockerUpdateGuard.Components;

/// <summary>
/// Source-generated logging contracts for the user actions of the UI components
/// </summary>
internal static partial class ComponentLoggingExtensions
{
    #region Methods

    /// <summary>
    /// Log that a user action failed
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="action">Name of the failed user action</param>
    /// <param name="exception">Exception that made the action fail</param>
    [LoggerMessage(EventId = 4000,
                   Level = LogLevel.Error,
                   Message = "User action {Action} failed")]
    public static partial void UserActionFailed(this ILogger logger, string action, Exception exception);

    #endregion // Methods
}