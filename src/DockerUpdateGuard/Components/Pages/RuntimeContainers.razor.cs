using System.Globalization;
using System.Text;

using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Images.Interfaces;
using DockerUpdateGuard.UI;

using Microsoft.AspNetCore.Components;

using MudBlazor;

namespace DockerUpdateGuard.Components.Pages;

/// <summary>
/// Runtime containers page
/// </summary>
public sealed partial class RuntimeContainers : IDisposable
{
    #region Const fields

    /// <summary>
    /// Persistent-state key for the prerendered runtime-container list
    /// </summary>
    private const string RuntimeContainersStateKey = "RuntimeContainers.List";

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
    /// Persisting-state subscription used to hand the prerendered data to the interactive render
    /// </summary>
    private PersistingComponentStateSubscription _persistingSubscription;

    /// <summary>
    /// Runtime-container list
    /// </summary>
    private IReadOnlyList<RuntimeContainerListItemData>? _containers;

    /// <summary>
    /// Current error message
    /// </summary>
    private string? _errorMessage;

    /// <summary>
    /// Busy-state flag
    /// </summary>
    private bool _isBusy;

    #endregion // Fields

    #region Properties

    /// <summary>
    /// Application view service
    /// </summary>
    [Inject]
    public IApplicationViewService ViewService { get; set; } = null!;

    /// <summary>
    /// Runtime-container scan orchestrator
    /// </summary>
    [Inject]
    public IRuntimeContainerScanOrchestrator RuntimeContainerScanOrchestrator { get; set; } = null!;

    /// <summary>
    /// Dashboard refresh state
    /// </summary>
    [Inject]
    public DashboardRefreshState DashboardRefreshState { get; set; } = null!;

