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
        throw new InvalidOperationException("Skeleton: not implemented");
    }

    #endregion // Static methods
}