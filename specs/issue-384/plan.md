# Plan: A failed batch item leaves its tracked changes in the shared scan context

Source: Issue #384
Tier: standard — a behavior change of the scan engine's error path (what a failed save leaves behind and how its run ends) that creates, changes or removes no security control.
Security areas touched: Failure messages and remote text (a new scan-run error line built from `FailureReason`), Logging and telemetry of external data (two new `[LoggerMessage]` methods in `Images/ImageHostLoggingExtensions.cs` carrying the observed image or Docker instance name and the exception)

## Problem / root cause

Both orchestrators scan their items through one scoped `DockerUpdateGuardDbContext` and save each item's scan run
twice: `Running` before the work (`ImageScanOrchestrator.cs:665-668`, `RuntimeContainerScanOrchestrator.cs:505-508`),
everything else at the end (`ImageScanOrchestrator.cs:725-726`, `RuntimeContainerScanOrchestrator.cs:573-574`). The
inner error handlers (`ImageScanOrchestrator.cs:712`, `RuntimeContainerScanOrchestrator.cs:560`) do not cover these
saves, so a save error leaves the item through the batch handler (`ImageScanOrchestrator.cs:616-619`,
`RuntimeContainerScanOrchestrator.cs:1283-1286`), which logs it and never touches the change tracker. The item's
added run and every other pending change stay tracked, and the next item's first `SaveChangesAsync` sends them again:
a run that never got past its first save is committed as `Running` by another item, and a change the database rejects
fails every later item too. The same holds for `ScanAsync` called from the registration page
(`Components/Pages/ObservedImages.razor.cs:186`), whose context lives as long as the circuit.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | The batch handlers catch an item's error without clearing the tracker | `ImageScanOrchestrator.cs:604-620`, `RuntimeContainerScanOrchestrator.cs:1252-1286`; no `ChangeTracker` use in either file (grep) | confirmed |
| 2 | The issue's line numbers (`:655-671`, `:1220-1255`, `VulnerabilityEnrichmentService.cs:503-507`) | the code moved: batch loops at the lines of row 1, the enrichment's clear-and-retry at `VulnerabilityEnrichmentService.cs:642-656`; the behavior they point at is confirmed (row 1) | refuted |
| 3 | A failed first save leaves the `Added` run tracked and the next item's save stores it as `Running` (the issue's measurement: 2 runs, 1 `Running`) | `ScanRuns.Add` + save at the lines above; the existing tests with `FailOnSaveAttempt = 1` (`ImageScanOrchestratorTests.cs:896`, `RuntimeContainerScanOrchestratorTests.cs:2038`) count only `Succeeded` runs | confirmed |
| 4 | A failed final save leaves the run `Modified` and the item's other changes pending; a rejected change repeats on every later item | final saves at the lines above sit outside the inner `try`; one context per scope (`DockerUpdateGuard.Data/ServiceCollectionExtensions.cs:27`, `AddDbContext`); intermediate catalog saves (`ImageCatalogRepository.cs:147,209,235`) send all pending entries too | confirmed |
| 5 | Nothing the batches use later is tracked state: the image batch holds identifiers, the runtime batch reads only `Id`/`Name` of its `DockerInstance` entities | `ImageScanOrchestrator.cs:590-593`; grep of `dockerInstance` in `RuntimeContainerScanOrchestrator.cs` (only `.Id`, `.Name`); no entity caches in the orchestrators or `ImageCatalogRepository` | confirmed |
| 6 | `ScanRun.Id` is generated on the client, so `Update` of a never-stored run issues an `UPDATE` of a missing row | `DockerUpdateGuard.Data/Entities/ScanRun.cs:13` (`Guid.NewGuid()`) | confirmed |
| 7 | The run's child collections (`ContainerSnapshots`, `UpdateFindings`, `ImageRelationships`, `VulnerabilityFindings`) are filled by fix-up while tracked (`ScanRun = scanRun` at `RuntimeContainerScanOrchestrator.cs:713`, `ScanRunId = scanRun.Id` at `:649`, `:936`, `ImageScanOrchestrator.cs:386,435,574`); `Add`/`Update` walk them, setting `EntityEntry.State` attaches only the run | to verify: the Tester's runtime AC1 test (no container snapshot of the failed run is stored), steps 5/6 | unverified |
| 8 | `FailureReason.FromException` maps a `DbUpdateException` or `InvalidOperationException` to `unexpected error`, never the exception's text | `Infrastructure/FailureReason.cs:49-85` | confirmed |
| 9 | `ScanRun.ErrorMessage` is an unbounded `text` column | `ScanRunConfiguration.cs:36-37` | confirmed |
| 10 | `ApplicationTelemetry.RefreshInventoryMetricsAsync` only reads | `ApplicationTelemetry.cs` holds no `SaveChanges` | confirmed |
| 11 | Event ids 2034 and 2043 are free | event ids of `ImageHostLoggingExtensions.cs` (2030-2033, 2040-2042 used) | confirmed |
| 12 | The area document says a run unsaved at its end stays `Running` until the repair | `docs/areas/scan-engine.md:206-208` | confirmed |

