# Plan: The runtime container page keeps the previous container's actions while a new route loads

Source: Issue #540
Tier: standard — a behavior change of the runtime container page (what it shows and sends after a route change) that creates or removes no security control: the app has no authentication, and the server's checks at confirmation stay unchanged.
Security areas touched: Container actions (`Components/Pages/RuntimeContainerDetail.razor.cs`)

## Problem / root cause

Blazor reuses the page instance when the route moves to another container (fact 1). `OnParametersSetAsync`
(`RuntimeContainerDetail.razor.cs:494-504`) starts the new load without clearing `_detail`, `_availability`,
`_actionOutcome` or `_errorMessage`; `DetailLoadState.RunAsync` clears only its own outcome, and `_detail` only on a
failed load (`DetailLoadState.cs:50-67`). The page renders the old detail with the old action section while
`ExecuteActionAsync` sends `DockerInstanceId`/`ContainerId` of the new route (`:449`). Three related defects of the
same pairing, found on the way:

- **Late answers.** `LoadAsync` (`:245-251`) and `LoadAvailabilityAsync` (`:333-360`) assign their answer whatever
  route is current when it arrives; a pending `OnParametersSetAsync` or `OnAfterRenderAsync` is not awaited by the
  next parameter set or render (fact 2), so a slow answer for the earlier container overwrites the current one: the
  dialog then names A, or shows A's endpoint and own-container warning, and acts on B.
- **Dialog across the move.** `ShowConfirmationAndExecuteAsync` (`:410-430`) reads the route identifiers only after
  the dialog closed (`ExecuteActionAsync`, `:449`): a dialog opened for A and confirmed after the move executes on B.
- **Outcome on the wrong page.** An action that ends after the move sets `_actionOutcome` or `_errorMessage`
  (`:452`, `:464`) on B's page.

The other three detail pages also keep the previous detail during a route change but offer no action on it
(fact 6): out of scope, follow-up below.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | Navigating between two routes of the same page reuses the component and calls `SetParametersAsync`/`OnParametersSetAsync` again | ASP.NET Core docs, *Routing* ("route parameters … component reused"); existing test `RuntimeContainerDetailRouteChangeAfterLoadFailureClearsAlert` re-renders the same instance with a new `ContainerId` | confirmed |
| 2 | `ComponentBase` does not wait for a pending `OnParametersSetAsync`/`OnAfterRenderAsync` before the next parameter set or render | `ComponentBase.SetParametersAsync`/`CallOnParametersSetAsync` and `OnAfterRenderAsync` source: no serialization | confirmed |
| 3 | The action section and its buttons render only inside the `_detail is not null` branch, so clearing `_detail` shows the skeleton and no buttons | `RuntimeContainerDetail.razor:25`, `:122-150`, `:494-503` | confirmed |
| 4 | `ConfirmAndExecuteAsync` returns without a dialog when `_detail` or `_availability` is null | `RuntimeContainerDetail.razor.cs:378` | confirmed |
| 5 | `OnAfterRenderAsync` re-requests the availability only when `_availabilityKey` differs from the route key | `:507-519` | confirmed |
| 6 | Docker instance, own image and observed image detail pages call no service but the view service's load | `grep "Service\.\w*Async("` in their `.razor.cs` | confirmed |
| 7 | Record 0059 is unreleased: commit `6642a68` (0059) is not in `v1.3.10` | `git merge-base --is-ancestor 6642a68 <v1.3.10>` false; `git ls-remote --tags origin` | confirmed |
| 8 | The detail carries the identifiers the view service was asked for, so on the current route they equal the route's | `ApplicationViewService.cs:1768-1769` (`CreateRuntimeContainerDetail` sets them from its arguments, the query matches `ContainerId == containerId`); every existing test fixture sets them so | confirmed |

## Acceptance criteria

The page in these criteria is one instance of `RuntimeContainerDetail` whose route moves from container A to
container B of the same or another Docker instance (any change of `DockerInstanceId` or `ContainerId`, ordinal).

- AC1: Right after the move, while B's detail load is pending, the page shows the loading skeleton only: no
  container name of A, no *Container actions* section, no action outcome alert and no action error alert of A.
- AC2: A successful detail answer for A (a detail or "not found") that arrives after the move is dropped: while B's
  load is pending the page still shows the skeleton, without A's name or the not-found panel, and once B's detail is
  shown it stays B's. (A late *failed* answer of A is the follow-up below.)
