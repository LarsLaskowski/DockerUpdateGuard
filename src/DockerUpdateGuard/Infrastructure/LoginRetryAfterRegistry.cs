using System.Collections.Concurrent;
using System.Globalization;
using System.Net;

namespace DockerUpdateGuard.Infrastructure;

/// <summary>
/// Process-wide memory of the windows in which a login answered HTTP 429 with a Retry-After header must not be repeated
/// </summary>
public sealed class LoginRetryAfterRegistry
{
    #region Fields

    /// <summary>
    /// End of the window per login target
    /// </summary>
    private readonly ConcurrentDictionary<string, DateTimeOffset> _windows = new(StringComparer.Ordinal);

    /// <summary>
    /// Time provider
    /// </summary>
    private readonly TimeProvider _timeProvider;

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

    #region Static methods

    /// <summary>
    /// Format the end of a window for a failure message
    /// </summary>
    /// <param name="retryAt">End of the window</param>
    /// <returns>UTC instant as yyyy-MM-ddTHH:mm:ssZ</returns>
    public static string FormatRetryAt(DateTimeOffset retryAt)
    {
        return retryAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Build the registry key of a login target
    /// </summary>
    /// <param name="loginUri">Login URL</param>
    /// <param name="userName">User name</param>
    /// <returns>Key of scheme, host, port, path and user name</returns>
    private static string CreateKey(Uri loginUri, string userName)
    {
        return $"{loginUri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped)}\n{userName}";
    }

    #endregion // Static methods

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
        var key = CreateKey(loginUri, userName);

        if (_windows.TryGetValue(key, out var until))
        {
            if (_timeProvider.GetUtcNow() < until)
            {
                retryAt = until;

                return true;
            }

            _windows.TryRemove(new KeyValuePair<string, DateTimeOffset>(key, until));
        }

        retryAt = default;

        return false;
    }

    /// <summary>
    /// Record the response of a login: a success ends a window, a 429 with a positive Retry-After opens or extends one
    /// </summary>
    /// <param name="loginUri">Login URL</param>
    /// <param name="userName">User name</param>
    /// <param name="response">Login response</param>
    public void RecordLoginResponse(Uri loginUri, string userName, HttpResponseMessage response)
    {
        var key = CreateKey(loginUri, userName);

        if (response.IsSuccessStatusCode)
        {
            _windows.TryRemove(key, out _);

            return;
        }

        if (response.StatusCode != HttpStatusCode.TooManyRequests)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        var wait = RetryAfterHeader.GetWait(response, now);

        if (wait is not TimeSpan requested)
        {
            return;
        }

        var until = now + requested;

        _windows.AddOrUpdate(key, until, (_, existing) => existing > until ? existing : until);
    }

    #endregion // Methods
}