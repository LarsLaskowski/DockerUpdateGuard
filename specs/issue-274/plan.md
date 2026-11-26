# Plan: Scan status chips show Succeeded and Partial without color on list pages

Source: Issue #274
Tier: standard — the chip colors of four pages change (a behavior change through one new shared mapping), no security control is created, changed or removed.
Security areas touched: none

## Problem / root cause

Seven pages each carry a private `GetScanStatusColor`. The four list-page copies map the status `COMPLETED`, which
`ScanRunStatus` never produces, and have no entry for `SUCCEEDED` or `PARTIAL`, so those fall to `Color.Default`:
`Dashboard.razor.cs:90`, `ScanHistory.razor.cs:119`, `ObservedImages.razor.cs:120`, `MyImages.razor.cs:105`. The three
detail-page copies (`ObservedImageDetail.razor.cs:87`, `MyImageDetail.razor.cs:87`, `RuntimeContainerDetail.razor.cs:193`)
map `SUCCEEDED`/`PARTIAL` correctly but also keep the dead `COMPLETED`. Line numbers in the issue are older; the
defect is the same.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | `ScanRunStatus` is `NotSet`, `Pending`, `Running`, `Succeeded`, `Partial`, `Failed`; no `Completed` | `src/DockerUpdateGuard.Data/Entities/ScanRunStatus.cs:11-36`, namespace `DockerUpdateGuard.Data.Entities` | confirmed |
| 2 | Every chip status string is `ScanRunStatus.ToString()` or `"NotScanned"` | `ApplicationViewService.cs:900` (runtime scan history, `NotSet` when the run is missing), `:967` (scan history items), `:1032-1033` and `:1088-1089` (latest status, `"NotScanned"` fallback); view-data defaults `"NotScanned"` (`ObservedImageListItemData.cs:38`, `ObservedImageDetailViewData.cs:33`) or `string.Empty` (`ScanHistoryItemData.cs:23`) | confirmed |
| 3 | The seven copies and their chip call sites | `Dashboard.razor:238`, `ScanHistory.razor:81`, `ObservedImages.razor:119`, `MyImages.razor:57`, `ObservedImageDetail.razor:37`, `MyImageDetail.razor:42` and `:230`, `RuntimeContainerDetail.razor:485`; mappings as quoted in *Problem* | confirmed |
| 4 | After the removal `using MudBlazor;` is unused in `Dashboard.razor.cs`, `ScanHistory.razor.cs` and `MyImageDetail.razor.cs` (no other MudBlazor type there), and the *Static methods* region of `MyImageDetail.razor.cs` is empty; the other four files keep other `Color` methods | grep of the seven code-behind files | confirmed |
| 5 | `DockerUpdateGuard.UI` is imported for every component | `src/DockerUpdateGuard/Components/_Imports.razor` | confirmed |
| 6 | MudBlazor 9.14.0 renders a chip's color as the class `mud-chip-color-<color>` | `RuntimeContainerDetailTests.cs:736-772` asserts `mud-chip-color-success`/`-warning`/`-default` on rendered chips and passes on `main` | confirmed |
| 7 | `Enum.TryParse<TEnum>(value, ignoreCase, out …)` also accepts an underlying number (`"3"`, also undefined ones such as `"99"`) and a comma-separated list, OR-ing the values (`"Pending, Running"` = 3 = `Succeeded`), without `[Flags]` | .NET API documentation of `Enum.TryParse` (*value*: "name or numeric value of one or more enumerated constants") | confirmed |
| 8 | Reflection fields in `MyImagesTests.cs:38-39` and `MyImageDetailTests.cs:30-31` throw in the static initializer once `GetScanStatusColor` is gone, failing every test of both classes | the `?? throw new InvalidOperationException` initializers | confirmed |
| 9 | `ScanHistoryTests.cs:46,297` and `ScanHistoryPersistentStateTests.cs:55,87` use `Status = "Completed"` and assert nothing about the status | the four tests read | confirmed |
| 10 | `Partial` means at least one step returned an incomplete result | `docs/areas/scan-engine.md:86-91` | confirmed |

## Acceptance criteria

- AC1: `ScanStatusDisplayFormatter.GetStatusColor` returns `Color.Success` for `Succeeded`, `Color.Warning` for
  `Partial` and `Pending`, `Color.Error` for `Failed` and `Color.Info` for `Running`, matching the name in any case.
- AC2: It returns `Color.Default` for every other input: `NotSet`, `NotScanned`, `Completed`, any other text, a number
  (`"3"`), a comma list (`"Pending, Running"`), a padded name (`" Succeeded "`), empty, whitespace and `null`.
- AC3: Every `ScanRunStatus` value except `NotSet` maps to a color other than `Color.Default` (iterated over
  `Enum.GetValues<ScanRunStatus>()`).