- AC3: An availability answer for A that arrives after the move is dropped: the section shows B's availability
  (once it arrives), never A's message or buttons.
- AC4: A confirmation sends no action (`ExecuteAsync` is not called) and shows no outcome or error alert when the
  dialog was opened on A and confirmed after the move, or when the shown detail's Docker instance and container
  identifiers differ (ordinal) from the route's.
- AC5: A confirmation or action of A that ends after the move: the action was sent with A's identifiers only, its
  outcome — a run result, a failed action or a failed confirmation dialog — is not shown on B's page, and while it
  runs B's offered buttons are disabled and none shows progress.
- AC6: A parameter set with the same Docker instance and container (a re-render of the route) keeps the shown detail
  while it reloads, as today.
- AC7: A tag selection saved or cleared on A that fails after the move was sent with A's identifiers and shows no
  `Saving the tag selection failed` / `Clearing the tag selection failed` alert on B's page.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `RuntimeContainerDetailRouteChangeWhileLoadPendingShowsSkeletonWithoutActions`, `RuntimeContainerDetailRouteChangeAfterActionClearsOutcome` | `src/Tests/DockerUpdateGuard.Tests/RuntimeContainerDetailTests.cs` |
| AC2 | `RuntimeContainerDetailLateDetailOfPreviousRouteIsDropped`, `RuntimeContainerDetailLateNotFoundOfPreviousRouteIsDropped` | same |
| AC3 | `RuntimeContainerDetailLateAvailabilityOfPreviousRouteIsDropped` | same |
| AC4 | `RuntimeContainerDetailConfirmationAfterRouteChangeExecutesNothingAsync`, `RuntimeContainerDetailConfirmationForDetailOfOtherContainerExecutesNothingAsync` | same |
| AC5 | `RuntimeContainerDetailActionEndingAfterRouteChangeShowsNoOutcomeAsync`, `RuntimeContainerDetailActionFailingAfterRouteChangeShowsNoErrorAsync`, `RuntimeContainerDetailConfirmationFailingAfterRouteChangeShowsNoErrorAsync` | same |
| AC6 | `RuntimeContainerDetailSameRouteReloadKeepsDetailShown` | same |
| AC7 | `RuntimeContainerDetailSelectionFailingAfterRouteChangeShowsNoErrorAsync`, `RuntimeContainerDetailClearSelectionFailingAfterRouteChangeShowsNoErrorAsync` | same |

Cases: every detail fixture of a new test sets `DockerInstanceId` and `ContainerId` to its route's values, as the
existing fixtures do (fact 8). AC1's first test loads A with offered actions shown, moves to B with a pending
`TaskCompletionSource`, and asserts `DetailPageAssert.AssertLoading`, no `Start`/`Stop`/`Restart` button and no text
of A's name; the second completes an action on A (outcome alert shown), then moves. AC2 keeps A's first load pending,
moves to B (pending), completes A with a detail (first test) or `null` (second; no not-found panel), asserts the
skeleton, completes B, and asserts B's name and not A's. AC3 keeps A's availability pending, moves to B, answers B
(one offered action), then A (a different message), and asserts B's buttons. AC4's first test renders the
`MudDialogProvider` as `RuntimeContainerDetailCancelledConfirmationExecutesNothingAsync` does, opens the dialog on A,
re-renders the page for B, confirms in the provider; the second answers the route with a detail whose `ContainerId`
differs and clicks an action; both assert `DidNotReceive().ExecuteAsync(…)` for any arguments. AC5's first two hold
`ExecuteAsync(A, …)` pending, confirm, move to B, answer B's availability with the same offered action, assert B's
buttons disabled without a `mud-progress-circular`, then complete it with a result or an exception and assert
`Received(1).ExecuteAsync(A's ids, …)` and no outcome or `Container action failed` alert; the third uses a substitute
`IDialogService` whose reference's `Result` is a pending task, moves, then faults it. AC6 re-renders with the same
parameters while the reload is pending and asserts A's name is still shown. AC7 holds the substitute
`IRuntimeContainerTagSelectionService`'s `SaveSelectionAsync` / `ClearSelectionAsync` for A pending, clicks *Select* /
*Clear*, moves, faults it, and asserts `Received(1)` with A's ids and no alert.

