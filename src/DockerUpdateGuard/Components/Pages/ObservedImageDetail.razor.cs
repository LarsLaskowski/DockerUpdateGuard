using DockerUpdateGuard.UI;

using Microsoft.AspNetCore.Components;

using MudBlazor;

namespace DockerUpdateGuard.Components.Pages;

/// <summary>
/// Observed image detail page
/// </summary>
public sealed partial class ObservedImageDetail : IDisposable
{
    #region Constants

    /// <summary>
    /// Persistent-state key for the prerendered observed-image detail
    /// </summary>
    private const string ObservedImageDetailStateKey = "ObservedImageDetail.State";

    #endregion // Constants

    #region Fields

    /// <summary>
    /// Persisting-state subscription used to hand the prerendered data to the interactive render
    /// </summary>
    private PersistingComponentStateSubscription _persistingSubscription;

    /// <summary>
    /// Observed-image detail view data
    /// </summary>
    private ObservedImageDetailViewData? _detail;

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
    /// Observed image identifier from the route
    /// </summary>
    [Parameter]
    public Guid ObservedImageId { get; set; }

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

    /// <summary>
    /// Logger
    /// </summary>
    [Inject]
    public ILogger<ObservedImageDetail> Logger { get; set; } = null!;

    #endregion // Properties

    #region Static methods

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
    /// <param name="isOwnImage">Whether the image is owned</param>
    /// <returns>Chip color</returns>
    private static Color GetBaseRuntimeAlertColor(bool isOwnImage)
    {
        return isOwnImage ? Color.Warning : Color.Info;
    }

    #endregion // Static methods

    #region Methods

    /// <summary>
    /// Load the observed-image detail view model
    /// </summary>
    /// <returns>Task</returns>
    private async Task LoadAsync()
    {
        var detail = await ViewService.GetObservedImageDetailAsync(ObservedImageId)
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
            PersistentState.PersistAsJson(ObservedImageDetailStateKey, _detail);
        }

        return Task.CompletedTask;
    }

    #endregion // Methods

    #region ComponentBase

    /// <inheritdoc/>
    protected override void OnInitialized()
    {
        _persistingSubscription = PersistentState.RegisterOnPersisting(PersistDetail);

        if (PersistentState.TryTakeFromJson<ObservedImageDetailViewData>(ObservedImageDetailStateKey, out var restoredDetail)
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

        var loadErrorMessage = await DetailPageLoader.LoadAsync(LoadAsync, Logger, "LoadObservedImageDetail", "Loading the observed image failed").ConfigureAwait(false);

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

    #endregion // ComponentBase

    #region IDisposable

    /// <inheritdoc/>
    public void Dispose()
    {
        _persistingSubscription.Dispose();
    }

    #endregion // IDisposable
}