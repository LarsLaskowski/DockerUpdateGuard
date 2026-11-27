using System.Globalization;

namespace DockerUpdateGuard.Infrastructure;

/// <summary>
/// Parses timestamps that external systems deliver as text into UTC instants
/// </summary>
internal static class ExternalTimestamp
{
    #region Static methods

    /// <summary>
    /// Parse a timestamp text into a UTC instant, reading a value without an offset as UTC
    /// </summary>
    /// <param name="value">Timestamp text</param>
    /// <returns>Parsed instant, null when the text is null, empty or not a timestamp</returns>
    public static DateTimeOffset? TryParseUtc(string? value)
    {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp))
        {
            return timestamp;
        }

        return null;
    }

    // Skeleton: removed with the implementation
#pragma warning disable S1172
    /// <summary>
    /// Convert a Unix time in seconds into a UTC instant
    /// </summary>
    /// <param name="seconds">Seconds since the Unix epoch</param>
    /// <returns>UTC instant, null when the value is zero, negative or out of the representable range</returns>
    public static DateTimeOffset? TryFromUnixSeconds(long seconds)
    {
        throw new InvalidOperationException("Skeleton: not implemented");
    }
#pragma warning restore S1172

    #endregion // Static methods
}