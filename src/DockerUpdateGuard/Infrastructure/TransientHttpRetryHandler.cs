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

    #endregion // Const fields

    #region Fields

    private readonly IOptionsMonitor<DockerUpdateGuardOptions> _options;
    private readonly ILogger<TransientHttpRetryHandler> _logger;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="options">Application options monitor</param>
    /// <param name="logger">Logger</param>
    public TransientHttpRetryHandler(IOptionsMonitor<DockerUpdateGuardOptions> options,
                                     ILogger<TransientHttpRetryHandler> logger)
    {
        _options = options;
        _logger = logger;
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
    /// Compute the backoff delay before the next retry attempt, honoring a Retry-After hint when present
    /// </summary>
    /// <param name="attempt">Zero-based index of the attempt that just failed</param>
    /// <param name="response">Response that failed, or null when an exception occurred</param>
    /// <returns>Delay to wait before the next attempt</returns>
    private static TimeSpan GetRetryDelay(int attempt, HttpResponseMessage? response)
    {
        var retryAfter = response?.Headers.RetryAfter;

        if (retryAfter is not null)
        {
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
        }

        var exponentialMilliseconds = BaseBackoffMilliseconds * Math.Pow(2, attempt);
        var cappedMilliseconds = Math.Min(exponentialMilliseconds, MaxBackoffMilliseconds);

        return TimeSpan.FromMilliseconds(cappedMilliseconds);
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

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                                                                 CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var retryCount = Math.Max(0, _options.CurrentValue.Scanning.RetryCount);
        var requestTarget = GetRequestTarget(request);
        var attempt = 0;

        while (true)
        {
            if (request.Content is not null)
            {
                await request.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
            }

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

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
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

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            attempt++;
        }
    }

    #endregion // Methods
}
