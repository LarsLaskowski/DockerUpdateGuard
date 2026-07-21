using DockerUpdateGuard.UI;

using Microsoft.AspNetCore.Components;

namespace DockerUpdateGuard.Components.Pages;

/// <summary>
/// Docker instance detail page
/// </summary>
public sealed partial class DockerInstanceDetail : IDisposable
{
    #region Constants

    /// <summary>
    /// Persistent-state key for the prerendered Docker-instance detail
    /// </summary>
    private const string DockerInstanceDetailStateKey = "DockerInstanceDetail.State";

    #endregion // Constants

    #region Fields

    /// <summary>
    /// Persisting-state subscription used to hand the prerendered data to the interactive render
    /// </summary>
    private PersistingComponentStateSubscription _persistingSubscription;

    /// <summary>
    /// Docker-instance detail view data
    /// </summary>
    private DockerInstanceDetailViewData? _detail;

    /// <summary>
    /// Indicates whether the current detail was restored from persistent state
    /// </summary>
    private bool _restoredFromState;

    #endregion // Fields

    #region Properties

    /// <summary>
    /// Docker instance identifier
    /// </summary>
    [Parameter]
    public Guid DockerInstanceId { get; set; }

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
    /// Load the Docker-instance detail view model
    /// </summary>
    /// <returns>Task</returns>
    private async Task LoadAsync()
    {
        var detail = await ViewService.GetDockerInstanceDetailAsync(DockerInstanceId)
                                      .ConfigureAwait(false);

        await InvokeAsync(() =>
                          {
                              _detail = detail;
                          }).ConfigureAwait(false);
    }

    /// <summary>
    /// Persist the current detail so the interactive render can reuse the prerendered data
    /// </summary>
    /// <returns>Task</returns>
    private Task PersistDetail()
    {
        if (_detail is not null)
        {
            PersistentState.PersistAsJson(DockerInstanceDetailStateKey, _detail);
        }

        return Task.CompletedTask;
    }

    #endregion // Methods

    #region ComponentBase

    /// <inheritdoc/>
    protected override void OnInitialized()
    {
        _persistingSubscription = PersistentState.RegisterOnPersisting(PersistDetail);

        if (PersistentState.TryTakeFromJson<DockerInstanceDetailViewData>(DockerInstanceDetailStateKey, out var restoredDetail)
            && restoredDetail is not null)
        {
            _detail = restoredDetail;
            _restoredFromState = true;
        }
    }

    /// <inheritdoc/>
    protected override async Task OnParametersSetAsync()
    {
        if (_restoredFromState)
        {
            _restoredFromState = false;

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