using DockerUpdateGuard.UI;

using Microsoft.AspNetCore.Components;

using MudBlazor;

namespace DockerUpdateGuard.Components.Pages;

/// <summary>
/// Scan history page
/// </summary>
public partial class ScanHistory
{
    #region Fields

    /// <summary>
    /// Scan-history items
    /// </summary>
    private IReadOnlyList<ScanHistoryItemData>? _scans;

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
    /// Resolve the chip color for a scan status
    /// </summary>
    /// <param name="status">Scan status</param>
    /// <returns>Chip color</returns>
    private static Color GetScanStatusColor(string status)
    {
        return status.ToUpperInvariant() switch
               {
                   "COMPLETED" => Color.Success,
                   "FAILED" => Color.Error,
                   "RUNNING" => Color.Info,
                   "PENDING" => Color.Warning,
                   _ => Color.Default,
               };
    }

    #endregion // Static methods

    #region Methods

    /// <inheritdoc/>
    protected override async Task OnInitializedAsync()
    {
        var scans = await ViewService.GetScanHistoryAsync()
                                     .ConfigureAwait(false);

        await InvokeAsync(() =>
                          {
                              _scans = scans;
                          }).ConfigureAwait(false);
    }

    #endregion // Methods
}