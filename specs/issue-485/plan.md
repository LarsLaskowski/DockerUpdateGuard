# Plan: The vulnerability refresh's final-save retry stores the run alone

Source: Issue #485
Tier: standard — a one-line behavior change in production code (which rows the retry of the final save writes), no security control.
Security areas touched: none

## Problem / root cause

`VulnerabilityEnrichmentService.RunRefreshAsync` retries a failed final save after `_dbContext.ChangeTracker.Clear()`
with `_dbContext.ScanRuns.Update(scanRun)` (`src/DockerUpdateGuard/Images/VulnerabilityEnrichmentService.cs:647`).
`Clear()` detaches the entities but leaves the run's navigation collections as they are; `Update` walks the graph
from the run: every finding that change-tracking fix-up put into `scanRun.VulnerabilityFindings`, each finding's
`ImageVersion`, that image version's other navigations and so on. Every entity with a key set comes back as
*modified* with its in-memory values. So the retry (a) writes again the pending changes the clear was meant to drop
(a reactivated finding, an image version's new assessment), and (b) issues an `UPDATE` for a finding that was added
but never inserted, which affects no row, fails the retry with a concurrency error and leaves the run *running*
until the stale-run repair.

The pending changes reach the final save when an image version's save is cancelled
(`ApplyAndSaveImageVulnerabilitiesAsync`, lines 443/452: the handler lets a cancellation through without
`DiscardPendingChanges`), when the save of an image version's *failed* assessment fails (line 462), or when the
stale-findings save fails (line 613); the final save then fails on the same changes or a database error.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | The retry calls `ScanRuns.Update(scanRun)` after `ChangeTracker.Clear()` and saves with `CancellationToken.None` (issue) | `VulnerabilityEnrichmentService.cs:643-657` | confirmed |
| 2 | A finding the refresh adds is put into `scanRun.VulnerabilityFindings` by fix-up | FK `ScanRunId` with navigation `ScanRun`/`VulnerabilityFindings` (`VulnerabilityFindingConfiguration.cs:86-89`); new findings carry `ScanRunId = scanRun.Id` (`VulnerabilityEnrichmentService.cs:283`) while the run is tracked; `DiscardPendingChanges` removes added findings from that collection for this reason (line 397) | confirmed |
| 3 | A reactivated finding joins `scanRun.VulnerabilityFindings` once changes are detected | `ReactivateFinding` sets `ScanRunId = scanRun.Id` (line 207); fix-up runs in `DetectChanges`, which a real `SaveChanges` and `ChangeTracker.Entries()` call; the end-to-end effect is row 7 | confirmed |
| 4 | `Update` marks a finding *modified*, not *added*, even when it was never inserted | `VulnerabilityFinding.Id` is initialized with `Guid.NewGuid()` (`VulnerabilityFinding.cs:52`); `Update` tracks an entity whose generated key is set as *modified* | confirmed |
| 5 | The run is always stored before the final save, so *modified* is the right state for the retry | first save at line 537 is outside the `try`; its failure ends the method before the final save | confirmed |
| 6 | `Entry(scanRun).State = EntityState.Modified` attaches the run alone | `ScanRunSaver.cs:53` (#483); 0040's runtime test showed that no child row of the run is saved | confirmed |
| 7 | The suspicion of the issue reproduces: on today's code the retry stores the pending finding and assessment (AC2) and fails on a never-inserted finding, leaving the run *running* (AC1) | to verify: the AC1 and AC2 tests fail on the unfixed head, Tester, step 5 | unverified |
| 8 | Every caller resolves the service from its own scope, so nothing tracked survives a failed retry into another refresh | `ScanHistory.razor.cs:201`, `VulnerabilityRescanControl.razor.cs:83`, `VulnerabilityRefreshBackgroundService.cs:80` create a scope | confirmed |
| 9 | A cancelled refresh ends with `Vulnerability refresh failed: cancelled` and `RefreshAsync` throws `OperationCanceledException` | `FailureReason.cs:58-61`; existing test `VulnerabilityEnrichmentServiceRefreshAsyncCancellationWhileApplyingImageIsNotIsolatedAsync` | confirmed |

If row 7 is refuted (a test passes on the unfixed head), the Tester reports it and the Lead decides in mode `decide`
before the Dev changes anything.

## Acceptance criteria

- AC1: When an image version's save is cancelled after the refresh added a finding for it, and the run's final save
  then fails once, the retry stores the run with status `Failed`, a completion time and a message containing
  `Vulnerability refresh failed: cancelled`; the added finding is not stored.
- AC2: When an image version's save is cancelled after the refresh reactivated a resolved finding of it, and the run's
  final save then fails once, the retry stores the run as in AC1 and nothing else: the finding stays resolved as
  stored before the refresh, and the image version keeps the assessment status and message stored before the refresh.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `VulnerabilityEnrichmentServiceRefreshAsyncFinalSaveRetryDoesNotStoreUninsertedFindingAsync` | `src/Tests/DockerUpdateGuard.Tests/VulnerabilityEnrichmentServiceTests.cs` |
| AC2 | `VulnerabilityEnrichmentServiceRefreshAsyncFinalSaveRetryDiscardsReactivatedFindingAsync` | `src/Tests/DockerUpdateGuard.Tests/VulnerabilityEnrichmentServiceTests.cs` |

Setup for both (SQLite, `SaveChangesFailingDbContext`, one live image version, `CreateService` with parallelism 1):
`SaveChangesException = new OperationCanceledException()` and a `FailWhen` that inspects the tracker — never
`FailOnSaveAttempt` alone: `FailWhen`'s `Entries()` call runs `DetectChanges` as a real save does, and without it
the reactivated finding of AC2 never reaches the run's collection, which would hide the defect.

- AC1: provider reports advisory `NEW`; `FailWhen` returns true whenever a `VulnerabilityFinding` entry is *added*,
  cancelling the token source the first time. The image save and the final save fail; the retry (finding *modified*
  today, so the condition is false) runs against the database. Assert: `RefreshAsync` throws
  `OperationCanceledException` (fact 9); the stored run is `Failed` with `CompletedAtUtc` set and the message; no
  finding `NEW` is stored.
- AC2: seed with `withOldFinding: true`, then store the `OLD` finding resolved (`IsActive = false`,
  `ResolvedAtUtc` set) and read the image version's stored assessment; provider reports `OLD`/`pkg`. `FailWhen`
  returns true for the first two calls that see a *modified* `VulnerabilityFinding` entry (the image save, cancelling
  the token, and the final save), false after. Assert as AC1 for the run, plus: `OLD` is stored inactive with its
  `ResolvedAtUtc` and `ScanRunId` unchanged, and the image version's assessment status and message equal the values
  read before the refresh.

The existing `VulnerabilityEnrichmentServiceRefreshAsyncTerminalSaveFailureIsLoggedAndRetriedAsync` stays unchanged:
it seeds no image version, so the retry writes the run alone either way.

## Approach

Replace line 647, `_dbContext.ScanRuns.Update(scanRun);`, with `_dbContext.Entry(scanRun).State = EntityState.Modified;`,
as `ScanRunSaver` does. Nothing else in the handler changes: the run keeps the status and message computed before,
the retry keeps `CancellationToken.None` (0040, *Consequences*), and a failed retry is logged as today. No
user-visible output changes.

## Options considered

One option: [0040](../../docs/decisions/0040-a-scan-item-whose-save-fails-is-saved-again-alone-as-failed.md) already
weighed the ways to re-attach a run over a cleared tracker for the same purpose — `Update`/`Add` walk the child
collections (today's defect), a copied run with the same identifier was its fallback and proved unneeded — and setting
the entry state is the form it chose. Calling `ScanRunSaver.SaveAsync` instead is no fix of this defect but a
behavior change: it forces the status to `Failed`, rethrows, and saves with the scan's cancellation token, which 0040
keeps apart from this retry deliberately.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Images/VulnerabilityEnrichmentService.cs` | line 647 as in *Approach* |
| DockerUpdateGuard.Tests | `VulnerabilityEnrichmentServiceTests.cs` | two tests (AC1, AC2) |

## Signatures (for the Dev's skeleton)

None: no API changes, no skeleton.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/VulnerabilityEnrichmentServiceTests.cs`

Existing test code that calls a changed signature: none.

Files of the change no unit test can cover: none.

## Areas

- Vulnerabilities: the retry of the final save stores the run alone.
- Scan engine: the sentence on the vulnerability refresh's final-save retry.

## Documentation updates

All owned by the Dev, written once the code is green:

- `docs/areas/vulnerabilities.md`, *Limits and errors*, the bullet ending "If saving the final run state fails, it is
  retried once.": say that the retry saves the run alone with the status and message it already has, and that the
  refresh's other pending changes are discarded; link 0040.
- `docs/areas/scan-engine.md`, *Limits and errors*, the sentence "A vulnerability refresh retries its final save once
  on a fresh state before giving up.": say that the retry saves the run alone.

Sweep: `grep` for "retr", "final save" and "cleared" in `docs/areas/` and `docs/ARCHITECTURE.md` found only these
two places; `ARCHITECTURE.md` describes no save recovery. The PR template asks for no changelog.

## Architecture check

No guarantee of `docs/ARCHITECTURE.md` or `.squad/project.md` changes: the refresh still uses one scoped context and
keeps the run's lifecycle; the retry only stops writing rows other than the run.

## Security considerations

- [x] Every byte or length limit on parsed input says whether it applies before or after decoding — no limits.
- [x] Every exception that can reach a user-visible failure reason or message is listed — none: the retry's message
  is the one computed before (fixed reasons, `FailureReason`), and the retry's own exception is only logged.

## Decision records

- `docs/decisions/0040-a-scan-item-whose-save-fails-is-saved-again-alone-as-failed.md` (Accepted, unreleased: no
  `v*` tag contains it) — extended in place by the Lead with one consequence: the vulnerability refresh's retry
  attaches its run the same way. No new record: the choice is 0040's, applied to the remaining caller.

## Out of scope / follow-ups

- Related observation, no defect against the documented behavior: after a cancelled image-version save, the final
  save (`CancellationToken.None`) commits that image version's applied results along with the run, since the
  cancellation skips `DiscardPendingChanges`. The Vulnerabilities area says a cancellation ends the run *failed* and
  that earlier work stays saved; whether the cancelled image version's results should be dropped is a separate
  question — proposed issue below, not part of this change.
  - Title: "A cancelled vulnerability refresh saves the cancelled image version's results with its final save"
  - Body: "When an image version's save is cancelled, `ApplyAndSaveImageVulnerabilitiesAsync` lets the cancellation
    through without discarding the pending changes, and the final save in `RunRefreshAsync` (with
    `CancellationToken.None`) then stores that image version's findings and assessment with the failed run. Decide
    whether those changes should be discarded before the final save. Related: #485."
