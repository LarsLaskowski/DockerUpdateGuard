namespace DockerUpdateGuard.Tests.Helper;

/// <summary>
/// HTTP message handler that answers every request with a response built by a delegate
/// </summary>
internal sealed class DelegateHttpMessageHandler : HttpMessageHandler
{
    #region Fields

    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="responseFactory">Factory that creates the response of a request</param>
    public DelegateHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
    {
        _responseFactory = responseFactory;
    }

    #endregion // Constructors

    #region HttpMessageHandler

    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = _responseFactory(request);

        response.RequestMessage = request;

        return Task.FromResult(response);
    }

    #endregion // HttpMessageHandler
}