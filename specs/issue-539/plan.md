# Plan: List pages show a failed first load inline

Source: Issue #539
Tier: standard — the eight list and overview pages change how a failed first load behaves (inline alert instead of the error page or a dead circuit); no security control is created or changed.
Security areas touched: Failure messages and remote text (the pages now carry a load failure reason built by `Infrastructure/FailureReason.cs`)

## Problem / root cause

Every list or overview page awaits its view-service call in `OnInitializedAsync` without a `try`/`catch`
(paths under `src/DockerUpdateGuard/Components/Pages/`): `Dashboard.razor.cs:184`, `MyImages.razor.cs:186`,
`ObservedImages.razor.cs:262`, `RuntimeContainers.razor.cs:432`, `DockerInstances.razor.cs:161`,
`ScanHistory.razor.cs:249`, `SharedBaseImages.razor.cs:106` and `Vulnerabilities.razor.cs:219` (inline loads). An
exception escapes the lifecycle method: on the prerender `UseExceptionHandler("/error")` (`Program.cs:32`) renders
`/error`, on the interactive render the circuit ends. The detail pages were fixed in #40 with `DetailPageLoader` and
`DetailLoadState` ([decision 0059](../../docs/decisions/0059-a-detail-page-shows-a-missing-record-or-a-failed-load-inline.md)).

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | The issue's pages (`ObservedImages`, `MyImages`, `RuntimeContainers`, `DockerInstances`) load unguarded in `OnInitializedAsync` | the lines above; the "…" also covers `Dashboard`, `ScanHistory`, `SharedBaseImages`, `Vulnerabilities`, same pattern | confirmed |
| 2 | A prerender exception renders `/error` outside Development | `Program.cs:32` | confirmed |
| 3 | A failed first load persists nothing, so the interactive render loads again | each `Persist*` method writes only a non-null field (e.g. `ObservedImages.razor.cs:168`) | confirmed |
| 4 | `DetailPageLoader.LoadAsync` catches every exception, logs `UserActionFailed` (event 4000) and returns `<prefix>: <FailureReason>`; an `InvalidOperationException` gives `unexpected error` | `Components/DetailPageLoader.cs`, `FailureReason.cs:36`, `DetailPageLoaderTests` | confirmed |
| 5 | `DetailPageLoader` has one production caller (`DetailLoadState.cs:56`) and one test file (`DetailPageLoaderTests.cs`); no document names it | `grep -rn DetailPageLoader src docs` | confirmed |
| 6 | `Dashboard`, `MyImages`, `DockerInstances`, `SharedBaseImages`, `Vulnerabilities` inject no logger; bUnit resolves an injected `ILogger<T>` without registration | `grep ILogger< Pages/*.razor.cs`; `RuntimeContainersPersistentStateTests` renders `RuntimeContainers` (which injects one) without registering a logger | confirmed |
| 7 | Reloads after user actions are already guarded: rescan reloads run inside `VulnerabilityRescanControl.RescanAsync`'s `try`; `RuntimeContainers.TriggerScanAsync`, `ScanHistory.TriggerVulnerabilityRefreshAsync` and `ObservedImages.ReloadAfterFailedRegistrationAsync` catch theirs | `VulnerabilityRescanControl.razor.cs:89`, the named methods | confirmed |
| 8 | Unguarded loads outside this scope (follow-up): the dashboard's refresh signal (`Dashboard.OnDashboardRefreshRequested`, `_ = InvokeAsync(LoadAsync)`: a failure is unobserved and unlogged) and the layout (`MainLayout.OnInitializedAsync`/`LoadSummaryAsync`, `NavMenu.OnInitializedAsync`) | the named members | confirmed |
| 9 | Decision 0059 is unreleased | `git tag --contains 6642a68` prints nothing; newest tag `v1.3.9` | confirmed |

## Acceptance criteria

The eight pages and their texts:

| Page | Prefix | Logged action |
| --- | --- | --- |
| Dashboard | `Loading the dashboard failed` | `LoadDashboard` |
| My Images | `Loading the images failed` | `LoadMyImages` |
| Observed Images | `Loading the observed images failed` | `LoadObservedImages` |
| Runtime Containers | `Loading the containers failed` | `LoadRuntimeContainers` |
| Docker Instances | `Loading the Docker instances failed` | `LoadDockerInstances` |
| Shared Base Images | `Loading the shared base images failed` | `LoadSharedBaseImages` |
| Vulnerabilities | `Loading the vulnerabilities failed` | `LoadVulnerabilities` |
| Scan History | `Loading the scan history failed` | `LoadScanHistory` |

- AC1: When the first load of one of the eight pages (no restored state) throws, the page renders (no exception
  escapes the render) with exactly one error alert `<prefix>: <reason>`, `<reason>` from
  `FailureReason.FromException` (`unexpected error` for an `InvalidOperationException`), never the exception's text,
  and neither the skeleton nor the empty-state panel nor any row. Decided by: `PageLoader.LoadAsync` returning a
  non-null message, and the markup branch "list field is null and `_loadErrorMessage` is not null".
