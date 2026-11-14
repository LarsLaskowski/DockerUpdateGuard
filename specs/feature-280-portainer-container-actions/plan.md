# Plan: Trigger Portainer container actions from the runtime container page and record them as action runs

Source: Issue #280 | [spec.md](spec.md)
Tier: security — the change adds guards that decide what anyone on the network may do to a container (the action allow-list, the server-side target, instance and in-progress checks) on a page reachable without sign-in.
Security areas touched: Container actions (`Portainer/`, the runtime container detail page that now triggers them); Secrets in configuration (Portainer credentials used by the new calls); Database and migrations (new writes of `ContainerActionRuns` and a startup step, no schema change); Logging and telemetry of external data (new log events, the `portainer.action` span with container names); Failure messages and remote text (new outcome messages shown on a page and stored)

## Problem / root cause

See [spec.md](spec.md), *Problem*. The client exists but has no caller (Verified facts 1–3).

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | `IPortainerClient` is registered as a singleton and never injected | `ServiceCollectionExtensions.cs:110`; no other reference outside `Portainer/` and tests | confirmed |
| 2 | The client succeeds only on 2xx, so 304 is a failure today | `PortainerClient.cs:514` | confirmed |
| 3 | The target is looked up by name in the container list; the actions are a string allow-list of six | `PortainerClient.cs:43-51, 215-240, 495` | confirmed |
| 4 | Every Portainer request runs under one `HttpClient.Timeout` of `RequestTimeoutSeconds` | `PortainerClient.cs:312` | confirmed |
| 5 | With `EndpointId` configured, the environment is used unchecked | `PortainerClient.cs:257-260, 433-438` | confirmed |
| 6 | Snapshots store the Engine's full container `Id` and a name cut to 200 characters, control characters replaced | `DockerInstanceClient.cs:34, 520-521`; `integration-failures.md` *Remote text fields* | confirmed |
| 7 | The detail page loads the latest snapshot of the container ID whether or not it is current | `ApplicationViewService.cs:1943-1945` | confirmed |
| 8 | Docker instances map to their options by name, case-insensitive | `RuntimeContainerScanOrchestrator.cs:1199` | confirmed |
| 9 | Action types, statuses and resource types are stored as integers; adding enum members needs no migration | `ContainerActionRunConfiguration.cs` (no conversion), `ContainerActionType.cs:11-26` | confirmed |
| 10 | Startup has `DockerUpdateGuardDbContext` and `TimeProvider` in scope after the instance synchronization | `ApplicationInitializationExtensions.cs` | confirmed |
| 11 | `TimeProvider` is registered as a singleton; `IDialogService` comes with `AddMudServices`, which the page tests' context already adds | `ServiceCollectionExtensions.cs:104`; `Helper/BlazorTestContextFactory.cs:32` | confirmed |
| 12 | A `CancellationTokenSource(TimeSpan, TimeProvider)` creates its timer through the provider, so `WaitRecordingTimeProvider` records each request bound | to verify: the AC12 test of step 5 records the due times, Tester | unverified |
| 13 | bUnit 2 can render the page with a non-interactive `RendererInfo`, under which the page must not call the availability check | to verify: the AC2 prerender test of step 5, Tester | unverified |
| 14 | Through Portainer's Docker proxy, the container list honors `filters={"id":[…]}`, start or stop of a container already in that state answers 304, stop and restart answer only after the container stopped, and `GET /api/endpoints/{id}` answers 404 for an unknown environment | to verify: after merge, Product Manager on the first image with the change against a real Portainer (linked verification issue, step 10); source: Docker Engine and Portainer API references, not reachable from the session; safe either way (exact `Id` match, any non-success fails closed) | unverified |
| 15 | Docker sets `HOSTNAME` to the first 12 hexadecimal characters of the container ID unless a host name is configured | to verify: the same after-merge check; source: Docker run reference; N8 shows no warning for anything else | unverified |

## Tests per criterion

