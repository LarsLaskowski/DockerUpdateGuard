using System.Globalization;
using System.Net;

using DockerUpdateGuard.Configuration;

using Microsoft.Extensions.Options;

namespace DockerUpdateGuard.Infrastructure;

/// <summary>
/// Delegating handler that retries transient HTTP failures with exponential backoff,
/// honoring the configured <see cref="ScanningOptions.RetryCount"/>
/// </summary>
public class TransientHttpRetryHandler : DelegatingHandler
{
    #region Const fields

    /// <summary>
    /// Base backoff delay in milliseconds applied before the first retry
    /// </summary>
    private const int BaseBackoffMilliseconds = 500;

    /// <summary>
    /// Maximum backoff delay in milliseconds between retries
    /// </summary>
    private const int MaxBackoffMilliseconds = 30000;

    /// <summary>
    /// Fraction of the exponential delay added as random jitter to avoid synchronized retries
    /// </summary>
    private const double JitterFactor = 0.2;

    #endregion // Const fields

    #region Fields

    private readonly IOptionsMonitor<DockerUpdateGuardOptions> _options;
    private readonly ILogger<TransientHttpRetryHandler> _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly Func<int, int> _nextJitterMilliseconds;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="options">Application options monitor</param>
    /// <param name="logger">Logger</param>
    public TransientHttpRetryHandler(IOptionsMonitor<DockerUpdateGuardOptions> options,
                                     ILogger<TransientHttpRetryHandler> logger)
        : this(options,
               logger,
               (delay, cancellationToken) => Task.Delay(delay, cancellationToken),
               maxExclusive => maxExclusive <= 0 ? 0 : Random.Shared.Next(maxExclusive))
    {
    }

    /// <summary>
    /// Constructor that allows the backoff delay and jitter to be supplied, used for deterministic testing
    /// </summary>
    /// <param name="options">Application options monitor</param>
    /// <param name="logger">Logger</param>
    /// <param name="delayAsync">Delay callback invoked before each retry attempt</param>
    /// <param name="nextJitterMilliseconds">Jitter provider returning a value in the range zero to the supplied exclusive upper bound</param>
    internal TransientHttpRetryHandler(IOptionsMonitor<DockerUpdateGuardOptions> options,
                                       ILogger<TransientHttpRetryHandler> logger,
                                       Func<TimeSpan, CancellationToken, Task> delayAsync,
                                       Func<int, int> nextJitterMilliseconds)
    {
        _options = options;
        _logger = logger;
        _delayAsync = delayAsync;
        _nextJitterMilliseconds = nextJitterMilliseconds;
    }

    #endregion // Constructors

    #region Static methods

    /// <summary>
    /// Determine whether a status code represents a transient failure that may succeed on retry
    /// </summary>
    /// <param name="statusCode">HTTP status code</param>
    /// <returns>True when the status code is transient</returns>
    private static bool IsTransientStatusCode(HttpStatusCode statusCode)
    {
        return statusCode == HttpStatusCode.RequestTimeout
               || statusCode == HttpStatusCode.TooManyRequests
               || (int)statusCode >= 500;
    }

    /// <summary>
    /// Determine whether a request method is idempotent, so that repeating it cannot run its effect twice
    /// </summary>
    /// <param name="method">HTTP method</param>
    /// <returns>True for GET, HEAD, OPTIONS, TRACE, PUT and DELETE; false for every other method</returns>
    private static bool IsIdempotentMethod(HttpMethod method)
    {
        return method == HttpMethod.Get
               || method == HttpMethod.Head
               || method == HttpMethod.Options
               || method == HttpMethod.Trace
               || method == HttpMethod.Put
               || method == HttpMethod.Delete;
    }

    /// <summary>
    /// Determine whether a transient status code proves that the server did not process the request
    /// </summary>
    /// <param name="statusCode">HTTP status code</param>
    /// <returns>True when the server rejected the request without processing it (429)</returns>
    private static bool IsNotProcessedStatusCode(HttpStatusCode statusCode)
    {
        return statusCode == HttpStatusCode.TooManyRequests;
    }

