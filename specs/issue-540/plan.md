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

## Acceptance criteria

The page in these criteria is one instance of `RuntimeContainerDetail` whose route moves from container A to
container B of the same or another Docker instance (any change of `DockerInstanceId` or `ContainerId`, ordinal).

- AC1: Right after the move, while B's detail load is pending, the page shows the loading skeleton only: no
  container name of A, no *Container actions* section, no action outcome alert and no action error alert of A.
- AC2: A detail answer for A that arrives after the move is dropped: while B's load is pending the page still shows
  the skeleton, and once B's detail is shown it stays B's.
- AC3: An availability answer for A that arrives after the move is dropped: the section shows B's availability
  (once it arrives), never A's message or buttons.
- AC4: A confirmation dialog opened on A and confirmed after the move sends no action (`ExecuteAsync` is not
  called) and shows no outcome or error alert.
- AC5: An action confirmed on A that ends after the move was sent with A's identifiers only, and its outcome — a
  run result or a failure — is not shown on B's page.
- AC6: A parameter set with the same Docker instance and container (a re-render of the route) keeps the shown detail
  while it reloads, as today.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `RuntimeContainerDetailRouteChangeWhileLoadPendingShowsSkeletonWithoutActions`, `RuntimeContainerDetailRouteChangeAfterActionClearsOutcome` | `src/Tests/DockerUpdateGuard.Tests/RuntimeContainerDetailTests.cs` |
| AC2 | `RuntimeContainerDetailLateDetailOfPreviousRouteIsDropped` | same |
| AC3 | `RuntimeContainerDetailLateAvailabilityOfPreviousRouteIsDropped` | same |
| AC4 | `RuntimeContainerDetailConfirmationAfterRouteChangeExecutesNothingAsync` | same |
| AC5 | `RuntimeContainerDetailActionEndingAfterRouteChangeShowsNoOutcomeAsync`, `RuntimeContainerDetailActionFailingAfterRouteChangeShowsNoErrorAsync` | same |
| AC6 | `RuntimeContainerDetailSameRouteReloadKeepsDetailShown` | same |

Cases: AC1's first test loads A with offered actions shown, moves to B with a pending `TaskCompletionSource`, and
asserts `DetailPageAssert.AssertLoading`, no `Start`/`Stop`/`Restart` button and no text of A's name; the second
completes an action on A (outcome alert shown), then moves. AC2 keeps A's first load pending, moves to B (pending),
completes A (skeleton still), completes B, and asserts B's name and not A's. AC3 keeps A's availability pending,
moves to B, answers B (one offered action), then A (a different message), and asserts B's buttons. AC4 renders the
`MudDialogProvider` as `RuntimeContainerDetailCancelledConfirmationExecutesNothingAsync` does, opens the dialog on A,
re-renders the page for B, confirms in the provider, and asserts `DidNotReceive().ExecuteAsync(…)` for any arguments.
AC5 holds `ExecuteAsync(A, …)` pending, confirms, moves to B, then completes it with a result (first test) or an
exception (second) and asserts `Received(1).ExecuteAsync(A's ids, …)` and no outcome or `Container action failed`
alert. AC6 re-renders with the same parameters while the reload is pending and asserts A's name is still shown.

## Approach

All changes are private members of `RuntimeContainerDetail.razor.cs`; the markup is unchanged.

1. A private property `RouteKey` (`$"{DockerInstanceId}/{ContainerId}"`, the key `OnAfterRenderAsync` builds today,
   which then uses it) and a field `_routeKey` (route key of the last parameter set). The Guid has a fixed length, so
   the key is unambiguous.
2. `OnParametersSetAsync`, before the restored-state check, on the dispatcher: when `_routeKey` differs from
   `RouteKey` and is not null, set `_detail`, `_availability`, `_availabilityKey`, `_actionOutcome` and
   `_errorMessage` to null (`_availabilityKey` so the next render asks again even if the route returns to the key
   asked last); then `_routeKey = RouteKey`. The restored path also records the key. `_runningAction` and `_isBusy`
   stay: an action or tag selection of A still in flight keeps B's buttons disabled until it ends (fail safe).
3. `LoadAsync` and `LoadAvailabilityAsync` take the identifiers into locals once, call the service with them, and
   assign their answer (in the failure branch of `LoadAvailabilityAsync` too) inside `InvokeAsync` only when the
   key of those locals still equals `RouteKey`.
4. `ShowConfirmationAndExecuteAsync` copies `DockerInstanceId` and `ContainerId` into locals at its start (still on
   the dispatcher: the click handler runs synchronously up to `DialogService.ShowAsync`) and passes them to
   `ExecuteActionAsync`, which gets two parameters for them. Its first `InvokeAsync` checks them against the current
   route: on a mismatch it sets nothing and returns before `ExecuteAsync`. Otherwise it sends `ExecuteAsync` with
   those locals, and sets `_actionOutcome` and the action's `_errorMessage` only while the route is still theirs. The
   reloads after the action stay as they are (they load the current route). Whether MudBlazor closes an open dialog
   on navigation is not relied on: a dialog closed that way answers *canceled* and sends nothing either.
5. One private helper `IsCurrentRoute(Guid dockerInstanceId, string containerId)` (ordinal comparison with
   `RouteKey`) serves steps 3 and 4, so the check exists once.

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
| DockerUpdateGuard | `Components/Pages/RuntimeContainerDetail.razor.cs` | route key, clearing, late-answer and confirmation guards |
| DockerUpdateGuard.Tests | `RuntimeContainerDetailTests.cs` | eight new tests |

## Signatures (for the Dev's skeleton)

None: every changed member is private (*Approach*). `ExecuteActionAsync` gains the parameters `Guid dockerInstanceId`
and `string containerId` after `actionType` (three parameters, wrapped as the style rules say); `RouteKey`,
`_routeKey` and `IsCurrentRoute` are new private members in the `Properties`, `Fields` and `Methods` regions.

## Test files

`src/Tests/DockerUpdateGuard.Tests/RuntimeContainerDetailTests.cs` (Tester), using its `CreateActionContext`,
`CreateLoadContext`, `RenderLoadPage` and `GetActionButtonTexts` helpers.

Existing test code that calls a changed signature: none. Existing tests kept unchanged: none of them moves the route
except `RuntimeContainerDetailRouteChangeAfterLoadFailureClearsAlert`, which already asserts the skeleton after the move.

Files no unit test can cover: none.

## Areas

- `docs/areas/web-ui.md` (+75 words): *Layout* (lines 40-44): the runtime container page is the exception to "the
  previously loaded detail stays shown" — it clears its detail, action section and action outcome at once and shows
  the skeleton, and drops answers for an earlier route. *Container actions section*: a confirmation given after the
  page moved to another container sends and records nothing; the outcome of an action that ends after such a move is
  not shown (its run is in its container's history).

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
- [x] Exceptions that reach a user-visible message: none new; a failed action on the current route still shows
  `Container action failed: <fixed reason>` (`FailureReason`), one on an earlier route shows nothing.

## Decision records

- `docs/decisions/0060-the-runtime-container-page-acts-only-on-the-state-of-its-current-route.md` (Proposed)

## Out of scope / follow-ups

- Proposed issue "[UI] Detail pages show the previous record and late load answers after a route change": the Docker
  instance, own image and observed image pages keep the previous detail during a route change, and
  `DetailLoadState.RunAsync` applies a late answer — a failed one too, also on the runtime container page — of an
  earlier route; guard the shared helper by route key (decision 0060, option 4).