Every test carries `[TestCategory("AC<n>")]`. Service tests build a `ServiceCollection` with EF Core InMemory, the real
`ContainerActionService`, a real `PortainerClient` over `SequenceHttpMessageHandler`/`DelegateHttpMessageHandler`, and
NSubstitute for `IHostApplicationLifetime` and `TimeProvider`.

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `PortainerClientExecuteContainerActionAsyncWithUnsupportedActionTypeRejectsWithoutContactingPortainerAsync` (rows NotSet, RedeployStack, UpdateService); `PortainerClientExecuteContainerActionAsyncPostsActionPathOfActionTypeAsync` (rows start, stop, restart) | `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs` |
| AC1 | `ContainerActionTypeValuesKeepStoredNumbers` | `src/Tests/DockerUpdateGuard.Data.Tests/ContainerActionTypeTests.cs` |
| AC2 | `ContainerActionServiceGetAvailabilityAsyncOffersActionsOfSnapshotStatusAsync` (rows per `ContainerRuntimeStatus`); `…WhenPortainerOrInstanceDisabledIsNotShownAsync` (rows); `…WhenNotInLatestScanShowsNotCurrentMessageWithoutContactingPortainerAsync`; `…WhenCapabilityUnsupportedShowsItsMessageWithoutActionsAsync`; `…WhenActionInProgressShowsInProgressMessageAsync` | `src/Tests/DockerUpdateGuard.Tests/ContainerActionServiceTests.cs` |
| AC2 | `PortainerClientCheckCapabilityAsyncWithUnknownEndpointIdReportsEnvironmentNotFoundAsync` | `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs` |
| AC2 | `RuntimeContainerDetailWithStaticRendererDoesNotCheckContainerActionsAsync`; `RuntimeContainerDetailRendersOfferedActionsOrAvailabilityMessageAsync` (rows) | `src/Tests/DockerUpdateGuard.Tests/RuntimeContainerDetailTests.cs` |
| AC3 | `ContainerActionConfirmationForStopShowsTextConsequenceAndDestructiveButton` (rows start, stop, restart); `ContainerActionConfirmationForOwnContainerShowsWarning` | `src/Tests/DockerUpdateGuard.Tests/ContainerActionConfirmationTests.cs` |
| AC3 | `ContainerActionServiceGetAvailabilityAsyncDetectsOwnContainerOnlyForDockerHostNameAsync` (rows: 12 lowercase hex prefix; empty; unset; `cafe`; uppercase hex; 12 hex not a prefix) | `src/Tests/DockerUpdateGuard.Tests/ContainerActionServiceTests.cs` |
| AC3 | `RuntimeContainerDetailCancelledConfirmationExecutesNothingAsync` | `src/Tests/DockerUpdateGuard.Tests/RuntimeContainerDetailTests.cs` |
| AC4 | `ContainerActionServiceExecuteAsyncWritesRequestedRunBeforeContactingPortainerAsync`; `…RecordsTerminalRunWithCompletionTimeAndMessageAsync`; `…LinksStoredEndpointOnlyWhenOneExistsAsync` (rows) | `src/Tests/DockerUpdateGuard.Tests/ContainerActionServiceTests.cs` |
| AC5 | `PortainerClientExecuteContainerActionAsyncWhenActionSucceedsReportsExecutedSuccessfullyAsync`; `…WhenEngineAnswersNotModifiedSucceedsAlreadyInStateAsync` (rows start, stop); `…WhenActionAnswersClientErrorFailsWithStatusAsync` (rows 401, 403, 404); `…WhenActionAnswersServerErrorReportsMayHaveBeenAppliedAfterOneAttemptAsync`; `…WhenActionRequestBreaksOffReportsMayHaveBeenAppliedAsync` | `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs` |
| AC5 | `ContainerActionServiceExecuteAsyncRunsMessagesAndLogsCarryNoCredentialAsync` | `src/Tests/DockerUpdateGuard.Tests/ContainerActionServiceTests.cs` |
| AC6 | `PortainerClientExecuteContainerActionAsyncWhenIdUnknownReportsContainerChangedWithoutActionAsync` (also a listed container of the same name and another ID); `…WithMalformedContainerIdRejectsWithoutContactingPortainerAsync` (rows) | `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs` |
| AC6 | `ContainerActionServiceExecuteAsyncWhenNotInLatestScanRecordsFailedWithoutContactingPortainerAsync` | `src/Tests/DockerUpdateGuard.Tests/ContainerActionServiceTests.cs` |
| AC7 | `ContainerActionServiceExecuteAsyncWithOverlappingRequestsExecutesOneAndRecordsOneInProgressAsync` | `src/Tests/DockerUpdateGuard.Tests/ContainerActionServiceTests.cs` |
| AC8 | `ContainerActionServiceExecuteAsyncWhenInstanceOrPortainerDisabledRecordsFailedWithoutContactingPortainerAsync` (rows); `…WhenEnvironmentUnknownRecordsFailedWithoutActionRequestAsync` | `src/Tests/DockerUpdateGuard.Tests/ContainerActionServiceTests.cs` |
| AC9 | `ContainerActionServiceExecuteAsyncFinalizesRunAfterCallerScopeIsDisposedAsync`; `…WhenStoppingBeforeSendRecordsCancelledAsync`; `…WhenStoppingDuringActionLeavesRunRunningAsync` | `src/Tests/DockerUpdateGuard.Tests/ContainerActionServiceTests.cs` |
| AC9 | `ContainerActionRunReconcilerReconcileInterruptedRunsAsyncFailsOpenRunsAndKeepsTerminalOnesAsync` | `src/Tests/DockerUpdateGuard.Tests/ContainerActionRunReconcilerTests.cs` |
| AC10 | `RuntimeContainerDetailWhileActionRunsDisablesAllActionButtonsAsync`; `RuntimeContainerDetailAfterActionShowsOutcomeAndReloadsHistoryAsync`; `RuntimeContainerDetailWithoutActionRunsShowsEmptyHistoryAsync` | `src/Tests/DockerUpdateGuard.Tests/RuntimeContainerDetailTests.cs` |
| AC10 | `ApplicationViewServiceGetRuntimeContainerDetailAsyncLoadsTwentyMostRecentActionRunsOfContainerAsync` | `src/Tests/DockerUpdateGuard.Tests/ApplicationViewServiceTests.cs` |
| AC11 | `ContainerActionServiceExecuteAsyncEmitsOneActionActivityWithAllowedTagsAsync`; `ContainerActionServiceExecuteAsyncLogsOnlyAllowedValuesAsync` | `src/Tests/DockerUpdateGuard.Tests/ContainerActionServiceTests.cs` |
| AC12 | `PortainerClientExecuteContainerActionAsyncBoundsActionRequestByActionTypeAsync` (rows: start R, stop and restart 300 + R; other requests R); `…WhenStopExceedsBoundReportsMayHaveBeenAppliedAsync` | `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs` |
| AC13 | `ContainerActionServiceExecuteAsyncWhenRequestedRunCannotBeWrittenSendsNoActionAndThrowsAsync`; `…WhenConfigurationInvalidSendsNoActionAndThrowsAsync`; `…WhenTerminalWriteFailsReturnsOutcomeAndReleasesContainerAsync` | `src/Tests/DockerUpdateGuard.Tests/ContainerActionServiceTests.cs` |
| AC13 | `RuntimeContainerDetailFailedContainerActionShowsFixedReasonAndLogsAsync` | `src/Tests/DockerUpdateGuard.Tests/RuntimeContainerDetailTests.cs` |