- AC4: All seven pages color their scan status chips with the shared mapping: a `Succeeded` chip carries
  `mud-chip-color-success`, a `Partial` chip `mud-chip-color-warning`, and the chip text is the status unchanged.
  No `GetScanStatusColor` remains under `src/DockerUpdateGuard/Components/Pages/`.
- AC5: No test pins the non-existent `Completed` status as a success: its rows leave `MyImagesTests` and
  `MyImageDetailTests`, and the scan-history test data uses `Succeeded`.
- AC6: `docs/areas/web-ui.md` states the scan status chip colors, and its *Known gaps* entry for #274 is removed.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `ScanStatusDisplayFormatterGetStatusColorKnownStatusReturnsExpectedColor` (`[DataRow]` per status in the enum's spelling, upper and lower case; names via `nameof(ScanRunStatus.…)` where the row uses the enum spelling) | `src/Tests/DockerUpdateGuard.Tests/ScanStatusDisplayFormatterTests.cs` |
| AC2 | `ScanStatusDisplayFormatterGetStatusColorOtherValueReturnsDefault` (`[DataRow]` for each input of AC2) | `src/Tests/DockerUpdateGuard.Tests/ScanStatusDisplayFormatterTests.cs` |
| AC3 | `ScanStatusDisplayFormatterGetStatusColorEveryStatusExceptNotSetHasColor` | `src/Tests/DockerUpdateGuard.Tests/ScanStatusDisplayFormatterTests.cs` |
| AC4 | each `[DataRow("Succeeded", "mud-chip-color-success")]`, `[DataRow("Partial", "mud-chip-color-warning")]`, asserting the scan status chip's class and text: `DashboardRecentScanStatusChipShowsStatusColorAsync` | `src/Tests/DockerUpdateGuard.Tests/DashboardRenderTests.cs` |
| AC4 | `ScanHistoryScanStatusChipShowsStatusColorAsync` | `src/Tests/DockerUpdateGuard.Tests/ScanHistoryTests.cs` |
| AC4 | `ObservedImagesLatestScanStatusChipShowsStatusColorAsync` | `src/Tests/DockerUpdateGuard.Tests/ObservedImagesTests.cs` |
| AC4 | `MyImagesLatestScanStatusChipShowsStatusColorAsync` | `src/Tests/DockerUpdateGuard.Tests/MyImagesTests.cs` |
| AC4 | `ObservedImageDetailLatestScanStatusChipShowsStatusColorAsync` | `src/Tests/DockerUpdateGuard.Tests/ObservedImageDetailTests.cs` |
| AC4 | `MyImageDetailScanStatusChipsShowStatusColorAsync` (latest status chip and scan history chip) | `src/Tests/DockerUpdateGuard.Tests/MyImageDetailTests.cs` |
| AC4 | `RuntimeContainerDetailScanHistoryChipShowsStatusColorAsync`; plus `check:` the Reviewer greps `GetScanStatusColor` under `src/DockerUpdateGuard/Components/Pages/` (no hit) | `src/Tests/DockerUpdateGuard.Tests/RuntimeContainerDetailTests.cs` |
| AC5 | `check:` the Reviewer greps `"Completed"` / `"COMPLETED"` in `src/Tests/DockerUpdateGuard.Tests/` — only the AC2 row of `ScanStatusDisplayFormatterTests` remains | — |
| AC6 | `check:` the Reviewer reads the `docs/areas/web-ui.md` diff against AC1–AC2 | — |

Each AC4 test finds the scan status chip by its table cell or section, not the first `.mud-chip` of the page (the
pages show other chips: vulnerability status, update state), and carries `[TestCategory("AC4")]`.

## Approach

- New `src/DockerUpdateGuard/UI/ScanStatusDisplayFormatter.cs`, beside `VulnerabilityDisplayFormatter` and shaped like
  it (`internal static`, `#region Fields` and `#region Static methods`). It holds a
  `private static readonly Dictionary<string, Color> _statusColors` built with `StringComparer.OrdinalIgnoreCase` and
  keyed by `nameof(ScanRunStatus.Succeeded)` → `Color.Success`, `nameof(ScanRunStatus.Partial)` → `Color.Warning`,
  `nameof(ScanRunStatus.Failed)` → `Color.Error`, `nameof(ScanRunStatus.Running)` → `Color.Info`,
  `nameof(ScanRunStatus.Pending)` → `Color.Warning`. `GetStatusColor` returns the entry for a non-null status found in
  it, otherwise `Color.Default` — exact name match only, no trimming, no parsing.
- The seven razor call sites call `ScanStatusDisplayFormatter.GetStatusColor(...)` with their current argument; chip
  text and markup stay unchanged. The seven `GetScanStatusColor` methods are deleted, with `using MudBlazor;` in
  `Dashboard.razor.cs`, `ScanHistory.razor.cs`, `MyImageDetail.razor.cs` and the then empty *Static methods* region of
  `MyImageDetail.razor.cs` (fact 4).
