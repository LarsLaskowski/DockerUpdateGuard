namespace DockerUpdateGuard.Infrastructure;

/// <summary>
/// Maps exceptions to fixed failure reasons that are safe to put into results and scan runs
/// </summary>
internal static class FailureReason
{
    #region Constants

    /// <summary>
    /// Reason for a cancelled operation
    /// </summary>
    public const string Cancelled = "cancelled";

    /// <summary>
    /// Reason for an operation that timed out
    /// </summary>
    public const string TimedOut = "timed out";

    /// <summary>
    /// Reason for a transport failure
    /// </summary>
    public const string ConnectionFailed = "connection failed";

    /// <summary>
    /// Reason for a malformed response
    /// </summary>
    public const string InvalidResponse = "invalid response";

    /// <summary>
    /// Reason for any other error
    /// </summary>
    public const string UnexpectedError = "unexpected error";

    #endregion // Constants

    #region Methods

    /// <summary>
    /// Map an exception to a fixed failure reason
    /// </summary>
    /// <param name="exception">Exception to map</param>
    /// <param name="timeout">Timeout of the failed call, if it has one</param>
    /// <param name="cancellationToken">Token of the caller</param>
    /// <returns>Fixed failure reason</returns>
    public static string FromException(Exception exception,
                                       TimeSpan? timeout,
                                       CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("Skeleton: not implemented");
    }

    #endregion // Methods
}