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
    [TestMethod]
    public void DashboardWithConfiguredAccountRendersMetricTiles()
    {
        var testContext = BlazorTestContextFactory.Create();

        using (testContext)
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
            testContext.AddFakePersistentComponentState();

            var component = testContext.RenderComponent<DashboardPage>();
            var markup = component.Markup;

            Assert.Contains("My Images", markup, "The My Images tile must render when a Docker Hub account is configured");
            Assert.Contains("Base Images", markup, "The Base Images tile must render when base images exist");
            Assert.Contains("Observed Images", markup, "The Observed Images metric tile must render");
            Assert.Contains("metric-value", markup, "The number metric tiles must render their values");
        }
    }

    #endregion // Methods
}