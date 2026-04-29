using System.Globalization;

using DockerUpdateGuard.UI;

using Microsoft.AspNetCore.Components;

using MudBlazor;

namespace DockerUpdateGuard.Components.Pages;

/// <summary>
/// Runtime containers page
/// </summary>
public partial class RuntimeContainers
{
    #region Const fields

    /// <summary>
    /// Sparkline width
    /// </summary>
    private const double SparklineWidth = 100d;

    /// <summary>
    /// Sparkline height
    /// </summary>
    private const double SparklineHeight = 26d;

    /// <summary>
    /// Sparkline padding
    /// </summary>
    private const double SparklinePadding = 2d;

    #endregion // Const fields

    #region Fields

    /// <summary>
    /// Runtime-container list
    /// </summary>
    private IReadOnlyList<RuntimeContainerListItemData>? _containers;

    #endregion // Fields

    #region Properties

    /// <summary>
    /// Application view service
    /// </summary>
    [Inject]
    public IApplicationViewService ViewService { get; set; } = null!;

    #endregion // Properties

    #region Static methods

    /// <summary>
    /// Resolve the chip color for an update state
    /// </summary>
    /// <param name="state">Update state</param>
    /// <returns>Chip color</returns>
    private static Color GetUpdateStateColor(string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return Color.Default;
        }

