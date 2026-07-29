using Bunit;

using DockerUpdateGuard.Components.Pages;
using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Images.Interfaces;
using DockerUpdateGuard.Tests.Data;
using DockerUpdateGuard.Tests.Helper;
using DockerUpdateGuard.UI;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using NSubstitute;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="ScanHistory"/>
/// </summary>
[TestClass]
public class ScanHistoryTests
{
    #region Methods

    /// <summary>
    /// Verify the refresh button is shown and triggers the enrichment service when scanning is enabled
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ScanHistoryVulnerabilitiesEnabledClickingRefreshTriggersEnrichment()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var viewService = Substitute.For<IApplicationViewService>();
            var enrichmentService = Substitute.For<IVulnerabilityEnrichmentService>();

            viewService.GetScanHistoryAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                       .Returns(new List<ScanHistoryItemData>
                                {
                                    new()
                                    {
                                        Type = "Vulnerability",
                                        Status = "Completed",
                                        TriggerSource = "Manual",
                                        StartedAtUtc = DateTimeOffset.UtcNow,
                                    }
                                });

            testContext.Services.AddSingleton(viewService);
            testContext.Services.AddSingleton(enrichmentService);
            testContext.Services.AddSingleton(new DashboardRefreshState());
            testContext.Services.AddSingleton<IOptionsMonitor<DockerUpdateGuardOptions>>(CreateOptionsMonitor(enabled: true));

            var component = testContext.Render<ScanHistory>();

            Assert.Contains("Refresh vulnerabilities", component.Markup, "The refresh button must be shown when vulnerability scanning is enabled");

            await component.Find("button").ClickAsync().ConfigureAwait(false);

            await enrichmentService.Received(1).RefreshAsync(ScanTriggerSource.Manual, Arg.Any<CancellationToken>()).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Verify the refresh button is hidden when scanning is disabled
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ScanHistoryVulnerabilitiesDisabledHidesRefreshButton()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var viewService = Substitute.For<IApplicationViewService>();

            viewService.GetScanHistoryAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                       .Returns(new List<ScanHistoryItemData>());

            testContext.Services.AddSingleton(viewService);
            testContext.Services.AddSingleton(Substitute.For<IVulnerabilityEnrichmentService>());
            testContext.Services.AddSingleton(new DashboardRefreshState());
            testContext.Services.AddSingleton<IOptionsMonitor<DockerUpdateGuardOptions>>(CreateOptionsMonitor(enabled: false));

            var component = testContext.Render<ScanHistory>();

