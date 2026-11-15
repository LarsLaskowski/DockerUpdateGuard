# Plan: Per-subject overlap guard for runtime container scans and vulnerability refreshes

Source: Issue #292
Tier: standard — a behavior change of the scan engine (manual and scheduled runs of one subject now wait for each other) that creates, changes or removes no security control.
Security areas touched: none

## Problem / root cause

Only observed-image scans are serialized: `Images/ImageScanOrchestrator.cs:38-48` (static `Lock` and
`Dictionary<Guid, ObservedImageScanLockEntry>`), `:172-215` (acquire/release a reference) and `ScanAsync` `:677-801`.

- `Images/RuntimeContainerScanOrchestrator.cs:448` `ScanInstanceAsync` creates its scan run and then deactivates and
  recreates the instance's update findings (`DeactivateSupersededRuntimeFindingsAsync` `:828`,
  `DeactivateRemovedRuntimeFindingsAsync` `:851`, `CreateRuntimeFindingAsync` `:876`) with no guard; `ScanAllAsync`
  (`:1194`, call at `:1237`) is started by the scheduled job and by *Check image updates*, each in its own context.
- `Images/VulnerabilityEnrichmentService.cs:501` `RunRefreshAsync` calls `ApplyAndSaveImageVulnerabilitiesAsync`
  (`:556`) per image version, whose `UpsertVulnerabilityFindingsAsync` (`:230`) loads the image version's findings,
  inserts the advisories it does not find and saves — with no guard, so two runs that load before either saves both
  insert the same advisory. `DeactivateStaleFindingsAsync` (`:461`, called after the batches) is unguarded too.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | The observed-image guard is a static reference-counted `SemaphoreSlim` registry; a second scan waits and runs in full; the entry is removed when unreferenced | `ImageScanOrchestrator.cs:38-48`, `:172-215`, `:677-801` | confirmed |
| 2 | Runtime container scans have no guard; `ScanInstanceAsync` creates the run before any work (the issue's line numbers are stale: `:438` is now `:448`, `ScanAllAsync` `:1194`) | `RuntimeContainerScanOrchestrator.cs:448-480` | confirmed |
| 3 | Vulnerability refreshes have no guard; the per-image step is now `ApplyAndSaveImageVulnerabilitiesAsync` (apply, save, and on a save failure discard and save the failed assessment) — the issue's "`ApplyImageVulnerabilitiesAsync` + `SaveChangesAsync` `:449-464`" predates decision 0028 | `VulnerabilityEnrichmentService.cs:405-451`, `:556` | confirmed |
| 4 | The issue's duplicate-finding scenario: both runs load, find no row, insert | `UpsertVulnerabilityFindingsAsync` `:230-290`, read; no unique index on `VulnerabilityFindings` (#14) | confirmed |
| 5 | Callers: scheduled jobs pass the stopping token; *Check image updates* (`RuntimeContainers.razor.cs:385`), *Refresh vulnerabilities* (`ScanHistory.razor.cs:203`) and *Rescan* (`Components/Common/VulnerabilityRescanControl.razor.cs:85`, not `Components/Shared`) pass **no** token | the call sites | confirmed |
| 5a | The issue's "a cancelled UI action ends the wait": no UI action is cancellable today, so a wait is cancelled by host shutdown only | fact 5 | refuted |
| 6 | The waiting run's context tracks no finding of its subject before the wait, so loading after the wait reads current rows | `RefreshAsync` loads `ImageVersions` + `RegistryRepository` only; `LiveImageInventoryQueryService` projects with `AsNoTracking`; image versions of one run are distinct; `ScanAllAsync` loads only `DockerInstances`; `ApplicationViewService` (the page's reads) uses `AsNoTracking` | confirmed |
| 7 | A cancelled refresh today: the generic catch in `RunRefreshAsync` records the run `Failed` with the fixed cancellation reason, then `RefreshInventoryMetricsAsync` throws `OperationCanceledException` | `VulnerabilityEnrichmentService.cs` catch after the batches; `ApplicationTelemetry.cs:135`; `FailureReason.cs:58`; a cancelled wait takes the same path | confirmed |
| 8 | `ScanAllAsync` rethrows `OperationCanceledException` of an instance and logs other errors | `RuntimeContainerScanOrchestrator.cs:1235-1248` | confirmed |
| 9 | One existing test reads the observed-image registry by reflection on `_observedImageScanLocks` | `ImageScanOrchestratorTests.cs:1600` | confirmed |
| 10 | Test projects run with `DoNotParallelize`; tests build services directly (19 + 23 + 19 constructions) | `MSTestSettings.cs`; grep of `new …(` | confirmed |
| 11 | Two contexts of one `SqliteTestDatabase` share one `SqliteConnection`, which is not thread-safe | `Data/SqliteTestDatabase.cs:38-49`; so test gates hold a flow outside a database command (before the base `SaveChangesAsync`, inside an external call) | confirmed |