- AC2: That failure is logged exactly once as event 4000 `User action <logged action> failed` at level Error with the
  caught exception.
- AC3: A page whose load makes two view-service calls shows no part of the data when the second one fails: Docker
  Instances shows no single-instance section when `GetDockerInstanceDetailAsync` throws; Scan History shows no
  `Next scheduled vulnerability refresh` caption when `GetVulnerabilityScheduleAsync` throws, and its *Refresh
  vulnerabilities* button stays enabled.
- AC4: After a failed first load, a user action whose reload succeeds shows the list in place of the load alert
  (Runtime Containers: *Check image updates* with a succeeding scan and reload shows the container row and no alert).

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1, AC2 | `DashboardLoadFailureShowsFixedErrorAndLogs` | `DashboardRenderTests.cs` |
| AC1, AC2 | `MyImagesLoadFailureShowsFixedErrorAndLogs` | `MyImagesTests.cs` |
| AC1, AC2 | `ObservedImagesLoadFailureShowsFixedErrorAndLogs` | `ObservedImagesTests.cs` |
| AC1, AC2 | `RuntimeContainersLoadFailureShowsFixedErrorAndLogs` | `RuntimeContainersTests.cs` |
| AC1, AC2 | `DockerInstancesLoadFailureShowsFixedErrorAndLogs` | `DockerInstancesRenderTests.cs` |
| AC1, AC2 | `SharedBaseImagesLoadFailureShowsFixedErrorAndLogs` | `SharedBaseImagesRenderTests.cs` |
| AC1, AC2 | `VulnerabilitiesLoadFailureShowsFixedErrorAndLogs` | `VulnerabilitiesTests.cs` |
| AC1, AC2 | `ScanHistoryLoadFailureShowsFixedErrorAndLogs` | `ScanHistoryTests.cs` |
| AC3 | `DockerInstancesDetailLoadFailureShowsNoInstanceData`, `ScanHistoryScheduleLoadFailureShowsNoScheduleAndKeepsRefreshEnabled` | `DockerInstancesRenderTests.cs`, `ScanHistoryTests.cs` |
| AC4 | `RuntimeContainersSuccessfulUpdateCheckAfterLoadFailureShowsList` | `RuntimeContainersTests.cs` |

Each AC1/AC2 test throws `new InvalidOperationException("secret text")` from the page's view-service call, waits with
`DetailPageAssert.AssertLoadErrorAsync(page, "<prefix>: unexpected error", "secret text")` (one alert, no skeleton,
no `.empty-state-panel`) and checks `DetailPageAssert.AssertLoadLoggedOnce(logger, "<logged action>", exception)`,
registering a `TestLogger<TPage>`. The AC3 tests let the first call answer one item (the instance name, a scan row)
and assert it is absent.

## Approach

