using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Images;
using DockerUpdateGuard.UI;

using Microsoft.AspNetCore.Components;

using MudBlazor;

namespace DockerUpdateGuard.Components.Pages;

/// <summary>
/// Observed images page
/// </summary>
public partial class ObservedImages
{
    #region Fields

    /// <summary>
    /// Observed-image registration request
    /// </summary>
    private readonly ObservedImageRegistrationRequest _request = new();

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

    /// <inheritdoc/>
    protected override async Task OnInitializedAsync()
    {
        await LoadAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Load the observed image list
    /// </summary>
    /// <returns>Task</returns>
    private async Task LoadAsync()
    {
        var images = await ViewService.GetObservedImagesAsync()
                                      .ConfigureAwait(false);

        await InvokeAsync(() =>
                          {
                              _images = images;
                          }).ConfigureAwait(false);
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

    /// <summary>
    /// Read account-discovered own images
    /// </summary>
    /// <returns>Own image list</returns>
    private IReadOnlyList<ObservedImageListItemData> GetOwnImages()
    {
        return _images?.Where(entity => entity.IsOwnImage)
                      .ToList() ?? [];
    }

    /// <summary>
    /// Read manually registered images
    /// </summary>
    /// <returns>Manual image list</returns>
    private IReadOnlyList<ObservedImageListItemData> GetManualImages()
    {
        return _images?.Where(entity => entity.IsOwnImage == false)
                      .ToList() ?? [];
    }

    #endregion // Methods
}