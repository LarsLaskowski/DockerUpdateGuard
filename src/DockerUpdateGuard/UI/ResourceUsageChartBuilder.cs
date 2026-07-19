using MudBlazor;

namespace DockerUpdateGuard.UI;

/// <summary>
/// Builds chart data from resource usage history
/// </summary>
internal static class ResourceUsageChartBuilder
{
    #region Methods

    /// <summary>
    /// Build chart labels from the resource history
    /// </summary>
    /// <param name="history">Resource history</param>
    /// <returns>Chart labels</returns>
    internal static string[] GetChartLabels(IReadOnlyList<ResourceUsagePointViewData> history)
    {
        return GetChartLabels(history, 8);
    }

    /// <summary>
    /// Build chart labels from the resource history with a maximum number of visible labels
    /// </summary>
    /// <param name="history">Resource history</param>
    /// <param name="maxVisibleLabels">Maximum number of visible labels</param>
    /// <returns>Chart labels</returns>
    internal static string[] GetChartLabels(IReadOnlyList<ResourceUsagePointViewData> history, int maxVisibleLabels)
    {
        var chronologicalHistory = GetChronologicalHistory(history);

        if (chronologicalHistory.Count == 0)
        {
            return [];
        }

        var effectiveMaxVisibleLabels = Math.Max(2, maxVisibleLabels);
        var visibleLabelStep = chronologicalHistory.Count <= effectiveMaxVisibleLabels
                                   ? 1
                                   : (int)Math.Ceiling((chronologicalHistory.Count - 1d) / (effectiveMaxVisibleLabels - 1d));

        return chronologicalHistory.Select((point, index) =>
                                           {
                                               if (index != 0
                                                   && index != chronologicalHistory.Count - 1
                                                   && index % visibleLabelStep != 0)
                                               {
                                                   return string.Empty;
                                               }

                                               return FormatChartLabel(point.RecordedAtUtc, index, chronologicalHistory.Count);
                                           })
                                   .ToArray();
    }

    /// <summary>
    /// Build a CPU chart series
    /// </summary>
    /// <param name="history">Resource history</param>
    /// <returns>Chart series</returns>
    internal static List<ChartSeries<double>> GetCpuSeries(IReadOnlyList<ResourceUsagePointViewData> history)
    {
        return [
                   new ChartSeries<double>
                   {
                       Name = "CPU",
                       Data = GetChronologicalHistory(history).Select(point => (double)point.CpuPercent)
                                                              .ToArray(),
                   },
               ];
    }

    /// <summary>
    /// Build a memory chart series
    /// </summary>
    /// <param name="history">Resource history</param>
    /// <returns>Chart series</returns>
    internal static List<ChartSeries<double>> GetMemorySeries(IReadOnlyList<ResourceUsagePointViewData> history)
    {
        return [
                   new ChartSeries<double>
                   {
                       Name = "Memory (MiB)",
                       Data = GetChronologicalHistory(history).Select(point => ConvertBytesToMebibytes(point.MemoryUsageBytes))
                                                              .ToArray(),
                   },
               ];
    }

    /// <summary>
    /// Build network chart series
    /// </summary>
    /// <param name="history">Resource history</param>
    /// <returns>Chart series</returns>
    internal static List<ChartSeries<double>> GetNetworkSeries(IReadOnlyList<ResourceUsagePointViewData> history)
    {
        var chronologicalHistory = GetChronologicalHistory(history);

        return [
                   new ChartSeries<double>
                   {
                       Name = "Receive (KB/s)",
                       Data = chronologicalHistory.Select(point => ConvertBytesPerSecondToKilobytes(point.NetworkRxBytesPerSecond))
                                                  .ToArray(),
                   },
                   new ChartSeries<double>
                   {
                       Name = "Transmit (KB/s)",
                       Data = chronologicalHistory.Select(point => ConvertBytesPerSecondToKilobytes(point.NetworkTxBytesPerSecond))
                                                  .ToArray(),
                   },
               ];
    }

    /// <summary>
    /// Order the resource history from oldest to newest
    /// </summary>
    /// <param name="history">Resource history</param>
    /// <returns>Chronological history</returns>
    private static List<ResourceUsagePointViewData> GetChronologicalHistory(IReadOnlyList<ResourceUsagePointViewData> history)
    {
        return history.OrderBy(point => point.RecordedAtUtc)
                      .ToList();
    }

    /// <summary>
    /// Format a chart label based on its position in the series
    /// </summary>
    /// <param name="recordedAtUtc">Recorded timestamp</param>
    /// <param name="index">Zero-based label index</param>
    /// <param name="labelCount">Total label count</param>
    /// <returns>Formatted label</returns>
    private static string FormatChartLabel(DateTimeOffset recordedAtUtc, int index, int labelCount)
    {
        var localTime = recordedAtUtc.ToLocalTime();

        return index == 0 || index == labelCount - 1
                   ? localTime.ToString("MM-dd HH:mm")
                   : localTime.ToString("HH:mm");
    }

    /// <summary>
    /// Convert a byte count to mebibytes
    /// </summary>
    /// <param name="bytes">Byte count</param>
    /// <returns>Mebibytes</returns>
    private static double ConvertBytesToMebibytes(long bytes)
    {
        return bytes / 1024d / 1024d;
    }

    /// <summary>
    /// Convert bytes per second to kilobytes per second
    /// </summary>
    /// <param name="value">Bytes per second</param>
    /// <returns>Kilobytes per second</returns>
    private static double ConvertBytesPerSecondToKilobytes(decimal value)
    {
        return (double)value / 1024d;
    }

    #endregion // Methods
}