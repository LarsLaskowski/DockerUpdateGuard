using DockerUpdateGuard.UI;

using Microsoft.AspNetCore.Components;

namespace DockerUpdateGuard.Components.Pages;

/// <summary>
/// Docker instances page
/// </summary>
public partial class DockerInstances
{
    #region Fields

    /// <summary>
    /// Docker-instance list
    /// </summary>
    private IReadOnlyList<DockerInstanceListItemData>? _instances;

    /// <summary>
    /// Single Docker-instance detail view data
    /// </summary>
    private DockerInstanceDetailViewData? _singleInstanceDetail;

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
    /// Format CPU usage
    /// </summary>
    /// <param name="usage">Usage data</param>
    /// <returns>Formatted value</returns>
    private static string FormatCpu(ResourceUsagePointViewData? usage)
    {
        return usage is null ? "n/a" : ResourceUsageFormatter.FormatCpuPercent(usage.CpuPercent);
    }

    /// <summary>
    /// Format memory usage
    /// </summary>
    /// <param name="usage">Usage data</param>
    /// <returns>Formatted value</returns>
    private static string FormatMemory(ResourceUsagePointViewData? usage)
    {
        return usage is null ? "n/a" : ResourceUsageFormatter.FormatMemory(usage.MemoryUsageBytes, usage.MemoryLimitBytes);
    }

    /// <summary>
    /// Format network usage
    /// </summary>
    /// <param name="usage">Usage data</param>
    /// <returns>Formatted value</returns>
    private static string FormatNetwork(ResourceUsagePointViewData? usage)
    {
        return usage is null
                   ? "n/a"
                   : $"{ResourceUsageFormatter.FormatBytesPerSecond(usage.NetworkRxBytesPerSecond)} ↓ / {ResourceUsageFormatter.FormatBytesPerSecond(usage.NetworkTxBytesPerSecond)} ↑";
    }

    #endregion // Static methods

    #region ComponentBase

    /// <inheritdoc/>
    protected override async Task OnInitializedAsync()
    {
        var instances = await ViewService.GetDockerInstancesAsync()
                                         .ConfigureAwait(false);
        var singleInstanceDetail = instances.Count == 1
                                       ? await ViewService.GetDockerInstanceDetailAsync(instances[0].Id)
                                                          .ConfigureAwait(false)
                                       : null;

        await InvokeAsync(() =>
                          {
                              _instances = instances;
                              _singleInstanceDetail = singleInstanceDetail;
                          }).ConfigureAwait(false);
    }

    #endregion // ComponentBase
}