## Approach

All changes are private members of `RuntimeContainerDetail.razor.cs`, plus one condition in the markup (step 6).

1. A private property `RouteKey` (`$"{DockerInstanceId}/{ContainerId}"`, the key `OnAfterRenderAsync` builds today,
   which then uses it) and a field `_routeKey` (route key of the last parameter set). The Guid has a fixed length, so
   the key is unambiguous. One private helper `IsCurrentRoute(Guid dockerInstanceId, string containerId)` (ordinal
   comparison with `RouteKey`) is the only route check of steps 3–7.
2. `OnParametersSetAsync`, before the restored-state check, on the dispatcher: when `_routeKey` differs from
   `RouteKey` and is not null, set `_detail`, `_availability`, `_availabilityKey`, `_actionOutcome` and
   `_errorMessage` to null (`_availabilityKey` so the next render asks again even if the route returns to the key
   asked last); then `_routeKey = RouteKey`. The restored path also records the key. `_runningAction` and `_isBusy`
   stay: an action or tag selection of A still in flight keeps B's buttons disabled until it ends (fail safe).
3. `LoadAsync` and `LoadAvailabilityAsync` take the identifiers into locals once, call the service with them, and
   assign their answer (in the failure branch of `LoadAvailabilityAsync` too) inside `InvokeAsync` only when
   `IsCurrentRoute(locals)`. The whole assignment `_detail = _loadState.Found(detail)` runs inside the guard, so a
   late answer also leaves `IsNotFound` alone.
4. `ConfirmAndExecuteAsync` takes `_detail` into a local together with the existing null checks and, still
   synchronously on the dispatcher (the click handler runs up to `DialogService.ShowAsync` without yielding), returns
   unless `IsCurrentRoute(detail.DockerInstanceId, detail.ContainerId)`: the identifiers the action uses are by
   construction those of the detail the dialog names (fact 8). `ShowConfirmationAndExecuteAsync` passes
   `detail.DockerInstanceId` and `detail.ContainerId` to `ExecuteActionAsync`, which gets two parameters for them. Its
   first `InvokeAsync` checks them with `IsCurrentRoute`: on a mismatch it sets nothing and returns before
   `ExecuteAsync`. Otherwise it records them as the running action's route (step 6), sends `ExecuteAsync` with them,
   and sets `_actionOutcome` and the action's `_errorMessage` only while `IsCurrentRoute` holds. The reloads after
   the action stay as they are (they load the current route). Whether MudBlazor closes an open dialog on navigation is
   not relied on: a dialog closed that way answers *canceled* and sends nothing either.
5. The outer catch of `ConfirmAndExecuteAsync` (`:391-399`) sets its `_errorMessage` only when
   `IsCurrentRoute(detail.DockerInstanceId, detail.ContainerId)` (the local of step 4); the log call is unchanged.
6. A field `_runningActionRouteKey` is set with `_runningAction` and cleared with it. The button markup
   (`RuntimeContainerDetail.razor:151`) shows the progress circle only when `_runningAction == offeredAction` and
   `_runningActionRouteKey == RouteKey`; `Disabled` stays `_runningAction is not null`.
7. `SaveSelectionAsync` and `ClearSelectionAsync` take the identifiers into locals at their start, send them, and set
   their `_errorMessage` only when `IsCurrentRoute(locals)`; their reload is step 3's.

No new user-visible output.

Early draft PR: no — the bUnit tests cover every criterion

## Options considered

