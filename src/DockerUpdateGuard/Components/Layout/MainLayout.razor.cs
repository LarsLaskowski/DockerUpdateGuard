using DockerUpdateGuard.UI;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

using MudBlazor;

namespace DockerUpdateGuard.Components.Layout;

/// <summary>
/// Main application layout
/// </summary>
public sealed partial class MainLayout : LayoutComponentBase, IDisposable
{
    #region Fields

    /// <summary>
    /// Application theme
    /// </summary>
    private readonly MudTheme _theme = new()
                                       {
                                           LayoutProperties = new LayoutProperties
                                                              {
                                                                  DefaultBorderRadius = "18px",
                                                              },
                                           PaletteLight = new PaletteLight
                                                          {
                                                              Primary = "#2563eb",
                                                              Secondary = "#0891b2",
                                                              Info = "#0284c7",
                                                              Success = "#15803d",
                                                              Warning = "#b45309",
                                                              Error = "#b91c1c",
                                                              Background = "#f3f6fb",
                                                              Surface = "#ffffff",
                                                              AppbarBackground = "rgba(255,255,255,0.82)",
                                                              DrawerBackground = "#0f172a",
                                                              DrawerText = "#e2e8f0",
                                                          },
                                       };

    /// <summary>
    /// Dashboard summary
    /// </summary>
    private DashboardViewData? _dashboardSummary;

    /// <summary>
    /// Navigation-drawer state
    /// </summary>
    private bool _drawerOpen = true;

    #endregion // Fields

    #region Properties

    /// <summary>
    /// Application view service
    /// </summary>
    [Inject]
    public IApplicationViewService ViewService { get; set; } = null!;

    /// <summary>
    /// Navigation manager
    /// </summary>
    [Inject]
    public NavigationManager NavigationManager { get; set; } = null!;

    /// <summary>
    /// Dashboard refresh state
    /// </summary>
    [Inject]
    public DashboardRefreshState DashboardRefreshState { get; set; } = null!;

    #endregion // Properties

    #region Methods

    /// <summary>
    /// Load the current dashboard summary
    /// </summary>
    /// <returns>Task</returns>
    private async Task LoadSummaryAsync()
    {
        var dashboardSummary = await ViewService.GetDashboardAsync()
                                                .ConfigureAwait(false);

        await InvokeAsync(() =>
                          {
                              _dashboardSummary = dashboardSummary;

                              StateHasChanged();
                          }).ConfigureAwait(false);
    }

    /// <summary>
    /// React to application navigation changes
    /// </summary>
    /// <param name="sender">Event sender</param>
    /// <param name="args">Navigation event arguments</param>
    private void OnLocationChanged(object? sender, LocationChangedEventArgs args)
    {
        _ = InvokeAsync(LoadSummaryAsync);
    }

    /// <summary>
    /// React to explicit dashboard refresh notifications
    /// </summary>
    private void OnDashboardRefreshRequested()
    {
        _ = InvokeAsync(LoadSummaryAsync);
    }

    /// <summary>
    /// Toggle the navigation drawer state
    /// </summary>
    private void ToggleDrawer()
    {
        _drawerOpen = _drawerOpen == false;
    }

    /// <summary>
    /// Get the protected asset count
    /// </summary>
    /// <returns>Protected asset count</returns>
    private int GetProtectedAssetCount()
    {
        if (_dashboardSummary is null)
        {
            return 0;
        }

        return _dashboardSummary.ObservedImageCount + _dashboardSummary.MyImageCount + _dashboardSummary.RuntimeContainerCount;
    }

    /// <summary>
    /// Get the label for the most recent scan
    /// </summary>
    /// <returns>Formatted scan label</returns>
    private string GetLatestScanLabel()
    {
        if (_dashboardSummary is null || _dashboardSummary.RecentScans.Count == 0)
        {
            return "No scans yet";
        }

        return _dashboardSummary.RecentScans[0]
        .StartedAtUtc
        .ToLocalTime()
        .ToString("g");
    }

    #endregion // Methods

    #region ComponentBase

    /// <inheritdoc/>
    protected override async Task OnInitializedAsync()
    {
        NavigationManager.LocationChanged += OnLocationChanged;
        DashboardRefreshState.Changed += OnDashboardRefreshRequested;

        await LoadSummaryAsync().ConfigureAwait(false);
    }

    #endregion // ComponentBase

    #region IDisposable

    /// <inheritdoc/>
    public void Dispose()
    {
        NavigationManager.LocationChanged -= OnLocationChanged;
        DashboardRefreshState.Changed -= OnDashboardRefreshRequested;
    }

    #endregion // IDisposable
}