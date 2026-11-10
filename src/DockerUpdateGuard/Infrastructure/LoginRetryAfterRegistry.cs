namespace DockerUpdateGuard.Infrastructure;

/// <summary>
/// Process-wide memory of the windows in which a login answered HTTP 429 with a Retry-After header must not be repeated
/// </summary>
public sealed class LoginRetryAfterRegistry
{
    #region Fields

    // Skeleton: removed with the implementation
#pragma warning disable S4487
    /// <summary>
    /// Time provider
    /// </summary>
    private readonly TimeProvider _timeProvider;
#pragma warning restore S4487

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="timeProvider">Time provider</param>
    public LoginRetryAfterRegistry(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    #endregion // Constructors

    #region Methods

    /// <summary>
    /// Check whether a login to the given URL with the given user name is deferred by a Retry-After window
    /// </summary>
    /// <param name="loginUri">Login URL</param>
    /// <param name="userName">User name</param>
    /// <param name="retryAt">End of the window when the login is deferred</param>
    /// <returns>True when the login must not be sent before <paramref name="retryAt"/></returns>
    public bool IsLoginDeferred(Uri loginUri, string userName, out DateTimeOffset retryAt)
    {
        throw new InvalidOperationException("Skeleton: not implemented");
    }

    /// <summary>
    /// Record the response of a login: a success ends a window, a 429 with a positive Retry-After opens or extends one
    /// </summary>
    /// <param name="loginUri">Login URL</param>
    /// <param name="userName">User name</param>
    /// <param name="response">Login response</param>
    public void RecordLoginResponse(Uri loginUri, string userName, HttpResponseMessage response)
    {
        throw new InvalidOperationException("Skeleton: not implemented");
    }

    #endregion // Methods
}