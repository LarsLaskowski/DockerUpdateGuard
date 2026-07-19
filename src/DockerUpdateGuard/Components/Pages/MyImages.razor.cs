using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.UI;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;

using MudBlazor;

namespace DockerUpdateGuard.Components.Pages;

/// <summary>
/// My images page — discovery-owned images from the configured Docker Hub account
/// </summary>
public partial class MyImages
{
    #region Fields

    /// <summary>
    /// Discovery-owned observed-image list
    /// </summary>
    private IReadOnlyList<ObservedImageListItemData>? _images;

    /// <summary>
    /// Configured Docker Hub user name, or null when not set
    /// </summary>
    private string? _dockerHubUserName;

    #endregion // Fields

    #region Properties

    /// <summary>
    /// Application view service
    /// </summary>
    [Inject]
    public IApplicationViewService ViewService { get; set; } = null!;

    /// <summary>
    /// Application options
    /// </summary>
    [Inject]
    public IOptions<DockerUpdateGuardOptions> AppOptions { get; set; } = null!;

    #endregion // Properties

    #region Static methods

    /// <summary>
    /// Build the section heading for the images table, incorporating the Docker Hub user name when available
    /// </summary>
    /// <param name="userName">Configured Docker Hub user name, or null when not set</param>
    /// <returns>Section heading text</returns>
    internal static string GetSectionTitle(string? userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            return "Images from Docker account";
        }

        return $"Images from Docker account ({userName})";
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

    /// <summary>
    /// Resolve the chip color for a vulnerability assessment status
    /// </summary>
    /// <param name="status">Vulnerability assessment status</param>
    /// <param name="severitySummary">Severity summary of the active findings</param>
    /// <returns>Chip color</returns>
    private static Color GetVulnerabilityStatusColor(string? status, VulnerabilitySeveritySummaryViewData? severitySummary)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return Color.Default;
        }

        return VulnerabilityDisplayFormatter.GetStatusColor(status, severitySummary);
    }

    #endregion // Static methods

    #region ComponentBase

    /// <inheritdoc/>
    protected override async Task OnInitializedAsync()
    {
        _dockerHubUserName = AppOptions.Value.DockerHub.UserName;

        var images = await ViewService.GetDiscoveryObservedImagesAsync()
                                      .ConfigureAwait(false);

        await InvokeAsync(() =>
                          {
                              _images = images;
                          }).ConfigureAwait(false);
    }

    #endregion // ComponentBase
}