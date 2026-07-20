using Bunit;

using DockerUpdateGuard.Components.Pages;
using DockerUpdateGuard.Tests.Helper;
using DockerUpdateGuard.UI;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="RuntimeContainerDetail"/>
/// </summary>
[TestClass]
public class RuntimeContainerDetailTests
{
    #region Methods

    /// <summary>
    /// Verify the vulnerability assessment card shows the fixable finding count and the update hint chip near the tag candidates
    /// </summary>
    [TestMethod]
    public void RuntimeContainerDetailWithFixableFindingsShowsFixableSummaryAndUpdateHint()
    {
        var testContext = BlazorTestContextFactory.Create();

        using (testContext)
        {
            var dockerInstanceId = Guid.NewGuid();
            const string containerId = "container-a";
            var viewService = Substitute.For<IApplicationViewService>();
            var tagSelectionService = Substitute.For<IRuntimeContainerTagSelectionService>();

            viewService.GetRuntimeContainerDetailAsync(dockerInstanceId, containerId, Arg.Any<CancellationToken>())
                       .Returns(new RuntimeContainerDetailViewData
                                {
                                    DockerInstanceId = dockerInstanceId,
                                    ContainerId = containerId,
                                    ContainerName = "api",
                                    DockerInstanceName = "Production",
                                    ImageReference = "ghcr.io/acme/api:1.0.0",
                                    CurrentTag = "1.0.0",
                                    RuntimeStatus = "Running",
                                    VulnerabilityAssessment = new VulnerabilityAssessmentViewData
                                                              {
                                                                  Status = "Findings detected",
                                                                  ActiveFindingCount = 2,
                                                                  FixableFindingCount = 1,
                                                              },
                                    UpdateFindings = [
                                                         new UpdateFindingViewData
                                                         {
                                                             Type = "Tag recommendation",
                                                             Summary = "Alternative tags are available",
                                                             IsActive = true,
                                                         }
                                                     ],
                                });

            testContext.Services.AddSingleton(viewService);
            testContext.Services.AddSingleton(tagSelectionService);

            var component = testContext.RenderComponent<RuntimeContainerDetail>(parameters => parameters.Add(page => page.DockerInstanceId, dockerInstanceId)
                                                                                                        .Add(page => page.ContainerId, containerId));

            Assert.Contains("1 of 2 active findings have a fix available",
                            component.Markup,
                            "The vulnerability assessment card must show the fixable finding count");
            Assert.Contains("Updating may resolve up to 1 of 2 active findings",
                            component.Markup,
                            "The available tag candidates section must show the fixable update hint chip");
        }
    }

    /// <summary>
    /// Verify the fixable summary line and update hint chip are hidden when there are no fixable findings
    /// </summary>
    [TestMethod]
    public void RuntimeContainerDetailWithoutFixableFindingsHidesFixableSummaryAndUpdateHint()
    {
        var testContext = BlazorTestContextFactory.Create();

        using (testContext)
        {
            var dockerInstanceId = Guid.NewGuid();
            const string containerId = "container-a";
            var viewService = Substitute.For<IApplicationViewService>();
            var tagSelectionService = Substitute.For<IRuntimeContainerTagSelectionService>();

            viewService.GetRuntimeContainerDetailAsync(dockerInstanceId, containerId, Arg.Any<CancellationToken>())
                       .Returns(new RuntimeContainerDetailViewData
                                {
                                    DockerInstanceId = dockerInstanceId,
                                    ContainerId = containerId,
                                    ContainerName = "api",
                                    DockerInstanceName = "Production",
                                    ImageReference = "ghcr.io/acme/api:1.0.0",
                                    CurrentTag = "1.0.0",
                                    RuntimeStatus = "Running",
                                    VulnerabilityAssessment = new VulnerabilityAssessmentViewData
                                                              {
                                                                  Status = "No findings",
                                                                  ActiveFindingCount = 0,
                                                                  FixableFindingCount = 0,
                                                              },
                                    UpdateFindings = [
                                                         new UpdateFindingViewData
                                                         {
                                                             Type = "Tag recommendation",
                                                             Summary = "Alternative tags are available",
                                                             IsActive = true,
                                                         }
                                                     ],
                                });

            testContext.Services.AddSingleton(viewService);
            testContext.Services.AddSingleton(tagSelectionService);

            var component = testContext.RenderComponent<RuntimeContainerDetail>(parameters => parameters.Add(page => page.DockerInstanceId, dockerInstanceId)
                                                                                                        .Add(page => page.ContainerId, containerId));

            Assert.DoesNotContain("active findings have a fix available",
                                  component.Markup,
                                  "The fixable summary line must be hidden when there are no active findings");
            Assert.DoesNotContain("Updating may resolve",
                                  component.Markup,
                                  "The update hint chip must be hidden when no active finding has a fix available");
        }
    }

    #endregion // Methods
}