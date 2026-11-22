# Plan: Observed Images registration failure handler can throw from its list reload

Source: Issue #344
Tier: standard — a behavior change (a failed reload no longer escapes the submit handler and kills the circuit) that creates, changes or removes no security control.
Security areas touched: Failure messages and remote text (what the Observed Images page shows for a failed registration); Logging and telemetry of external data (one more `UserActionFailed` log call carrying an exception)

## Problem / root cause

`ObservedImages.RegisterAsync` (`src/DockerUpdateGuard/Components/Pages/ObservedImages.razor.cs:182-216`) catches
every exception of the registration, logs it, sets `_errorMessage` and then, still inside the `catch` block, calls
`LoadAsync()` (line 210) without a guard. `LoadAsync` (line 156) calls `IApplicationViewService.GetManualObservedImagesAsync`,
which queries the database. When the database is unreachable — the usual reason the registration failed in the first
place — the reload throws, the `finally` block resets `_isBusy`, and the exception leaves the `OnValidSubmit` handler.
In Blazor Server an exception from an event handler is unhandled: the circuit ends and the user sees the generic error
UI instead of the page's error message. This breaks [Web UI, *Limits and errors*](../../docs/areas/web-ui.md#limits-and-errors):
"An error in a user action is shown as an error message on the page that started it; the page stays usable".

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | `RegisterAsync` calls `LoadAsync()` inside its `catch` block, unguarded | `ObservedImages.razor.cs:200-211` | confirmed |
| 2 | `LoadAsync` reads the list through `ViewService.GetManualObservedImagesAsync()` and only then assigns `_images`, so a failed reload leaves `_images` unchanged | `ObservedImages.razor.cs:156-162` | confirmed |
| 3 | The handler is the form's `OnValidSubmit`, so an exception escaping it is an unhandled event-handler exception | `ObservedImages.razor:31` | confirmed |
| 4 | No other page reloads inside a user-action `catch`: the other `catch (Exception …)` blocks of `Components/` (ScanHistory, RuntimeContainers, RuntimeContainerDetail, VulnerabilityRescanControl) only log and set a message | `grep -rn "catch (Exception" src/DockerUpdateGuard/Components -A12` | confirmed |
| 5 | `HandleRescannedAsync` also calls `LoadAsync`, but it runs as `OnRescanned` inside `VulnerabilityRescanControl.RescanAsync`'s `try`, so its failure is already shown as `Vulnerability rescan failed: …` and logged — consistent with *Limits and errors* ("a later step of the same action … can fail"), so no defect | `VulnerabilityRescanControl.razor.cs:76-102` | confirmed |
| 5a | The reload after a saved tag selection runs inside the save's `try`, so its failure is logged as `SaveTagSelection` | `RuntimeContainerDetail.razor.cs:276-285` | confirmed |
| 6 | The existing failure tests stub `GetManualObservedImagesAsync` to return `[]` on every call, so their reload succeeds and they are unaffected by the fix | `ObservedImagesTests.cs:93-94`, `148-149` | confirmed |
| 7 | `UserActionFailureAssert.AssertLogged` asserts exactly one log entry, so the new test with two entries asserts the entries itself | `src/Tests/DockerUpdateGuard.Tests/Helper/UserActionFailureAssert.cs` | confirmed |
| 8 | On the unfixed code the new test fails: bUnit's `SubmitAsync` faults with the reload exception, and the second log entry is missing in any case | to verify: the Tester, step 5 (the test must fail on the unfixed head) | unverified |
| 9 | The rule against nested `try` (S1141) is not in the project's Sonar profile; the plan still uses a separate method so no `try` is nested | `.editorconfig` `squad:sonar-rules` block has no `S1141` | confirmed |

## Acceptance criteria

- AC1: When the registration fails and the list reload that follows it fails as well, the submit completes without an
  exception escaping the handler, the page shows the registration's message as today — for an
  `InvalidOperationException` exactly `Registration failed: unexpected error` — and no text of either exception, and
  the `Register and scan` button is enabled again. The message is decided by the `catch` block of
  `ObservedImages.RegisterAsync` through `FailureReason.FromException(<registration exception>, …)`; the reload
  exception never reaches the message.
