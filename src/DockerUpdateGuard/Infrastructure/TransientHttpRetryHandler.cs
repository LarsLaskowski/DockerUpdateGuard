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
            return requestUri.GetLeftPart(UriPartial.Path);
        }

        return requestUri.OriginalString;
    }

    #endregion // Static methods

    #region Methods

    /// <summary>
    /// Compute the backoff delay before the next retry attempt, honoring a Retry-After hint when present
    /// </summary>
    /// <param name="attempt">Zero-based index of the attempt that just failed</param>
    /// <param name="response">Response that failed, or null when an exception occurred</param>
    /// <returns>Delay to wait before the next attempt</returns>
    private TimeSpan GetRetryDelay(int attempt, HttpResponseMessage? response)
    {
        var retryAfter = response?.Headers.RetryAfter;

        if (retryAfter is not null)
        {
            if (retryAfter.Delta is TimeSpan delta && delta > TimeSpan.Zero)
            {
                return ClampToMaximum(delta);
            }

            if (retryAfter.Date is DateTimeOffset date)
            {
                var untilDate = date - DateTimeOffset.UtcNow;

                if (untilDate > TimeSpan.Zero)
                {
                    return ClampToMaximum(untilDate);
                }
            }
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
                var delay = GetRetryDelay(attempt, response);

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