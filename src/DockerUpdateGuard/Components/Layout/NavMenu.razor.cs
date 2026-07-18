using System.Reflection;

using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.UI;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;

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

    /// <summary>
    /// Whether the My Images navigation entry should be shown
    /// </summary>
    private bool _showMyImages;

    /// <summary>
    /// Whether the Base Images navigation entry should be shown
    /// </summary>
    private bool _showBaseImages;

    #endregion // Fields

    #region Properties

    /// <summary>
    /// Application configuration
    /// </summary>
    [Inject]
    public IConfiguration Configuration { get; set; } = null!;

    /// <summary>
    /// Application options
    /// </summary>
    [Inject]
    public IOptions<DockerUpdateGuardOptions> AppOptions { get; set; } = null!;

    /// <summary>
    /// Application view service
    /// </summary>
    [Inject]
    public IApplicationViewService ViewService { get; set; } = null!;

    #endregion // Properties

    #region Static methods

    /// <summary>
    /// Determine whether Docker Hub account discovery is configured for the given options
    /// </summary>
    /// <param name="dockerHub">Docker Hub options to evaluate</param>
    /// <returns>True when both UserName and Pat are non-empty</returns>
    private static bool IsDockerHubAccountConfigured(DockerHubOptions dockerHub)
    {
        return string.IsNullOrWhiteSpace(dockerHub.UserName) == false
               && string.IsNullOrWhiteSpace(dockerHub.Pat) == false;
    }

    #endregion // Static methods

    #region Methods

    /// <summary>
    /// Determine whether Docker Hub account discovery is configured
    /// </summary>
    /// <returns>True when both UserName and Pat are non-empty</returns>
    private bool IsDockerHubAccountConfigured()
    {
        return IsDockerHubAccountConfigured(AppOptions.Value.DockerHub);
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

    #region ComponentBase

    /// <inheritdoc/>
    protected override async Task OnInitializedAsync()
    {
        _displayVersion = ResolveDisplayVersion();
        _showMyImages = IsDockerHubAccountConfigured();
        _showBaseImages = await ViewService.HasBaseImagesAsync().ConfigureAwait(false);
    }

    #endregion // ComponentBase
}