- AC2: The failed reload is logged as user action `RegisterObservedImage` (event ID 4000, level Error, message
  `User action RegisterObservedImage failed`) carrying the reload exception instance, after the entry for the
  registration exception: two entries in that order.
- AC3: After the failed reload the list still shows the rows it showed before the submit.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `ObservedImagesFailedReloadAfterFailedRegistrationKeepsPageUsableAsync` | `src/Tests/DockerUpdateGuard.Tests/ObservedImagesTests.cs` |
| AC2 | `ObservedImagesFailedReloadAfterFailedRegistrationIsLoggedAsync` | `src/Tests/DockerUpdateGuard.Tests/ObservedImagesTests.cs` |
| AC3 | `ObservedImagesFailedReloadAfterFailedRegistrationKeepsPageUsableAsync` | `src/Tests/DockerUpdateGuard.Tests/ObservedImagesTests.cs` |

Test setup for both: `GetManualObservedImagesAsync` returns one list item on the first call (the initial render) and
a faulted task with its own exception (`InvalidOperationException` with a host name and "refused" in its message,
distinct from the registration exception's) on the second; `RegisterAsync` faults with an `InvalidOperationException`.
AC1/AC3: `SubmitAsync` completes, `UserActionFailureAssert.AssertShown(markup, "Registration failed: unexpected error",
<texts of both exception messages>)`, the submit button has no `disabled` attribute, and the initial item's name is
still in the markup. AC2: `logger.Entries` has two entries, both event ID 4000, level Error and message
`User action RegisterObservedImage failed`, the first `AreSame` the registration exception, the second the reload
exception. A shared private static setup helper in the test class keeps the two tests free of duplicated arrangement.

## Approach

- In `ObservedImages.razor.cs`, replace the `await LoadAsync()` in `RegisterAsync`'s `catch` block by a call to a new
  private method `ReloadAfterFailedRegistrationAsync()` (`Task`, no parameters, region *Methods*, XML doc). It awaits
  `LoadAsync()` in a `try`; its `catch (Exception exception)` logs with `Logger.UserActionFailed("RegisterObservedImage",
  exception)` and does nothing else: `_errorMessage` keeps the registration's message, `_images` keeps its last value.
  Nothing is rethrown, so the `finally` of `RegisterAsync` still resets `_isBusy`.
- The reload after a failed registration stays (it shows an image whose registration succeeded but whose scan failed).
- No new user-visible output: the existing alert is unchanged.
- `LoadAsync`, `OnInitializedAsync` and `HandleRescannedAsync` stay as they are (fact 5).

Early draft PR: no

## Options considered

- Chosen: guard the reload in its own method and log its failure under the same action — the page keeps the message
  the area document promises and the reload failure is not lost.
- Remove the reload from the `catch` block: lost — a registration that succeeded with a failed scan would not appear
  in the list until the page is reloaded.
- Move the reload into `finally`: lost — it would run after a successful registration too (which navigates away) and
  needs the same guard anyway.
- Show a second message (`The list could not be refreshed`) when the reload fails: lost — the registration's message
  already reports the failure, the usual cause (database unreachable) is the same, and *Limits and errors* names one
  message per action.
- Log the reload failure under a new action name (`ReloadObservedImages`): lost — *Limits and errors* logs a later step
  of an action under that action's name (the reload after a save is logged as `SaveTagSelection`), and a new name would
  add a row to its table for a step, not a user action.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Components/Pages/ObservedImages.razor.cs` | Guarded reload in a new private method |
| DockerUpdateGuard.Tests | `ObservedImagesTests.cs` | Two new tests and a private setup helper |

## Signatures (for the Dev's skeleton)

None: the change adds only a private method, described in *Approach*; no API changes, so step 4 is skipped.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/ObservedImagesTests.cs` (existing class `ObservedImagesTests`).

Existing test code that calls a changed signature: none.

Files of the change no unit test can cover: none.

## Areas

- `docs/areas/web-ui.md (+35 words)`: in *Limits and errors*, the user-action bullet gains one sentence after "the
  reload after a save) can fail after the earlier step succeeded.": when the list reload after a failed registration
  fails as well, the registration's message stays, the list keeps what it showed, and the reload error is logged as a
  second `RegisterObservedImage` entry.

## Documentation updates