## Approach

**Client (`PortainerClient`).** New members beside the old ones (removed in step 6):

- `CheckCapabilityAsync` answers an `ExternalOperationResult`:
  - Portainer disabled or without `BaseUrl` → *not configured*, with today's texts.
  - Status endpoint non-success, or environment resolution failing → *unsupported*, with today's messages.
  - A configured `EndpointId` is checked with `GET /api/endpoints/{id}`:
    - 404 → *unsupported*, `Portainer environment <id> not found; check Portainer:EndpointId of Docker instance '<instance>'`;
    - any other non-success → *unsupported*, `Portainer environment <id> check failed: HTTP <status>`.
  - Success → *succeeded* with `EndpointId` set.
  - Error → *failed*, `Portainer connection error: <reason>`.
- `ExecuteContainerActionAsync`, in order:
  1. Action type other than start/stop/restart → *unsupported*, `Action '<type>' is not supported; allowed: start, stop, restart`.
  2. Container ID not exactly 64 lowercase hexadecimal characters → *not found* with E5's message. This is a guard on the URL path; no request is sent for either step 1 or 2.
  3. Login, environment resolution and the check above, exactly as `CheckCapabilityAsync` (its failures as *not configured*/*unsupported*/*failed*).
  4. `GET …/docker/containers/json?all=true&filters={"id":["<id>"]}`: non-success → *failed* `Action '<action>' failed: HTTP <status>`; no item whose `Id` equals the ID exactly → *not found* with E5's message.
  5. `POST …/docker/containers/<id>/<start|stop|restart>` with an empty body:
     - 2xx → *succeeded*, E1's message;
     - 304 → *succeeded* with `AlreadyInRequestedState`, E2's message;
     - 5xx → *unknown*, `Action '<action>' failed: HTTP <status>` plus E4's suffix;
     - other non-success → *failed* with the HTTP status;
     - any error raised by this request → *unknown* with the fixed reason plus the suffix.
  - Errors before step 5 → *failed*, `Action '<action>' failed: <reason>`.
- **Bounds.**
  - `HttpClient.Timeout` becomes infinite. Each request runs under a `CancellationTokenSource(bound, TimeProvider)` linked to the caller's token.
  - The bound is `RequestTimeoutSeconds`, except a stop or restart request: 300 s plus `RequestTimeoutSeconds` (0037).
  - `FailureReason.FromException` gets the bound that fired and the caller's token, so a timeout reads `timed out after <bound> seconds` and a caller cancellation `cancelled`.
  - Seam for AC12: the provider's `CreateTimer` due times (`WaitRecordingTimeProvider`).
- Logging keeps EventIds 3304–3310 with the action name `start`/`stop`/`restart`.

**Service (`ContainerActionService`, singleton).** It creates an `IServiceScopeFactory` scope per database step and never uses the circuit's `DbContext`. Its process-wide in-progress set is a `ConcurrentDictionary` keyed by instance ID and container ID (0036). Instance options are found by name from `IOptionsMonitor.CurrentValue`.

- `GetAvailabilityAsync`:
  - not shown when the instance is unknown, missing from configuration, disabled, or has Portainer disabled;
  - otherwise not current (E6 page text, no network) when the container's latest snapshot is not in the instance's latest runtime scan run that recorded snapshots;
  - otherwise `CheckCapabilityAsync`: *not succeeded* → its message, no actions; succeeded → `EndpointId`, plus E8's page text when the set holds the container.
  - `OfferedActions`: running, paused, restarting → stop, restart; exited, not set → start; dead → none.
  - `IsOwnContainer` per N8, from `IConfiguration["HOSTNAME"]`.