## Acceptance criteria

- AC1: In a batch of either orchestrator, when an item's save fails because of that item's own pending change, none of
  the item's changes is stored by a later item's save (observed image: its image version's registry metadata stays as
  before; runtime: no container snapshot of its run is stored), and the later item's run ends `Succeeded`.
- AC2: When an item's first save fails, its run is stored once, with status `Failed`, a completion time and the error
  message `Observed image scan could not be saved: unexpected error` (runtime: `Runtime container scan could not be
  saved: unexpected error`); no run of the batch is left `Running`, the batch logs the item as failed (2033 / 2042), and
  `ImageScanOrchestrator.ScanAsync` throws the original exception.
- AC3: When an item's final save fails, its run (stored as `Running`) ends `Failed` with a completion time and the
  error message made of the distinct messages gathered so far, each on its own line, followed by the line of AC2.
- AC4: When the recovery save fails too, it is logged with event 2034 (observed image) or 2043 (Docker instance) at
  level `Error`, the original exception is rethrown, and nothing of the item stays tracked: a later scan on the same
  context stores no run or change of the failed item.
- AC5: An `OperationCanceledException` from a save is rethrown without a recovery save (one save attempt).

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `ImageScanOrchestratorScanAllAsyncDiscardsFailedImageChangesBeforeNextImageAsync` | `src/Tests/DockerUpdateGuard.Tests/ImageScanOrchestratorTests.cs` |
| AC1 | `RuntimeContainerScanOrchestratorScanAllAsyncDiscardsFailedInstanceChangesBeforeNextInstanceAsync` | `src/Tests/DockerUpdateGuard.Tests/RuntimeContainerScanOrchestratorTests.cs` |
| AC2 | `ImageScanOrchestratorScanAllAsyncCompletesRemainingImageAfterEarlierImageFailsBeforeScanAsync` (adapted), `ImageScanOrchestratorScanAsyncStoresFailedRunWhenFirstSaveFailsAsync` | `src/Tests/DockerUpdateGuard.Tests/ImageScanOrchestratorTests.cs` |
| AC2 | `RuntimeContainerScanOrchestratorScanAllAsyncScansRemainingInstanceAfterEarlierInstanceFailsBeforeScanAsync` (adapted) | `src/Tests/DockerUpdateGuard.Tests/RuntimeContainerScanOrchestratorTests.cs` |
| AC3 | `ImageScanOrchestratorScanAllAsyncDiscardsFailedImageChangesBeforeNextImageAsync` | `src/Tests/DockerUpdateGuard.Tests/ImageScanOrchestratorTests.cs` |
| AC3 | `RuntimeContainerScanOrchestratorScanAllAsyncDiscardsFailedInstanceChangesBeforeNextInstanceAsync` | `src/Tests/DockerUpdateGuard.Tests/RuntimeContainerScanOrchestratorTests.cs` |
| AC4 | `ImageScanOrchestratorScanAsyncLeavesNothingTrackedWhenFailedRunCannotBeSavedAsync`, `ImageScanOrchestratorScanAllAsyncContinuesAfterItemScanFailureAsync` (adapted) | `src/Tests/DockerUpdateGuard.Tests/ImageScanOrchestratorTests.cs` |
| AC4 | `RuntimeContainerScanOrchestratorScanAllAsyncContinuesAfterInstanceScanFailureAsync` (adapted) | `src/Tests/DockerUpdateGuard.Tests/RuntimeContainerScanOrchestratorTests.cs` |
| AC5 | `ImageScanOrchestratorScanAllAsyncRethrowsCancellationFromItemScanAsync` (adapted) | `src/Tests/DockerUpdateGuard.Tests/ImageScanOrchestratorTests.cs` |
| AC5 | `RuntimeContainerScanOrchestratorScanAllAsyncRethrowsCancellationFromInstanceScanAsync` (adapted) | `src/Tests/DockerUpdateGuard.Tests/RuntimeContainerScanOrchestratorTests.cs` |

Cases (SQLite, `SqliteTestDatabase.CreateSaveChangesFailingDbContext`):

