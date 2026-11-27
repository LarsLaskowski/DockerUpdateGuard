# Plan: Scan-run cleanup keeps the 20 most recent runs as a minimum, not a cap

Source: Issue #290
Tier: standard — the removal rule of the cleanup pass changes behavior (which scan runs survive); no security control is created, changed or removed.
Security areas touched: none

## Problem / root cause

`ScanCleanupBackgroundService.LoadRemovableScanRunsAsync`
(`src/DockerUpdateGuard/Images/ScanCleanupBackgroundService.cs:146-176`) removes the **union** of the unreferenced
completed runs older than the cutoff (`oldScanRuns`, :165-167) and those not among the 20 most recently completed
unreferenced runs (`excessScanRuns`, :169-171, ids from :159-163). A run therefore survives only when it is both
within retention and among the newest 20: the constant `MaxRetainedScanRuns` (:21) is a cap. The product decision
(#290) makes it a minimum counted over all completed runs ([0064](../../docs/decisions/0064-the-twenty-most-recent-completed-scan-runs-are-a-retention-minimum.md)).

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | The removable set is the union of "older than cutoff" and "beyond the newest 20 unreferenced" | `ScanCleanupBackgroundService.cs:159-176` (the issue's line numbers predate the extraction into `LoadRemovableScanRunsAsync`; the logic is as the issue quotes it) | confirmed |
| 2 | The newest-20 set is counted over unreferenced runs only, not over all completed runs | :159 orders `completedUnreferencedScanRuns`, filtered at :151-157 | confirmed |
| 3 | The pass reads the time with `DateTimeOffset.UtcNow` (:285), the startup repair too (:270); the injected `TimeProvider` reaches only the base class, whose field is private | `ScanCleanupBackgroundService.cs:66-70`, `ScheduledBackgroundService.cs:20,37-42` | confirmed |
| 4 | The cutoff is always at least one day before the pass start, so `CompletedAtUtc < cutoff` implies the current `CompletedAtUtc <= cleanupStartedAtUtc` | `RetainScanRunsDays` range 1–3650, `DockerUpdateGuardOptionsValidator.cs:296-299` | confirmed |
| 5 | `List<Guid>.Contains(entity.Id)` inside a `Where` is translated server-side by the providers in use | the same pattern runs today at :169 and for `liveImageVersionIdList` (:310) against Npgsql in production and InMemory in the tests | confirmed |
| 6 | No test covers "a referenced run older than the cutoff is kept" (the issue's AC asks for an existing one) | `ScanCleanupBackgroundServiceTests.cs`: the only scan-run retention tests are :30 (cap) and :118 (running run) | confirmed |
| 7 | The reload harness hands the job `Substitute.For<TimeProvider>()`, whose unconfigured `GetUtcNow` returns `default(DateTimeOffset)`; its core pass would then throw at `AddDays(-RetainScanRunsDays)` (caught and logged by the base loop) once the pass reads the injected clock | `Helper/WaitRecordingTimeProvider.cs:60`, `ScanCleanupReloadHarness.cs:73,89`, `ScheduledBackgroundService.cs:173-208` | confirmed |
| 8 | Event 2020 logs the removed scan-run count as `… and {RemovedScanRuns} scan runs`, and `TestLogger` records the formatted message | `ImageHostLoggingExtensions.cs:119-128`, `Data/TestLogger{TCategoryName}.cs:54-70` | confirmed |
| 9 | The README describes `RetainScanRunsDays` at line 181 (the issue says :153) | `README.md:181` | confirmed |

## Acceptance criteria

Times below are relative to the pass start the injected clock returns; "old" means completed more than
`RetainScanRunsDays` (30) days before it; every run is completed and unreferenced unless stated.

- AC1: 25 runs all within retention → all 25 are kept.
- AC2: 25 old runs → exactly the 20 most recently completed are kept, the 5 oldest removed.
- AC3: 5 runs within retention and 30 old runs → the 5 recent and the 15 newest old runs are kept (20), the other
  15 removed, and the cleanup log entry (event 2020) reports `15 scan runs`.
- AC4: 21 old runs, the oldest of them referenced by an image relationship → all 21 are kept.
- AC5: 25 old runs, the 2 newest referenced by an image relationship → 20 remain: the 2 referenced and the next 18;
  the 5 oldest are removed (the minimum counts referenced runs).
- AC6: a `Running` run is never removed (unchanged).
- AC7: the README describes `RetainScanRunsDays` as "Retention period for completed scan history; the 20 most recent
  completed runs are always kept".
- AC8: `docs/areas/scan-engine.md`, *Retention and cleanup*, states the new rule, and its *Known gaps* entry for #290
  is removed with its heading.
- AC9: the *Coverage gate* passes; every assertion has a message.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `ScanCleanupBackgroundServiceExecuteCoreAsyncKeepsAllScanRunsWithinRetentionAsync` (replaces `…RemovesUnreferencedScanRunsBeyondLatestTwentyAsync`) | `src/Tests/DockerUpdateGuard.Tests/ScanCleanupBackgroundServiceTests.cs` |
| AC2 | `ScanCleanupBackgroundServiceExecuteCoreAsyncKeepsLatestTwentyScanRunsPastRetentionAsync` | same |
| AC3 | `ScanCleanupBackgroundServiceExecuteCoreAsyncKeepsMinimumOfTwentyScanRunsAsync` | same |
| AC4 | `ScanCleanupBackgroundServiceExecuteCoreAsyncKeepsReferencedScanRunPastRetentionAsync` | same |
| AC5 | `ScanCleanupBackgroundServiceExecuteCoreAsyncCountsReferencedScanRunsTowardMinimumAsync` | same |
| AC6 | `ScanCleanupBackgroundServiceExecuteCoreAsyncKeepsRunningScanRunsAsync` (exists, unchanged; tagged `AC6`) | same |
| AC7 | check: the Reviewer reads `README.md:181` in step 8 | — |
| AC8 | check: the Reviewer reads the area document in step 8 | — |
| AC9 | CI: *Coverage gate* in step 7; check: the Reviewer reads the assertions | — |

Each test asserts the exact set of remaining `CorrelationId`s (ordered), not only the count. AC1, AC2, AC4 and AC5
fail on today's code (AC4 passes today: it guards the reference rule against the rewrite).

## Approach

- **Rule.** In `LoadRemovableScanRunsAsync`:
  1. `retainedScanRunIds`: `dbContext.ScanRuns.Where(CompletedAtUtc != null).OrderByDescending(CompletedAtUtc)
     .Take(MinimumRetainedScanRuns).Select(Id).ToListAsync` — over **all** completed runs, referenced or not, at
     most 20 ids in memory.
  2. Return `dbContext.ScanRuns.Where(CompletedAtUtc != null && CompletedAtUtc < cutoff && <the four reference
     conditions as today> && retainedScanRunIds.Contains(Id) == false).ToListAsync` — one server-side query that
     loads only the removable runs. `oldScanRuns`, `excessScanRuns` and the `Concat`/`DistinctBy` go.
  3. The parameter `cleanupStartedAtUtc` is dropped (fact 4): the method takes `(dbContext, cutoff,
     cancellationToken)`; its summary reads "Load the completed scan runs that are removable: completed before the
     cutoff, not among the most recently completed ones and referred to by no data".
  Ties on `CompletedAtUtc` at the 20th place are left to the database's order; either choice keeps 20 runs.
- **Constant.** `MaxRetainedScanRuns` becomes `MinimumRetainedScanRuns` (value 20), summary "Number of most recently
  completed scan runs that cleanup always keeps, even past the retention period".
- **Clock** (`docs/UNIT_TESTS.md`, *Time in tests*: a changed service moves to `TimeProvider`). The service stores
  the constructor's `timeProvider` in its own `private readonly TimeProvider _timeProvider` (the base field is
  private); `ExecuteCoreAsync` and `ExecuteStartupAsync` read `_timeProvider.GetUtcNow()` instead of
  `DateTimeOffset.UtcNow`. No other behavior of the repair changes.
- **Tests.** The five new tests build the job with the existing four-argument `TestScanCleanupBackgroundService`
  constructor and an NSubstitute `TimeProvider` whose `GetUtcNow` returns one fixed instant; seeded `StartedAtUtc` /
  `CompletedAtUtc` are offsets from it ("old" = 31 days and more before it, distinct minutes per run). AC4/AC5
  references are `ImageRelationship` rows (`ScanRunId` set; two `ImageVersion`s through `ImageCatalogRepository`
  as in :528-540), because cleanup never removes relationships, so the reference still exists after the pass.
  New tests pass `TestContext.CancellationToken` (`.squad/stack.md`, *Writing tests*). A shared seeding helper for
  the runs is the Tester's choice; logic needed by several tests goes into one helper, not copies.
- The cleanup log line and its arguments are unchanged.

Early draft PR: no

## Options considered

1. **Minimum counted over all completed runs, removal only when old AND outside the 20 AND unreferenced** — chosen
   (Product Manager's decision in #290).
2. **Minimum counted over unreferenced runs only** (today's counting set) — rejected by the decision: the number of
   runs kept would depend on how many runs data refers to (record 0064, option 2).
3. **Materialize all completed unreferenced runs and filter in memory** — simpler LINQ, but loads every run each pass,
   worsening #81; rejected for the server-side `Where`.
4. **One query with the newest-20 ids as a subquery** (`IQueryable.Contains`) — saves one round trip of at most 20
   ids, but its translation with `Take` inside `NOT IN` is not exercised anywhere in the code base yet; the
   two-query form uses only patterns that run today (fact 5).
5. **Keep `DateTimeOffset.UtcNow`** (the issue leaves it open) — rejected: `docs/UNIT_TESTS.md` requires the
   injected clock for changed time-dependent service logic, and tests must not depend on the real clock.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Images/ScanCleanupBackgroundService.cs` | rule, constant rename, clock field |
| DockerUpdateGuard.Tests | `ScanCleanupBackgroundServiceTests.cs` | one test replaced, four added, `AC6` tag |
| DockerUpdateGuard.Tests | `Helper/WaitRecordingTimeProvider.cs` | configured `GetUtcNow` (fact 7) |

## Signatures (for the Dev's skeleton)

No public, protected or interface member changes. Private changes only: the constant rename, the new private field
`_timeProvider` (assigned in the existing constructor — the skeleton holds this for real), and the private static
`LoadRemovableScanRunsAsync` losing its `cleanupStartedAtUtc` parameter (three parameters). Step 4 has nothing to
stub; the Tester's tests run against today's rule and fail as stated under *Tests per criterion*.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/ScanCleanupBackgroundServiceTests.cs` (Tester).

Existing test code that calls a changed signature: none. Adapted by the Tester: `Helper/WaitRecordingTimeProvider.cs`
— its substitute gets `GetUtcNow` returning a fixed UTC instant, so the reload tests' core pass no longer runs on
`default(DateTimeOffset)`; `Helper/TestScanCleanupBackgroundService.cs` — the `timeProvider` parameter's doc
"Time provider used for the wait" becomes "Time provider". The existing tests keep the three-argument constructor
(`TimeProvider.System`); none has input the new rule treats differently (`…KeepsRunningScanRunsAsync`: 20 runs
within retention; the stale-repair tests: repaired runs complete at the pass start).

Files no unit test can cover: none.

## Areas

- `docs/areas/scan-engine.md` (2705 words by `wc`; plan-check measures; about +15 words, −40 for the removed gap):
  the *Scan runs* bullet of *Retention and cleanup* reads that a completed run nothing refers to is removed only when
  it completed before the cutoff and is not among the 20 most recently completed scan runs (counted over all
  completed runs, referenced or not), so the 20 most recent completed runs survive any pause; referenced and
  `Running` runs are kept as today. *Known gaps* (its only entry is #290) is removed with its heading. Add 0064 to
  *Related decisions*.

## Documentation updates

Written by the Dev once the code is green:

- `README.md:181` — the `RetainScanRunsDays` description as AC7 words it.
- `docs/areas/scan-engine.md` — as *Areas*.
- `src/DockerUpdateGuard/Configuration/ScanningOptions.cs:66` — summary "Number of days to retain completed scan
  history; the most recent completed runs are kept beyond it".
- Sweep result: `docs/ARCHITECTURE.md:380` ("purges history past retention") stays true; `docs/areas/web-ui.md:70`
  (scan history shows the 20 most recent runs) is unaffected and now always filled; `appsettings.json:49`,
  `README.md:289`, `INSTALL.md`, `DOCKER.md` carry only the value. No changelog exists; the PR template asks only for
  README, ARCHITECTURE and the area document. `.squad/project.md`, `docs/UNIT_TESTS.md`: no change.

## Architecture check

No guarantee of `.squad/project.md` or `docs/ARCHITECTURE.md` changes: removal stays in the pass's single
`SaveChangesAsync`, the reference rules, the other retention rules and the stale-run repair are untouched. The
cleanup keeps more data than before, which is what the documented retention promises.

## Security considerations

- [x] No limits on parsed input.
- [x] No exception reaches a user-visible message: a failing pass is logged by the base loop as today.

## Decision records

- `docs/decisions/0064-the-twenty-most-recent-completed-scan-runs-are-a-retention-minimum.md` (Proposed), indexed.

## Out of scope / follow-ups

- #81 (cleanup loads all rows of the other tables) stays open; this change only stops the scan-run part from
  loading more than the removable runs.