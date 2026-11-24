using DockerUpdateGuard.Infrastructure;

namespace DockerUpdateGuard.Components;

/// <summary>
/// Guards the load of a detail page so that a failed load becomes a fixed message instead of an unhandled exception
/// </summary>
internal static class DetailPageLoader
{
    #region Static methods

    /// <summary>
    /// Run the load of a detail page and report a failure as a fixed message
    /// </summary>
    /// <param name="load">Load of the page</param>
    /// <param name="logger">Logger of the page</param>
    /// <param name="action">Name of the logged user action</param>
    /// <param name="messagePrefix">Prefix of the message shown to the user</param>
    /// <returns>Null when the load completed, otherwise the message with the fixed failure reason</returns>
    internal static async Task<string?> LoadAsync(Func<Task> load, ILogger logger, string action, string messagePrefix)
    {
        try
        {
            await load().ConfigureAwait(false);

            return null;
        }
        catch (Exception exception)
        {
            logger.UserActionFailed(action, exception);

            return $"{messagePrefix}: {FailureReason.FromException(exception, null, CancellationToken.None)}";
        }
    }

    #endregion // Static methods
}