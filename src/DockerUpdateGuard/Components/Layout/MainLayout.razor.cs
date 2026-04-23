using Microsoft.AspNetCore.Components;

using MudBlazor;

namespace DockerUpdateGuard.Components.Layout;

/// <summary>
/// Main application layout
/// </summary>
public partial class MainLayout : LayoutComponentBase
{
    #region Fields

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
    private bool _drawerOpen = true;

    #endregion // Fields

    #region Methods

    /// <summary>
    /// Toggle the navigation drawer state
    /// </summary>
    private void ToggleDrawer()
    {
        _drawerOpen = _drawerOpen == false;
    }

    #endregion // Methods
}