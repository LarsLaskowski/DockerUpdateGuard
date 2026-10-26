# Plan: Pages show a fixed reason for failed user actions and log the exception

Source: Issue #323
Status: Draft
Tier: standard — a behavior change (page error text, new log entries, one service raising the marker type) that creates, changes or removes no security control: the fixed-reason mapping of record 0009 is reused, not changed.
Security areas touched: Logging and telemetry of external data (six new error log entries carry the caught exception, whose message can name registry, container or database hosts)

## Problem / root cause

Six catch blocks of user actions assign the caught exception's own message to the page's error text and log
nothing:

- `src/DockerUpdateGuard/Components/Shared/VulnerabilityRescanControl.razor.cs:89` (`RescanAsync`)
- `src/DockerUpdateGuard/Components/Pages/RuntimeContainers.razor.cs:389` (`TriggerScanAsync`)
- `src/DockerUpdateGuard/Components/Pages/RuntimeContainerDetail.razor.cs:229` (`SaveSelectionAsync`) and `:263`
  (`ClearSelectionAsync`)
- `src/DockerUpdateGuard/Components/Pages/ObservedImages.razor.cs:196` (`RegisterAsync`)
- `src/DockerUpdateGuard/Components/Pages/ScanHistory.razor.cs:206` (`TriggerVulnerabilityRefreshAsync`)

Each is `_errorMessage = exception.Message;` inside `catch (Exception exception)`, rendered by a `MudAlert` (the
rescan control: a `span`). Whatever escapes the services — a database error naming its host, an EF Core message, an
`ArgumentException` text with `(Parameter 'value')` — reaches anyone who opens the page (no login), and the operator
finds no log entry. Record 0009 fixed this for integration results and scan runs, not for the pages.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | The six catch sites at the lines the issue names assign `exception.Message` | Read the six files; `grep -n "exception.Message" src/DockerUpdateGuard/Components` lists exactly these six lines | confirmed |
| 2 | They log nothing; no component injects a logger | `grep -rn ILogger src/DockerUpdateGuard/Components` finds nothing | confirmed |
| 3 | No other catch block in the UI shows an exception message | `grep -rn catch` in `Components/`, `UI/`: only the six plus `MainLayout.razor.cs:228,246` (theme storage, no message shown) | confirmed |
| 4 | `FailureReason.FromException(exception, timeout, token)` maps `ReportableException` → its message, OCE with cancelled token → `cancelled`, other OCE/`TimeoutException` → `timed out`, `HttpRequestException`/`IOException` → `connection failed`, `JsonException` → `invalid response`, else `unexpected error`; it is `internal` in the host assembly, the components' assembly | `src/DockerUpdateGuard/Infrastructure/FailureReason.cs:9,49-84` | confirmed |
| 5 | The existing callers compose `"<what> failed: <reason>"` | `VulnerabilityEnrichmentService.cs:489` (`Vulnerability refresh failed: …`), `RuntimeContainerScanOrchestrator.cs:517`, `ImageScanOrchestrator.cs:763` | confirmed |
| 6 | The parser rejects a reference without a repository name (`:latest`, `/`, `@sha256:x`) with `ArgumentException("The image reference does not contain a repository name")`; a blank value throws through `ThrowIfNullOrWhiteSpace` and cannot come from the form (`[Required]` rejects whitespace) | `ImageReferenceParser.cs:104,116-125`; `ObservedImageRegistrationRequest.cs:15,26` | confirmed |
| 7 | `ImageRegistrationService.RegisterAsync` calls the parser before touching the database, and the Observed Images page is its only caller | `ImageRegistrationService.cs:61`; `grep -rln IImageRegistrationService src/DockerUpdateGuard` → the service, its interface, `ServiceCollectionExtensions.cs`, `ObservedImages.razor.cs` | confirmed |
| 8 | `ImageReferenceParser.Parse` has another caller that catches its `ArgumentException` | `DockerInstanceClient.cs:220-229` | confirmed |
| 9 | The tag selection throws a composed `InvalidOperationException` only when the container has no snapshot (a race with cleanup) | `RuntimeContainerTagSelectionService.cs:54-57` | confirmed |
| 10 | Two existing tests assert that the raw message is shown | `VulnerabilityRescanControlTests.cs:54-76` (`"rescan boom"`), `ScanHistoryTests.cs:99-124` (`"scan boom"`) | confirmed |
| 11 | A bUnit `BunitContext` resolves `ILogger<T>` for an `[Inject]` property without the test registering one, so the component tests that register no logger keep rendering (also pages that embed `VulnerabilityRescanControl`: My Images, Observed Images, Runtime Containers and their detail pages) | to verify: Dev, step 4 — after the skeleton adds the `Logger` properties, run *Build* and *Test*; every existing component test passes. If not, the Tester registers `NullLogger<>` for `ILogger<>` in `BlazorTestContextFactory.Create` in step 5. Hint so far: `bunit.dll` 2.12.5 references `AddLogging` and `NullLoggerFactory` (strings); `BlazorTestContextFactory.Create` registers no logger | unverified |
| 12 | Event ID 4000 is unused; application events use 1000–1010, 2000–2099, 3100–3503 | `grep -rho "EventId = [0-9]*" src/DockerUpdateGuard` | confirmed |
| 13 | `docs/areas/web-ui.md:126-127` states "The message is the error's own message"; `docs/areas/image-catalog.md:86-88` "the form shows the error message"; `docs/ARCHITECTURE.md:418-421` limits the fixed-reason rule to clients and orchestrators | Read | confirmed |
| 14 | Record 0009 is unreleased | `git merge-base --is-ancestor 0458e3c 252b2eb` (v1.3.7, latest tag of `git ls-remote --tags origin`) → not an ancestor | confirmed |

