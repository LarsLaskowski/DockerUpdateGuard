namespace DockerUpdateGuard.UI;

/// <summary>
/// Formats timestamps for display in the user interface
/// </summary>
public static class TimestampFormatter
{
    #region Static methods

    /// <summary>
    /// Format a timestamp as UTC text
    /// </summary>
    /// <param name="timestamp">Timestamp to format</param>
    /// <returns>Formatted timestamp</returns>
    public static string Format(DateTimeOffset timestamp)
    {
        throw new InvalidOperationException("Skeleton: not implemented");
    }

    /// <summary>
    /// Format an optional timestamp as UTC text
    /// </summary>
    /// <param name="timestamp">Timestamp to format, if any</param>
    /// <returns>Formatted timestamp or a placeholder when there is none</returns>
    public static string FormatOptional(DateTimeOffset? timestamp)
    {
        throw new InvalidOperationException("Skeleton: not implemented");
    }

    /// <summary>
    /// Format a timestamp as a chart axis label
    /// </summary>
    /// <param name="timestamp">Timestamp to format</param>
    /// <param name="includeDateAndZone">Include the date and the time zone in the label</param>
    /// <returns>Chart label</returns>
    public static string FormatChartLabel(DateTimeOffset timestamp, bool includeDateAndZone)
    {
        throw new InvalidOperationException("Skeleton: not implemented");
    }

    #endregion // Static methods
}