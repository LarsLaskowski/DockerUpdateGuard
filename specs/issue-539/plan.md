# Plan: List pages and the layout show a failed load without the error page

Source: Issue #539
Tier: standard — the eight list and overview pages and the layout change how a failed load behaves (inline alert or hidden values instead of the error page or a dead circuit); no security control is created or changed.
Security areas touched: Failure messages and remote text (the pages now carry a load failure reason built by `Infrastructure/FailureReason.cs`); Logging and telemetry of external data (each failed first load is logged as event 4000 with the caught exception through `PageLoader.LoadAsync`)

## Problem / root cause

Every list or overview page awaits its view-service call in `OnInitializedAsync` without a `try`/`catch`
(paths under `src/DockerUpdateGuard/Components/Pages/`): `Dashboard.razor.cs:184`, `MyImages.razor.cs:186`,
`ObservedImages.razor.cs:262`, `RuntimeContainers.razor.cs:432`, `DockerInstances.razor.cs:161`,
`ScanHistory.razor.cs:249`, `SharedBaseImages.razor.cs:106` and `Vulnerabilities.razor.cs:219` (inline loads). The
layout that renders around every page (`@Body`, `MainLayout.razor:84`) does the same: `MainLayout.OnInitializedAsync`
(`MainLayout.razor.cs:285-300`, `GetDashboardSummaryAsync` at :144) and `NavMenu.OnInitializedAsync`
(`NavMenu.razor.cs:133-138`, `HasBaseImagesAsync`). An exception escapes the lifecycle method: on the prerender
`UseExceptionHandler("/error")` (`Program.cs:32`) renders `/error`, on the interactive render the circuit ends. With a
database outage every one of these calls fails, so guarding the pages alone would still end on `/error`. The reloads
on a refresh signal or navigation (`Dashboard.OnDashboardRefreshRequested`, `MainLayout.OnLocationChanged` and
`OnDashboardRefreshRequested`) discard the task of an unguarded load, so their failure is neither shown nor logged.
The detail pages were fixed in #40 with `DetailPageLoader` and `DetailLoadState`
([decision 0059](../../docs/decisions/0059-a-detail-page-shows-a-missing-record-or-a-failed-load-inline.md)).

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | The issue's pages (`ObservedImages`, `MyImages`, `RuntimeContainers`, `DockerInstances`) load unguarded in `OnInitializedAsync` | the lines above; the "…" also covers `Dashboard`, `ScanHistory`, `SharedBaseImages`, `Vulnerabilities`, same pattern | confirmed |
| 2 | A prerender exception renders `/error` outside Development | `Program.cs:32` | confirmed |
| 3 | A failed first load persists nothing, so the interactive render loads again | each `Persist*` method writes only a non-null field (e.g. `ObservedImages.razor.cs:168`, `MainLayout.PersistSummary`) | confirmed |
| 4 | `DetailPageLoader.LoadAsync` catches every exception, logs `UserActionFailed` (event 4000) and returns `<prefix>: <FailureReason>`; an `InvalidOperationException` gives `unexpected error` | `Components/DetailPageLoader.cs`, `FailureReason.cs:36`, `DetailPageLoaderTests` | confirmed |
| 5 | `DetailPageLoader` has one production caller (`DetailLoadState.cs:56`) and one test file (`DetailPageLoaderTests.cs`); no document names it | `grep -rn DetailPageLoader src docs` | confirmed |
| 6 | `Dashboard`, `MyImages`, `DockerInstances`, `SharedBaseImages`, `Vulnerabilities`, `MainLayout`, `NavMenu` inject no logger; bUnit resolves an injected `ILogger<T>` without registration | `grep ILogger< Components/**/*.razor.cs`; `RuntimeContainersPersistentStateTests` renders `RuntimeContainers` (which injects one) without registering a logger | confirmed |
| 7 | Reloads after user actions are already guarded: rescan reloads run inside `VulnerabilityRescanControl.RescanAsync`'s `try`; `RuntimeContainers.TriggerScanAsync`, `ScanHistory.TriggerVulnerabilityRefreshAsync` and `ObservedImages.ReloadAfterFailedRegistrationAsync` catch theirs | `VulnerabilityRescanControl.razor.cs:89`, the named methods | confirmed |
| 8 | `MainLayout` and `NavMenu` are the only components besides the pages that call `IApplicationViewService` | `grep -rln IApplicationViewService Components` | confirmed |
| 9 | The top bar shows its status skeleton while `_dashboardSummary` is null (`MainLayout.razor:39`); the menu shows *Shared Base Images* only while `_showBaseImages` is true (`NavMenu.razor:13`) | the named lines | confirmed |
| 10 | The *Refresh vulnerabilities* button exists only when vulnerability scanning is enabled | `ScanHistory.razor:12` | confirmed |
| 11 | Decision 0059 is unreleased | `git tag --contains 6642a68` prints nothing; newest tag `v1.3.9` | confirmed |

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
  Instances shows no single-instance section and no instance row when `GetDockerInstanceDetailAsync` throws; Scan
  History, with vulnerability scanning enabled, shows no scan row and no `Next scheduled vulnerability refresh`
  caption when `GetVulnerabilityScheduleAsync` throws, and its *Refresh vulnerabilities* button is present and enabled.
