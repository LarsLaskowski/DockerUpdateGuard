using DockerUpdateGuard.Data.Entities;

using MudBlazor;

namespace DockerUpdateGuard.UI;

/// <summary>
/// Shared chip-color formatting for scan statuses
/// </summary>
internal static class ScanStatusDisplayFormatter
{
    #region Fields

    /// <summary>
    /// Chip colors by scan status name, matched case-insensitively
    /// </summary>
    private static readonly Dictionary<string, Color> _statusColors = new(StringComparer.OrdinalIgnoreCase)
                                                                      {
                                                                          [nameof(ScanRunStatus.Succeeded)] = Color.Success,
                                                                          [nameof(ScanRunStatus.Partial)] = Color.Warning,
                                                                          [nameof(ScanRunStatus.Failed)] = Color.Error,
                                                                          [nameof(ScanRunStatus.Running)] = Color.Info,
                                                                          [nameof(ScanRunStatus.Pending)] = Color.Warning,
                                                                      };

    #endregion // Fields

    #region Static methods

    /// <summary>
    /// Resolve the chip color for a scan status
    /// </summary>
    /// <param name="status">Scan status</param>
    /// <returns>Chip color</returns>
    public static Color GetStatusColor(string? status)
    {
        if (status is not null && _statusColors.TryGetValue(status, out var color))
        {
            return color;
        }

        return Color.Default;
    }

    #endregion // Static methods
}