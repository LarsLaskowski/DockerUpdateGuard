using DockerUpdateGuard.Infrastructure;

namespace DockerUpdateGuard.DockerHub;

/// <summary>
/// Shared cache of the Docker Hub access token
/// </summary>
public sealed class DockerHubAccessTokenCache : IDisposable
{
    #region Fields

    /// <summary>
    /// Time provider
    /// </summary>
#pragma warning disable S4487 // Skeleton: removed with the implementation
    private readonly TimeProvider _timeProvider;
#pragma warning restore S4487 // Skeleton: removed with the implementation

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="timeProvider">Time provider</param>
    public DockerHubAccessTokenCache(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    #endregion // Constructors

    #region Methods

    /// <summary>
    /// Return the cached access token or request a new one
    /// </summary>
    /// <param name="loginUri">Login address</param>
    /// <param name="userName">User name</param>
    /// <param name="personalAccessToken">Personal access token</param>
    /// <param name="loginTimeout">Timeout of the login request</param>
    /// <param name="requestToken">Function requesting a new token</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Access token result</returns>
#pragma warning disable S2325 // Skeleton: removed with the implementation
    public async Task<ExternalOperationResult<string>> GetOrRequestAsync(Uri loginUri,
                                                                         string userName,
                                                                         string personalAccessToken,
                                                                         TimeSpan loginTimeout,
                                                                         Func<CancellationToken, Task<ExternalOperationResult<string>>> requestToken,
                                                                         CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        throw new InvalidOperationException("Skeleton: not implemented");
    }
#pragma warning restore S2325 // Skeleton: removed with the implementation

    #endregion // Methods

    #region IDisposable

    /// <inheritdoc/>
    public void Dispose()
    {
        // Skeleton: nothing to release yet
    }

    #endregion // IDisposable
}