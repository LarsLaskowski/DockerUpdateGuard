# Plan: Detail pages tell loading, not found and a failed load apart

Source: Issue #40
Tier: standard — the four detail pages change what they render for a missing record and for a failed load (a behavior change), and no security control is created, changed or removed.
Security areas touched: Failure messages and remote text (a detail page newly shows a fixed failure reason for a failed load); Logging and telemetry of external data (new log calls carrying the load exception); Container actions (`Components/Pages/RuntimeContainerDetail.razor` is in its file list — only the page's load path changes, the action paths stay as they are)

## Problem / root cause

Each detail page decides its markup on one condition, `@if (_detail is null)` → `<TableSkeleton …/>`
(`DockerInstanceDetail.razor:14`, `MyImageDetail.razor:16`, `ObservedImageDetail.razor:14`,
`RuntimeContainerDetail.razor:21`). Its `LoadAsync` assigns the view service's answer to `_detail`
(`DockerInstanceDetail.razor.cs:104-110`, `MyImageDetail.razor.cs:93-99`, `ObservedImageDetail.razor.cs:103-109`,
`RuntimeContainerDetail.razor.cs:240-246`), and the view service answers `null` for an unknown identifier, so
"still loading" and "does not exist" are the same state and the skeleton never goes away. `OnParametersSetAsync`
(`…:143-154`, `:132-143`, `:142-153`, `:488-499`) calls `LoadAsync` without a `try`, so an exception of the view
service is unhandled: during the server prerender the request ends on the `/error` page (`Program.cs:32`), on the
interactive render the circuit fails. There is no `ErrorBoundary` in the application.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | The view service returns `null` for an unknown identifier on all three methods the pages call | `ApplicationViewService.cs:1516-1523` (observed image, used by both image pages), `:1660-1668` (no latest runtime snapshot), `:1963-1975` (Docker instance) | confirmed |
| 2 | The pages render "Loading…" | They render `TableSkeleton` (MudBlazor `MudSkeleton` placeholders), no text; the substance (a permanent loading state) holds, so only the wording is refuted | refuted |
| 3 | A service exception goes to "the Blazor error boundary" | No `ErrorBoundary` component or `blazor-error-ui` exists under `src/DockerUpdateGuard`; prerender exceptions reach `UseExceptionHandler("/error")` (`Program.cs:32`), interactive ones end the circuit; the effect (no inline report) holds | refuted |
| 4 | The write paths report errors inline | `RuntimeContainerDetail.razor.cs:268-322, 432-469` catch, log `UserActionFailed` and set `_errorMessage` with `FailureReason.FromException`; `ObservedImages.razor.cs:183-215` likewise | confirmed |
| 5 | `RuntimeContainerDetail`'s user actions reload through the same `LoadAsync`, and a reload failure is reported under the action's prefix | `RuntimeContainerDetail.razor.cs:280, 310, 448`; [Web UI](../../docs/areas/web-ui.md#limits-and-errors) ("the reload after a save") | confirmed |
| 6 | `ObservedImages.razor` distinguishes loading, empty and data | `ObservedImages.razor:82-139` (`_images is null` → skeleton, `Count == 0` → `empty-state-panel`, else table) | confirmed |
| 7 | The Router has a not-found page, so `NavigationManager.NotFound()` is available | `Components/Routes.razor` (`NotFoundPage="typeof(Pages.NotFound)"`) | confirmed |
| 8 | bUnit resolves an `ILogger<T>` without registration, and tests can register `TestLogger<T>` | `RuntimeContainerDetail` injects `ILogger<RuntimeContainerDetail>` and its tests without a logger pass; `RuntimeContainerDetailTests.cs:772` registers `TestLogger<RuntimeContainerDetail>` | confirmed |
| 9 | Prerendered state is persisted only for a non-null detail | `PersistDetail` in each page (`if (_detail is not null)`) | confirmed |
| 10 | The skeleton is detectable in markup by the class `mud-skeleton` | `TableSkeleton.razor` renders `MudSkeleton`; the class name `mud-skeleton` is a string of `MudBlazor.dll` 9.14.0 (`~/.nuget/packages/mudblazor/9.14.0/lib/net11.0`), the class builder's base; the AC3 tests assert it on rendered markup, so a wrong class fails them on the fixed code, not silently | confirmed |
| 11 | No existing test asserts the skeleton or a `null` detail | `grep -i skeleton` over the detail page tests: no hit | confirmed |
| 12 | `web-ui.md` lists this defect as a known gap | `docs/areas/web-ui.md`, *Known gaps*, bullet "A detail route with a well-formed identifier …" | confirmed |

## Acceptance criteria

The texts per page (used by AC1, AC2, AC5):

| Page | Not-found message | Back link (text → href) | Load error prefix | Logged action |
| ---- | ----------------- | ----------------------- | ----------------- | ------------- |
| DockerInstanceDetail | `This Docker instance does not exist.` | `Back to Docker Instances` → `/docker-instances` | `Loading the Docker instance failed` | `LoadDockerInstanceDetail` |
| MyImageDetail | `This image does not exist.` | `Back to My Images` → `/my-images` | `Loading the image failed` | `LoadMyImageDetail` |
| ObservedImageDetail | `This observed image does not exist.` | `Back to Observed Images` → `/observed-images` | `Loading the observed image failed` | `LoadObservedImageDetail` |
| RuntimeContainerDetail | `This container is not known on this Docker instance.` | `Back to Runtime Containers` → `/runtime-containers` | `Loading the container failed` | `LoadRuntimeContainerDetail` |

- AC1: When the view service answers `null` for the route's identifier, each page shows its not-found panel (message
  and back link of the table) and no skeleton (`mud-skeleton`), no error alert.
- AC2: When the view service throws during the page load (`OnParametersSetAsync`), each page shows one error alert
  `<prefix>: <reason>`, the reason being `FailureReason.FromException(exception, null, CancellationToken.None)` (an
  `InvalidOperationException("secret text")` gives `Loading the observed image failed: unexpected error`), never the
  exception's own message; no skeleton, no not-found panel, no detail data; the failure is logged once through
  `UserActionFailed` (event 4000) with the action of the table and the exception.
- AC3: While the view service's answer is pending, each page shows the skeleton and neither the not-found panel nor
  an error alert; a detail that arrives is rendered as today, without alert or panel.
- AC4: When a page instance gets a new route identifier after it showed a detail, and the new load answers `null` or
  throws, the previous detail is no longer shown (the not-found panel or the error alert instead); when it then gets
  an identifier that loads, the alert and the panel are gone.
- AC5: `RuntimeContainerDetail`'s user actions keep their error behavior: when the reload after *Select* throws,
  the page shows `Saving the tag selection failed: <reason>` as today and no load error alert; when the reload after
  an action answers `null`, the not-found panel replaces the skeleton.
- AC6: `DetailPageLoader.LoadAsync` returns `null` and logs nothing when the load completes; when it throws, it logs
  `UserActionFailed(action, exception)` once and returns `<messagePrefix>: <FailureReason.FromException(exception,
  null, CancellationToken.None)>` (`HttpRequestException` → `… : connection failed`).

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `DockerInstanceDetailUnknownIdShowsNotFoundPanel`, `MyImageDetailUnknownIdShowsNotFoundPanel`, `ObservedImageDetailUnknownIdShowsNotFoundPanel`, `RuntimeContainerDetailUnknownContainerShowsNotFoundPanel` | `DockerInstanceDetailTests.cs`, `MyImageDetailTests.cs`, `ObservedImageDetailTests.cs`, `RuntimeContainerDetailTests.cs` |
| AC2 | `DockerInstanceDetailLoadFailureShowsFixedErrorAndLogs`, `MyImageDetailLoadFailureShowsFixedErrorAndLogs`, `ObservedImageDetailLoadFailureShowsFixedErrorAndLogs`, `RuntimeContainerDetailLoadFailureShowsFixedErrorAndLogs` | same four files |
| AC3 | `DockerInstanceDetailPendingLoadShowsSkeletonOnly`, `MyImageDetailPendingLoadShowsSkeletonOnly`, `ObservedImageDetailPendingLoadShowsSkeletonOnly`, `RuntimeContainerDetailPendingLoadShowsSkeletonOnly` (a `TaskCompletionSource` holds the answer, then completes it with a detail) | same four files |
| AC4 | `ObservedImageDetailRouteChangeToUnknownIdHidesPreviousDetail`, `RuntimeContainerDetailRouteChangeAfterLoadFailureClearsAlert` | `ObservedImageDetailTests.cs`, `RuntimeContainerDetailTests.cs` |
| AC5 | `RuntimeContainerDetailSelectReloadFailureKeepsActionPrefix`, `RuntimeContainerDetailSelectReloadWithoutDetailShowsNotFoundPanel` | `RuntimeContainerDetailTests.cs` |
| AC6 | `DetailPageLoaderCompletedLoadReturnsNullWithoutLog`, `DetailPageLoaderFailedLoadReturnsPrefixedFixedReasonAndLogs` | `DetailPageLoaderTests.cs` |

## Approach

1. **Shared load guard.** New `internal static class DetailPageLoader` (`src/DockerUpdateGuard/Components/`, beside
   `ComponentLoggingExtensions`): `LoadAsync` awaits the page's load delegate; on an exception it logs
   `logger.UserActionFailed(action, exception)` and returns the prefixed fixed reason, otherwise `null`. One helper
   instead of four copies of the `try/catch`: the CI quality gate's duplication check on new code would count four
   near-identical `OnParametersSetAsync` bodies. No exception type is let through (also not
   `ObjectDisposedException`: the page's `InvokeAsync` afterwards behaves as today when the page is gone).
2. **Page state.** Each page gets two fields beside `_detail`: `bool _isNotFound` and `string? _loadErrorMessage`.
   `LoadAsync` sets `_detail = detail` and `_isNotFound = detail is null` in its one `InvokeAsync`, so every load —
   also `RuntimeContainerDetail`'s reloads after a user action — yields not-found for `null`. `LoadAsync` itself
   keeps throwing: the user actions of `RuntimeContainerDetail` keep their own `catch` and prefix (AC5).
   `OnParametersSetAsync`, after the unchanged restored-state branch, calls `DetailPageLoader.LoadAsync(LoadAsync,
   Logger, <action>, <prefix>)` and, in one `InvokeAsync`, sets `_loadErrorMessage` to its result; on a non-null
   result it also sets `_detail = null` and `_isNotFound = false` (no stale detail of another identifier, AC4).
   `MyImageDetail`, `ObservedImageDetail` and `DockerInstanceDetail` get an injected `Logger` like
   `RuntimeContainerDetail`'s. The restored-from-state path and `PersistDetail` stay: a not-found or failed prerender
   persists nothing, so the interactive render loads once more and reaches the same state.
3. **Markup**, replacing the `@if (_detail is null) … else …` block of each page; the page header stays above it, and
   `RuntimeContainerDetail`'s existing action alert (`_errorMessage`) stays where it is:
   1. `_loadErrorMessage` set → `<MudAlert Severity="Severity.Error" Dense="true">@_loadErrorMessage</MudAlert>` and
      nothing else;
   2. else `_detail is not null` → the detail markup, unchanged;
   3. else `_isNotFound` → `<MudPaper Class="empty-state-panel" Elevation="0">` holding `<MudText>` with the
      not-found message and `<MudLink Href="…">` with the back link (the table above), in this order;
   4. else → the existing `TableSkeleton`, unchanged.
   The message and the alert are plain text, rendered HTML-encoded by Razor. No new CSS.

Early draft PR: no

## Options considered

1. **Inline three states with a shared load guard (chosen)** — the page keeps its header and names what is missing,
   with a way back; the recommendation of the issue and the pattern of `ObservedImages.razor`.
2. **`NavigationManager.NotFound()` for a `null` answer** — renders the application's generic `/not-found` page and
   answers HTTP 404 on the prerender (fact 7). Lost: the generic page says only "The requested page could not be
   found" for a route that is valid, the user loses the context of which record is gone, and how it combines with
   the prerender's persisted state and bUnit's navigation manager is unmeasured; the issue asks for the inline view.
3. **Let a failed load reach an `ErrorBoundary` around the page body** — a new layout-wide component that would
   also swallow errors of other pages differently from today, and it cannot give the fixed, page-named message the
   write paths give. Lost.
4. **The `try/catch` inside each page's `LoadAsync`** — fewer call sites, but the reload after a
   `RuntimeContainerDetail` user action would then report under the load prefix instead of the action's, against
   [Web UI](../../docs/areas/web-ui.md#limits-and-errors) (fact 5). Lost.
5. **The `try/catch` copied into each `OnParametersSetAsync`** — four near-identical blocks the CI duplication
   check counts on new code. Lost to the shared helper.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Components/DetailPageLoader.cs` | new helper |
| DockerUpdateGuard | `Components/Pages/DockerInstanceDetail.razor` + `.razor.cs` | state fields, `Logger`, guarded load, markup branches |
| DockerUpdateGuard | `Components/Pages/MyImageDetail.razor` + `.razor.cs` | same |
| DockerUpdateGuard | `Components/Pages/ObservedImageDetail.razor` + `.razor.cs` | same |
| DockerUpdateGuard | `Components/Pages/RuntimeContainerDetail.razor` + `.razor.cs` | state fields, guarded load, markup branches (has `Logger`) |

## Signatures (for the Dev's skeleton)

In `src/DockerUpdateGuard/Components/DetailPageLoader.cs`, namespace `DockerUpdateGuard.Components`:

- `internal static class DetailPageLoader`
- `internal static async Task<string?> LoadAsync(Func<Task> load, ILogger logger, string action, string messagePrefix)`

Injected properties (region `Properties`, as `RuntimeContainerDetail.Logger`):

- `[Inject] public ILogger<DockerInstanceDetail> Logger { get; set; } = null!;`
- `[Inject] public ILogger<MyImageDetail> Logger { get; set; } = null!;`
- `[Inject] public ILogger<ObservedImageDetail> Logger { get; set; } = null!;`

The new fields and the markup branches are private (*Approach*). Four parameters, no nested type, no keyword-named
parameter (*Writing code*). The skeleton holds no real piece: the page tests of AC1–AC5 fail on the unchanged pages,
the AC6 tests on the skeleton body.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/DetailPageLoaderTests.cs` (new)
- `src/Tests/DockerUpdateGuard.Tests/DockerInstanceDetailTests.cs` (new)
- `src/Tests/DockerUpdateGuard.Tests/MyImageDetailTests.cs`, `ObservedImageDetailTests.cs`,
  `RuntimeContainerDetailTests.cs` (extended)

Logging is asserted with the existing `TestLogger<T>`; the view service is an NSubstitute substitute as in the
existing page tests. The existing tests stay unchanged: none renders a `null` detail or asserts the skeleton (fact
11); a test whose substitute answers `null` (no configured answer) now gets the not-found panel instead of the
skeleton, and none asserts on either.

Existing test code that calls a changed signature: none.

Files of the change no unit test can cover: none.

## Areas

- `docs/areas/web-ui.md (+120 words)`: *General* — the loading sentence ("While a page loads its data …") gains that a
  detail page whose record does not exist shows a not-found note with a link back to its list instead, and keeps its
  route; *Limits and errors* — a failed load of a detail page shows an error alert `<prefix>: <reason>` with the
  fixed reasons and is logged like a user action, with the four prefixes and logged actions of the table added (as
  a short table or as rows of the existing one, marked as page loads); *Known gaps* — the #40 bullet is removed; *Related decisions* — a line for
  [0059](../../docs/decisions/0059-a-detail-page-shows-a-missing-record-or-a-failed-load-inline.md).

## Documentation updates

- `docs/areas/web-ui.md` as under *Areas* (Dev).
- `docs/ARCHITECTURE.md`, section 9: none — no new page, component or service; the helper is internal page plumbing.
- No `README.md`, `.squad/project.md` or `docs/UNIT_TESTS.md` change; the PR's release-notes label is the
  orchestrator's.

## Architecture check

Pages still depend only on `IApplicationViewService` (section 9); the guarantee "integration calls answer expected
failures with a fixed reason, never an error's own text" ([decision 0009](../../docs/decisions/0009-integration-results-carry-fixed-failure-reasons.md))
is kept on the pages: the alert carries only `FailureReason.FromException`'s reason. No other guarantee of
`.squad/project.md` is touched.

## Security considerations

- [x] No limits: the change parses no input.
- [x] Exceptions reaching a user-visible message: any exception of `GetDockerInstanceDetailAsync`,
  `GetObservedImageDetailAsync` or `GetRuntimeContainerDetailAsync` during a page load (database errors,
  `ArgumentException` for a blank container id, `ObjectDisposedException`); the user sees only `<prefix>: <fixed
  reason>` (`unexpected error`, `connection failed`, `timed out`, `invalid response`, or a `ReportableException`'s
  application-composed message). The exception itself goes only to the log, through the existing `UserActionFailed`
  message, which carries a fixed action name.

## Decision records

- `docs/decisions/0059-a-detail-page-shows-a-missing-record-or-a-failed-load-inline.md` (Proposed)

## Out of scope / follow-ups

- The list pages (`ObservedImages`, `MyImages`, `RuntimeContainers`, `DockerInstances`, …) load in
  `OnInitializedAsync` without a `try` either, so a failed list load also ends on `/error` or ends the circuit.
  Proposed issue — title: "List pages report a failed initial load only through the error page"; body: "The list
  pages load their data in `OnInitializedAsync` without a try/catch (e.g. `ObservedImages.razor.cs`, `OnInitializedAsync`).
  A view-service exception ends the prerender on `/error` or ends the interactive circuit instead of an inline error
  alert, unlike the detail pages after #40. Show a fixed-reason alert as the detail pages do."
- The HTTP status of a not-found detail page stays 200 (option 2).
- `RuntimeContainerDetail.OnAfterRenderAsync` still calls `ContainerActionService.GetAvailabilityAsync` for a container that is not there; the container-actions path stays unchanged (challenge note 2).

## Challenge notes

Devil's Advocate, mode plan, issue 40 (/home/user/DockerUpdateGuard/specs/issue-40/plan.md, /home/user/DockerUpdateGuard/docs/decisions/0059-a-detail-page-shows-a-missing-record-or-a-failed-load-inline.md)

The plan holds up. I found no major objections and two minor ones.

**What I checked, and it matches the plan**
- **The three null answers.**
  - The observed image (used by both image pages) and the Docker instance return null in `src/DockerUpdateGuard/UI/ApplicationViewService.cs:1516-1523` and `:1963-1975`. The runtime container returns null at `:1660-1668`.
  - The container path also has `ArgumentException.ThrowIfNullOrWhiteSpace(containerId)`. The route `{ContainerId}` makes that unreachable in practice, but the plan's catch-all covers it anyway.
- **The cause.** Each page's `LoadAsync` assigns `_detail` directly, and `OnParametersSetAsync` has no try/catch: DockerInstanceDetail.razor.cs:104-110 and 144-154, MyImageDetail 93-99 and 133-143, ObservedImageDetail 103-109 and 143-153, RuntimeContainerDetail 240-246 and 489-499.
- **Fact 5 (Option 4 rightly rejected).** `RuntimeContainerDetail`'s actions reload through `LoadAsync` inside their own catch with their own prefix (lines 280, 310, 448). Putting the catch inside `LoadAsync` would have changed which prefix an action's reload failure shows.
- **Scope.** Only these four pages have a parameterized route. `DockerInstances.razor:124` `_singleInstanceDetail` is a list-page variant, not a sibling defect. The list pages are correctly a follow-up.
- **No stale doc claims.** `docs/areas/web-ui.md:33` (the loading sentence) and `:203-205` (the #40 known gap) are the lines the plan updates.
- **The bUnit test context is interactive.** Existing tests call `SetRendererInfo` only to force "Static" (`RuntimeContainerDetailTests.cs:480`), so `OnAfterRenderAsync` runs in the new tests.
- **No need for `RESULT: NO CHANGE` and no simpler option.** The defect reproduces from the code. The shared helper is the least code that handles all four pages.

**Objections**

1. **minor: a previous state stays visible while a new identifier loads.** Step 2 sets `_isNotFound` and `_loadErrorMessage` only after a load finishes (fields `_isNotFound` / `_loadErrorMessage`, *Approach* step 2). So after a route change, while the new load is pending, the page still shows the previous identifier's not-found panel or error alert (and, as today, its previous detail) instead of the skeleton that AC3 describes. AC3's tests cover only the first load, so they cannot catch this.
   - Instruction for the Dev: at the start of `OnParametersSetAsync` (after the restored-state branch), clear `_isNotFound` and `_loadErrorMessage` in one `InvokeAsync` before calling `DetailPageLoader.LoadAsync`. Whether to also clear `_detail` there is the Dev's call, but say which in web-ui.md.
   - Instruction for the Tester: extend the AC4 test `RuntimeContainerDetailRouteChangeAfterLoadFailureClearsAlert` (or the ObservedImageDetail one) so the second load is held by a `TaskCompletionSource`. While it is pending, assert that the previous alert or panel is gone.

2. **minor: the runtime container page still contacts Portainer for a container that is not there.** `RuntimeContainerDetail.OnAfterRenderAsync` (`RuntimeContainerDetail.razor.cs:502-514`) calls `ContainerActionService.GetAvailabilityAsync` on every interactive render with a new key. It does so whether the page shows a detail, the new not-found panel or the load error alert. On a not-found container this is a Portainer call whose failure is logged as `LoadContainerActions` (line 347), next to the new `LoadRuntimeContainerDetail` entry.
   - Instruction for the Dev: leave the call as it is (no change to the container-actions path, as the plan's *Security areas* line promises) and record it in plan *Out of scope*.
   - Instruction for the Tester: the AC2 test for RuntimeContainerDetail must assert exactly one `UserActionFailed` entry whose action is `LoadRuntimeContainerDetail`, not the total number of log entries, because the availability check can add its own entry.

## Security notes

Security plan review for issue 40, mode `plan`. Inputs: `/home/user/DockerUpdateGuard/specs/issue-40/plan.md` and `/home/user/DockerUpdateGuard/docs/decisions/0059-a-detail-page-shows-a-missing-record-or-a-failed-load-inline.md` at HEAD `20addd8`.

I found no blocking problems. There is one non-blocking finding, about older behaviour this plan does not cause.

**Tier and listed areas**
- The tier `standard` is right. The plan adds no security control and changes or removes none. It only sends one more failure path through the existing `FailureReason.FromException` and `UserActionFailed` (event 4000). `FailureReason.cs`, `ReportableException.cs` and `ComponentLoggingExtensions.cs` stay unchanged.
- *Security areas touched* lists all three areas the change reaches:
  - Failure messages and remote text
  - Logging and telemetry of external data
  - Container actions (`RuntimeContainerDetail.razor`)
- I found no area that is touched but not listed. The change parses no input, adds no outbound call, and the pages still depend only on `IApplicationViewService`.

**What I checked by reading the code**
- **Fixed reason in the alert.** In `src/DockerUpdateGuard/Infrastructure/FailureReason.cs:49-85`, `FromException` returns the exception's message only for a `ReportableException`. Every other type maps to a fixed string, and `InvalidOperationException` falls through to `unexpected error`. So the AC2 example ("secret text" gives `Loading the observed image failed: unexpected error`) holds, and the prefixes in the plan's table are all fixed strings. This keeps decision 0009 and the Integration failures rules.
- **Logged exception.** `docs/areas/telemetry.md` (*Credentials and external data*) already lets the error entries of user actions carry "the exception with its message, which can name a registry, Docker or database host". The new calls use the same message with a fixed action name. They do not log the route values (`ContainerId` and the GUIDs come from the URL), and no new log template is added. I ran the *Logger arguments* concern on the planned call `UserActionFailed(action, exception)`: `action` is always a constant from the plan's table, so no logger argument carries external data.
- **Rendered text.** The not-found note and the alert are plain text, encoded by Razor. The not-found note does not echo the route identifier.
- **Container actions.** The plan makes these stricter, not looser. `ConfirmAndExecuteAsync` (`RuntimeContainerDetail.razor.cs`, around line 373) refuses when `_detail is null`. A failed load now sets `_detail = null`, and the action buttons sit inside the detail branch, so nothing can be triggered from the not-found or error state. The user actions keep their own `catch` and prefix (AC5), so an action's reload is still reported under its own message.
- **Prerender.** A failed or not-found prerender saves nothing to persisted state (`PersistDetail` stores only a non-null detail). No exception data reaches the persisted JSON.
- **No measurements.** The plan makes no security claim that depends on measured or library behaviour, so I ran no commands.

**Findings**

[NON-BLOCKING] Stale container detail while a new route is loading (this existed before the change)
- File: `src/DockerUpdateGuard/Components/Pages/RuntimeContainerDetail.razor.cs`. `OnParametersSetAsync` around line 488 does not clear `_detail` before `LoadAsync`, and the actions target the route values (`ExecuteActionAsync` at around line 444 calls `ContainerActionService.ExecuteAsync(DockerInstanceId, ContainerId, …)`).
- The scenario: the same page instance gets a new `ContainerId`. This happens with browser back/forward between two container detail pages, because Blazor reuses the component. While the new load is pending, the previous container's detail and action buttons stay on screen. The confirmation dialog would name the old container (`detail.ContainerName`), but the action would run against the new route's container.
- The plan does not cause this: AC4 clears the old detail only when the new load answers `null` or throws. Fixing it would also change AC3/AC4 behaviour, so it belongs in its own issue.
- Smallest fix: in `OnParametersSetAsync`, when the route key differs from the loaded one, set `_detail = null` (and `_availability = null`) before loading, so the skeleton shows and `ConfirmAndExecuteAsync`'s guard applies.
- Proposed issue, title: "A runtime container detail page keeps the previous container's actions while a new route loads". Body: "`RuntimeContainerDetail` keeps `_detail` and `_availability` of the previous route until the new load completes, while its actions use the new route's `DockerInstanceId`/`ContainerId`; a confirmation during that window names the old container and acts on the new one. Clear both when the route key changes before the load."
- Recommendation: issue
