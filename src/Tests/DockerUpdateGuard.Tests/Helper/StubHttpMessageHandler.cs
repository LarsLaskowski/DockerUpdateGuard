using System.Net;
using System.Text;

using DockerUpdateGuard.Tests.Data;

namespace DockerUpdateGuard.Tests.Helper;

/// <summary>
/// Stub HTTP message handler for deterministic Docker Hub client tests
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    #region Fields

    private readonly List<ObservedRequest> _requests = [];
    private readonly Dictionary<string, HttpResponseMessage> _responses = new(StringComparer.Ordinal);

    #endregion // Fields

    #region Properties

    /// <summary>
    /// Observed outbound requests
    /// </summary>
    public IReadOnlyList<ObservedRequest> Requests => _requests;

    /// <summary>
    /// Observed request body
    /// </summary>
    public string RequestBody { get; private set; } = string.Empty;

    /// <summary>
    /// Observed request URI
    /// </summary>
    public string RequestUri { get; private set; } = string.Empty;

    /// <summary>
    /// Configured response
    /// </summary>
    public HttpResponseMessage Response { get; set; } = new(HttpStatusCode.OK);

    #endregion // Properties

    #region Methods

    /// <summary>
    /// Add a JSON response for a request URI
    /// </summary>
    /// <param name="requestUri">Absolute request URI</param>
    /// <param name="jsonContent">JSON content</param>
    public void AddResponse(string requestUri, string jsonContent)
    {
        AddResponse(requestUri, jsonContent, "application/json");
    }

    /// <summary>
    /// Add a response with an explicit media type for a request URI
    /// </summary>
    /// <param name="requestUri">Absolute request URI</param>
    /// <param name="content">Response content</param>
    /// <param name="mediaType">Response media type</param>
    public void AddResponse(string requestUri, string content, string mediaType)
    {
        _responses[requestUri] = new HttpResponseMessage(HttpStatusCode.OK)
                                 {
                                     Content = new StringContent(content,
                                                                 Encoding.UTF8,
                                                                 mediaType),
                                 };
    }

    /// <summary>
    /// Clone a configured response for repeatable handler usage
    /// </summary>
    /// <param name="response">Template response</param>
    /// <returns>Cloned response</returns>
    private static HttpResponseMessage CloneResponse(HttpResponseMessage response)
    {
        var content = response.Content is null
                          ? null
                          : new StringContent(response.Content.ReadAsStringAsync().GetAwaiter().GetResult(),
                                              Encoding.UTF8,
                                              response.Content.Headers.ContentType?.MediaType);

        return new HttpResponseMessage(response.StatusCode)
               {
                   Content = content,
               };
    }

    #endregion // Methods

    #region HttpMessageHandler

    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request.RequestUri);

        RequestUri = request.RequestUri.ToString();
        RequestBody = request.Content is null
                          ? string.Empty
                          : request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();

        _requests.Add(new ObservedRequest
                      {
                          Method = request.Method.Method,
                          RequestUri = request.RequestUri.ToString(),
                          AuthorizationScheme = request.Headers.Authorization?.Scheme,
                          AuthorizationParameter = request.Headers.Authorization?.Parameter,
                      });

        if (_responses.TryGetValue(request.RequestUri.ToString(), out var response))
        {
            return Task.FromResult(CloneResponse(response));
        }

        var fallbackResponse = CloneResponse(Response);
        fallbackResponse.RequestMessage = request;

        return Task.FromResult(fallbackResponse);
    }

    #endregion // HttpMessageHandler
}