- `ExecuteAsync`, in order:
  1. Read the options; a validation error propagates (E15).
  2. Load the instance and the latest snapshot of the container ID. No instance or snapshot → *failed* result without a run, logged (no page shows such a container).
  3. Checks that each write one *Failed* run and return: instance missing from configuration or disabled (E14); Portainer disabled (E7); not current (E6); container in the set (E8, added atomically).
  4. Write *Requested*. Fields: instance, stored endpoint if any, the snapshot (the current one, else the latest), type, *Container*, name, `UI`, `TimeProvider` time. Then write *Running*. A failure of either releases the set entry and propagates (E15).
  5. Call the client with `IHostApplicationLifetime.ApplicationStopping` as its token (never the caller's: ExecuteAsync takes none).
  6. Finalize:
     - *succeeded* → *Succeeded*;
     - stopping requested and outcome *unknown* → no write, the run stays *Running* (E10);
     - stopping requested otherwise → *Cancelled*, E10's text;
     - else → *Failed* with the outcome message.
     - The message goes to `ErrorMessage` for every terminal status (0035), with `CompletedAtUtc`, saved with `CancellationToken.None`.
     - A failed terminal write is logged and the outcome still returned.
  7. Release the set entry in `finally`.
- One `portainer.action` activity (`ActivityKind.Internal`) per call. Tags: `docker.instance.name`, `action.type` (enum name), `container.name`, `result.status` (run status).
- Log events in `ContainerActionServiceLogging`, carrying action, container name, instance name, status:
  - 3320 requested (Information);
  - 3321 finished with status (Information);
  - 3322 rejected with the check (Warning);
  - 3323 terminal write failed (Error, exception);
  - 3324 runs reconciled with count (Information).
- **Startup.** `ContainerActionRunReconciler.ReconcileInterruptedRunsAsync`, called by `InitializeDockerUpdateGuardAsync` after the instance synchronization. It sets every *Requested*/*Running* run to *Failed*, `Interrupted: the application stopped before the action completed`, now, and logs 3324 when it changed any.

**UI layout** (new output; texts from the spec):

- A `Container actions` section (`section-paper`, h5 heading, description `Start, stop or restart this container through Portainer. Every action asks for confirmation and is recorded.`) sits directly after the container summary.
- It renders only once `GetAvailabilityAsync` answered `IsShown`. The page calls it in `OnAfterRenderAsync(firstRender)` when `RendererInfo.IsInteractive`.
- Order inside the section:
  1. the status note of S1 (`table-secondary`);
  2. one `MudAlert` with the availability message (Info for not current or in progress, Warning otherwise), or the buttons in a row (Start `Color.Primary` filled, Stop and Restart `Color.Error` outlined, all disabled while one runs, the running one with a small `MudProgressCircular`);
  3. the outcome `MudAlert` (Success when *Succeeded*, else Error);
  4. h6 `Action history` with a dense `MudTable`. Columns: Action, Status (chip: Succeeded success, Failed error, Running info, Requested warning, Cancelled default), Requested, Completed (`—` when empty), Requested by, Message. Times use `ToLocalTime().ToString("g")`. When there are no runs, an `empty-state-panel` shows S6's text.
- Display names (`Start`, `Stop`, `Restart`, `Redeploy stack`, `Update service`) come from `ContainerActionDisplayNames`.
- The dialog `ContainerActionConfirmation` is opened through `IDialogService`:
  - its `TitleContent` is S3's title; the body is S3's sentence, the consequence line, and S4's warning as a Warning `MudAlert`;
  - actions are `Cancel` (text) and the action button (`Color.Error` for stop and restart, `Color.Primary` for start);
  - Cancel or closing returns without a call.
- An exception from `ExecuteAsync` is logged as `UserActionFailed("ContainerAction", …)` and shown as `Container action failed: <fixed reason>`. An `ObjectDisposedException` after the page is gone is ignored.
- After every outcome the page calls `LoadAsync` and the availability check again.
- `ApplicationViewService.GetRuntimeContainerDetailAsync` adds `ActionHistory`: the 20 most recent runs of the instance whose snapshot has the route's container ID, newest first.

## Options considered

1. Scoped action service with `IDbContextFactory` (issue's suggestion) — scoped dies with the circuit and holds no process-wide guard; `IDbContextFactory` is not registered, and the scope pattern already exists (`VulnerabilityRescanControl`). Lost to a singleton with `IServiceScopeFactory`.
2. Guard by a non-terminal run in the database, or a partial unique index — rejected in 0036.
3. Bound by the container's stop timeout (inspect), Docker's `t`, or a new key — rejected in 0037.
4. Change `GetCapabilityAsync`/`ExecuteActionAsync` in place — 90 call sites in `PortainerClientTests` would stop compiling with assertion changes the Dev may not make; new members coexist until the Tester moved the tests.
5. Keep `PortainerActionResult` instead of `ExternalOperationResult` (the issue allows a recorded reason) — the guarantee of `.squad/project.md` applies, and *unknown* is exactly the "may have been applied" case.
6. *Running* when the action request is sent, through a callback from the client — two writes inside the client's call for a state the user cannot tell apart; the spec now says *Running* once Portainer is contacted, and *Cancelled* versus left open follows from the outcome.
7. Container lookup by `GET /containers/{id}/json` — a new reader of the inspect format without a real sample; the list with an `id` filter uses the existing `DockerContainerItem`.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| Data | `Entities/ContainerActionType.cs` | two appended members |
| Telemetry | `TelemetryTagNames.cs` | `container.name` |
| Host | `Portainer/PortainerClient.cs`, `PortainerClientLogging.cs`, `Interfaces/IPortainerClient.cs`, `Data/PortainerCapabilityData.cs`, new `Data/PortainerContainerActionRequest.cs`, `Data/PortainerActionOutcomeData.cs`; deleted `Data/PortainerActionRequest.cs`, `Data/PortainerActionResult.cs` | new contract |
| Host | new `Portainer/Interfaces/IContainerActionService.cs`, `Portainer/ContainerActionService.cs`, `Portainer/ContainerActionServiceLogging.cs`, `Portainer/ContainerActionRunReconciler.cs`, `Portainer/Data/ContainerActionAvailabilityData.cs`, `Portainer/Data/ContainerActionRunResultData.cs` | service |
| Host | `ServiceCollectionExtensions.cs`, `ApplicationInitializationExtensions.cs` | singleton registration, startup step |
| Host | `UI/RuntimeContainerDetailViewData.cs`, `UI/ApplicationViewService.cs`, new `UI/ContainerActionRunViewData.cs`, `UI/ContainerActionDisplayNames.cs` | history |
| Host | `Components/Pages/RuntimeContainerDetail.razor(.cs)`, new `Components/Common/ContainerActionConfirmation.razor(.cs)` | section, dialog |

## Signatures (for the Dev's skeleton)

- `StartContainer = 4` and `StopContainer = 5` appended to ContainerActionType
- `public const string ContainerName = "container.name";` in TelemetryTagNames
- `public PortainerClient(IHttpClientFactory httpClientFactory, ILogger<PortainerClient> logger, LoginRetryAfterRegistry loginRetryAfterRegistry, TimeProvider timeProvider)`
- `Task<ExternalOperationResult<PortainerCapabilityData>> CheckCapabilityAsync(DockerInstanceOptions instanceOptions, CancellationToken cancellationToken = default);` on IPortainerClient and PortainerClient
- `Task<ExternalOperationResult<PortainerActionOutcomeData>> ExecuteContainerActionAsync(DockerInstanceOptions instanceOptions, PortainerContainerActionRequest actionRequest, CancellationToken cancellationToken = default);` on IPortainerClient and PortainerClient
- `public string? EndpointId { get; set; }` on PortainerCapabilityData
- `public class PortainerContainerActionRequest` with `public ContainerActionType ActionType { get; set; }`, `public string ContainerId { get; set; } = string.Empty;`, `public string ContainerName { get; set; } = string.Empty;`
- `public class PortainerActionOutcomeData` with `public bool AlreadyInRequestedState { get; set; }`, `public string Message { get; set; } = string.Empty;`
- `public interface IContainerActionService` with `Task<ContainerActionAvailabilityData> GetAvailabilityAsync(Guid dockerInstanceId, string containerId, CancellationToken cancellationToken = default);` and `Task<ContainerActionRunResultData> ExecuteAsync(Guid dockerInstanceId, string containerId, ContainerActionType actionType);`
- `public sealed class ContainerActionService : IContainerActionService` with `public ContainerActionService(IServiceScopeFactory serviceScopeFactory, IPortainerClient portainerClient, IOptionsMonitor<DockerUpdateGuardOptions> optionsMonitor, IHostApplicationLifetime applicationLifetime, IConfiguration configuration, TimeProvider timeProvider, ILogger<ContainerActionService> logger)`
- `public class ContainerActionAvailabilityData` with `public bool IsShown { get; set; }`, `public bool IsCurrent { get; set; }`, `public bool IsSupported { get; set; }`, `public bool IsInProgress { get; set; }`, `public bool IsOwnContainer { get; set; }`, `public string? EndpointId { get; set; }`, `public string? Message { get; set; }`, `public IReadOnlyList<ContainerActionType> OfferedActions { get; set; } = [];`
- `public class ContainerActionRunResultData` with `public ContainerActionStatus Status { get; set; }`, `public string Message { get; set; } = string.Empty;`
- `public static class ContainerActionRunReconciler` with `public static Task<int> ReconcileInterruptedRunsAsync(DockerUpdateGuardDbContext dbContext, TimeProvider timeProvider, CancellationToken cancellationToken)`
- `public class ContainerActionRunViewData` with `public string Action { get; set; } = string.Empty;`, `public string Status { get; set; } = string.Empty;`, `public DateTimeOffset RequestedAtUtc { get; set; }`, `public DateTimeOffset? CompletedAtUtc { get; set; }`, `public string? RequestedBy { get; set; }`, `public string? Message { get; set; }`
- `public IReadOnlyList<ContainerActionRunViewData> ActionHistory { get; set; } = [];` on RuntimeContainerDetailViewData
- `public static class ContainerActionDisplayNames` with `public static string GetDisplayName(ContainerActionType actionType)`
- `public IContainerActionService ContainerActionService { get; set; } = null!;` and `public IDialogService DialogService { get; set; } = null!;`, both with the Inject attribute, on RuntimeContainerDetail
- `public sealed partial class ContainerActionConfirmation` with `public IMudDialogInstance MudDialog { get; set; } = null!;` (a cascading parameter), `public ContainerActionType ActionType { get; set; }`, `public string ContainerName { get; set; } = string.Empty;`, `public string DockerInstanceName { get; set; } = string.Empty;`, `public string EndpointId { get; set; } = string.Empty;`, `public bool IsOwnContainer { get; set; }` (parameters)
- Deleted in step 6, after the Tester moved their tests: GetCapabilityAsync and ExecuteActionAsync of IPortainerClient and PortainerClient, the classes PortainerActionRequest and PortainerActionResult, and the private name lookup.

## Test files

New: `ContainerActionServiceTests.cs`, `ContainerActionRunReconcilerTests.cs`, `ContainerActionConfirmationTests.cs` under
`src/Tests/DockerUpdateGuard.Tests/`; `ContainerActionTypeTests.cs` under `src/Tests/DockerUpdateGuard.Data.Tests/`.
Extended: `PortainerClientTests.cs`, `RuntimeContainerDetailTests.cs`, `ApplicationViewServiceTests.cs`.

Existing call sites:

- 36 `new PortainerClient(…)` in `PortainerClientTests.cs` — the Dev adds `TimeProvider.System` in step 4 (mechanical).
- The 53 tests over `GetCapabilityAsync`/`ExecuteActionAsync` and the helpers `RunFailingActionAsync`/`RunFailingCapabilityAsync` — the Tester moves them in step 5 to the new members, asserting outcome status and message:
  - tests of the removed behavior (allow-list `remove`, lookup by name, "not found in endpoint" text) become their ID-based counterparts or are deleted;
  - the timeout tests keep 15 seconds for login and lookup.
- `RuntimeContainerDetailTests.cs`, `RuntimeContainerDetailRenderTests.cs`, `RuntimeContainerDetailPersistentStateTests.cs` register no `IContainerActionService` — the Tester adds a substitute (returning `IsShown` false) in step 5. Their inputs have no Portainer, so no assertion changes.
- `ServiceCollectionExtensionsTests.cs:114` and `ApplicationInitializationExtensionsTests.cs` — unchanged. Their inputs resolve the client with the registered `TimeProvider`, and startup finds no runs to reconcile.

Files no unit test can cover: none.

## Areas

- `container-actions.md`: *Capability check* (environment check), *Actions* (three actions by ID, bounds, 304, messages, *unknown*), *Action runs* (when written, status flow, `UI`, checks, in-progress set, reconciliation, no retention, `ErrorMessage` holds every outcome message), *Transport security* (the warning now appears once a page checks Portainer), *Known gaps* entry removed, decisions 0034–0037 linked.
- `web-ui.md`: route table row, *Actions* rows for Start/Stop/Restart, the "no container actions" sentence narrowed to redeploy and update, error-prefix row `Container action failed` / `ContainerAction`.
- `hosting.md`: *Startup sequence* step "Reconcile interrupted container actions" after step 4; *Security posture* bullet on Portainer.
- `scan-engine.md`: action runs are never cleaned up and keep their snapshots.
- `telemetry.md`: *Traces* table gains `portainer.action` (internal kind, its tags), reserved list and *Known gaps* drop it.
- `docs/areas/README.md`: Container actions row says start/stop/restart.

## Documentation updates

All by the Dev, against the code as built:

- The area documents above.
- `README.md`: Portainer section — what enabling it allows (start, stop, restart from the runtime container page for anyone who reaches the UI) and that `RequestTimeoutSeconds` bounds each request, stop/restart 300 s longer.
- `docs/ARCHITECTURE.md`:
  - §3 the startup step;
  - §5.1 nothing (row stays true);
  - §6 `PortainerClient` row (capability with environment check, container lookup by ID, start/stop/restart, `ExternalOperationResult`);
  - §9 `IContainerActionService` beside the view service, and its own scope;
  - §10 the span;
  - §11 the no-sign-in bullet names container actions on Portainer-enabled instances and the in-process guard's single-instance assumption.
- `.squad/project.md` — the Dev reports the exact text; the orchestrator writes it:
  - *Container actions* line becomes `Portainer/PortainerClient.cs`, `Portainer/ContainerActionService.cs`, `Components/Pages/RuntimeContainerDetail.razor` without "(no page calls it today)";
  - *Integration surface* needs nothing.

## Architecture check

- No sign-in stays (guarantee); the README and §11 state the consequence.
- Fixed failure reasons: every outcome text above is composed of fixed text, configuration names, the container name and HTTP statuses.
- Layering: the service, page and component live in the host; only the enum changes in `.Data`.
- Single instance: the in-process set and startup reconciliation assume it, as the scan engine does.

## Security considerations

- [x] Limits: the stored container name is already cut to 200 (`ResourceName` 200); a container ID is accepted only as 64 lowercase hex before it reaches a URL; no other parsed input gets a new limit. No limits on decoded sizes.
- [x] Exceptions reaching a message: the client maps every error to `FailureReason` texts (`cancelled`, `timed out after <n> seconds`, `connection failed`, `invalid response`, `unexpected error`) or the authentication error. Errors of the service's database or options steps reach the page as `Container action failed: <fixed reason>` (`FailureReason.FromException` without timeout); none shows an exception message.
- Credentials stay in request headers; runs, messages, logs and spans carry action, container and instance names, endpoint ID and HTTP status. Environment names appear only in the existing ambiguity message, rendered encoded by MudBlazor.
- Triggers: only `@onclick` handlers after the dialog; the route and query string start nothing; `ExecuteAsync` rechecks everything server-side.

## Decision records

- `docs/decisions/0034-container-actions-are-start-stop-and-restart-of-a-container.md` (Proposed)
- `docs/decisions/0035-action-runs-are-an-audit-history-written-before-portainer-is-contacted.md` (Proposed)
- `docs/decisions/0036-one-container-action-at-a-time-per-container-guarded-in-the-process.md` (Proposed)
- `docs/decisions/0037-stop-and-restart-requests-wait-up-to-300-seconds-beyond-the-request-timeout.md` (Proposed)

## Out of scope / follow-ups

- Follow-up issue (orchestrator): "[Container actions] Redeploy Portainer stacks and update Swarm services from the runtime container page", body as in the step-2a result (depends on #291; real stack/service samples; manual test).
- Verification issue (step 10): Verified facts 14–15 on the first image with the change, by the Product Manager.
- #83 (`portainer.request` spans), #36 (server time zone in the history) stay open.

## Security notes

I'm approving the plan for feature #280 (Start/Stop/Restart through Portainer, option A). I found no blocking defects and four non-blocking ones.

**Files reviewed:** `specs/feature-280-portainer-container-actions/spec.md`, `plan.md` and `tasks.md`, decision records 0034–0037, `docs/areas/container-actions.md`, *Security posture* in `docs/areas/hosting.md`, *Credentials and external data* in `docs/areas/telemetry.md`, `src/DockerUpdateGuard/Portainer/PortainerClient.cs`, `Infrastructure/TransientHttpRetryHandler.cs`, `Infrastructure/FailureReason.cs`, the Portainer section of `Configuration/DockerUpdateGuardOptionsValidator.cs`, `ContainerActionRunConfiguration.cs`, and the snapshot load in `ApplicationViewService.GetRuntimeContainerDetailAsync`.

**Tier:** `security` is correct. The plan adds the guards that decide what anyone on the network may do to a container: the allow-list by action type, the 64-hex container ID guard, the server-side recheck at confirmation, and the in-process guard.

## Measurement (Verified fact 12, the AC12 seam)

A per-request deadline is now the only time bound, because `HttpClient.Timeout` becomes infinite. So I checked that `CancellationTokenSource(TimeSpan, TimeProvider)` creates its timer through the provider and cancels a linked token when that timer fires. I ran this in the scratch project `/tmp/claude-0/-home-user-DockerUpdateGuard/3f282fb3-74f5-58ed-8975-902bfeaa0df0/scratchpad/ctsprobe` (net10.0, the repository's target framework):

`cd …/scratchpad/ctsprobe && dotnet run`

The program builds the token source with 315 s and a recording `TimeProvider` (it records each `CreateTimer` due time and fires the stored callback on demand). It links that token with an outer one and then fires the callback. Output:
```
CreateTimer calls: 1; due: 00:05:15; cancelled before fire: False
after fire: cts=True linked=True outer=False
```
Fact 12 holds: one `CreateTimer` call carrying the bound, and firing it cancels the linked request token but not the caller's. The `WaitRecordingTimeProvider` seam is sound. The plan can mark fact 12 confirmed, citing this measurement.

## Checks that pass
- **Untrusted input in URLs:** the container ID reaches a URL path only as exactly 64 lowercase hex characters (step 2). The list lookup matches `Id` exactly, so Docker's prefix-matching `id` filter and an ignored filter (fact 14, unverified) both fail closed. No container is matched by name any more.
- **No retry of a processed action:** the stop/restart POST stays under decision 0008. `TransientHttpRetryHandler` retries a non-idempotent request only when it cannot have been processed. Once the per-request linked token is cancelled it stops retrying (`cancellationToken.IsCancellationRequested == false` in its catch filter), and 5xx or a transport error after sending maps to *unknown*.
- **Fixed failure reasons:** every outcome goes through `FailureReason.FromException` or fixed text. The page error is `Container action failed: <fixed reason>`, and the exception goes only to the log (allowed for user-action error entries by telemetry.md).
- **Credentials:** they stay in request headers. Logs and spans carry only action, container name (control characters already replaced at scan), instance name and HTTP status, which telemetry.md allows. AC5 and AC11 pin this.
- **Database:** `ErrorMessage` is `text`, so no truncation or save failure from long messages. The snapshot load is scoped by instance and container ID, and a mismatched pair is caught by the not-current check.
- **Triggers:** only an interactive `@onclick` after the dialog can start an action. No route or prerender path, and `ExecuteAsync` rechecks everything on the server.
- **Insecure HTTP warning:** EventId 3311 sits in the shared `CreateAuthenticatedClientAsync`, so both new members inherit it. The type and ID guards run before it, which matches *Transport security* ("no warning when rejected by the allow-list").

## Findings (all non-blocking)

1. **[NON-BLOCKING] "Insecure Portainer HTTP" is touched but not listed.** `plan.md` line 5 omits it. The plan edits `PortainerClient.CreateAuthenticatedClientAsync`, the method that holds both the 3311 warning and the `client.Timeout` line it replaces. It also lists a *Transport security* update in *Areas*. `.squad/project.md` names `Portainer/PortainerClient.cs` for this area.
   - **Smallest fix:** add `Insecure Portainer HTTP (the 3311 warning, now reached by CheckCapabilityAsync and ExecuteContainerActionAsync)` to *Security areas touched*.
   - **Recommendation:** fix now.

2. **[NON-BLOCKING] The environment check can be sidestepped by a non-numeric `EndpointId`.** The value is never validated (only `IsNullOrWhiteSpace`, `PortainerClient.cs:257-260`; no rule in the validator). The new `GET /api/endpoints/{id}` puts it into the path unescaped. Forms Portainer would accept differently:
   - dot segments: `1/../../users` normalizes to `GET /api/users`, which answers 200, so *supported*;
   - a query or fragment: `1?x=` or `1#` cuts off the rest of the action path;
   - leading/trailing whitespace or percent-encoded dot segments.

   The action itself still fails closed: the container lookup or POST lands on another path and answers non-success, and only an operator sets the key. But the new check would report *supported* and the dialog would name an environment that does not exist.
   - **Smallest fix, with no new configuration rule:** in the client, treat an `EndpointId` that is not all ASCII digits like an unknown environment (*unsupported*, `Portainer environment <id> not found; …`) before any request. Add a row to `PortainerClientCheckCapabilityAsyncWithUnknownEndpointIdReportsEnvironmentNotFoundAsync` (`1/../../users`, `1?x=`, ` 1 `).
   - **Recommendation:** fix now (one guard beside the container ID guard). Alternatively, a linked issue for a validator rule.

3. **[NON-BLOCKING] N6 says environment names reach "only the page", but the plan also stores them.**
   - **What happens:** at confirmation, step 3 can end with the ambiguity message (after a config reload, or `X-Total-Count` changes since page load). That message holds environment names and goes into the run's `ErrorMessage`, from where the history table shows it. The plan's *Security considerations* bullet admits the message reaches the page but not that it is stored.
   - **Impact:** harmless in itself (names are already sanitized and cut to 64; MudTable encodes them). But the statement in the spec and in container-actions.md *Limits and errors* would be untrue.
   - **Smallest fix:** N6 reads `Portainer environment names reach only the page, HTML-encoded, and the run's stored message.` The Dev's container-actions.md text says the same.
   - **Recommendation:** fix now (wording).

4. **[NON-BLOCKING] Exposure to DNS rebinding grows.** No `AllowedHosts` is configured (nothing in `src/DockerUpdateGuard/appsettings*.json`). A web page an operator visits inside the trusted perimeter can therefore rebind its own host name to the app, read the prerendered circuit descriptors as same-origin, and drive the Blazor circuit. Before this change that reached scans and tag selection; with it, Stop/Restart of every container on Portainer-enabled instances. This is a perimeter-hardening gap, not a defect of this plan.
   - **Recommendation:** a linked issue: document `AllowedHosts` in the README and *Security posture*, and/or check the hub's Origin. Optionally the README Portainer sentence this plan adds also names `AllowedHosts`.

5. **[NON-BLOCKING] The time bound now rests on every request having its own deadline.** With `HttpClient.Timeout` infinite, any Portainer request that misses its per-request token source waits without limit. The AC12 row "other requests R" should therefore list each kind separately so a missed one fails a test: login (`/api/auth`), `/api/system/status`, `/api/endpoints`, `/api/endpoints/{id}`, the container list, plus start.
   - **Recommendation:** fix now, in the plan's AC12 row (Tester).

## Not measured
- Facts 13–15 are not security properties: each fails closed, so there is no gate on approval. Fact 13 (bUnit static renderer) is the Tester's; facts 14–15 are the after-merge real-Portainer check.

## Challenge notes

Devil's Advocate, mode `plan`: feature #280 (specs/feature-280-portainer-container-actions/)

The plan holds up on substance. I checked its claims against the code and found none that fail:
- No caller exists for `IPortainerClient`.
- Today's client treats 304 as a failure: it succeeds only on 2xx (`PortainerClient.cs:514`).
- The container is found by name (`PortainerClient.cs:215-240`).
- A configured `EndpointId` is used without any check (`:257-260`).
- `ExternalOperationStatus.Unknown` exists (`Infrastructure/ExternalOperationStatus.cs:41`).
- `FailureReason.FromException` already tells "cancelled" (the caller's token) from "timed out after <n> seconds".
- A per-request bound keeps today's documented meaning, "each request including its retries" (`docs/areas/container-actions.md:105`). The retry handler sits inside the `HttpClient` (`ServiceCollectionExtensions.cs` named client `Portainer` + `TransientHttpRetryHandler`), so the linked token still covers the retries. POST is retried only on 429 or failures raised before sending.
- Snapshots keep the Engine's full `Id` (`DockerInstanceClient.cs:520`). The "current set" definition matches `docs/areas/docker-instances.md:121-123`.
- The test counts are right: 36 `new PortainerClient(` lines, 90 old-member call sites and 53 test methods in `PortainerClientTests.cs`.
- `WaitRecordingTimeProvider` records `CreateTimer` due times, which is the seam the AC12 test needs.
- Event IDs 3320–3324 are free (the highest used is 3313), and `TelemetryActivityNames.PortainerAction` ("portainer.action") already exists.

I found nothing that should be redesigned. Three test gaps remain, all minor:

1. **minor — the existing page tests will break, not stay unchanged.** In bUnit 2.15.2, a component that reads `RendererInfo` without the test calling `SetRendererInfo` throws `Bunit.Rendering.MissingRendererInfoException` (bunit.xml in the package). No test under `src/Tests` calls `SetRendererInfo` today. The plan (line 199) says `RuntimeContainerDetailTests.cs`, `RuntimeContainerDetailRenderTests.cs` and `RuntimeContainerDetailPersistentStateTests.cs` only need an `IContainerActionService` substitute.
   Instruction for the Tester: in every existing test that renders `RuntimeContainerDetail`, also set an interactive `RendererInfo` on the bUnit context (per test or in `Helper/BlazorTestContextFactory.cs`). The AC2 prerender test sets a non-interactive one. The dialog tests (AC3, and the AC10/AC13 page tests that confirm) must render a `MudDialogProvider` next to the page, because the provider sits only in `MainLayout.razor:5`.

2. **minor — AC7 passes on a wrong build.** The one-action-per-container guard is a set inside the service (0036), so it only works across tabs or circuits if `IContainerActionService` is registered as a singleton. The planned AC7 test builds one service instance directly. If the service were registered as scoped, it would still pass, while two tabs would each get their own set.
   Instruction for the Tester: add a test with `[TestCategory("AC7")]` to `ServiceCollectionExtensionsTests.cs` that resolves `IContainerActionService` from two scopes of `AddDockerUpdateGuardHost` and asserts both are the same instance. The existing shared-singleton test at `ServiceCollectionExtensionsTests.cs:92-119` shows the pattern.

3. **minor — no test checks that startup actually runs the reconciliation.** AC9 is covered only by a test of the static `ContainerActionRunReconciler.ReconcileInterruptedRunsAsync`. The plan (line 200) keeps `ApplicationInitializationExtensionsTests.cs` unchanged, and the existing startup test already executes `InitializeDockerUpdateGuardAsync`. So if the call were left out of `ApplicationInitializationExtensions.cs` (after the instance sync at line 49-50), every test would still pass and coverage would not notice.
   Instruction for the Tester: add a test with `[TestCategory("AC9")]` to `ApplicationInitializationExtensionsTests.cs`. It uses the InMemory database of line 33, seeds one *Running* and one *Succeeded* run for a configured instance, calls `InitializeDockerUpdateGuardAsync`, and asserts that the open run became *Failed* with E10's "Interrupted" message and that the terminal run is unchanged.

Files:
- /home/user/DockerUpdateGuard/specs/feature-280-portainer-container-actions/plan.md
- /home/user/DockerUpdateGuard/specs/feature-280-portainer-container-actions/tasks.md
- /home/user/DockerUpdateGuard/src/Tests/DockerUpdateGuard.Tests/ServiceCollectionExtensionsTests.cs
- /home/user/DockerUpdateGuard/src/Tests/DockerUpdateGuard.Tests/ApplicationInitializationExtensionsTests.cs
- /home/user/DockerUpdateGuard/src/Tests/DockerUpdateGuard.Tests/Helper/BlazorTestContextFactory.cs
- /home/user/DockerUpdateGuard/src/DockerUpdateGuard/ApplicationInitializationExtensions.cs