            Assert.DoesNotContain("Refresh vulnerabilities", component.Markup, "The refresh button must be hidden when vulnerability scanning is disabled");
        }
    }

    /// <summary>
    /// Verify a surfaced enrichment failure is shown as an error alert
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ScanHistoryRefreshFailureShowsErrorAlert()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var viewService = Substitute.For<IApplicationViewService>();
            var enrichmentService = Substitute.For<IVulnerabilityEnrichmentService>();

            viewService.GetScanHistoryAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                       .Returns(new List<ScanHistoryItemData>());
            enrichmentService.RefreshAsync(Arg.Any<ScanTriggerSource>(), Arg.Any<CancellationToken>())
                             .Returns(Task.FromException(new InvalidOperationException("scan boom")));

            testContext.Services.AddSingleton(viewService);
            testContext.Services.AddSingleton(enrichmentService);
            testContext.Services.AddSingleton(new DashboardRefreshState());
            testContext.Services.AddSingleton<IOptionsMonitor<DockerUpdateGuardOptions>>(CreateOptionsMonitor(enabled: true));

            var component = testContext.Render<ScanHistory>();

            await component.Find("button").ClickAsync().ConfigureAwait(false);

            Assert.Contains("scan boom", component.Markup, "A failed refresh must surface its error message as an alert");
        }
    }

    /// <summary>
    /// Verify the estimated next scheduled vulnerability refresh is shown when scanning is enabled
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ScanHistoryVulnerabilitiesEnabledShowsNextScheduledRefresh()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var viewService = Substitute.For<IApplicationViewService>();
            var nextRefreshUtc = new DateTimeOffset(2026, 7, 29, 18, 30, 0, TimeSpan.Zero);

            viewService.GetScanHistoryAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                       .Returns(new List<ScanHistoryItemData>());
            viewService.GetVulnerabilityScheduleAsync(Arg.Any<CancellationToken>())
                       .Returns(new VulnerabilityScheduleViewData
                                {
                                    VulnerabilityScanningEnabled = true,
                                    NextVulnerabilityRefreshUtc = nextRefreshUtc,
                                });

            testContext.Services.AddSingleton(viewService);
            testContext.Services.AddSingleton(Substitute.For<IVulnerabilityEnrichmentService>());
            testContext.Services.AddSingleton(new DashboardRefreshState());
            testContext.Services.AddSingleton<IOptionsMonitor<DockerUpdateGuardOptions>>(CreateOptionsMonitor(enabled: true));

            var component = testContext.Render<ScanHistory>();

            Assert.Contains("Next scheduled vulnerability refresh:",
                            component.Markup,
                            "The page header must show the estimated next scheduled vulnerability refresh");
            Assert.Contains(nextRefreshUtc.ToLocalTime().ToString("g"),
                            component.Markup,
                            "The estimated next scheduled vulnerability refresh must be rendered as a local timestamp");
        }
    }

    /// <summary>
    /// Verify the no-run-yet variant is shown when no vulnerability run was recorded
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ScanHistoryWithoutRecordedVulnerabilityRunShowsShortlyHint()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var viewService = Substitute.For<IApplicationViewService>();

            viewService.GetScanHistoryAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                       .Returns(new List<ScanHistoryItemData>());
            viewService.GetVulnerabilityScheduleAsync(Arg.Any<CancellationToken>())
                       .Returns(new VulnerabilityScheduleViewData
                                {
                                    VulnerabilityScanningEnabled = true,
                                });

            testContext.Services.AddSingleton(viewService);
            testContext.Services.AddSingleton(Substitute.For<IVulnerabilityEnrichmentService>());
            testContext.Services.AddSingleton(new DashboardRefreshState());
            testContext.Services.AddSingleton<IOptionsMonitor<DockerUpdateGuardOptions>>(CreateOptionsMonitor(enabled: true));

            var component = testContext.Render<ScanHistory>();

            Assert.Contains("Next scheduled vulnerability refresh: shortly (no run recorded yet)",
                            component.Markup,
                            "The page header must fall back to the no-run-yet variant when no vulnerability run was recorded");
        }
    }

    /// <summary>
    /// Verify the next scheduled vulnerability refresh is hidden when scanning is disabled
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ScanHistoryVulnerabilitiesDisabledHidesNextScheduledRefresh()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var viewService = Substitute.For<IApplicationViewService>();

            viewService.GetScanHistoryAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                       .Returns(new List<ScanHistoryItemData>());
            viewService.GetVulnerabilityScheduleAsync(Arg.Any<CancellationToken>())
                       .Returns(new VulnerabilityScheduleViewData());

            testContext.Services.AddSingleton(viewService);
            testContext.Services.AddSingleton(Substitute.For<IVulnerabilityEnrichmentService>());
            testContext.Services.AddSingleton(new DashboardRefreshState());
            testContext.Services.AddSingleton<IOptionsMonitor<DockerUpdateGuardOptions>>(CreateOptionsMonitor(enabled: false));

            var component = testContext.Render<ScanHistory>();

            Assert.DoesNotContain("Next scheduled vulnerability refresh",
                                  component.Markup,
                                  "The next scheduled vulnerability refresh must be hidden when vulnerability scanning is disabled");
        }
    }

    /// <summary>
    /// Create an options monitor for the tests
    /// </summary>
    /// <param name="enabled">Whether vulnerability scanning is enabled</param>
    /// <returns>Options monitor</returns>
    private static TestOptionsMonitor<DockerUpdateGuardOptions> CreateOptionsMonitor(bool enabled)
    {
        return new TestOptionsMonitor<DockerUpdateGuardOptions>(new DockerUpdateGuardOptions
                                                                {
                                                                    Vulnerabilities = new VulnerabilityOptions
                                                                                      {
                                                                                          Enabled = enabled,
                                                                                          Provider = enabled ? VulnerabilityProviderKind.Trivy : VulnerabilityProviderKind.None,
                                                                                      },
                                                                });
    }

    #endregion // Methods
}