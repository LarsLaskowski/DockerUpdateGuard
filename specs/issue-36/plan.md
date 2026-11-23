# Plan: Timestamps are shown in UTC with an explicit suffix

Source: Issue #36
Tier: standard — a user-visible display behavior changes on every page (time zone and format of timestamps); no security control is created, changed or removed.
Security areas touched: Container actions (display only: the timestamps of the *Container actions* section's note and action history in `Components/Pages/RuntimeContainerDetail.razor`; nothing about how actions are offered, confirmed, executed or recorded changes)

## Problem / root cause

The UI formats each timestamp in place with `DateTimeOffset.ToLocalTime()` and the short `g` pattern of the current
culture. The components run on the server, so `ToLocalTime()` converts to the server process's time zone and `g`
uses the server's culture; the text carries no zone indicator. Every site (paths under `src/DockerUpdateGuard/`):

- `Components/Pages/Dashboard.razor:234`, `ScanHistory.razor:78`, `ScanHistory.razor.cs:137`, `DockerInstances.razor:135`,
  `RuntimeContainers.razor:83`, `ObservedImageDetail.razor:109`, `MyImageDetail.razor:102,237,241`,
  `RuntimeContainerDetail.razor:54,130,189,190,330,396,488`
- `Components/Common/VulnerabilityRescanControl.razor:2`, `VulnerabilityAssessmentCard.razor:34`,
  `VulnerabilityFindingsTable.razor:71,78`
- `Components/Layout/MainLayout.razor.cs:278-281` (top bar *last activity*)
- `UI/ResourceUsageChartBuilder.cs:137-141` (chart labels, `MM-dd HH:mm` / `HH:mm` in local time)

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | Timestamps are rendered with `ToLocalTime()` and `g` / local `HH:mm` | `grep -rn ToLocalTime src/DockerUpdateGuard`: the sites listed above. The issue's line numbers have drifted, and it misses `VulnerabilityRescanControl.razor:2`, `VulnerabilityAssessmentCard.razor:34`, `VulnerabilityFindingsTable.razor:71,78`, `ScanHistory.razor.cs:137` and `RuntimeContainerDetail.razor:190`, so its list is incomplete | confirmed |
| 2 | The components run on the server | `docs/ARCHITECTURE.md:102`, `:392` (Interactive Server only) | confirmed |
| 3 | `ToLocalTime()` converts with the process's local zone, `g` with `CultureInfo.CurrentCulture`, and nothing sets a per-request culture | .NET documentation of `DateTimeOffset.ToLocalTime`; no `UseRequestLocalization` or culture setup in `src/DockerUpdateGuard` (grep) | confirmed |
| 4 | Timestamps carry no zone indicator | the sites above; `web-ui.md:30` states "time zone and culture of the server process" | confirmed |
| 5 | No decision record or `.squad/project.md` guarantee covers the display zone | grep of `docs/decisions`, `.squad/project.md` for "time zone", "ToLocalTime", "local time" | confirmed |
| 6 | The compose files set no `TZ` and mount no `/etc/localtime` | grep of `docs/docker-compose.*.yml`, `INSTALL.md`, `README.md`, `DOCKER.md` | confirmed |
| 7 | In a custom format string `:` is the culture's time separator, so `HH:mm` under a culture with another separator differs | .NET documentation, custom date and time format strings (`:` specifier) | confirmed |
| 8 | Three existing tests compute their expected text with `ToLocalTime().ToString("g", CultureInfo.CurrentCulture)` | `VulnerabilityAssessmentCardTests.cs:155`, `ScanHistoryTests.cs:173`, `RuntimeContainerDetailTests.cs:398` | confirmed |
| 9 | `ResourceUsageChartBuilder` is `internal`, visible to the test project, and has no test file | `AssemblyInfo.cs` (`InternalsVisibleTo("DockerUpdateGuard.Tests")`); no `ResourceUsageChartBuilderTests.cs` | confirmed |
| 10 | `UpdateDetectionService.cs:579` writes a stored message with `:O` (explicit offset): not a display site of this change | file read | confirmed |
| 11 | A test can run the formatter under a culture with other separators by cloning `CultureInfo.InvariantCulture` and setting `DateTimeFormat.TimeSeparator` / `DateSeparator`, also in globalization-invariant mode | to verify: Tester, step 5, when writing AC1's culture test | unverified |
| 12 | On a test machine in UTC, `ToLocalTime()` and `ToUniversalTime()` give the same value, so no unit test can tell them apart without changing the process zone | `TimeZoneInfo.Local` is process-wide; hence AC4 (check) | confirmed |

## Acceptance criteria

- AC1: `TimestampFormatter.Format` returns the instant in UTC as `yyyy-MM-dd HH:mm UTC` (e.g. `2026-11-23 21:27 UTC`
  for `2026-11-23 23:27 +02:00`), with `-`, `:` and digits as written whatever the current culture;
  `FormatOptional` returns the same text for a value and `string.Empty` for `null`.
- AC2: Resource chart labels are UTC: the first and the last label read `MM-dd HH:mm UTC`, the visible labels between
  them `HH:mm`, hidden labels stay empty as today.
- AC3: The pages and components show their timestamps in the AC1 format: the top bar's *last activity*, the scan
  history's next scheduled refresh, the vulnerability assessment card's and the rescan control's `Checked …`, and the
  action history's *Requested* column — and every other site of *Problem / root cause* through the same formatter.
- AC4: No `ToLocalTime(` and no `"g"` timestamp format remains in `src/DockerUpdateGuard`.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `TimestampFormatterFormatWithOffsetReturnsUtcWithSuffix`, `TimestampFormatterFormatUnderOtherSeparatorsReturnsInvariantText`, `TimestampFormatterFormatOptionalWithValueReturnsFormattedText`, `TimestampFormatterFormatOptionalWithNullReturnsEmpty` | `src/Tests/DockerUpdateGuard.Tests/TimestampFormatterTests.cs` |
| AC2 | `ResourceUsageChartBuilderGetChartLabelsWithOffsetTimestampsReturnsUtcLabels` | `src/Tests/DockerUpdateGuard.Tests/ResourceUsageChartBuilderTests.cs` |
| AC3 | `MainLayoutWithLatestScanRendersLastActivityInUtc` (new); `VulnerabilityRescanControlWithCheckedAtShowsUtcTimestampAsync` (new); adapted: `VulnerabilityAssessmentCardShowCheckedAtEnabledRendersCheckedLine`, the next-refresh test at `ScanHistoryTests.cs:173`, the action-history test at `RuntimeContainerDetailTests.cs:398` | `MainLayoutRenderTests.cs`, `VulnerabilityRescanControlTests.cs`, `VulnerabilityAssessmentCardTests.cs`, `ScanHistoryTests.cs`, `RuntimeContainerDetailTests.cs` (all in `src/Tests/DockerUpdateGuard.Tests/`) |
| AC4 | check: the Reviewer runs `grep -rnF -e 'ToLocalTime(' -e 'ToString("g"' -e ':g}' src/DockerUpdateGuard` in step 8 and finds no hit | — |

Test inputs carry a non-zero offset (`+02:00`) where the test builds the value, and the expected text is a literal
(`2026-11-23 21:27 UTC`), never computed with the formatter. The culture test sets `CultureInfo.CurrentCulture` to the
clone of fact 11 and restores it in `finally`. Each test carries its `[TestCategory("ACn")]`.

## Approach

- New `UI/TimestampFormatter.cs`, a static class next to `ResourceUsageFormatter`: converts with
  `ToUniversalTime()` and formats with `CultureInfo.InvariantCulture` and private constants
  `"yyyy-MM-dd HH:mm 'UTC'"`, `"MM-dd HH:mm 'UTC'"` and `"HH:mm"`.
- Every site of *Problem / root cause* calls it; null handling stays as today: `—` for an incomplete action run,
  an empty text for a missing *Published* time or latest-scan completion (`FormatOptional`), `Never scanned`,
  `No scans yet`, `shortly (no run recorded yet)`. The surrounding text (`Observed `, `Checked `, `Saved `, the
  container actions note) is unchanged.
- `ResourceUsageChartBuilder`: its private `FormatChartLabel` is removed; `GetChartLabels` calls
  `TimestampFormatter.FormatChartLabel(point.RecordedAtUtc, index == 0 || index == count - 1)`; unused
  `System.Globalization` usings go.
- Layout: the timestamp replaces the old text at the same place; no new element, heading or column.

Early draft PR: no

## Options considered

1. **UTC with suffix, fixed invariant format, one formatter** — chosen: the same text for every user and server,
   deterministic tests, and the one place a later browser-zone conversion would change.
2. **Browser time zone via JS interop** (the issue's second option) — lost: a browser script without a test runner
   (coverage exclusion), the zone known only once interactive, so the prerendered first paint shows another time;
   conversion by zone name needs time-zone data the Alpine image may lack, a fixed offset is wrong across DST.
3. **Server zone with an explicit offset** — lost: keeps the server-dependent time the issue calls wrong.
4. **A configured display zone** — lost: new option and image dependency, still one zone for all users.
5. **Culture `g` plus ` UTC`** — lost: date order follows the server culture, ambiguous, culture-dependent tests.

Recorded in [0058](../../docs/decisions/0058-timestamps-are-shown-in-utc-with-an-explicit-suffix.md).

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `UI/TimestampFormatter.cs` | new |
| DockerUpdateGuard | `UI/ResourceUsageChartBuilder.cs` | labels through the formatter |
| DockerUpdateGuard | the `.razor` / `.razor.cs` files of *Problem / root cause* | call the formatter |
| DockerUpdateGuard.Tests | test files of *Tests per criterion* | new / adapted |

## Signatures (for the Dev's skeleton)

In `src/DockerUpdateGuard/UI/TimestampFormatter.cs`, namespace `DockerUpdateGuard.UI` (already in `_Imports.razor`):

- `public static class TimestampFormatter`
- `public static string Format(DateTimeOffset timestamp)`
- `public static string FormatOptional(DateTimeOffset? timestamp)`
- `public static string FormatChartLabel(DateTimeOffset timestamp, bool includeDateAndZone)`

Deleted: the private `ResourceUsageChartBuilder.FormatChartLabel(DateTimeOffset, int, int)` is removed with the
implementation (step 6), not in the skeleton. The skeleton holds nothing for real.

## Test files

`src/Tests/DockerUpdateGuard.Tests/TimestampFormatterTests.cs` and `ResourceUsageChartBuilderTests.cs` (new);
`MainLayoutRenderTests.cs`, `VulnerabilityRescanControlTests.cs`, `VulnerabilityAssessmentCardTests.cs`,
`ScanHistoryTests.cs`, `RuntimeContainerDetailTests.cs` (extended or adapted, Tester, step 5).

Existing test code that calls a changed signature: none. The three tests of fact 8 keep their inputs but their
expected text changes: adapted by the Tester in step 5 as AC3 tests.

Files of the change no unit test can cover: none.

## Areas

- `docs/areas/web-ui.md (+45 words)`: *General*, the timestamp bullet now says UTC in `yyyy-MM-dd HH:mm UTC`, the
  same for every browser and independent of the server's zone and culture, and the chart labels of AC2; *Related
  decisions* gains 0058.

## Documentation updates

- `docs/areas/web-ui.md` — as *Areas*, Dev, after the code is green.
- `docs/decisions/0058-…` and its index row — Lead (written, `Proposed`).
- `README.md`, `docs/ARCHITECTURE.md`, `INSTALL.md`: none (swept for time zone, `TZ`, `localtime`, timestamp; no
  statement). No changelog file exists; the release-notes label is the orchestrator's.

## Architecture check

No guarantee of `.squad/project.md` is touched; layering holds (a UI helper in the host's `UI/`). Interactive Server
rendering is unchanged.

## Security considerations

- [x] No limits.
- [x] None: the formatter cannot throw for a valid `DateTimeOffset`, and no exception text reaches a page.

## Decision records

- `docs/decisions/0058-timestamps-are-shown-in-utc-with-an-explicit-suffix.md` (Proposed)

## Out of scope / follow-ups

- Conversion to the browser's time zone (option 2): not planned; the formatter is its single seam.
- Stored messages with timestamps (fact 10) and log output keep their formats.

## Challenge notes

Devil's Advocate, plan mode, issue #36 (`/home/user/DockerUpdateGuard/specs/issue-36/plan.md`)

The plan holds up. I checked its assumptions against the code:

- **Sites:** `grep` for `ToLocalTime`, `"g"` and `:g}` in `src/DockerUpdateGuard` finds exactly the sites the plan lists, with the same lines. The grep also turned up no other place that shows a timestamp: no implicit `DateTimeOffset` rendering, no `toLocale` in `wwwroot`, and `LoginRetryAfterRegistry.cs:48` already uses invariant UTC.
- **Facts 8 and 12:** confirmed. The three tests compute their expected text with `ToLocalTime().ToString("g", ...)` (`VulnerabilityAssessmentCardTests.cs:155`, `ScanHistoryTests.cs:173`, `RuntimeContainerDetailTests.cs:398`). The `_requestedAtUtc` fixture at `RuntimeContainerDetailTests.cs:69` has offset zero, so the zone conversion is proven only by the AC1 test that uses `+02:00`.
- **Test seams:** they exist for the planned tests.
  - `MainLayoutRenderTests.CreateContext(DashboardSummaryViewData?)` takes a summary from the test.
  - `VulnerabilityRescanControlTests.cs` exists.
  - `ResourceUsageChartBuilder` is internal and has a `GetChartLabels(history, maxVisibleLabels)` overload (`UI/ResourceUsageChartBuilder.cs:30`), so the AC2 test can produce hidden labels.
- **Scope and option:** right-sized. Choosing UTC with a suffix through one formatter matches the issue's own recommendation.

Objections:

1. **minor:** The plan removes the unused `System.Globalization` using only in `ResourceUsageChartBuilder.cs`, but two more files lose their only `CultureInfo` use in this change. In `Components/Layout/MainLayout.razor.cs` it is line 281, in `Components/Pages/ScanHistory.razor.cs` line 137, and each file imports `using System.Globalization;` on line 1. Left in place, each would raise an unnecessary-using (IDE0005) diagnostic, which the *Analyzer gate* forbids on a changed file. Instruction for the Dev: remove `using System.Globalization;` from `MainLayout.razor.cs` and `ScanHistory.razor.cs` along with the formatter change, unless something else in the file still uses it.