- `DockerInstances.razor` keeps its plain-text status (issue, *Implementation notes*).
- No new user-visible output: only the colors of existing chips change.

Early draft PR: no

## Options considered

- **`Enum.TryParse<ScanRunStatus>(status, ignoreCase: true, …)` and a switch on the enum** (suggested by the issue):
  lost, because it also accepts numbers and comma lists (fact 7) — `"Pending, Running"` would turn green — so it needs
  `Enum.IsDefined` plus a digit/comma guard to keep "anything else → default", more branches than the lookup for the
  same rename safety.
- **A `ToUpperInvariant()` switch on string literals** (today's form): lost, a renamed enum member would silently turn
  its chip grey again; `nameof` keys break the build instead (issue, *Implementation notes*).
- **Chosen:** the case-insensitive dictionary keyed by `nameof`, which is null-safe and exact by construction.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `UI/ScanStatusDisplayFormatter.cs` | new |
| DockerUpdateGuard | `Components/Pages/{Dashboard,ScanHistory,ObservedImages,MyImages,ObservedImageDetail,MyImageDetail,RuntimeContainerDetail}.razor` | call the shared helper |
| DockerUpdateGuard | the seven matching `.razor.cs` | `GetScanStatusColor` deleted (fact 4 cleanups) |
| DockerUpdateGuard.Tests | see *Test files* | new and adapted tests |

## Signatures (for the Dev's skeleton)

- `internal static class ScanStatusDisplayFormatter` (namespace `DockerUpdateGuard.UI`)
- `public static Color GetStatusColor(string? status)`

Deleted: `GetScanStatusColor` in `Dashboard`, `ScanHistory`, `ObservedImages`, `MyImages`, `ObservedImageDetail`,
`MyImageDetail` (internal) and `RuntimeContainerDetail` — the skeleton already rewires the seven razor call sites to
the helper and deletes the methods, so the AC4 tests fail on the skeleton only through its unimplemented helper.

## Test files

- New: `src/Tests/DockerUpdateGuard.Tests/ScanStatusDisplayFormatterTests.cs` (AC1–AC3).
- Extended: `DashboardRenderTests.cs`, `ScanHistoryTests.cs`, `ObservedImagesTests.cs`, `MyImagesTests.cs`,
  `ObservedImageDetailTests.cs`, `MyImageDetailTests.cs`, `RuntimeContainerDetailTests.cs` (AC4).

Existing test code that calls a changed signature, adapted by the Tester in step 5 (fact 8):
`MyImagesTests.cs` — `_getScanStatusColorMethod` and the tests `MyImagesGetScanStatusColorKnownStatusReturnsExpectedColor`
and `MyImagesGetScanStatusColorNullOrBlankReturnsDefault` are deleted (their non-`Completed` cases live in
`ScanStatusDisplayFormatterTests`); `MyImageDetailTests.cs` — `_getScanStatusColorMethod`, its then empty *Fields*
region, the unused `using System.Reflection;` and `MyImageDetailGetScanStatusColorKnownStatusReturnsExpectedColor`
are deleted. `Status = "Completed"` becomes `"Succeeded"` in `ScanHistoryTests.cs:46,297` and
`ScanHistoryPersistentStateTests.cs:55,87` (Tester; fact 9: no assertion changes).

Files of the change no unit test can cover: none.

## Areas

- `docs/areas/web-ui.md` (+30 words): *General* gets one bullet — the scan status chips of the seven pages share one
  color mapping, AC1 and AC2 in prose (any other value neutral), the chip text being the status name; the *Known
  gaps* entry for #274 is removed (about -35 words of the +65 added).

## Documentation updates

- `docs/areas/web-ui.md` as in *Areas* (Dev, once the code is green). The sweep of the area documents and
  `docs/ARCHITECTURE.md` found no enumeration of formatters or chip mappings to extend; `scan-engine.md:8` only
  refers to the chips. No changelog exists; the PR template asks for nothing more.

## Architecture check

UI-only: a shared presentation helper in `src/DockerUpdateGuard/UI/`, like `VulnerabilityDisplayFormatter`. No
guarantee of `.squad/project.md`, no data flow, configuration, migration or service is touched.

## Security considerations

- [x] No limits: the helper does one dictionary lookup on a status string the application produced.
- [x] No exception reaches a user-visible message: the lookup cannot throw for any string or `null`.

## Decision records

- none: the two colors and the single shared mapping are the Product Manager's decisions stated in the issue, the
  area document states the mapping, and the lookup form is a routine implementation choice.

## Out of scope / follow-ups

- The Docker Instances page shows the latest runtime scan status as plain text (`DockerInstances.razor`); it stays.
