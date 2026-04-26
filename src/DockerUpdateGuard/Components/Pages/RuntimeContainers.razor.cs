using DockerUpdateGuard.UI;

using Microsoft.AspNetCore.Components;

using MudBlazor;

namespace DockerUpdateGuard.Components.Pages;

/// <summary>
/// Runtime containers page
/// </summary>
public partial class RuntimeContainers
{
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