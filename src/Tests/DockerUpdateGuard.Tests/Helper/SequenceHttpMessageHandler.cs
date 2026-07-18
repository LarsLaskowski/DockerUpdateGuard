using System.Net;
using System.Text;

using DockerUpdateGuard.Tests.Data;

namespace DockerUpdateGuard.Tests.Helper;

/// <summary>
/// Sequence-based HTTP message handler for deterministic Docker engine tests
/// </summary>
internal sealed class SequenceHttpMessageHandler : HttpMessageHandler
{
    #region Fields

    private readonly Dictionary<string, Queue<HttpResponseMessage>> _responses = new(StringComparer.Ordinal);
    private readonly List<ObservedRequest> _requests = [];

    #endregion // Fields

    #region Properties

    /// <summary>
    /// Observed outbound requests
    /// </summary>
    public IReadOnlyList<ObservedRequest> Requests => _requests;

    #endregion // Properties

    #region Methods

    /// <summary>
    /// Add a JSON response to the request sequence for a URI
    /// </summary>
    /// <param name="requestUri">Absolute request URI</param>
    /// <param name="jsonContent">JSON content</param>
    public void AddJsonResponse(string requestUri, string jsonContent)
    {
        AddResponse(requestUri,
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(jsonContent,
                                                    Encoding.UTF8,
                                                    "application/json"),
                    });
    }

    /// <summary>
    /// Add a response to the request sequence for a URI
    /// </summary>
    /// <param name="requestUri">Absolute request URI</param>
    /// <param name="response">Configured response</param>
    public void AddResponse(string requestUri, HttpResponseMessage response)
    {
        var normalizedRequestUri = new Uri(requestUri).AbsoluteUri;

        if (_responses.TryGetValue(normalizedRequestUri, out var queue) == false)
        {
            queue = new Queue<HttpResponseMessage>();

            _responses[normalizedRequestUri] = queue;
        }

        queue.Enqueue(response);
    }

    /// <summary>
    /// Clone a configured response for repeatable handler usage
    /// </summary>
    /// <param name="response">Template response</param>
    /// <returns>Cloned response</returns>
    private static HttpResponseMessage CloneResponse(HttpResponseMessage response)
    {
        var clone = new HttpResponseMessage(response.StatusCode)
                    {
                        Content = response.Content is null
                                      ? null
                                      : new StringContent(response.Content.ReadAsStringAsync().GetAwaiter().GetResult(),
                                                          Encoding.UTF8,
                                                          response.Content.Headers.ContentType?.MediaType),
                    };

        foreach (var header in response.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }

    #endregion // Methods

    #region HttpMessageHandler

    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request.RequestUri);

        _requests.Add(new ObservedRequest
                      {
                          Method = request.Method.Method,
                          RequestUri = request.RequestUri.AbsoluteUri,
                          AuthorizationScheme = request.Headers.Authorization?.Scheme,
                          AuthorizationParameter = request.Headers.Authorization?.Parameter,
                          ApiKeyHeader = request.Headers.TryGetValues("X-API-Key", out var apiKeyValues)
                                             ? apiKeyValues.FirstOrDefault()
                                             : null,
                      });

        if (_responses.TryGetValue(request.RequestUri.AbsoluteUri, out var queue)
            && queue.Count > 0)
        {
            return Task.FromResult(CloneResponse(queue.Dequeue()));
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                               {
                                   RequestMessage = request,
                               });
    }

    #endregion // HttpMessageHandler
}