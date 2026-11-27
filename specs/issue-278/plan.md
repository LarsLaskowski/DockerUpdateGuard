# Plan: Scan History and Dashboard texts name only the recorded run types

Source: Issue #278
Tier: trivial — UI wording of two Razor literals, with render tests and the area document (past `PATCH_MAX_LINES`); no behavior change.
Security areas touched: none

## Problem / root cause

The description literals `Components/Pages/ScanHistory.razor:10` and `Components/Pages/Dashboard.razor:216` name
discovery and cleanup runs, but both lists show the `ScanRuns` table unfiltered (`UI/ApplicationViewService.cs:536`),
which only three kinds of work write.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | `ScanRunType` has `ObservedImage`, `RuntimeContainer`, `Vulnerability` (plus `NotSet = 0`) | `src/DockerUpdateGuard.Data/Entities/ScanRunType.cs` | confirmed |
| 2 | Only `ImageScanOrchestrator`, `RuntimeContainerScanOrchestrator`, `VulnerabilityEnrichmentService` add scan runs | grep `ScanRuns.Add` / `new ScanRun` under `src/` (non-test): `ImageScanOrchestrator.cs:737`, `RuntimeContainerScanOrchestrator.cs:633`, `VulnerabilityEnrichmentService.cs:538` | confirmed |
| 3 | The other background services create no scan run (cleanup only removes or repairs them) | grep `ScanRun` in `Images/*BackgroundService.cs`: only `ScanCleanupBackgroundService.cs` | confirmed |
| 4 | Both lists show all scan runs, newest first | `ApplicationViewService.cs:536-548`, `:1808`, `:2025` | confirmed |
| 5 | The Dashboard description is on line 215 | It is on line 216 | refuted |
| 6 | Retention "keeps the latest 20 runs per type" | `ScanCleanupBackgroundService.cs:21,150-160`: the 20 most recently completed unreferenced runs overall, not per type (`docs/areas/scan-engine.md:157`); irrelevant to the fix | refuted |
| 7 | These two literals are the only UI text under `src/DockerUpdateGuard/Components` claiming discovery/cleanup runs | grep -i `discovery|cleanup|jobs`: the two lines, plus `MyImageDetail.razor:12` "discovery-owned image" (an image, not a run) | confirmed |
| 8 | `docs/areas/web-ui.md` *Known gaps* holds only the #278 entry | `docs/areas/web-ui.md:250-254` | confirmed |
| 9 | The *Recent scans* header renders also with no recent scans (outside the `RecentScans.Count == 0` branch) | `Dashboard.razor:213-219` | confirmed |

## Acceptance criteria

- AC1: The Scan History page description (`.page-description`) reads exactly "Operational history of observed image
  scans, runtime container scans and vulnerability refreshes." and contains neither "discovery" nor "cleanup"
  (case-insensitive).
- AC2: The dashboard's *Recent scans* section description (the `.section-description` in the `.section-header` whose
  heading is "Recent scans") reads exactly "Latest observed image scans, runtime container scans and vulnerability
  refreshes." and contains neither "discovery" nor "cleanup" (case-insensitive).
- AC3: No user-facing text under `src/DockerUpdateGuard/Components` claims that discovery, synchronization, resource
  statistics or cleanup appear as scan runs.
- AC4: `docs/areas/web-ui.md` states which run types the Scan History page and the dashboard's recent scans show, and
  its *Known gaps* entry for #278 is removed.
- AC5: Format, Build, Test, Analyzer gate and Coverage gate pass (`.squad/stack.md`).

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `ScanHistoryPageDescriptionNamesOnlyRecordedRunTypes` | `src/Tests/DockerUpdateGuard.Tests/ScanHistoryTests.cs` |
| AC2 | `DashboardRecentScansDescriptionNamesOnlyRecordedRunTypes` | `src/Tests/DockerUpdateGuard.Tests/DashboardRenderTests.cs` |
| AC3 | check: Reviewer greps `src/DockerUpdateGuard/Components` case-insensitively for "discovery", "cleanup", "synchroni", "statistics"; no hit describes recorded runs | — |
| AC4 | check: Reviewer reads the two route rows and the removed *Known gaps* section of `docs/areas/web-ui.md` against the code | — |
| AC5 | CI: step 7 gates (`gate-stamp.py`) and the draft PR's CI | — |

Each test selects the element (not the whole markup), compares its trimmed text with `Assert.AreEqual` and checks
the lower-cased text with `Assert.DoesNotContain` for both words, each with a message. The Dashboard test may render
with no recent scans (fact 9).

## Approach

The Tester writes the two tests first (they fail on the old texts); the Dev replaces the two literals with the exact
texts of AC1 and AC2, elements and classes unchanged, then updates `docs/areas/web-ui.md` (*Areas*).

Early draft PR: no — nothing a CI run alone can show.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Components/Pages/ScanHistory.razor` | description literal (line 10) |
| DockerUpdateGuard | `Components/Pages/Dashboard.razor` | *Recent scans* description literal (line 216) |
| DockerUpdateGuard.Tests | `ScanHistoryTests.cs`, `DashboardRenderTests.cs` | one test each |

## Signatures (for the Dev's skeleton)

None: no API member is added or changed.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/ScanHistoryTests.cs` (existing class for `ScanHistory`; no new `ScanHistoryRenderTests` file)
- `src/Tests/DockerUpdateGuard.Tests/DashboardRenderTests.cs`

Existing test code that calls a changed signature: none. Files no unit test can cover: none.

## Areas

`docs/areas/web-ui.md (-10 words)`: the `/` and `/scan-history` rows' "most recent scan runs" each gain "(observed image
scans, runtime container scans and vulnerability refreshes)", linked to `scan-engine.md#scan-runs`; the *Known gaps*
section (heading and its only entry) is removed.

## Documentation updates

- `docs/areas/web-ui.md` — as *Areas*, Dev. No `README.md`, `ARCHITECTURE.md` or changelog change.

## Architecture check

No guarantee touched: no data, job or flow changes; the scan-run model stays as `docs/areas/scan-engine.md` states.

## Security considerations

- [x] No limits: no parsed input.
- [x] None: no exception path changes.

## Decision records

- none: the issue's Product Manager decision keeps the existing scan-run model.

## Out of scope / follow-ups

- Friendlier *Type* column labels (issue).
- `README.md:25` ("Scan History: recent background activity and outcomes") reads as all background jobs; the issue
  excludes README changes (candidate follow-up).
