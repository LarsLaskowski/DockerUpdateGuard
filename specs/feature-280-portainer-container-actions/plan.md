# Plan: Trigger Portainer container actions from the runtime container page and record them as action runs

Source: Issue #280 | [spec.md](spec.md)
Tier: security — the change adds guards that decide what anyone on the network may do to a container (the action allow-list, the server-side target, instance and in-progress checks) on a page reachable without sign-in.
Security areas touched: Container actions (`Portainer/`, the detail page that triggers them); Insecure Portainer HTTP (the 3311 warning in `CreateAuthenticatedClientAsync`, now reached by both new members); Secrets in configuration (Portainer credentials); Database and migrations (new run writes, a startup step, no schema change); Logging and telemetry of external data (new log events, the `portainer.action` span); Failure messages and remote text (outcome messages shown and stored)

## Problem / root cause

See [spec.md](spec.md), *Problem*. The client exists but has no caller (Verified fact 1).

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | The client has no caller, succeeds only on 2xx, looks the container up by name, uses a configured `EndpointId` unchecked and unvalidated, and bounds every request by one `HttpClient.Timeout` | `ServiceCollectionExtensions.cs:110`; `PortainerClient.cs:43-51, 215-240, 257-260, 312, 514` | confirmed |
| 2 | Snapshots store the full container `Id` and a cleaned name of at most 200 characters; the detail page loads the latest snapshot whether current or not; instances map to options by name | `DockerInstanceClient.cs:34, 520-521`; `ApplicationViewService.cs:1943`; `RuntimeContainerScanOrchestrator.cs:1199` | confirmed |
| 3 | Enum columns are integers, so new members need no migration; startup has the context and `TimeProvider` after the instance sync; `IDialogService` comes with `AddMudServices` | `ContainerActionRunConfiguration.cs`; `ApplicationInitializationExtensions.cs`; `BlazorTestContextFactory.cs:32` | confirmed |
| 4 | `CancellationTokenSource(TimeSpan, TimeProvider)` creates one timer through the provider with the bound, and firing it cancels the linked request token but not the caller's | Security's measurement in a net10.0 scratch probe (*Security review*) | confirmed |
| 5 | bUnit 2 renders with a set `RendererInfo` (it throws `MissingRendererInfoException` when a component reads one that was not set) | to verify: the AC2 prerender test of step 5, Tester | unverified |
| 6 | Through Portainer's Docker proxy, the container list honors `filters={"id":[…]}`, start or stop of a container already in that state answers 304, stop and restart answer only after the container stopped, and `GET /api/endpoints/{id}` answers 404 for an unknown environment | to verify: after merge, Product Manager on the first image with the change against a real Portainer (linked verification issue, step 10); source: Docker Engine and Portainer API references, not reachable from the session; safe either way (exact `Id` match, any non-success fails closed) | unverified |
| 7 | Docker sets `HOSTNAME` to the first 12 hexadecimal characters of the container ID unless a host name is configured | to verify: the same after-merge check; source: Docker run reference; N8 shows no warning for anything else | unverified |

## Tests per criterion