    /// <summary>
    /// Determine whether an exception was raised before any byte of the request reached the server
    /// </summary>
    /// <param name="exception">Exception</param>
    /// <returns>True for a connection-phase error of the top-level HTTP request exception</returns>
    private static bool IsRaisedBeforeSending(Exception exception)
    {
        return exception is HttpRequestException
                         {
                             HttpRequestError: HttpRequestError.NameResolutionError
                                               or HttpRequestError.ConnectionError
                                               or HttpRequestError.SecureConnectionError
                                               or HttpRequestError.ProxyTunnelError
                         };
    }

    /// <summary>
    /// Describe the outcome of a failed send as a log-safe token without the exception message
    /// </summary>
    /// <param name="exception">Exception</param>
    /// <returns>The HTTP request error name, or Timeout for a timeout</returns>
    private static string GetExceptionOutcome(Exception exception)
    {
        return exception is HttpRequestException httpRequestException
                   ? httpRequestException.HttpRequestError.ToString()
                   : "Timeout";
    }

    /// <summary>
    /// Determine whether an exception represents a transient transport failure
    /// </summary>
    /// <param name="exception">Exception</param>
    /// <returns>True when the exception is transient</returns>
    private static bool IsTransientException(Exception exception)
    {
        return exception is HttpRequestException or TimeoutException;
    }

    /// <summary>
    /// Clamp a delay to the configured maximum backoff
    /// </summary>
    /// <param name="delay">Delay to clamp</param>
    /// <returns>Delay no greater than the maximum backoff</returns>
    private static TimeSpan ClampToMaximum(TimeSpan delay)
    {
        var maximum = TimeSpan.FromMilliseconds(MaxBackoffMilliseconds);

        return delay > maximum ? maximum : delay;
    }

