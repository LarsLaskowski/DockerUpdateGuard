namespace DockerUpdateGuard.Infrastructure;

/// <summary>
/// Delegating handler that bounds a request and the read of its response body by one deadline
/// </summary>
internal sealed class RequestDeadlineHandler : DelegatingHandler
{
    #region Fields

    private readonly TimeProvider _timeProvider;

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
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var deadlineSource = new CancellationTokenSource(RequestTimeout, _timeProvider);
        HttpResponseMessage? response = null;

        try
        {
            using (var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadlineSource.Token))
            {
                response = await base.SendAsync(request, linkedSource.Token).ConfigureAwait(false);

                var originalContent = response.Content;
                var innerStream = await originalContent.ReadAsStreamAsync(linkedSource.Token).ConfigureAwait(false);
                var deadlineContent = new StreamContent(new DeadlineReadStream(innerStream, originalContent, deadlineSource));

                foreach (var header in originalContent.Headers)
                {
                    deadlineContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                response.Content = deadlineContent;
            }

            return response;
        }
        catch
        {
            response?.Dispose();
            deadlineSource.Dispose();

            throw;
        }
    }

    #endregion // HttpMessageHandler
}