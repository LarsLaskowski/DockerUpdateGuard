using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Images.Data;
using DockerUpdateGuard.Images.Interfaces;
using DockerUpdateGuard.UI;

using Microsoft.AspNetCore.Components;

using MudBlazor;

namespace DockerUpdateGuard.Components.Pages;

/// <summary>
/// Observed images page
/// </summary>
public sealed partial class ObservedImages : IDisposable
{
    #region Constants

    /// <summary>
    /// Persistent-state key for the prerendered manual observed-image list
    /// </summary>
    private const string ObservedImagesStateKey = "ObservedImages.List";

    #endregion // Constants

    #region Fields

    /// <summary>
    /// Observed-image registration request
    /// </summary>
    private readonly ObservedImageRegistrationRequest _request = new();

    /// <summary>
    /// Persisting-state subscription used to hand the prerendered data to the interactive render
    /// </summary>
    private PersistingComponentStateSubscription _persistingSubscription;

    /// <summary>
    /// Current error message
    /// </summary>
    private string? _errorMessage;

    /// <summary>
    /// Busy-state flag
    /// </summary>
    private bool _isBusy;

    /// <summary>
    /// Observed-image list
    /// </summary>
    private IReadOnlyList<ObservedImageListItemData>? _images;

    #endregion // Fields

    #region Properties

    /// <summary>
    /// Image registration service
    /// </summary>
    [Inject]
    public IImageRegistrationService ImageRegistrationService { get; set; } = null!;

    /// <summary>
    /// Image scan orchestrator
    /// </summary>
    [Inject]
    public IImageScanOrchestrator ImageScanOrchestrator { get; set; } = null!;

    /// <summary>
    /// Navigation manager
    /// </summary>
    [Inject]
    public NavigationManager NavigationManager { get; set; } = null!;

    /// <summary>
    /// Application view service
    /// </summary>
    [Inject]
    public IApplicationViewService ViewService { get; set; } = null!;

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
    /// Resolve the chip color for a scan status
    /// </summary>
    /// <param name="status">Scan status</param>
    /// <returns>Chip color</returns>
    private static Color GetScanStatusColor(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return Color.Default;
        }

        return status.ToUpperInvariant() switch
               {
                   "COMPLETED" => Color.Success,
                   "FAILED" => Color.Error,
                   "RUNNING" => Color.Info,
                   "PENDING" => Color.Warning,
                   _ => Color.Default,
               };
    }

    /// <summary>
    /// Resolve the chip color for a vulnerability assessment status
    /// </summary>
    /// <param name="status">Vulnerability assessment status</param>
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

    #endregion // Static methods

    #region Methods

    /// <summary>
    /// Load the manual image list
    /// </summary>
    /// <returns>Task</returns>
    private async Task LoadAsync()
    {
        var images = await ViewService.GetManualObservedImagesAsync()
                                      .ConfigureAwait(false);

        await InvokeAsync(() =>
                          {
                              _images = images;
                          }).ConfigureAwait(false);
    }

    /// <summary>
    /// Persist the current image list so the interactive render can reuse the prerendered data
    /// </summary>
    /// <returns>Task</returns>
    private Task PersistImages()
    {
        if (_images is not null)
        {
            PersistentState.PersistAsJson(ObservedImagesStateKey, _images);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Register a new observed image and start a scan
    /// </summary>
    /// <returns>Task</returns>
    private async Task RegisterAsync()
    {
        _isBusy = true;
        _errorMessage = null;

        try
        {
            var observedImage = await ImageRegistrationService.RegisterAsync(_request)
                                                              .ConfigureAwait(false);

            await ImageScanOrchestrator.ScanAsync(observedImage.Id, ScanTriggerSource.Manual)
                                       .ConfigureAwait(false);

            DashboardRefreshState.NotifyChanged();

            await InvokeAsync(() =>
                              {
                                  NavigationManager.NavigateTo($"observed-images/{observedImage.Id}");
                              }).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await InvokeAsync(() =>
                              {
                                  _errorMessage = exception.Message;
                              }).ConfigureAwait(false);

            await LoadAsync().ConfigureAwait(false);
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
        _persistingSubscription = PersistentState.RegisterOnPersisting(PersistImages);

        if (PersistentState.TryTakeFromJson<IReadOnlyList<ObservedImageListItemData>>(ObservedImagesStateKey, out var restoredImages)
            && restoredImages is not null)
        {
            _images = restoredImages;

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