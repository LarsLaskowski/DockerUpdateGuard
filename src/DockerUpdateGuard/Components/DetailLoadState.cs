namespace DockerUpdateGuard.Components;

/// <summary>
/// Outcome of the load of a detail page: a record that does not exist or the message of a failed load
/// </summary>
internal sealed class DetailLoadState
{
    #region Fields

    // Skeleton: removed with the implementation
#pragma warning disable S1144, IDE0044, CS0169
    /// <summary>
    /// Key of the latest parameter set
    /// </summary>
    private string? _routeKey;

    /// <summary>
    /// Number of route changes so far
    /// </summary>
    private int _routeChanges;
#pragma warning restore S1144, IDE0044, CS0169

    #endregion // Fields

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

    // Skeleton: removed with the implementation
#pragma warning disable CA1822, S2325
    /// <summary>
    /// Record the route key of the latest parameter set
    /// </summary>
    /// <param name="routeKey">Key of the route the page shows</param>
    /// <returns>True when the key replaces a different key of an earlier parameter set</returns>
    public bool SetRoute(string routeKey)
    {
        throw new InvalidOperationException("Skeleton: not implemented");
    }
#pragma warning restore CA1822, S2325

    /// <summary>
    /// Record the answer of a load for a route
    /// </summary>
    /// <typeparam name="T">Type of the detail</typeparam>
    /// <param name="routeKey">Key of the route the load was requested for</param>
    /// <param name="detail">Loaded detail, null when the record does not exist</param>
    /// <param name="shown">Detail the page currently shows</param>
    /// <returns>The detail the page shows afterwards</returns>
    public T? Found<T>(string routeKey, T? detail, T? shown)
        where T : class
    {
        // Skeleton: zero-value stub keeps the existing page tests green
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

        var errorMessage = await PageLoader.LoadAsync(load, logger, action, messagePrefix).ConfigureAwait(false);

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