    /// <summary>
    /// Persistent component state used to reuse the prerendered data on the interactive render
    /// </summary>
    [Inject]
    public PersistentComponentState PersistentState { get; set; } = null!;

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
    /// <param name="severitySummary">Severity summary of the active findings</param>
    /// <returns>Chip color</returns>
    private static Color GetVulnerabilityStatusColor(string? status, VulnerabilitySeveritySummaryViewData? severitySummary)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return Color.Default;
        }

        return VulnerabilityDisplayFormatter.GetStatusColor(status, severitySummary);
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
    /// Build CPU sparkline path
    /// </summary>
    /// <param name="container">Runtime container list item</param>
    /// <returns>SVG path data</returns>
    private static string GetCpuSparklinePath(RuntimeContainerListItemData container)
    {
        ArgumentNullException.ThrowIfNull(container);

        return BuildSparklinePath(GetChronologicalHistory(container.ResourceUsageHistory).Select(point => (double)point.CpuPercent));
    }

    /// <summary>
    /// Build memory sparkline path
    /// </summary>
    /// <param name="container">Runtime container list item</param>
    /// <returns>SVG path data</returns>
    private static string GetMemorySparklinePath(RuntimeContainerListItemData container)
    {
        ArgumentNullException.ThrowIfNull(container);

        return BuildSparklinePath(GetChronologicalHistory(container.ResourceUsageHistory).Select(point => (double)point.MemoryUsageBytes));
    }

    /// <summary>
    /// Build network receive sparkline path
    /// </summary>
    /// <param name="container">Runtime container list item</param>
    /// <returns>SVG path data</returns>
    private static string GetNetworkReceiveSparklinePath(RuntimeContainerListItemData container)
    {
        ArgumentNullException.ThrowIfNull(container);

        return BuildSparklinePath(GetChronologicalHistory(container.ResourceUsageHistory).Select(point => (double)point.NetworkRxBytesPerSecond));
    }

    /// <summary>
    /// Build network transmit sparkline path
    /// </summary>
    /// <param name="container">Runtime container list item</param>
    /// <returns>SVG path data</returns>
    private static string GetNetworkTransmitSparklinePath(RuntimeContainerListItemData container)
    {
        ArgumentNullException.ThrowIfNull(container);

        return BuildSparklinePath(GetChronologicalHistory(container.ResourceUsageHistory).Select(point => (double)point.NetworkTxBytesPerSecond));
    }

    /// <summary>
    /// Order resource history from oldest to newest
    /// </summary>
    /// <param name="history">Resource history</param>
    /// <returns>Chronological history</returns>
    private static List<ResourceUsagePointViewData> GetChronologicalHistory(IReadOnlyList<ResourceUsagePointViewData> history)
    {
        ArgumentNullException.ThrowIfNull(history);

        return history.OrderBy(point => point.RecordedAtUtc)
                      .ToList();
    }

    /// <summary>
    /// Build a smoothed sparkline path for SVG rendering
    /// </summary>
    /// <param name="values">Series values</param>
    /// <returns>SVG path data</returns>
    private static string BuildSparklinePath(IEnumerable<double> values)
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

            return $"M 0,{singleY} L {FormatSvgCoordinate(SparklineWidth)},{singleY}";
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
                                           var xValue = SparklinePadding + (usableWidth * index / (valueArray.Length - 1d));
                                           var normalizedValue = (value - minimum) / range;
                                           var yValue = SparklinePadding + (usableHeight * (1d - normalizedValue));

                                           return (X: xValue, Y: yValue);
                                       })
                               .ToArray();
        var pathBuilder = new StringBuilder();

        pathBuilder.Append("M ");
        pathBuilder.Append(FormatSvgCoordinate(points[0].X));
        pathBuilder.Append(',');
        pathBuilder.Append(FormatSvgCoordinate(points[0].Y));

        for (var index = 0; index < points.Length - 1; index++)
        {
            var previousPoint = index > 0 ? points[index - 1] : points[index];
            var currentPoint = points[index];
            var nextPoint = points[index + 1];
            var followingPoint = index + 2 < points.Length ? points[index + 2] : nextPoint;
            var firstControlPoint = (X: currentPoint.X + ((nextPoint.X - previousPoint.X) / 6d),
                                     Y: currentPoint.Y + ((nextPoint.Y - previousPoint.Y) / 6d));
            var secondControlPoint = (X: nextPoint.X - ((followingPoint.X - currentPoint.X) / 6d),
                                      Y: nextPoint.Y - ((followingPoint.Y - currentPoint.Y) / 6d));

            pathBuilder.Append(" C ");
            pathBuilder.Append(FormatSvgCoordinate(firstControlPoint.X));
            pathBuilder.Append(',');
            pathBuilder.Append(FormatSvgCoordinate(firstControlPoint.Y));
            pathBuilder.Append(' ');
            pathBuilder.Append(FormatSvgCoordinate(secondControlPoint.X));
            pathBuilder.Append(',');
            pathBuilder.Append(FormatSvgCoordinate(secondControlPoint.Y));
            pathBuilder.Append(' ');
            pathBuilder.Append(FormatSvgCoordinate(nextPoint.X));
            pathBuilder.Append(',');
            pathBuilder.Append(FormatSvgCoordinate(nextPoint.Y));
        }

        return pathBuilder.ToString();
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

    /// <summary>
    /// Load the current runtime-container list
    /// </summary>
    /// <returns>Task</returns>
    private async Task LoadAsync()
    {
        var containers = await ViewService.GetRuntimeContainersAsync()
                                          .ConfigureAwait(false);

        await InvokeAsync(() =>
                          {
                              _containers = containers;

                              StateHasChanged();
                          }).ConfigureAwait(false);
    }

    /// <summary>
    /// Persist the current runtime-container list so the interactive render can reuse the prerendered data
    /// </summary>
    /// <returns>Task</returns>
    private Task PersistContainers()
    {
        if (_containers is not null)
        {
            PersistentState.PersistAsJson(RuntimeContainersStateKey, _containers);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Trigger a manual update check for the currently used runtime images
    /// </summary>
    /// <returns>Task</returns>
    private async Task TriggerScanAsync()
    {
        await InvokeAsync(() =>
                          {
                              _isBusy = true;
                              _errorMessage = null;
                          }).ConfigureAwait(false);

        try
        {
            await RuntimeContainerScanOrchestrator.ScanAllAsync(ScanTriggerSource.Manual)
                                                  .ConfigureAwait(false);

            DashboardRefreshState.NotifyChanged();

            await LoadAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await InvokeAsync(() =>
                              {
                                  _errorMessage = exception.Message;
                              }).ConfigureAwait(false);
        }
        finally
        {
            await InvokeAsync(() =>
                              {
                                  _isBusy = false;
                              }).ConfigureAwait(false);
        }
    }

    #endregion // Methods

    #region ComponentBase

    /// <inheritdoc/>
    protected override async Task OnInitializedAsync()
    {
        _persistingSubscription = PersistentState.RegisterOnPersisting(PersistContainers);

        if (PersistentState.TryTakeFromJson<IReadOnlyList<RuntimeContainerListItemData>>(RuntimeContainersStateKey, out var restoredContainers)
            && restoredContainers is not null)
        {
            _containers = restoredContainers;

            return;
        }

        await LoadAsync().ConfigureAwait(false);
    }

    #endregion // ComponentBase

    #region IDisposable

    /// <inheritdoc/>
    public void Dispose()
    {
        _persistingSubscription.Dispose();
    }

    #endregion // IDisposable
}