# Plan: Detail pages show the previous record and late load answers after a route change

Source: Issue #544
Tier: standard — changes what four pages show during and after a route change (behavior and control flow of the
shared load helper), no security control.
Security areas touched: none

## Problem / root cause

- `DetailLoadState.RunAsync` (`src/DockerUpdateGuard/Components/DetailLoadState.cs`, second `invoke`) sets
  `ErrorMessage = errorMessage` and, on a failure, calls `clearDetail()` for every load that ends, whatever route it
  was started for. A late failure of an earlier route therefore drops the current detail and shows the alert; a late
  success sets `ErrorMessage = null` and hides the current route's alert (with no detail, the page shows its skeleton).
- `DockerInstanceDetail`, `MyImageDetail` and `ObservedImageDetail` (`OnParametersSetAsync`) never clear `_detail` on
  a route change, so the previous record stays shown while the new load is pending.
- Related defect found on the way: the same three pages' `LoadAsync` assign `_detail = _loadState.Found(detail)`
  without a route check, so a late *successful* answer of the earlier route replaces the current record (and a late
  not-found answer shows the not-found note). Only the runtime container page guards this (`LoadAsync`,
  `IsCurrentRoute`, #540).

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | The second step of `RunAsync` applies every load's outcome unconditionally | `DetailLoadState.cs`, `RunAsync`, second `invoke` lambda | confirmed |
| 2 | The three pages keep the previous record during a route change | their `OnParametersSetAsync` has no clearing; area doc `web-ui.md` *General* says "the previously loaded detail stays shown" | confirmed |
| 3 | The runtime container page clears its state on a route change and drops a late successful/not-found answer | `RuntimeContainerDetail.razor.cs` `OnParametersSetAsync` (`_lastRouteKey`), `LoadAsync` (`IsCurrentRoute`); tests `RuntimeContainerDetailLateDetailOfPreviousRouteIsDropped`, `…LateNotFoundOfPreviousRouteIsDropped` | confirmed |
| 4 | On the runtime page a late failure of the earlier route drops the current detail and shows the alert; a late success hides the current alert | follows from fact 1: `clearDetail` is `() => _detail = null`, the alert renders from `_loadState.ErrorMessage`; stated in `web-ui.md` *General* and record 0060 *Consequences* | confirmed |
| 5 | The three pages also apply a late successful answer of an earlier route | their `LoadAsync`: `InvokeAsync(() => _detail = _loadState.Found(detail))` without a check (not in the issue) | confirmed |
| 6 | `ComponentBase.InvokeAsync` runs the callback inline when already on the renderer's context, so the first step of `RunAsync`, called from `OnParametersSetAsync`, cannot end late | Blazor's `RendererSynchronizationContextDispatcher.InvokeAsync` executes synchronously when `CheckAccess()` holds; every read and write of the route key below happens on that context | confirmed |
| 7 | A restored first render returns before `RunAsync`; the runtime page records its route key before that return | the four `OnParametersSetAsync` (`_restoredFromState`) | confirmed |
| 8 | `DetailLoadState` is used only by the four detail pages and has no test file | `grep -rl DetailLoadState src`; no `DetailLoadStateTests.cs` | confirmed |
| 9 | Record 0060 is unreleased | `git tag --contains a41c6dd` prints nothing (latest tag v1.3.9) | confirmed |

## Acceptance criteria

- AC1: On the Docker instance, own image and observed image detail pages, a change of the route identifier hides the
  previous record at once: the page shows its loading skeleton (no detail, no not-found note, no alert) until the new
  route's load ends — also when the first render reused the prerendered record (persistent state).
- AC2: On every detail page, a record or not-found answer of a load for an earlier route that arrives after the route
  change shows nothing of it: the current route's skeleton, detail, not-found note or alert stays.
- AC3: On every detail page, a failed load started before the latest route change (also when the page has since
  returned to that load's route, A → B → A) that ends after the change shows no load alert
  and keeps the current route's detail (on the runtime container page with its action section); the failure is still
  logged once as `UserActionFailed` with the page's load action. Decided by the second step of
  `DetailLoadState.RunAsync`.
- AC4: On every detail page, a successful load started before the latest route change (also for A → B → A) that ends
  after the current load failed
  keeps the current alert `<prefix>: <reason>` shown. Decided by the second step of `DetailLoadState.RunAsync`.
- AC5: A re-render with the same route identifier keeps the shown record until its reload completes, on every detail
  page (unchanged behavior).

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `DetailLoadStateSetRouteReportsOnlyChangedKey` | `src/Tests/DockerUpdateGuard.Tests/DetailLoadStateTests.cs` |
| AC1 | `DockerInstanceDetailRouteChangeWhileLoadPendingShowsSkeleton` | `src/Tests/DockerUpdateGuard.Tests/DockerInstanceDetailTests.cs` |
| AC1 | `MyImageDetailRouteChangeWhileLoadPendingShowsSkeleton` | `src/Tests/DockerUpdateGuard.Tests/MyImageDetailTests.cs` |
| AC1 | `ObservedImageDetailRouteChangeWhileLoadPendingShowsSkeleton` | `src/Tests/DockerUpdateGuard.Tests/ObservedImageDetailTests.cs` |
| AC1 | `DockerInstanceDetailRouteChangeAfterRestoreShowsSkeleton` | `src/Tests/DockerUpdateGuard.Tests/DockerInstanceDetailPersistentStateTests.cs` |
| AC1 | `MyImageDetailRouteChangeAfterRestoreShowsSkeleton` | `src/Tests/DockerUpdateGuard.Tests/MyImageDetailPersistentStateTests.cs` |
| AC1 | `ObservedImageDetailRouteChangeAfterRestoreShowsSkeleton` | `src/Tests/DockerUpdateGuard.Tests/ObservedImageDetailPersistentStateTests.cs` |
| AC2 | `DetailLoadStateFoundForEarlierRouteKeepsShownDetail` | `src/Tests/DockerUpdateGuard.Tests/DetailLoadStateTests.cs` |
| AC2 | `DockerInstanceDetailLateDetailOfPreviousRouteIsDropped` | `src/Tests/DockerUpdateGuard.Tests/DockerInstanceDetailTests.cs` |
| AC2 | `MyImageDetailLateDetailOfPreviousRouteIsDropped` | `src/Tests/DockerUpdateGuard.Tests/MyImageDetailTests.cs` |
| AC2 | `ObservedImageDetailLateDetailOfPreviousRouteIsDropped` | `src/Tests/DockerUpdateGuard.Tests/ObservedImageDetailTests.cs` |
| AC3 | `DetailLoadStateLateFailureOfEarlierRouteKeepsCurrentOutcome` (A → B and A → B → A) | `src/Tests/DockerUpdateGuard.Tests/DetailLoadStateTests.cs` |
| AC3 | `DockerInstanceDetailLateLoadFailureOfPreviousRouteKeepsCurrentDetail` | `src/Tests/DockerUpdateGuard.Tests/DockerInstanceDetailTests.cs` |
| AC3 | `RuntimeContainerDetailLateLoadFailureOfPreviousRouteKeepsCurrentDetailAsync` | `src/Tests/DockerUpdateGuard.Tests/RuntimeContainerDetailTests.cs` |
| AC4 | `DetailLoadStateLateSuccessOfEarlierRouteKeepsCurrentAlert` (A → B and A → B → A) | `src/Tests/DockerUpdateGuard.Tests/DetailLoadStateTests.cs` |
| AC4 | `DockerInstanceDetailLateDetailAfterCurrentLoadFailureKeepsAlert` | `src/Tests/DockerUpdateGuard.Tests/DockerInstanceDetailTests.cs` |
| AC4 | `RuntimeContainerDetailLateDetailAfterCurrentLoadFailureKeepsAlert` | `src/Tests/DockerUpdateGuard.Tests/RuntimeContainerDetailTests.cs` |
| AC5 | `DetailLoadStateSetRouteReportsOnlyChangedKey` (same key answers false) | `src/Tests/DockerUpdateGuard.Tests/DetailLoadStateTests.cs` |
| AC5 | `DockerInstanceDetailSameRouteReloadKeepsDetailShown` | `src/Tests/DockerUpdateGuard.Tests/DockerInstanceDetailTests.cs` |

Test cases the Tester derives:

- `DetailLoadStateTests` call the helper directly with a synchronous `invoke` (`action => { action(); return
  Task.CompletedTask; }`), `TaskCompletionSource` loads and a `TestLogger`. `SetRoute`: first key `false`, same key
  `false`, other key `true`. `Found` after `SetRoute("a")`, `SetRoute("b")`: `Found("a", detailA, shown)` returns
  `shown`, and `Found("a", null, shown)` leaves `IsNotFound` `false`; `Found("b", null, shown)` returns `null` and sets
  `IsNotFound`. Late failure: run A pending, `SetRoute("b")`, run B completes, A fails → `ErrorMessage` null,
  `clearDetail` not called, one `UserActionFailed` entry. Late success: run A pending, B fails, A completes →
  `ErrorMessage` is B's message. Both cases also run for A → B → A: run A1 pending, `SetRoute("b")`, `SetRoute("a")`,
  run A2 ends (success for the late-failure case, failure for the late-success case), then A1 ends → A2's outcome
  stays (`clearDetail` not called, or A2's message kept). The control case (the current route's failure sets the message and calls
  `clearDetail`) is covered by the existing `…LoadFailureShowsFixedErrorAndLogs` page tests.
- Page tests use the existing patterns: `page.Render(...)` to move the route (as `ObservedImageDetailRouteChangeTo…`),
  `DetailPageAssert.AssertLoading` / `AssertLoadedAsync` / `AssertLoadErrorAsync`, and for the runtime page its private
  `RenderRoutePage`, `MoveRoute`, `SettleAsync` with `pendingA`/`pendingB`. The AC3 runtime test also asserts that
  the current container's action button stays offered (`WaitForOfferedActionAsync`, `GetActionButtonTexts`).
- The restore tests persist a detail for id A (`GetPersistentComponentState().Persist`), render A, move to id B with a
  pending load, assert the skeleton and the absence of A's name.
- The runtime page's AC1, AC2 and AC5 are held by its existing #540 tests (`RuntimeContainerDetailRouteChangeWhileLoadPendingShowsSkeletonWithoutActions`,
  `…LateDetailOfPreviousRouteIsDropped`, `…LateNotFoundOfPreviousRouteIsDropped`, `…SameRouteReloadKeepsDetailShown`),
  which must stay green unchanged.

## Approach

`DetailLoadState` owns the route key:

- New private fields `_routeKey` (`string?`), the key of the latest parameter set, and `_routeChanges` (`int`), the
  number of route changes so far. `SetRoute(routeKey)` records the key and answers whether it replaced a different,
  earlier key (`false` on the first call and for the same key); each time it answers `true` it increments
  `_routeChanges`.
- `RunAsync` keeps its signature; it captures `_routeChanges` synchronously at its start (it is called from
  `OnParametersSetAsync` on the renderer's context, right after `SetRoute`) and in its second `invoke` returns at once,
  setting nothing and calling nothing, when `_routeChanges` has moved on. The counter, not the key, decides here, so a
  load started before A → B → A cannot set the outcome of the load started after the return (*Challenge*, objection
  1). Same-route reloads that never left the route stay unordered (AC5). The first step stays as it is (fact 6). `PageLoader.LoadAsync` still logs every failure, also a late one.
- `Found` takes the route key the load was requested for and the detail the page shows: while that key is `_routeKey`
  it sets `IsNotFound` and returns the loaded detail, otherwise it changes nothing and returns the shown detail. A
  page that never calls `SetRoute` keeps no answer (fails closed; record 0060 *Consequences*). `Found` keeps comparing
  keys: a late answer under the same key is that route's record.
- All reads and writes of `_routeKey` and `_routeChanges` happen on the renderer's synchronization context (`OnParametersSetAsync`, the
  `invoke` callbacks, `InvokeAsync` in `LoadAsync`), so no lock is needed.

Pages:

- `DockerInstanceDetail`, `MyImageDetail`, `ObservedImageDetail`: a private `RouteKey` property
  (`DockerInstanceId.ToString()` / `ObservedImageId.ToString()`). `OnParametersSetAsync` first calls
  `if (_loadState.SetRoute(RouteKey)) { _detail = null; }`, **before** the `_restoredFromState` return (AC1 after a
  restore); `LoadAsync` captures `var routeKey = RouteKey;` before the view-service call and assigns
  `_detail = _loadState.Found(routeKey, detail, _detail)`.
- `RuntimeContainerDetail`: `OnParametersSetAsync` replaces `_lastRouteKey` by `if (_loadState.SetRoute(routeKey))`
  around the same five clearings; `LoadAsync` captures `var routeKey = RouteKey;` with the two identifiers and
  replaces its `IsCurrentRoute` wrapper by `_detail = _loadState.Found(routeKey, detail, _detail)` (equivalent: the
  recorded key is the page's `RouteKey` on the renderer's context). Every other `IsCurrentRoute` use stays.

No new user-visible output: the existing skeleton, not-found note and alert are shown in new situations only.

Early draft PR: no

## Options considered

- **Route key and a count of route changes in the helper (chosen)** — the issue's request and record 0060 option 4;
  one place for all four pages; the count keeps A → B → A from reviving a load of the first visit.
- **The route key alone as the guard of `RunAsync`** — the first plan; a late failure of a load started before
  A → B → A matched the key again and dropped the current record (*Challenge*, objection 1).
- **A sequence number for every load** — orders same-route reloads too, which never showed another record; the runtime
  page already compares route keys for availability and actions (record 0060, option 5).
- **Route comparison and clearing copied into each page, helper unchanged** — leaves the helper applying a late
  failure, four copies of one comparison (the duplication condition on new code) (record 0060, option 6).
- **Pass the route key into `RunAsync` as a seventh parameter instead of `SetRoute`** — would still need the key
  recorded on the restored first render, which skips `RunAsync`, so the three pages would keep a `_lastRouteKey` copy
  each; rejected for the same duplication.
- **Leave the three pages showing the previous record (only guard the helper)** — the issue asks for the clearing; the
  previous record under a new route is the same mismatch #540 removed from the runtime page.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Components/DetailLoadState.cs` | `_routeKey`, `_routeChanges`, `SetRoute`, `Found` changed, guard in `RunAsync` |
| DockerUpdateGuard | `Components/Pages/DockerInstanceDetail.razor.cs` | `RouteKey`, `SetRoute` call and clearing, `Found` call |
| DockerUpdateGuard | `Components/Pages/MyImageDetail.razor.cs` | as above |
| DockerUpdateGuard | `Components/Pages/ObservedImageDetail.razor.cs` | as above |
| DockerUpdateGuard | `Components/Pages/RuntimeContainerDetail.razor.cs` | `_lastRouteKey` removed, `SetRoute`, `Found` call |

The `.razor` markup files stay unchanged (read: they render from `_detail` and `_loadState` only).

## Signatures (for the Dev's skeleton)

In `internal sealed class DetailLoadState`:

- `public bool SetRoute(string routeKey)` — region `Methods`; `<returns>` "True when the key replaces a different key
  of an earlier parameter set".
- `public T? Found<T>(string routeKey, T? detail, T? shown) where T : class` — replaces `Found<T>(T? detail)`, which is
  deleted.
- `RunAsync` keeps its signature (six parameters, within S107's seven); only its body changes.

New private members described in *Approach*: fields `_routeKey` and `_routeChanges` (new `Fields` region in `DetailLoadState`), property
`RouteKey` in the three pages (`Properties` region, as in `RuntimeContainerDetail`). Deleted: the field `_lastRouteKey`
of `RuntimeContainerDetail`.

The names were checked against *Writing code* (`.squad/stack.md`): no nested type, no keyword-named parameter, at
most six parameters. All five production files are rewritten by the skeleton step only where named above; the
existing `Found` call sites (four `LoadAsync`) must change in the skeleton so the solution builds.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/DetailLoadStateTests.cs` (new)
- `src/Tests/DockerUpdateGuard.Tests/DockerInstanceDetailTests.cs`, `MyImageDetailTests.cs`, `ObservedImageDetailTests.cs`,
  `RuntimeContainerDetailTests.cs`, `DockerInstanceDetailPersistentStateTests.cs`, `MyImageDetailPersistentStateTests.cs`,
  `ObservedImageDetailPersistentStateTests.cs` (extended), all under `src/Tests/DockerUpdateGuard.Tests/`

Existing test code that calls a changed signature: none (no test calls `DetailLoadState` or `Found`). Existing tests
whose input a criterion handles differently: none — the route-change tests of the observed image page
(`…RouteChangeToUnknownIdHidesPreviousDetail`, `…RouteChangeToFailingIdHidesPreviousDetail`) and the runtime page's
#540 tests assert states the change keeps; they stay unchanged.

Files of the change no unit test can cover: none.

## Areas

`docs/areas/web-ui.md (-10 words)`: *General*, the paragraph "When the page gets another route identifier, …" (lines
42–47) is rewritten — every detail page clears its detail, not-found note and load error at once and shows its
skeleton until the new load ends, and drops a record, not-found or failure answer that arrives for an earlier route;
the runtime container page also clears its action section and the outcome or error of an action, and drops a late
availability answer; the parenthesis about a late failed load is removed. *Related decisions*: the 0060 link and its
title.

## Documentation updates

- `docs/areas/web-ui.md` — as *Areas* (Dev), including the 0060 link under *Related decisions*: new file name
  `0060-a-detail-page-acts-only-on-the-state-of-its-current-route.md`, text "A detail page acts only on the state of
  its current route."
- Sweep: `docs/ARCHITECTURE.md` (line 409 names detail pages only for the vulnerability display), `README.md` (line 212,
  container actions), `docs/UNIT_TESTS.md` (no test double added), `.squad/project.md`: no change. No changelog file
  exists; the PR carries its release-notes label (`docs/CONTRIBUTING.md`).

## Architecture check

No guarantee of `.squad/project.md` or flow of `docs/ARCHITECTURE.md` changes: the pages still load through the view
service once per parameter set and through `PageLoader`; only which answers are applied changes.

## Security considerations

- [x] No limits.
- [x] Exceptions reaching a user-visible message: unchanged — a load failure still reaches the user only as
  `<prefix>: <fixed reason>` through `PageLoader`/`FailureReason`, and now only for the current route.

## Decision records

- `docs/decisions/0060-a-detail-page-acts-only-on-the-state-of-its-current-route.md` (Accepted, unreleased, extended in
  place): renamed from `0060-the-runtime-container-page-acts-only-on-the-state-of-its-current-route.md`, retitled,
  options 4–6, decision now options 1 and 4, consequences updated; index row updated.

## Challenge

- Objection 1 (major): a route-key guard in `RunAsync` fails for A → B → A — a late failure of the first visit's load
  drops the record of the second visit's load, a late success hides its alert. **Accepted.** `SetRoute` counts route
  changes and `RunAsync` compares the count captured at its start (*Approach*); `Found` keeps the key comparison; the
  AC3 and AC4 helper tests gain an A → B → A case (*Tests per criterion*, test cases); record 0060's option 5,
  decision and consequence on unordered loads were reworded to cover only reloads of a route that was never left.

## Out of scope / follow-ups

- Ordering two loads of the same route that was never left (record 0060, *Consequences*).
