using DockerUpdateGuard.UI;

using Microsoft.AspNetCore.Components;

using MudBlazor;

namespace DockerUpdateGuard.Components.Pages;

/// <summary>
/// Runtime container detail page
/// </summary>
public sealed partial class RuntimeContainerDetail : IDisposable
{
    #region Constants

    /// <summary>
    /// Persistent-state key for the prerendered runtime-container detail
    /// </summary>
    private const string RuntimeContainerDetailStateKey = "RuntimeContainerDetail.State";

    #endregion // Constants

    #region Fields

    /// <summary>
    /// Persisting-state subscription used to hand the prerendered data to the interactive render
    /// </summary>
    private PersistingComponentStateSubscription _persistingSubscription;

    /// <summary>
    /// Runtime-container detail view data
    /// </summary>
    private RuntimeContainerDetailViewData? _detail;

    /// <summary>
    /// Current error message
    /// </summary>
    private string? _errorMessage;

    /// <summary>
    /// Busy-state flag
    /// </summary>
    private bool _isBusy;

    /// <summary>
    /// Indicates whether the current detail was restored from persistent state
    /// </summary>
    private bool _restoredFromState;

    #endregion // Fields

    #region Properties

    /// <summary>
    /// Docker instance identifier from the route
    /// </summary>
    [Parameter]
    public Guid DockerInstanceId { get; set; }

    /// <summary>
    /// Container identifier from the route
    /// </summary>
    [Parameter]
    public string ContainerId { get; set; } = string.Empty;

    /// <summary>
    /// Application view service
    /// </summary>
    [Inject]
    public IApplicationViewService ViewService { get; set; } = null!;

    /// <summary>
    /// Manual tag selection service
    /// </summary>
    [Inject]
    public IRuntimeContainerTagSelectionService TagSelectionService { get; set; } = null!;

    /// <summary>
    /// Persistent component state used to reuse the prerendered data on the interactive render
    /// </summary>
    [Inject]
    public PersistentComponentState PersistentState { get; set; } = null!;

    #endregion // Properties

    #region Static methods

    /// <summary>
    /// Resolve the chip color for an update status
    /// </summary>
    /// <param name="status">Update status</param>
    /// <returns>Chip color</returns>
    private static Color GetUpdateStateColor(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return Color.Default;
        }

        return status.ToUpperInvariant() switch
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
    /// Resolve the chip color for a scan status
    /// </summary>
    /// <param name="status">Scan status</param>
    /// <returns>Chip color</returns>
    private static Color GetScanStatusColor(string status)
    {
        return status.ToUpperInvariant() switch
               {
                   "SUCCEEDED" => Color.Success,
                   "COMPLETED" => Color.Success,
                   "PARTIAL" => Color.Warning,
                   "FAILED" => Color.Error,
                   "RUNNING" => Color.Info,
                   "PENDING" => Color.Warning,
                   _ => Color.Default,
               };
    }

    /// <summary>
    /// Resolve the chip color for a base-runtime alert
    /// </summary>
    /// <param name="hasLinkedOwnImage">Whether the container is linked to an own image</param>
    /// <returns>Chip color</returns>
    private static Color GetBaseRuntimeAlertColor(bool hasLinkedOwnImage)
    {
        return hasLinkedOwnImage ? Color.Warning : Color.Info;
    }

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
    /// Load the detail view model
    /// </summary>
    /// <returns>Task</returns>
    private async Task LoadAsync()
    {
        var detail = await ViewService.GetRuntimeContainerDetailAsync(DockerInstanceId, ContainerId)
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
            PersistentState.PersistAsJson(RuntimeContainerDetailStateKey, _detail);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Save a manual tag selection
    /// </summary>
    /// <param name="tag">Selected tag</param>
    /// <param name="digest">Selected digest</param>
    /// <returns>Task</returns>
    private async Task SaveSelectionAsync(string tag, string? digest)
    {
        await InvokeAsync(() =>
                          {
                              _isBusy = true;
                              _errorMessage = null;
                          }).ConfigureAwait(false);

        try
        {
            await TagSelectionService.SaveSelectionAsync(DockerInstanceId, ContainerId, tag, digest)
                                     .ConfigureAwait(false);
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

    /// <summary>
    /// Clear the manual tag selection
    /// </summary>
    /// <returns>Task</returns>
    private async Task ClearSelectionAsync()
    {
        await InvokeAsync(() =>
                          {
                              _isBusy = true;
                              _errorMessage = null;
                          }).ConfigureAwait(false);

        try
        {
            await TagSelectionService.ClearSelectionAsync(DockerInstanceId, ContainerId)
                                     .ConfigureAwait(false);
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
    protected override void OnInitialized()
    {
        _persistingSubscription = PersistentState.RegisterOnPersisting(PersistDetail);

        if (PersistentState.TryTakeFromJson<RuntimeContainerDetailViewData>(RuntimeContainerDetailStateKey, out var restoredDetail)
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