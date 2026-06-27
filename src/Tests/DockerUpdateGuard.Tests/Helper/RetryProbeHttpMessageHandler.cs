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
    /// Optional Retry-After header value applied to every emitted response
    /// </summary>
    public RetryConditionHeaderValue? RetryAfter { get; set; }

    #endregion // Properties

    #region HttpMessageHandler

    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        AttemptCount++;

        var outcome = _outcomes.Count > 0 ? _outcomes.Dequeue() : _fallbackStatusCode;

        if (outcome is null)
        {
            throw new HttpRequestException("Simulated transient transport failure");
        }

        var response = new HttpResponseMessage(outcome.Value)
                       {
                           RequestMessage = request,
                       };

        if (RetryAfter is not null)
        {
            response.Headers.RetryAfter = RetryAfter;
        }

        return Task.FromResult(response);
    }

    #endregion // HttpMessageHandler
}
