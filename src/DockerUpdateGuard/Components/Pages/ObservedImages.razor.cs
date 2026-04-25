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

    private readonly ObservedImageRegistrationRequest _request = new();

    private string? _errorMessage;
    private bool _isBusy;
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

    #endregion // Properties

    #region Methods

    /// <inheritdoc/>
    protected override async Task OnInitializedAsync()
    {
        await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Load the observed image list
    /// </summary>
    /// <returns>Task</returns>
    private async Task LoadAsync()
    {
        _images = await ViewService.GetObservedImagesAsync()
                                   .ConfigureAwait(true);
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
                                                              .ConfigureAwait(true);

            await ImageScanOrchestrator.ScanAsync(observedImage.Id, ScanTriggerSource.Manual)
                                       .ConfigureAwait(true);

            NavigationManager.NavigateTo($"observed-images/{observedImage.Id}");
        }
        catch (Exception exception)
        {
            _errorMessage = exception.Message;

            await LoadAsync().ConfigureAwait(true);
        }
        finally
        {
            _isBusy = false;
        }
    }

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

    #endregion // Methods
}