- AC4: After a failed first load, a user action whose reload succeeds shows the list in place of the load alert
  (Runtime Containers: *Check image updates* with a succeeding scan and reload shows the container row and no alert).
- AC5: When the layout's first summary load throws, the layout renders with its body; the top bar shows neither the
  status skeleton nor *Protected assets* / *Last activity* nor the exception's text; the failure is logged once as
  `LoadDashboardSummary`. Decided by: `_summaryLoadFailed` and the markup branch "summary is null and
  `_summaryLoadFailed` is false" for the skeleton.
- AC6: When the menu's `HasBaseImagesAsync` throws, the menu renders its other entries, hides *Shared Base Images*,
  shows no exception text, and logs the failure once as `LoadNavigation`.
- AC7: A reload that fails after a successful load keeps what is shown and is logged once: a dashboard refresh signal
  (`LoadDashboard`) keeps the metrics and shows no alert; a layout reload on navigation (`LoadDashboardSummary`) keeps
  the top bar values. No such failure escapes the discarded task unlogged.

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
| AC5 | `MainLayoutSummaryLoadFailureRendersBodyWithoutSummaryAndLogs` | `MainLayoutRenderTests.cs` |
| AC6 | `NavMenuBaseImagesCheckFailureHidesEntryAndLogs` | `NavMenuTests.cs` |
| AC7 | `DashboardFailedRefreshKeepsDashboardAndLogs`, `MainLayoutFailedReloadOnNavigationKeepsSummaryAndLogs` | `DashboardRenderTests.cs`, `MainLayoutRenderTests.cs` |

Each AC1/AC2 test throws `new InvalidOperationException("secret text")` from the page's view-service call, waits with
`DetailPageAssert.AssertLoadErrorAsync(page, "<prefix>: unexpected error", "secret text")` (one alert, no skeleton,
no `.empty-state-panel`) and checks `DetailPageAssert.AssertLoadLoggedOnce(logger, "<logged action>", exception)`,
registering a `TestLogger<TPage>`. The AC3 tests let the first call answer one item (the instance name, a scan row)
and assert it is absent; the Scan History test sets `Vulnerabilities.Enabled = true` and asserts the refresh button is
present without a `disabled` attribute. The AC5 test renders `MainLayout` with a body fragment of known text and
asserts that text, the absence of `app-topbar__status-skeleton`, `Protected assets` and `secret text`. The AC7 tests
answer the first call, then throw on the second (a `DashboardRefreshState.NotifyChanged()`, a
`NavigationManager.NavigateTo`) and assert the first values stay, no `.mud-alert` (dashboard), one log entry.

## Approach

