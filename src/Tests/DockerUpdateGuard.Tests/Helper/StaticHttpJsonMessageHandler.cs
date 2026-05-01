using System.Net;
using System.Text;

namespace DockerUpdateGuard.Tests.Helper;

/// <summary>
/// Fixed-response HTTP message handler for metadata tests
/// </summary>
internal sealed class StaticHttpJsonMessageHandler : HttpMessageHandler
{
    #region Fields

    /// <summary>
    /// JSON payload
    /// </summary>
    private readonly string _jsonPayload;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="jsonPayload">JSON payload</param>
    public StaticHttpJsonMessageHandler(string jsonPayload)
    {
        _jsonPayload = jsonPayload;
    }

    #endregion // Constructors

    #region Methods

    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                               {
                                   Content = new StringContent(_jsonPayload,
                                                               Encoding.UTF8,
                                                               "application/json"),
                               });
    }

    #endregion // Methods
}