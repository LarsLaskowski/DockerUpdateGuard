namespace DockerUpdateGuard.UI;

/// <summary>
/// Formatting helpers for resource usage values
/// </summary>
public static class ResourceUsageFormatter
{
    #region Methods

    /// <summary>
    /// Format a CPU percentage
    /// </summary>
    /// <param name="cpuPercent">CPU percentage</param>
    /// <returns>Formatted value</returns>
    public static string FormatCpuPercent(decimal cpuPercent)
    {
        return $"{cpuPercent:F1} %";
    }

    /// <summary>
    /// Format memory usage and limit
    /// </summary>
    /// <param name="usageBytes">Usage bytes</param>
    /// <param name="limitBytes">Limit bytes</param>
    /// <returns>Formatted value</returns>
    public static string FormatMemory(long usageBytes, long limitBytes)
    {
        return limitBytes > 0
                   ? $"{FormatBytes(usageBytes)} / {FormatBytes(limitBytes)}"
                   : FormatBytes(usageBytes);
    }

    /// <summary>
    /// Format a byte-rate value
    /// </summary>
    /// <param name="bytesPerSecond">Rate value</param>
    /// <returns>Formatted value</returns>
    public static string FormatBytesPerSecond(decimal bytesPerSecond)
    {
        return $"{FormatBytes((long)Math.Round(bytesPerSecond, MidpointRounding.AwayFromZero))}/s";
    }

    /// <summary>
    /// Format a byte value
    /// </summary>
    /// <param name="bytes">Byte value</param>
    /// <returns>Formatted value</returns>
    public static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        decimal value = bytes;
        var suffixIndex = 0;

        while (value >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            value /= 1024;
            suffixIndex++;
        }

        return $"{value:F1} {suffixes[suffixIndex]}";
    }

    #endregion // Methods
}