Issue claims: all confirmed (sites and lines, raw message shown, nothing logged, `FailureReason` and record 0009 as
the remedy).

## Acceptance criteria

- [ ] AC1: When a user action fails with an exception that is not a `ReportableException`, its page shows exactly
  `<prefix>: <reason>`, where `<reason>` is `FailureReason.FromException(exception, null, CancellationToken.None)`,
  and the markup does not contain the exception's message. Decided by the catch block's assignment
  `_errorMessage = $"<prefix>: {FailureReason.FromException(exception, null, CancellationToken.None)}"`. Prefixes:
  - `VulnerabilityRescanControl.RescanAsync`: `Vulnerability rescan failed`
  - `RuntimeContainers.TriggerScanAsync`: `Update check failed`
  - `RuntimeContainerDetail.SaveSelectionAsync`: `Saving the tag selection failed`
  - `RuntimeContainerDetail.ClearSelectionAsync`: `Clearing the tag selection failed`
  - `ObservedImages.RegisterAsync`: `Registration failed`
  - `ScanHistory.TriggerVulnerabilityRefreshAsync`: `Vulnerability refresh failed`
- [ ] AC2: Each of these six catch blocks logs the failure once through `UserActionFailed`: event ID `4000`, level
  `Error`, the caught exception instance as the entry's exception, the message `User action <action> failed` with
  `<action>` = `VulnerabilityRescan`, `UpdateCheck`, `SaveTagSelection`, `ClearTagSelection`,
  `RegisterObservedImage`, `VulnerabilityRefresh` (same order as AC1), on the component's `ILogger<T>`.
- [ ] AC3: The reason follows the shared mapping, not a fixed text: a `ReportableException` shows its message after
  the prefix (`Vulnerability rescan failed: <message>`), an `HttpRequestException` shows `connection failed`
  (`Vulnerability refresh failed: connection failed`).
- [ ] AC4: `ImageRegistrationService.RegisterAsync` with a reference the parser rejects (`:latest`) throws a
  `ReportableException` whose message is exactly `The image reference does not contain a repository name` and whose
  `InnerException` is the parser's `ArgumentException`; no observed image is saved. On the page this reads
  `Registration failed: The image reference does not contain a repository name` (AC1 with AC3).

## Tests per criterion

Each AC1 test also asserts AC2 (one test per catch site): the failing collaborator is an NSubstitute double throwing
an `InvalidOperationException` whose message names a fake host (`db.internal.test:5432`); a `TestLogger<T>`
(`Tests/Data`) is registered as `ILogger<T>` on the bUnit context.

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1, AC2 | `VulnerabilityRescanControlFailedRescanShowsFixedReasonAndLogsAsync` (replaces `VulnerabilityRescanControlFailedRescanShowsErrorMessageAsync`; keeps its `OnRescanned` assertion) | `src/Tests/DockerUpdateGuard.Tests/VulnerabilityRescanControlTests.cs` |
| AC1, AC2 | `RuntimeContainersFailedUpdateCheckShowsFixedReasonAndLogsAsync` | `src/Tests/DockerUpdateGuard.Tests/RuntimeContainersTests.cs` |
| AC1, AC2 | `RuntimeContainerDetailFailedSaveSelectionShowsFixedReasonAndLogsAsync`, `RuntimeContainerDetailFailedClearSelectionShowsFixedReasonAndLogsAsync` | `src/Tests/DockerUpdateGuard.Tests/RuntimeContainerDetailTests.cs` |
| AC1, AC2 | `ObservedImagesFailedRegistrationShowsFixedReasonAndLogsAsync` (fills name and reference, submits the form; the registration service double throws) | `src/Tests/DockerUpdateGuard.Tests/ObservedImagesTests.cs` (new) |
| AC1, AC2 | `ScanHistoryRefreshFailureShowsFixedReasonAndLogs` (replaces `ScanHistoryRefreshFailureShowsErrorAlert`) | `src/Tests/DockerUpdateGuard.Tests/ScanHistoryTests.cs` |
| AC3 | `VulnerabilityRescanControlReportableFailureShowsComposedMessageAsync` | `src/Tests/DockerUpdateGuard.Tests/VulnerabilityRescanControlTests.cs` |
| AC3 | `ScanHistoryRefreshConnectionFailureShowsConnectionFailedReason` (`HttpRequestException` naming `db.internal.test`; asserts the host is absent) | `src/Tests/DockerUpdateGuard.Tests/ScanHistoryTests.cs` |
| AC4 | `ImageRegistrationServiceRegisterAsyncReferenceWithoutRepositoryThrowsReportableExceptionAsync` (real `ImageReferenceParser`, SQLite database; asserts message, inner exception type and that `ObservedImages` stays empty) | `src/Tests/DockerUpdateGuard.Tests/ImageRegistrationServiceTests.cs` |

