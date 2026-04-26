using System.Reflection;

using Microsoft.AspNetCore.Components;

namespace DockerUpdateGuard.Components.Layout;

/// <summary>
/// Application navigation menu
/// </summary>
public partial class NavMenu
{
    #region Fields

    /// <summary>
    /// Displayed application version
    /// </summary>
    private string _displayVersion = "unknown";

    #endregion // Fields

    #region Properties

    /// <summary>
    /// Application configuration
    /// </summary>
    [Inject]
    public IConfiguration Configuration { get; set; } = null!;

    #endregion // Properties

    #region Methods

    /// <inheritdoc/>
    protected override void OnInitialized()
    {
        _displayVersion = ResolveDisplayVersion();
    }

    /// <summary>
    /// Resolve the version to display in the navigation footer
    /// </summary>
    /// <returns>Display version</returns>
    private string ResolveDisplayVersion()
    {
        var configuredVersion = Configuration["DockerUpdateGuard:DisplayVersion"];

        if (string.IsNullOrWhiteSpace(configuredVersion) == false)
        {
            return configuredVersion;
        }

        var environmentVersion = Environment.GetEnvironmentVariable("DockerUpdateGuard__DisplayVersion");

        if (string.IsNullOrWhiteSpace(environmentVersion) == false)
        {
            return environmentVersion;
        }

        var entryAssembly = Assembly.GetEntryAssembly() ?? typeof(NavMenu).Assembly;
        var informationalVersion = entryAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informationalVersion) == false)
        {
            return informationalVersion;
        }

        var fileVersion = entryAssembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;

        if (string.IsNullOrWhiteSpace(fileVersion) == false)
        {
            return fileVersion;
        }

        var assemblyVersion = entryAssembly.GetName().Version;

        if (assemblyVersion is not null)
        {
            return assemblyVersion.ToString(3);
        }

        return "unknown";
    }

    #endregion // Methods
}