namespace DockerUpdateGuard.Infrastructure;

/// <summary>
/// Reads the wait a server asked for with a Retry-After header
/// </summary>
internal static class RetryAfterHeader
{
    #region Static methods

    /// <summary>
    /// Read the wait the server asked for with a Retry-After header, without clamping it
    /// </summary>
    /// <param name="response">Response that failed, or null when an exception occurred</param>
    /// <param name="now">Current time used to resolve a date</param>
    /// <returns>The positive wait requested by a delta or a date, or null when the header is missing or asks for no wait</returns>
    internal static TimeSpan? GetWait(HttpResponseMessage? response, DateTimeOffset now)
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
            var untilDate = date - now;

            if (untilDate > TimeSpan.Zero)
            {
                return untilDate;
            }
        }

        return null;
    }

    #endregion // Static methods
}