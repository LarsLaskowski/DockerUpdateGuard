using Bunit;

using DockerUpdateGuard.Tests.Helper;
using DockerUpdateGuard.UI;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using DockerInstancesPage = DockerUpdateGuard.Components.Pages.DockerInstances;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Rendering tests for <see cref="DockerInstancesPage"/>
/// </summary>
[TestClass]
public class DockerInstancesRenderTests
{
    #region Methods

    /// <summary>
    /// Verify the single-instance history charts render inside the responsive chart wrapper
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerInstancesSingleInstanceWithHistoryRendersResponsiveCharts()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var viewService = Substitute.For<IApplicationViewService>();
            var instanceId = Guid.NewGuid();

            viewService.GetDockerInstancesAsync(Arg.Any<CancellationToken>())
                       .Returns(Task.FromResult<IReadOnlyList<DockerInstanceListItemData>>([
                                                                                               new DockerInstanceListItemData
                                                                                               {
                                                                                                   Id = instanceId,
                                                                                                   Name = "local",
                                                                                                   EndpointUri = "unix:///var/run/docker.sock",
                                                                                               },
                                                                                           ]));

            viewService.GetDockerInstanceDetailAsync(instanceId, Arg.Any<CancellationToken>())
                       .Returns(Task.FromResult<DockerInstanceDetailViewData?>(new DockerInstanceDetailViewData
                                                                               {
                                                                                   Id = instanceId,
                                                                                   Name = "local",
                                                                                   EndpointUri = "unix:///var/run/docker.sock",
                                                                                   CurrentResourceUsage = CreateUsagePoint(),
                                                                                   ResourceUsageHistory = [CreateUsagePoint(), CreateUsagePoint()],
                                                                               }));

            testContext.Services.AddSingleton(viewService);

            var component = testContext.Render<DockerInstancesPage>();

            Assert.Contains("dug-chart", component.Markup, "The usage-history charts must render inside the responsive chart wrapper");
        }
    }

    /// <summary>
    /// Verify the instance list shows the latest scan completion in UTC and nothing for an instance never scanned
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC3")]
    public async Task DockerInstancesWithAndWithoutScanCompletionRendersUtcOrEmptyAsync()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var viewService = Substitute.For<IApplicationViewService>();

            viewService.GetDockerInstancesAsync(Arg.Any<CancellationToken>())
                       .Returns(Task.FromResult<IReadOnlyList<DockerInstanceListItemData>>([
                                                                                               new DockerInstanceListItemData
                                                                                               {
                                                                                                   Id = Guid.NewGuid(),
                                                                                                   Name = "scanned",
                                                                                                   LatestScanStatus = "Succeeded",
                                                                                                   LatestScanCompletedAtUtc = new DateTimeOffset(2026, 11, 23, 23, 27, 0, TimeSpan.FromHours(2)),
                                                                                               },
                                                                                               new DockerInstanceListItemData
                                                                                               {
                                                                                                   Id = Guid.NewGuid(),
                                                                                                   Name = "fresh",
                                                                                                   LatestScanStatus = "NotScanned",
                                                                                               },
                                                                                           ]));

            testContext.Services.AddSingleton(viewService);

            var component = testContext.Render<DockerInstancesPage>();
            var secondaries = component.FindAll("span.table-secondary").Select(element => element.TextContent.Trim()).ToList();

            Assert.Contains("Succeeded · 2026-11-23 21:27 UTC", secondaries, "A completed scan must be shown in UTC with the zone suffix");
            Assert.Contains("NotScanned ·", secondaries, "An instance without a completed scan must show no timestamp");
        }
    }

    /// <summary>
    /// Create a resource usage point for the tests
    /// </summary>
    /// <returns>Resource usage point</returns>
    private static ResourceUsagePointViewData CreateUsagePoint()
    {
        return new ResourceUsagePointViewData
               {
                   CpuPercent = 12.5m,
                   MemoryUsageBytes = 256 * 1024 * 1024,
                   MemoryLimitBytes = 1024 * 1024 * 1024,
                   NetworkRxBytesPerSecond = 1024,
                   NetworkTxBytesPerSecond = 512,
                   RecordedAtUtc = DateTimeOffset.UtcNow,
               };
    }

    #endregion // Methods
}