Decision record 0060: (1) clear on route change, drop late answers, send nothing after the move — chosen;
(2) the issue's smallest fix, clearing before the load only — loses: late answers and a dialog open across the move
still pair A's name with B's identifiers; (3) execute with the identifiers the dialog named — loses: acts on a
container the page no longer shows, its outcome on another page; (4) the guard in `DetailLoadState` for all four
detail pages — loses for now: three pages without actions, cosmetic gain; follow-up.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Components/Pages/RuntimeContainerDetail.razor.cs` | route key, clearing, late-answer, confirmation and tag-selection guards |
| DockerUpdateGuard | `Components/Pages/RuntimeContainerDetail.razor` | progress-circle condition (step 6) |
| DockerUpdateGuard.Tests | `RuntimeContainerDetailTests.cs` | thirteen new tests |

## Signatures (for the Dev's skeleton)

None: every changed member is private (*Approach*). `ExecuteActionAsync` gains the parameters `Guid dockerInstanceId`
and `string containerId` after `actionType` (three parameters, wrapped as the style rules say); `RouteKey`,
`_routeKey`, `_runningActionRouteKey` and `IsCurrentRoute` are new private members in the `Properties`, `Fields` and
`Methods` regions.

## Test files

`src/Tests/DockerUpdateGuard.Tests/RuntimeContainerDetailTests.cs` (Tester), using its `CreateActionContext`,
`CreateLoadContext`, `RenderLoadPage` and `GetActionButtonTexts` helpers.

Existing test code that calls a changed signature: none. Existing tests kept unchanged: none of them moves the route
except `RuntimeContainerDetailRouteChangeAfterLoadFailureClearsAlert`, which already asserts the skeleton after the
move; every existing detail fixture already carries its route's identifiers (fact 8), so step 4's check passes them.

Files no unit test can cover: none.

## Areas

- `docs/areas/web-ui.md` (+95 words): *Layout* (lines 40-44): the runtime container page is the exception to "the
  previously loaded detail stays shown" — it clears its detail, action section and action outcome at once and shows
  the skeleton, and drops answers for an earlier route. *Container actions section*: a confirmation given after the
  page moved to another container sends and records nothing; while an action of the earlier container still runs,
  the new container's buttons are disabled without progress; the outcome or failure of an action, confirmation or tag
  selection that ends after such a move is not shown (an action's run is in its container's history).

## Documentation updates

- `docs/areas/web-ui.md` (Dev): as *Areas*, once the code is green.
- Sweep: `docs/ARCHITECTURE.md` (lines 410-421, 459: the confirmation dialog, no route statement), `container-actions.md`
  (checks at confirmation: unchanged, server side), `README.md`: no change. No changelog exists.

## Architecture check

No guarantee of `.squad/project.md` is touched; the server-side checks at confirmation
(`container-actions.md`, *Checks at confirmation*) and the one-action-per-container guard (decision 0036) are
unchanged. Layering unchanged.

## Security considerations

- [x] No limits.
- [x] Exceptions that reach a user-visible message: none new; a failed action, confirmation or tag selection on the
  current route still shows its fixed-reason message (`FailureReason`), one of an earlier route shows nothing.

## Decision records

- `docs/decisions/0060-the-runtime-container-page-acts-only-on-the-state-of-its-current-route.md` (Proposed)

## Out of scope / follow-ups

- Proposed issue "[UI] Detail pages show the previous record and late load answers after a route change": the Docker
  instance, own image and observed image pages keep the previous detail during a route change, and
  `DetailLoadState.RunAsync`'s second step applies the outcome of a late load of an earlier route unconditionally
  (`DetailLoadState.cs:58-67`), also on the runtime container page: a late *failure* drops the current detail and
  shows the load alert; a late *success* sets `ErrorMessage = null` after the current route's load failed, hiding
  that alert and leaving the skeleton up. Guard the shared helper by route key (decision 0060, option 4).

## Challenge

Minor notes of the Devil's Advocate, each decided:

1. Late `RunAsync` failure or success of A changes B's page — **follow-up**. Guarding `RunAsync`'s second step
   changes the helper all four detail pages share (option 4 of 0060); both outcomes fail safe here (no detail, no
   actions, nothing sent). AC2 now reads "a successful detail answer"; the follow-up names both cases.
2. A's progress spinner on B's button — **folded in**: Approach step 6, the button stays disabled without progress
   (AC5, the *Areas* text).
3. The outer catch of `ConfirmAndExecuteAsync` sets its error without a route check — **folded in**: Approach step 5,
   AC5's third test.
4. Tag selection save/clear failures land on B's page — **folded in**: the same pattern in two methods, Approach step
   7, AC7.

## Security review

Both non-blocking findings recommended `fix now`, both taken:

1. The sent identifiers equal the shown detail's — **fixed**: Approach step 4 checks `detail.DockerInstanceId` /
   `detail.ContainerId` against the route (ordinal) before the dialog and sends those identifiers; the view service
   answers the identifiers it was asked for (fact 8). AC4's second test covers a mismatch.
2. A late "not found" of A on B's page — **fixed**: Approach step 3 guards the whole `_loadState.Found` assignment;
   AC2's second test.