Service tests use EF Core InMemory, a real `PortainerClient` over the HTTP test handlers, and NSubstitute for
`IHostApplicationLifetime` and `TimeProvider`.

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `PortainerClientExecuteContainerActionAsyncWithUnsupportedActionTypeRejectsWithoutContactingPortainerAsync` (rows NotSet, RedeployStack, UpdateService); `PortainerClientExecuteContainerActionAsyncPostsActionPathOfActionTypeAsync` (rows start, stop, restart) | `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs` |
| AC1 | `ContainerActionTypeValuesKeepStoredNumbers` | `src/Tests/DockerUpdateGuard.Data.Tests/ContainerActionTypeTests.cs` |
| AC2 | `ContainerActionServiceGetAvailabilityAsyncOffersActionsOfSnapshotStatusAsync` (rows per `ContainerRuntimeStatus`); `…WhenPortainerOrInstanceDisabledIsNotShownAsync` (rows); `…WhenNotInLatestScanShowsNotCurrentMessageWithoutContactingPortainerAsync`; `…WhenCapabilityUnsupportedShowsItsMessageWithoutActionsAsync`; `…WhenActionInProgressShowsInProgressMessageAsync` | `src/Tests/DockerUpdateGuard.Tests/ContainerActionServiceTests.cs` |
| AC2 | `PortainerClientCheckCapabilityAsyncWithUnknownEndpointIdReportsEnvironmentNotFoundAsync` (rows: 404; `1/../../users`, `1?x=`, ` 1 ` without any request) | `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs` |
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
| AC7 | `ServiceCollectionExtensionsAddDockerUpdateGuardHostSharesContainerActionServiceAcrossScopes` | `src/Tests/DockerUpdateGuard.Tests/ServiceCollectionExtensionsTests.cs` |
| AC8 | `ContainerActionServiceExecuteAsyncWhenInstanceOrPortainerDisabledRecordsFailedWithoutContactingPortainerAsync` (rows); `…WhenEnvironmentUnknownRecordsFailedWithoutActionRequestAsync` | `src/Tests/DockerUpdateGuard.Tests/ContainerActionServiceTests.cs` |
| AC9 | `ContainerActionServiceExecuteAsyncFinalizesRunAfterCallerScopeIsDisposedAsync`; `…WhenStoppingBeforeSendRecordsCancelledAsync`; `…WhenStoppingDuringActionLeavesRunRunningAsync` | `src/Tests/DockerUpdateGuard.Tests/ContainerActionServiceTests.cs` |
| AC9 | `ContainerActionRunReconcilerReconcileInterruptedRunsAsyncFailsOpenRunsAndKeepsTerminalOnesAsync` | `src/Tests/DockerUpdateGuard.Tests/ContainerActionRunReconcilerTests.cs` |
| AC9 | `ApplicationInitializationExtensionsInitializeDockerUpdateGuardReconcilesInterruptedRunsAsync` | `src/Tests/DockerUpdateGuard.Tests/ApplicationInitializationExtensionsTests.cs` |
| AC10 | `RuntimeContainerDetailWhileActionRunsDisablesAllActionButtonsAsync`; `RuntimeContainerDetailAfterActionShowsOutcomeAndReloadsHistoryAsync`; `RuntimeContainerDetailWithoutActionRunsShowsEmptyHistoryAsync` | `src/Tests/DockerUpdateGuard.Tests/RuntimeContainerDetailTests.cs` |
| AC10 | `ApplicationViewServiceGetRuntimeContainerDetailAsyncLoadsTwentyMostRecentActionRunsOfContainerAsync` | `src/Tests/DockerUpdateGuard.Tests/ApplicationViewServiceTests.cs` |
| AC11 | `ContainerActionServiceExecuteAsyncEmitsOneActionActivityWithAllowedTagsAsync`; `ContainerActionServiceExecuteAsyncLogsOnlyAllowedValuesAsync` | `src/Tests/DockerUpdateGuard.Tests/ContainerActionServiceTests.cs` |
| AC12 | `PortainerClientExecuteContainerActionAsyncBoundsActionRequestByActionTypeAsync` (rows, each request kind its own: login `/api/auth`, `/api/system/status`, `/api/endpoints`, `/api/endpoints/{id}`, the container list, start → R; stop, restart → 300 + R); `…WhenStopExceedsBoundReportsMayHaveBeenAppliedAsync` | `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs` |
| AC13 | `ContainerActionServiceExecuteAsyncWhenRequestedRunCannotBeWrittenSendsNoActionAndThrowsAsync`; `…WhenConfigurationInvalidSendsNoActionAndThrowsAsync`; `…WhenTerminalWriteFailsReturnsOutcomeAndReleasesContainerAsync` | `src/Tests/DockerUpdateGuard.Tests/ContainerActionServiceTests.cs` |
| AC13 | `RuntimeContainerDetailFailedContainerActionShowsFixedReasonAndLogsAsync` | `src/Tests/DockerUpdateGuard.Tests/RuntimeContainerDetailTests.cs` |

## Approach

**Client (`PortainerClient`).** New members beside the old ones (removed in step 6):

- `CheckCapabilityAsync` answers an `ExternalOperationResult`:
  - Portainer disabled or without `BaseUrl` → *not configured*, with today's texts.
  - Status endpoint non-success, or environment resolution failing → *unsupported*, with today's messages.
  - A configured `EndpointId` that is not all ASCII digits counts as unknown before any request (URL path guard); otherwise it is checked with `GET /api/endpoints/{id}`:
    - 404 → *unsupported*, `Portainer environment <id> not found; check Portainer:EndpointId of Docker instance '<instance>'`;
    - any other non-success → *unsupported*, `Portainer environment <id> check failed: HTTP <status>`.
  - Success → *succeeded* with `EndpointId` set.
  - Error → *failed*, `Portainer connection error: <reason>`.
- `ExecuteContainerActionAsync`, in order:
  1. Action type other than start/stop/restart → *unsupported*, `Action '<type>' is not supported; allowed: start, stop, restart`.
  2. Container ID not exactly 64 lowercase hexadecimal characters → *not found* with E5's message (URL path guard; no request for steps 1–2).
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
  - `FailureReason.FromException` gets the bound that fired and the caller's token (`timed out after <bound> seconds`, `cancelled`). Seam for AC12: `WaitRecordingTimeProvider`'s due times.
