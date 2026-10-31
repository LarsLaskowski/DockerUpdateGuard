# Plan: Regression tests for scan batch isolation (F-024) and finding preservation (F-025)

Source: Issue #19
Status: Draft
Tier: trivial — the change adds tests and one option on a test double only; no production code, behavior or control flow changes, and the test diff is far beyond the `patch` line budget.
Security areas touched: none

## Problem / root cause

Issue #19 (review finding F-048) reports that the two scan-resilience defects F-024 and F-025 have no regression
test. Both defects are **already fixed on `main`**: F-025 by #129 (commit `25234b2`, issue #79 closed) and F-024 by
#134 (commit `1e96c1f`, issue #77 closed); both commits are ancestors of `origin/main` (its shallow history reaches
#299, which follows both). #129 and #134 added tests, so most of the gap the issue describes is closed. What is
still missing, read against the code:

1. **F-024, runtime orchestrator.** `RuntimeContainerScanOrchestrator.ScanAllAsync` catches a per-instance failure
   (`src/DockerUpdateGuard/Images/RuntimeContainerScanOrchestrator.cs:1240-1254`), but its only batch test,
   `RuntimeContainerScanOrchestratorScanAllAsyncContinuesAfterInstanceScanFailureAsync`
   (`src/Tests/DockerUpdateGuard.Tests/RuntimeContainerScanOrchestratorTests.cs:1922`), configures **one**
   instance. It proves the batch completes, not that a later instance is still scanned: a `break` in the catch
   would pass it.
2. **F-024, image orchestrator.** `ImageScanOrchestratorScanAllAsyncContinuesAfterItemScanFailureAsync`
   (`src/Tests/DockerUpdateGuard.Tests/ImageScanOrchestratorTests.cs:761`) has two images but makes **every**
   `SaveChangesAsync` fail, so it proves the second image was attempted, never that it is scanned to completion
   after the first one failed before the inner `try` (initial `ScanRun` save,
   `src/DockerUpdateGuard/Images/ImageScanOrchestrator.cs:719`).
3. **F-025, observed-image feed path.** #129 made the observed-image path keep its derived findings when the
   release feed fails (`ImageScanOrchestrator.cs:423-426` for .NET, `:472-475` for nginx; the delete follows at
   `:428` / `:477`). The configuration-refresh branch (`:351-354`) has a test
   (`ImageScanOrchestratorScanAsyncPreservesDerivedFindingWhenConfigurationRefreshFailsAsync`, `:1092`), the feed
   branch none: moving the delete above the feed check would go unnoticed.

The runtime discovery and registry cases of F-025 that the issue's recommendation (2) asks for already exist:
`RuntimeContainerScanOrchestratorScanAllAsyncPreservesFindingsWhenDiscoveryFailsAsync` (`:438`) and
`…PreservesFindingsWhenRegistryEvaluationFailsAsync` (`:551`) seed an active finding with a first scan and keep it
active through a failed second one.

