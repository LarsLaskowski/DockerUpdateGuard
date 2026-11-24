using Bunit;

using DockerUpdateGuard.Tests.Helper;
using DockerUpdateGuard.UI;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using RuntimeContainerDetailPage = DockerUpdateGuard.Components.Pages.RuntimeContainerDetail;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Rendering tests for <see cref="RuntimeContainerDetailPage"/>
/// </summary>
[TestClass]
public class RuntimeContainerDetailRenderTests
{
    #region Methods

    /// <summary>
    /// Verify the resource-history charts render inside the responsive chart wrapper and the current-usage cards render
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task RuntimeContainerDetailWithHistoryRendersResponsiveChartsAndUsageCards()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var viewService = Substitute.For<IApplicationViewService>();
            var tagSelectionService = Substitute.For<IRuntimeContainerTagSelectionService>();
            var instanceId = Guid.NewGuid();

            viewService.GetRuntimeContainerDetailAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                       .Returns(Task.FromResult<RuntimeContainerDetailViewData?>(new RuntimeContainerDetailViewData
                                                                                 {
                                                                                     DockerInstanceId = instanceId,
                                                                                     ContainerId = "abc123",
                                                                                     ContainerName = "api",
                                                                                     DockerInstanceName = "local",
                                                                                     ImageReference = "docker.io/company/api:1.0.0",
                                                                                     ActiveBaseImageVulnerabilityFindingCount = 1,
                                                                                     BaseImageVulnerabilitySummary = "1 base image finding",
                                                                                     VulnerabilityAssessment = new VulnerabilityAssessmentViewData
                                                                                                               {
                                                                                                                   Status = "Findings detected",
                                                                                                                   Source = "Trivy",
                                                                                                                   ActiveFindingCount = 3,
                                                                                                                   FixableFindingCount = 2,
                                                                                                                   NewFindingCount = 1,
                                                                                                                   ResolvedFindingCount = 1,
                                                                                                                   CheckedAtUtc = DateTimeOffset.UtcNow,
                                                                                                                   Message = "Scan completed",
                                                                                                               },
                                                                                     CurrentResourceUsage = CreateUsagePoint(),
                                                                                     ResourceUsageHistory = [CreateUsagePoint(), CreateUsagePoint()],
                                                                                 }));

            testContext.Services.AddSingleton(viewService);
            testContext.Services.AddSingleton(tagSelectionService);
            testContext.AddHiddenContainerActions();

            var component = testContext.Render<RuntimeContainerDetailPage>(parameters => parameters.Add(page => page.DockerInstanceId, instanceId)
                                                                                                   .Add(page => page.ContainerId, "abc123"));
            var markup = component.Markup;

            Assert.Contains("dug-chart", markup, "The resource-history charts must render inside the responsive chart wrapper");
            Assert.Contains("summary-value", markup, "The current-usage summary cards must render their values");
        }
    }

    /// <summary>
    /// Verify the saved tag selection time and the scan history start are shown in UTC
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC3")]
    public async Task RuntimeContainerDetailWithSelectionAndScanHistoryRendersUtcTimestampsAsync()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var viewService = Substitute.For<IApplicationViewService>();
            var tagSelectionService = Substitute.For<IRuntimeContainerTagSelectionService>();
            var instanceId = Guid.NewGuid();

            viewService.GetRuntimeContainerDetailAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                       .Returns(Task.FromResult<RuntimeContainerDetailViewData?>(new RuntimeContainerDetailViewData
                                                                                 {
                                                                                     DockerInstanceId = instanceId,
                                                                                     ContainerId = "abc123",
                                                                                     ContainerName = "api",
                                                                                     DockerInstanceName = "local",
                                                                                     ImageReference = "docker.io/company/api:1.0.0",
                                                                                     ManualSelectionImage = "docker.io/company/api:2.0.0",
                                                                                     ManualSelectionAtUtc = new DateTimeOffset(2026, 11, 23, 23, 27, 0, TimeSpan.FromHours(2)),
                                                                                     ScanHistory = [
                                                                                                       new ScanHistoryItemData
                                                                                                       {
                                                                                                           Status = "Succeeded",
                                                                                                           TriggerSource = "Manual",
                                                                                                           StartedAtUtc = new DateTimeOffset(2026, 11, 23, 10, 5, 0, TimeSpan.FromHours(2)),
                                                                                                       },
                                                                                                   ],
                                                                                 }));

            testContext.Services.AddSingleton(viewService);
            testContext.Services.AddSingleton(tagSelectionService);
            testContext.AddHiddenContainerActions();

            var component = testContext.Render<RuntimeContainerDetailPage>(parameters => parameters.Add(page => page.DockerInstanceId, instanceId)
                                                                                                   .Add(page => page.ContainerId, "abc123"));
            var markup = component.Markup;

            Assert.Contains("Saved 2026-11-23 21:27 UTC", markup, "The saved tag selection time must be shown in UTC");
            Assert.Contains("2026-11-23 08:05 UTC", markup, "The scan history start must be shown in UTC");
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