- AC1/AC3 image: two observed images, registry metadata succeeds for both; the context fails every save while the
  first image's `ImageVersion` (by digest) is pending `Modified`. Assert: the first run `Failed` with the AC3 message,
  the first image version's `MetadataJson` still `null` in a fresh context, the second run `Succeeded`, no run `Running`.
  Without the fix the second image's first save fails as well.
- AC1/AC3 runtime: two instances; discovery returns one container for the first, none for the second; the context
  fails every save while a `ContainerSnapshot` of the first instance is pending `Added`. Assert: the first run `Failed`
  ending in the AC2 runtime line, no `ContainerSnapshot` stored, the second run `Succeeded`.
- AC2: the adapted tests (`FailOnSaveAttempt = 1`) also assert two runs, none `Running`, the first `Failed` with the
  message and a completion time; the new `ScanAsync` test asserts the thrown exception is the context's
  `SaveChangesException` and the stored `Failed` run.
- AC4: the context fails every save while an `Added` run of the first observed image is pending; `ScanAsync` of the
  first image throws the original exception and logs 2034, then `ScanAsync` of the second image on the same context
  succeeds and the database holds no run of the first image. The adapted `…ContinuesAfter…Failure…` tests (every save
  fails) also assert 2034 / 2043 per item.
- AC5: the adapted cancellation tests also assert `SaveChangesAttemptCount == 1`.

## Approach

In each orchestrator, a private method saves the run's pending changes and owns the recovery; `ScanAsync` and
`ScanLockedInstanceAsync` call it instead of their two `_dbContext.SaveChangesAsync(cancellationToken)` calls, so their
own control flow (and cognitive complexity) stays as it is. Its parameters: the run, whether the run is already stored
(`false` for the first save, `true` for the final one), the `statusMessages` list, the subject's name for the log, the
cancellation token (at most six with the exception, if the Dev splits the recovery into a second private method).

