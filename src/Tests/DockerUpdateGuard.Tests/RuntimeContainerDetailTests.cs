using System.Globalization;

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
    /// Container identifier of the container action tests
    /// </summary>
    private const string ActionContainerId = "f907e278af7c63d535c4c440b720247f8c2dca219ffc8167dd3865b16004319e";

    /// <summary>
    /// Message of a successful restart (E1)
    /// </summary>
    private const string RestartSucceededMessage = "Action 'restart' on 'api' executed successfully";

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
    /// Docker instance identifier of the container action tests
    /// </summary>
    private static readonly Guid _actionInstanceId = Guid.NewGuid();

    /// <summary>
    /// Request time of the history rows
    /// </summary>
    private static readonly DateTimeOffset _requestedAtUtc = new(2026, 11, 14, 9, 30, 0, TimeSpan.Zero);

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
                                    && text.Contains(_requestedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture), StringComparison.Ordinal)
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

    #endregion // Methods
}