- Logging keeps EventIds 3304–3310 with the action name `start`/`stop`/`restart`.

**Service (`ContainerActionService`, singleton).** An `IServiceScopeFactory` scope per database step, never the circuit's `DbContext`; the in-progress set is a `ConcurrentDictionary` keyed by instance and container ID (0036); instance options by name from `IOptionsMonitor.CurrentValue`.

- `GetAvailabilityAsync`:
  - not shown when the instance is unknown, missing from configuration, disabled, or has Portainer disabled;
  - otherwise not current (E6 page text, no network) when the container's latest snapshot is not in the instance's latest runtime scan run that recorded snapshots;
  - otherwise `CheckCapabilityAsync`: *not succeeded* → its message, no actions; succeeded → `EndpointId`, plus E8's page text when the set holds the container.
  - `OfferedActions`: running, paused, restarting → stop, restart; exited, not set → start; dead → none.
  - `IsOwnContainer` per N8, from `IConfiguration["HOSTNAME"]`.
- `ExecuteAsync`, in order:
  1. Read the options; a validation error propagates (E15).
  2. Load the instance and the container ID's latest snapshot; none → *failed* result without a run, logged (no page shows it).
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
- **Startup.** `InitializeDockerUpdateGuardAsync` calls `ContainerActionRunReconciler.ReconcileInterruptedRunsAsync` after the instance sync: every *Requested*/*Running* run becomes *Failed* with E10's "Interrupted" text and the current time; 3324 is logged when any changed.

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

1. Scoped service with `IDbContextFactory` (the issue's suggestion) — dies with the circuit, holds no process-wide guard, and the factory is not registered; a singleton with `IServiceScopeFactory` follows `VulnerabilityRescanControl`.
2. Database guard or partial unique index — rejected in 0036. Container stop timeout, Docker's `t`, a new key — rejected in 0037.
3. Change the old client members in place — 90 test call sites would need assertion changes in the skeleton step.
4. Keep `PortainerActionResult` — the outcome guarantee of `.squad/project.md` applies, and *unknown* is the "may have been applied" case.
5. *Running* set by a client callback when the request is sent — a state the user cannot tell apart; the spec says *Running* once Portainer is contacted.
6. Lookup by `GET /containers/{id}/json` — a new inspect reader without a real sample; the `id`-filtered list reuses `DockerContainerItem`.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| Data, Telemetry, Host | the files of *Signatures*, plus `PortainerClientLogging.cs`, `ContainerActionServiceLogging.cs`, `ServiceCollectionExtensions.cs` (singleton), `ApplicationInitializationExtensions.cs`, `ApplicationViewService.cs`, the page's `.razor` | as *Approach* says |

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
- Every existing test rendering `RuntimeContainerDetail` (`RuntimeContainerDetailTests.cs`, `…RenderTests.cs`, `…PersistentStateTests.cs`) — the Tester, step 5: an `IContainerActionService` substitute (`IsShown` false) and an interactive `RendererInfo` (per test or in `Helper/BlazorTestContextFactory.cs`); no assertion changes. Tests that open the dialog render a `MudDialogProvider` beside the page.
- `ServiceCollectionExtensionsTests.cs` and `ApplicationInitializationExtensionsTests.cs` — existing tests unchanged (no runs to reconcile); one new test each (AC7, AC9).

Files no unit test can cover: none.

## Areas

- `container-actions.md`: *Capability check* (environment check, digits-only `EndpointId`), *Actions* (three actions by ID, bounds, 304, messages, *unknown*), *Action runs* (when written, status flow, `UI`, checks, in-progress set, reconciliation, no retention, `ErrorMessage` holds every outcome message), *Transport security* (warning now reached from the page), *Limits and errors* (environment names reach only the page, HTML-encoded, and a run's stored message), *Known gaps* entry removed, 0034–0037 linked.
- `web-ui.md`: route row, *Actions* rows, "no container actions" narrowed to redeploy and update, error-prefix row `Container action failed` / `ContainerAction`.
- `hosting.md`: *Startup sequence* step "Reconcile interrupted container actions" after step 4; *Security posture* bullet on Portainer.
- `scan-engine.md`: action runs are never cleaned up and keep their snapshots. `telemetry.md`: `portainer.action` (internal, its tags) in *Traces*, out of the reserved list and *Known gaps*. `docs/areas/README.md`: start/stop/restart.

## Documentation updates

All by the Dev, against the code as built: the area documents above; `README.md` Portainer section (enabling it lets anyone who reaches the UI start, stop and restart that instance's containers; stop/restart wait 300 s beyond `RequestTimeoutSeconds`); `docs/ARCHITECTURE.md` §3 startup step, §6 `PortainerClient` row (environment check, lookup by ID, start/stop/restart, `ExternalOperationResult`), §9 `IContainerActionService` with its own scope, §10 the span, §11 container actions on Portainer-enabled instances and the in-process guard's single-instance assumption. `.squad/project.md` (the Dev reports the text, the orchestrator writes it): the *Container actions* line names `Portainer/PortainerClient.cs`, `Portainer/ContainerActionService.cs`, `Components/Pages/RuntimeContainerDetail.razor` and drops "(no page calls it today)".

## Architecture check

- No sign-in stays (guarantee); the README and §11 state the consequence.
- Fixed failure reasons: every outcome text above is composed of fixed text, configuration names, the container name and HTTP statuses.
- Layering: the service, page and component live in the host; only the enum changes in `.Data`.
- Single instance: the in-process set and startup reconciliation assume it, as the scan engine does.

## Security considerations

- [x] Limits: the stored container name is already cut to 200 (`ResourceName` 200); a container ID is accepted only as 64 lowercase hex before it reaches a URL; no other parsed input gets a new limit. No limits on decoded sizes.
- [x] Exceptions reaching a message: the client maps every error to `FailureReason` texts (`cancelled`, `timed out after <n> seconds`, `connection failed`, `invalid response`, `unexpected error`) or the authentication error. Errors of the service's database or options steps reach the page as `Container action failed: <fixed reason>` (`FailureReason.FromException` without timeout); none shows an exception message.
- Credentials stay in request headers; logs and spans carry action, container and instance names and HTTP status. Environment names appear only in the existing ambiguity message, on the page (encoded) and in a run's stored message (N6).
- Triggers: only `@onclick` handlers after the dialog; the route and query string start nothing; `ExecuteAsync` rechecks everything server-side.

## Decision records

- `docs/decisions/0034-container-actions-are-start-stop-and-restart-of-a-container.md` (Proposed)
- `docs/decisions/0035-action-runs-are-an-audit-history-written-before-portainer-is-contacted.md` (Proposed)
- `docs/decisions/0036-one-container-action-at-a-time-per-container-guarded-in-the-process.md` (Proposed)
- `docs/decisions/0037-stop-and-restart-requests-wait-up-to-300-seconds-beyond-the-request-timeout.md` (Proposed)

## Out of scope / follow-ups

- Follow-up issue (orchestrator): "[Container actions] Redeploy Portainer stacks and update Swarm services from the runtime container page", body as in the step-2a result (depends on #291; real stack/service samples; manual test).
- Verification issue (step 10): Verified facts 6–7 on the first image with the change, by the Product Manager.
- Follow-up issue (orchestrator): "[Hosting] Protect the UI against DNS rebinding with AllowedHosts and an Origin check" (*Security review*, finding 4).
- #83 (`portainer.request` spans), #36 (server time zone in the history) stay open.

## Security review

Security approved with five non-blocking findings and confirmed Verified fact 4 by measurement (a net10.0 probe: one
`CreateTimer` call with the 315 s bound; firing it cancelled the linked token, not the caller's).

1. Insecure Portainer HTTP not listed — adopted: added to *Security areas touched*.
2. A non-numeric `EndpointId` can sidestep the environment check — adopted: an `EndpointId` not all ASCII digits counts as an unknown environment before any request (*Approach*), with rows `1/../../users`, `1?x=`, ` 1 ` in the AC2 client test. No validator rule: the client guards where the value reaches a URL.
3. N6 says environment names reach only the page, but a run stores the ambiguity message — adopted: N6 in `spec.md` now names the stored message; *Areas* gives `container-actions.md` the same wording.
4. DNS rebinding reaches the circuit without `AllowedHosts` — decided: a follow-up issue (*Out of scope*). It is a perimeter gap the actions widen but do not create, and its fix (host filtering, an Origin check on the hub) changes hosting for every page.
5. Every request needs its own deadline once `HttpClient.Timeout` is infinite — adopted: the AC12 row lists each request kind.

## Challenge

The plan challenge raised three minor objections, all adopted.

1. Existing page tests break on `RendererInfo`, and dialog tests need a `MudDialogProvider` — *Test files* now orders both (fact 5 notes the exception).
2. AC7 passes with a scoped registration — added `ServiceCollectionExtensionsAddDockerUpdateGuardHostSharesContainerActionServiceAcrossScopes`.
3. No test shows that startup runs the reconciliation — added `ApplicationInitializationExtensionsInitializeDockerUpdateGuardReconcilesInterruptedRunsAsync` (seeds a *Running* and a *Succeeded* run; only the first becomes *Failed* "Interrupted").