    /// <summary>
    /// Read the wait the server asked for with a Retry-After header, without clamping it
    /// </summary>
    /// <param name="response">Response that failed, or null when an exception occurred</param>
    /// <returns>The positive wait requested by a delta or a date, or null when the header is missing or asks for no wait</returns>
    private static TimeSpan? GetRetryAfter(HttpResponseMessage? response)
    {
        var retryAfter = response?.Headers.RetryAfter;

        if (retryAfter is null)
        {
            return null;
        }

        if (retryAfter.Delta is TimeSpan delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        if (retryAfter.Date is DateTimeOffset date)
        {
            var untilDate = date - DateTimeOffset.UtcNow;

            if (untilDate > TimeSpan.Zero)
            {
                return untilDate;
            }
        }

        return null;
    }

    /// <summary>
    /// Build a log-safe request target that omits the query string to avoid leaking credentials
    /// </summary>
    /// <param name="request">Request message</param>
    /// <returns>Request target description</returns>
    private static string GetRequestTarget(HttpRequestMessage request)
    {
        var requestUri = request.RequestUri;

        if (requestUri is null)
        {
            return "(unknown)";
        }

        if (requestUri.IsAbsoluteUri)
        {
            return UrlRedaction.RedactUserInfo(requestUri.GetLeftPart(UriPartial.Path));
        }

        var originalString = requestUri.OriginalString;
        var queryOrFragment = originalString.IndexOfAny(['?', '#']);

        return UrlRedaction.RedactUserInfo(queryOrFragment < 0 ? originalString : originalString[..queryOrFragment]);
    }

    #endregion // Static methods

    #region Methods

    /// <summary>
    /// Log that a transient outcome of a non-idempotent request was not retried
    /// </summary>
    /// <param name="request">Request message</param>
    /// <param name="requestTarget">Log-safe request target</param>
    /// <param name="outcome">Status code number, HTTP request error name or Timeout</param>
    private void LogNotRetried(HttpRequestMessage request, string requestTarget, string outcome)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.NonIdempotentRequestNotRetried(request.Method.Method, requestTarget, outcome);
        }
    }

    /// <summary>
    /// Log that a non-idempotent request was not retried because the server asked for a longer wait than the maximum
    /// </summary>
    /// <param name="request">Request message</param>
    /// <param name="requestTarget">Log-safe request target</param>
    /// <param name="statusCode">Status code number</param>
    /// <param name="retryAfter">Wait requested by the server</param>
    private void LogRetryAfterExceedsMaximum(HttpRequestMessage request,
                                             string requestTarget,
                                             int statusCode,
                                             TimeSpan retryAfter)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.RetryAfterExceedsMaximumNotRetried(request.Method.Method,
                                                       requestTarget,
                                                       statusCode,
                                                       (long)retryAfter.TotalMilliseconds,
                                                       MaxBackoffMilliseconds);
        }
    }

    /// <summary>
    /// Determine whether a non-idempotent request must not be retried because the server asked for a longer wait than
    /// the maximum, and log the decision
    /// </summary>
    /// <param name="request">Request message</param>
    /// <param name="requestTarget">Log-safe request target</param>
    /// <param name="statusCode">Status code number</param>
    /// <param name="isIdempotent">True when the request method is idempotent</param>
    /// <param name="retryAfter">Wait requested by the server, or null when there is none</param>
    /// <returns>True when the response is to be returned without a retry</returns>
    private bool IsRetryAfterBeyondMaximum(HttpRequestMessage request,
                                           string requestTarget,
                                           int statusCode,
                                           bool isIdempotent,
                                           TimeSpan? retryAfter)
    {
        if (isIdempotent || retryAfter is not TimeSpan requested || requested.TotalMilliseconds <= MaxBackoffMilliseconds)
        {
            return false;
        }

        LogRetryAfterExceedsMaximum(request, requestTarget, statusCode, requested);

        return true;
    }

    /// <summary>
    /// Compute the backoff delay before the next retry attempt, honoring a Retry-After hint when present
    /// </summary>
    /// <param name="attempt">Zero-based index of the attempt that just failed</param>
    /// <param name="retryAfter">Wait requested by the server, or null when there is none</param>
    /// <returns>Delay to wait before the next attempt</returns>
    private TimeSpan GetRetryDelay(int attempt, TimeSpan? retryAfter)
    {
        if (retryAfter is TimeSpan requested)
        {
            return ClampToMaximum(requested);
        }

        var exponentialMilliseconds = Math.Min(BaseBackoffMilliseconds * Math.Pow(2, attempt), MaxBackoffMilliseconds);
        var jitterMilliseconds = _nextJitterMilliseconds((int)(exponentialMilliseconds * JitterFactor));
        var totalMilliseconds = Math.Min(exponentialMilliseconds + jitterMilliseconds, MaxBackoffMilliseconds);

        return TimeSpan.FromMilliseconds(totalMilliseconds);
    }

    #endregion // Methods

    #region HttpMessageHandler

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                                                                 CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Content is not null)
        {
            await request.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
        }

        var retryCount = Math.Max(0, _options.CurrentValue.Scanning.RetryCount);
        var requestTarget = GetRequestTarget(request);
        var isIdempotent = IsIdempotentMethod(request.Method);
        var attempt = 0;

        while (true)
        {
            try
            {
                var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

                if (IsTransientStatusCode(response.StatusCode) == false || attempt >= retryCount)
                {
                    return response;
                }

                var statusCode = (int)response.StatusCode;

                if (isIdempotent == false && IsNotProcessedStatusCode(response.StatusCode) == false)
                {
                    LogNotRetried(request, requestTarget, statusCode.ToString(CultureInfo.InvariantCulture));

                    return response;
                }

                var retryAfter = GetRetryAfter(response);

                if (IsRetryAfterBeyondMaximum(request, requestTarget, statusCode, isIdempotent, retryAfter))
                {
                    return response;
                }

                var delay = GetRetryDelay(attempt, retryAfter);

                response.Dispose();
                _logger.TransientResponseRetry(request.Method.Method,
                                               requestTarget,
                                               statusCode,
                                               attempt + 1,
                                               retryCount,
                                               (int)delay.TotalMilliseconds);

                await _delayAsync(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsTransientException(exception)
                                              && cancellationToken.IsCancellationRequested == false
                                              && attempt < retryCount)
            {
                if (isIdempotent == false && IsRaisedBeforeSending(exception) == false)
                {
                    LogNotRetried(request, requestTarget, GetExceptionOutcome(exception));

                    throw;
                }

                var delay = GetRetryDelay(attempt, null);

                _logger.TransientExceptionRetry(exception,
                                                request.Method.Method,
                                                requestTarget,
                                                attempt + 1,
                                                retryCount,
                                                (int)delay.TotalMilliseconds);

                await _delayAsync(delay, cancellationToken).ConfigureAwait(false);
            }

            attempt++;
        }
    }

    #endregion // HttpMessageHandler
}