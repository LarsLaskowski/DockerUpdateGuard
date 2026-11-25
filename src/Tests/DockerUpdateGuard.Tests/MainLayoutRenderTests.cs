using Bunit;
using Bunit.TestDoubles;

using DockerUpdateGuard.Components.Layout;
using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Tests.Data;
using DockerUpdateGuard.Tests.Helper;
using DockerUpdateGuard.UI;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using MudBlazor;

using NSubstitute;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Rendering tests for <see cref="MainLayout"/>
/// </summary>
[TestClass]
public class MainLayoutRenderTests
{
    #region Methods

    /// <summary>
    /// Verify the compact status pill and the full meta cards are rendered once the dashboard summary is available
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task MainLayoutWithDashboardSummaryRendersCompactPillAndFullMeta()
    {
        var testContext = CreateContext(new DashboardSummaryViewData
                                        {
                                            ObservedImageCount = 2,
                                            MyImageCount = 1,
                                            RuntimeContainerCount = 4,
                                        });

        await using (testContext)
        {
            var component = testContext.Render<MainLayout>(parameters => parameters.Add(layout => layout.Body, string.Empty));
            var markup = component.Markup;

            Assert.Contains("app-topbar__meta--full", markup, "The full meta cards must render when the dashboard summary is available");
            Assert.Contains("app-topbar__meta--compact", markup, "The compact status pill must render when the dashboard summary is available");
            Assert.Contains("app-topbar__pill", markup, "The compact status pill chip must render when the dashboard summary is available");

            var menu = component.FindComponent<MudMenu>();

            await component.InvokeAsync(() => menu.Instance.OpenMenuAsync(EventArgs.Empty)).ConfigureAwait(false);

            Assert.Contains("app-topbar__meta-popover", component.Markup, "Opening the compact pill must reveal the popover exposing both metrics");
        }
    }

    /// <summary>
    /// Verify the last activity of the top bar is rendered as a UTC timestamp
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC3")]
    public async Task MainLayoutWithLatestScanRendersLastActivityInUtc()
    {
        var testContext = CreateContext(new DashboardSummaryViewData
                                        {
                                            LatestScan = new ScanHistoryItemData
                                                         {
                                                             StartedAtUtc = new DateTimeOffset(2026, 11, 24, 0, 27, 0, TimeSpan.FromHours(2)),
                                                         },
                                        });

        await using (testContext)
        {
            var component = testContext.Render<MainLayout>(parameters => parameters.Add(layout => layout.Body, string.Empty));

            Assert.Contains("2026-11-23 22:27 UTC", component.Markup, "The last activity must be rendered in UTC with the zone suffix");
        }
    }

    /// <summary>
    /// Verify a fixed-size skeleton is rendered while the dashboard summary is still loading
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task MainLayoutWithoutDashboardSummaryRendersLoadingSkeleton()
    {
        var testContext = CreateContext(dashboardSummary: null);

        await using (testContext)
        {
            var component = testContext.Render<MainLayout>(parameters => parameters.Add(layout => layout.Body, string.Empty));

            Assert.Contains("app-topbar__status-skeleton", component.Markup, "A skeleton must render while the dashboard summary is loading to avoid a top-bar height jump");
        }
    }

    /// <summary>
    /// Verify toggling the dark-mode button applies the dark layout class
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task MainLayoutTogglingDarkModeButtonAppliesDarkLayoutClass()
    {
        var testContext = CreateContext(dashboardSummary: null);

        await using (testContext)
        {
            var component = testContext.Render<MainLayout>(parameters => parameters.Add(layout => layout.Body, string.Empty));

            Assert.DoesNotContain("dug-dark", component.Markup, "The layout must start in light mode when no preference is stored and the system reports light mode");

            await component.Find(".app-topbar__theme-toggle").ClickAsync().ConfigureAwait(false);

            Assert.Contains("dug-dark", component.Markup, "Toggling the dark-mode button must apply the dark layout class");
        }
    }