## Acceptance criteria

- AC1: Two concurrent runtime container scans of the same Docker instance run one after the other: while the first runs,
  the second has created no scan run and not contacted the instance; once the first has finished, the second runs in full
  (two scan runs, two discoveries).
- AC2: Runtime container scans of different Docker instances may run at the same time.
- AC3: Two concurrent vulnerability refreshes of the same image version apply their results one after the other and leave
  exactly one active finding per advisory and package; the waiting refresh's scan run exists before its wait.
- AC4: The guard covers only applying and saving an image version's results: a refresh's provider call for an image
  version runs while another refresh applies that image version, and a refresh of another image version completes while
  it does.
- AC5: The stale-finding deactivation of a full refresh does not run while another full refresh's deactivation runs.
- AC6: A waiting scan cancelled by its token does no work: a runtime scan creates no scan run, does not contact the
  instance and ends with `OperationCanceledException`; a vulnerability refresh applies nothing for the image version it
  waited for (no finding of its run, the image version's assessment keeps the other run's scan run).
- AC7: A lock entry exists only while its key is held or awaited, also after a cancelled wait.
- AC8: Observed-image scans keep their guard semantics.
- AC9: `docs/areas/scan-engine.md`, `docs/areas/vulnerabilities.md` and `docs/ARCHITECTURE.md` describe the new guards, and
  the *Known gaps* entry for #292 is gone.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | RuntimeContainerScanOrchestratorScanAllAsyncConcurrentScansOfSameInstanceRunSequentiallyAsync | src/Tests/DockerUpdateGuard.Tests/RuntimeContainerScanOrchestratorTests.cs |
