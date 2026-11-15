using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;

using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Data;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Infrastructure;
using DockerUpdateGuard.Portainer;
using DockerUpdateGuard.Portainer.Data;
using DockerUpdateGuard.Portainer.Interfaces;
using DockerUpdateGuard.Telemetry;
using DockerUpdateGuard.Tests.Data;
using DockerUpdateGuard.Tests.Helper;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NSubstitute;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="ContainerActionService"/>
/// </summary>
[TestClass]
public class ContainerActionServiceTests
{
    #region Constants

    /// <summary>
    /// Docker instance name used by the tests
    /// </summary>
    private const string InstanceName = "Production";

    /// <summary>
    /// Container name of the snapshot
    /// </summary>
    private const string ContainerName = "my-container";

    /// <summary>
    /// Full container identifier of the snapshot, 64 lowercase hexadecimal characters
    /// </summary>
    private const string ContainerId = "f907e278af7c63d535c4c440b720247f8c2dca219ffc8167dd3865b16004319e";

    /// <summary>
    /// Container identifier of another container
    /// </summary>
    private const string OtherContainerId = "d9298a10d1b0735837dc4bd85dac641b0f3cef27a47e5d53a54f2f3f5b2fcffa";

    /// <summary>
    /// Portainer base URL
    /// </summary>
    private const string PortainerBaseUrl = "https://portainer.example.test";

    /// <summary>
    /// Path of the container list within environment 7
    /// </summary>
    private const string ContainerListPath = "/api/endpoints/7/docker/containers/json";

    /// <summary>
    /// Method and path prefix of the action requests of the snapshot's container within environment 7
    /// </summary>
    private const string ActionRequestPrefix = "POST /api/endpoints/7/docker/containers/" + ContainerId + "/";

    /// <summary>
    /// Portainer user name, a credential that must not leave the client
    /// </summary>
    private const string SecretUsername = "secret-user-4711";

    /// <summary>
    /// Portainer password, a credential that must not leave the client
    /// </summary>
    private const string SecretPassword = "secret-password-4711";

    /// <summary>
    /// JWT Portainer answers the login with, a credential that must not leave the client
    /// </summary>
    private const string SecretJwt = "secret-jwt-4711";

    /// <summary>
    /// Message of a successful restart (E1)
    /// </summary>
    private const string RestartSucceededMessage = "Action 'restart' on 'my-container' executed successfully";

    /// <summary>
    /// Value of RequestedBy for every run
    /// </summary>
    private const string RequestedByUi = "UI";

    /// <summary>
    /// Seconds a gated action request waits for its release before it gives up
    /// </summary>
    private const int GateTimeoutSeconds = 10;

    #endregion // Constants

    #region Fields

    /// <summary>
    /// Current time of the service's clock
    /// </summary>
    private static readonly DateTimeOffset _now = new(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);

    #endregion // Fields

    #region Properties

    /// <summary>
    /// Test context providing information about and functionality for the current test run
    /// </summary>
    public TestContext TestContext { get; set; }

    #endregion // Properties

    #region Methods

