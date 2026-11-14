namespace DockerUpdateGuard.Tests.Helper;

/// <summary>
/// HTTP message handler that answers every request with a response built by a delegate
/// </summary>
internal sealed class DelegateHttpMessageHandler : HttpMessageHandler
{
    #region Fields

    /// <summary>
    /// Factory that creates the response of a request, observing the request's cancellation token
    /// </summary>
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responseFactory;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="responseFactory">Factory that creates the response of a request</param>
    public DelegateHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
    {
        _responseFactory = (request, _) => Task.FromResult(responseFactory(request));
    }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="responseFactory">Factory that creates the response of a request and receives the request's cancellation token</param>
    public DelegateHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
    {
        _responseFactory = responseFactory;
    }

    #endregion // Constructors

    #region HttpMessageHandler

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await _responseFactory(request, cancellationToken).ConfigureAwait(false);

        response.RequestMessage = request;

        return response;
    }

    #endregion // HttpMessageHandler
}