## Approach

- New `ComponentLoggingExtensions` (source-generated `[LoggerMessage]`, as `HostLoggingExtensions`) with one event,
  4000. Each of the five components gets an injected `ILogger<T>` property `Logger`. Each catch block, inside its
  existing `InvokeAsync` callback or right before it, calls `Logger.UserActionFailed(<action>, exception)` and sets
  `_errorMessage` as AC1 says. The Dev may hold each component's prefix and action in private constants.
- `ImageRegistrationService.RegisterAsync` wraps only the `_imageReferenceParser.Parse(...)` call:
  `catch (ArgumentException exception)` → `throw new ReportableException(ReferenceWithoutRepositoryMessage, exception)`.
  `ArgumentNullException` for a null request stays outside the `try`.
- Layout unchanged: the same `MudAlert` (the rescan control: the same `span`) at the same place; only its text
  changes. No new user-visible element.
- Not changed: the tag-selection service's composed "not found" text (fact 9) maps to `unexpected error`; the
  `OperationCanceledException` without a caller token maps to `timed out`, as the shared mapping does today.

## Options considered

1. **Chosen:** each catch logs and shows `<action> failed: <FailureReason>`; the registration service wraps the
   parser's rejection in `ReportableException`.
2. **A generic text without a reason** — loses the connection/timeout distinction the shared mapping gives at no
   cost; the issue asks for `FailureReason`.
3. **The shared mapping alone, no wrap** — the invalid reference, the one page error a user can fix by changing the
   input and the one `image-catalog.md` documents, would read `unexpected error`.
4. **The parser raises `ReportableException`** — changes the exception type for every caller (fact 8,
   `DockerInstanceClient` catches `ArgumentException`).
5. **A shared helper or base component for the catch** — six two-line catches; a new type buys nothing and the
   components keep their different busy-state handling.
6. **Validate the reference in the form (a custom validation attribute)** — duplicates the parser's rules in a
   second place.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Components/ComponentLoggingExtensions.cs` (new) | event 4000 `UserActionFailed` |
| DockerUpdateGuard | `Components/Shared/VulnerabilityRescanControl.razor.cs` | `Logger` property, catch block |
| DockerUpdateGuard | `Components/Pages/RuntimeContainers.razor.cs` | `Logger` property, catch block |
| DockerUpdateGuard | `Components/Pages/RuntimeContainerDetail.razor.cs` | `Logger` property, two catch blocks |
| DockerUpdateGuard | `Components/Pages/ObservedImages.razor.cs` | `Logger` property, catch block |
| DockerUpdateGuard | `Components/Pages/ScanHistory.razor.cs` | `Logger` property, catch block |
| DockerUpdateGuard | `Images/ImageRegistrationService.cs` | constant, wrap of the parse call |

No `.razor` markup changes.

## Signatures (for the Dev's skeleton)

- `internal static partial class ComponentLoggingExtensions` in namespace `DockerUpdateGuard.Components`, file
  `src/DockerUpdateGuard/Components/ComponentLoggingExtensions.cs`, with
  `[LoggerMessage(EventId = 4000, Level = LogLevel.Error, Message = "User action {Action} failed")]`
  `public static partial void UserActionFailed(this ILogger logger, string action, Exception exception);`
  (source-generated, no skeleton body).
- On each of `VulnerabilityRescanControl`, `RuntimeContainers`, `RuntimeContainerDetail`, `ObservedImages`,
  `ScanHistory`: `[Inject] public ILogger<TComponent> Logger { get; set; } = null!;` with `TComponent` the component
  itself, in its `Properties` region.
- `ImageRegistrationService`: `internal const string ReferenceWithoutRepositoryMessage = "The image reference does not contain a repository name";`
  in a `Constants` region.

The skeleton adds only these; the catch blocks and the wrap stay unchanged until step 6, so the new tests fail.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/VulnerabilityRescanControlTests.cs`, `RuntimeContainersTests.cs`,
  `RuntimeContainerDetailTests.cs`, `ScanHistoryTests.cs`, `ImageRegistrationServiceTests.cs` (existing) and
  `ObservedImagesTests.cs` (new; `ObservedImagesPersistentStateTests.cs` keeps the persistent-state tests).

