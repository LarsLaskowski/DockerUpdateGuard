namespace DockerUpdateGuard.Infrastructure;

/// <summary>
/// Source-generated logging contracts for <see cref="TransientHttpRetryHandler"/>
/// </summary>
internal static partial class TransientHttpRetryHandlerLogging
{
    #region Methods

    /// <summary>
    /// Log that a transient response status triggered a retry
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="method">HTTP method</param>
    /// <param name="requestTarget">Request target</param>
    /// <param name="statusCode">HTTP status code</param>
    /// <param name="attempt">One-based number of the attempt that failed</param>
    /// <param name="retryCount">Configured retry count</param>
    /// <param name="delayMilliseconds">Backoff delay before the next attempt in milliseconds</param>
    [LoggerMessage(EventId = 3500,
                   Level = LogLevel.Warning,
                   Message = "Transient HTTP response {StatusCode} for {Method} {RequestTarget}; retrying ({Attempt}/{RetryCount}) after {DelayMilliseconds} ms")]
    public static partial void TransientResponseRetry(this ILogger logger,
                                                      string method,
                                                      string requestTarget,
                                                      int statusCode,
                                                      int attempt,
                                                      int retryCount,
                                                      int delayMilliseconds);

    /// <summary>
    /// Log that a transient transport exception triggered a retry
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="exception">Exception</param>
    /// <param name="method">HTTP method</param>
    /// <param name="requestTarget">Request target</param>
    /// <param name="attempt">One-based number of the attempt that failed</param>
    /// <param name="retryCount">Configured retry count</param>
    /// <param name="delayMilliseconds">Backoff delay before the next attempt in milliseconds</param>
    [LoggerMessage(EventId = 3501,
                   Level = LogLevel.Warning,
                   Message = "Transient HTTP failure for {Method} {RequestTarget}; retrying ({Attempt}/{RetryCount}) after {DelayMilliseconds} ms")]
    public static partial void TransientExceptionRetry(this ILogger logger,
                                                       Exception exception,
                                                       string method,
                                                       string requestTarget,
                                                       int attempt,
                                                       int retryCount,
                                                       int delayMilliseconds);

    /// <summary>
    /// Log that a transient outcome of a non-idempotent request was not retried
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="method">HTTP method</param>
    /// <param name="requestTarget">Request target</param>
    /// <param name="outcome">Status code number, HTTP request error name or Timeout</param>
    [LoggerMessage(EventId = 3502,
                   Level = LogLevel.Debug,
                   Message = "Transient HTTP outcome {Outcome} for {Method} {RequestTarget} not retried: the method is not idempotent")]
    public static partial void NonIdempotentRequestNotRetried(this ILogger logger,
                                                              string method,
                                                              string requestTarget,
                                                              string outcome);

    /// <summary>
    /// Log that a non-idempotent request was not retried because the Retry-After wait exceeds the maximum wait
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="method">HTTP method</param>
    /// <param name="requestTarget">Request target</param>
    /// <param name="statusCode">HTTP status code</param>
    /// <param name="retryAfterMilliseconds">Wait requested by the server in milliseconds</param>
    /// <param name="maximumMilliseconds">Maximum wait in milliseconds</param>
    [LoggerMessage(EventId = 3503,
                   Level = LogLevel.Debug,
                   Message = "Transient HTTP response {StatusCode} for {Method} {RequestTarget} not retried: Retry-After of {RetryAfterMilliseconds} ms exceeds the maximum wait of {MaximumMilliseconds} ms for a method that is not idempotent")]
    public static partial void RetryAfterExceedsMaximumNotRetried(this ILogger logger,
                                                                  string method,
                                                                  string requestTarget,
                                                                  int statusCode,
                                                                  long retryAfterMilliseconds,
                                                                  int maximumMilliseconds);

    #endregion // Methods
}