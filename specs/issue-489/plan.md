# Plan: Runtime findings kept after a failed tag lookup move to the container's new snapshot

Source: Issue #489
Tier: standard — a behavior change of the runtime scan (which snapshot kept findings reference, and so what the pages show) that creates, changes or removes no security control.
Security areas touched: none

## Problem / root cause

`RuntimeContainerScanOrchestrator.ProcessContainerAsync` (`src/DockerUpdateGuard/Images/RuntimeContainerScanOrchestrator.cs`)
records the container's new snapshot (line 680) before the registry calls. Two paths then leave the container's
active findings on the previous snapshot:

- **Failed tag lookup** (lines 715-722, `ApplyUnevaluatedTagResult` lines 842-857 with `evaluated = false`): the block
  at line 724 is skipped, so neither `DeactivateSupersededRuntimeFindingsAsync` nor the runtime check
  (`CreateDerivedBaseRuntimeFindingAsync`, whose `KeepFindings` moves derived findings since 0042) runs. Update
  findings *and* derived runtime findings stay active on the old snapshot.
- **Processing error** (catch, lines 748-764): the snapshot is marked *failed* and saved with the run, nothing moves.
  An error inside the runtime check (after line 726) additionally leaves the update findings already replaced while
  the derived ones were neither kept nor deactivated.