The test double `SaveChangesFailingDbContext` (`src/Tests/DockerUpdateGuard.Tests/Data/SaveChangesFailingDbContext.cs`)
can fail every attempt or every attempt after the first N, but not a single early attempt; cases 1 and 2 need
that.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | Issue: "`ScanAllAsync` calls `ScanAsync` in a `foreach` without a per-item `catch`" (F-024). | `ImageScanOrchestrator.cs:655-671` and `RuntimeContainerScanOrchestrator.cs:1220-1255` wrap each item in `try`, rethrow `OperationCanceledException`, log other exceptions (event 2033 / 2042) and go on. Fixed by #134. | refuted |
| 2 | Issue: `SingleAsync` throws for an image deleted between listing and scan. | `ImageScanOrchestrator.cs:691-700` uses `SingleOrDefaultAsync` and logs event 2032; tested by `ImageScanOrchestratorScanAsyncWithMissingObservedImageSkipsWithoutThrowingAsync` (`ImageScanOrchestratorTests.cs:725`), fixed by #134. | refuted |
| 3 | Issue: findings are deleted/deactivated before any registry work, "per F-025 they do not" survive. | Observed images: delete only after a successful configuration read and feed read (`ImageScanOrchestrator.cs:351-354, 368, 423-428, 472-477`). Runtime: deactivation only per evaluated container (`RuntimeContainerScanOrchestrator.cs:668-672`) and for undiscovered containers only after a successful discovery (`:515-520`). Fixed by #129 (`git show 25234b2`). | refuted |
| 4 | Issue: "all batch tests use only one instance or skip the pre-`try` throw". | Runtime: the single batch-failure test has one instance (`RuntimeContainerScanOrchestratorTests.cs:1934-1945`). Image: two images, but `FailOnSaveChanges = true` fails every save (`ImageScanOrchestratorTests.cs:813`), so no image completes (problem items 1 and 2). | confirmed |
| 5 | Issue: no test seeds active findings and makes discovery/registry fail. | Tests at `RuntimeContainerScanOrchestratorTests.cs:438` and `:551`, `ImageScanOrchestratorTests.cs:1092` do exactly that. | refuted |
| 6 | No test covers the feed-failure branch of the observed-image derived finding. | `grep GetChannelReleaseAsync` in `ImageScanOrchestratorTests.cs`: every stub returns `Succeeded`. | confirmed |
| 7 | `docs/review/findings.md` (the issue's source of truth) is not on `main`; its branch `review/review-analysis-plan` is not on the remote. | `ls docs/review` fails; `git fetch origin review/review-analysis-plan`: "couldn't find remote ref". The issue bodies of #19, #77, #79 were read instead. | confirmed |
| 8 | Before the inner `try`, the only statement of `ScanInstanceAsync` that touches I/O is the initial `ScanRun` save (`RuntimeContainerScanOrchestrator.cs:468-471`); in `ScanAsync` it is the lock, the image query and the initial save (`ImageScanOrchestrator.cs:687-720`). So save attempt 1 of the orchestrator's context fails the first item before its `try`. | Code read; `InstanceDiscoveryService` saves through its own context in the existing test (`RuntimeContainerScanOrchestratorTests.cs:1948`), so its saves do not count. | confirmed |
| 9 | The batch order is not fixed (no `OrderBy` in either `ScanAllAsync` query, `ImageScanOrchestrator.cs:641-644`, `RuntimeContainerScanOrchestrator.cs:1205-1207`). | Code read. The tests below therefore assert on "the item that failed" and "the other one", never on which item comes first. | confirmed |
| 10 | After a failed `SaveChangesAsync`, the failed item's tracked entities (its `Running` `ScanRun`, removals, deactivations) stay in the shared context and are committed by the next item's save. | to verify: a scratch run of the AC1/AC2 tests that counts `ScanRun` rows with status `Running` after the batch, owner Tester, step 6 — reported only, not asserted; the follow-up issue below owns the fix. Basis so far: neither orchestrator clears the change tracker after a batch-item failure (`VulnerabilityEnrichmentService.cs:503-507` does). | unverified |

## Acceptance criteria

- [ ] AC1: When an observed-image batch has two enabled images and the first save of the scan fails (before the
  inner `try` of the first image scanned), the other image is still scanned to completion: exactly one batch-item
  failure (event 2033) is logged, the registry is asked for exactly one tag, and exactly one `ObservedImage` scan
  run ends `Succeeded` with a completion time. Guard: the per-item `catch` in `ImageScanOrchestrator.ScanAllAsync`
  (`ImageScanOrchestrator.cs:657-670`).
- [ ] AC2: When a runtime batch has two enabled, configured Docker instances and the first save of the scan fails
  (the initial `ScanRun` save of the first instance scanned), the other instance is still scanned: exactly one
  batch-item failure (event 2042) is logged, discovery runs exactly once, exactly one `RuntimeContainer` scan run
  ends `Succeeded` with a completion time, and the batch-completed event 2072 is logged. Guard: the per-instance
  `catch` in `RuntimeContainerScanOrchestrator.ScanAllAsync` (`RuntimeContainerScanOrchestrator.cs:1240-1254`).
- [ ] AC3: An active derived .NET base-runtime finding of an observed image, created by a first scan, stays the
  only such finding and stays active when a second scan reads the image configuration but the .NET release feed
  fails. Guard: the early return before the delete in
  `ImageScanOrchestrator.CreateDotNetDerivedBaseRuntimeFindingAsync` (`ImageScanOrchestrator.cs:423-428`).
- [ ] AC4: Each new test fails when its guard is removed: the Tester checks once in a scratch worktree, never
  committed — AC1: the per-item `try`/`catch` of the image `ScanAllAsync` removed; AC2: the runtime catch body
  ending in `break;`; AC3: the `DeleteSupersededObservedFindingsAsync` call at `:428` moved above the check at
  `:423` — and names the result per mutation in its report.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `ImageScanOrchestratorScanAllAsyncCompletesRemainingImageAfterEarlierImageFailsBeforeScanAsync` | `src/Tests/DockerUpdateGuard.Tests/ImageScanOrchestratorTests.cs` |
| AC2 | `RuntimeContainerScanOrchestratorScanAllAsyncScansRemainingInstanceAfterEarlierInstanceFailsBeforeScanAsync` | `src/Tests/DockerUpdateGuard.Tests/RuntimeContainerScanOrchestratorTests.cs` |
| AC3 | `ImageScanOrchestratorScanAsyncPreservesDerivedFindingWhenReleaseFeedFailsAsync` | `src/Tests/DockerUpdateGuard.Tests/ImageScanOrchestratorTests.cs` |
| AC4 | the three tests above, run once against each mutation (scratch worktree, step 6) | — |

These are regression tests for code that is already correct: they pass on the current code. Tier `trivial` skips
steps 4 and 5; the orchestrator launches the Tester in step 6 to write them (plan-named tests), and the Dev has
no edit.

## Approach

- **Test double.** `SaveChangesFailingDbContext` gets an option that fails exactly one attempt (see *Signatures*);
  its `SaveChangesAsync` throws when `SaveChangesAttemptCount == FailOnSaveAttempt`, besides the existing two
  conditions, which stay as they are.
- **AC1.** Seed two observed images on a plain context (as the existing test at `ImageScanOrchestratorTests.cs:761`
  does), build the orchestrator on `CreateSaveChangesFailingDbContext()` with `FailOnSaveAttempt = 1`. Stubs as in
  `ImageScanOrchestratorScanAsyncRemainsSucceededWhenNoBaseImageIsFoundAsync` (`:613`): `GetTagAsync` succeeds,
  `ResolveAsync` returns `Succeeded([])`, `GetImageConfigurationAsync` returns `NotFound`. Run `ScanAllAsync`, clear
  the tracker, then assert the counts of AC1; `GetTagAsync` is checked with `Received(1)`. No assertion on the
  total number of scan runs or on which image failed (Verified facts 9 and 10).
- **AC2.** As the existing test at `RuntimeContainerScanOrchestratorTests.cs:1922`, with two
  `DockerInstanceOptions` (distinct names and URLs, both enabled), `InstanceDiscoveryService` on its own context,
  `FailOnSaveAttempt = 1` instead of `FailOnSaveChanges`, and `DiscoverContainersAsync` returning `Succeeded([])`.
  Assert the counts of AC2; discovery is checked with `Received(1)`. Same restrictions as AC1.
- **AC3.** As the test at `ImageScanOrchestratorTests.cs:1092`, but `GetImageConfigurationAsync` always succeeds
  and `GetChannelReleaseAsync("9.0", …)` returns `Succeeded` (9.0.15) on the first call and `Failed` on the second.
  Two `ScanAsync` runs, tracker cleared, then: one finding of type `DerivedBaseRuntimeUpdate`, active.
- Existing tests stay unchanged: none of their inputs reaches the new option (it defaults to `null`).
- No user-visible output.

## Options considered

- **Write no test (`NO CHANGE`), since both defects are fixed and partly tested** — lost: problem items 1–3 are
  real gaps, item 1 is exactly the issue's recommendation (1).
- **Strengthen the two existing batch tests instead of adding new ones** — lost: they pin a different case
  (every item fails; cancellation is rethrown next to them) that stays worth keeping; changing their setup would
  drop it.
- **Also cover the nginx feed branch (`:472-475`)** — lost: it is the same guard shape as the .NET one; one test of
  the pattern is the regression guard the issue asks for, a second adds about 100 lines for no new path.
- **Fix the related defects below in this change** — lost: both change behavior (one is documented behavior), the
  issue is a test gap, and each needs its own decision; they become follow-up issues.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard.Tests | `Data/SaveChangesFailingDbContext.cs` | new property `FailOnSaveAttempt`, one extra condition in `SaveChangesAsync` |
| DockerUpdateGuard.Tests | `ImageScanOrchestratorTests.cs` | tests of AC1 and AC3 |
| DockerUpdateGuard.Tests | `RuntimeContainerScanOrchestratorTests.cs` | test of AC2 |

## Signatures (for the Dev's skeleton)

Test code only, written by the Tester (no skeleton on tier `trivial`), in the `Properties` region of
`SaveChangesFailingDbContext`:

```csharp
/// <summary>
/// 1-based number of the single persistence attempt that throws
/// </summary>
public int? FailOnSaveAttempt { get; set; }
```

## Test files

- `src/Tests/DockerUpdateGuard.Tests/ImageScanOrchestratorTests.cs`
- `src/Tests/DockerUpdateGuard.Tests/RuntimeContainerScanOrchestratorTests.cs`
- `src/Tests/DockerUpdateGuard.Tests/Data/SaveChangesFailingDbContext.cs` (test double)

Existing test code that calls a changed signature: none (a property is added, nothing changes).

Files of the change no unit test can cover: none.

## Areas

none — no change in behavior. `docs/areas/scan-engine.md` (*Failure isolation*, "the batch goes on with the next
one") and `docs/areas/base-images.md` ("when the configuration or the feed cannot be read they stay as they are")
already state what AC1–AC3 test.

## Documentation updates

none. Sweep: `docs/areas/scan-engine.md`, `docs/areas/base-images.md`, `docs/areas/update-detection.md`,
`docs/ARCHITECTURE.md` and `docs/UNIT_TESTS.md` were searched for batch isolation, finding deactivation and the
failing-context double; `docs/UNIT_TESTS.md` does not list `SaveChangesFailingDbContext`, nothing enumerates its
options.

## Architecture check

No guarantee of `docs/ARCHITECTURE.md` or `.squad/project.md` is touched; the tests pin the existing failure
isolation of the scan engine.

## Security considerations

- [x] no limits.
- [x] none: no new exception reaches a user-visible message.

## Decision records

- none: no decision beyond adding the missing tests.

## Out of scope / follow-ups

Two related defects found while planning; the orchestrator opens them as issues (they change behavior):

1. **Title:** "A failed batch item leaves its tracked changes in the shared scan context"
   **Body:** `ImageScanOrchestrator.ScanAllAsync` (`src/DockerUpdateGuard/Images/ImageScanOrchestrator.cs:655-671`)
   and `RuntimeContainerScanOrchestrator.ScanAllAsync` (`RuntimeContainerScanOrchestrator.cs:1220-1255`) catch a
   per-item failure but do not clear the change tracker of the shared `DbContext`. A failure before or at a
   `SaveChangesAsync` leaves that item's entities tracked — its `Running` `ScanRun`, removed or deactivated
   findings — and the next item's save commits them: the failed item gets a scan run that stays `Running` until the
   cleanup marks it failed, and a failure caused by the item's own entities (a constraint violation) repeats on
   every later item, starving the batch again as in F-024. `VulnerabilityEnrichmentService.cs:503-507` clears the
   tracker and saves the scan run alone after a failed save. Expected: a failed item's changes never reach a later
   item's save, and its scan run ends `Failed`. Found while planning #19 (unverified by measurement there; the
   test of #19 AC1/AC2 can count the `Running` runs).
2. **Title:** "Runtime derived base-runtime findings disappear when the release feed or image inspect fails"
   **Body:** For an evaluated container, `RuntimeContainerScanOrchestrator.ProcessContainerAsync`
   (`RuntimeContainerScanOrchestrator.cs:668-688`) deactivates all active findings of the container, the *derived
   base runtime update* included, and then recreates the derived one only if image inspect, the detector and the
   .NET/nginx release feed all succeed (`:954-1028`, `:1062-1070`). A transient feed or Docker inspect failure
   therefore removes the runtime alert until the next good scan — the flicker F-025 fixed for observed images,
   whose findings stay when the feed fails (`ImageScanOrchestrator.cs:423-428`; `docs/areas/base-images.md`).
   `docs/areas/update-detection.md` (*Update findings and tag candidates*) documents the current deactivation, so
   this is a product decision: keep the previous derived finding of a container when its runtime check could not
   be completed, as for observed images, or document the asymmetry.