        return state.ToUpperInvariant() switch
               {
                   "UPDATE AVAILABLE" => Color.Warning,
                   "UP TO DATE" => Color.Success,
                   "MANUAL REVIEW REQUIRED" => Color.Info,
                   "FAILED" => Color.Error,
                   "UNSUPPORTED" => Color.Default,
                   _ => Color.Info,
               };
    }

    /// <summary>
    /// Resolve the chip color for a vulnerability status
    /// </summary>
    /// <param name="status">Vulnerability status</param>
    /// <returns>Chip color</returns>
    private static Color GetVulnerabilityStatusColor(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return Color.Default;
        }

        return status.ToUpperInvariant() switch
               {
                   "FINDINGS DETECTED" => Color.Warning,
                   "NO FINDINGS" => Color.Success,
                   "FAILED" => Color.Error,
                   "NOT CONFIGURED" => Color.Default,
                   "UNSUPPORTED" => Color.Default,
                   _ => Color.Info,
               };
    }

    /// <summary>
    /// Format CPU usage
    /// </summary>
    /// <param name="usage">Resource usage</param>
    /// <returns>Formatted value</returns>
    private static string FormatCpu(ResourceUsagePointViewData? usage)
    {
        return usage is null ? "n/a" : ResourceUsageFormatter.FormatCpuPercent(usage.CpuPercent);
    }

    /// <summary>
    /// Format memory usage
    /// </summary>
    /// <param name="usage">Resource usage</param>
    /// <returns>Formatted value</returns>
    private static string FormatMemory(ResourceUsagePointViewData? usage)
    {
        return usage is null ? "n/a" : ResourceUsageFormatter.FormatMemory(usage.MemoryUsageBytes, usage.MemoryLimitBytes);
    }

    /// <summary>
    /// Format network usage
    /// </summary>
    /// <param name="usage">Resource usage</param>
    /// <returns>Formatted value</returns>
    private static string FormatNetwork(ResourceUsagePointViewData? usage)
    {
        return usage is null
                   ? "n/a"
                   : $"{ResourceUsageFormatter.FormatBytesPerSecond(usage.NetworkRxBytesPerSecond)} ↓ / {ResourceUsageFormatter.FormatBytesPerSecond(usage.NetworkTxBytesPerSecond)} ↑";
    }

    /// <summary>
    /// Determine whether the runtime container has enough resource history for a sparkline
    /// </summary>
    /// <param name="container">Runtime container list item</param>
    /// <returns>True when a sparkline can be shown</returns>
    private static bool HasResourceHistory(RuntimeContainerListItemData container)
    {
        ArgumentNullException.ThrowIfNull(container);

        return container.ResourceUsageHistory.Count > 1;
    }

    /// <summary>
    /// Build CPU sparkline points
    /// </summary>
    /// <param name="container">Runtime container list item</param>
    /// <returns>SVG polyline points</returns>
    private static string GetCpuSparklinePoints(RuntimeContainerListItemData container)
    {
        ArgumentNullException.ThrowIfNull(container);

        return BuildSparklinePoints(GetChronologicalHistory(container.ResourceUsageHistory).Select(point => (double)point.CpuPercent));
    }

    /// <summary>
    /// Build memory sparkline points
    /// </summary>
    /// <param name="container">Runtime container list item</param>
    /// <returns>SVG polyline points</returns>
    private static string GetMemorySparklinePoints(RuntimeContainerListItemData container)
    {
        ArgumentNullException.ThrowIfNull(container);

        return BuildSparklinePoints(GetChronologicalHistory(container.ResourceUsageHistory).Select(point => (double)point.MemoryUsageBytes));
    }

    /// <summary>
    /// Build network receive sparkline points
    /// </summary>
    /// <param name="container">Runtime container list item</param>
    /// <returns>SVG polyline points</returns>
    private static string GetNetworkReceiveSparklinePoints(RuntimeContainerListItemData container)
    {
        ArgumentNullException.ThrowIfNull(container);

        return BuildSparklinePoints(GetChronologicalHistory(container.ResourceUsageHistory).Select(point => (double)point.NetworkRxBytesPerSecond));
    }

    /// <summary>
    /// Build network transmit sparkline points
    /// </summary>
    /// <param name="container">Runtime container list item</param>
    /// <returns>SVG polyline points</returns>
    private static string GetNetworkTransmitSparklinePoints(RuntimeContainerListItemData container)
    {
        ArgumentNullException.ThrowIfNull(container);

        return BuildSparklinePoints(GetChronologicalHistory(container.ResourceUsageHistory).Select(point => (double)point.NetworkTxBytesPerSecond));
    }

    /// <summary>
    /// Order resource history from oldest to newest
    /// </summary>
    /// <param name="history">Resource history</param>
    /// <returns>Chronological history</returns>
    private static IReadOnlyList<ResourceUsagePointViewData> GetChronologicalHistory(IReadOnlyList<ResourceUsagePointViewData> history)
    {
        ArgumentNullException.ThrowIfNull(history);

        return history.OrderBy(point => point.RecordedAtUtc)
                      .ToList();
    }

    /// <summary>
    /// Build sparkline points for an SVG polyline
    /// </summary>
    /// <param name="values">Series values</param>
    /// <returns>SVG polyline points</returns>
    private static string BuildSparklinePoints(IEnumerable<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var valueArray = values.ToArray();

        if (valueArray.Length == 0)
        {
            return string.Empty;
        }

        if (valueArray.Length == 1)
        {
            var singleY = FormatSvgCoordinate(SparklineHeight / 2d);

            return $"0,{singleY} {FormatSvgCoordinate(SparklineWidth)},{singleY}";
        }

        var minimum = valueArray.Min();
        var maximum = valueArray.Max();
        var range = maximum - minimum;

        if (range <= 0d)
        {
            range = 1d;
        }

        var usableWidth = SparklineWidth - (SparklinePadding * 2d);
        var usableHeight = SparklineHeight - (SparklinePadding * 2d);
        var points = valueArray.Select((value, index) =>
                                       {
                                           var x = SparklinePadding + (usableWidth * index / (valueArray.Length - 1d));
                                           var normalizedValue = (value - minimum) / range;
                                           var y = SparklinePadding + (usableHeight * (1d - normalizedValue));

                                           return $"{FormatSvgCoordinate(x)},{FormatSvgCoordinate(y)}";
                                       });

        return string.Join(' ', points);
    }

    /// <summary>
    /// Format an SVG coordinate using invariant culture
    /// </summary>
    /// <param name="value">Coordinate value</param>
    /// <returns>Formatted coordinate</returns>
    private static string FormatSvgCoordinate(double value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    #endregion // Static methods

    #region Methods

    /// <inheritdoc/>
    protected override async Task OnInitializedAsync()
    {
        var containers = await ViewService.GetRuntimeContainersAsync()
                                          .ConfigureAwait(false);

        await InvokeAsync(() =>
                          {
                              _containers = containers;
                          }).ConfigureAwait(false);
    }

    #endregion // Methods
}