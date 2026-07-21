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
    [TestMethod]
    public void DockerInstancesSingleInstanceWithHistoryRendersResponsiveCharts()
    {
        var testContext = BlazorTestContextFactory.Create();

        using (testContext)
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

            var component = testContext.RenderComponent<DockerInstancesPage>();

            Assert.Contains("dug-chart", component.Markup, "The usage-history charts must render inside the responsive chart wrapper");
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