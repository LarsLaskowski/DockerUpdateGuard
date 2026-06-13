using System.Net;
using System.Text;

namespace DockerUpdateGuard.Tests.Helper;

/// <summary>
/// Fixed-response HTTP message handler for metadata tests
/// </summary>
internal sealed class StaticHttpMessageHandler : HttpMessageHandler
{
    #region Fields

    /// <summary>
    /// Response payload
    /// </summary>
    private readonly string _payload;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="payload">Response payload</param>
    public StaticHttpMessageHandler(string payload)
    {
        _payload = payload;
    }

    #endregion // Constructors

    #region HttpMessageHandler

    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                               {
                                   Content = new StringContent(_payload,
                                                               Encoding.UTF8,
                                                               "text/html"),
                               });
    }

    #endregion // HttpMessageHandler
}