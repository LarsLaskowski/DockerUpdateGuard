using Bunit;

using DockerUpdateGuard.Components.Common;
using DockerUpdateGuard.Components.Pages;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Portainer.Data;
using DockerUpdateGuard.Portainer.Interfaces;
using DockerUpdateGuard.Tests.Data;
using DockerUpdateGuard.Tests.Helper;
using DockerUpdateGuard.UI;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using MudBlazor;

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

    /// <summary>
    /// Container identifier of the load tests
    /// </summary>
    private const string LoadContainerId = "container-load";

    /// <summary>
    /// Second container identifier of the load tests
    /// </summary>
    private const string SecondContainerId = "container-load-second";

    /// <summary>
    /// Container identifier of the container action tests
    /// </summary>
    private const string ActionContainerId = "f907e278af7c63d535c4c440b720247f8c2dca219ffc8167dd3865b16004319e";

    /// <summary>
    /// Message of a successful restart (E1)
    /// </summary>
    private const string RestartSucceededMessage = "Action 'restart' on 'api' executed successfully";

    /// <summary>
    /// Container identifier of the first route test container
    /// </summary>
    private const string RouteContainerA = "route-container-a";

    /// <summary>
    /// Container identifier of the second route test container
    /// </summary>
    private const string RouteContainerB = "route-container-b";

    /// <summary>
    /// Container name of the first route test container
    /// </summary>
    private const string RouteNameA = "route-api-a";

    /// <summary>
    /// Container name of the second route test container
    /// </summary>
    private const string RouteNameB = "route-api-b";

    /// <summary>
    /// Seconds a test waits for the page to reach an expected state
    /// </summary>
    private const int WaitSeconds = 2;

    #endregion // Constants

    #region Fields

    /// <summary>
    /// Docker instance identifier of the failing-selection tests
    /// </summary>
    private static readonly Guid _failingSelectionInstanceId = Guid.NewGuid();

    /// <summary>
    /// Docker instance identifier of the load tests
    /// </summary>
    private static readonly Guid _loadInstanceId = Guid.NewGuid();

    /// <summary>
    /// Docker instance identifier of the container action tests
    /// </summary>
    private static readonly Guid _actionInstanceId = Guid.NewGuid();

    /// <summary>
    /// Docker instance identifier of the route tests
    /// </summary>
    private static readonly Guid _routeInstanceId = Guid.NewGuid();

    /// <summary>
    /// Request time of the history rows
    /// </summary>
    private static readonly DateTimeOffset _requestedAtUtc = new(2026, 11, 14, 11, 30, 0, TimeSpan.FromHours(2));

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
            testContext.AddHiddenContainerActions();

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
            testContext.AddHiddenContainerActions();

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
    /// Verify the section shows the actions the availability offers, or its message instead of the buttons, below the runtime status note
    /// </summary>
    /// <param name="scenario">Availability scenario</param>
    /// <param name="expectedButtons">Expected action buttons, comma separated</param>
    /// <param name="expectedMessage">Expected availability message; empty when buttons are shown</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    [DataRow("Running", "Stop,Restart", "")]
    [DataRow("Exited", "Start", "")]
    [DataRow("NotCurrent", "", "This container is not in the latest runtime scan of 'Production'; no actions are available.")]
    [DataRow("Unsupported", "", "Portainer environment 8 not found; check Portainer:EndpointId of Docker instance 'Production'")]
    [DataRow("InProgress", "", "An action is in progress for this container.")]
    [DataRow("Dead", "", "No actions are available for this container.")]
    public async Task RuntimeContainerDetailRendersOfferedActionsOrAvailabilityMessageAsync(string scenario, string expectedButtons, string expectedMessage)
    {
        var containerActionService = CreateContainerActionService(CreateAvailability(scenario));
        var testContext = CreateActionContext(containerActionService, CreateViewService(), new TestLogger<RuntimeContainerDetail>());

        await using (testContext)
        {
            var page = RenderActionPage(testContext);

            await page.WaitForAssertionAsync(() => Assert.Contains("Container actions", page.Markup, "The section must render once the availability is known"), TimeSpan.FromSeconds(WaitSeconds)).ConfigureAwait(false);

            var section = FindActionSection(page).TextContent;

            Assert.Contains("Start, stop or restart this container through Portainer. Every action asks for confirmation and is recorded.", section, "The section must carry its description");
            Assert.Contains("an action does not update it. Run ", section, "The section must carry the runtime status note of S1");
            Assert.Contains(expectedMessage, section, $"The section must show the message of scenario {scenario}");
            Assert.AreSequenceEqual(expectedButtons.Split(',', StringSplitOptions.RemoveEmptyEntries),
                                    GetActionButtonTexts(page),
                                    SequenceOrder.InAnyOrder,
                                    $"Scenario {scenario} must offer exactly the buttons '{expectedButtons}'");
        }
    }

    /// <summary>
    /// Verify cancelling the confirmation dialog executes and records nothing
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC3")]
    public async Task RuntimeContainerDetailCancelledConfirmationExecutesNothingAsync()
    {
        var containerActionService = CreateContainerActionService(CreateAvailability("Running"));
        var testContext = CreateActionContext(containerActionService, CreateViewService(), new TestLogger<RuntimeContainerDetail>());

        await using (testContext)
        {
            var provider = testContext.Render<MudDialogProvider>();
            var page = RenderActionPage(testContext);

            await ConfirmAsync(page, provider, "Stop", "Cancel").ConfigureAwait(false);

            await provider.WaitForAssertionAsync(() => Assert.IsEmpty(provider.FindAll(".mud-dialog"), "Cancel must close the dialog"), TimeSpan.FromSeconds(WaitSeconds)).ConfigureAwait(false);

            await containerActionService.DidNotReceive()
                                        .ExecuteAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<ContainerActionType>())
                                        .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Verify every action button of the container is disabled while a confirmed action runs and the confirmed one shows progress
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC10")]
    public async Task RuntimeContainerDetailWhileActionRunsDisablesAllActionButtonsAsync()
    {
        var pending = new TaskCompletionSource<ContainerActionRunResultData>(TaskCreationOptions.RunContinuationsAsynchronously);
        var containerActionService = CreateContainerActionService(CreateAvailability("Running"));
        var testContext = CreateActionContext(containerActionService, CreateViewService(), new TestLogger<RuntimeContainerDetail>());

        containerActionService.ExecuteAsync(_actionInstanceId, ActionContainerId, ContainerActionType.StopContainer)
                              .Returns(pending.Task);

        await using (testContext)
        {
            var provider = testContext.Render<MudDialogProvider>();
            var page = RenderActionPage(testContext);

            await ConfirmAsync(page, provider, "Stop", "Stop").ConfigureAwait(false);

            await page.WaitForAssertionAsync(() => Assert.IsTrue(FindButton(page, "Stop").HasAttribute("disabled"), "The confirmed button must be disabled while its action runs"), TimeSpan.FromSeconds(WaitSeconds)).ConfigureAwait(false);

            var stopButton = FindButton(page, "Stop");
            var restartButton = FindButton(page, "Restart");

            pending.TrySetResult(new ContainerActionRunResultData
                                 {
                                     Status = ContainerActionStatus.Succeeded,
                                     Message = "Action 'stop' on 'api' executed successfully",
                                 });

            Assert.IsTrue(restartButton.HasAttribute("disabled"), "Every other action button of the container must be disabled while an action runs");
            Assert.IsNotNull(stopButton.QuerySelector(".mud-progress-circular"), "The confirmed button must show progress");
        }
    }

    /// <summary>
    /// Verify the page shows the outcome of a confirmed action and reloads the history and the availability
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC10")]
    [TestCategory("AC3")]
    public async Task RuntimeContainerDetailAfterActionShowsOutcomeAndReloadsHistoryAsync()
    {
        var history = new List<ContainerActionRunViewData>
                      {
                          new()
                          {
                              Action = "Restart",
                              Status = "Succeeded",
                              RequestedAtUtc = _requestedAtUtc,
                              CompletedAtUtc = _requestedAtUtc.AddSeconds(5),
                              RequestedBy = "UI",
                              Message = RestartSucceededMessage,
                          },
                          new()
                          {
                              Action = "Stop",
                              Status = "Running",
                              RequestedAtUtc = _requestedAtUtc.AddMinutes(-10),
                              RequestedBy = "UI",
                          }
                      };
        var viewService = CreateViewService();
        var containerActionService = CreateContainerActionService(CreateAvailability("Running"));
        var testContext = CreateActionContext(containerActionService, viewService, new TestLogger<RuntimeContainerDetail>());

        viewService.GetRuntimeContainerDetailAsync(_actionInstanceId, ActionContainerId, Arg.Any<CancellationToken>())
                   .Returns(CreateActionDetail([]), CreateActionDetail(history));
        containerActionService.ExecuteAsync(_actionInstanceId, ActionContainerId, ContainerActionType.RestartContainer)
                              .Returns(new ContainerActionRunResultData
                                       {
                                           Status = ContainerActionStatus.Succeeded,
                                           Message = RestartSucceededMessage,
                                       });

        await using (testContext)
        {
            var provider = testContext.Render<MudDialogProvider>();
            var page = RenderActionPage(testContext);

            await ConfirmAsync(page, provider, "Restart", "Restart").ConfigureAwait(false);

            await page.WaitForAssertionAsync(() => Assert.HasCount(2, FindActionSection(page).QuerySelectorAll("tbody tr"), "The reloaded history must list both runs"), TimeSpan.FromSeconds(WaitSeconds)).ConfigureAwait(false);

            var outcome = FindActionSection(page).QuerySelectorAll(".mud-alert")
                                                 .Single(alert => alert.TextContent.Contains(RestartSucceededMessage, StringComparison.Ordinal));
            var rows = FindActionSection(page).QuerySelectorAll("tbody tr")
                                              .Select(row => row.TextContent)
                                              .ToList();

            Assert.Contains("success", outcome.ClassName ?? string.Empty, "A succeeded outcome must use the success style");
            Assert.Contains("Action history", FindActionSection(page).TextContent, "The history must carry its heading");
            Assert.Contains(text => text.Contains("Restart", StringComparison.Ordinal)
                                    && text.Contains("Succeeded", StringComparison.Ordinal)
                                    && text.Contains("2026-11-14 09:30 UTC", StringComparison.Ordinal)
                                    && text.Contains("UI", StringComparison.Ordinal)
                                    && text.Contains(RestartSucceededMessage, StringComparison.Ordinal),
                            rows,
                            "The newest run must show its action, status, request time, requester and message");
            Assert.Contains(text => text.Contains("Stop", StringComparison.Ordinal)
                                    && text.Contains("Running", StringComparison.Ordinal)
                                    && text.Contains('—'),
                            rows,
                            "A run without completion time must show a dash in the Completed column");
            await viewService.Received(2)
                             .GetRuntimeContainerDetailAsync(_actionInstanceId, ActionContainerId, Arg.Any<CancellationToken>())
                             .ConfigureAwait(false);
            await containerActionService.Received(2)
                                        .GetAvailabilityAsync(_actionInstanceId, ActionContainerId, Arg.Any<CancellationToken>())
                                        .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Verify a container without action runs shows the empty history text
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC10")]
    public async Task RuntimeContainerDetailWithoutActionRunsShowsEmptyHistoryAsync()
    {
        var containerActionService = CreateContainerActionService(CreateAvailability("Running"));
        var testContext = CreateActionContext(containerActionService, CreateViewService(), new TestLogger<RuntimeContainerDetail>());

        await using (testContext)
        {
            var page = RenderActionPage(testContext);

            await page.WaitForAssertionAsync(() => Assert.Contains("Container actions", page.Markup, "The section must render once the availability is known"), TimeSpan.FromSeconds(WaitSeconds)).ConfigureAwait(false);

            Assert.Contains("No container actions yet.",
                            FindActionSection(page).QuerySelector(".empty-state-panel")?.TextContent ?? string.Empty,
                            "A container without runs must show the empty history text of S6");
        }
    }

    /// <summary>
    /// Verify a failing container action shows a fixed reason instead of the exception message and logs the exception
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC13")]
    public async Task RuntimeContainerDetailFailedContainerActionShowsFixedReasonAndLogsAsync()
    {
        var exception = new InvalidOperationException("connection to db.internal.test:5432 refused");
        var logger = new TestLogger<RuntimeContainerDetail>();
        var containerActionService = CreateContainerActionService(CreateAvailability("Running"));
        var testContext = CreateActionContext(containerActionService, CreateViewService(), logger);

        containerActionService.ExecuteAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<ContainerActionType>())
                              .Returns(Task.FromException<ContainerActionRunResultData>(exception));

        await using (testContext)
        {
            var provider = testContext.Render<MudDialogProvider>();
            var page = RenderActionPage(testContext);

            await ConfirmAsync(page, provider, "Restart", "Restart").ConfigureAwait(false);

            await page.WaitForAssertionAsync(() => Assert.Contains("Container action failed", page.Markup, "The page must show the failure"), TimeSpan.FromSeconds(WaitSeconds)).ConfigureAwait(false);

            UserActionFailureAssert.AssertShown(page.Markup, "Container action failed: unexpected error", "db.internal.test", "refused");
            UserActionFailureAssert.AssertLogged(logger, "ContainerAction", exception);
        }
    }

    /// <summary>
    /// Verify a render that is not interactive (prerender) never runs the capability check and shows no action section
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    public async Task RuntimeContainerDetailWithStaticRendererDoesNotCheckContainerActionsAsync()
    {
        var containerActionService = CreateContainerActionService(CreateAvailability("Running"));
        var testContext = CreateActionContext(containerActionService, CreateViewService(), new TestLogger<RuntimeContainerDetail>());

        testContext.SetRendererInfo(new RendererInfo("Static", false));

        await using (testContext)
        {
            var page = RenderActionPage(testContext);

            await page.WaitForAssertionAsync(() => Assert.Contains("ghcr.io/acme/api:1.0.0", page.Markup, "The detail must render"), TimeSpan.FromSeconds(WaitSeconds)).ConfigureAwait(false);

            Assert.IsFalse(HasActionSection(page), "A prerendered page must not show the action section");
            Assert.IsEmpty(containerActionService.ReceivedCalls(), "A prerendered page must not check the container actions");
        }
    }

    /// <summary>
    /// Verify an availability that is not shown (instance or Portainer disabled) renders no action section
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    public async Task RuntimeContainerDetailWhenActionsNotShownRendersNoActionSectionAsync()
    {
        var containerActionService = CreateContainerActionService(new ContainerActionAvailabilityData());
        var testContext = CreateActionContext(containerActionService, CreateViewService(), new TestLogger<RuntimeContainerDetail>());

        await using (testContext)
        {
            var page = RenderActionPage(testContext);

            await WaitForAvailabilityRequestAsync(page, containerActionService).ConfigureAwait(false);

            Assert.Contains("ghcr.io/acme/api:1.0.0", page.Markup, "The detail must render");
            Assert.IsFalse(HasActionSection(page), "An availability that is not shown must render no action section");
            Assert.AreSequenceEqual(Array.Empty<string>(), GetActionButtonTexts(page), "An availability that is not shown must offer no action button");
        }
    }

    /// <summary>
    /// Verify a failing availability load keeps the action section hidden and logs the failure
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC13")]
    public async Task RuntimeContainerDetailFailedAvailabilityLoadHidesActionSectionAndLogsAsync()
    {
        var exception = new InvalidOperationException("connection to db.internal.test:5432 refused");
        var logger = new TestLogger<RuntimeContainerDetail>();
        var containerActionService = CreateFailingAvailabilityService(exception);
        var testContext = CreateActionContext(containerActionService, CreateViewService(), logger);

        await using (testContext)
        {
            var page = RenderActionPage(testContext);

            await page.WaitForAssertionAsync(() => Assert.IsNotEmpty(logger.Entries, "The failure must be logged"), TimeSpan.FromSeconds(WaitSeconds)).ConfigureAwait(false);

            Assert.IsFalse(HasActionSection(page), "A failed availability load must keep the action section hidden");
            Assert.DoesNotContain("db.internal.test", page.Markup, "The page must not show the exception text");
            UserActionFailureAssert.AssertLogged(logger, "LoadContainerActions", exception);
        }
    }

    /// <summary>
    /// Verify an availability load that ends because the page's services are disposed shows and logs nothing
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC13")]
    public async Task RuntimeContainerDetailDisposedDuringAvailabilityLoadShowsAndLogsNothingAsync()
    {
        var logger = new TestLogger<RuntimeContainerDetail>();
        var containerActionService = CreateFailingAvailabilityService(new ObjectDisposedException("scope"));
        var testContext = CreateActionContext(containerActionService, CreateViewService(), logger);

        await using (testContext)
        {
            var page = RenderActionPage(testContext);

            await WaitForAvailabilityRequestAsync(page, containerActionService).ConfigureAwait(false);

            Assert.IsFalse(HasActionSection(page), "Without an availability the action section must stay hidden");
            Assert.IsEmpty(page.FindAll(".mud-alert-filled-error, .mud-alert-text-error"), "A disposed page must show no error");
            Assert.IsEmpty(logger.Entries, "A disposed page is not a failure and must not be logged");
        }
    }

    /// <summary>
    /// Verify a confirmation dialog that cannot be opened shows a fixed reason, logs the exception and executes nothing
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC13")]
    public async Task RuntimeContainerDetailFailedConfirmationDialogShowsFixedReasonAndLogsAsync()
    {
        var exception = new InvalidOperationException("connection to db.internal.test:5432 refused");
        var logger = new TestLogger<RuntimeContainerDetail>();
        var containerActionService = CreateContainerActionService(CreateAvailability("Running"));
        var testContext = CreateActionContext(containerActionService, CreateViewService(), logger, CreateFailingDialogService(exception));

        await using (testContext)
        {
            var page = RenderActionPage(testContext);

            await ClickActionAsync(page, "Restart").ConfigureAwait(false);

            await page.WaitForAssertionAsync(() => Assert.Contains("Container action failed", page.Markup, "The page must show the failure"), TimeSpan.FromSeconds(WaitSeconds)).ConfigureAwait(false);

            UserActionFailureAssert.AssertShown(page.Markup, "Container action failed: unexpected error", "db.internal.test", "refused");
            UserActionFailureAssert.AssertLogged(logger, "ContainerAction", exception);
            await containerActionService.DidNotReceive()
                                        .ExecuteAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<ContainerActionType>())
                                        .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Verify a confirmation dialog that ends because the page's services are disposed shows and logs nothing and executes nothing
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC13")]
    public async Task RuntimeContainerDetailDisposedDuringConfirmationShowsAndLogsNothingAsync()
    {
        var logger = new TestLogger<RuntimeContainerDetail>();
        var containerActionService = CreateContainerActionService(CreateAvailability("Running"));
        var dialogService = CreateFailingDialogService(new ObjectDisposedException("circuit"));
        var testContext = CreateActionContext(containerActionService, CreateViewService(), logger, dialogService);

        await using (testContext)
        {
            var page = RenderActionPage(testContext);

            await ClickActionAsync(page, "Restart").ConfigureAwait(false);

            await page.WaitForAssertionAsync(() => Assert.IsNotEmpty(dialogService.ReceivedCalls(), "The dialog must have been requested"), TimeSpan.FromSeconds(WaitSeconds)).ConfigureAwait(false);

            Assert.DoesNotContain("Container action failed", page.Markup, "A disposed page must show no failure");
            Assert.IsEmpty(logger.Entries, "A disposed page is not a failure and must not be logged");
            await containerActionService.DidNotReceive()
                                        .ExecuteAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<ContainerActionType>())
                                        .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Verify an action that ends because the page's services are disposed shows and logs nothing and releases the buttons
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC13")]
    public async Task RuntimeContainerDetailDisposedDuringActionShowsAndLogsNothingAsync()
    {
        var logger = new TestLogger<RuntimeContainerDetail>();
        var containerActionService = CreateContainerActionService(CreateAvailability("Running"));
        var testContext = CreateActionContext(containerActionService, CreateViewService(), logger);

        containerActionService.ExecuteAsync(_actionInstanceId, ActionContainerId, ContainerActionType.RestartContainer)
                              .Returns(Task.FromException<ContainerActionRunResultData>(new ObjectDisposedException("scope")));

        await using (testContext)
        {
            var provider = testContext.Render<MudDialogProvider>();
            var page = RenderActionPage(testContext);

            await ConfirmAsync(page, provider, "Restart", "Restart").ConfigureAwait(false);

            await page.WaitForAssertionAsync(() => Assert.IsFalse(FindButton(page, "Restart").HasAttribute("disabled"), "The buttons must be released after the action ended"), TimeSpan.FromSeconds(WaitSeconds)).ConfigureAwait(false);

            Assert.DoesNotContain("Container action failed", page.Markup, "A disposed page must show no failure");
            Assert.IsEmpty(logger.Entries, "A disposed page is not a failure and must not be logged");
            await containerActionService.Received(1)
                                        .ExecuteAsync(_actionInstanceId, ActionContainerId, ContainerActionType.RestartContainer)
                                        .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Verify a failed action shows its outcome message in the error style
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC10")]
    public async Task RuntimeContainerDetailAfterFailedActionShowsOutcomeAsErrorAsync()
    {
        const string failedMessage = "Action 'restart' failed: HTTP 403";
        var containerActionService = CreateContainerActionService(CreateAvailability("Running"));
        var testContext = CreateActionContext(containerActionService, CreateViewService(), new TestLogger<RuntimeContainerDetail>());

        containerActionService.ExecuteAsync(_actionInstanceId, ActionContainerId, ContainerActionType.RestartContainer)
                              .Returns(new ContainerActionRunResultData
                                       {
                                           Status = ContainerActionStatus.Failed,
                                           Message = failedMessage,
                                       });

        await using (testContext)
        {
            var provider = testContext.Render<MudDialogProvider>();
            var page = RenderActionPage(testContext);

            await ConfirmAsync(page, provider, "Restart", "Restart").ConfigureAwait(false);

            await page.WaitForAssertionAsync(() => Assert.Contains(failedMessage, FindActionSection(page).TextContent, "The page must show the outcome"), TimeSpan.FromSeconds(WaitSeconds)).ConfigureAwait(false);

            var outcome = FindActionSection(page).QuerySelectorAll(".mud-alert")
                                                 .Single(alert => alert.TextContent.Contains(failedMessage, StringComparison.Ordinal));

            Assert.Contains("error", outcome.ClassName ?? string.Empty, "A failed outcome must use the error style");
        }
    }

    /// <summary>
    /// Verify the history shows each run status in a chip of its color
    /// </summary>
    /// <param name="status">Run status</param>
    /// <param name="expectedColorClass">Expected chip color class</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC10")]
    [DataRow("Succeeded", "mud-chip-color-success")]
    [DataRow("Failed", "mud-chip-color-error")]
    [DataRow("Running", "mud-chip-color-info")]
    [DataRow("Requested", "mud-chip-color-warning")]
    [DataRow("Cancelled", "mud-chip-color-default")]
    public async Task RuntimeContainerDetailActionHistoryShowsStatusChipInItsColorAsync(string status, string expectedColorClass)
    {
        var viewService = Substitute.For<IApplicationViewService>();
        var containerActionService = CreateContainerActionService(CreateAvailability("Running"));
        var testContext = CreateActionContext(containerActionService, viewService, new TestLogger<RuntimeContainerDetail>());

        viewService.GetRuntimeContainerDetailAsync(_actionInstanceId, ActionContainerId, Arg.Any<CancellationToken>())
                   .Returns(CreateActionDetail([
                                                   new ContainerActionRunViewData
                                                   {
                                                       Action = "Restart",
                                                       Status = status,
                                                       RequestedAtUtc = _requestedAtUtc,
                                                       RequestedBy = "UI",
                                                   }
                                               ]));

        await using (testContext)
        {
            var page = RenderActionPage(testContext);

            await page.WaitForAssertionAsync(() => Assert.HasCount(1, FindActionSection(page).QuerySelectorAll("tbody tr .mud-chip"), "The history must list the run"), TimeSpan.FromSeconds(WaitSeconds)).ConfigureAwait(false);

            var chip = FindActionSection(page).QuerySelector("tbody tr .mud-chip");

            Assert.AreEqual(status, chip?.TextContent.Trim(), "The chip must show the run status");
            Assert.Contains(expectedColorClass, chip?.ClassName ?? string.Empty, $"Status {status} must be shown in the chip color '{expectedColorClass}'");
        }
    }

    /// <summary>
    /// Verify an unknown container shows the not-found panel with a link back to the list instead of the skeleton
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
    public async Task RuntimeContainerDetailUnknownContainerShowsNotFoundPanel()
    {
        var viewService = Substitute.For<IApplicationViewService>();
        var testContext = CreateLoadContext(viewService, new TestLogger<RuntimeContainerDetail>());

        await using (testContext)
        {
            viewService.GetRuntimeContainerDetailAsync(_loadInstanceId, LoadContainerId, Arg.Any<CancellationToken>())
                       .Returns(Task.FromResult<RuntimeContainerDetailViewData?>(null));

            var page = RenderLoadPage(testContext, LoadContainerId);

            await DetailPageAssert.AssertNotFoundAsync(page, "This container is not known on this Docker instance.", "Back to Runtime Containers", "/runtime-containers").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Verify a failed load shows the fixed reason instead of the exception text, shows no skeleton and logs the load failure once
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    public async Task RuntimeContainerDetailLoadFailureShowsFixedErrorAndLogs()
    {
        var exception = new InvalidOperationException("secret text");
        var logger = new TestLogger<RuntimeContainerDetail>();
        var viewService = Substitute.For<IApplicationViewService>();
        var testContext = CreateLoadContext(viewService, logger);

        await using (testContext)
        {
            viewService.GetRuntimeContainerDetailAsync(_loadInstanceId, LoadContainerId, Arg.Any<CancellationToken>())
                       .Returns(Task.FromException<RuntimeContainerDetailViewData?>(exception));

            var page = RenderLoadPage(testContext, LoadContainerId);

            await DetailPageAssert.AssertLoadErrorAsync(page, "Loading the container failed: unexpected error", "secret text").ConfigureAwait(false);
            DetailPageAssert.AssertLoadLoggedOnce(logger, "LoadRuntimeContainerDetail", exception);
        }
    }

    /// <summary>
    /// Verify a route change after a failed load clears the alert at once, while the new load is pending, and the loaded detail shows no alert
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC4")]
    public async Task RuntimeContainerDetailRouteChangeAfterLoadFailureClearsAlert()
    {
        var exception = new InvalidOperationException("secret text");
        var pending = new TaskCompletionSource<RuntimeContainerDetailViewData?>();
        var logger = new TestLogger<RuntimeContainerDetail>();
        var viewService = Substitute.For<IApplicationViewService>();
        var testContext = CreateLoadContext(viewService, logger);

        await using (testContext)
        {
            viewService.GetRuntimeContainerDetailAsync(_loadInstanceId, LoadContainerId, Arg.Any<CancellationToken>())
                       .Returns(Task.FromException<RuntimeContainerDetailViewData?>(exception));
            viewService.GetRuntimeContainerDetailAsync(_loadInstanceId, SecondContainerId, Arg.Any<CancellationToken>())
                       .Returns(pending.Task);

            var page = RenderLoadPage(testContext, LoadContainerId);

            await DetailPageAssert.AssertLoadErrorAsync(page, "Loading the container failed: unexpected error", "secret text").ConfigureAwait(false);

            page.Render(parameters => parameters.Add(component => component.DockerInstanceId, _loadInstanceId)
                                                .Add(component => component.ContainerId, SecondContainerId));

            DetailPageAssert.AssertLoading(page);

            pending.SetResult(CreateLoadDetail(SecondContainerId, "second-api"));

            await DetailPageAssert.AssertLoadedAsync(page, "second-api", "This container is not known on this Docker instance.").ConfigureAwait(false);
            DetailPageAssert.AssertLoadLoggedOnce(logger, "LoadRuntimeContainerDetail", exception);
        }
    }

    /// <summary>
    /// Verify a reload after a tag selection that answers no detail replaces the skeleton with the not-found panel
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC5")]
    public async Task RuntimeContainerDetailSelectReloadWithoutDetailShowsNotFoundPanel()
    {
        var viewService = Substitute.For<IApplicationViewService>();
        var testContext = CreateLoadContext(viewService, new TestLogger<RuntimeContainerDetail>());

        await using (testContext)
        {
            viewService.GetRuntimeContainerDetailAsync(_loadInstanceId, LoadContainerId, Arg.Any<CancellationToken>())
                       .Returns(CreateLoadDetail(LoadContainerId, "load-api"));

            var page = RenderLoadPage(testContext, LoadContainerId);

            await DetailPageAssert.AssertLoadedAsync(page, "load-api", "This container is not known on this Docker instance.").ConfigureAwait(false);

            viewService.GetRuntimeContainerDetailAsync(_loadInstanceId, LoadContainerId, Arg.Any<CancellationToken>())
                       .Returns(Task.FromResult<RuntimeContainerDetailViewData?>(null));

            await FindButton(page, "Select").ClickAsync().ConfigureAwait(false);

            await DetailPageAssert.AssertNotFoundAsync(page, "This container is not known on this Docker instance.", "Back to Runtime Containers", "/runtime-containers").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Verify a pending load shows the skeleton only, and the loaded detail replaces it
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC3")]
    public async Task RuntimeContainerDetailPendingLoadShowsSkeletonOnly()
    {
        var pending = new TaskCompletionSource<RuntimeContainerDetailViewData?>();
        var viewService = Substitute.For<IApplicationViewService>();
        var testContext = CreateLoadContext(viewService, new TestLogger<RuntimeContainerDetail>());

        await using (testContext)
        {
            viewService.GetRuntimeContainerDetailAsync(_loadInstanceId, LoadContainerId, Arg.Any<CancellationToken>())
                       .Returns(pending.Task);

            var page = RenderLoadPage(testContext, LoadContainerId);

            DetailPageAssert.AssertLoading(page);

            pending.SetResult(CreateLoadDetail(LoadContainerId, "load-api"));

            await DetailPageAssert.AssertLoadedAsync(page, "load-api", "This container is not known on this Docker instance.").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Verify a reload after a tag selection that fails keeps the action's prefix, shows no skeleton or not-found panel and logs the action once
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC5")]
    public async Task RuntimeContainerDetailSelectReloadFailureKeepsActionPrefix()
    {
        var exception = new InvalidOperationException("secret text");
        var logger = new TestLogger<RuntimeContainerDetail>();
        var viewService = Substitute.For<IApplicationViewService>();
        var testContext = CreateLoadContext(viewService, logger);

        await using (testContext)
        {
            viewService.GetRuntimeContainerDetailAsync(_loadInstanceId, LoadContainerId, Arg.Any<CancellationToken>())
                       .Returns(CreateLoadDetail(LoadContainerId, "load-api"));

            var page = RenderLoadPage(testContext, LoadContainerId);

            await DetailPageAssert.AssertLoadedAsync(page, "load-api", "This container is not known on this Docker instance.").ConfigureAwait(false);

            viewService.GetRuntimeContainerDetailAsync(_loadInstanceId, LoadContainerId, Arg.Any<CancellationToken>())
                       .Returns(Task.FromException<RuntimeContainerDetailViewData?>(exception));

            await FindButton(page, "Select").ClickAsync().ConfigureAwait(false);

            await page.WaitForAssertionAsync(() => Assert.Contains("Saving the tag selection failed: unexpected error", page.Markup, "The reload failure must carry the action's prefix"), TimeSpan.FromSeconds(WaitSeconds)).ConfigureAwait(false);

            Assert.DoesNotContain("Loading the container failed", page.Markup, "The reload failure must not carry the load prefix");
            Assert.DoesNotContain("secret text", page.Markup, "The exception text must not be shown");
            Assert.IsEmpty(page.FindAll(".mud-skeleton"), "The failed reload must not show the skeleton");
            Assert.DoesNotContain("This container is not known on this Docker instance.", page.Markup, "The failed reload must not show the not-found message");
            DetailPageAssert.AssertLoadLoggedOnce(logger, "SaveTagSelection", exception);
        }
    }

    /// <summary>
    /// Verify a route change while the new load is pending shows the skeleton only: no name of the earlier container and no action section
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
    public async Task RuntimeContainerDetailRouteChangeWhileLoadPendingShowsSkeletonWithoutActions()
    {
        var pending = new TaskCompletionSource<RuntimeContainerDetailViewData?>();
        var viewService = CreateRouteViewService();
        var containerActionService = CreateRouteActionService(CreateAvailability("Running"), CreateAvailability("Running"));
        var testContext = CreateRouteContext(viewService, containerActionService, Substitute.For<IRuntimeContainerTagSelectionService>(), new TestLogger<RuntimeContainerDetail>());

        viewService.GetRuntimeContainerDetailAsync(_routeInstanceId, RouteContainerB, Arg.Any<CancellationToken>())
                   .Returns(pending.Task);

        await using (testContext)
        {
            var page = RenderRoutePage(testContext, RouteContainerA);

            await WaitForOfferedActionAsync(page, "Stop").ConfigureAwait(false);

            MoveRoute(page, RouteContainerB);

            DetailPageAssert.AssertLoading(page);
            Assert.DoesNotContain(RouteNameA, page.Markup, "The name of the earlier container must not be shown while the new load is pending");
            Assert.IsFalse(HasActionSection(page), "The action section of the earlier container must not be shown while the new load is pending");
            Assert.AreSequenceEqual(Array.Empty<string>(), GetActionButtonTexts(page), "No action button of the earlier container may be offered while the new load is pending");
        }
    }

    /// <summary>
    /// Verify a route change after an action shows no outcome of the earlier container's action
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
    public async Task RuntimeContainerDetailRouteChangeAfterActionClearsOutcome()
    {
        var pending = new TaskCompletionSource<RuntimeContainerDetailViewData?>();
        var viewService = CreateRouteViewService();
        var containerActionService = CreateRouteActionService(CreateAvailability("Running"), CreateAvailability("Running"));
        var testContext = CreateRouteContext(viewService, containerActionService, Substitute.For<IRuntimeContainerTagSelectionService>(), new TestLogger<RuntimeContainerDetail>());

        containerActionService.ExecuteAsync(_routeInstanceId, RouteContainerA, ContainerActionType.RestartContainer)
                              .Returns(new ContainerActionRunResultData
                                       {
                                           Status = ContainerActionStatus.Succeeded,
                                           Message = RestartSucceededMessage,
                                       });
        viewService.GetRuntimeContainerDetailAsync(_routeInstanceId, RouteContainerB, Arg.Any<CancellationToken>())
                   .Returns(pending.Task);

        await using (testContext)
        {
            var provider = testContext.Render<MudDialogProvider>();
            var page = RenderRoutePage(testContext, RouteContainerA);

            await ConfirmAsync(page, provider, "Restart", "Restart").ConfigureAwait(false);

            await page.WaitForAssertionAsync(() => Assert.Contains(RestartSucceededMessage, page.Markup, "The outcome of the action must be shown"), TimeSpan.FromSeconds(WaitSeconds)).ConfigureAwait(false);

            MoveRoute(page, RouteContainerB);

            DetailPageAssert.AssertLoading(page);
            Assert.DoesNotContain(RestartSucceededMessage, page.Markup, "The outcome of the earlier container's action must not be shown after the route change");
        }
    }

    /// <summary>
    /// Verify a detail answer of the earlier route that arrives after the route change is dropped
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    public async Task RuntimeContainerDetailLateDetailOfPreviousRouteIsDropped()
    {
        var pendingA = new TaskCompletionSource<RuntimeContainerDetailViewData?>();
        var pendingB = new TaskCompletionSource<RuntimeContainerDetailViewData?>();
        var viewService = CreateRouteViewService();
        var testContext = CreateRouteContext(viewService, CreateRouteActionService(new ContainerActionAvailabilityData(), new ContainerActionAvailabilityData()), Substitute.For<IRuntimeContainerTagSelectionService>(), new TestLogger<RuntimeContainerDetail>());

        viewService.GetRuntimeContainerDetailAsync(_routeInstanceId, RouteContainerA, Arg.Any<CancellationToken>())
                   .Returns(pendingA.Task);
        viewService.GetRuntimeContainerDetailAsync(_routeInstanceId, RouteContainerB, Arg.Any<CancellationToken>())
                   .Returns(pendingB.Task);

        await using (testContext)
        {
            var page = RenderRoutePage(testContext, RouteContainerA);

            MoveRoute(page, RouteContainerB);
            pendingA.SetResult(CreateRouteDetail(RouteContainerA, RouteNameA));

            await SettleAsync(page).ConfigureAwait(false);

            DetailPageAssert.AssertLoading(page);
            Assert.DoesNotContain(RouteNameA, page.Markup, "The late detail of the earlier container must not be shown");

            pendingB.SetResult(CreateRouteDetail(RouteContainerB, RouteNameB));

            await DetailPageAssert.AssertLoadedAsync(page, RouteNameB, "This container is not known on this Docker instance.").ConfigureAwait(false);

            Assert.DoesNotContain(RouteNameA, page.Markup, "The shown detail must stay the one of the current container");
        }
    }

    /// <summary>
    /// Verify a not-found answer of the earlier route that arrives after the route change is dropped
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    public async Task RuntimeContainerDetailLateNotFoundOfPreviousRouteIsDropped()
    {
        var pendingA = new TaskCompletionSource<RuntimeContainerDetailViewData?>();
        var pendingB = new TaskCompletionSource<RuntimeContainerDetailViewData?>();
        var viewService = CreateRouteViewService();
        var testContext = CreateRouteContext(viewService, CreateRouteActionService(new ContainerActionAvailabilityData(), new ContainerActionAvailabilityData()), Substitute.For<IRuntimeContainerTagSelectionService>(), new TestLogger<RuntimeContainerDetail>());

        viewService.GetRuntimeContainerDetailAsync(_routeInstanceId, RouteContainerA, Arg.Any<CancellationToken>())
                   .Returns(pendingA.Task);
        viewService.GetRuntimeContainerDetailAsync(_routeInstanceId, RouteContainerB, Arg.Any<CancellationToken>())
                   .Returns(pendingB.Task);

        await using (testContext)
        {
            var page = RenderRoutePage(testContext, RouteContainerA);

            MoveRoute(page, RouteContainerB);
            pendingA.SetResult(null);

            await SettleAsync(page).ConfigureAwait(false);

            DetailPageAssert.AssertLoading(page);

            pendingB.SetResult(CreateRouteDetail(RouteContainerB, RouteNameB));

            await DetailPageAssert.AssertLoadedAsync(page, RouteNameB, "This container is not known on this Docker instance.").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Verify an availability answer of the earlier route that arrives after the route change is dropped
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC3")]
    public async Task RuntimeContainerDetailLateAvailabilityOfPreviousRouteIsDropped()
    {
        var pendingA = new TaskCompletionSource<ContainerActionAvailabilityData>();
        var viewService = CreateRouteViewService();
        var containerActionService = CreateRouteActionService(CreateAvailability("Exited"), CreateAvailability("Exited"));
        var testContext = CreateRouteContext(viewService, containerActionService, Substitute.For<IRuntimeContainerTagSelectionService>(), new TestLogger<RuntimeContainerDetail>());

        containerActionService.GetAvailabilityAsync(_routeInstanceId, RouteContainerA, Arg.Any<CancellationToken>())
                              .Returns(pendingA.Task);

        await using (testContext)
        {
            var page = RenderRoutePage(testContext, RouteContainerA);

            MoveRoute(page, RouteContainerB);

            await WaitForOfferedActionAsync(page, "Start").ConfigureAwait(false);

            pendingA.SetResult(CreateAvailability("InProgress"));

            await SettleAsync(page).ConfigureAwait(false);

            Assert.AreEqual("Start", string.Join(",", GetActionButtonTexts(page)), "The section must offer the actions of the current container only");
            Assert.DoesNotContain("An action is in progress for this container.", page.Markup, "The message of the earlier container's availability must not be shown");
        }
    }

    /// <summary>
    /// Verify an availability failure of the earlier route that arrives after the route change leaves the current container's availability shown
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC3")]
    public async Task RuntimeContainerDetailLateAvailabilityFailureOfPreviousRouteKeepsCurrentActionsAsync()
    {
        var pendingA = new TaskCompletionSource<ContainerActionAvailabilityData>();
        var viewService = CreateRouteViewService();
        var containerActionService = CreateRouteActionService(CreateAvailability("Exited"), CreateAvailability("Exited"));
        var testContext = CreateRouteContext(viewService, containerActionService, Substitute.For<IRuntimeContainerTagSelectionService>(), new TestLogger<RuntimeContainerDetail>());

        containerActionService.GetAvailabilityAsync(_routeInstanceId, RouteContainerA, Arg.Any<CancellationToken>())
                              .Returns(pendingA.Task);

        await using (testContext)
        {
            var page = RenderRoutePage(testContext, RouteContainerA);

            MoveRoute(page, RouteContainerB);

            await WaitForOfferedActionAsync(page, "Start").ConfigureAwait(false);

            pendingA.SetException(new InvalidOperationException("late failure of the earlier container"));

            await SettleAsync(page).ConfigureAwait(false);

            Assert.AreEqual("Start", string.Join(",", GetActionButtonTexts(page)), "A late failure of the earlier container must not hide the actions of the current container");
        }
    }

    /// <summary>
    /// Verify a re-render with the same route while the reload is pending keeps the shown detail
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC6")]
    public async Task RuntimeContainerDetailSameRouteReloadKeepsDetailShown()
    {
        var pending = new TaskCompletionSource<RuntimeContainerDetailViewData?>();
        var viewService = CreateRouteViewService();
        var testContext = CreateRouteContext(viewService, CreateRouteActionService(new ContainerActionAvailabilityData(), new ContainerActionAvailabilityData()), Substitute.For<IRuntimeContainerTagSelectionService>(), new TestLogger<RuntimeContainerDetail>());

        await using (testContext)
        {
            var page = RenderRoutePage(testContext, RouteContainerA);

            await DetailPageAssert.AssertLoadedAsync(page, RouteNameA, "This container is not known on this Docker instance.").ConfigureAwait(false);

            viewService.GetRuntimeContainerDetailAsync(_routeInstanceId, RouteContainerA, Arg.Any<CancellationToken>())
                       .Returns(pending.Task);

            MoveRoute(page, RouteContainerA);

            Assert.Contains(RouteNameA, page.Markup, "The shown detail must stay while the same route reloads");

            pending.SetResult(CreateRouteDetail(RouteContainerA, RouteNameA));

            await DetailPageAssert.AssertLoadedAsync(page, RouteNameA, "This container is not known on this Docker instance.").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Verify a confirmation of a dialog opened on the earlier container and confirmed after the route change executes nothing
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC4")]
    public async Task RuntimeContainerDetailConfirmationAfterRouteChangeExecutesNothingAsync()
    {
        var viewService = CreateRouteViewService();
        var containerActionService = CreateRouteActionService(CreateAvailability("Running"), CreateAvailability("Running"));
        var testContext = CreateRouteContext(viewService, containerActionService, Substitute.For<IRuntimeContainerTagSelectionService>(), new TestLogger<RuntimeContainerDetail>());

        await using (testContext)
        {
            var provider = testContext.Render<MudDialogProvider>();
            var page = RenderRoutePage(testContext, RouteContainerA);

            await ClickActionAsync(page, "Stop").ConfigureAwait(false);

            await provider.WaitForAssertionAsync(() => Assert.IsNotEmpty(provider.FindAll(".mud-dialog"), "The confirmation dialog must open"), TimeSpan.FromSeconds(WaitSeconds)).ConfigureAwait(false);

            MoveRoute(page, RouteContainerB);

            await provider.FindAll(".mud-dialog button")
                          .Single(button => button.TextContent.Trim() == "Stop")
                          .ClickAsync()
                          .ConfigureAwait(false);

            await provider.WaitForAssertionAsync(() => Assert.IsEmpty(provider.FindAll(".mud-dialog"), "The confirmation must close the dialog"), TimeSpan.FromSeconds(WaitSeconds)).ConfigureAwait(false);
            await SettleAsync(page).ConfigureAwait(false);

            await containerActionService.DidNotReceive()
                                        .ExecuteAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<ContainerActionType>())
                                        .ConfigureAwait(false);
            Assert.IsEmpty(page.FindAll(".mud-alert"), "A confirmation after the route change must show no outcome or error");
        }
    }

    /// <summary>
    /// Verify an action of a page whose shown detail belongs to another container than its route opens no dialog and executes nothing
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC4")]
    public async Task RuntimeContainerDetailConfirmationForDetailOfOtherContainerExecutesNothingAsync()
    {
        var viewService = CreateRouteViewService();
        var containerActionService = CreateRouteActionService(CreateAvailability("Running"), CreateAvailability("Running"));
        var testContext = CreateRouteContext(viewService, containerActionService, Substitute.For<IRuntimeContainerTagSelectionService>(), new TestLogger<RuntimeContainerDetail>());

        viewService.GetRuntimeContainerDetailAsync(_routeInstanceId, RouteContainerA, Arg.Any<CancellationToken>())
                   .Returns(CreateRouteDetail(RouteContainerA, RouteNameA, RouteContainerB));

        await using (testContext)
        {
            var provider = testContext.Render<MudDialogProvider>();
            var page = RenderRoutePage(testContext, RouteContainerA);

            await ClickActionAsync(page, "Stop").ConfigureAwait(false);
            await SettleAsync(page).ConfigureAwait(false);

            foreach (var dialogButton in provider.FindAll(".mud-dialog button").Where(button => button.TextContent.Trim() == "Stop"))
            {
                await dialogButton.ClickAsync().ConfigureAwait(false);
            }

            await SettleAsync(page).ConfigureAwait(false);

            Assert.IsEmpty(provider.FindAll(".mud-dialog"), "A detail of another container must not open the confirmation dialog");
            await containerActionService.DidNotReceive()
                                        .ExecuteAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<ContainerActionType>())
                                        .ConfigureAwait(false);
            Assert.IsEmpty(page.FindAll(".mud-alert"), "A refused confirmation must show no outcome or error");
        }
    }

    /// <summary>
    /// Verify an action that ends after the route change shows no outcome on the new container's page and leaves its buttons disabled without progress meanwhile
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC5")]
    public async Task RuntimeContainerDetailActionEndingAfterRouteChangeShowsNoOutcomeAsync()
    {
        var pending = new TaskCompletionSource<ContainerActionRunResultData>(TaskCreationOptions.RunContinuationsAsynchronously);
        var viewService = CreateRouteViewService();
        var containerActionService = CreateRouteActionService(CreateAvailability("Running"), CreateAvailability("Running"));
        var testContext = CreateRouteContext(viewService, containerActionService, Substitute.For<IRuntimeContainerTagSelectionService>(), new TestLogger<RuntimeContainerDetail>());

        containerActionService.ExecuteAsync(_routeInstanceId, RouteContainerA, ContainerActionType.StopContainer)
                              .Returns(pending.Task);

        await using (testContext)
        {
            var provider = testContext.Render<MudDialogProvider>();
            var page = RenderRoutePage(testContext, RouteContainerA);

            await ConfirmAsync(page, provider, "Stop", "Stop").ConfigureAwait(false);

            await page.WaitForAssertionAsync(() => Assert.IsTrue(FindButton(page, "Stop").HasAttribute("disabled"), "The button must be disabled while the action runs"), TimeSpan.FromSeconds(WaitSeconds)).ConfigureAwait(false);

            MoveRoute(page, RouteContainerB);

            await page.WaitForAssertionAsync(() => Assert.Contains(RouteNameB, page.Markup, "The detail of the new container must be shown"), TimeSpan.FromSeconds(WaitSeconds)).ConfigureAwait(false);
            await WaitForOfferedActionAsync(page, "Stop").ConfigureAwait(false);

            var stopButton = FindButton(page, "Stop");

            Assert.IsTrue(stopButton.HasAttribute("disabled"), "The new container's button must be disabled while the earlier container's action runs");
            Assert.IsNull(stopButton.QuerySelector(".mud-progress-circular"), "The new container's button must not show the progress of the earlier container's action");

            pending.SetResult(new ContainerActionRunResultData
                              {
                                  Status = ContainerActionStatus.Succeeded,
                                  Message = "Action 'stop' on 'api' executed successfully",
                              });

            await page.WaitForAssertionAsync(() => Assert.IsFalse(FindButton(page, "Stop").HasAttribute("disabled"), "The buttons must be released after the action ended"), TimeSpan.FromSeconds(WaitSeconds)).ConfigureAwait(false);
            await SettleAsync(page).ConfigureAwait(false);

            await containerActionService.Received(1)
                                        .ExecuteAsync(_routeInstanceId, RouteContainerA, ContainerActionType.StopContainer)
                                        .ConfigureAwait(false);
            Assert.IsEmpty(page.FindAll(".mud-alert"), "The outcome of the earlier container's action must not be shown on the new container's page");
            Assert.DoesNotContain("executed successfully", page.Markup, "The outcome text of the earlier container's action must not be shown");
        }
    }

    /// <summary>
    /// Verify an action that fails after the route change shows no error on the new container's page
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC5")]
    public async Task RuntimeContainerDetailActionFailingAfterRouteChangeShowsNoErrorAsync()
    {
        var pending = new TaskCompletionSource<ContainerActionRunResultData>(TaskCreationOptions.RunContinuationsAsynchronously);
        var logger = new TestLogger<RuntimeContainerDetail>();
        var viewService = CreateRouteViewService();
        var containerActionService = CreateRouteActionService(CreateAvailability("Running"), CreateAvailability("Running"));
        var testContext = CreateRouteContext(viewService, containerActionService, Substitute.For<IRuntimeContainerTagSelectionService>(), logger);

        containerActionService.ExecuteAsync(_routeInstanceId, RouteContainerA, ContainerActionType.StopContainer)
                              .Returns(pending.Task);

        await using (testContext)
        {
            var provider = testContext.Render<MudDialogProvider>();
            var page = RenderRoutePage(testContext, RouteContainerA);

            await ConfirmAsync(page, provider, "Stop", "Stop").ConfigureAwait(false);

            await page.WaitForAssertionAsync(() => Assert.IsTrue(FindButton(page, "Stop").HasAttribute("disabled"), "The button must be disabled while the action runs"), TimeSpan.FromSeconds(WaitSeconds)).ConfigureAwait(false);

            MoveRoute(page, RouteContainerB);

            await page.WaitForAssertionAsync(() => Assert.Contains(RouteNameB, page.Markup, "The detail of the new container must be shown"), TimeSpan.FromSeconds(WaitSeconds)).ConfigureAwait(false);

            pending.SetException(new InvalidOperationException("connection to db.internal.test:5432 refused"));

            await page.WaitForAssertionAsync(() => Assert.IsNotEmpty(logger.Entries, "The failure must be logged"), TimeSpan.FromSeconds(WaitSeconds)).ConfigureAwait(false);
            await SettleAsync(page).ConfigureAwait(false);

            await containerActionService.Received(1)
                                        .ExecuteAsync(_routeInstanceId, RouteContainerA, ContainerActionType.StopContainer)
                                        .ConfigureAwait(false);
            Assert.DoesNotContain("Container action failed", page.Markup, "The failure of the earlier container's action must not be shown on the new container's page");
            Assert.IsEmpty(page.FindAll(".mud-alert"), "No alert may be shown on the new container's page");
        }
    }

    /// <summary>
    /// Verify a confirmation dialog that fails after the route change shows no error on the new container's page
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC5")]
    public async Task RuntimeContainerDetailConfirmationFailingAfterRouteChangeShowsNoErrorAsync()
    {
        var pendingResult = new TaskCompletionSource<DialogResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var logger = new TestLogger<RuntimeContainerDetail>();
        var dialogReference = Substitute.For<IDialogReference>();
        var dialogService = Substitute.For<IDialogService>();
        var viewService = CreateRouteViewService();
        var containerActionService = CreateRouteActionService(CreateAvailability("Running"), CreateAvailability("Running"));
        var testContext = CreateRouteContext(viewService, containerActionService, Substitute.For<IRuntimeContainerTagSelectionService>(), logger, dialogService);

        dialogReference.Result.Returns(pendingResult.Task);
        dialogService.ShowAsync<ContainerActionConfirmation>(Arg.Any<string?>(), Arg.Any<DialogParameters>())
                     .Returns(Task.FromResult(dialogReference));

        await using (testContext)
        {
            var page = RenderRoutePage(testContext, RouteContainerA);

            await ClickActionAsync(page, "Stop").ConfigureAwait(false);

            await page.WaitForAssertionAsync(() => Assert.IsNotEmpty(dialogService.ReceivedCalls(), "The dialog must have been requested"), TimeSpan.FromSeconds(WaitSeconds)).ConfigureAwait(false);

            MoveRoute(page, RouteContainerB);

            await page.WaitForAssertionAsync(() => Assert.Contains(RouteNameB, page.Markup, "The detail of the new container must be shown"), TimeSpan.FromSeconds(WaitSeconds)).ConfigureAwait(false);

            pendingResult.SetException(new InvalidOperationException("connection to db.internal.test:5432 refused"));

            await page.WaitForAssertionAsync(() => Assert.IsNotEmpty(logger.Entries, "The failure must be logged"), TimeSpan.FromSeconds(WaitSeconds)).ConfigureAwait(false);
            await SettleAsync(page).ConfigureAwait(false);

            Assert.DoesNotContain("Container action failed", page.Markup, "The failure of the earlier container's confirmation must not be shown on the new container's page");
            Assert.IsEmpty(page.FindAll(".mud-alert"), "No alert may be shown on the new container's page");
        }
    }

    /// <summary>
    /// Verify a tag selection that fails after the route change shows no error on the new container's page
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC7")]
    public async Task RuntimeContainerDetailSelectionFailingAfterRouteChangeShowsNoErrorAsync()
    {
        var pending = new TaskCompletionSource();
        var logger = new TestLogger<RuntimeContainerDetail>();
        var tagSelectionService = Substitute.For<IRuntimeContainerTagSelectionService>();
        var testContext = CreateRouteContext(CreateRouteViewService(), CreateRouteActionService(new ContainerActionAvailabilityData(), new ContainerActionAvailabilityData()), tagSelectionService, logger);

        tagSelectionService.SaveSelectionAsync(_routeInstanceId, RouteContainerA, Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                           .Returns(pending.Task);

        await using (testContext)
        {
            var page = RenderRoutePage(testContext, RouteContainerA);

            await page.WaitForAssertionAsync(() => Assert.Contains(RouteNameA, page.Markup, "The detail of the first container must be shown"), TimeSpan.FromSeconds(WaitSeconds)).ConfigureAwait(false);

            var clickTask = FindButton(page, "Select").ClickAsync();

            await page.WaitForAssertionAsync(() => Assert.IsNotEmpty(tagSelectionService.ReceivedCalls(), "The selection must be sent"), TimeSpan.FromSeconds(WaitSeconds)).ConfigureAwait(false);

            MoveRoute(page, RouteContainerB);

            pending.SetException(new InvalidOperationException("connection to db.internal.test:5432 refused"));

            await clickTask.ConfigureAwait(false);
            await page.WaitForAssertionAsync(() => Assert.IsNotEmpty(logger.Entries, "The failure must be logged"), TimeSpan.FromSeconds(WaitSeconds)).ConfigureAwait(false);
            await SettleAsync(page).ConfigureAwait(false);

            await tagSelectionService.Received(1)
                                     .SaveSelectionAsync(_routeInstanceId, RouteContainerA, Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                                     .ConfigureAwait(false);
            Assert.DoesNotContain("Saving the tag selection failed", page.Markup, "The failure of the earlier container's selection must not be shown on the new container's page");
            Assert.IsEmpty(page.FindAll(".mud-alert"), "No alert may be shown on the new container's page");
        }
    }

    /// <summary>
    /// Verify a tag selection clear that fails after the route change shows no error on the new container's page
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC7")]
    public async Task RuntimeContainerDetailClearSelectionFailingAfterRouteChangeShowsNoErrorAsync()
    {
        var pending = new TaskCompletionSource();
        var logger = new TestLogger<RuntimeContainerDetail>();
        var tagSelectionService = Substitute.For<IRuntimeContainerTagSelectionService>();
        var testContext = CreateRouteContext(CreateRouteViewService(), CreateRouteActionService(new ContainerActionAvailabilityData(), new ContainerActionAvailabilityData()), tagSelectionService, logger);

        tagSelectionService.ClearSelectionAsync(_routeInstanceId, RouteContainerA, Arg.Any<CancellationToken>())
                           .Returns(pending.Task);

        await using (testContext)
        {
            var page = RenderRoutePage(testContext, RouteContainerA);

            await page.WaitForAssertionAsync(() => Assert.Contains(RouteNameA, page.Markup, "The detail of the first container must be shown"), TimeSpan.FromSeconds(WaitSeconds)).ConfigureAwait(false);

            var clickTask = FindButton(page, "Clear selection").ClickAsync();

            await page.WaitForAssertionAsync(() => Assert.IsNotEmpty(tagSelectionService.ReceivedCalls(), "The clear must be sent"), TimeSpan.FromSeconds(WaitSeconds)).ConfigureAwait(false);

            MoveRoute(page, RouteContainerB);

            pending.SetException(new InvalidOperationException("connection to db.internal.test:5432 refused"));

            await clickTask.ConfigureAwait(false);
            await page.WaitForAssertionAsync(() => Assert.IsNotEmpty(logger.Entries, "The failure must be logged"), TimeSpan.FromSeconds(WaitSeconds)).ConfigureAwait(false);
            await SettleAsync(page).ConfigureAwait(false);

            await tagSelectionService.Received(1)
                                     .ClearSelectionAsync(_routeInstanceId, RouteContainerA, Arg.Any<CancellationToken>())
                                     .ConfigureAwait(false);
            Assert.DoesNotContain("Clearing the tag selection failed", page.Markup, "The failure of the earlier container's clear must not be shown on the new container's page");
            Assert.IsEmpty(page.FindAll(".mud-alert"), "No alert may be shown on the new container's page");
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
        testContext.AddHiddenContainerActions();

        return testContext;
    }

    /// <summary>
    /// Create the availability of a scenario
    /// </summary>
    /// <param name="scenario">Scenario name</param>
    /// <returns>Availability</returns>
    private static ContainerActionAvailabilityData CreateAvailability(string scenario)
    {
        var availability = new ContainerActionAvailabilityData
                           {
                               IsShown = true,
                               IsCurrent = scenario != "NotCurrent",
                               IsSupported = scenario != "Unsupported",
                               IsInProgress = scenario == "InProgress",
                               EndpointId = "7",
                           };

        switch (scenario)
        {
            case "Running":
                {
                    availability.OfferedActions = [ContainerActionType.StopContainer, ContainerActionType.RestartContainer];
                }
                break;

            case "Exited":
                {
                    availability.OfferedActions = [ContainerActionType.StartContainer];
                }
                break;

            case "NotCurrent":
                {
                    availability.Message = "This container is not in the latest runtime scan of 'Production'; no actions are available.";
                }
                break;

            case "Unsupported":
                {
                    availability.Message = "Portainer environment 8 not found; check Portainer:EndpointId of Docker instance 'Production'";
                }
                break;

            case "Dead":
                {
                    availability.OfferedActions = [];
                }
                break;

            default:
                {
                    availability.OfferedActions = [ContainerActionType.StopContainer, ContainerActionType.RestartContainer];
                    availability.Message = "An action is in progress for this container.";
                }
                break;
        }

        return availability;
    }

    /// <summary>
    /// Create a container action service substitute answering the given availability
    /// </summary>
    /// <param name="availability">Availability</param>
    /// <returns>Container action service substitute</returns>
    private static IContainerActionService CreateContainerActionService(ContainerActionAvailabilityData availability)
    {
        var containerActionService = Substitute.For<IContainerActionService>();

        containerActionService.GetAvailabilityAsync(_actionInstanceId, ActionContainerId, Arg.Any<CancellationToken>())
                              .Returns(availability);

        return containerActionService;
    }

    /// <summary>
    /// Create a container action service substitute whose availability check throws the given exception
    /// </summary>
    /// <param name="exception">Exception of the availability check</param>
    /// <returns>Container action service substitute</returns>
    private static IContainerActionService CreateFailingAvailabilityService(Exception exception)
    {
        var containerActionService = Substitute.For<IContainerActionService>();

        containerActionService.GetAvailabilityAsync(_actionInstanceId, ActionContainerId, Arg.Any<CancellationToken>())
                              .Returns(Task.FromException<ContainerActionAvailabilityData>(exception));

        return containerActionService;
    }

    /// <summary>
    /// Create a dialog service substitute whose dialogs cannot be opened
    /// </summary>
    /// <param name="exception">Exception of opening a dialog</param>
    /// <returns>Dialog service substitute</returns>
    private static IDialogService CreateFailingDialogService(Exception exception)
    {
        var dialogService = Substitute.For<IDialogService>();

        dialogService.ShowAsync<ContainerActionConfirmation>(Arg.Any<string?>(), Arg.Any<DialogParameters>())
                     .Returns(Task.FromException<IDialogReference>(exception));

        return dialogService;
    }

    /// <summary>
    /// Check whether the page renders the Container actions section
    /// </summary>
    /// <param name="page">Rendered page</param>
    /// <returns>True when the section is rendered</returns>
    private static bool HasActionSection(IRenderedComponent<RuntimeContainerDetail> page)
    {
        return page.FindAll(".section-paper h5")
                   .Any(heading => heading.TextContent.Trim() == "Container actions");
    }

    /// <summary>
    /// Wait until the page asked for the availability of the container actions
    /// </summary>
    /// <param name="page">Rendered page</param>
    /// <param name="containerActionService">Container action service substitute</param>
    /// <returns>Task</returns>
    private static async Task WaitForAvailabilityRequestAsync(IRenderedComponent<RuntimeContainerDetail> page, IContainerActionService containerActionService)
    {
        await page.WaitForAssertionAsync(() => Assert.ContainsSingle(call => call.GetMethodInfo().Name == nameof(IContainerActionService.GetAvailabilityAsync),
                                                                     containerActionService.ReceivedCalls(),
                                                                     "The page must ask for the availability once"),
                                         TimeSpan.FromSeconds(WaitSeconds)).ConfigureAwait(false);
    }

    /// <summary>
    /// Click an action button of the page once it is offered
    /// </summary>
    /// <param name="page">Rendered page</param>
    /// <param name="actionText">Text of the page's action button</param>
    /// <returns>Task</returns>
    private static async Task ClickActionAsync(IRenderedComponent<RuntimeContainerDetail> page, string actionText)
    {
        await page.WaitForAssertionAsync(() => Assert.ContainsSingle(button => button.TextContent.Trim() == actionText, page.FindAll("button"), $"The page must offer {actionText}"),
                                         TimeSpan.FromSeconds(WaitSeconds)).ConfigureAwait(false);

        await FindButton(page, actionText).ClickAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Create the detail of the container action tests
    /// </summary>
    /// <param name="history">Action history</param>
    /// <returns>Detail</returns>
    private static RuntimeContainerDetailViewData CreateActionDetail(IReadOnlyList<ContainerActionRunViewData> history)
    {
        return new RuntimeContainerDetailViewData
               {
                   DockerInstanceId = _actionInstanceId,
                   ContainerId = ActionContainerId,
                   ContainerName = "api",
                   DockerInstanceName = "Production",
                   ImageReference = "ghcr.io/acme/api:1.0.0",
                   CurrentTag = "1.0.0",
                   RuntimeStatus = "Running",
                   ActionHistory = history,
               };
    }

    /// <summary>
    /// Create a view service substitute answering the detail of the container action tests without history
    /// </summary>
    /// <returns>View service substitute</returns>
    private static IApplicationViewService CreateViewService()
    {
        var viewService = Substitute.For<IApplicationViewService>();

        viewService.GetRuntimeContainerDetailAsync(_actionInstanceId, ActionContainerId, Arg.Any<CancellationToken>())
                   .Returns(CreateActionDetail([]));

        return viewService;
    }

    /// <summary>
    /// Create an interactive test context for the container action tests
    /// </summary>
    /// <param name="containerActionService">Container action service substitute</param>
    /// <param name="viewService">View service substitute</param>
    /// <param name="logger">Logger registered for the page</param>
    /// <param name="dialogService">Dialog service replacing MudBlazor's; null keeps MudBlazor's</param>
    /// <returns>Test context</returns>
    private static Bunit.BunitContext CreateActionContext(IContainerActionService containerActionService,
                                                          IApplicationViewService viewService,
                                                          TestLogger<RuntimeContainerDetail> logger,
                                                          IDialogService? dialogService = null)
    {
        var testContext = BlazorTestContextFactory.Create();

        testContext.Services.AddSingleton(viewService);
        testContext.Services.AddSingleton(Substitute.For<IRuntimeContainerTagSelectionService>());
        testContext.Services.AddSingleton(containerActionService);
        testContext.Services.AddSingleton<ILogger<RuntimeContainerDetail>>(logger);

        if (dialogService is not null)
        {
            testContext.Services.AddSingleton(dialogService);
        }

        testContext.SetRendererInfo(new RendererInfo("Server", true));

        return testContext;
    }

    /// <summary>
    /// Render the page of the container action tests
    /// </summary>
    /// <param name="testContext">Test context</param>
    /// <returns>Rendered page</returns>
    private static IRenderedComponent<RuntimeContainerDetail> RenderActionPage(Bunit.BunitContext testContext)
    {
        return testContext.Render<RuntimeContainerDetail>(parameters => parameters.Add(page => page.DockerInstanceId, _actionInstanceId)
                                                                                  .Add(page => page.ContainerId, ActionContainerId));
    }

    /// <summary>
    /// Read the texts of the action buttons of the page
    /// </summary>
    /// <param name="page">Rendered page</param>
    /// <returns>Texts of the Start, Stop and Restart buttons</returns>
    private static List<string> GetActionButtonTexts(IRenderedComponent<RuntimeContainerDetail> page)
    {
        string[] actionTexts = ["Start", "Stop", "Restart"];

        return page.FindAll("button")
                   .Select(button => button.TextContent.Trim())
                   .Where(actionTexts.Contains)
                   .ToList();
    }

    /// <summary>
    /// Find the Container actions section of the page by its heading
    /// </summary>
    /// <param name="page">Rendered page</param>
    /// <returns>Section element</returns>
    private static AngleSharp.Dom.IElement FindActionSection(IRenderedComponent<RuntimeContainerDetail> page)
    {
        return page.FindAll(".section-paper")
                   .Single(section => section.QuerySelector("h5")?.TextContent.Trim() == "Container actions");
    }

    /// <summary>
    /// Find the button with the given text
    /// </summary>
    /// <typeparam name="TComponent">Component type</typeparam>
    /// <param name="component">Rendered component</param>
    /// <param name="text">Button text</param>
    /// <returns>Button</returns>
    private static AngleSharp.Dom.IElement FindButton<TComponent>(IRenderedComponent<TComponent> component, string text)
        where TComponent : IComponent
    {
        return component.FindAll("button")
                        .Single(button => button.TextContent.Trim() == text);
    }

    /// <summary>
    /// Click an action button of the page and then a button of the confirmation dialog
    /// </summary>
    /// <param name="page">Rendered page</param>
    /// <param name="provider">Rendered dialog provider</param>
    /// <param name="actionText">Text of the page's action button</param>
    /// <param name="dialogButtonText">Text of the dialog button to click</param>
    /// <returns>Task</returns>
    private static async Task ConfirmAsync(IRenderedComponent<RuntimeContainerDetail> page,
                                           IRenderedComponent<MudDialogProvider> provider,
                                           string actionText,
                                           string dialogButtonText)
    {
        await page.WaitForAssertionAsync(() => Assert.ContainsSingle(button => button.TextContent.Trim() == actionText, page.FindAll("button"), $"The page must offer {actionText}"),
                                         TimeSpan.FromSeconds(WaitSeconds)).ConfigureAwait(false);

        await FindButton(page, actionText).ClickAsync().ConfigureAwait(false);

        await provider.WaitForAssertionAsync(() => Assert.IsNotEmpty(provider.FindAll(".mud-dialog"), "The confirmation dialog must open"), TimeSpan.FromSeconds(WaitSeconds)).ConfigureAwait(false);

        var dialogButton = provider.FindAll(".mud-dialog button")
                                   .Single(button => button.TextContent.Trim() == dialogButtonText);

        await dialogButton.ClickAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Create a test context for the load tests: the given view service, a tag selection service, the logger and hidden container actions
    /// </summary>
    /// <param name="viewService">View service substitute</param>
    /// <param name="logger">Logger registered for the page</param>
    /// <returns>Test context</returns>
    private static Bunit.BunitContext CreateLoadContext(IApplicationViewService viewService, TestLogger<RuntimeContainerDetail> logger)
    {
        var testContext = BlazorTestContextFactory.Create();

        testContext.Services.AddSingleton(viewService);
        testContext.Services.AddSingleton(Substitute.For<IRuntimeContainerTagSelectionService>());
        testContext.Services.AddSingleton<ILogger<RuntimeContainerDetail>>(logger);
        testContext.AddHiddenContainerActions();

        return testContext;
    }

    /// <summary>
    /// Render the page of the load tests for a container
    /// </summary>
    /// <param name="testContext">Test context</param>
    /// <param name="containerId">Container identifier of the route</param>
    /// <returns>Rendered page</returns>
    private static IRenderedComponent<RuntimeContainerDetail> RenderLoadPage(Bunit.BunitContext testContext, string containerId)
    {
        return testContext.Render<RuntimeContainerDetail>(parameters => parameters.Add(page => page.DockerInstanceId, _loadInstanceId)
                                                                                  .Add(page => page.ContainerId, containerId));
    }

    /// <summary>
    /// Create the detail of the load tests, offering one tag candidate
    /// </summary>
    /// <param name="containerId">Container identifier</param>
    /// <param name="containerName">Container name</param>
    /// <returns>Detail</returns>
    private static RuntimeContainerDetailViewData CreateLoadDetail(string containerId, string containerName)
    {
        return new RuntimeContainerDetailViewData
               {
                   DockerInstanceId = _loadInstanceId,
                   ContainerId = containerId,
                   ContainerName = containerName,
                   DockerInstanceName = "Production",
                   ImageReference = "ghcr.io/acme/api:1.0.0",
                   CurrentTag = "1.0.0",
                   RuntimeStatus = "Running",
                   AvailableTagCandidates = [
                                                new TagCandidateViewData
                                                {
                                                    Tag = "1.1.0",
                                                    Reason = "Newer patch release",
                                                }
                                            ],
               };
    }

    /// <summary>
    /// Create a view service substitute answering the details of both route test containers at once
    /// </summary>
    /// <returns>View service substitute</returns>
    private static IApplicationViewService CreateRouteViewService()
    {
        var viewService = Substitute.For<IApplicationViewService>();

        viewService.GetRuntimeContainerDetailAsync(_routeInstanceId, RouteContainerA, Arg.Any<CancellationToken>())
                   .Returns(CreateRouteDetail(RouteContainerA, RouteNameA));
        viewService.GetRuntimeContainerDetailAsync(_routeInstanceId, RouteContainerB, Arg.Any<CancellationToken>())
                   .Returns(CreateRouteDetail(RouteContainerB, RouteNameB));

        return viewService;
    }

    /// <summary>
    /// Create a container action service substitute answering the given availabilities of both route test containers
    /// </summary>
    /// <param name="availabilityA">Availability of the first container</param>
    /// <param name="availabilityB">Availability of the second container</param>
    /// <returns>Container action service substitute</returns>
    private static IContainerActionService CreateRouteActionService(ContainerActionAvailabilityData availabilityA, ContainerActionAvailabilityData availabilityB)
    {
        var containerActionService = Substitute.For<IContainerActionService>();

        containerActionService.GetAvailabilityAsync(_routeInstanceId, RouteContainerA, Arg.Any<CancellationToken>())
                              .Returns(availabilityA);
        containerActionService.GetAvailabilityAsync(_routeInstanceId, RouteContainerB, Arg.Any<CancellationToken>())
                              .Returns(availabilityB);

        return containerActionService;
    }

    /// <summary>
    /// Create the detail of a route test container, offering a tag candidate and a stored manual selection
    /// </summary>
    /// <param name="containerId">Container identifier of the route</param>
    /// <param name="containerName">Container name</param>
    /// <param name="detailContainerId">Container identifier the detail carries; null carries the route's</param>
    /// <returns>Detail</returns>
    private static RuntimeContainerDetailViewData CreateRouteDetail(string containerId, string containerName, string? detailContainerId = null)
    {
        return new RuntimeContainerDetailViewData
               {
                   DockerInstanceId = _routeInstanceId,
                   ContainerId = detailContainerId ?? containerId,
                   ContainerName = containerName,
                   DockerInstanceName = "Production",
                   ImageReference = "ghcr.io/acme/api:1.0.0",
                   CurrentTag = "1.0.0",
                   RuntimeStatus = "Running",
                   ManualSelectionImage = "ghcr.io/acme/api:1.1.0",
                   AvailableTagCandidates = [
                                                new TagCandidateViewData
                                                {
                                                    Tag = "1.1.0",
                                                    Reason = "Newer patch release",
                                                }
                                            ],
               };
    }

    /// <summary>
    /// Create an interactive test context for the route tests
    /// </summary>
    /// <param name="viewService">View service substitute</param>
    /// <param name="containerActionService">Container action service substitute</param>
    /// <param name="tagSelectionService">Tag selection service substitute</param>
    /// <param name="logger">Logger registered for the page</param>
    /// <param name="dialogService">Dialog service replacing MudBlazor's; null keeps MudBlazor's</param>
    /// <returns>Test context</returns>
    private static Bunit.BunitContext CreateRouteContext(IApplicationViewService viewService,
                                                         IContainerActionService containerActionService,
                                                         IRuntimeContainerTagSelectionService tagSelectionService,
                                                         TestLogger<RuntimeContainerDetail> logger,
                                                         IDialogService? dialogService = null)
    {
        var testContext = BlazorTestContextFactory.Create();

        testContext.Services.AddSingleton(viewService);
        testContext.Services.AddSingleton(tagSelectionService);
        testContext.Services.AddSingleton(containerActionService);
        testContext.Services.AddSingleton<ILogger<RuntimeContainerDetail>>(logger);

        if (dialogService is not null)
        {
            testContext.Services.AddSingleton(dialogService);
        }

        testContext.SetRendererInfo(new RendererInfo("Server", true));

        return testContext;
    }

    /// <summary>
    /// Render the page of the route tests for a container
    /// </summary>
    /// <param name="testContext">Test context</param>
    /// <param name="containerId">Container identifier of the route</param>
    /// <returns>Rendered page</returns>
    private static IRenderedComponent<RuntimeContainerDetail> RenderRoutePage(Bunit.BunitContext testContext, string containerId)
    {
        return testContext.Render<RuntimeContainerDetail>(parameters => parameters.Add(page => page.DockerInstanceId, _routeInstanceId)
                                                                                  .Add(page => page.ContainerId, containerId));
    }

    /// <summary>
    /// Move the route of the page to another container of the same Docker instance
    /// </summary>
    /// <param name="page">Rendered page</param>
    /// <param name="containerId">Container identifier of the new route</param>
    private static void MoveRoute(IRenderedComponent<RuntimeContainerDetail> page, string containerId)
    {
        page.Render(parameters => parameters.Add(component => component.DockerInstanceId, _routeInstanceId)
                                            .Add(component => component.ContainerId, containerId));
    }

    /// <summary>
    /// Wait until the page offers the action button
    /// </summary>
    /// <param name="page">Rendered page</param>
    /// <param name="actionText">Text of the action button</param>
    /// <returns>Task</returns>
    private static async Task WaitForOfferedActionAsync(IRenderedComponent<RuntimeContainerDetail> page, string actionText)
    {
        await page.WaitForAssertionAsync(() => Assert.ContainsSingle(button => button.TextContent.Trim() == actionText, page.FindAll("button"), $"The page must offer {actionText}"),
                                         TimeSpan.FromSeconds(WaitSeconds)).ConfigureAwait(false);
    }

    /// <summary>
    /// Let the answers that are already completed reach the page's dispatcher
    /// </summary>
    /// <param name="page">Rendered page</param>
    /// <returns>Task</returns>
    private static async Task SettleAsync(IRenderedComponent<RuntimeContainerDetail> page)
    {
        await Task.Yield();
        await page.InvokeAsync(() =>
                               {
                               }).ConfigureAwait(false);
        await page.InvokeAsync(() =>
                               {
                               }).ConfigureAwait(false);
    }

    #endregion // Methods
}