# Plan: Keep a runtime container's derived base-runtime finding while its runtime check cannot be completed

Source: Issue #385
Tier: standard — a behavior change of the runtime scan's finding bookkeeping that creates, changes or removes no security control.
Security areas touched: none

## Problem / root cause

`RuntimeContainerScanOrchestrator.ProcessContainerAsync` calls `DeactivateSupersededRuntimeFindingsAsync`
(`src/DockerUpdateGuard/Images/RuntimeContainerScanOrchestrator.cs:711-716`) for every evaluated container. Its
query (`:875-884`) selects all active findings of the container, the *derived base runtime update* included, and
`DeactivateFindings` (`:177-186`) resolves them. `CreateDerivedBaseRuntimeFindingAsync` (`:990-1046`) then creates the
derived finding again only when every input was read: it returns without one when the image inspect failed
(`:999-1002`), treats a failed image history as empty (`:1011-1013`), and `CreateDotNetRuntimeFindingAsync` /
`CreateNginxRuntimeFindingAsync` return without one when the feed answer is not *succeeded* (`:1066-1071`,
`:1108-1113`). A transient Docker Engine or feed failure therefore resolves a still valid alert.

Decision (the issue's open product question): keep the previous derived finding when the runtime check could not be
completed, as for observed images — [decision 0042](../../docs/decisions/0042-a-runtime-container-keeps-its-derived-runtime-finding-while-its-runtime-check-cannot-be-completed.md).
The pages read a container's findings through its latest snapshot (facts 6, 7), so "keep" means: stay active **and**
move to the new snapshot.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | An evaluated container's active findings are all deactivated, derived ones included, before the derived check runs | `RuntimeContainerScanOrchestrator.cs:711-716`, `:875-884` (no type filter), `:177-186`; the issue cites `:668-688`, the lines have moved since | confirmed |
| 2 | The derived finding is recreated only when inspect, detector and feed all succeed | `:999-1002`, `:1016-1020`, `:1066-1071`, `:1108-1113` | confirmed |
| 3 | A failed image history is treated as an empty history, not as a failure | `:1011-1013` (not in the issue) | confirmed |
| 4 | Observed images keep their derived findings when the image configuration or the feed cannot be read, and remove them when no runtime is detected | `ImageScanOrchestrator.cs:300-303`, `:315-320`, `:372-377`, `:421-426` (issue cites `:423-428`); `docs/areas/base-images.md:121-123`; commit 25234b2 (F-025, #129) | confirmed |
| 5 | A feed channel missing from the feed is answered *not found*, which observed images treat like a failed feed | `DotNetReleaseMetadataService.cs:233-235`, `NginxReleaseMetadataService.cs:190-192`, `ImageScanOrchestrator.cs:372` | confirmed |
| 6 | Every runtime scan records a new snapshot per container before the tag lookup | `RuntimeContainerScanOrchestrator.cs:651-667`, tag lookup from `:677` | confirmed |
| 7 | The pages read a container's findings by the latest snapshot's id: container detail, dashboard list, base-image and my-image pages | `ApplicationViewService.cs:1989-1990`, `:784-789`, `:1587-1592`, `:1908-1912` | confirmed |
| 8 | A snapshot with an active finding is never cleaned up; a scan run referenced by any finding is never cleaned up | `ScanCleanupBackgroundService.cs:203-209`, `:245-248` | confirmed |
| 9 | `docs/areas/update-detection.md` documents the deactivation of all findings of an evaluated container | `update-detection.md:146-148` | confirmed |
| 10 | Findings kept after a failed tag lookup stay active on the previous snapshot and are therefore not shown (related defect, out of scope) | `:711` skipped when `evaluated` is false; fact 7; test `RuntimeContainerScanOrchestratorScanAllAsyncPreservesFindingsWhenRegistryEvaluationFailsAsync` asserts only the database row | confirmed |

## Acceptance criteria

Each criterion is about a container that the first runtime scan gave an active *derived base runtime update* finding
and that a second scan of the same instance evaluates (registry tags read).

- AC1: When the .NET or the nginx release feed answers anything but *succeeded* in the second scan, the previous
  derived finding is still active, has no resolution time, references the second scan's snapshot of the container,
  and it is the container's only derived finding.
- AC2: The same holds when the second scan's image inspect, or its image history, answers anything but *succeeded*.
- AC3: When the second scan reads all inputs and the runtime is no longer behind the feed, or no runtime is detected,
  the previous derived finding is inactive with a resolution time and the container has no active derived finding.
- AC4: When the second scan reads all inputs and the runtime is still behind, the container has exactly one active
  derived finding, referencing the second scan's snapshot; the previous one is inactive.
- AC5: In a second scan whose release feed fails, the container's previous *runtime image update* finding is
  deactivated as before (only derived findings are kept).

## Tests per criterion

AC3–AC5 pin behavior that holds today through the restructured code path; they cannot fail before the fix and are
deferred to step 6 (`.squad/routing.md`, *Pipeline*, step 5).

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | RuntimeContainerScanOrchestratorScanAllAsyncKeepsDerivedFindingWhenDotNetReleaseFeedFailsAsync, RuntimeContainerScanOrchestratorScanAllAsyncKeepsDerivedFindingWhenNginxReleaseFeedFailsAsync | src/Tests/DockerUpdateGuard.Tests/RuntimeContainerScanOrchestratorTests.cs |
| AC2 | RuntimeContainerScanOrchestratorScanAllAsyncKeepsDerivedFindingWhenImageInspectFailsAsync, RuntimeContainerScanOrchestratorScanAllAsyncKeepsDerivedFindingWhenImageHistoryFailsAsync | src/Tests/DockerUpdateGuard.Tests/RuntimeContainerScanOrchestratorTests.cs |
| AC3 | RuntimeContainerScanOrchestratorScanAllAsyncResolvesDerivedFindingWhenRuntimeIsCurrentAsync, RuntimeContainerScanOrchestratorScanAllAsyncResolvesDerivedFindingWhenNoRuntimeIsDetectedAsync | src/Tests/DockerUpdateGuard.Tests/RuntimeContainerScanOrchestratorTests.cs |
| AC4 | RuntimeContainerScanOrchestratorScanAllAsyncReplacesDerivedFindingWhenRuntimeIsStillOutdatedAsync | src/Tests/DockerUpdateGuard.Tests/RuntimeContainerScanOrchestratorTests.cs |
| AC5 | RuntimeContainerScanOrchestratorScanAllAsyncDeactivatesRuntimeImageUpdateFindingWhenReleaseFeedFailsAsync | src/Tests/DockerUpdateGuard.Tests/RuntimeContainerScanOrchestratorTests.cs |

Test setup, as in `RuntimeContainerScanOrchestratorScanAllAsyncCreatesDerivedBaseRuntimeFindingAsync`: SQLite or
InMemory database as that test uses, NSubstitute for `IDockerInstanceClient`, `IDerivedBaseRuntimeDetector` and the
two release metadata services, each failing input answered by a `Returns(first, second)` sequence (a failed answer is
`ExternalOperationResult.Failed<…>`). "The second scan's snapshot" is the container's snapshot with the latest
`RecordedAtUtc`; each assertion reads the database after `ChangeTracker.Clear()`. AC5 needs tags that make the
evaluation *update available* in both scans.

## Approach

Only `RuntimeContainerScanOrchestrator` changes; all members touched are private.

1. `DeactivateSupersededRuntimeFindingsAsync` excludes findings of type `DerivedBaseRuntimeUpdate` from its query. Its
   call site and the other finding types are unchanged; `DeactivateRemovedRuntimeFindingsAsync` (removed containers)
   stays as it is and still covers derived findings.
2. The derived check owns its findings. `CreateDerivedBaseRuntimeFindingAsync` loads the container's active derived
   findings (same filter by `ContainerSnapshot.DockerInstanceId`, `ContainerSnapshot.ContainerId` and `IsActive`,
   type `DerivedBaseRuntimeUpdate`, with `TagCandidates` included for `DeactivateFindings`) and then:
   - inspect data missing, or image history not *succeeded* (or without data): **keep** and return;
   - no runtime or no channel detected: **deactivate** (`DeactivateFindings`) and return;
   - .NET / nginx feed answer not *succeeded* or without data: **keep**;
   - feed read: **deactivate**, then add the new finding when `IsPatchBehind` holds, exactly as today.
   The history is now read before deciding, so a failed history no longer reaches the detector as an empty list.
3. **Keep** moves each previous finding to the new snapshot: its `ContainerSnapshot` navigation is set to
   `findingContext.Snapshot` (the snapshot is tracked as added with a client-generated id), which sets
   `ContainerSnapshotId`. `IsActive`, `ResolvedAtUtc`, `DetectedAtUtc`, `ScanRunId`, `Summary` and `Details` stay
   unchanged: the finding still names the run and the versions of the check that created it.
4. Structure is the Dev's choice within the analyzer gate (cognitive complexity of `CreateDerivedBaseRuntimeFindingAsync`):
   for instance pass the loaded findings to `CreateDotNetRuntimeFindingAsync` / `CreateNginxRuntimeFindingAsync`, which
   keep or deactivate them as `ImageScanOrchestrator.CreateDotNetDerivedBaseRuntimeFindingAsync` does with
   `DeleteSupersededObservedFindingsAsync`. `RuntimeFindingContext` already carries the snapshot with its instance and
   container ids.

The scan status is unchanged: feed, inspect and history failures still add no message and do not make the run
*partial* (`base-images.md`, *Limits and errors*). No new user-visible output.

## Options considered

Listed with the reason each lost in decision 0042: documenting the asymmetry (the issue's alternative; keeps the
flicker), keeping the finding on its old snapshot (invisible, fact 7), copying it to the new snapshot (resolves and
re-detects a valid finding on every failure), and reading findings per container in the pages (four view queries and
the related defect of fact 10; follow-up issue below).

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Images/RuntimeContainerScanOrchestrator.cs` | Approach 1–4 |
| DockerUpdateGuard.Tests | `RuntimeContainerScanOrchestratorTests.cs` | eight new tests |

## Signatures (for the Dev's skeleton)

None: every member the change adds or changes is private, so there is no skeleton (step 4 skipped).

## Test files

- src/Tests/DockerUpdateGuard.Tests/RuntimeContainerScanOrchestratorTests.cs

Existing test code that calls a changed signature: none. Existing tests kept unchanged: the two derived-finding tests
(`…CreatesDerivedBaseRuntimeFindingAsync`, `…CreatesDerivedNginxBaseRuntimeFindingAsync`) run one scan, and
`…PreservesFindingsWhenRegistryEvaluationFailsAsync` and `…RemovesStaleTagCandidatesAfterRescanAsync` have no derived
finding; none has an input a criterion handles differently.

Files no unit test can cover: none.

## Areas

- Base images: the running-container rule of *Release feeds and runtime findings* and the feed failure line of
  *Limits and errors*.
- Update detection: the deactivation rule of *Update findings and tag candidates* excludes derived findings.

## Documentation updates

All by the Dev, against the code as built:

- `docs/areas/base-images.md`, *Release feeds and runtime findings* (lines 118-123): after the observed-image
  replacement rule, add the running-container rule — with all inputs read (image inspect, image history, feed) the
  previous derived findings are deactivated like other runtime findings and a new one is added if still outdated;
  with no runtime detected they are deactivated; when the image inspect, the image history or the feed cannot be
  read they stay active and move to the container's new snapshot.
- `docs/areas/base-images.md`, *Limits and errors* (lines 130-131): feed failures suppress a new runtime finding and
  keep the previous one (observed image and running container); they still do not change the scan status.
- `docs/areas/base-images.md`, *Related decisions*: replace "None yet." with a link to decision 0042.
- `docs/areas/update-detection.md`, *Update findings and tag candidates* (lines 146-148): the deactivation of an
  evaluated container covers its findings except *derived base runtime update*, whose replacement is described in
  Base images (link).

Sweep: `docs/ARCHITECTURE.md` (only the `UpdateFinding` entity row, line 185, still true), `docs/areas/web-ui.md`,
`scan-engine.md` (retention, fact 8, unchanged), `docker-instances.md` and `README.md` hold no statement the change
makes untrue. The PR template asks for no changelog. No `.squad/` file changes.

## Architecture check

No guarantee of `.squad/project.md` is touched: the runtime scan still writes only its own findings, update evaluation
stays free of I/O, the single-instance assumption and the layering are unchanged. No `docs/ARCHITECTURE.md` update.

## Security considerations

- [x] No limits: the change parses no input.
- [x] None: no new exception reaches a user-visible text; feed, inspect and history failures stay silent as today.

## Decision records

- `docs/decisions/0042-a-runtime-container-keeps-its-derived-runtime-finding-while-its-runtime-check-cannot-be-completed.md` (Proposed)

## Out of scope / follow-ups

- Follow-up issue (fact 10), title "Runtime findings kept after a failed tag lookup are hidden from the pages", body:
  "When a runtime scan cannot read a container's registry tags (or processing the container fails), its previous
  update findings stay active but remain on the previous container snapshot (`RuntimeContainerScanOrchestrator.ProcessContainerAsync`).
  The pages read findings by the latest snapshot (`ApplicationViewService` container detail, dashboard, base-image and
  my-image pages), so the alerts disappear from the UI until the next good scan, the flicker F-025 meant to prevent.
  The test `RuntimeContainerScanOrchestratorScanAllAsyncPreservesFindingsWhenRegistryEvaluationFailsAsync` checks only
  the database. Decide whether kept findings move to the new snapshot (as decision 0042 does for derived runtime
  findings) or the pages read active findings per container. Found while planning #385."

## Challenge notes

Devil's Advocate, mode `plan`: specs/issue-385/plan.md and docs/decisions/0042-a-runtime-container-keeps-its-derived-runtime-finding-while-its-runtime-check-cannot-be-completed.md

The plan holds up. I found one minor objection and no major ones.

**What I checked against the code**
- **Root cause.** The plan's root cause and its facts 1–3 match the code:
  - `/home/user/DockerUpdateGuard/src/DockerUpdateGuard/Images/RuntimeContainerScanOrchestrator.cs:711-716` calls `DeactivateSupersededRuntimeFindingsAsync`. Its query at `:875-884` has no type filter, so the derived finding is deactivated too.
  - Inspect returning null exits at `:999-1002`, and a failed history becomes `[]` at `:1011-1013`.
  - A failed feed returns without a finding at `:1066-1071` and `:1108-1113`.
- **Observed images (fact 4).** The plan's description of how observed images behave is right. `ImageScanOrchestrator.cs:300-303` keeps the findings when the configuration cannot be read, and `:315-320` removes them when no runtime is detected. This matches `docs/areas/base-images.md:121-123`.
- **Moving the kept finding to the new snapshot is needed.** Kept findings have to move, or no page shows them:
  - `ApplicationViewService.cs:1587-1592` and `:1908-1912` read derived findings only by the latest snapshot ids.
  - The snapshot's id is assigned client-side before the derived check runs, because the existing code already uses `findingContext.Snapshot.Id`.
- **The finding stays about the same image.** Moving the finding cannot attach it to a container now running a different image: a Docker container id is tied to one image. The kept `SubjectImageVersionId` therefore stays consistent with the new snapshot.
- **Scope.** `DeactivateRemovedRuntimeFindingsAsync` (`:893-906`) still covers derived findings of containers that were removed. There are no other runtime writers of `DerivedBaseRuntimeUpdate`: the only other creator is `ImageScanOrchestrator.cs:389/438`, for observed images.
- **Test deferral.** Deferring AC3–AC5 is correct: today's code already deactivates and re-creates the finding in each of those cases, so those tests cannot fail before the fix.
- **Existing tests.** The plan says no existing test changes, and that is right. Only the two single-scan tests assert on a derived finding (`RuntimeContainerScanOrchestratorTests.cs:1771`, `:1902`).
- **Documentation.** The passages the plan points to are where it says: `docs/areas/update-detection.md:146-148` and `docs/areas/base-images.md:118-131`. The decision index already lists 0042 as Proposed.

**Objections**

1. `minor`: The *Test setup* passage says to use "NSubstitute for … `IDerivedBaseRuntimeDetector` … as in `RuntimeContainerScanOrchestratorScanAllAsyncCreatesDerivedBaseRuntimeFindingAsync`". That test actually uses the real detector (`RuntimeContainerScanOrchestratorTests.cs:1681` `new DerivedBaseRuntimeDetector()`, and the nginx test at `:1813`). This is minor because both setups make the AC tests fail before the fix.
   Instruction for the Tester: base the new tests on that test's real `DerivedBaseRuntimeDetector`, and give the AC2 history test inspect data whose environment alone lets the detector find the runtime. That makes the test prove that a failed history no longer reaches the detector as an empty history. With a stubbed detector that answers `Arg.Any`, the test would only show that the finding stays active.
