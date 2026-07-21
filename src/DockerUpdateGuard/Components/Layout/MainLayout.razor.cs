using DockerUpdateGuard.UI;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

using MudBlazor;

namespace DockerUpdateGuard.Components.Layout;

/// <summary>
/// Main application layout
/// </summary>
public sealed partial class MainLayout : LayoutComponentBase, IDisposable
{
    #region Constants

    /// <summary>
    /// Persistent-state key for the prerendered dashboard summary
    /// </summary>
    private const string DashboardSummaryStateKey = "MainLayout.DashboardSummary";

    /// <summary>
    /// Browser-storage key for the persisted dark-mode preference
    /// </summary>
    private const string DarkModeStorageKey = "dug-dark-mode";

    #endregion // Constants

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
                                           PaletteDark = new PaletteDark
                                                         {
                                                             Primary = "#3b82f6",
                                                             Secondary = "#22d3ee",
                                                             Info = "#38bdf8",
                                                             Success = "#4ade80",
                                                             Warning = "#fbbf24",
                                                             Error = "#f87171",
                                                             Background = "#0b1220",
                                                             Surface = "#111a2e",
                                                             AppbarBackground = "rgba(15, 23, 42, 0.82)",
                                                             DrawerBackground = "#0b1220",
                                                             DrawerText = "#e2e8f0",
                                                             TextPrimary = "#e2e8f0",
                                                             TextSecondary = "#94a3b8",
                                                         },
                                       };

    /// <summary>
    /// Persisting-state subscription used to hand the prerendered summary to the interactive render
    /// </summary>
    private PersistingComponentStateSubscription _persistingSubscription;

    /// <summary>
    /// Dashboard summary
    /// </summary>
    private DashboardSummaryViewData? _dashboardSummary;

    /// <summary>
    /// Navigation-drawer state
    /// </summary>
    private bool _drawerOpen = true;

    /// <summary>
    /// Dark-mode state
    /// </summary>
    private bool _isDarkMode;

    /// <summary>
    /// Theme provider instance, used to detect the system dark-mode preference
    /// </summary>
    private MudThemeProvider _themeProvider = null!;

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

    /// <summary>
    /// Persistent component state used to reuse the prerendered summary on the interactive render
    /// </summary>
    [Inject]
    public PersistentComponentState PersistentState { get; set; } = null!;

    /// <summary>
    /// Protected browser local storage used to persist the dark-mode preference
    /// </summary>
    [Inject]
    public ProtectedLocalStorage BrowserStorage { get; set; } = null!;

    #endregion // Properties

    #region Methods

    /// <summary>
    /// Load the current dashboard summary
    /// </summary>
    /// <returns>Task</returns>
    private async Task LoadSummaryAsync()
    {
        var dashboardSummary = await ViewService.GetDashboardSummaryAsync()
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
    /// Persist the current dashboard summary so the interactive render can reuse the prerendered data
    /// </summary>
    /// <returns>Task</returns>
    private Task PersistSummary()
    {
        if (_dashboardSummary is not null)
        {
            PersistentState.PersistAsJson(DashboardSummaryStateKey, _dashboardSummary);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Toggle the navigation drawer state
    /// </summary>
    private void ToggleDrawer()
    {
        _drawerOpen = _drawerOpen == false;
    }

    /// <summary>
    /// Get the CSS class for the layout root, reflecting the current dark-mode state
    /// </summary>
    /// <returns>Layout root CSS class</returns>
    private string GetLayoutClass()
    {
        return _isDarkMode ? "app-layout dug-dark" : "app-layout";
    }

    /// <summary>
    /// Toggle the dark-mode state and persist the new preference
    /// </summary>
    /// <returns>Task</returns>
    private async Task ToggleDarkModeAsync()
    {
        _isDarkMode = _isDarkMode == false;

        await SetStoredDarkModePreferenceAsync(_isDarkMode).ConfigureAwait(false);
    }

    /// <summary>
    /// Read the persisted dark-mode preference from browser local storage
    /// </summary>
    /// <returns>Persisted preference, or null when not yet stored or the storage cannot be reached</returns>
    private async Task<bool?> GetStoredDarkModePreferenceAsync()
    {
        try
        {
            var result = await BrowserStorage.GetAsync<bool>(DarkModeStorageKey)
                                             .ConfigureAwait(false);

            return result.Success ? result.Value : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Persist the dark-mode preference to browser local storage
    /// </summary>
    /// <param name="isDarkMode">Dark-mode preference to persist</param>
    /// <returns>Task</returns>
    private async Task SetStoredDarkModePreferenceAsync(bool isDarkMode)
    {
        try
        {
            await BrowserStorage.SetAsync(DarkModeStorageKey, isDarkMode)
                                .ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // Storage cannot be reached before the interactive circuit is attached; the preference is re-derived on the next render.
        }
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
        if (_dashboardSummary is null || _dashboardSummary.LatestScan is null)
        {
            return "No scans yet";
        }

        return _dashboardSummary.LatestScan
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
        _persistingSubscription = PersistentState.RegisterOnPersisting(PersistSummary);

        if (PersistentState.TryTakeFromJson<DashboardSummaryViewData>(DashboardSummaryStateKey, out var restoredSummary)
            && restoredSummary is not null)
        {
            _dashboardSummary = restoredSummary;

            return;
        }

        await LoadSummaryAsync().ConfigureAwait(false);
    }

    /// <inheritdoc/>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender == false)
        {
            return;
        }

        var storedPreference = await GetStoredDarkModePreferenceAsync().ConfigureAwait(false);

        _isDarkMode = storedPreference ?? await _themeProvider.GetSystemDarkModeAsync().ConfigureAwait(false);

        StateHasChanged();
    }

    #endregion // ComponentBase

    #region IDisposable

    /// <inheritdoc/>
    public void Dispose()
    {
        NavigationManager.LocationChanged -= OnLocationChanged;
        DashboardRefreshState.Changed -= OnDashboardRefreshRequested;
        _persistingSubscription.Dispose();
    }

    #endregion // IDisposable
}