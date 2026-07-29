using Bunit;
using Bunit.TestDoubles;

using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Tests.Helper;
using DockerUpdateGuard.UI;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using NSubstitute;

using DashboardPage = DockerUpdateGuard.Components.Pages.Dashboard;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Rendering tests for <see cref="DashboardPage"/>
/// </summary>
[TestClass]
public class DashboardRenderTests
{
    #region Methods

    /// <summary>
    /// Verify the number metric tiles render for the phone two-up grid, including the optional My Images and Base Images tiles
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DashboardWithConfiguredAccountRendersMetricTiles()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var viewService = Substitute.For<IApplicationViewService>();

            viewService.GetDashboardAsync(Arg.Any<CancellationToken>())
                       .Returns(Task.FromResult(new DashboardViewData
                                                {
                                                    ObservedImageCount = 3,
                                                    MyImageCount = 2,
                                                    DockerInstanceCount = 1,
                                                    RuntimeContainerCount = 5,
                                                    BaseImageCount = 4,
                                                    ActiveUpdateFindingCount = 1,
                                                    OwnImageBaseRuntimeWarningCount = 0,
                                                    ActiveVulnerabilityFindingCount = 2,
                                                }));

            var options = Options.Create(new DockerUpdateGuardOptions
                                         {
                                             DockerHub =
                                             {
                                                 UserName = "octocat",
                                                 Pat = "token",
                                             },
                                         });

            testContext.Services.AddSingleton(viewService);
            testContext.Services.AddSingleton(new DashboardRefreshState());
            testContext.Services.AddSingleton<IOptions<DockerUpdateGuardOptions>>(options);
            testContext.AddBunitPersistentComponentState();

            var component = testContext.Render<DashboardPage>();
            var markup = component.Markup;

            Assert.Contains("My Images", markup, "The My Images tile must render when a Docker Hub account is configured");
            Assert.Contains("Base Images", markup, "The Base Images tile must render when base images exist");
            Assert.Contains("Observed Images", markup, "The Observed Images metric tile must render");
            Assert.Contains("metric-value", markup, "The number metric tiles must render their values");
            Assert.Contains("metric-card__icon", markup, "The metric tiles must render an icon badge");
        }
    }

    /// <summary>
    /// Verify the info banner is rendered when vulnerability scanning is disabled
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DashboardWithDisabledVulnerabilityScanningRendersInfoBanner()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var markup = RenderDashboardMarkup(testContext, VulnerabilityConfigurationHintKind.ScanningDisabled);

            Assert.Contains("Vulnerability scanning is disabled",
                            markup,
                            "The dashboard must explain how to enable vulnerability scanning when it is switched off");
            Assert.Contains("DockerUpdateGuard:Vulnerabilities:Enabled",
                            markup,
                            "The disabled banner must name the configuration key that enables vulnerability scanning");
        }
    }

    /// <summary>
    /// Verify the warning banner names the missing Trivy base URL
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DashboardWithMissingTrivyBaseUrlRendersWarningBanner()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var markup = RenderDashboardMarkup(testContext, VulnerabilityConfigurationHintKind.TrivyBaseUrlMissing);

            Assert.Contains("DockerUpdateGuard:Vulnerabilities:TrivyBaseUrl",
                            markup,
                            "The misconfiguration banner must name the missing Trivy base URL setting");
            Assert.DoesNotContain("Vulnerability scanning is disabled",
                                  markup,
                                  "The disabled banner must not be rendered while vulnerability scanning is enabled");
        }
    }

    /// <summary>
    /// Verify no configuration banner is rendered for a correctly configured provider
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DashboardWithConfiguredVulnerabilityProviderRendersNoBanner()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var markup = RenderDashboardMarkup(testContext, VulnerabilityConfigurationHintKind.None);

            Assert.DoesNotContain("Vulnerability scanning is disabled",
                                  markup,
                                  "The disabled banner must not be rendered while vulnerability scanning is enabled");
            Assert.DoesNotContain("DockerUpdateGuard:Vulnerabilities:TrivyBaseUrl",
                                  markup,
                                  "The misconfiguration banner must not be rendered for a correctly configured provider");
        }
    }

    /// <summary>
    /// Render the dashboard for a vulnerability configuration hint and return its markup
    /// </summary>
    /// <param name="testContext">bUnit test context</param>
    /// <param name="hintKind">Vulnerability configuration hint kind</param>
    /// <returns>Rendered markup</returns>
    private static string RenderDashboardMarkup(BunitContext testContext, VulnerabilityConfigurationHintKind hintKind)
    {
        var viewService = Substitute.For<IApplicationViewService>();

        viewService.GetDashboardAsync(Arg.Any<CancellationToken>())
                   .Returns(Task.FromResult(new DashboardViewData
                                            {
                                                VulnerabilityScanningEnabled = hintKind != VulnerabilityConfigurationHintKind.ScanningDisabled,
                                                VulnerabilityConfigurationHint = hintKind,
                                            }));

        testContext.Services.AddSingleton(viewService);
        testContext.Services.AddSingleton(new DashboardRefreshState());
        testContext.Services.AddSingleton<IOptions<DockerUpdateGuardOptions>>(Options.Create(new DockerUpdateGuardOptions()));

        var component = testContext.Render<DashboardPage>();

        return component.Markup;
    }

    #endregion // Methods
}