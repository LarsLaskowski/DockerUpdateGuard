# Plan: A cancelled vulnerability refresh saves the cancelled image version's results with its final save

Source: Issue #486
Tier: standard — a behavior change of what an aborted vulnerability refresh stores, with a decision record, and no security control.
Security areas touched: none

## Problem / root cause

`ApplyAndSaveImageVulnerabilitiesAsync` handles only errors that are not a cancellation
(`src/DockerUpdateGuard/Images/VulnerabilityEnrichmentService.cs:443`, `when (cancellationToken.IsCancellationRequested == false)`):
a cancelled save at line 438 leaves the method with the image version's applied changes still tracked (new findings
*added*, reactivated, updated and stale-resolved findings and the image version's assessment *modified*). The run's
error handler (lines 625–632) only sets the status and message, and the final save at line 640 runs with
`CancellationToken.None`, so it commits those changes with the failed run — after the image version's guard was
released at line 588.

The same handler leaves two more aborted steps pending for the final save (related defects, same cause):

- a cancelled save of the stale-findings pass (line 613): the deactivations are stored with the failed run;
- a failing save of the *failed* assessment (line 462, decision 0028's "second failure"), cancelled or not: the final
  save stores the *failed* assessment after all, or fails on it again and reaches the retry (lines 643–657).

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | A cancellation while saving one image version's results is let through without discarding the pending changes (issue) | `VulnerabilityEnrichmentService.cs:438`, `:443` filter; `DiscardPendingChanges` (`:452`) runs only after the filter matched | confirmed |
| 2 | The final save in `RunRefreshAsync` uses `CancellationToken.None` and stores the pending changes with the failed run (issue) | `:625` catches every exception including `OperationCanceledException`; `:634`–`:641` set the run and save with `CancellationToken.None`; nothing between discards tracked entries; read, and AC1's test reproduces it | confirmed |
| 3 | A failed `SaveChangesAsync` leaves the entries in their states | EF Core accepts changes only after a successful save; `SaveChangesFailingDbContext.SaveChangesAsync` throws before `base` (`src/Tests/DockerUpdateGuard.Tests/Data/SaveChangesFailingDbContext.cs:63`–`75`); the #485 tests rely on it | confirmed |
| 4 | The image version's guard is released before the final save | `finally` at `:586`–`:589` | confirmed |
| 5 | A cancelled stale-findings save leaves the deactivations pending | `:606`–`:614`: the findings are changed by `DeactivateStaleFindingsAsync` before the save that takes the token | confirmed |
| 6 | A failing save of the *failed* assessment leaves that assessment pending | `:454`–`:463`, no handler around the save | confirmed |
| 7 | Every other abort point leaves nothing pending: the scan-lock wait (`:573`), the provider calls (`:566`), the findings query (`:251`), the stale query (`:485`) | each throws before any entity is changed, and every earlier step saved | confirmed |
| 8 | An added finding left in a tracked image version's or the run's collection is added again by the next `DetectChanges` | EF Core change detection follows navigations of tracked entities; this is why `DiscardPendingChanges` removes added findings from both collections (`:394`–`:398`) | confirmed |
| 9 | Every caller runs the service in a scope of its own | `ServiceCollectionExtensions.cs:139` (scoped); `VulnerabilityRefreshBackgroundService.cs:80`, `ScanHistory.razor.cs:201`, `VulnerabilityRescanControl.razor.cs:83` create a scope | confirmed |
| 10 | The two #485 tests reach the final save's retry only because the cancelled image version's changes are still pending | `VulnerabilityEnrichmentServiceTests.cs:3084`–`3094`, `:3150`–`:3163`: their `FailWhen` fails a save while an added or modified finding is tracked | confirmed |
| 11 | Decisions 0028 and 0040 are unreleased | `git tag --contains` on their adding commits (`06fae55`, `5bb36c4`) prints nothing | confirmed |

## Acceptance criteria

- AC1: When the save of an image version's results is cancelled, the refresh stores none of that image version's
  changes: no new finding, existing findings (active and resolved) as stored before, and its assessment as stored
  before. The run is stored *failed* with `Vulnerability refresh failed: cancelled`, by the final save's first attempt
  (the final-save failure, event 2053, is not logged).
- AC2: When the save of a full refresh's stale-findings pass is cancelled, the stale findings stay active as stored
  before; the run is stored *failed* with `Vulnerability refresh failed: cancelled`.
- AC3: When saving an image version's *failed* assessment fails as well (the error is not a cancellation), the image
  version keeps the assessment stored before the refresh, and the run is stored *failed* with a message starting
  `Vulnerability refresh failed: ` by the final save's first attempt (event 2053 is not logged).
- AC4: The final save's retry still stores the run alone (#485): its two tests, adapted so that the first attempt of the
  final save fails, pass with their assertions unchanged.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `VulnerabilityEnrichmentServiceRefreshAsyncCancelledImageSaveDoesNotStoreImageResultsAsync` | `src/Tests/DockerUpdateGuard.Tests/VulnerabilityEnrichmentServiceTests.cs` |
| AC2 | `VulnerabilityEnrichmentServiceRefreshAsyncCancelledStaleFindingSaveKeepsStaleFindingsActiveAsync` | `src/Tests/DockerUpdateGuard.Tests/VulnerabilityEnrichmentServiceTests.cs` |
| AC3 | `VulnerabilityEnrichmentServiceRefreshAsyncFailedAssessmentSaveFailureKeepsPreviousAssessmentAsync` | `src/Tests/DockerUpdateGuard.Tests/VulnerabilityEnrichmentServiceTests.cs` |
| AC4 | `VulnerabilityEnrichmentServiceRefreshAsyncFinalSaveRetryDoesNotStoreUninsertedFindingAsync`, `VulnerabilityEnrichmentServiceRefreshAsyncFinalSaveRetryDiscardsReactivatedFindingAsync` (existing, adapted) | `src/Tests/DockerUpdateGuard.Tests/VulnerabilityEnrichmentServiceTests.cs` |

Cases, each on `SaveChangesFailingDbContext` over `SqliteTestDatabase`, its `FailWhen` failing a save **once** so that on
the unfixed code the final save succeeds and stores the pending changes (that is the failure each new test shows):

- AC1: one live image version seeded with an active finding `OLD` and a resolved finding `GONE`; the provider reports
  `NEW` and `GONE` (one added, one reactivated, `OLD` stale-resolved, assessment changed). `FailWhen` cancels the token
  and throws `OperationCanceledException` the first time an added `VulnerabilityFinding` is tracked. Assert: no `NEW`;
  `OLD` active with `ResolvedByScanRunId` null; `GONE` inactive with its stored `ResolvedAtUtc` and `ScanRunId`; the
  image version's assessment status, message and scan run id as read before; run status, `CompletedAtUtc`, message; no
  log entry 2053; `RefreshAsync` throws `OperationCanceledException` (as today, from the metrics refresh after the final
  save).
- AC2: one live image version without findings, its provider result reporting none, and one image version that is not
  live with an active finding. `FailWhen` cancels and throws the first time a modified `VulnerabilityFinding` with
  `IsActive == false` is tracked. Assert: the stale finding active, `ResolvedAtUtc` and `ResolvedByScanRunId` null; run
  status and message.
- AC3: one live image version with a stored assessment; `FailWhen` throws `InvalidOperationException` on the first two
  saves in which that `ImageVersion` entry is modified (the results save, then the *failed*-assessment save). Assert:
  assessment status, message and scan run id as read before; run *failed*, message starts with
  `Vulnerability refresh failed: `; no log entry 2053.
- AC4 (Tester): in both #485 tests, `FailWhen` also fails once the first save in which the run's entry is modified with
  status *failed* (the final save's first attempt), so the retry still runs; their assertions stay.

## Approach

1. Generalize the private `DiscardPendingChanges` so it serves both callers: it takes the run and the image versions
   whose collections may hold an added finding (`ScanRun scanRun, IEnumerable<ImageVersion> imageVersions`), and for an
   added `VulnerabilityFinding` removes it from the run's collection and from the collection of the image version whose
   `Id` equals the finding's `ImageVersionId` (no reliance on the finding's reference navigation, so it cannot throw in an
   error handler). The rest is unchanged. `ApplyAndSaveImageVulnerabilitiesAsync` passes `[image]`.
2. In `RunRefreshAsync`'s error handler (line 625), call `DiscardPendingChanges(scanRun, images)` first, before the
   status is set and before logging. The run is *unchanged* at that point (saved at line 537, not touched in the loop),
   so the discard does not reset it; its final values are set after the handler, as today.
3. Leave the per-image filter (line 443), the final save and its retry (lines 638–658) unchanged. The retry stays for a
   failure of the run's own save.

No new user-visible text; messages and event ids are unchanged.

## Options considered

Recorded with the reasons in [0041](../../docs/decisions/0041-an-aborted-vulnerability-refresh-discards-its-unsaved-changes-before-the-runs-final-save.md):
discard always in the run's error handler (chosen); discard only on cancellation (the outcome after an error would
depend on whether it repeats, through a second failing save and a warning); discard in the per-image filter before
rethrowing (misses the stale-findings save and the failed recovery save); clear the tracker and attach the run by its
entry state (detaches the loaded image versions, needs no new mechanism the discard does not already provide); keep
storing the changes (written outside the guard, by a run that reports a cancellation).

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Images/VulnerabilityEnrichmentService.cs` | `DiscardPendingChanges` generalized; called in `RunRefreshAsync`'s error handler |
| DockerUpdateGuard.Tests | `VulnerabilityEnrichmentServiceTests.cs` | three new tests, two adapted |

## Signatures (for the Dev's skeleton)

None: the change touches one private method and one error handler; no API member is added or changed, and no skeleton
file is needed.

## Test files

`src/Tests/DockerUpdateGuard.Tests/VulnerabilityEnrichmentServiceTests.cs` (the class's existing file).

Existing test code that calls a changed signature: none. Existing tests whose input the change handles differently:
the two #485 tests (Verified fact 10), adapted by the Tester as AC4 says. The cancellation test
`VulnerabilityEnrichmentServiceRefreshAsyncCancellationWhileApplyingImageIsNotIsolatedAsync` (line 2692) and
`VulnerabilityEnrichmentServiceRefreshImageVersionAsyncWaitingRefreshCancelledDoesNotApplyResultsAsync` (line 2891)
cancel before any entity changes (Verified fact 7) and stay unchanged; so do the per-image save-failure tests, whose
recovery save succeeds, and `VulnerabilityEnrichmentServiceRefreshAsyncTerminalSaveFailureIsLoggedAndRetriedAsync`,
whose failure hits the run's own save.

Files of the change no unit test can cover: none.

## Areas

Vulnerabilities: *Limits and errors* — what an aborted refresh stores.

## Documentation updates

Owned by the Dev, written against the green code:

- `docs/areas/vulnerabilities.md`, *Limits and errors*, the bullet on unexpected errors (lines 206–209): after "work
  saved before stays saved", state that the changes of the step that was aborted — by an error or a cancellation — are
  discarded before the run's final state is saved, linking decision 0041; the retry sentence stays.
- Same section, the per-image bullet (lines 215–221): "If saving the *Failed* assessment fails as well" gains that the
  image version then keeps its previous assessment; the cancellation sentence gains that the cancelled image version's
  results are not stored.
- `docs/areas/scan-engine.md` line 212–213 ("A vulnerability refresh retries its final save once …"): unchanged, still
  true; no edit. `docs/ARCHITECTURE.md`: no statement on this flow (sweep for "final save", "discard", "cancel"); no
  edit. No changelog file exists.

## Architecture check

No guarantee of `docs/ARCHITECTURE.md` or `.squad/project.md` changes: scan runs still end with a terminal status, the
per-image guard (0038) is strengthened, as no result is written outside it any more. No flow or component changes.

## Security considerations

- [x] No limits.
- [x] Exceptions reaching a user-visible message: unchanged — the run's message is built by `FailureReason.FromException`
  (fixed reasons, decision 0009) as today.

## Decision records

- `docs/decisions/0041-an-aborted-vulnerability-refresh-discards-its-unsaved-changes-before-the-runs-final-save.md`
  (Proposed), index row added.

## Out of scope / follow-ups

- `RunRefreshAsync` refreshes the inventory metrics with the cancelled token after the final save (line 660), which is
  what makes a cancelled refresh throw; telemetry and the completion log are skipped then. Existing behavior the tests
  rely on; not changed here.

## Challenge notes

I found no major objections and three minor ones. The plan's root cause is correct and the approach is sound.

**What I checked against the code** (`/home/user/DockerUpdateGuard/src/DockerUpdateGuard/Images/VulnerabilityEnrichmentService.cs`):
- **The defect:** the filter at :443 lets a cancellation through, and `DiscardPendingChanges` at :452 is only reached after a non-cancellation error. The run's handler at :625–632 discards nothing, and the final save at :640 runs with `CancellationToken.None`. The image version's guard is released at :586–589, before that save. Facts 1, 2 and 4 hold.
- **The two related paths:** the stale-findings changes made at :497–502 are still pending when the save at :613 is cancelled, and the save of the *failed* assessment at :462 has no handler. Facts 5 and 6 hold.
- **The run is unchanged in the handler:** it was saved at :537 and the loop never modifies it. A finding added to its collection does not mark it modified, so step 2 can discard first without resetting the run.
- **No sibling with the same defect:** no other file in `src/DockerUpdateGuard` saves with `CancellationToken.None`. `ScanRunSaver.cs:38–66` passes the scan's token and does not catch a cancellation, so a cancelled observed-image or runtime scan never reaches a final save.
- **The tests can be built as planned:** `SaveChangesFailingDbContext.cs:63–75` throws before calling `base`. The two #485 tests (`VulnerabilityEnrichmentServiceTests.cs:3083–3094` and `:3149–3163`) reach the retry only because entries are still pending, so the AC4 adaptation is needed and works. The AC1–AC3 tests would fail on the unfixed code as the plan says.
- **Existing tests are not affected:** I read `:523` (the terminal save failure test, still retried), `:1128`, `:2588`, `:2664` (their recovery save succeeds) and `:2692` (no pending entity).
- **Other claims:** event 2053 is `VulnerabilitySaveFailed` (`ImageHostLoggingExtensions.cs:307`). The cancelled refresh throws from the real `ApplicationTelemetry.RefreshInventoryMetricsAsync` at :660, as AC1 assumes. The flow is the same for `RefreshImageVersionAsync`, since it goes through `RunRefreshAsync` (:717).
- **Need and alternatives:** a change is warranted. Discarding fits decision 0028 ("cancellation stays a cancellation") and the per-image guard of 0038. 0041 weighs the alternatives fairly.

**Minor objections:**

1. **Decision 0041's wording about the failed assessment is wrong.** The second Consequences bullet says the image version's failure "is visible only in the run's message". In the AC3 case the run's message is only the generic `Vulnerability refresh failed: <reason>` (:629). The per-image message is never added to the run, because :465 is not reached. The image reference appears only in the per-image warning (event 2055), which is logged at :460 before the failing save. The Lead should reword the bullet: the run's message reports a generic failure, and the warning names the image version.
   - Why minor: wording in the record; the behavior is unchanged.

2. **The retry sentence in the area document will mislead.** In `docs/areas/vulnerabilities.md` (lines 207–209), "the refresh's other pending changes are discarded" would wrongly suggest that the retry is what drops an aborted step's changes. After the fix, the error handler has already discarded them and the retry only re-saves the run. The Dev should reword it to say the retry saves the run alone with the status and message it already has, and keep the link to decision 0040.
   - Why minor: a documentation sentence the Dev can fix without a plan change.

3. **The two adapted #485 tests change meaning.** Once AC4 adapts them, nothing is pending when the retry runs. Their assertions that the finding and assessment are unchanged are then guaranteed by the new discard, not by the retry. The Tester should:
   - update their XML summaries to say they check that the retry stores the run alone after a failed first attempt of the final save;
   - add `[TestCategory("AC4")]` next to their existing #485 ids (`docs/UNIT_TESTS.md`, *Criterion ids*).
   - Why minor: the tests still pass and still exercise the retry; only their description and tags are out of date.

**Files:**
- /home/user/DockerUpdateGuard/specs/issue-486/plan.md
- /home/user/DockerUpdateGuard/docs/decisions/0041-an-aborted-vulnerability-refresh-discards-its-unsaved-changes-before-the-runs-final-save.md
- /home/user/DockerUpdateGuard/src/DockerUpdateGuard/Images/VulnerabilityEnrichmentService.cs
- /home/user/DockerUpdateGuard/src/Tests/DockerUpdateGuard.Tests/VulnerabilityEnrichmentServiceTests.cs
- /home/user/DockerUpdateGuard/docs/areas/vulnerabilities.md
