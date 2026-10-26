using Bunit;

using DockerUpdateGuard.Components.Pages;
using DockerUpdateGuard.Tests.Data;
using DockerUpdateGuard.Tests.Helper;
using DockerUpdateGuard.UI;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using NSubstitute;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="RuntimeContainerDetail"/>
/// </summary>
[TestClass]
public class RuntimeContainerDetailTests
{
    #region Constants

    /// <summary>
    /// Container identifier of the failing-selection tests
    /// </summary>
    private const string FailingSelectionContainerId = "container-failing";

    #endregion // Constants

    #region Fields

    /// <summary>
    /// Docker instance identifier of the failing-selection tests
    /// </summary>
    private static readonly Guid _failingSelectionInstanceId = Guid.NewGuid();

    #endregion // Fields

    #region Methods

    /// <summary>
    /// Verify the vulnerability assessment card shows the fixable finding count and the update hint chip near the tag candidates
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task RuntimeContainerDetailWithFixableFindingsShowsFixableSummaryAndUpdateHint()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
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

            var component = testContext.Render<RuntimeContainerDetail>(parameters => parameters.Add(page => page.DockerInstanceId, dockerInstanceId)
                                                                                               .Add(page => page.ContainerId, containerId));

            Assert.Contains("1 of 2 findings can be fixed by updating this image",
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
    /// <returns>Task</returns>
    [TestMethod]
    public async Task RuntimeContainerDetailWithoutFixableFindingsHidesFixableSummaryAndUpdateHint()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
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

            var component = testContext.Render<RuntimeContainerDetail>(parameters => parameters.Add(page => page.DockerInstanceId, dockerInstanceId)
                                                                                               .Add(page => page.ContainerId, containerId));

            Assert.DoesNotContain("summary-remediation",
                                  component.Markup,
                                  "The remediation line must be hidden when there are no active findings");
            Assert.DoesNotContain("Updating may resolve",
                                  component.Markup,
                                  "The update hint chip must be hidden when no active finding has a fix available");
        }
    }

    /// <summary>
    /// Verify a failed tag selection save shows a fixed reason instead of the exception message and logs the exception
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task RuntimeContainerDetailFailedSaveSelectionShowsFixedReasonAndLogsAsync()
    {
        var exception = new InvalidOperationException("connection to db.internal.test:5432 refused");
        var logger = new TestLogger<RuntimeContainerDetail>();
        var testContext = CreateFailingSelectionContext(exception, logger, hasManualSelection: false);

        await using (testContext)
        {
            var component = testContext.Render<RuntimeContainerDetail>(parameters => parameters.Add(page => page.DockerInstanceId, _failingSelectionInstanceId)
                                                                                               .Add(page => page.ContainerId, FailingSelectionContainerId));

            await component.FindAll("button").Single(button => button.TextContent.Contains("Select", StringComparison.Ordinal)).ClickAsync().ConfigureAwait(false);

            UserActionFailureAssert.AssertShown(component.Markup, "Saving the tag selection failed: unexpected error", "db.internal.test", "refused");
            UserActionFailureAssert.AssertLogged(logger, "SaveTagSelection", exception);
        }
    }

    /// <summary>
    /// Verify a failed tag selection clear shows a fixed reason instead of the exception message and logs the exception
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task RuntimeContainerDetailFailedClearSelectionShowsFixedReasonAndLogsAsync()
    {
        var exception = new InvalidOperationException("connection to db.internal.test:5432 refused");
        var logger = new TestLogger<RuntimeContainerDetail>();
        var testContext = CreateFailingSelectionContext(exception, logger, hasManualSelection: true);

        await using (testContext)
        {
            var component = testContext.Render<RuntimeContainerDetail>(parameters => parameters.Add(page => page.DockerInstanceId, _failingSelectionInstanceId)
                                                                                               .Add(page => page.ContainerId, FailingSelectionContainerId));

            await component.FindAll("button").Single(button => button.TextContent.Contains("Clear selection", StringComparison.Ordinal)).ClickAsync().ConfigureAwait(false);

            UserActionFailureAssert.AssertShown(component.Markup, "Clearing the tag selection failed: unexpected error", "db.internal.test", "refused");
            UserActionFailureAssert.AssertLogged(logger, "ClearTagSelection", exception);
        }
    }

    /// <summary>
    /// Create a test context whose tag selection service throws the given exception from save and clear
    /// </summary>
    /// <param name="exception">Exception thrown by the tag selection service</param>
    /// <param name="logger">Logger registered for the page</param>
    /// <param name="hasManualSelection">Indicates whether the detail carries a stored manual selection</param>
    /// <returns>Test context</returns>
    private static Bunit.BunitContext CreateFailingSelectionContext(Exception exception, TestLogger<RuntimeContainerDetail> logger, bool hasManualSelection)
    {
        var testContext = BlazorTestContextFactory.Create();
        var viewService = Substitute.For<IApplicationViewService>();
        var tagSelectionService = Substitute.For<IRuntimeContainerTagSelectionService>();

        viewService.GetRuntimeContainerDetailAsync(_failingSelectionInstanceId, FailingSelectionContainerId, Arg.Any<CancellationToken>())
                   .Returns(new RuntimeContainerDetailViewData
                            {
                                DockerInstanceId = _failingSelectionInstanceId,
                                ContainerId = FailingSelectionContainerId,
                                ContainerName = "api",
                                DockerInstanceName = "Production",
                                ImageReference = "ghcr.io/acme/api:1.0.0",
                                CurrentTag = "1.0.0",
                                RuntimeStatus = "Running",
                                ManualSelectionImage = hasManualSelection ? "ghcr.io/acme/api:1.1.0" : null,
                                AvailableTagCandidates = [
                                                             new TagCandidateViewData
                                                             {
                                                                 Tag = "1.1.0",
                                                                 Reason = "Newer patch release",
                                                             }
                                                         ],
                            });
        tagSelectionService.SaveSelectionAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                           .Returns(Task.FromException(exception));
        tagSelectionService.ClearSelectionAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                           .Returns(Task.FromException(exception));

        testContext.Services.AddSingleton(viewService);
        testContext.Services.AddSingleton(tagSelectionService);
        testContext.Services.AddSingleton<ILogger<RuntimeContainerDetail>>(logger);

        return testContext;
    }

    #endregion // Methods
}