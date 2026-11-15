# Plan: A runtime container whose processing fails keeps its place, findings and versions

Source: Issue #491, Issue #492 (one shared run)
Tier: standard — a behavior change of the runtime scan's failure path (which snapshot is recorded and which versions it carries); no security control is created, changed or removed.
Security areas touched: Container actions (a container whose processing failed now has a snapshot in the latest runtime scan, so the action gate of `RuntimeContainerDetail.razor` offers actions for it; the gate itself is unchanged)

## Problem / root cause

Both issues fit one change: they are the same failure path of `RuntimeContainerScanOrchestrator.ProcessContainerAsync`
(`src/DockerUpdateGuard/Images/RuntimeContainerScanOrchestrator.cs`), and the snapshot #491 adds is only correct with
the versions #492 carries.

- **#491:** the image inspect (line 664) and the catalog call `GetOrCreateImageVersionAsync` (668) run before the
  snapshot is created and added (677-693). When either throws, `snapshot` is still `null`, so the handler (768-788)
  records nothing (`if (snapshot is not null)`, 772). The pages take an instance's current containers from the
  snapshots of its latest run (`ApplicationViewService.GetLatestContainerSnapshotsAsync`, `UI/ApplicationViewService.cs`
  1087-1118), so the container and its findings (still active on the old snapshot) disappear for one scan.
