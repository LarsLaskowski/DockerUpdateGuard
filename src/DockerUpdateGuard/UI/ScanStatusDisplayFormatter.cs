using DockerUpdateGuard.Data.Entities;

using MudBlazor;

namespace DockerUpdateGuard.UI;

/// <summary>
/// Shared chip-color formatting for scan statuses
/// </summary>
internal static class ScanStatusDisplayFormatter
{
    #region Fields

#pragma warning disable S1144 // Skeleton: removed with the implementation
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
#pragma warning restore S1144

    #endregion // Fields

    #region Static methods

#pragma warning disable S1172 // Skeleton: removed with the implementation
    /// <summary>
    /// Resolve the chip color for a scan status
    /// </summary>
    /// <param name="status">Scan status</param>
    /// <returns>Chip color</returns>
    public static Color GetStatusColor(string? status)
    {
        throw new InvalidOperationException("Skeleton: not implemented");
    }
#pragma warning restore S1172

    #endregion // Static methods
}