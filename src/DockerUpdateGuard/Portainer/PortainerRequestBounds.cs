namespace DockerUpdateGuard.Portainer;

/// <summary>
/// Runs the requests of one Portainer operation, each under its own deadline on the injected time provider, and remembers the bound of the latest request
/// </summary>
internal sealed class PortainerRequestBounds
{
    #region Fields

    /// <summary>
    /// Time provider that measures the deadlines
    /// </summary>
    private readonly TimeProvider _timeProvider;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="timeProvider">Time provider that measures the deadlines</param>
    /// <param name="requestTimeout">Bound of an ordinary request</param>
    public PortainerRequestBounds(TimeProvider timeProvider, TimeSpan requestTimeout)
    {
        _timeProvider = timeProvider;
        RequestTimeout = requestTimeout;
        Latest = requestTimeout;
    }

    #endregion // Constructors

    #region Properties

    /// <summary>
    /// Bound of an ordinary request
    /// </summary>
    public TimeSpan RequestTimeout { get; }

    /// <summary>
    /// Bound of the request that ran last
    /// </summary>
    public TimeSpan Latest { get; private set; }

    #endregion // Properties

    #region Methods

    /// <summary>
    /// Run a request, including the reading of its answer, under a bound that starts now
    /// </summary>
    /// <typeparam name="TResult">Result type of the request</typeparam>
    /// <param name="bound">Bound of the request</param>
    /// <param name="request">Request to run with the token that carries the caller's cancellation and the bound</param>
    /// <param name="cancellationToken">Cancellation token of the caller</param>
    /// <returns>Result of the request</returns>
    public async Task<TResult> RunAsync<TResult>(TimeSpan bound, Func<CancellationToken, Task<TResult>> request, CancellationToken cancellationToken)
    {
        Latest = bound;

        using var boundSource = new CancellationTokenSource(bound, _timeProvider);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, boundSource.Token);

        return await request(linkedSource.Token).ConfigureAwait(false);
    }

    #endregion // Methods
}