1. **Shared helper renamed.** `DetailPageLoader` becomes `PageLoader` (`Components/PageLoader.cs`, summary "Guards
   the load of a page or the layout …"), body unchanged; `DetailLoadState.RunAsync` calls `PageLoader.LoadAsync`.
2. **Each of the eight pages** gets a private field `_loadErrorMessage` (`string?`) and a private
   `Task LoadOrReportAsync()`:

   ```csharp
   var loadErrorMessage = await PageLoader.LoadAsync(LoadAsync, Logger, "<logged action>", "<prefix>").ConfigureAwait(false);

   await InvokeAsync(() => _loadErrorMessage = loadErrorMessage).ConfigureAwait(false);
   ```

   It replaces the unrestored `await LoadAsync()` in `OnInitializedAsync`, and on the dashboard also the
   `InvokeAsync(LoadAsync)` of `OnDashboardRefreshRequested` (AC7). `SharedBaseImages` and `Vulnerabilities` first move
   their inline load into a private `LoadAsync()`. `LoadAsync` keeps assigning all fields in one `InvokeAsync` after
   every call has answered, so a failing second call leaves both fields null (AC3). Restored state, `Persist*` and the
   user actions are unchanged; a user action's reload keeps its own message (decision 0059).
3. **Page markup.** The load alert replaces the skeleton in its branch only — `@if (<list> is null) { @if
   (_loadErrorMessage is not null) { alert } else { skeleton } }` — so a later successful load shows the list without
   clearing the field (AC4), and a failed reload after a successful one changes nothing visible (AC7). The alert is
   `<MudAlert Severity="Severity.Error" Dense="true">@_loadErrorMessage</MudAlert>`, as on the detail pages. Layout:
   - My Images, Runtime Containers, Docker Instances, Shared Base Images, Vulnerabilities, Scan History: where the
     `TableSkeleton` stands, after the page header (on Runtime Containers and Scan History after the action alert,
     which can show at the same time);
   - Observed Images: inside the *Manual images* section paper in place of its `TableSkeleton`; the registration
     form above stays usable;
   - Dashboard: the `MudStack` `page-shell` of the null branch holds only the alert in place of
     `PageHeaderSkeleton` and `MetricGridSkeleton` (its header is part of the loaded hero panel).
4. **Layout.** `MainLayout` gets a private `bool _summaryLoadFailed` and a private `Task LoadSummaryOrReportAsync()`
   that runs `PageLoader.LoadAsync(LoadSummaryAsync, Logger, "LoadDashboardSummary", "Loading the summary failed")`
   and sets `_summaryLoadFailed = message is not null` with `StateHasChanged()` in one `InvokeAsync`; it replaces the
   call in `OnInitializedAsync`, `OnLocationChanged` and `OnDashboardRefreshRequested`. Markup: the status skeleton
   shows only while the summary is null and `_summaryLoadFailed` is false; with a summary the values show as today,
   so a failed reload keeps them. `NavMenu` gets a private `Task LoadBaseImagesEntryAsync()` (call, then
   `InvokeAsync` assignment of `_showBaseImages`), run from `OnInitializedAsync` through
   `PageLoader.LoadAsync(LoadBaseImagesEntryAsync, Logger, "LoadNavigation", "Loading the navigation failed")`; the
   returned message is not shown and `_showBaseImages` stays false. The layout shows no text for these failures: the
   page's own load reports an outage, and the prefixes appear only through the helper's contract.

Early draft PR: no

## Options considered

1. **Guard each page's and the layout's load with the shared helper (chosen).** One code path with the detail pages,
   as 0059 decided.
2. **Reuse `DetailLoadState` on the list pages** — its not-found state does not exist for a list, and its
   `RunAsync` clears data a list page has not loaded yet; a list needs only the message.
3. **A new `ListLoadState` type with a `RunAsync` like `DetailLoadState`'s** — a second copy of the invoke/clear
   sequence beside `DetailLoadState` (the quality gate's duplication), for two lines per page.
4. **Keep the name `DetailPageLoader`** — a list page or the layout calling a "detail page" helper misleads; the rename
   touches one production caller and one test file (fact 5), and the record does not name the type.
5. **An error boundary around the page body** — rejected in 0059 (option 3): layout-wide and cannot name the page's
   load.
6. **An alert in the top bar for a failed summary** — repeats on every page what the page's own alert already says,
   for two secondary counts; hiding the values is enough.
7. **Leave the layout out of scope (follow-up issue)** — a database outage fails the layout too, so the pages' alert
   would never reach the user (Devil's Advocate, major).

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Components/DetailPageLoader.cs` → `Components/PageLoader.cs` | renamed type and file |
| DockerUpdateGuard | `Components/DetailLoadState.cs` | calls `PageLoader.LoadAsync` |
| DockerUpdateGuard | `Pages/Dashboard`, `MyImages`, `ObservedImages`, `RuntimeContainers`, `DockerInstances`, `ScanHistory`, `SharedBaseImages`, `Vulnerabilities` (`.razor` and `.razor.cs`) | guarded load, load alert |
| DockerUpdateGuard | `Layout/MainLayout`, `Layout/NavMenu` (`.razor` and `.razor.cs`) | guarded loads, hidden values |

## Signatures (for the Dev's skeleton)

- `internal static class PageLoader` in `Components/PageLoader.cs` with
  `internal static async Task<string?> LoadAsync(Func<Task> load, ILogger logger, string action, string messagePrefix)`
  — the body of today's `DetailPageLoader.LoadAsync`, moved for real in the skeleton.
- Deleted: `DetailPageLoader` (`Components/DetailPageLoader.cs` is removed).
- On `Dashboard`, `MyImages`, `DockerInstances`, `SharedBaseImages`, `Vulnerabilities`, `MainLayout` and `NavMenu`,
  in the `Properties` region: `[Inject] public ILogger<Dashboard> Logger { get; set; } = null!;` (the component's own
  type each), documented "Logger" like the other pages — real in the skeleton.

The skeleton holds the rename and the loggers for real; behavior is unchanged, so every new test fails on it (the
exception escapes the render, or the discarded reload logs nothing). No nested type, no parameter beyond four.

## Test files

`DashboardRenderTests.cs`, `MyImagesTests.cs`, `ObservedImagesTests.cs`, `RuntimeContainersTests.cs`,
`DockerInstancesRenderTests.cs`, `SharedBaseImagesRenderTests.cs`, `VulnerabilitiesTests.cs`, `ScanHistoryTests.cs`,
`MainLayoutRenderTests.cs`, `NavMenuTests.cs` under `src/Tests/DockerUpdateGuard.Tests/`.

Existing test code that calls a changed signature: `DetailPageLoaderTests.cs` calls `DetailPageLoader.LoadAsync`
twice — the Dev changes the calls to `PageLoader.LoadAsync` in step 4 (mechanically); the Tester renames the file,
class and methods to `PageLoaderTests` / `PageLoader…` in step 5, no assertion touched, and updates the summaries of
`Helper/DetailPageAssert.cs` from "detail pages" to "pages". No existing test has an input the change handles
differently: none makes a view-service call throw; `MainLayoutWithoutDashboardSummaryRendersLoadingSkeleton` answers
null, which still shows the skeleton.

Files of the change no unit test can cover: none.

## Areas

- `docs/areas/web-ui.md` (+200 words; 3129 now): *General* — the list pages and the dashboard show an error alert in
  place of the skeleton when their first load fails, until a later load succeeds; a failed reload keeps what is shown;
  the top bar shows no values when its first load fails and keeps its last values when a reload fails; the
  *Shared Base Images* entry is hidden when its check fails; *Limits and errors* — the failed-load bullet covers these
  pages, with the eight rows of the table above, and the layout's two logged actions `LoadDashboardSummary` and
  `LoadNavigation`, which show no text.

## Documentation updates

- `docs/areas/web-ui.md`: as under *Areas* (Dev, after the code is green).
- `docs/ARCHITECTURE.md` §11, bullet *Failure messages carry fixed reasons*: "the pages never show one for a failed
  user action" becomes "… for a failed user action or page load" (Dev).
- No `README.md`, configuration or changelog change (the repository has no changelog; the PR's release-notes label is
  the orchestrator's).

## Architecture check

Guarantee "fixed failure reasons, never an error's text" (0009) is extended to the list pages' and the layout's loads;
layering is untouched (UI only, `IApplicationViewService` stays the only data source). No flow in `ARCHITECTURE.md`
changes beyond the wording above.

## Security considerations

- [x] No limits.
- [x] Every exception of a page's view-service call reaches the user only as `<prefix>: <FailureReason>` through
  `PageLoader.LoadAsync`; the layout shows none. The exception reaches only the log entry (event 4000), as for the
  detail pages.

## Decision records

- `docs/decisions/0059-a-detail-page-shows-a-missing-record-or-a-failed-load-inline.md` (Accepted, unreleased) —
  extended in place: source #539, the list pages, the dashboard and the layout in *Context* and *Decision*, the
  double log of a failure on both renders and the hidden layout values under *Consequences*. Index row unchanged.

## Challenge

- **Major — the layout loads unguarded around every page, so a database outage still ends on `/error`.** Accepted:
  `MainLayout` and `NavMenu` are in scope (Approach 4, AC5, AC6), with the refresh and navigation reloads (AC7); the
  follow-up issue of the first plan is dropped, since nothing of it is left.
- **Minor — the Scan History schedule test must enable vulnerability scanning and assert the button and the missing
  scan row.** Accepted: AC3 and the test description say so.

## Security review

- **Non-blocking — *Security areas touched* must also name *Logging and telemetry of external data*.** Fixed: the
  header line now reads as Security gave it.

## Out of scope / follow-ups

- None: the dashboard refresh and layout loads of the first plan's follow-up are part of this change.