| AC2 | RuntimeContainerScanOrchestratorScanAllAsyncConcurrentScansOfDifferentInstancesMayOverlapAsync (passes on today's code: deferred to step 6) | src/Tests/DockerUpdateGuard.Tests/RuntimeContainerScanOrchestratorTests.cs |
| AC3 | VulnerabilityEnrichmentServiceConcurrentRefreshesOfSameImageVersionDoNotDuplicateFindingsAsync | src/Tests/DockerUpdateGuard.Tests/VulnerabilityEnrichmentServiceTests.cs |
| AC4 | VulnerabilityEnrichmentServiceRefreshImageVersionAsyncProviderCallDoesNotWaitForGuardAsync, VulnerabilityEnrichmentServiceRefreshImageVersionAsyncOtherImageVersionDoesNotWaitAsync (both pass on today's code: deferred to step 6) | src/Tests/DockerUpdateGuard.Tests/VulnerabilityEnrichmentServiceTests.cs |
| AC5 | VulnerabilityEnrichmentServiceRefreshAsyncStaleFindingDeactivationWaitsForRunningDeactivationAsync | src/Tests/DockerUpdateGuard.Tests/VulnerabilityEnrichmentServiceTests.cs |
| AC6 | RuntimeContainerScanOrchestratorScanAllAsyncWaitingScanCancelledDoesNotRunAsync, VulnerabilityEnrichmentServiceRefreshImageVersionAsyncWaitingRefreshCancelledDoesNotApplyResultsAsync | src/Tests/DockerUpdateGuard.Tests/RuntimeContainerScanOrchestratorTests.cs, src/Tests/DockerUpdateGuard.Tests/VulnerabilityEnrichmentServiceTests.cs |
| AC7 | KeyedScanLockReleaseOfLastReferenceRemovesEntry, KeyedScanLockCancelledWaitRemovesEntry, KeyedScanLockWaitAsyncSecondWaiterWaitsForRelease, KeyedScanLockWaitAsyncDifferentKeysDoNotWait; adapted ImageScanOrchestratorScanAsyncReleasesObservedImageScanLockAfterCompletionAsync | src/Tests/DockerUpdateGuard.Tests/KeyedScanLockTests.cs, src/Tests/DockerUpdateGuard.Tests/ImageScanOrchestratorTests.cs |
| AC8 | ImageScanOrchestratorScanAsyncConcurrentRunsForSameObservedImageAreSerializedAsync (existing, unchanged) | src/Tests/DockerUpdateGuard.Tests/ImageScanOrchestratorTests.cs |
| AC9 | manual: the Reviewer in step 8, on the stamped head, against *Documentation updates* | — |

Test design (the Tester's, within these bounds): no `Task.Delay` or real-clock waits. A run is held by a
`TaskCompletionSource` gate — runtime: an NSubstitute `IDockerInstanceClient.DiscoverContainersAsync` whose first call
signals and blocks; vulnerability: a context whose `SaveChangesAsync` signals and blocks before calling the base (a new
hand-written double in `Data/`, e.g. `GatedSaveChangesDbContext`, created by a new `SqliteTestDatabase` method; the Tester
reports its *Test doubles* line for `.squad/project.md`). "The second run is waiting" is observed through the seam:
loop with `await Task.Yield()` until `GetReferenceCount(key)` of the service's lock reaches 2 **or** the second run's
own progress signal fires (discovery called, its task completed) — so the loop ends on fixed and on unfixed code, and
the assertions then fail on unfixed code. Each test uses fresh keys (new instance / image version ids), since the locks
are static. AC5 holds `StaleFindingDeactivationLock` itself (`WaitAsync(Guid.Empty, …)`) with a non-live active finding
seeded, and asserts the finding is still active while the refresh waits.

## Approach

1. **`Images/Helper/KeyedScanLock.cs`** (new, `internal sealed`): the observed-image registry moved into a type — a
   `Lock`, a `Dictionary<Guid, KeyedScanLockEntry>`, one `SemaphoreSlim(1, 1)` per key. `WaitAsync` takes a reference
   (creating the entry), waits on the semaphore with the token, and on a cancelled or failed wait drops the reference
   (removing and disposing the entry at zero) before rethrowing. `Release` releases the semaphore and drops the
   reference the same way; a key without an entry is an `InvalidOperationException`. Invariant: an entry exists exactly
   while its reference count is above zero. `Images/Data/ObservedImageScanLockEntry.cs` is renamed to
   `KeyedScanLockEntry.cs` (same two properties).
2. **`ImageScanOrchestrator`**: the two static fields and `AcquireObservedImageScanLock` /
   `ReleaseObservedImageScanLockReference` go; `ScanAsync` waits on `ObservedImageScanLocks` keyed by the observed image
   id and releases in `finally` only when the wait succeeded. Same semantics.
3. **`RuntimeContainerScanOrchestrator.ScanInstanceAsync`**: first statement waits on `DockerInstanceScanLocks` keyed by
   `dockerInstance.Id`; the whole existing body (scan run creation included, so its start time is when work starts) runs
   inside `try`, `Release` in `finally`. `ScanAllAsync` is unchanged (it rethrows the cancelled wait).
4. **`VulnerabilityEnrichmentService.RunRefreshAsync`**: around each `ApplyAndSaveImageVulnerabilitiesAsync` call, wait on
   `ImageVersionScanLocks` keyed by the image version id, release in `finally` — one key at a time, never across the
   provider calls of a batch. Around `DeactivateStaleFindingsAsync` and its `SaveChangesAsync`, wait on
   `StaleFindingDeactivationLock` with the fixed key `Guid.Empty`. The scan run is created before any wait (unchanged);
   a cancelled wait goes through the existing generic catch (fact 7). No other change: statuses, messages, metrics,
   `ExternalOperationResult` handling stay.
5. No new log message, configuration key or migration. Bound over the database: none new (one more wait before existing
   work).

## Options considered

1. Shared `KeyedScanLock`, one static instance per subject kind, exposed as `internal static` properties — chosen.
2. The same type as DI singletons — 61 test constructions and three constructors change, keyed registrations; lost.
3. Copy the registry into each service — three copies; lost.
4. Guard a whole refresh or a batch's provider calls — serializes unrelated images, defeats `MaxParallelScans`; lost.
5. Database guard / unique index (#14) — migration, cross-instance scope the PM did not ask for; lost.
6. Skip or merge the second request — rejected by the Product Manager in the issue.

Details in decision 0038.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Images/Helper/KeyedScanLock.cs` | new |
| DockerUpdateGuard | `Images/Data/ObservedImageScanLockEntry.cs` → `Images/Data/KeyedScanLockEntry.cs` | renamed type |
| DockerUpdateGuard | `Images/ImageScanOrchestrator.cs` | uses `KeyedScanLock`; static registry removed |
| DockerUpdateGuard | `Images/RuntimeContainerScanOrchestrator.cs` | instance guard |
| DockerUpdateGuard | `Images/VulnerabilityEnrichmentService.cs` | image-version and deactivation guards |

## Signatures (for the Dev's skeleton)

- `internal sealed class KeyedScanLock` (`Images/Helper/KeyedScanLock.cs`, namespace `DockerUpdateGuard.Images.Helper`)
  - `public int Count { get; }` — number of keys held or awaited
  - `public int GetReferenceCount(Guid key)` — holders plus waiters of the key, 0 without an entry
  - `public Task WaitAsync(Guid key, CancellationToken cancellationToken)`
  - `public void Release(Guid key)`
- `internal sealed class KeyedScanLockEntry` (`Images/Data/KeyedScanLockEntry.cs`): `public required SemaphoreSlim Semaphore { get; init; }`, `public int ReferenceCount { get; set; }`
- Deleted: `ObservedImageScanLockEntry` (file removed with `rm` and staged).
- `ImageScanOrchestrator`: `internal static KeyedScanLock ObservedImageScanLocks { get; } = new();`
- `RuntimeContainerScanOrchestrator`: `internal static KeyedScanLock DockerInstanceScanLocks { get; } = new();`
- `VulnerabilityEnrichmentService`: `internal static KeyedScanLock ImageVersionScanLocks { get; } = new();` and `internal static KeyedScanLock StaleFindingDeactivationLock { get; } = new();`

The skeleton keeps the old observed-image fields until mode `implement`. Names checked against *Writing code*: no
nested type, no underscore, no `Flags`/`Enum` suffix.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/KeyedScanLockTests.cs` (new)
- `src/Tests/DockerUpdateGuard.Tests/RuntimeContainerScanOrchestratorTests.cs`
- `src/Tests/DockerUpdateGuard.Tests/VulnerabilityEnrichmentServiceTests.cs`
- `src/Tests/DockerUpdateGuard.Tests/ImageScanOrchestratorTests.cs`
- `src/Tests/DockerUpdateGuard.Tests/Data/` — the gated-save context double and its `SqliteTestDatabase` factory method

Existing test code affected: `ImageScanOrchestratorScanAsyncReleasesObservedImageScanLockAfterCompletionAsync`
(`ImageScanOrchestratorTests.cs:1545-1606`) reflects on `_observedImageScanLocks`; it still compiles after the change,
so the **Tester** adapts it in step 5 to assert `ImageScanOrchestrator.ObservedImageScanLocks.GetReferenceCount(id) == 0`
(no reflection). No constructor changes, so no other call site moves. The existing tests of the three services keep
their inputs: none runs two scans of one subject at once.

Files no unit test can cover: none.

## Areas

- Scan engine — *Overlapping scans* rewritten, *Scan runs* timing, *Deployment assumption* plural guards, *Known gaps*
  entry for #292 removed, *Related decisions* gains 0038.
- Vulnerabilities — the batch bullet gains the per-image-version wait.

## Documentation updates

All by the Dev, against the code as built:

- `docs/areas/scan-engine.md` *Overlapping scans*: replace the third bullet ("have no such guard") with the runtime
  rule (same Docker instance never at the same time in one process; second waits, then runs in full; different
  instances may overlap) and the vulnerability rule (applying and saving one image version's results never at the same
  time; provider calls not held; different image versions not; a full refresh's stale-finding deactivation never at the
  same time as another's); add that a wait ends when its run is cancelled (host shutdown), and that a manual action
  started during the scheduled run of the same subject waits for it.
- Same file, *Scan runs*: a `RuntimeContainer` run is stored when its wait has ended, so its start time is when its
  work starts; a `Vulnerability` run is stored before any wait, and a wait counts towards its duration.
- Same file, *Deployment assumption*: "Its scan guard is local" → "Its scan guards are local".
- Same file, *Known gaps*: delete the #292 entry. *Related decisions*: add 0038.
- `docs/areas/vulnerabilities.md` (the bullet "Provider calls run in batches…", line ~50): applying and saving an image
  version's results waits while another refresh applies the same image version ([Scan engine](scan-engine.md#overlapping-scans)).
- `docs/ARCHITECTURE.md` §7, the `ImageScanOrchestrator` bullet: the three services guard their subjects (observed image,
  Docker instance, image version, plus the stale-finding deactivation) with static `KeyedScanLock` registries — a
  same-process guard; link decision 0038. §11 *Single-instance assumption*: "`ImageScanOrchestrator`'s in-memory scan
  lock" → "the in-memory scan guards".
- `.squad/project.md` *Test doubles*: the Tester's line for the gated-save context (the orchestrator writes it).
- Sweep result: no other document enumerates the guards (`grep -i "overlap\|same time"` over `docs/`, `README.md`);
  `web-ui.md` and `docker-instances.md` need no change.

## Architecture check

The single-instance assumption and "duplicate scans are wasteful, never incorrect" (`.squad/project.md`) stay; the
guards remove an in-process race and add no cross-instance claim. Layering unchanged (all in the host project). No
background job, interval or registration changes.

## Security considerations

- [x] No limits on parsed input.
- [x] No new exception reaches a user-visible message: a cancelled wait reaches the run's message only through
  `FailureReason.FromException` (fixed cancellation reason), as today.

## Decision records

- `docs/decisions/0038-runtime-container-scans-and-vulnerability-results-wait-for-a-per-subject-guard-in-the-process.md` (Proposed)

## Out of scope / follow-ups

- A unique index on vulnerability findings across instances: #14.
- The circuit-scoped context of the UI pages: #21.
- A per-image rescan and a full refresh's stale-finding deactivation are not serialized against each other (the issue
  asks for deactivation against deactivation only). The deactivation touches only image versions outside the live set,
  so the worst interleaving deactivates a just-rescanned finding of a non-live image version, which the next full
  refresh would deactivate anyway; no follow-up proposed.
