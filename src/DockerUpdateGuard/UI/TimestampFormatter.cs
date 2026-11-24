using System.Globalization;

namespace DockerUpdateGuard.UI;

/// <summary>
/// Formats timestamps for display in the user interface
/// </summary>
public static class TimestampFormatter
{
    #region Constants

    /// <summary>
    /// Format of a full timestamp
    /// </summary>
    private const string TimestampFormat = "yyyy-MM-dd HH:mm 'UTC'";

    /// <summary>
    /// Format of a chart label with date and zone
    /// </summary>
    private const string ChartLabelWithDateFormat = "MM-dd HH:mm 'UTC'";

    /// <summary>
    /// Format of a chart label with the time only
    /// </summary>
    private const string ChartLabelTimeFormat = "HH:mm";

    #endregion // Constants

    #region Static methods

    /// <summary>
    /// Format a timestamp as UTC text
    /// </summary>
    /// <param name="timestamp">Timestamp to format</param>
    /// <returns>Formatted timestamp</returns>
    public static string Format(DateTimeOffset timestamp)
    {
        return timestamp.ToUniversalTime()
                        .ToString(TimestampFormat, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Format an optional timestamp as UTC text
    /// </summary>
    /// <param name="timestamp">Timestamp to format, if any</param>
    /// <returns>Formatted timestamp or an empty text when there is none</returns>
    public static string FormatOptional(DateTimeOffset? timestamp)
    {
        return timestamp is DateTimeOffset value
                   ? Format(value)
                   : string.Empty;
    }

    /// <summary>
    /// Format a timestamp as a chart axis label
    /// </summary>
    /// <param name="timestamp">Timestamp to format</param>
    /// <param name="includeDateAndZone">Include the date and the time zone in the label</param>
    /// <returns>Chart label</returns>
    public static string FormatChartLabel(DateTimeOffset timestamp, bool includeDateAndZone)
    {
        return timestamp.ToUniversalTime()
                        .ToString(includeDateAndZone ? ChartLabelWithDateFormat : ChartLabelTimeFormat, CultureInfo.InvariantCulture);
    }

    #endregion // Static methods
}