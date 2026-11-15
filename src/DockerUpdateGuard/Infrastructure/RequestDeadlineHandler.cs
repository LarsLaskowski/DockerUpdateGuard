namespace DockerUpdateGuard.Infrastructure;

/// <summary>
/// Delegating handler that bounds a request and the read of its response body by one deadline
/// </summary>
internal sealed class RequestDeadlineHandler : DelegatingHandler
{
    #region Fields

#pragma warning disable S4487 // Skeleton: removed with the implementation
    private readonly TimeProvider _timeProvider;
#pragma warning restore S4487 // Skeleton: removed with the implementation

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="timeProvider">Time provider that runs the deadline</param>
    /// <param name="requestTimeout">Time allowed from sending the request until its response body is read</param>
    public RequestDeadlineHandler(TimeProvider timeProvider, TimeSpan requestTimeout)
    {
        _timeProvider = timeProvider;
        RequestTimeout = requestTimeout;
    }

    #endregion // Constructors

    #region Properties

    /// <summary>
    /// Time allowed from sending the request until its response body is read
    /// </summary>
    public TimeSpan RequestTimeout { get; }

    #endregion // Properties

    #region HttpMessageHandler

    /// <inheritdoc/>
#pragma warning disable S4487 // Skeleton: removed with the implementation
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("Skeleton: not implemented");
    }
#pragma warning restore S4487 // Skeleton: removed with the implementation

    #endregion // HttpMessageHandler
}