    /// <summary>
    /// Verify a failed first summary load renders the layout with its body, hides the summary and the skeleton, and logs the failure once
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC5")]
    public async Task MainLayoutSummaryLoadFailureRendersBodyWithoutSummaryAndLogs()
    {
        var exception = new InvalidOperationException("secret text");
        var logger = new TestLogger<MainLayout>();
        var viewService = Substitute.For<IApplicationViewService>();

        viewService.GetDashboardSummaryAsync(Arg.Any<CancellationToken>())
                   .Returns(Task.FromException<DashboardSummaryViewData>(exception));
        viewService.HasBaseImagesAsync(Arg.Any<CancellationToken>())
                   .Returns(Task.FromResult(false));

        var testContext = CreateContext(viewService);

        await using (testContext)
        {
            testContext.Services.AddSingleton<ILogger<MainLayout>>(logger);

            var component = testContext.Render<MainLayout>(parameters => parameters.Add(layout => layout.Body, "layout body marker"));

            await component.WaitForAssertionAsync(() => DetailPageAssert.AssertLoadLoggedOnce(logger, "LoadDashboardSummary", exception), TimeSpan.FromSeconds(DetailPageAssert.WaitSeconds)).ConfigureAwait(false);

            Assert.Contains("layout body marker", component.Markup, "The layout must render its body");
            Assert.DoesNotContain("app-topbar__status-skeleton", component.Markup, "A failed load must not show the status skeleton");
            Assert.DoesNotContain("Protected assets", component.Markup, "A failed load must not show the summary values");
            Assert.DoesNotContain("Last activity", component.Markup, "A failed load must not show the summary values");
            Assert.DoesNotContain("secret text", component.Markup, "The layout must not show the exception's text");
        }
    }

    /// <summary>
    /// Verify a reload on navigation that fails keeps the top bar values and logs the failure once
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC7")]
    public async Task MainLayoutFailedReloadOnNavigationKeepsSummaryAndLogs()
    {
        var exception = new InvalidOperationException("secret text");
        var logger = new TestLogger<MainLayout>();
        var viewService = Substitute.For<IApplicationViewService>();
        var summary = new DashboardSummaryViewData
                      {
                          LatestScan = new ScanHistoryItemData
                                       {
                                           StartedAtUtc = new DateTimeOffset(2026, 11, 24, 0, 27, 0, TimeSpan.FromHours(2)),
                                       },
                      };

        viewService.GetDashboardSummaryAsync(Arg.Any<CancellationToken>())
                   .Returns(Task.FromResult(summary), Task.FromException<DashboardSummaryViewData>(exception));
        viewService.HasBaseImagesAsync(Arg.Any<CancellationToken>())
                   .Returns(Task.FromResult(false));

        var testContext = CreateContext(viewService);

        await using (testContext)
        {
            testContext.Services.AddSingleton<ILogger<MainLayout>>(logger);

            var component = testContext.Render<MainLayout>(parameters => parameters.Add(layout => layout.Body, string.Empty));
            var navigation = testContext.Services.GetRequiredService<NavigationManager>();

            Assert.Contains("2026-11-23 22:27 UTC", component.Markup, "The first load must show the summary");

            await component.InvokeAsync(() => navigation.NavigateTo("/elsewhere")).ConfigureAwait(false);
            await component.WaitForAssertionAsync(() => DetailPageAssert.AssertLoadLoggedOnce(logger, "LoadDashboardSummary", exception), TimeSpan.FromSeconds(DetailPageAssert.WaitSeconds)).ConfigureAwait(false);

            Assert.Contains("2026-11-23 22:27 UTC", component.Markup, "A failed reload must keep the top bar values");
            Assert.DoesNotContain("app-topbar__status-skeleton", component.Markup, "A failed reload must not show the status skeleton");
            Assert.DoesNotContain("secret text", component.Markup, "The layout must not show the exception's text");
        }
    }

    /// <summary>
    /// Create a bUnit test context wired up with the services required to render the layout
    /// </summary>
    /// <param name="dashboardSummary">Dashboard summary returned by the view service, or null to keep the loading state</param>
    /// <returns>Configured test context</returns>
    private static Bunit.BunitContext CreateContext(DashboardSummaryViewData? dashboardSummary)
    {
        var viewService = Substitute.For<IApplicationViewService>();

        viewService.GetDashboardSummaryAsync(Arg.Any<CancellationToken>())
                   .Returns(Task.FromResult(dashboardSummary!));
        viewService.HasBaseImagesAsync(Arg.Any<CancellationToken>())
                   .Returns(Task.FromResult(false));

        return CreateContext(viewService);
    }

    /// <summary>
    /// Create a bUnit test context wired up with the services required to render the layout around a view service
    /// </summary>
    /// <param name="viewService">View service the layout reads</param>
    /// <returns>Configured test context</returns>
    private static Bunit.BunitContext CreateContext(IApplicationViewService viewService)
    {
        var testContext = BlazorTestContextFactory.Create();

        testContext.Services.AddSingleton(viewService);
        testContext.Services.AddSingleton(new DashboardRefreshState());
        testContext.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        testContext.Services.AddSingleton<IOptions<DockerUpdateGuardOptions>>(Options.Create(new DockerUpdateGuardOptions()));
        testContext.Services.AddSingleton(new ResourceAssetCollection([]));
        testContext.AddBunitPersistentComponentState();

        return testContext;
    }

    #endregion // Methods
}