Existing test code affected: no signature changes. The two tests of fact 10 assert the raw message, which AC1
forbids; the Tester replaces them in step 5 as *Tests per criterion* says. Every other test is unchanged: none feeds
a failing action. Fact 11 decides whether `BlazorTestContextFactory` needs a logger (Tester, step 5).

Files no unit test can cover: none.

## Areas

- `docs/areas/web-ui.md` — *Limits and errors*: a failed action's message is the action and a fixed reason, and the
  failure is logged (event 4000); *Related decisions*: 0009.
- `docs/areas/image-catalog.md` — *Limits and errors*: the text a rejected reference and an over-long value show.

## Documentation updates

Owned by the Dev, written once the code is green. Sweep: `grep -n -i "error message\|own message\|exception"` over
`docs/areas/*.md`, `docs/ARCHITECTURE.md`, `README.md`; the hits are the three below. `docs/areas/telemetry.md`
names only the startup and inventory event IDs, not every event, so it does not change.

- `docs/areas/web-ui.md:126-127`: replace "The message is the error's own message." by: the message names the action
  and a fixed reason (`Vulnerability rescan failed: connection failed`), never the error's own text; the reasons are
  those of [Registries](registries.md#failure-model-of-integrations), and an application-composed message (the
  rejected image reference) is passed on; the error is logged as an error with event ID 4000 and the exception. List
  the six prefixes of AC1 against the actions of the *Actions* table. Add 0009 under *Related decisions*.
- `docs/areas/image-catalog.md:86-88`: a reference without a repository name shows
  `Registration failed: The image reference does not contain a repository name`; a value longer than the stored
  limits shows `Registration failed: unexpected error`.
- `docs/ARCHITECTURE.md:418-421` (*Failure messages carry fixed reasons*): extend to the pages' user actions and link
  [Web UI](areas/web-ui.md#limits-and-errors).

## Architecture check

No guarantee of `.squad/project.md` is touched: no client or orchestrator changes, layering stays (the logging
contract lives in the host project next to its components). The fixed-reason rule of `docs/ARCHITECTURE.md` §11 is
extended, not weakened.

## Security considerations

- [x] No limits.
- [x] Exceptions that reach a page's text: every exception escaping the six actions, through `FailureReason`
  (`cancelled`, `timed out`, `connection failed`, `invalid response`, `unexpected error`). A `ReportableException`
  passes its message: the new one is the fixed constant of AC4; the existing ones (Portainer authentication, client
  certificate, named pipe) are caught inside their clients and do not reach these pages. The log entries carry the
  exception, as record 0009 accepts for the integrations.

## Decision records

- `docs/decisions/0009-integration-results-carry-fixed-failure-reasons.md` (Accepted, unreleased, extended in place):
  title, *Source* #323, context paragraph, options 9–11, one decision sentence, one consequence; index row title
  updated.

## Out of scope / follow-ups

- Related defect: `ObservedImageRegistrationRequest` validates only `[Required]`; a name over 200 characters or a
  reference part over the catalog limits fails only at the database (`unexpected error` after this change).
  Proposed follow-up issue: title "Registration form does not check the stored length limits", body: "The Observed
  Images form accepts a name over 200 characters and image reference parts over the catalog limits
  (`docs/areas/image-catalog.md`); the registration then fails at the database and the page shows
  `Registration failed: unexpected error`. Validate the lengths in `ObservedImageRegistrationRequest` so the form
  reports them before anything is sent."
- Related defect: `ObservedImages.RegisterAsync` reloads the list inside its catch block; a failing reload escapes the
  handler unhandled and breaks the page's connection. Proposed follow-up issue: title "Observed Images registration
  failure handler can throw from its list reload", body: "`ObservedImages.RegisterAsync` calls `LoadAsync()` inside
  its `catch` block; when the reload fails too (the database is unreachable), that exception escapes the submit
  handler unhandled instead of being shown as the page's error message."