1. `await _dbContext.SaveChangesAsync(cancellationToken)`; on `catch (Exception exception) when (exception is not
   OperationCanceledException)` (the batch handlers' own rule for cancellation):
2. `_dbContext.ChangeTracker.Clear()`;
3. `statusMessages.Add($"Observed image scan could not be saved: {FailureReason.FromException(exception, null, cancellationToken)}")`
   (runtime: `Runtime container scan could not be saved: …`); `scanRun.Status = ScanRunStatus.Failed`,
   `CompletedAtUtc = DateTimeOffset.UtcNow`, `ErrorMessage = string.Join(Environment.NewLine, statusMessages.Distinct())`;
4. `_dbContext.Entry(scanRun).State = stored ? EntityState.Modified : EntityState.Added` — never `Update`, `Add` or
   `Attach`, which walk the run's child collections (fact 7);
5. `SaveChangesAsync(CancellationToken.None)` as the enrichment does; on any exception log it with the new event
   (2034 / 2043) and clear the tracker again;
6. `throw;` the original exception.

The batch handlers (`catch (Exception exception)` of both `ScanAllAsync`) also call `_dbContext.ChangeTracker.Clear()`
before logging, so any failure of an item leaves nothing for the next one. No metric is recorded for a run saved by the
recovery. No new user-visible layout: the line joins the existing run message.

## Options considered

1. **Recovery save of the run alone inside each item, plus a clear in the batch handler (chosen)** — meets both halves
   of the issue, also for the registration page's direct `ScanAsync`.
2. **Only a clear in the batch handlers** — a run whose final save failed stays `Running`, one whose first save failed
   leaves no trace, and the direct `ScanAsync` keeps the defect.
3. **Entry-by-entry discard as in 0028** — needed there because later work is tracked; here nothing is (fact 5).
4. **Re-attach the run with `Update`/`Add`, as the enrichment does** — would save the item's child rows after all or
   fail on rows never inserted (facts 6, 7).
5. **A scope per item** — a composition-root change for a failure the clear handles.

Details in the record.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Images/ImageScanOrchestrator.cs` | private save-and-recover method; both saves of `ScanAsync` use it; batch handler clears the tracker |
| DockerUpdateGuard | `Images/RuntimeContainerScanOrchestrator.cs` | the same for `ScanLockedInstanceAsync` and `ScanAllAsync` |
| DockerUpdateGuard | `Images/ImageHostLoggingExtensions.cs` | two log messages |
| DockerUpdateGuard.Tests | `Data/SaveChangesFailingDbContext.cs` | a condition on the tracked entries under which every save fails (Tester) |

## Signatures (for the Dev's skeleton)

In `ImageHostLoggingExtensions` (same visibility and region as its neighbours):

- `[LoggerMessage(EventId = 2034, Level = LogLevel.Error, Message = "Could not save the failed scan run of observed image {ObservedImage}")] public static partial void ObservedImageScanRunSaveFailed(this ILogger logger, Exception exception, string observedImage);`
- `[LoggerMessage(EventId = 2043, Level = LogLevel.Error, Message = "Could not save the failed scan run of Docker instance {DockerInstance}")] public static partial void RuntimeContainerScanRunSaveFailed(this ILogger logger, Exception exception, string dockerInstance);`

No other new or changed API member; the orchestrators' new methods are private, and the test helper below is the
Tester's.

## Test files

`src/Tests/DockerUpdateGuard.Tests/ImageScanOrchestratorTests.cs`,
`src/Tests/DockerUpdateGuard.Tests/RuntimeContainerScanOrchestratorTests.cs`,
`src/Tests/DockerUpdateGuard.Tests/Data/SaveChangesFailingDbContext.cs`.

The Tester adds to `SaveChangesFailingDbContext` a nullable property `FailWhen`, a function from the context's
`ChangeTracker` to `bool`: a save throws (`SaveChangesException`, else the `InvalidOperationException` of today) while
it returns true.

Existing tests whose inputs the change handles differently, adapted by the Tester (rows above): the two
`…FailsBeforeScanAsync` tests (a `Failed` run is now stored), the two `…ContinuesAfter…FailureAsync` tests (a recovery
save is attempted and logged), the two `…RethrowsCancellation…` tests (pinned to one attempt). No changed signature
affects test call sites. Other users of `SaveChangesFailingDbContext` (`VulnerabilityEnrichmentServiceTests`) are
unaffected: `FailWhen` defaults to `null`.

Files no unit test can cover: none.

## Areas

Scan engine: a failed save of an observed-image or runtime run no longer leaves it `Running` or its changes pending.

## Documentation updates

Dev, against the green code, in `docs/areas/scan-engine.md`:

- *Scan runs*, bullet "A run is stored with status `Running` before any work begins…": a run whose first save fails is
  stored `Failed` instead (*Limits and errors*).
- *Scan runs*, bullet "After every recorded run the inventory gauges are refreshed…": except a run saved only by the
  recovery after a failed save.
- *Failure isolation*, bullet "Inside a batch, an error while scanning one observed image or one Docker instance…": the
  item's unsaved changes are discarded first, so none is saved with a later item.
- *Limits and errors*, the error-message bullet: add `Observed image scan could not be saved: <reason>` and `Runtime
  container scan could not be saved: <reason>`.
- *Limits and errors*, bullet "A run that cannot be saved at its end…" (lines 206-208): an observed-image or runtime run
  whose first or final save fails is saved once more on its own as `Failed`, the item's other changes discarded, and
  the error rethrown; only if that save fails as well does a stored run stay `Running` until the repair (a run whose
  first save failed is then not stored); the vulnerability refresh sentence stays. Link record 0040.

Sweep: `docs/ARCHITECTURE.md` (§7 orchestrators, §8 cleanup row) and the other area documents hold no statement about
save failures or the change tracker; `telemetry.md` lists no log event ids; no changelog in the repository. The PR
template's checklist asks for the area document and the record, both listed.

## Architecture check

No guarantee of `.squad/project.md` is touched: the catalog keeps its one write path (the clear drops only pending
entries, the catalog's own conflict handling is unchanged), the orchestrators keep their scoped context, and failure
reasons stay fixed (fact 8). No `docs/ARCHITECTURE.md` change.

## Security considerations

- [x] No limits.
- [x] The save exception reaches the run's message (shown in the scan history) only through `FailureReason.FromException`
  (`unexpected error`, `timed out`, `connection failed`), never its own text; the exception itself goes only to the log
  (2034 / 2043, and 2033 / 2042 as today).

## Decision records

- `docs/decisions/0040-a-scan-item-whose-save-fails-is-saved-again-alone-as-failed.md` (Proposed)

## Out of scope / follow-ups

- Related defect, suspected (fact 7 applies): `VulnerabilityEnrichmentService.cs:647` re-attaches its run with
  `ScanRuns.Update(scanRun)` after the clear, which walks `scanRun.VulnerabilityFindings` and may save changes the clear
  dropped or fail on findings never inserted. Proposed issue: "The vulnerability refresh's final-save retry re-attaches
  the run's findings" — body: the line, the mechanism, and the fix of this plan (set the run's entry state only).
- Changes an item's inner error handler lets through to its final save (partial work before a caught error) are kept as
  today.
