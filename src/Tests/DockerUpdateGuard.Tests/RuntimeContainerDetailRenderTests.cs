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

            var component = testContext.Render<RuntimeContainerDetailPage>(parameters => parameters.Add(page => page.DockerInstanceId, instanceId)
                                                                                                   .Add(page => page.ContainerId, "abc123"));
            var markup = component.Markup;

            Assert.Contains("dug-chart", markup, "The resource-history charts must render inside the responsive chart wrapper");
            Assert.Contains("summary-value", markup, "The current-usage summary cards must render their values");
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