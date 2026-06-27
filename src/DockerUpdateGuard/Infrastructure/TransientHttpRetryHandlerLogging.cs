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

    #endregion // Methods
}