- **#492:** `ResolvedVersionTag` and `AvailableUpdateVersionTag` are written only by `PopulateResolvedVersionTags`
  (244-275), called only from `EvaluateContainerUpdateAsync` (834). A failed tag lookup (`evaluated == false`, 730-742)
  or an exception before line 834 leaves both `null`, and the list falls back to the findings' tag candidates
  (`ApplicationViewService.cs` 829-838).

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | Inspect or catalog throwing before the snapshot leaves no snapshot (#491) | `RuntimeContainerScanOrchestrator.cs` 664-693, 772 (the issue's "around 651-660" is now 664-693) | confirmed |
| 2 | Issue #491: the image inspect throws in practice | The real `DockerInstanceClient.InspectImageAsync` (`Docker/DockerInstanceClient.cs` 1238-1274) throws only on a requested cancellation or its argument guards; every other error is a *failed* result, which yields `inspectData == null` (orchestrator 1265-1267) — so only a cancellation or a substituted client throws there; the reorder (Approach 1) covers it anyway | refuted |
| 3 | `GetOrCreateImageVersionAsync` can throw: a database error in `FindImageVersionAsync` or `SaveChangesAsync` that is no `DbUpdateException`, or `SingleAsync` after one; it saves the shared context, so pending changes of earlier containers are written there | `Data/Repositories/ImageCatalogRepository.cs` 94-226 | confirmed |
| 4 | Current containers = snapshots of each instance's latest runtime run that has snapshots | `ApplicationViewService.cs` 1087-1118; `docs/areas/docker-instances.md` *Container snapshots* | confirmed |
| 5 | The two version tags are set only in `PopulateResolvedVersionTags`, which always assigns both (#492; issue's "232-268" moved to 244-275 with #489) | orchestrator 244-275, 834 | confirmed |
| 6 | The list derives missing versions from the latest snapshot's findings' candidates; those are mostly higher versions, so an alias falls back to the raw tag | `ApplicationViewService.cs` 829-838, 543-555; `Images/UpdateDetectionService.cs` 58, 119-150 | confirmed |
| 7 | `ContainerSnapshot.ImageVersionId` is required (`Guid`, not nullable) | `Data/Entities/ContainerSnapshot.cs` 23 | confirmed |
| 8 | Earlier-snapshot queries order by `RecordedAtUtc` after materializing (the SQLite test database cannot order by `DateTimeOffset`) | `GetPreviousResolvedVersionTagAsync` 1290-1305 (the pattern the new query follows) | confirmed |
| 9 | `FailureReason.FromException` yields only fixed reasons, except a `ReportableException`'s own text | `Infrastructure/FailureReason.cs` 49-84 | confirmed |
| 10 | Record 0043 is unreleased (no tag contains it); latest tag `v1.3.8` | `git tag --contains` on its adding commit: empty | confirmed |

## Acceptance criteria

All through `ScanAllAsync`, a container scanned twice; "the second snapshot" is the container's snapshot of the second
run.

- AC1: (#491) when the image inspect throws in the second scan, the second run records a snapshot of the container
  with the catalogued image version, assessment *failed*, message `Container '<name>' could not be processed: <reason>`
  (reason from `FailureReason.FromException`), and every finding active before the scan stays active and references it.
- AC2: (#491, #492) when the catalog call throws in the second scan, the second run records a snapshot of the container
  with the image version, resolved version and available update version of its first snapshot, assessment *failed*
  with the message of AC1, and every finding active before the scan stays active and references it; the run is
  *partial*.
- AC3: (#492) when the tag lookup returns a failure other than *not found* or *unsupported*, the second snapshot has
  the first snapshot's resolved version and available update version.
- AC4: (#492) when processing throws after the snapshot was recorded and before the evaluation completed (the tag
  lookup throws), the second snapshot has the first snapshot's resolved version and available update version.
- AC5: (#492) when the tag lookup returns *not found*, the second snapshot has no resolved and no available update
  version, although the first has both.
- AC6: (#492) when the first snapshot names another image version (another running digest), a failed tag lookup leaves
  the second snapshot without resolved and available update version.

## Tests per criterion

All in `src/Tests/DockerUpdateGuard.Tests/RuntimeContainerScanOrchestratorTests.cs`. For AC2-AC6 the first scan must
store both versions: a container on an alias tag (`latest`) whose running digest equals a semantic tag's, with a newer
semantic tag recommended (as in `...KeepsResolvedVersionAfterRegistryTagRotationAsync`).

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | RuntimeContainerScanOrchestratorScanAllAsyncRecordsFailedSnapshotWhenImageInspectThrowsAsync | RuntimeContainerScanOrchestratorTests.cs |
| AC2 | RuntimeContainerScanOrchestratorScanAllAsyncRecordsFailedSnapshotFromPreviousSnapshotWhenImageCatalogThrowsAsync | RuntimeContainerScanOrchestratorTests.cs |
| AC3 | RuntimeContainerScanOrchestratorScanAllAsyncKeepsVersionTagsWhenTagLookupFailsAsync | RuntimeContainerScanOrchestratorTests.cs |
| AC4 | RuntimeContainerScanOrchestratorScanAllAsyncKeepsVersionTagsWhenContainerProcessingThrowsAsync | RuntimeContainerScanOrchestratorTests.cs |
| AC5 | RuntimeContainerScanOrchestratorScanAllAsyncStoresNoVersionTagsWhenRepositoryIsNotFoundAsync | RuntimeContainerScanOrchestratorTests.cs |
| AC6 | RuntimeContainerScanOrchestratorScanAllAsyncKeepsNoVersionTagsOfAnotherImageVersionAsync | RuntimeContainerScanOrchestratorTests.cs |

## Approach

All changes are private members of `RuntimeContainerScanOrchestrator`; no new type, no new text.

1. **Order in `ProcessContainerAsync`** after the reference validation (659-662, unchanged): load the container's
   active findings (`LoadActiveFindingsAsync`, now taking the instance id and container id instead of the snapshot,
   moved before the catalog call), then its latest earlier snapshot (new `LoadPreviousSnapshotAsync`), then the catalog
   call, then create and add the snapshot, then the image inspect (moved from 664 to directly before `GetTagAsync`).
   The rest is unchanged. An inspect exception now meets a recorded snapshot and takes the existing 0043 path (AC1).
2. **`LoadPreviousSnapshotAsync(dockerInstanceId, containerId, cancellationToken)`** returns the latest snapshot of the
   instance and container by `RecordedAtUtc`, or `null`: a projection of `ImageVersionId`, `ResolvedVersionTag`,
   `AvailableUpdateVersionTag` and `RecordedAtUtc` (into an untracked `ContainerSnapshot`), materialized and ordered in
   memory as fact 8. Its result size is bounded by the snapshot retention, like `GetPreviousResolvedVersionTagAsync`.
3. **`CreateSnapshot(...)`** (private static): builds the snapshot as 677-691 do today, from the instance, run,
   discovered container, an image version id and the previous snapshot, and copies the previous snapshot's two version
   tags only when its `ImageVersionId` equals the given one (AC3, AC4, AC6). Used for the normal snapshot and the
   fallback below. At most five parameters (S107 limit seven).
4. **Evaluated without tags** (`ApplyUnevaluatedTagResult`, *not found* / *unsupported* branch, 884-898): set both
   version tags to `null` (AC5) — the snapshot no longer starts empty. `PopulateResolvedVersionTags` already overwrites
   both, so a completed evaluation is unchanged.
5. **Handler (768-788):** when `snapshot` is `null` and a previous snapshot was loaded, create the fallback with
   `CreateSnapshot` and the previous snapshot's image version id, add it, then continue as today (status *failed*,
   message, `KeepActiveFindings`) (AC2). The handler stays free of I/O: everything it needs was loaded before the
   catalog call. With no previous snapshot (a new container, or a failure in the two loads) nothing is recorded, as
   today.

Not changed: the invalid-reference returns (648, 661), the run's *partial* status and message list,
`GetPreviousResolvedVersionTagAsync`, the pages.

## Options considered

1. **Chosen:** take the image version and the two version tags from the container's latest earlier snapshot, the tags
   set at snapshot creation and replaced by an evaluation.
2. **Pages fall back to an older snapshot** for a container missing from the latest run or for missing versions —
   lost: the pages cannot tell a failed container from a removed one, and every current-container read needs it.
3. **Snapshot without image version** — lost: entity model change with migration and null handling in every page.
4. **Carry the tags only in the failure branches** (the option #492 names, applied there) — lost: the handler cannot
   tell whether the evaluation already stored its own tags before the error (an exception in `CreateRuntimeFindingAsync`
   comes after them).
5. **Fallback alone, without moving the inspect** — lost: an inspect error would record the previous image version
   although the catalog call would have given the current one.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Images/RuntimeContainerScanOrchestrator.cs` | Approach 1-5 |
| DockerUpdateGuard.Tests | `RuntimeContainerScanOrchestratorTests.cs`, `Helper/TwoScanOutcome.cs` if extended | six tests |

## Signatures (for the Dev's skeleton)

None: every new or changed member is private (Approach), so there is no skeleton.

## Test files

`src/Tests/DockerUpdateGuard.Tests/RuntimeContainerScanOrchestratorTests.cs`. The catalog seam for AC2 is the
orchestrator's `IImageCatalogRepository` parameter: an NSubstitute double that forwards to a real
`ImageCatalogRepository` until the test switches it between the two scans, then throws (the first scan also calls it
for the recommended version, so a call count does not work).

Existing test code that calls a changed signature: none (only private members change). If the Tester extends
`RunTwoScansAsync` or `TwoScanOutcome` (a catalog double, the snapshot's tags and scan run), it adapts their call sites
in the same file.

Files no unit test can cover: none.

## Areas

- Docker instances, *Container snapshots*: a container whose processing fails before its image version is known is
  recorded with the previous snapshot's image version; a container without an earlier snapshot is not.
- Update detection, *Alias resolution* and *Update findings and tag candidates*: the version carry, and the kept
  findings moving to that snapshot too.

## Documentation updates

All by the Dev, against the code as built:

- `docs/areas/docker-instances.md`, *Container snapshots*: a bullet after the *current* containers bullet with the
  fallback rule of Approach 5 (image version of the latest earlier snapshot, *failed*, failure message; none for a
  container without an earlier snapshot, the run still *partial*); the first bullet's "one container snapshot per
  discovered container" points to the two exceptions (invalid reference, this one). Add 0044 under *Related decisions*.
- `docs/areas/update-detection.md`, *Alias resolution*, the resolved-version bullet: a new snapshot starts with the
  resolved and available update version of the container's latest earlier snapshot when it names the same image
  version; a completed evaluation replaces both, *no tag data* and *unsupported* store none. *Update findings and tag
  candidates*: drop "after its snapshot was recorded" and link the fallback in Docker instances. *Related decisions*:
  replace "None yet" by 0043 and 0044.
- Sweep done: `docs/ARCHITECTURE.md` (no statement on failed snapshots or version carry), `README.md`,
  `docs/areas/container-actions.md` 105-108 (the "in the latest runtime scan" definition stays true; no edit),
  `docs/areas/scan-engine.md` 88 (still true). No changelog exists.

## Architecture check

Single write path of the catalog: the fallback writes no catalog entry, it reuses an existing image version id. Records
are written by the scan that owns them: snapshots and findings stay the runtime scan's. Update evaluation stays free of
I/O: the new query runs in the orchestrator before the evaluation. No change to `docs/ARCHITECTURE.md`.

## Security considerations

- [x] No limits: no parsed input changes.
- [x] Exceptions that reach a user-visible message: any exception of the catalog call or the two new loads reaches the
  fallback snapshot's assessment message and the run's message as `Container '<name>' could not be processed: ` plus
  `FailureReason.FromException` — a fixed reason (fact 9), the same text the run already records today.
- Container actions: the gate's rule is unchanged; the fallback snapshot carries the runtime status and running flag
  this scan discovered, so the actions offered match the container's current state.

## Decision records

- `docs/decisions/0044-a-runtime-container-whose-processing-fails-takes-what-it-could-not-determine-from-its-latest-earlier-snapshot.md` (Proposed), indexed.
- `docs/decisions/0043-...` (unreleased, edited in place): its two "separate defect" consequences now point to 0044.

## Out of scope / follow-ups

- A container whose image reference is invalid still records no snapshot (docker-instances.md); a valid image version
  cannot exist for it.
- A container seen for the first time whose catalog call fails records no snapshot (0044, Consequences).