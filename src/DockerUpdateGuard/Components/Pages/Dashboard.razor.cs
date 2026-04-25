using DockerUpdateGuard.UI;

using Microsoft.AspNetCore.Components;

using MudBlazor;

namespace DockerUpdateGuard.Components.Pages;

/// <summary>
/// Dashboard page
/// </summary>
public partial class Dashboard
{
    #region Fields

    private DashboardViewData? _dashboard;

    #endregion // Fields

    #region Properties

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
        var dashboard = await ViewService.GetDashboardAsync()
                                         .ConfigureAwait(false);

        await InvokeAsync(() =>
                          {
                              _dashboard = dashboard;
                          }).ConfigureAwait(false);
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
                   "COMPLETED" => Color.Success,
                   "FAILED" => Color.Error,
                   "RUNNING" => Color.Info,
                   "PENDING" => Color.Warning,
                   _ => Color.Default,
               };
    }

    #endregion // Methods
}