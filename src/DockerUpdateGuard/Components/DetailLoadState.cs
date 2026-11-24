namespace DockerUpdateGuard.Components;

/// <summary>
/// Outcome of the load of a detail page: a record that does not exist or the message of a failed load
/// </summary>
internal sealed class DetailLoadState
{
    #region Properties

    /// <summary>
    /// Indicates whether the last load found no record for the route identifier
    /// </summary>
    public bool IsNotFound { get; private set; }

    /// <summary>
    /// Message of the last failed load, null when the load did not fail
    /// </summary>
    public string? ErrorMessage { get; private set; }

    #endregion // Properties

    #region Methods

    /// <summary>
    /// Record the answer of a load
    /// </summary>
    /// <typeparam name="T">Type of the detail</typeparam>
    /// <param name="detail">Loaded detail, null when the record does not exist</param>
    /// <returns>The detail</returns>
    public T? Found<T>(T? detail)
        where T : class
    {
        IsNotFound = detail is null;

        return detail;
    }

    /// <summary>
    /// Run the load of a detail page: clear the previous outcome, load, and on a failure keep the fixed message and drop the detail
    /// </summary>
    /// <param name="invoke">Runs an action on the component's synchronization context</param>
    /// <param name="load">Load of the page</param>
    /// <param name="logger">Logger of the page</param>
    /// <param name="action">Name of the logged user action</param>
    /// <param name="messagePrefix">Prefix of the message shown to the user</param>
    /// <param name="clearDetail">Drops the detail of the page</param>
    /// <returns>Task</returns>
    public async Task RunAsync(Func<Action, Task> invoke, Func<Task> load, ILogger logger, string action, string messagePrefix, Action clearDetail)
    {
        await invoke(() =>
                     {
                         IsNotFound = false;
                         ErrorMessage = null;
                     }).ConfigureAwait(false);

        var errorMessage = await DetailPageLoader.LoadAsync(load, logger, action, messagePrefix).ConfigureAwait(false);

        await invoke(() =>
                     {
                         ErrorMessage = errorMessage;

                         if (errorMessage is not null)
                         {
                             clearDetail();
                             IsNotFound = false;
                         }
                     }).ConfigureAwait(false);
    }

    #endregion // Methods
}