1. **Shared helper renamed.** `DetailPageLoader` becomes `PageLoader` (`Components/PageLoader.cs`, summary "Guards
   the load of a page …"), body unchanged; `DetailLoadState.RunAsync` calls `PageLoader.LoadAsync`. No other logic.
2. **Each of the eight pages** gets a private field `_loadErrorMessage` (`string?`, documented) and replaces the
   unrestored `await LoadAsync()` in `OnInitializedAsync` by

   ```csharp
   var loadErrorMessage = await PageLoader.LoadAsync(LoadAsync, Logger, "<logged action>", "<prefix>").ConfigureAwait(false);

   await InvokeAsync(() => _loadErrorMessage = loadErrorMessage).ConfigureAwait(false);
   ```

   `SharedBaseImages` and `Vulnerabilities` first move their inline load into a private `LoadAsync()` (call, then
   `InvokeAsync` assignment), like the other pages. `LoadAsync` keeps assigning all fields in one `InvokeAsync`
   after every call has answered, so a failing second call leaves both fields null (AC3). The restored-state path,
   `Persist*` and the user actions are unchanged; a user action's reload keeps its own message (decision 0059).
3. **Markup.** The load alert replaces the skeleton in its branch only — `@if (<list> is null) { @if
   (_loadErrorMessage is not null) { alert } else { skeleton } }` — so a later successful reload, which sets the list,
   shows it and hides the alert without clearing the field (AC4). The alert is
   `<MudAlert Severity="Severity.Error" Dense="true">@_loadErrorMessage</MudAlert>`, as on the detail pages. Layout:
   - My Images, Runtime Containers, Docker Instances, Shared Base Images, Vulnerabilities, Scan History: where the
     `TableSkeleton` stands, after the page header (on Runtime Containers and Scan History after the action alert,
     which can show at the same time);
   - Observed Images: inside the *Manual images* section paper in place of its `TableSkeleton`; the registration
     form above stays usable;
   - Dashboard: the `MudStack` `page-shell` of the null branch holds only the alert in place of
     `PageHeaderSkeleton` and `MetricGridSkeleton` (its header is part of the loaded hero panel).

Early draft PR: no

## Options considered

1. **Guard each page's first load with the shared helper (chosen).** One code path with the detail pages, as 0059
   decided.
2. **Reuse `DetailLoadState` on the list pages** — its not-found state does not exist for a list, and its
   `RunAsync` clears data a list page has not loaded yet; a list needs only the message.
3. **A new `ListLoadState` type with a `RunAsync` like `DetailLoadState`'s** — a second copy of the invoke/clear
   sequence beside `DetailLoadState` (the quality gate's duplication), for two lines per page.
4. **Keep the name `DetailPageLoader`** — a list page calling a "detail page" helper misleads; the rename touches one
   production caller and one test file (fact 5), and the record does not name the type.
5. **An error boundary around the page body** — rejected in 0059 (option 3): layout-wide and cannot name the page's
   load.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Components/DetailPageLoader.cs` → `Components/PageLoader.cs` | renamed type and file |
| DockerUpdateGuard | `Components/DetailLoadState.cs` | calls `PageLoader.LoadAsync` |
| DockerUpdateGuard | `Pages/Dashboard`, `MyImages`, `ObservedImages`, `RuntimeContainers`, `DockerInstances`, `ScanHistory`, `SharedBaseImages`, `Vulnerabilities` (`.razor` and `.razor.cs`) | guarded first load, load alert |

## Signatures (for the Dev's skeleton)

- `internal static class PageLoader` in `Components/PageLoader.cs` with
  `internal static async Task<string?> LoadAsync(Func<Task> load, ILogger logger, string action, string messagePrefix)`
  — the body of today's `DetailPageLoader.LoadAsync`, moved for real in the skeleton.
- Deleted: `DetailPageLoader` (`Components/DetailPageLoader.cs` is removed).
- On `Dashboard`, `MyImages`, `DockerInstances`, `SharedBaseImages` and `Vulnerabilities`, in the `Properties`
  region: `[Inject] public ILogger<Dashboard> Logger { get; set; } = null!;` (the page's own type each), documented
  "Logger" like the other pages — real in the skeleton.

The skeleton holds the rename and the loggers for real; the pages' behavior is unchanged, so every new test fails on
it (the exception escapes the render). No nested type, no parameter beyond four.

## Test files

`DashboardRenderTests.cs`, `MyImagesTests.cs`, `ObservedImagesTests.cs`, `RuntimeContainersTests.cs`,
`DockerInstancesRenderTests.cs`, `SharedBaseImagesRenderTests.cs`, `VulnerabilitiesTests.cs`, `ScanHistoryTests.cs`
under `src/Tests/DockerUpdateGuard.Tests/`.

Existing test code that calls a changed signature: `DetailPageLoaderTests.cs` calls `DetailPageLoader.LoadAsync`
twice — the Dev changes the calls to `PageLoader.LoadAsync` in step 4 (mechanically); the Tester renames the file,
class and methods to `PageLoaderTests` / `PageLoader…` in step 5, no assertion touched, and updates the summaries of
`Helper/DetailPageAssert.cs` from "detail pages" to "pages". No existing page test has an input the change handles
differently: none makes a view-service call throw on the first load.

Files of the change no unit test can cover: none.

## Areas

- `docs/areas/web-ui.md` (+130 words; 3129 now): *General* — the list pages and the dashboard show an error alert in
  place of the skeleton when their first load fails, until a later load succeeds; *Limits and errors* — the
  failed-load bullet covers these pages too, with the eight rows of the table above (a second table or the existing
  one, "Page load" column), and states that a user action's reload keeps the action's prefix.

## Documentation updates

- `docs/areas/web-ui.md`: as under *Areas* (Dev, after the code is green).
- `docs/ARCHITECTURE.md` §11, bullet *Failure messages carry fixed reasons*: "the pages never show one for a failed
  user action" becomes "… for a failed user action or page load" (Dev).
- No `README.md`, configuration or changelog change (the repository has no changelog; the PR's release-notes label is
  the orchestrator's).

## Architecture check

Guarantee "fixed failure reasons, never an error's text" (0009) is extended to the list pages' loads; layering is
untouched (UI only, `IApplicationViewService` stays the pages' only data source). No flow in `ARCHITECTURE.md`
changes beyond the wording above.

## Security considerations

- [x] No limits.
- [x] Every exception of a page's view-service call reaches the user only as `<prefix>: <FailureReason>` through
  `PageLoader.LoadAsync`; the exception's message only reaches the log entry (event 4000), as for the detail pages.

## Decision records

- `docs/decisions/0059-a-detail-page-shows-a-missing-record-or-a-failed-load-inline.md` (Accepted, unreleased) —
  extended in place: source #539, the list pages and the dashboard in *Context* and *Decision*, the double log of a
  failure on both renders under *Consequences*. Index row unchanged.

## Out of scope / follow-ups

- Follow-up issue "[UI] The dashboard refresh and the layout loads leave a failed load unlogged or end the circuit":
  `Dashboard.OnDashboardRefreshRequested` discards the task of `LoadAsync`, so a failed refresh is neither shown nor
  logged; `MainLayout` (`OnInitializedAsync`, `LoadSummaryAsync` on navigation and refresh) and
  `NavMenu.OnInitializedAsync` load unguarded, so a failure there ends every page's prerender on `/error`. Found
  while planning #539.