The pages read findings by the latest snapshot, so the kept alerts vanish until the next evaluated scan.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | After a failed tag lookup the previous findings stay active on the previous snapshot (issue) | `RuntimeContainerScanOrchestrator.cs:697-744`: no deactivation, no move when `evaluated` is false | confirmed |
| 2 | After a processing error the findings stay on the previous snapshot (issue) | catch at `RuntimeContainerScanOrchestrator.cs:748-764` only marks the snapshot; snapshot added at 680 and saved by the final `SaveScanRunAsync` (589); test `…ContinuesAfterContainerProcessingFailureAsync` (lines 872-878) expects the failed snapshot | confirmed |
| 3 | Derived runtime findings are hidden the same way after a failed tag lookup (not in the issue) | runtime check only inside `if (evaluated)`, lines 724-744; [Base images](../../docs/areas/base-images.md#release-feeds-and-runtime-findings) "made only when the container's update evaluation completed" | confirmed |
| 4 | Container detail reads findings by latest snapshot (issue) | `ApplicationViewService.cs:1975-1994` (`ContainerSnapshotId == latestSnapshot.Id`), base-runtime alert from that list (2040) | confirmed |
| 5 | Runtime container list reads findings by latest snapshot | `ApplicationViewService.cs:772-804`: tag candidates per latest snapshot feed the resolved and available update tag (829-837) when the snapshot has none, as a *failed* snapshot has not | confirmed |
| 6 | My-image and observed-image pages read runtime alerts by latest snapshot (issue) | list: `ApplicationViewService.cs:1580-1609`; detail: 1897-1916 (`linkedSnapshotIds`) | confirmed |
| 7 | The dashboard reads findings by latest snapshot (issue) | `ApplicationViewService.cs:1782-1795`: counts all active findings and own-image warnings via observed images; latest snapshots give only the container count and vulnerability scope | refuted |
| 8 | The base-image page reads findings by latest snapshot (issue) | `ApplicationViewService.cs:899-929`: latest snapshots list the runtime containers of a base image; no update finding is read | refuted |
| 9 | The test `…PreservesFindingsWhenRegistryEvaluationFailsAsync` checks only the database (issue) | `RuntimeContainerScanOrchestratorTests.cs:648-655`: count and `IsActive`, no snapshot | confirmed |
| 10 | Tag candidates belong to the finding only, so a moved finding keeps them | `TagCandidate.cs:18` (`UpdateFindingId`), no snapshot key | confirmed |
| 11 | Snapshot ids are generated on creation, so a finding can reference the new snapshot before the save | `ContainerSnapshot.cs:13` (`= Guid.NewGuid()`); `CreateRuntimeFindingAsync` already uses `snapshot.Id` (line 980) | confirmed |
| 12 | A later evaluated scan deactivates moved findings | `DeactivateSupersededRuntimeFindingsAsync` filters by instance and container id, not snapshot (lines 888-894) | confirmed |
| 13 | A tracking query returns the already tracked instance of a finding without overwriting its in-memory values, so findings preloaded by the new step and deactivated through a later query of the same container are the same objects | to verify: the AC4 test on SQLite (Tester, step 5/6), which fails if a deactivated finding moves (EF Core identity resolution) | unverified |
| 14 | Related defect: a container whose processing fails before its snapshot is recorded (image inspect or catalog call throws) drops out of the current containers for that scan | `GetLatestContainerSnapshotsAsync`, `ApplicationViewService.cs:1087-1124`, takes the snapshots of the latest run; `ProcessContainerAsync` 651-660 run before the snapshot exists | confirmed |

## Acceptance criteria

Scans of one container on one Docker instance; "previous findings" are the findings active after the first scan.

- AC1: When the tag lookup of a later scan fails (outcome other than succeeded, not found or unsupported), each
  previous update finding (*runtime image update*, *tag recommendation*) stays active with the same id, no resolution
  time and its tag candidates, and references the snapshot recorded by that scan.
- AC2: When the tag lookup of a later scan fails, the previous *derived base runtime update* finding stays active
  with the same id and references the snapshot recorded by that scan.
- AC3: When processing the container raises an error after the later scan recorded its snapshot (the tag lookup
  throws), each previous finding stays active with the same id and references that snapshot, whose assessment is
  *failed*.
- AC4: When processing raises an error in the runtime check after the later scan's update evaluation completed (the
  image history call throws), the previous update finding stays inactive with its resolution time on the first
  snapshot, the update finding that evaluation created is the container's only active update finding and references
  the second snapshot, and the previous derived finding stays active with the same id and references the second
  snapshot.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `RuntimeContainerScanOrchestratorScanAllAsyncPreservesFindingsWhenRegistryEvaluationFailsAsync` (adapted: asserts id, no resolution time, the tag candidates and the second snapshot's id) | `src/Tests/DockerUpdateGuard.Tests/RuntimeContainerScanOrchestratorTests.cs` |
| AC2 | `RuntimeContainerScanOrchestratorScanAllAsyncMovesDerivedFindingWhenTagLookupFailsAsync` | `src/Tests/DockerUpdateGuard.Tests/RuntimeContainerScanOrchestratorTests.cs` |
| AC3 | `RuntimeContainerScanOrchestratorScanAllAsyncMovesFindingsWhenContainerProcessingThrowsAsync` | `src/Tests/DockerUpdateGuard.Tests/RuntimeContainerScanOrchestratorTests.cs` |
| AC4 | `RuntimeContainerScanOrchestratorScanAllAsyncMovesOnlyActiveFindingsWhenRuntimeCheckThrowsAsync` | `src/Tests/DockerUpdateGuard.Tests/RuntimeContainerScanOrchestratorTests.cs` |

AC2 and AC4 use the real `DerivedBaseRuntimeDetector` as `RunTwoDerivedScansAsync` does; the Tester may give that
helper an optional parameter for the second scan's tag lookup (existing callers unchanged) or write the setup in the
test. AC4's first scan needs registry tags with a newer version so both scans create a *runtime image update*.

## Approach

All in `RuntimeContainerScanOrchestrator.ProcessContainerAsync`; no page, view query or entity changes.

1. Declare `IReadOnlyList<UpdateFinding> previousFindings = []` beside `snapshot` (before the `try`).
2. Right after `_dbContext.ContainerSnapshots.Add(snapshot)` and before the registry calls, load the container's
   active findings of every type (instance id and container id through `ContainerSnapshot`, `IsActive`, with
   `TagCandidates`) into `previousFindings` — a new private method next to `LoadActiveDerivedFindingsAsync`, taking the
   snapshot and the cancellation token.
3. A new private static method next to `KeepFindings` moves those of the list that are still active in memory
   (`IsActive`) to the snapshot — `KeepFindings` on the filtered list. It does no I/O, so it is safe in the catch.
4. Call it when `evaluated` is false (before `return failureMessage`) and in the catch when `snapshot is not null`
   (after marking it *failed*). The evaluated path is unchanged: there `DeactivateSupersededRuntimeFindingsAsync`
   and the runtime check already leave every active finding on the new snapshot, and their queries return the
   preloaded instances (fact 13), whose in-memory state step 3 reads.

Cost: one more query per container and scan. Findings the failed processing created (added, not yet in the database)
are not in the list and already reference the new snapshot. No user-visible output is new: the pages show the kept
findings as they showed them before the failed scan.

## Options considered

1. **Move the still active findings to the new snapshot in the scan (chosen)** — the 0042 mechanism, one place.
2. **Pages read active findings per container** — four view reads change (runtime list, container detail, observed
   and my-image list and detail), a second mechanism beside 0042, and the detail shows findings that do not belong
   to the shown snapshot.
3. **No new snapshot when the tag lookup fails** — the container would drop out of the current set (fact 14's
   mechanism) and lose its *failed* status.
4. **Copy and deactivate** — 0042 option 4: resolves still valid findings, new detection times.

The issue names options 1 and 2. Inside option 1, loading the findings in the catch instead of preloading them lost:
a query in the catch can throw (a failing database, a cancelled token) out of `ProcessContainerAsync` and end the
whole instance scan *failed*.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Images/RuntimeContainerScanOrchestrator.cs` | preload and move, as *Approach* |
| DockerUpdateGuard.Tests | `RuntimeContainerScanOrchestratorTests.cs` | AC1 adapted, AC2-AC4 new |

## Signatures (for the Dev's skeleton)

None: the change adds two private methods of `RuntimeContainerScanOrchestrator` (described in *Approach*) and no
API; no skeleton.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/RuntimeContainerScanOrchestratorTests.cs`

Existing test code that calls a changed signature: none. The existing test
`RuntimeContainerScanOrchestratorScanAllAsyncPreservesFindingsWhenRegistryEvaluationFailsAsync` has an input AC1
handles differently and is adapted by the Tester in step 5 (AC1). `…WhenContainerProcessingThrowsReportsFixedReasonAsync`
and `…ContinuesAfterContainerProcessingFailureAsync` scan once, with no previous findings: unchanged. The `KeepsDerivedFinding…`
and `ResolvesDerivedFinding…` tests run the evaluated path, which is unchanged.

Files of the change no unit test can cover: none.

## Areas

- [Update detection](../../docs/areas/update-detection.md#update-findings-and-tag-candidates): the rule for a failed
  tag lookup and a processing error.
- [Base images](../../docs/areas/base-images.md#release-feeds-and-runtime-findings): derived findings of a container
  whose update evaluation did not complete.

## Documentation updates

Owned by the Dev, written against the code once green:

- `docs/areas/update-detection.md`, *Update findings and tag candidates*: replace "When the tag lookup failed, the
  previous findings stay active." by the rule: when the tag lookup failed, or processing the container raised an error
  after its snapshot was recorded, every finding of the container still active at that point stays active, keeps its
  tag candidates and moves to the container's new snapshot, which the pages read; findings the failed processing had
  already deactivated or created stay as they are.
- `docs/areas/base-images.md`, *Release feeds and runtime findings*, the running-container bullet ("made only when the
  container's update evaluation completed"): add that when it did not complete the previous derived findings stay
  active and move to the new snapshot, linking Update detection.
- Sweep done: `docs/areas/docker-instances.md` (*Container snapshots*), `docs/areas/web-ui.md`, `docs/areas/scan-engine.md`
  (snapshot retention: a snapshot with an active finding stays — still true) and `docs/ARCHITECTURE.md` (entity table,
  orchestrators) state nothing the change makes untrue. No README or changelog entry (the PR template asks for none).

## Architecture check

The guarantee "other records are written by the scan that owns them" holds: the runtime scan alone moves its
findings. No I/O enters update evaluation. Layering unchanged; no guarantee of `.squad/project.md` is touched, and
`docs/ARCHITECTURE.md` needs no update.

## Security considerations

- [x] No limits.
- [x] No new exception reaches a user-visible message: the catch keeps its fixed reason (`FailureReason.FromException`); the move does no I/O.

## Decision records

- `docs/decisions/0043-runtime-findings-kept-through-a-failed-container-evaluation-move-to-its-new-snapshot.md` (Proposed), linking 0042, which stays unchanged.

## Out of scope / follow-ups

- Fact 14, proposed follow-up issue: "A runtime container whose processing fails before its snapshot is recorded
  drops out of the pages" — when the image inspect or the image catalog call throws in `ProcessContainerAsync`
  (`RuntimeContainerScanOrchestrator.cs:651-660`), no snapshot is recorded for the container, while the run records
  the others; the pages take the current containers from the latest run's snapshots, so the container and its still
  active findings disappear until the next scan.
- Running the runtime check after a failed tag lookup (0042 keeps it tied to a completed evaluation).

## Challenge notes

Devil's Advocate, mode `plan`, specs/issue-489 (/home/user/DockerUpdateGuard/specs/issue-489/plan.md, /home/user/DockerUpdateGuard/docs/decisions/0043-runtime-findings-kept-through-a-failed-container-evaluation-move-to-its-new-snapshot.md)

The plan holds up. I found no major objections and two minor ones.

**Claims I checked against the code (all as the plan states):**
- **Where the snapshot is added and the failure paths:** `RuntimeContainerScanOrchestrator.cs` adds the snapshot at line 680. The `evaluated` guard is at 724. A tag result other than NotFound or Unsupported sets `evaluated = false` at 842-857. The catch at 748-764 only marks the snapshot *failed*.
- **Existing move and deactivate logic:** `KeepFindings` (193-199) only reassigns `ContainerSnapshot`, so it is safe in the catch. `DeactivateSupersededRuntimeFindingsAsync` (884-899) filters by instance and container, not by snapshot. `CreateRuntimeFindingAsync` adds the new finding (Added, not yet in the database) at line 1013, and the database-side `IsActive` filter of the later deactivate query cannot return it. That supports AC4's "only active update finding".
- **Fact 13:** the context is shared across scans, the tests use `SqliteTestDatabase` (test file lines 45, 554), and tracking queries resolve to the tracked instances.
- **Pages:** the container detail reads `ContainerSnapshotId == latestSnapshot.Id` (`ApplicationViewService.cs:1990`). The runtime list reads by latest snapshot ids (785-786). The own and observed image pages read derived findings by latest or linked snapshot ids (1591-1592, 1911-1912). `UpdateFindingConfiguration.cs:45` has an index on (ContainerSnapshotId, IsActive) but no unique constraint, so moving cannot hit a key conflict.
- **Option 3 (no new snapshot):** rejecting it is right. `GetLatestContainerSnapshotsAsync` (1087-1124) takes the current containers from the latest run's snapshots.
- **Moving findings across a changed image:** no risk. `ContainerId` is Docker's container `Id` (`DockerInstanceClient.cs:520`), and Docker cannot change a container's image without recreating it, which gives a new id.
- **Retention:** the sentence in 0043 is true. `docs/areas/scan-engine.md:166` removes old snapshots that have no active update finding.

**Minor objections:**

1. **minor — the claim "the pages show the kept findings as they showed them before the failed scan" is only true for the alerts, not the runtime list's version columns.** A *failed* snapshot never gets `ResolvedVersionTag` or `AvailableUpdateVersionTag`, because `PopulateResolvedVersionTags` (`RuntimeContainerScanOrchestrator.cs:232-268`) runs only in `EvaluateContainerUpdateAsync`. The list then works them out from the moved findings' candidates (`ApplicationViewService.cs:829-837`). Those candidates usually do not include the running digest's version tag, so the resolved version can fall back to the raw tag (for example `latest`). This flicker already exists and the plan does not make it worse; the available update tag actually gets better.
   - Instruction for the Dev: write the area-document rule about the findings and their tag candidates only, and do not say the container list looks the same as before.
   - Instruction for the Lead: either name the version columns as a follow-up issue under *Out of scope*, or decide to copy the previous snapshot's two version tags onto the failed snapshot.
   - Why minor: the alert fix is unchanged; only one plan sentence and one possible follow-up are affected.

2. **minor — one exception path in the catch can leave two active update findings on the new snapshot.** `CreateRuntimeFindingAsync` adds the new finding (line 1013). If `DeactivateSupersededRuntimeFindingsAsync` then throws (a database fault, or `OperationCanceledException`, which `catch (Exception)` at 748 also catches), the old finding is still active in memory and the new step moves it too. Today the old one is hidden on the old snapshot, so only one shows.
   - Instruction for the Dev: put a comment on the move in the catch saying this case is accepted, because the next evaluated scan deactivates both through the container-scoped query. No test is needed.
   - Instruction for the Dev, on the area-document wording "findings the failed processing had already ... created stay as they are": it is accurate, so keep it.
   - Why minor: it needs an infrastructure fault in a narrow window, and the next good scan fixes it on its own.