    /// <summary>
    /// Verify the offered actions follow the snapshot's runtime status
    /// </summary>
    /// <param name="status">Runtime status of the snapshot</param>
    /// <param name="expectedActions">Expected offered actions, comma separated</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    [DataRow(ContainerRuntimeStatus.Running, "StopContainer,RestartContainer")]
    [DataRow(ContainerRuntimeStatus.Paused, "StopContainer,RestartContainer")]
    [DataRow(ContainerRuntimeStatus.Restarting, "StopContainer,RestartContainer")]
    [DataRow(ContainerRuntimeStatus.Exited, "StartContainer")]
    [DataRow(ContainerRuntimeStatus.NotSet, "StartContainer")]
    [DataRow(ContainerRuntimeStatus.Dead, "")]
    public async Task ContainerActionServiceGetAvailabilityAsyncOffersActionsOfSnapshotStatusAsync(ContainerRuntimeStatus status, string expectedActions)
    {
        var databaseName = Guid.NewGuid().ToString();
        var seeded = await SeedAsync(databaseName, status, true, false).ConfigureAwait(false);
        var requests = new ConcurrentQueue<string>();
        using var provider = CreateServiceProvider(databaseName, new ContainerActionRunWriteFailingInterceptor());
        using var handler = CreatePortainerHandler(requests, CreateAnswer(HttpStatusCode.NoContent));

        var service = CreateService(provider, handler, CreateOptions(null), null, new TestLogger<ContainerActionService>(), CancellationToken.None);

        var availability = await service.GetAvailabilityAsync(seeded.InstanceId, ContainerId, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(availability.IsShown, "An enabled instance with Portainer enabled must show the section");
        Assert.IsTrue(availability.IsCurrent, "A snapshot of the latest runtime scan must be current");
        Assert.IsTrue(availability.IsSupported, "A Portainer that knows the configured environment must support actions");
        Assert.IsFalse(availability.IsInProgress, "No action runs for the container");
        Assert.AreEqual("7", availability.EndpointId, "The environment of the capability check must be reported for the dialog");
        Assert.AreSequenceEqual(ParseActions(expectedActions),
                                availability.OfferedActions.ToList(),
                                SequenceOrder.InAnyOrder,
                                $"A container of status {status} must be offered exactly {expectedActions}");
    }

    /// <summary>
    /// Verify the section is not shown for an unknown instance, an instance missing from or disabled in the configuration, or one with Portainer disabled
    /// </summary>
    /// <param name="scenario">Scenario name</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    [DataRow("InstanceDisabled")]
    [DataRow("PortainerDisabled")]
    [DataRow("InstanceNotConfigured")]
    [DataRow("UnknownInstance")]
    public async Task ContainerActionServiceGetAvailabilityAsyncWhenPortainerOrInstanceDisabledIsNotShownAsync(string scenario)
    {
        var databaseName = Guid.NewGuid().ToString();
        var seeded = await SeedAsync(databaseName, ContainerRuntimeStatus.Running, true, false).ConfigureAwait(false);
        var requests = new ConcurrentQueue<string>();
        using var provider = CreateServiceProvider(databaseName, new ContainerActionRunWriteFailingInterceptor());
        using var handler = CreatePortainerHandler(requests, CreateAnswer(HttpStatusCode.NoContent));
        var instanceId = scenario == "UnknownInstance" ? Guid.NewGuid() : seeded.InstanceId;

        var service = CreateService(provider, handler, CreateRejectingOptions(scenario), null, new TestLogger<ContainerActionService>(), CancellationToken.None);

        var availability = await service.GetAvailabilityAsync(instanceId, ContainerId, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(availability.IsShown, $"The section must not be shown in scenario {scenario}");
        Assert.IsEmpty(availability.OfferedActions, $"No action may be offered in scenario {scenario}");
        Assert.IsEmpty(requests, $"Portainer must not be contacted in scenario {scenario}");
    }

    /// <summary>
    /// Verify a container that is not in the latest runtime scan shows the not-current message and Portainer is not contacted
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    public async Task ContainerActionServiceGetAvailabilityAsyncWhenNotInLatestScanShowsNotCurrentMessageWithoutContactingPortainerAsync()
    {
        var databaseName = Guid.NewGuid().ToString();
        var seeded = await SeedAsync(databaseName, ContainerRuntimeStatus.Running, true, true).ConfigureAwait(false);
        var requests = new ConcurrentQueue<string>();
        using var provider = CreateServiceProvider(databaseName, new ContainerActionRunWriteFailingInterceptor());
        using var handler = CreatePortainerHandler(requests, CreateAnswer(HttpStatusCode.NoContent));

        var service = CreateService(provider, handler, CreateOptions(null), null, new TestLogger<ContainerActionService>(), CancellationToken.None);

        var availability = await service.GetAvailabilityAsync(seeded.InstanceId, ContainerId, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(availability.IsShown, "The section must be shown for an enabled instance with Portainer enabled");
        Assert.IsFalse(availability.IsCurrent, "A container missing from the latest runtime scan must not be current");
        Assert.AreEqual("This container is not in the latest runtime scan of 'Production'; no actions are available.",
                        availability.Message,
                        "The not-current message of E6 must be shown");
        Assert.IsEmpty(availability.OfferedActions, "A container that is not current must offer no action");
        Assert.IsEmpty(requests, "Portainer must not be contacted for a container that is not current");
    }

    /// <summary>
    /// Verify a capability check that finds the configured environment unknown shows its message instead of actions
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    public async Task ContainerActionServiceGetAvailabilityAsyncWhenCapabilityUnsupportedShowsItsMessageWithoutActionsAsync()
    {
        var databaseName = Guid.NewGuid().ToString();
        var seeded = await SeedAsync(databaseName, ContainerRuntimeStatus.Running, true, false).ConfigureAwait(false);
        var requests = new ConcurrentQueue<string>();
        using var provider = CreateServiceProvider(databaseName, new ContainerActionRunWriteFailingInterceptor());
        using var handler = CreatePortainerHandler(requests, CreateAnswer(HttpStatusCode.NoContent));

        var service = CreateService(provider, handler, CreateOptions(instance => instance.Portainer.EndpointId = "8"), null, new TestLogger<ContainerActionService>(), CancellationToken.None);

        var availability = await service.GetAvailabilityAsync(seeded.InstanceId, ContainerId, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(availability.IsShown, "The section must be shown so that the capability message is visible");
        Assert.IsFalse(availability.IsSupported, "An unknown environment must not support actions");
        Assert.AreEqual("Portainer environment 8 not found; check Portainer:EndpointId of Docker instance 'Production'",
                        availability.Message,
                        "The capability message must be shown instead of the buttons");
        Assert.IsEmpty(availability.OfferedActions, "An unsupported capability must offer no action");
    }

    /// <summary>
    /// Verify the page load reports an action this process is executing for the container
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    public async Task ContainerActionServiceGetAvailabilityAsyncWhenActionInProgressShowsInProgressMessageAsync()
    {
        var databaseName = Guid.NewGuid().ToString();
        var seeded = await SeedAsync(databaseName, ContainerRuntimeStatus.Running, true, false).ConfigureAwait(false);
        var requests = new ConcurrentQueue<string>();
        var actionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var provider = CreateServiceProvider(databaseName, new ContainerActionRunWriteFailingInterceptor());
        using var handler = CreatePortainerHandler(requests, CreateGatedAnswer(actionStarted, release));

        var service = CreateService(provider, handler, CreateOptions(null), null, new TestLogger<ContainerActionService>(), CancellationToken.None);
        var running = service.ExecuteAsync(seeded.InstanceId, ContainerId, ContainerActionType.RestartContainer);

        await actionStarted.Task.WaitAsync(TimeSpan.FromSeconds(GateTimeoutSeconds), TestContext.CancellationToken).ConfigureAwait(false);

        var availability = await service.GetAvailabilityAsync(seeded.InstanceId, ContainerId, TestContext.CancellationToken).ConfigureAwait(false);

        release.TrySetResult();

        await running.ConfigureAwait(false);

        Assert.IsTrue(availability.IsInProgress, "A running action of this process must be reported on page load");
        Assert.AreEqual("An action is in progress for this container.", availability.Message, "The in-progress message of E8 must be shown");
    }

    /// <summary>
    /// Verify the own container is detected only for a host name of 12 lowercase hexadecimal characters that starts the container identifier
    /// </summary>
    /// <param name="hostName">Value of HOSTNAME; null leaves it unset</param>
    /// <param name="expected">Whether the container is the application's own</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC3")]
    [DataRow("f907e278af7c", true)]
    [DataRow("", false)]
    [DataRow(null, false)]
    [DataRow("cafe", false)]
    [DataRow("F907E278AF7C", false)]
    [DataRow("d9298a10d1b0", false)]
    public async Task ContainerActionServiceGetAvailabilityAsyncDetectsOwnContainerOnlyForDockerHostNameAsync(string? hostName, bool expected)
    {
        var databaseName = Guid.NewGuid().ToString();
        var seeded = await SeedAsync(databaseName, ContainerRuntimeStatus.Running, true, false).ConfigureAwait(false);
        var requests = new ConcurrentQueue<string>();
        using var provider = CreateServiceProvider(databaseName, new ContainerActionRunWriteFailingInterceptor());
        using var handler = CreatePortainerHandler(requests, CreateAnswer(HttpStatusCode.NoContent));

        var service = CreateService(provider, handler, CreateOptions(null), hostName, new TestLogger<ContainerActionService>(), CancellationToken.None);

        var availability = await service.GetAvailabilityAsync(seeded.InstanceId, ContainerId, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(expected,
                        availability.IsOwnContainer,
                        $"HOSTNAME '{hostName ?? "<unset>"}' must {(expected ? string.Empty : "not ")}mark the container as the application's own");
    }

    /// <summary>
    /// Verify the run is written with all its fields before Portainer is contacted
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC4")]
    public async Task ContainerActionServiceExecuteAsyncWritesRequestedRunBeforeContactingPortainerAsync()
    {
        var databaseName = Guid.NewGuid().ToString();
        var seeded = await SeedAsync(databaseName, ContainerRuntimeStatus.Running, true, false).ConfigureAwait(false);
        var requests = new ConcurrentQueue<string>();
        var runsAtFirstRequest = new ConcurrentQueue<List<ContainerActionRun>>();
        using var provider = CreateServiceProvider(databaseName, new ContainerActionRunWriteFailingInterceptor());
        using var handler = new DelegateHttpMessageHandler(async (request, token) =>
                                                           {
                                                               runsAtFirstRequest.Enqueue(await LoadRunsAsync(databaseName).ConfigureAwait(false));

                                                               return await AnswerPortainerAsync(request, requests, CreateAnswer(HttpStatusCode.NoContent), token).ConfigureAwait(false);
                                                           });

        var service = CreateService(provider, handler, CreateOptions(null), null, new TestLogger<ContainerActionService>(), CancellationToken.None);

        await service.ExecuteAsync(seeded.InstanceId, ContainerId, ContainerActionType.RestartContainer).ConfigureAwait(false);

        Assert.IsTrue(runsAtFirstRequest.TryPeek(out var runs), "Portainer must be contacted");
        Assert.HasCount(1, runs, "Exactly one run must exist when Portainer is first contacted");

        var run = runs[0];

        Assert.AreEqual(seeded.InstanceId, run.DockerInstanceId, "The run must reference the instance");
        Assert.AreEqual(seeded.EndpointId, run.PortainerEndpointId, "The run must reference the instance's stored Portainer endpoint");
        Assert.AreEqual(seeded.SnapshotId, run.ContainerSnapshotId, "The run must reference the snapshot of the container");
        Assert.AreEqual(ContainerActionType.RestartContainer, run.ActionType, "The run must carry the action type");
        Assert.AreEqual(PortainerResourceType.Container, run.ResourceType, "The run must target a container");
        Assert.AreEqual(ContainerName, run.ResourceName, "The run must carry the container name");
        Assert.AreEqual(RequestedByUi, run.RequestedBy, "Every run is requested by the UI");
        Assert.AreEqual(_now, run.RequestedAtUtc, "The request time must come from the injected clock");
        Assert.AreEqual(ContainerActionStatus.Running, run.Status, "The run must be Running once Portainer is contacted");
        Assert.IsNull(run.CompletedAtUtc, "The run must not be completed before Portainer answers");
    }

    /// <summary>
    /// Verify a finished action records the terminal status with completion time and message, in one run
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC4")]
    public async Task ContainerActionServiceExecuteAsyncRecordsTerminalRunWithCompletionTimeAndMessageAsync()
    {
        var databaseName = Guid.NewGuid().ToString();
        var seeded = await SeedAsync(databaseName, ContainerRuntimeStatus.Running, true, false).ConfigureAwait(false);
        var requests = new ConcurrentQueue<string>();
        using var provider = CreateServiceProvider(databaseName, new ContainerActionRunWriteFailingInterceptor());
        using var handler = CreatePortainerHandler(requests, CreateAnswer(HttpStatusCode.NoContent));

        var service = CreateService(provider, handler, CreateOptions(null), null, new TestLogger<ContainerActionService>(), CancellationToken.None);

        var result = await service.ExecuteAsync(seeded.InstanceId, ContainerId, ContainerActionType.RestartContainer).ConfigureAwait(false);

        var runs = await LoadRunsAsync(databaseName).ConfigureAwait(false);

        Assert.AreEqual(ContainerActionStatus.Succeeded, result.Status, "A successful action must report Succeeded");
        Assert.AreEqual(RestartSucceededMessage, result.Message, "The outcome message of E1 must be returned");
        Assert.HasCount(1, runs, "A confirmed action must write exactly one run");
        Assert.AreEqual(ContainerActionStatus.Succeeded, runs[0].Status, "The run must end Succeeded");
        Assert.AreEqual(_now, runs[0].CompletedAtUtc, "The completion time must come from the injected clock");
        Assert.AreEqual(RestartSucceededMessage, runs[0].ErrorMessage, "The outcome message must be stored with every terminal status");
    }

    /// <summary>
    /// Verify the run links the instance's stored Portainer endpoint only when one exists
    /// </summary>
    /// <param name="withStoredEndpoint">Whether the instance has a stored Portainer endpoint</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC4")]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ContainerActionServiceExecuteAsyncLinksStoredEndpointOnlyWhenOneExistsAsync(bool withStoredEndpoint)
    {
        var databaseName = Guid.NewGuid().ToString();
        var seeded = await SeedAsync(databaseName, ContainerRuntimeStatus.Running, withStoredEndpoint, false).ConfigureAwait(false);
        var requests = new ConcurrentQueue<string>();
        using var provider = CreateServiceProvider(databaseName, new ContainerActionRunWriteFailingInterceptor());
        using var handler = CreatePortainerHandler(requests, CreateAnswer(HttpStatusCode.NoContent));

        var service = CreateService(provider, handler, CreateOptions(null), null, new TestLogger<ContainerActionService>(), CancellationToken.None);

        var result = await service.ExecuteAsync(seeded.InstanceId, ContainerId, ContainerActionType.RestartContainer).ConfigureAwait(false);

        var runs = await LoadRunsAsync(databaseName).ConfigureAwait(false);

        Assert.AreEqual(ContainerActionStatus.Succeeded, result.Status, "The action must run whether or not an endpoint is stored");
        Assert.HasCount(1, runs, "A confirmed action must write exactly one run");
        Assert.AreEqual(seeded.EndpointId, runs[0].PortainerEndpointId, $"The run must link the stored endpoint only when one exists (stored: {withStoredEndpoint})");
    }

    /// <summary>
    /// Verify no credential reaches the result, the run or a log entry, on success and on failure
    /// </summary>
    /// <param name="actionStatus">HTTP status of the action answer</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC5")]
    [DataRow(HttpStatusCode.NoContent)]
    [DataRow(HttpStatusCode.InternalServerError)]
    public async Task ContainerActionServiceExecuteAsyncRunsMessagesAndLogsCarryNoCredentialAsync(HttpStatusCode actionStatus)
    {
        var databaseName = Guid.NewGuid().ToString();
        var seeded = await SeedAsync(databaseName, ContainerRuntimeStatus.Running, true, false).ConfigureAwait(false);
        var requests = new ConcurrentQueue<string>();
        var serviceLogger = new TestLogger<ContainerActionService>();
        var clientLogger = new TestLogger<PortainerClient>();
        using var provider = CreateServiceProvider(databaseName, new ContainerActionRunWriteFailingInterceptor());
        using var handler = CreatePortainerHandler(requests, CreateAnswer(actionStatus));
        var options = CreateOptions(instance =>
                                    {
                                        instance.Portainer.ApiToken = null;
                                        instance.Portainer.Username = SecretUsername;
                                        instance.Portainer.Password = SecretPassword;
                                    });

        var service = CreateService(provider.GetRequiredService<IServiceScopeFactory>(), CreatePortainerClient(handler, clientLogger), options, null, serviceLogger, CancellationToken.None);

        var result = await service.ExecuteAsync(seeded.InstanceId, ContainerId, ContainerActionType.RestartContainer).ConfigureAwait(false);

        var runs = await LoadRunsAsync(databaseName).ConfigureAwait(false);
        var texts = CollectTexts(result.Message, runs, serviceLogger.Entries.Concat(clientLogger.Entries));

        Assert.Contains("POST /api/auth", requests, "The login must have run so that a JWT exists");
        Assert.HasCount(1, runs, "A confirmed action must write exactly one run");
        Assert.DoesNotContain(text => text.Contains(SecretUsername, StringComparison.Ordinal), texts, "No run, message or log entry may carry the user name");
        Assert.DoesNotContain(text => text.Contains(SecretPassword, StringComparison.Ordinal), texts, "No run, message or log entry may carry the password");
        Assert.DoesNotContain(text => text.Contains(SecretJwt, StringComparison.Ordinal), texts, "No run, message or log entry may carry the JWT");
    }

    /// <summary>
    /// Verify a confirmation for a container no longer in the latest runtime scan ends Failed without contacting Portainer
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC6")]
    public async Task ContainerActionServiceExecuteAsyncWhenNotInLatestScanRecordsFailedWithoutContactingPortainerAsync()
    {
        var databaseName = Guid.NewGuid().ToString();
        var seeded = await SeedAsync(databaseName, ContainerRuntimeStatus.Running, true, true).ConfigureAwait(false);
        var requests = new ConcurrentQueue<string>();
        using var provider = CreateServiceProvider(databaseName, new ContainerActionRunWriteFailingInterceptor());
        using var handler = CreatePortainerHandler(requests, CreateAnswer(HttpStatusCode.NoContent));

        var service = CreateService(provider, handler, CreateOptions(null), null, new TestLogger<ContainerActionService>(), CancellationToken.None);

        var result = await service.ExecuteAsync(seeded.InstanceId, ContainerId, ContainerActionType.RestartContainer).ConfigureAwait(false);

        var runs = await LoadRunsAsync(databaseName).ConfigureAwait(false);

        AssertFailedRun(result, runs, "Container is not in the latest runtime scan; run a runtime scan and try again");
        Assert.IsEmpty(requests, "Portainer must not be contacted for a container that is not current");
    }

    /// <summary>
    /// Verify two overlapping confirmations for one container execute one action and record the other as Failed
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC7")]
    public async Task ContainerActionServiceExecuteAsyncWithOverlappingRequestsExecutesOneAndRecordsOneInProgressAsync()
    {
        var databaseName = Guid.NewGuid().ToString();
        var seeded = await SeedAsync(databaseName, ContainerRuntimeStatus.Running, true, false).ConfigureAwait(false);
        var requests = new ConcurrentQueue<string>();
        var actionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var provider = CreateServiceProvider(databaseName, new ContainerActionRunWriteFailingInterceptor());
        using var handler = CreatePortainerHandler(requests, CreateGatedAnswer(actionStarted, release));

        var service = CreateService(provider, handler, CreateOptions(null), null, new TestLogger<ContainerActionService>(), CancellationToken.None);
        var first = service.ExecuteAsync(seeded.InstanceId, ContainerId, ContainerActionType.RestartContainer);

        await actionStarted.Task.WaitAsync(TimeSpan.FromSeconds(GateTimeoutSeconds), TestContext.CancellationToken).ConfigureAwait(false);

        var second = await service.ExecuteAsync(seeded.InstanceId, ContainerId, ContainerActionType.StopContainer).ConfigureAwait(false);

        release.TrySetResult();

        var firstResult = await first.ConfigureAwait(false);
        var runs = await LoadRunsAsync(databaseName).ConfigureAwait(false);

        Assert.AreEqual(ContainerActionStatus.Succeeded, firstResult.Status, "The first confirmation must execute its action");
        Assert.AreEqual(ContainerActionStatus.Failed, second.Status, "The overlapping confirmation must fail");
        Assert.AreEqual("Another action is in progress for this container", second.Message, "The overlapping confirmation must carry the message of E8");
        Assert.HasCount(1, requests.Where(request => request.StartsWith(ActionRequestPrefix, StringComparison.Ordinal)).ToList(), "Exactly one action request must be sent");
        Assert.HasCount(2, runs, "Each confirmation must write one run");
        Assert.ContainsSingle(run => run.Status == ContainerActionStatus.Succeeded && run.ActionType == ContainerActionType.RestartContainer,
                              runs,
                              "The executed action must end Succeeded");
        Assert.ContainsSingle(run => run.Status == ContainerActionStatus.Failed
                                     && run.ActionType == ContainerActionType.StopContainer
                                     && run.ErrorMessage == "Another action is in progress for this container",
                              runs,
                              "The overlapping confirmation must be recorded Failed with the message of E8");
    }

    /// <summary>
    /// Verify a confirmation that finds the instance missing from or disabled in the configuration, or Portainer disabled, ends Failed without contacting Portainer
    /// </summary>
    /// <param name="scenario">Scenario name</param>
    /// <param name="expectedMessage">Expected run message</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC8")]
    [DataRow("InstanceDisabled", "Docker instance 'Production' is disabled")]
    [DataRow("InstanceNotConfigured", "Docker instance 'Production' is disabled")]
    [DataRow("PortainerDisabled", "Portainer is not enabled for Docker instance 'Production'")]
    public async Task ContainerActionServiceExecuteAsyncWhenInstanceOrPortainerDisabledRecordsFailedWithoutContactingPortainerAsync(string scenario, string expectedMessage)
    {
        var databaseName = Guid.NewGuid().ToString();
        var seeded = await SeedAsync(databaseName, ContainerRuntimeStatus.Running, true, false).ConfigureAwait(false);
        var requests = new ConcurrentQueue<string>();
        using var provider = CreateServiceProvider(databaseName, new ContainerActionRunWriteFailingInterceptor());
        using var handler = CreatePortainerHandler(requests, CreateAnswer(HttpStatusCode.NoContent));

        var service = CreateService(provider, handler, CreateRejectingOptions(scenario), null, new TestLogger<ContainerActionService>(), CancellationToken.None);

        var result = await service.ExecuteAsync(seeded.InstanceId, ContainerId, ContainerActionType.RestartContainer).ConfigureAwait(false);

        var runs = await LoadRunsAsync(databaseName).ConfigureAwait(false);

        AssertFailedRun(result, runs, expectedMessage);
        Assert.IsEmpty(requests, $"Portainer must not be contacted in scenario {scenario}");
    }

    /// <summary>
    /// Verify a confirmation rejected by a check logs one warning naming the action, container, instance and the check
    /// </summary>
    /// <param name="scenario">Scenario name</param>
    /// <param name="withNewerScan">Whether a newer runtime scan holds only another container</param>
    /// <param name="expectedCheck">Name of the check that rejects the action</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC11")]
    [DataRow("InstanceDisabled", false, "instance disabled")]
    [DataRow("PortainerDisabled", false, "portainer disabled")]
    [DataRow("Enabled", true, "not current")]
    public async Task ContainerActionServiceExecuteAsyncWhenRejectedLogsRejectingCheckAsync(string scenario, bool withNewerScan, string expectedCheck)
    {
        var databaseName = Guid.NewGuid().ToString();
        var seeded = await SeedAsync(databaseName, ContainerRuntimeStatus.Running, true, withNewerScan).ConfigureAwait(false);
        var requests = new ConcurrentQueue<string>();
        var logger = new TestLogger<ContainerActionService>();
        using var provider = CreateServiceProvider(databaseName, new ContainerActionRunWriteFailingInterceptor());
        using var handler = CreatePortainerHandler(requests, CreateAnswer(HttpStatusCode.NoContent));

        var service = CreateService(provider, handler, CreateRejectingOptions(scenario), null, logger, CancellationToken.None);

        await service.ExecuteAsync(seeded.InstanceId, ContainerId, ContainerActionType.RestartContainer).ConfigureAwait(false);

        var rejection = Assert.ContainsSingle(entry => entry.EventId.Id == 3322, logger.Entries, $"A rejection by the check '{expectedCheck}' must be logged once");

        Assert.AreEqual(LogLevel.Warning, rejection.LogLevel, "A rejected action must be logged as a warning");
        Assert.AreEqual($"Container action RestartContainer on container {ContainerName} of Docker instance {InstanceName} rejected: {expectedCheck}",
                        rejection.Message,
                        "The rejection must name the action, container, instance and the rejecting check only");
    }

    /// <summary>
    /// Verify a confirmation for an instance or a container the database does not hold fails with the confirmation text of E6, writes no run, does not contact Portainer and logs one warning without the container identifier
    /// </summary>
    /// <param name="unknownInstance">Whether the instance is unknown; otherwise the container is</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC6")]
    [TestCategory("AC11")]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ContainerActionServiceExecuteAsyncWhenInstanceOrContainerUnknownFailsWithoutRunAsync(bool unknownInstance)
    {
        var databaseName = Guid.NewGuid().ToString();
        var seeded = await SeedAsync(databaseName, ContainerRuntimeStatus.Running, true, false).ConfigureAwait(false);
        var requests = new ConcurrentQueue<string>();
        using var provider = CreateServiceProvider(databaseName, new ContainerActionRunWriteFailingInterceptor());
        using var handler = CreatePortainerHandler(requests, CreateAnswer(HttpStatusCode.NoContent));

        var logger = new TestLogger<ContainerActionService>();
        var service = CreateService(provider, handler, CreateOptions(null), null, logger, CancellationToken.None);
        var instanceId = unknownInstance ? Guid.NewGuid() : seeded.InstanceId;
        var containerId = unknownInstance ? ContainerId : OtherContainerId;

        var result = await service.ExecuteAsync(instanceId, containerId, ContainerActionType.RestartContainer).ConfigureAwait(false);

        var runs = await LoadRunsAsync(databaseName).ConfigureAwait(false);

        Assert.AreEqual(ContainerActionStatus.Failed, result.Status, "A confirmation for an unknown target must fail");
        Assert.AreEqual("Container is not in the latest runtime scan; run a runtime scan and try again",
                        result.Message,
                        "A confirmation for an unknown target must return the confirmation text of E6");
        Assert.IsEmpty(runs, "Without a known instance and snapshot no run can be written");
        Assert.IsEmpty(requests, "Portainer must not be contacted for an unknown target");

        var rejection = Assert.ContainsSingle(entry => entry.EventId.Id == 3325, logger.Entries, "An unknown target must be logged once");

        Assert.AreEqual(LogLevel.Warning, rejection.LogLevel, "An unknown target must be logged as a warning");
        Assert.AreEqual($"Container action RestartContainer rejected: no snapshot of the container on Docker instance {instanceId}",
                        rejection.Message,
                        "The entry must name only the action type and the instance identifier");
        Assert.DoesNotContain(entry => entry.Message.Contains(containerId, StringComparison.Ordinal),
                              logger.Entries,
                              "No log entry may carry the container identifier of the request");
    }

    /// <summary>
    /// Verify a confirmation whose configured environment Portainer does not know ends Failed without an action request
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC8")]
    public async Task ContainerActionServiceExecuteAsyncWhenEnvironmentUnknownRecordsFailedWithoutActionRequestAsync()
    {
        var databaseName = Guid.NewGuid().ToString();
        var seeded = await SeedAsync(databaseName, ContainerRuntimeStatus.Running, true, false).ConfigureAwait(false);
        var requests = new ConcurrentQueue<string>();
        using var provider = CreateServiceProvider(databaseName, new ContainerActionRunWriteFailingInterceptor());
        using var handler = CreatePortainerHandler(requests, CreateAnswer(HttpStatusCode.NoContent));

        var service = CreateService(provider, handler, CreateOptions(instance => instance.Portainer.EndpointId = "8"), null, new TestLogger<ContainerActionService>(), CancellationToken.None);

        var result = await service.ExecuteAsync(seeded.InstanceId, ContainerId, ContainerActionType.RestartContainer).ConfigureAwait(false);

        var runs = await LoadRunsAsync(databaseName).ConfigureAwait(false);

        AssertFailedRun(result, runs, "Portainer environment 8 not found; check Portainer:EndpointId of Docker instance 'Production'");
        Assert.DoesNotContain(request => request.StartsWith("POST /api/endpoints/", StringComparison.Ordinal), requests, "No action request may be sent to an unknown environment");
    }

    /// <summary>
    /// Verify an action finishes and its run is finalized after the scope of the caller that started it was disposed
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC9")]
    public async Task ContainerActionServiceExecuteAsyncFinalizesRunAfterCallerScopeIsDisposedAsync()
    {
        var databaseName = Guid.NewGuid().ToString();
        var seeded = await SeedAsync(databaseName, ContainerRuntimeStatus.Running, true, false).ConfigureAwait(false);
        var requests = new ConcurrentQueue<string>();
        var actionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var provider = CreateServiceProvider(databaseName, new ContainerActionRunWriteFailingInterceptor());
        using var handler = CreatePortainerHandler(requests, CreateGatedAnswer(actionStarted, release));
        var callerScope = provider.CreateScope();

        var service = CreateService(callerScope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
                                    CreatePortainerClient(handler, new TestLogger<PortainerClient>()),
                                    CreateOptions(null),
                                    null,
                                    new TestLogger<ContainerActionService>(),
                                    CancellationToken.None);
        var running = service.ExecuteAsync(seeded.InstanceId, ContainerId, ContainerActionType.RestartContainer);

        await actionStarted.Task.WaitAsync(TimeSpan.FromSeconds(GateTimeoutSeconds), TestContext.CancellationToken).ConfigureAwait(false);

        callerScope.Dispose();
        release.TrySetResult();

        var result = await running.ConfigureAwait(false);
        var runs = await LoadRunsAsync(databaseName).ConfigureAwait(false);

        Assert.AreEqual(ContainerActionStatus.Succeeded, result.Status, "The action must run to its end after the caller's scope is gone");
        Assert.HasCount(1, runs, "A confirmed action must write exactly one run");
        Assert.AreEqual(ContainerActionStatus.Succeeded, runs[0].Status, "The run must be finalized after the caller's scope is gone");
    }

    /// <summary>
    /// Verify an application shutdown before the action request is sent records the run as Cancelled
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC9")]
    public async Task ContainerActionServiceExecuteAsyncWhenStoppingBeforeSendRecordsCancelledAsync()
    {
        var databaseName = Guid.NewGuid().ToString();
        var seeded = await SeedAsync(databaseName, ContainerRuntimeStatus.Running, true, false).ConfigureAwait(false);
        var requests = new ConcurrentQueue<string>();
        using var stopping = new CancellationTokenSource();
        using var provider = CreateServiceProvider(databaseName, new ContainerActionRunWriteFailingInterceptor());
        using var handler = CreatePortainerHandler(requests, CreateAnswer(HttpStatusCode.NoContent));

        await stopping.CancelAsync().ConfigureAwait(false);

        var service = CreateService(provider, handler, CreateOptions(null), null, new TestLogger<ContainerActionService>(), stopping.Token);

        var result = await service.ExecuteAsync(seeded.InstanceId, ContainerId, ContainerActionType.RestartContainer).ConfigureAwait(false);

        var runs = await LoadRunsAsync(databaseName).ConfigureAwait(false);

        Assert.AreEqual(ContainerActionStatus.Cancelled, result.Status, "A shutdown before sending must cancel the action");
        Assert.HasCount(1, runs, "A confirmed action must write exactly one run");
        Assert.AreEqual(ContainerActionStatus.Cancelled, runs[0].Status, "The run must be recorded Cancelled");
        Assert.AreEqual("Cancelled: the application stopped before the action was sent", runs[0].ErrorMessage, "The run must carry the message of E10");
        Assert.AreEqual(_now, runs[0].CompletedAtUtc, "A cancelled run must be completed");
        Assert.DoesNotContain(request => request.StartsWith(ActionRequestPrefix, StringComparison.Ordinal), requests, "No action request may be sent once the application stops");
    }

    /// <summary>
    /// Verify an application shutdown while the action request is in flight leaves the run Running for the startup reconciliation
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC9")]
    public async Task ContainerActionServiceExecuteAsyncWhenStoppingDuringActionLeavesRunRunningAsync()
    {
        var databaseName = Guid.NewGuid().ToString();
        var seeded = await SeedAsync(databaseName, ContainerRuntimeStatus.Running, true, false).ConfigureAwait(false);
        var requests = new ConcurrentQueue<string>();
        using var stopping = new CancellationTokenSource();
        using var provider = CreateServiceProvider(databaseName, new ContainerActionRunWriteFailingInterceptor());
        using var handler = CreatePortainerHandler(requests, token => StopDuringActionAsync(stopping, token));

        var service = CreateService(provider, handler, CreateOptions(null), null, new TestLogger<ContainerActionService>(), stopping.Token);

        await service.ExecuteAsync(seeded.InstanceId, ContainerId, ContainerActionType.RestartContainer).ConfigureAwait(false);

        var runs = await LoadRunsAsync(databaseName).ConfigureAwait(false);

        Assert.HasCount(1, requests.Where(request => request.StartsWith(ActionRequestPrefix, StringComparison.Ordinal)).ToList(), "The action request must have been sent");
        Assert.HasCount(1, runs, "A confirmed action must write exactly one run");
        Assert.AreEqual(ContainerActionStatus.Running, runs[0].Status, "A run whose action may have been applied must stay Running when the application stops");
        Assert.IsNull(runs[0].CompletedAtUtc, "A run left Running must not be completed");
    }

    /// <summary>
    /// Verify each action emits one internal action activity whose tags are exactly instance, action type, container name and run status
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC11")]
    public async Task ContainerActionServiceExecuteAsyncEmitsOneActionActivityWithAllowedTagsAsync()
    {
        var databaseName = Guid.NewGuid().ToString();
        var seeded = await SeedAsync(databaseName, ContainerRuntimeStatus.Running, true, false).ConfigureAwait(false);
        var requests = new ConcurrentQueue<string>();
        var activities = new ConcurrentQueue<Activity>();
        using var listener = CreateActivityListener(activities);
        using var provider = CreateServiceProvider(databaseName, new ContainerActionRunWriteFailingInterceptor());
        using var handler = CreatePortainerHandler(requests, CreateAnswer(HttpStatusCode.NoContent));

        var service = CreateService(provider, handler, CreateOptions(null), null, new TestLogger<ContainerActionService>(), CancellationToken.None);

        await service.ExecuteAsync(seeded.InstanceId, ContainerId, ContainerActionType.RestartContainer).ConfigureAwait(false);

        var actionActivities = activities.Where(activity => activity.OperationName == TelemetryActivityNames.PortainerAction)
                                         .ToList();

        Assert.HasCount(1, actionActivities, "Each action must emit exactly one action activity");

        var tags = actionActivities[0].TagObjects
                                      .ToDictionary(tag => tag.Key, tag => tag.Value?.ToString());

        Assert.AreEqual(ActivityKind.Internal, actionActivities[0].Kind, "The action activity must be internal");
        Assert.AreSequenceEqual([TelemetryTagNames.DockerInstanceName, TelemetryTagNames.ActionType, TelemetryTagNames.ContainerName, TelemetryTagNames.ResultStatus],
                                tags.Keys.ToList(),
                                SequenceOrder.InAnyOrder,
                                "The action activity must carry only the allowed tags");
        Assert.AreEqual(InstanceName, tags[TelemetryTagNames.DockerInstanceName], "The activity must name the instance");
        Assert.AreEqual(nameof(ContainerActionType.RestartContainer), tags[TelemetryTagNames.ActionType], "The activity must carry the action type's name");
        Assert.AreEqual(ContainerName, tags[TelemetryTagNames.ContainerName], "The activity must name the container");
        Assert.AreEqual(nameof(ContainerActionStatus.Succeeded), tags[TelemetryTagNames.ResultStatus], "The activity must carry the run status");
    }

    /// <summary>
    /// Verify the action is logged as requested and finished with container and instance name, and no entry carries the container identifier or a credential
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC11")]
    public async Task ContainerActionServiceExecuteAsyncLogsOnlyAllowedValuesAsync()
    {
        var databaseName = Guid.NewGuid().ToString();
        var seeded = await SeedAsync(databaseName, ContainerRuntimeStatus.Running, true, false).ConfigureAwait(false);
        var requests = new ConcurrentQueue<string>();
        var logger = new TestLogger<ContainerActionService>();
        using var provider = CreateServiceProvider(databaseName, new ContainerActionRunWriteFailingInterceptor());
        using var handler = CreatePortainerHandler(requests, CreateAnswer(HttpStatusCode.NoContent));

        var service = CreateService(provider, handler, CreateOptions(null), null, logger, CancellationToken.None);

        await service.ExecuteAsync(seeded.InstanceId, ContainerId, ContainerActionType.RestartContainer).ConfigureAwait(false);

        Assert.Contains(entry => entry.EventId.Id == 3320
                                 && entry.LogLevel == LogLevel.Information
                                 && entry.Message.Contains(ContainerName, StringComparison.Ordinal)
                                 && entry.Message.Contains(InstanceName, StringComparison.Ordinal),
                        logger.Entries,
                        "The requested action must be logged with container and instance name");
        Assert.Contains(entry => entry.EventId.Id == 3321
                                 && entry.LogLevel == LogLevel.Information
                                 && entry.Message.Contains(nameof(ContainerActionStatus.Succeeded), StringComparison.Ordinal),
                        logger.Entries,
                        "The finished action must be logged with its status");
        Assert.DoesNotContain(entry => entry.Message.Contains(ContainerId, StringComparison.Ordinal),
                              logger.Entries,
                              "No log entry may carry the container identifier");
        Assert.DoesNotContain(entry => entry.Message.Contains(PortainerBaseUrl, StringComparison.Ordinal) || entry.Message.Contains("pat-token", StringComparison.Ordinal),
                              logger.Entries,
                              "No log entry may carry the Portainer address or the API token");
    }

    /// <summary>
    /// Verify a Requested run that cannot be written sends no action, writes no run and throws
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC13")]
    public async Task ContainerActionServiceExecuteAsyncWhenRequestedRunCannotBeWrittenSendsNoActionAndThrowsAsync()
    {
        var databaseName = Guid.NewGuid().ToString();
        var seeded = await SeedAsync(databaseName, ContainerRuntimeStatus.Running, true, false).ConfigureAwait(false);
        var requests = new ConcurrentQueue<string>();
        var interceptor = new ContainerActionRunWriteFailingInterceptor(ContainerActionStatus.Requested);
        using var provider = CreateServiceProvider(databaseName, interceptor);
        using var handler = CreatePortainerHandler(requests, CreateAnswer(HttpStatusCode.NoContent));

        var service = CreateService(provider, handler, CreateOptions(null), null, new TestLogger<ContainerActionService>(), CancellationToken.None);

        await Assert.ThrowsAsync<Exception>(() => service.ExecuteAsync(seeded.InstanceId, ContainerId, ContainerActionType.RestartContainer),
                                            "A failed write of the Requested run must reach the page as an error")
                    .ConfigureAwait(false);

        var runs = await LoadRunsAsync(databaseName).ConfigureAwait(false);

        Assert.AreEqual(1, interceptor.FailedWriteCount, "The Requested run must have been attempted once");
        Assert.IsEmpty(runs, "No run may exist beyond what was written");
        Assert.DoesNotContain(request => request.StartsWith(ActionRequestPrefix, StringComparison.Ordinal), requests, "No action request may be sent without a Requested run");
    }

    /// <summary>
    /// Verify an invalid configuration at confirmation sends no action, writes no run and throws its validation error
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC13")]
    public async Task ContainerActionServiceExecuteAsyncWhenConfigurationInvalidSendsNoActionAndThrowsAsync()
    {
        var databaseName = Guid.NewGuid().ToString();
        var seeded = await SeedAsync(databaseName, ContainerRuntimeStatus.Running, true, false).ConfigureAwait(false);
        var requests = new ConcurrentQueue<string>();
        var optionsMonitor = Substitute.For<IOptionsMonitor<DockerUpdateGuardOptions>>();
        using var provider = CreateServiceProvider(databaseName, new ContainerActionRunWriteFailingInterceptor());
        using var handler = CreatePortainerHandler(requests, CreateAnswer(HttpStatusCode.NoContent));

        optionsMonitor.CurrentValue.Returns(_ => throw new OptionsValidationException(Options.DefaultName, typeof(DockerUpdateGuardOptions), ["DockerInstances:0:Portainer:BaseUrl is invalid"]));

        var service = new ContainerActionService(provider.GetRequiredService<IServiceScopeFactory>(),
                                                 CreatePortainerClient(handler, new TestLogger<PortainerClient>()),
                                                 optionsMonitor,
                                                 CreateLifetime(CancellationToken.None),
                                                 CreateConfiguration(null),
                                                 CreateClock(),
                                                 new TestLogger<ContainerActionService>());

        await Assert.ThrowsAsync<OptionsValidationException>(() => service.ExecuteAsync(seeded.InstanceId, ContainerId, ContainerActionType.RestartContainer),
                                                             "An invalid configuration must reach the page as an error")
                    .ConfigureAwait(false);

        var runs = await LoadRunsAsync(databaseName).ConfigureAwait(false);

        Assert.IsEmpty(runs, "An invalid configuration must write no run");
        Assert.IsEmpty(requests, "An invalid configuration must send nothing to Portainer");
    }

    /// <summary>
    /// Verify a failed terminal write still returns the outcome, logs the failure and leaves the container actionable
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC13")]
    public async Task ContainerActionServiceExecuteAsyncWhenTerminalWriteFailsReturnsOutcomeAndReleasesContainerAsync()
    {
        var databaseName = Guid.NewGuid().ToString();
        var seeded = await SeedAsync(databaseName, ContainerRuntimeStatus.Running, true, false).ConfigureAwait(false);
        var requests = new ConcurrentQueue<string>();
        var logger = new TestLogger<ContainerActionService>();
        var interceptor = new ContainerActionRunWriteFailingInterceptor(ContainerActionStatus.Succeeded, ContainerActionStatus.Failed, ContainerActionStatus.Cancelled);
        using var provider = CreateServiceProvider(databaseName, interceptor);
        using var handler = CreatePortainerHandler(requests, CreateAnswer(HttpStatusCode.NoContent));

        var service = CreateService(provider, handler, CreateOptions(null), null, logger, CancellationToken.None);

        var result = await service.ExecuteAsync(seeded.InstanceId, ContainerId, ContainerActionType.RestartContainer).ConfigureAwait(false);

        var availability = await service.GetAvailabilityAsync(seeded.InstanceId, ContainerId, TestContext.CancellationToken).ConfigureAwait(false);
        var runs = await LoadRunsAsync(databaseName).ConfigureAwait(false);

        Assert.AreEqual(ContainerActionStatus.Succeeded, result.Status, "The outcome must still be returned when its run cannot be finalized");
        Assert.AreEqual(RestartSucceededMessage, result.Message, "The outcome message must still be returned");
        Assert.IsGreaterThanOrEqualTo(1, interceptor.FailedWriteCount, "The terminal write must have been attempted");
        Assert.HasCount(1, runs, "The run must exist");
        Assert.AreEqual(ContainerActionStatus.Running, runs[0].Status, "The run must stay Running for the startup reconciliation");
        Assert.Contains(entry => entry.EventId.Id == 3323 && entry.LogLevel == LogLevel.Error && entry.Exception is not null,
                        logger.Entries,
                        "The failed terminal write must be logged with its exception");
        Assert.IsFalse(availability.IsInProgress, "A failed terminal write must not leave the container blocked");
    }

    /// <summary>
    /// Assert a result and the only run ended Failed with the given message and completion time
    /// </summary>
    /// <param name="result">Result of the confirmation</param>
    /// <param name="runs">Runs in the database</param>
    /// <param name="expectedMessage">Expected message</param>
    private static void AssertFailedRun(ContainerActionRunResultData result, List<ContainerActionRun> runs, string expectedMessage)
    {
        Assert.AreEqual(ContainerActionStatus.Failed, result.Status, $"The confirmation must fail with '{expectedMessage}'");
        Assert.AreEqual(expectedMessage, result.Message, "The confirmation must return the check's message");
        Assert.HasCount(1, runs, "A rejected confirmation must write exactly one run");
        Assert.AreEqual(ContainerActionStatus.Failed, runs[0].Status, "The rejected run must be Failed");
        Assert.AreEqual(expectedMessage, runs[0].ErrorMessage, "The rejected run must store the check's message");
        Assert.AreEqual(_now, runs[0].CompletedAtUtc, "The rejected run must be completed at the clock's time");
        Assert.AreEqual(RequestedByUi, runs[0].RequestedBy, "Every run is requested by the UI");
    }

    /// <summary>
    /// Parse a comma separated list of action types
    /// </summary>
    /// <param name="actions">Comma separated action type names</param>
    /// <returns>Action types</returns>
    private static List<ContainerActionType> ParseActions(string actions)
    {
        return actions.Split(',', StringSplitOptions.RemoveEmptyEntries)
                      .Select(Enum.Parse<ContainerActionType>)
                      .ToList();
    }

    /// <summary>
    /// Collect every text a credential could leak into: the result message, the run messages and the log messages and exception texts
    /// </summary>
    /// <param name="resultMessage">Result message</param>
    /// <param name="runs">Runs</param>
    /// <param name="entries">Log entries</param>
    /// <returns>Texts</returns>
    private static List<string> CollectTexts(string resultMessage, List<ContainerActionRun> runs, IEnumerable<TestLogEntry> entries)
    {
        var texts = new List<string>
                    {
                        resultMessage
                    };

        texts.AddRange(runs.Select(run => run.ErrorMessage ?? string.Empty));
        texts.AddRange(entries.Select(entry => $"{entry.Message} {entry.Exception}"));

        return texts;
    }

    /// <summary>
    /// Create a listener that collects the stopped activities of the application's activity source
    /// </summary>
    /// <param name="activities">Collected activities</param>
    /// <returns>Listener</returns>
    private static ActivityListener CreateActivityListener(ConcurrentQueue<Activity> activities)
    {
        var listener = new ActivityListener
                       {
                           ShouldListenTo = source => source.Name == DockerUpdateGuardTelemetry.ActivitySourceName,
                           Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                           ActivityStopped = activities.Enqueue,
                       };

        ActivitySource.AddActivityListener(listener);

        return listener;
    }

    /// <summary>
    /// Create the application options with one enabled Docker instance whose Portainer uses an API token and environment 7
    /// </summary>
    /// <param name="configure">Further configuration of the instance; null keeps the defaults</param>
    /// <returns>Options</returns>
    private static DockerUpdateGuardOptions CreateOptions(Action<DockerInstanceOptions>? configure)
    {
        var instance = new DockerInstanceOptions
                       {
                           Name = InstanceName,
                           BaseUrl = "https://docker.example.test",
                           Enabled = true,
                           Portainer = new PortainerOptions
                                       {
                                           Enabled = true,
                                           BaseUrl = PortainerBaseUrl,
                                           ApiToken = "pat-token",
                                           EndpointId = "7",
                                       },
                       };

        configure?.Invoke(instance);

        return new DockerUpdateGuardOptions
               {
                   DockerInstances = [instance],
               };
    }

    /// <summary>
    /// Create the options of a scenario in which no action may run
    /// </summary>
    /// <param name="scenario">Scenario name</param>
    /// <returns>Options</returns>
    private static DockerUpdateGuardOptions CreateRejectingOptions(string scenario)
    {
        return scenario switch
               {
                   "InstanceDisabled" => CreateOptions(instance => instance.Enabled = false),
                   "PortainerDisabled" => CreateOptions(instance => instance.Portainer.Enabled = false),
                   "InstanceNotConfigured" => new DockerUpdateGuardOptions(),
                   _ => CreateOptions(null),
               };
    }

    /// <summary>
    /// Create the configuration carrying the host name
    /// </summary>
    /// <param name="hostName">Value of HOSTNAME; null leaves it unset</param>
    /// <returns>Configuration</returns>
    private static IConfiguration CreateConfiguration(string? hostName)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);

        if (hostName is not null)
        {
            values["HOSTNAME"] = hostName;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values)
                                         .Build();
    }

    /// <summary>
    /// Create the clock of the service, answering a fixed time
    /// </summary>
    /// <returns>Clock</returns>
    private static TimeProvider CreateClock()
    {
        var clock = Substitute.For<TimeProvider>();

        clock.GetUtcNow().Returns(_now);

        return clock;
    }

    /// <summary>
    /// Create the application lifetime with the given stopping token
    /// </summary>
    /// <param name="stoppingToken">Token of ApplicationStopping</param>
    /// <returns>Application lifetime</returns>
    private static IHostApplicationLifetime CreateLifetime(CancellationToken stoppingToken)
    {
        var lifetime = Substitute.For<IHostApplicationLifetime>();

        lifetime.ApplicationStopping.Returns(stoppingToken);

        return lifetime;
    }

    /// <summary>
    /// Create a service provider whose scopes each hold their own context on the named in-memory database
    /// </summary>
    /// <param name="databaseName">In-memory database name</param>
    /// <param name="interceptor">Save interceptor of every context</param>
    /// <returns>Service provider</returns>
    private static ServiceProvider CreateServiceProvider(string databaseName, ContainerActionRunWriteFailingInterceptor interceptor)
    {
        var services = new ServiceCollection();

        services.AddScoped(_ => new DockerUpdateGuardDbContext(new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(databaseName)
                                                                                                                        .AddInterceptors(interceptor)
                                                                                                                        .Options));

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Create a real Portainer client over the given handler
    /// </summary>
    /// <param name="handler">Handler answering the Portainer requests</param>
    /// <param name="logger">Logger of the client</param>
    /// <returns>Portainer client</returns>
    private static PortainerClient CreatePortainerClient(HttpMessageHandler handler, TestLogger<PortainerClient> logger)
    {
        var httpClientFactory = Substitute.For<IHttpClientFactory>();

        httpClientFactory.CreateClient(PortainerClient.HttpClientName).Returns(_ => new HttpClient(handler, false));

        return new PortainerClient(httpClientFactory,
                                   logger,
                                   new LoginRetryAfterRegistry(TimeProvider.System),
                                   TimeProvider.System);
    }

    /// <summary>
    /// Create the service under test
    /// </summary>
    /// <param name="provider">Service provider holding the database</param>
    /// <param name="handler">Handler answering the Portainer requests</param>
    /// <param name="options">Current options</param>
    /// <param name="hostName">Value of HOSTNAME; null leaves it unset</param>
    /// <param name="logger">Logger of the service</param>
    /// <param name="stoppingToken">Token of ApplicationStopping</param>
    /// <returns>Service</returns>
    private static ContainerActionService CreateService(ServiceProvider provider,
                                                        HttpMessageHandler handler,
                                                        DockerUpdateGuardOptions options,
                                                        string? hostName,
                                                        TestLogger<ContainerActionService> logger,
                                                        CancellationToken stoppingToken)
    {
        return CreateService(provider.GetRequiredService<IServiceScopeFactory>(),
                             CreatePortainerClient(handler, new TestLogger<PortainerClient>()),
                             options,
                             hostName,
                             logger,
                             stoppingToken);
    }

    /// <summary>
    /// Create the service under test
    /// </summary>
    /// <param name="scopeFactory">Scope factory of the database steps</param>
    /// <param name="portainerClient">Portainer client</param>
    /// <param name="options">Current options</param>
    /// <param name="hostName">Value of HOSTNAME; null leaves it unset</param>
    /// <param name="logger">Logger of the service</param>
    /// <param name="stoppingToken">Token of ApplicationStopping</param>
    /// <returns>Service</returns>
    private static ContainerActionService CreateService(IServiceScopeFactory scopeFactory,
                                                        IPortainerClient portainerClient,
                                                        DockerUpdateGuardOptions options,
                                                        string? hostName,
                                                        TestLogger<ContainerActionService> logger,
                                                        CancellationToken stoppingToken)
    {
        return new ContainerActionService(scopeFactory,
                                          portainerClient,
                                          new TestOptionsMonitor<DockerUpdateGuardOptions>(options),
                                          CreateLifetime(stoppingToken),
                                          CreateConfiguration(hostName),
                                          CreateClock(),
                                          logger);
    }

    /// <summary>
    /// Create a handler that answers Portainer like an instance whose environment 7 holds the snapshot's container
    /// </summary>
    /// <param name="requests">Methods and paths of the requests sent</param>
    /// <param name="answerAction">Answer of the action request</param>
    /// <returns>Handler</returns>
    private static DelegateHttpMessageHandler CreatePortainerHandler(ConcurrentQueue<string> requests, Func<CancellationToken, Task<HttpResponseMessage>> answerAction)
    {
        return new DelegateHttpMessageHandler((request, token) => AnswerPortainerAsync(request, requests, answerAction, token));
    }

    /// <summary>
    /// Answer a Portainer request by its method and path
    /// </summary>
    /// <param name="request">Request</param>
    /// <param name="requests">Methods and paths of the requests sent</param>
    /// <param name="answerAction">Answer of the action request</param>
    /// <param name="cancellationToken">Token of the request</param>
    /// <returns>Response</returns>
    private static Task<HttpResponseMessage> AnswerPortainerAsync(HttpRequestMessage request,
                                                                  ConcurrentQueue<string> requests,
                                                                  Func<CancellationToken, Task<HttpResponseMessage>> answerAction,
                                                                  CancellationToken cancellationToken)
    {
        var requestKind = $"{request.Method.Method} {request.RequestUri?.AbsolutePath}";

        requests.Enqueue(requestKind);

        if (requestKind.StartsWith(ActionRequestPrefix, StringComparison.Ordinal))
        {
            return answerAction(cancellationToken);
        }

        var response = requestKind switch
                       {
                           "POST /api/auth" => CreateJsonResponse($$"""{"jwt":"{{SecretJwt}}"}"""),
                           "GET /api/system/status" => new HttpResponseMessage(HttpStatusCode.OK),
                           "GET /api/endpoints/7" => CreateJsonResponse("""{"Id":7,"Name":"local"}"""),
                           "GET " + ContainerListPath => CreateJsonResponse($$"""[{"Id":"{{ContainerId}}","Names":["/{{ContainerName}}"]}]"""),
                           _ => new HttpResponseMessage(HttpStatusCode.NotFound),
                       };

        return Task.FromResult(response);
    }

    /// <summary>
    /// Create a successful JSON response
    /// </summary>
    /// <param name="json">Response body</param>
    /// <returns>Response</returns>
    private static HttpResponseMessage CreateJsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
               {
                   Content = new StringContent(json, Encoding.UTF8, "application/json"),
               };
    }

    /// <summary>
    /// Create an answer of the action request with the given status
    /// </summary>
    /// <param name="statusCode">HTTP status</param>
    /// <returns>Answer</returns>
    private static Func<CancellationToken, Task<HttpResponseMessage>> CreateAnswer(HttpStatusCode statusCode)
    {
        return _ => Task.FromResult(new HttpResponseMessage(statusCode));
    }

    /// <summary>
    /// Create an answer of the action request that signals its start and waits for its release
    /// </summary>
    /// <param name="actionStarted">Signalled when the action request arrives</param>
    /// <param name="release">Completed by the test to let the action answer</param>
    /// <returns>Answer</returns>
    private static Func<CancellationToken, Task<HttpResponseMessage>> CreateGatedAnswer(TaskCompletionSource actionStarted, TaskCompletionSource release)
    {
        return async token =>
               {
                   actionStarted.TrySetResult();

                   await release.Task.WaitAsync(TimeSpan.FromSeconds(GateTimeoutSeconds), token).ConfigureAwait(false);

                   return new HttpResponseMessage(HttpStatusCode.NoContent);
               };
    }

    /// <summary>
    /// Answer the action request by stopping the application and waiting for the request's cancellation
    /// </summary>
    /// <param name="stopping">Source of ApplicationStopping</param>
    /// <param name="cancellationToken">Token of the request</param>
    /// <returns>Response, only when the request is not cancelled in time</returns>
    private static async Task<HttpResponseMessage> StopDuringActionAsync(CancellationTokenSource stopping, CancellationToken cancellationToken)
    {
        await stopping.CancelAsync().ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromSeconds(GateTimeoutSeconds), cancellationToken).ConfigureAwait(false);

        return new HttpResponseMessage(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// Load every action run of the named in-memory database
    /// </summary>
    /// <param name="databaseName">In-memory database name</param>
    /// <returns>Runs</returns>
    private static async Task<List<ContainerActionRun>> LoadRunsAsync(string databaseName)
    {
        var dbContext = new DockerUpdateGuardDbContext(new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(databaseName)
                                                                                                                .Options);

        await using (dbContext.ConfigureAwait(false))
        {
            return await dbContext.ContainerActionRuns.AsNoTracking()
                                                      .ToListAsync()
                                                      .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Create a finished runtime scan of the instance
    /// </summary>
    /// <param name="instance">Docker instance</param>
    /// <param name="startedAtUtc">Start time</param>
    /// <returns>Scan run</returns>
    private static ScanRun CreateRuntimeScan(DockerInstance instance, DateTimeOffset startedAtUtc)
    {
        return new ScanRun
               {
                   Type = ScanRunType.RuntimeContainer,
                   Status = ScanRunStatus.Succeeded,
                   TriggerSource = ScanTriggerSource.Scheduled,
                   DockerInstance = instance,
                   StartedAtUtc = startedAtUtc,
                   CompletedAtUtc = startedAtUtc.AddMinutes(1),
               };
    }

    /// <summary>
    /// Create a container snapshot recorded by the scan
    /// </summary>
    /// <param name="instance">Docker instance</param>
    /// <param name="imageVersion">Image version</param>
    /// <param name="scanRun">Scan run</param>
    /// <param name="containerId">Container identifier</param>
    /// <param name="status">Runtime status</param>
    /// <returns>Snapshot</returns>
    private static ContainerSnapshot CreateSnapshot(DockerInstance instance,
                                                    ImageVersion imageVersion,
                                                    ScanRun scanRun,
                                                    string containerId,
                                                    ContainerRuntimeStatus status)
    {
        return new ContainerSnapshot
               {
                   DockerInstance = instance,
                   ImageVersion = imageVersion,
                   ScanRun = scanRun,
                   ContainerId = containerId,
                   Name = containerId == ContainerId ? ContainerName : "other-container",
                   Status = status,
                   IsRunning = status == ContainerRuntimeStatus.Running,
                   RecordedAtUtc = scanRun.StartedAtUtc,
               };
    }

    /// <summary>
    /// Seed an instance with a runtime scan holding the snapshot of the test container
    /// </summary>
    /// <param name="databaseName">In-memory database name</param>
    /// <param name="status">Runtime status of the snapshot</param>
    /// <param name="withStoredEndpoint">Whether the instance has a stored Portainer endpoint</param>
    /// <param name="withNewerScan">Whether a newer runtime scan holds only another container</param>
    /// <returns>Identifiers of the instance, the snapshot and the stored endpoint</returns>
    private async Task<(Guid InstanceId, Guid SnapshotId, Guid? EndpointId)> SeedAsync(string databaseName,
                                                                                       ContainerRuntimeStatus status,
                                                                                       bool withStoredEndpoint,
                                                                                       bool withNewerScan)
    {
        var dbContext = new DockerUpdateGuardDbContext(new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(databaseName)
                                                                                                                .Options);

        await using (dbContext.ConfigureAwait(false))
        {
            var imageVersion = new ImageVersion
                               {
                                   RegistryRepository = new RegistryRepository
                                                        {
                                                            Registry = "docker.io",
                                                            Repository = "company/app",
                                                        },
                                   Tag = "1.0.0",
                                   Digest = "sha256:app",
                               };
            var instance = new DockerInstance
                           {
                               Name = InstanceName,
                               EndpointUri = "https://docker.example.test",
                               ConnectionKind = DockerConnectionKind.Https,
                               PortainerEndpoint = withStoredEndpoint
                                                       ? new PortainerEndpoint
                                                         {
                                                             Name = InstanceName,
                                                             BaseUrl = PortainerBaseUrl,
                                                             ExternalEndpointId = "7",
                                                         }
                                                       : null,
                           };
            var scanRun = CreateRuntimeScan(instance, _now.AddHours(-1));
            var snapshot = CreateSnapshot(instance, imageVersion, scanRun, ContainerId, status);

            dbContext.ImageVersions.Add(imageVersion);
            dbContext.DockerInstances.Add(instance);
            dbContext.ScanRuns.Add(scanRun);
            dbContext.ContainerSnapshots.Add(snapshot);

            if (withNewerScan)
            {
                var newerScan = CreateRuntimeScan(instance, _now.AddMinutes(-30));

                dbContext.ScanRuns.Add(newerScan);
                dbContext.ContainerSnapshots.Add(CreateSnapshot(instance, imageVersion, newerScan, OtherContainerId, ContainerRuntimeStatus.Running));
            }

            await dbContext.SaveChangesAsync(TestContext.CancellationToken)
                           .ConfigureAwait(false);

            return (instance.Id, snapshot.Id, instance.PortainerEndpoint?.Id);
        }
    }

    #endregion // Methods
}