- `docs/areas/web-ui.md`: the sentence of *Areas*, by the Dev once the code is green. No other document states this:
  `docs/ARCHITECTURE.md:500` ("the pages never show one for a failed user action") still holds; no README,
  configuration, route or setting changes; the repository has no changelog.

## Architecture check

The page-error guarantee of `docs/ARCHITECTURE.md` (fixed reasons, never exception messages, decision record 0009) is
kept: the reload exception reaches only the log, through the existing `UserActionFailed` extension. No guarantee of
`.squad/project.md` changes.

## Security considerations

- [x] No limits.
- [x] Exceptions that can reach a user-visible message: the registration exception (`ImageRegistrationService.RegisterAsync`,
  `ImageScanOrchestrator.ScanAsync`), shown as `Registration failed: <FailureReason.FromException>` as today; the reload
  exception (`GetManualObservedImagesAsync`) reaches no message, only the log entry, as the registration exception
  already does.

## Decision records

- none: no decision beyond the obvious fix; record 0009 already covers fixed reasons for page actions.

## Out of scope / follow-ups

- A failed list load in `OnInitializedAsync` (database unreachable when the page opens) is a page load, not a user
  action, and falls under the unhandled-error handling of [Hosting](../../docs/areas/hosting.md); not changed here.

## Challenge notes

Plan challenge for issue #344 (/home/user/DockerUpdateGuard/specs/issue-344/plan.md). The plan holds up. I found two minor test-detail problems and nothing major.

I checked these claims in the code and they are correct:
- **The reported fault is real.** `RegisterAsync` calls `LoadAsync()` unguarded inside its `catch` (`src/DockerUpdateGuard/Components/Pages/ObservedImages.razor.cs:200-211`). `LoadAsync` assigns `_images` only after the query returns (`:156-162`), so a failed reload leaves the list unchanged.
- **No sibling code path has the same fault.**
  - Every other `catch (Exception …)` under `Components/` only logs and sets a message.
  - The reloads in `RuntimeContainerDetail.razor.cs` sit inside the `try`: `SaveSelectionAsync` (`:278-279`), `ClearSelectionAsync` (`:308-309`) and `ExecuteActionAsync` (`:445-447`).
  - `HandleRescannedAsync` (`:222-227`) runs inside `VulnerabilityRescanControl`'s `try`.
  - `MyImages`, `ObservedImageDetail` and `MyImageDetail` have no reload inside a `catch`.
- **The test helper does need replacing for AC2.** `UserActionFailureAssert.AssertLogged` asserts exactly one entry (`src/Tests/DockerUpdateGuard.Tests/Helper/UserActionFailureAssert.cs:48`), so the AC2 test has to check its two entries itself.
- **A rendered row works in the test.** `ObservedImagesPersistentStateTests.cs:76-87` renders one with only the page's services registered, so the planned setup with one list item on the first call is feasible.
- **The rejected options are rightly rejected.** Dropping the reload would hide an image whose scan failed. Moving it into `finally` would also run it on success, which navigates away.
- **The scope and the area document change are right.** The *Limits and errors* change matches `docs/areas/web-ui.md:165-172`, which logs a later step under the action's own name.

Objections:

1. **minor: the list-item name in AC3 could match by accident.** The AC3 check "the initial item's name is still in the markup" passes for the wrong reason if that name also appears elsewhere on the page. The values typed into the form are rendered back into the inputs' `value` attributes, and the exception texts are also on the page side. Instruction for the Tester: give the initial list item a unique name that is neither a typed form value (`Company API`, `docker.io/company/api:1.0.0`) nor part of either exception message, for example `existing-observed-image`. This is minor because it only changes the test fixture.

2. **minor: the forbidden-text check in AC1 cannot tell the two messages apart.** The plan gives the reload exception "a host name and 'refused'". The existing registration exception in `ObservedImagesTests.cs:87` is `"connection to db.internal.test:5432 refused"`. If both messages share the host and the word "refused", the check cannot show that the reload's own text stays off the page. Instruction for the Tester: give the reload exception a host of its own (for example `reload.db.internal.test`) and pass that host as a forbidden text, next to the registration exception's host. This is minor because it only strengthens an assertion and needs no change to the plan's approach.
