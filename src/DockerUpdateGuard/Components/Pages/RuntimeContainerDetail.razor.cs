using DockerUpdateGuard.Components.Common;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Infrastructure;
using DockerUpdateGuard.Portainer.Data;
using DockerUpdateGuard.Portainer.Interfaces;
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
    /// Availability of container actions; null until the interactive render loaded it
    /// </summary>
    private ContainerActionAvailabilityData? _availability;

    /// <summary>
    /// Instance and container whose availability was requested last
    /// </summary>
    private string? _availabilityKey;

    /// <summary>
    /// Action that is executing for the container
    /// </summary>
    private ContainerActionType? _runningAction;

    /// <summary>
    /// Outcome of the last action
    /// </summary>
    private ContainerActionRunResultData? _actionOutcome;

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

    /// <summary>
    /// Indicates whether the last load found no record for the route identifier
    /// </summary>
    private bool _isNotFound;

    /// <summary>
    /// Message of the last failed load, null when the load did not fail
    /// </summary>
    private string? _loadErrorMessage;

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

    /// <summary>
    /// Container action service
    /// </summary>
    [Inject]
    public IContainerActionService ContainerActionService { get; set; } = null!;

    /// <summary>
    /// Dialog service
    /// </summary>
    [Inject]
    public IDialogService DialogService { get; set; } = null!;

    /// <summary>
    /// Logger
    /// </summary>
    [Inject]
    public ILogger<RuntimeContainerDetail> Logger { get; set; } = null!;

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
    /// Resolve the chip color for a container action run status
    /// </summary>
    /// <param name="status">Run status</param>
    /// <returns>Chip color</returns>
    private static Color GetActionStatusColor(string status)
    {
        return status switch
               {
                   nameof(ContainerActionStatus.Succeeded) => Color.Success,
                   nameof(ContainerActionStatus.Failed) => Color.Error,
                   nameof(ContainerActionStatus.Running) => Color.Info,
                   nameof(ContainerActionStatus.Requested) => Color.Warning,
                   _ => Color.Default,
               };
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
                              _isNotFound = detail is null;
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
            Logger.UserActionFailed("SaveTagSelection", exception);

            await InvokeAsync(() => _errorMessage = $"Saving the tag selection failed: {FailureReason.FromException(exception, null, CancellationToken.None)}").ConfigureAwait(false);
        }
        finally
        {
            await InvokeAsync(() => _isBusy = false).ConfigureAwait(false);
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
            Logger.UserActionFailed("ClearTagSelection", exception);

            await InvokeAsync(() => _errorMessage = $"Clearing the tag selection failed: {FailureReason.FromException(exception, null, CancellationToken.None)}").ConfigureAwait(false);
        }
        finally
        {
            await InvokeAsync(() => _isBusy = false).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Load the availability of container actions
    /// </summary>
    /// <returns>Task</returns>
    private async Task LoadAvailabilityAsync()
    {
        try
        {
            var availability = await ContainerActionService.GetAvailabilityAsync(DockerInstanceId, ContainerId)
                                                           .ConfigureAwait(false);

            await InvokeAsync(() =>
                              {
                                  _availability = availability;
                                  StateHasChanged();
                              }).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // The page is gone, nothing to show
        }
        catch (Exception exception)
        {
            Logger.UserActionFailed("LoadContainerActions", exception);

            await InvokeAsync(() =>
                              {
                                  _availability = null;
                                  StateHasChanged();
                              }).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Start the confirmation of a container action; the click handler returns once the dialog is open, so the page stays responsive while the dialog waits for the user
    /// </summary>
    /// <param name="actionType">Action type</param>
    private void StartAction(ContainerActionType actionType)
    {
        _ = ConfirmAndExecuteAsync(actionType);
    }

    /// <summary>
    /// Ask for confirmation of a container action and execute it when confirmed
    /// </summary>
    /// <param name="actionType">Action type</param>
    /// <returns>Task</returns>
    private async Task ConfirmAndExecuteAsync(ContainerActionType actionType)
    {
        if (_detail is null || _availability is null || _runningAction is not null)
        {
            return;
        }

        try
        {
            await ShowConfirmationAndExecuteAsync(actionType, _detail, _availability).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // The page is gone while the dialog was open
        }
        catch (Exception exception)
        {
            Logger.UserActionFailed("ContainerAction", exception);

            await InvokeAsync(() =>
                              {
                                  _errorMessage = $"Container action failed: {FailureReason.FromException(exception, null, CancellationToken.None)}";
                                  StateHasChanged();
                              }).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Show the confirmation dialog and execute the action when it is confirmed
    /// </summary>
    /// <param name="actionType">Action type</param>
    /// <param name="detail">Detail the dialog names</param>
    /// <param name="availability">Availability the dialog names</param>
    /// <returns>Task</returns>
    private async Task ShowConfirmationAndExecuteAsync(ContainerActionType actionType, RuntimeContainerDetailViewData detail, ContainerActionAvailabilityData availability)
    {
        var parameters = new DialogParameters<ContainerActionConfirmation>
                         {
                             { dialog => dialog.ActionType, actionType },
                             { dialog => dialog.ContainerName, detail.ContainerName },
                             { dialog => dialog.DockerInstanceName, detail.DockerInstanceName },
                             { dialog => dialog.EndpointId, availability.EndpointId ?? string.Empty },
                             { dialog => dialog.IsOwnContainer, availability.IsOwnContainer }
                         };
        var dialogReference = await DialogService.ShowAsync<ContainerActionConfirmation>(null, parameters)
                                                 .ConfigureAwait(false);
        var result = await dialogReference.Result.ConfigureAwait(false);

        if (result is null || result.Canceled)
        {
            return;
        }

        await ExecuteActionAsync(actionType).ConfigureAwait(false);
    }

    /// <summary>
    /// Execute a confirmed container action and show its outcome
    /// </summary>
    /// <param name="actionType">Action type</param>
    /// <returns>Task</returns>
    private async Task ExecuteActionAsync(ContainerActionType actionType)
    {
        await InvokeAsync(() =>
                          {
                              _runningAction = actionType;
                              _actionOutcome = null;
                              _errorMessage = null;
                              StateHasChanged();
                          }).ConfigureAwait(false);

        try
        {
            var outcome = await ContainerActionService.ExecuteAsync(DockerInstanceId, ContainerId, actionType)
                                                      .ConfigureAwait(false);

            await InvokeAsync(() => _actionOutcome = outcome).ConfigureAwait(false);
            await LoadAsync().ConfigureAwait(false);
            await LoadAvailabilityAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // The page is gone, the action's run is recorded by the service
        }
        catch (Exception exception)
        {
            Logger.UserActionFailed("ContainerAction", exception);

            await InvokeAsync(() => _errorMessage = $"Container action failed: {FailureReason.FromException(exception, null, CancellationToken.None)}").ConfigureAwait(false);
        }
        finally
        {
            await InvokeAsync(() =>
                              {
                                  _runningAction = null;
                                  StateHasChanged();
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

        await InvokeAsync(() =>
                          {
                              _isNotFound = false;
                              _loadErrorMessage = null;
                          }).ConfigureAwait(false);

        var loadErrorMessage = await DetailPageLoader.LoadAsync(LoadAsync, Logger, "LoadRuntimeContainerDetail", "Loading the container failed").ConfigureAwait(false);

        await InvokeAsync(() =>
                          {
                              _loadErrorMessage = loadErrorMessage;

                              if (loadErrorMessage is not null)
                              {
                                  _detail = null;
                                  _isNotFound = false;
                              }
                          }).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // The capability check contacts Portainer, so it runs on the interactive render only, never during prerender
        var key = $"{DockerInstanceId}/{ContainerId}";

        if (RendererInfo.IsInteractive && _availabilityKey != key)
        {
            _availabilityKey = key;
            _availability = null;

            await LoadAvailabilityAsync().ConfigureAwait(false);
        }
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