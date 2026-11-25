using System.Reflection;

using Bunit;

using DockerUpdateGuard.Components.Pages;
using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Tests.Data;
using DockerUpdateGuard.Tests.Helper;
using DockerUpdateGuard.UI;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using MudBlazor;

using NSubstitute;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="MyImages"/>
/// </summary>
[TestClass]
public class MyImagesTests
{
    #region Fields

    /// <summary>
    /// Non-public section-title builder
    /// </summary>
    private static readonly MethodInfo _getSectionTitleMethod = typeof(MyImages).GetMethod("GetSectionTitle", BindingFlags.NonPublic | BindingFlags.Static)
                                                                    ?? throw new InvalidOperationException("MyImages must expose the non-public static GetSectionTitle method");

    /// <summary>
    /// Non-public scan-status chip color resolver
    /// </summary>
    private static readonly MethodInfo _getScanStatusColorMethod = typeof(MyImages).GetMethod("GetScanStatusColor", BindingFlags.NonPublic | BindingFlags.Static)
                                                                       ?? throw new InvalidOperationException("MyImages must expose the non-public static GetScanStatusColor method");

    /// <summary>
    /// Non-public vulnerability-status chip color resolver
    /// </summary>
    private static readonly MethodInfo _getVulnerabilityStatusColorMethod = typeof(MyImages).GetMethod("GetVulnerabilityStatusColor", BindingFlags.NonPublic | BindingFlags.Static)
                                                                                ?? throw new InvalidOperationException("MyImages must expose the non-public static GetVulnerabilityStatusColor method");

    #endregion // Fields

    #region Methods

    /// <summary>
    /// Verify the section title includes the Docker Hub user name when one is configured
    /// </summary>
    [TestMethod]
    public void MyImagesGetSectionTitleWithUserNameIncludesUserName()
    {
        var title = (string)_getSectionTitleMethod.Invoke(null, ["myuser"])!;

        Assert.AreEqual("Images from Docker account (myuser)",
                        title,
                        "Section title must include the Docker Hub user name in parentheses");
    }

    /// <summary>
    /// Verify the section title omits parentheses when no user name is configured
    /// </summary>
    /// <param name="userName">Null or blank user name</param>
    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void MyImagesGetSectionTitleWithoutUserNameOmitsParentheses(string? userName)
    {
        var title = (string)_getSectionTitleMethod.Invoke(null, [userName])!;

        Assert.AreEqual("Images from Docker account",
                        title,
                        "Section title must not include parentheses when no user name is set");
    }

    /// <summary>
    /// Verify scan status chip colors map to expected MudBlazor color values
    /// </summary>
    /// <param name="status">Scan status string</param>
    /// <param name="expectedColor">Expected chip color</param>
    [TestMethod]
    [DataRow("Completed", Color.Success)]
    [DataRow("COMPLETED", Color.Success)]
    [DataRow("completed", Color.Success)]
    [DataRow("Failed", Color.Error)]
    [DataRow("FAILED", Color.Error)]
    [DataRow("Running", Color.Info)]
    [DataRow("RUNNING", Color.Info)]
    [DataRow("Pending", Color.Warning)]
    [DataRow("PENDING", Color.Warning)]
    [DataRow("Unknown", Color.Default)]
    [DataRow("anything-else", Color.Default)]
    public void MyImagesGetScanStatusColorKnownStatusReturnsExpectedColor(string status, Color expectedColor)
    {
        var color = (Color)_getScanStatusColorMethod.Invoke(null, [status])!;

        Assert.AreEqual(expectedColor,
                        color,
                        $"Scan status '{status}' must map to Color.{expectedColor}");
    }

    /// <summary>
    /// Verify null or blank scan status resolves to the default chip color
    /// </summary>
    /// <param name="status">Null or blank scan status</param>
    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void MyImagesGetScanStatusColorNullOrBlankReturnsDefault(string? status)
    {
        var color = (Color)_getScanStatusColorMethod.Invoke(null, [status])!;

        Assert.AreEqual(Color.Default,
                        color,
                        "Null or blank scan status must resolve to Color.Default");
    }

    /// <summary>
    /// Verify vulnerability status chip colors map to expected MudBlazor color values
    /// </summary>
    /// <param name="status">Vulnerability assessment status string</param>
    /// <param name="expectedColor">Expected chip color</param>
    [TestMethod]
    [DataRow("Findings Detected", Color.Warning)]
    [DataRow("FINDINGS DETECTED", Color.Warning)]
    [DataRow("No Findings", Color.Success)]
    [DataRow("NO FINDINGS", Color.Success)]
    [DataRow("Failed", Color.Error)]
    [DataRow("FAILED", Color.Error)]
    [DataRow("Not Configured", Color.Default)]
    [DataRow("NOT CONFIGURED", Color.Default)]
    [DataRow("Unsupported", Color.Default)]
    [DataRow("UNSUPPORTED", Color.Default)]
    [DataRow("anything-else", Color.Info)]
    public void MyImagesGetVulnerabilityStatusColorKnownStatusReturnsExpectedColor(string status, Color expectedColor)
    {
        var color = (Color)_getVulnerabilityStatusColorMethod.Invoke(null, [status, null])!;

        Assert.AreEqual(expectedColor,
                        color,
                        $"Vulnerability status '{status}' must map to Color.{expectedColor}");
    }

    /// <summary>
    /// Verify null or blank vulnerability status resolves to the default chip color
    /// </summary>
    /// <param name="status">Null or blank vulnerability status</param>
    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void MyImagesGetVulnerabilityStatusColorNullOrBlankReturnsDefault(string? status)
    {
        var color = (Color)_getVulnerabilityStatusColorMethod.Invoke(null, [status, null])!;

        Assert.AreEqual(Color.Default,
                        color,
                        "Null or blank vulnerability status must resolve to Color.Default");
    }

    /// <summary>
    /// Verify a failed first load shows the fixed error alert in place of the skeleton and logs the failure once
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
    [TestCategory("AC2")]
    public async Task MyImagesLoadFailureShowsFixedErrorAndLogs()
    {
        var exception = new InvalidOperationException("secret text");
        var logger = new TestLogger<MyImages>();
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var viewService = Substitute.For<IApplicationViewService>();

            viewService.GetDiscoveryObservedImagesAsync(Arg.Any<CancellationToken>())
                       .Returns(Task.FromException<IReadOnlyList<ObservedImageListItemData>>(exception));

            testContext.Services.AddSingleton(viewService);
            testContext.Services.AddSingleton(new DashboardRefreshState());
            testContext.Services.AddSingleton(Options.Create(new DockerUpdateGuardOptions()));
            testContext.Services.AddSingleton<ILogger<MyImages>>(logger);

            var page = testContext.Render<MyImages>();

            await DetailPageAssert.AssertLoadErrorAsync(page, "Loading the images failed: unexpected error", "secret text").ConfigureAwait(false);
            DetailPageAssert.AssertLoadLoggedOnce(logger, "LoadMyImages", exception);
        }
    }

    #endregion // Methods
}