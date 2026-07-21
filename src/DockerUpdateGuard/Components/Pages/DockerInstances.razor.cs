using DockerUpdateGuard.UI;

using Microsoft.AspNetCore.Components;

namespace DockerUpdateGuard.Components.Pages;

/// <summary>
/// Docker instances page
/// </summary>
public sealed partial class DockerInstances : IDisposable
{
    #region Constants

    /// <summary>
    /// Persistent-state key for the prerendered Docker-instance list
    /// </summary>
    private const string DockerInstancesStateKey = "DockerInstances.List";

    /// <summary>
    /// Persistent-state key for the prerendered single Docker-instance detail
    /// </summary>
    private const string SingleInstanceDetailStateKey = "DockerInstances.SingleDetail";

    #endregion // Constants

    #region Fields

    /// <summary>
    /// Persisting-state subscription used to hand the prerendered data to the interactive render
    /// </summary>
    private PersistingComponentStateSubscription _persistingSubscription;

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

    /// <summary>
    /// Persistent component state used to reuse the prerendered data on the interactive render
    /// </summary>
    [Inject]
    public PersistentComponentState PersistentState { get; set; } = null!;

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

    #region Methods

    /// <summary>
    /// Load the Docker-instance list and, for a single configured instance, its detail
    /// </summary>
    /// <returns>Task</returns>
    private async Task LoadAsync()
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

    /// <summary>
    /// Persist the current instance data so the interactive render can reuse the prerendered data
    /// </summary>
    /// <returns>Task</returns>
    private Task PersistInstances()
    {
        if (_instances is not null)
        {
            PersistentState.PersistAsJson(DockerInstancesStateKey, _instances);
        }

        if (_singleInstanceDetail is not null)
        {
            PersistentState.PersistAsJson(SingleInstanceDetailStateKey, _singleInstanceDetail);
        }

        return Task.CompletedTask;
    }

    #endregion // Methods

    #region ComponentBase

    /// <inheritdoc/>
    protected override async Task OnInitializedAsync()
    {
        _persistingSubscription = PersistentState.RegisterOnPersisting(PersistInstances);

        if (PersistentState.TryTakeFromJson<IReadOnlyList<DockerInstanceListItemData>>(DockerInstancesStateKey, out var restoredInstances)
            && restoredInstances is not null)
        {
            _instances = restoredInstances;

            if (PersistentState.TryTakeFromJson<DockerInstanceDetailViewData>(SingleInstanceDetailStateKey, out var restoredDetail)
                && restoredDetail is not null)
            {
                _singleInstanceDetail = restoredDetail;
            }

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