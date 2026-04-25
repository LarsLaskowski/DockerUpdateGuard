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
    /// Get the label for the most recent scan
    /// </summary>
    /// <returns>Formatted scan label</returns>
    private string GetLatestScanLabel()
    {
        if (_dashboard is null || _dashboard.RecentScans.Count == 0)
        {
            return "No scans yet";
        }

        return _dashboard.RecentScans[0]
        .StartedAtUtc
        .ToLocalTime()
        .ToString("g");
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