using System.Net;
using System.Net.Http.Headers;

namespace DockerUpdateGuard.Tests.Helper;

/// <summary>
/// HTTP message handler that replays a configured sequence of outcomes and counts attempts
/// </summary>
internal sealed class RetryProbeHttpMessageHandler : HttpMessageHandler
{
    #region Fields

    private readonly Queue<HttpStatusCode?> _outcomes;
    private readonly HttpStatusCode _fallbackStatusCode;
    private readonly List<string?> _requestBodies = [];
    private readonly List<long?> _contentLengths = [];

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="outcomes">Outcomes to replay; a null entry throws a transient transport failure</param>
    /// <param name="fallbackStatusCode">Status code returned once the configured outcomes are exhausted</param>
    public RetryProbeHttpMessageHandler(IEnumerable<HttpStatusCode?> outcomes, HttpStatusCode fallbackStatusCode)
    {
        _outcomes = new Queue<HttpStatusCode?>(outcomes);
        _fallbackStatusCode = fallbackStatusCode;
    }

    #endregion // Constructors

    #region Properties

    /// <summary>
    /// Number of times the handler was invoked
    /// </summary>
    public int AttemptCount { get; private set; }

    /// <summary>
    /// URI text of the most recent request the handler received
    /// </summary>
    public string? LastRequestUri { get; private set; }

    /// <summary>
    /// Optional Retry-After header value applied to every emitted response
    /// </summary>
    public RetryConditionHeaderValue? RetryAfter { get; set; }

    /// <summary>
    /// Factory that creates the exception thrown for a null outcome; defaults to a plain transient transport failure
    /// </summary>
    public Func<Exception> FailureFactory { get; set; } = () => new HttpRequestException("Simulated transient transport failure");

    /// <summary>
    /// Request bodies received per attempt; null for an attempt without content
    /// </summary>
    public IReadOnlyList<string?> RequestBodies => _requestBodies;

    /// <summary>
    /// Content-Length header values received per attempt; null when the attempt had no content or no length
    /// </summary>
    public IReadOnlyList<long?> ContentLengths => _contentLengths;

    #endregion // Properties

    #region HttpMessageHandler

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        AttemptCount++;
        LastRequestUri = request.RequestUri is { IsAbsoluteUri: false } relativeUri ? relativeUri.OriginalString : request.RequestUri?.AbsoluteUri;

        _contentLengths.Add(request.Content?.Headers.ContentLength);
        _requestBodies.Add(request.Content is null
                               ? null
                               : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

        var outcome = _outcomes.Count > 0 ? _outcomes.Dequeue() : _fallbackStatusCode;

        if (outcome is null)
        {
            throw FailureFactory();
        }

        var response = new HttpResponseMessage(outcome.Value)
                       {
                           RequestMessage = request,
                       };

        if (RetryAfter is not null)
        {
            response.Headers.RetryAfter = RetryAfter;
        }

        return response;
    }

    #endregion // HttpMessageHandler
}