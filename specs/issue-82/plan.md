# Plan: Calendar year-prefixed tags are compared across years

Source: Issue #82
Tier: standard — changes which registry tags the update evaluation and the runtime container page treat as successors, a user-visible behavior change without any security control.
Security areas touched: none

## Problem / root cause

`UpdateDetectionService.GetHigherYearPrefixedCandidates` (`src/DockerUpdateGuard/Images/UpdateDetectionService.cs:101`)
keeps only candidates with `candidate.Year == currentYear`, as the area document states (rule 4, "no other year is
ever recommended"). For date-stamped tags (`2024-12-15` → `2025-01-05`) every release after a year change is
therefore dropped, and `EvaluateYearPrefixedUpdate` (`:496-529`) returns *up to date*, "No newer year-based version
was found". The same year rule hides such candidates on the runtime container page
(`src/DockerUpdateGuard/UI/TagCandidateViewHelper.cs:209-213`, `HasSameOrNewerComparableVersion`). The Product Manager
chose option C: only **calendar tags** (year-prefixed tags whose suffix contains no letter) are comparable across
years; product-year tags such as `2019-CU32-ubuntu-20.04` keep the same-year rule.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | Year-prefixed candidates are filtered to the current year | `UpdateDetectionService.cs:101` `candidate.Year == currentYear` | confirmed |
| 2 | Issue: a cross-year case "falls into the digest/NeedsReview path" | `EvaluateYearPrefixedUpdate` returns non-null for every year-prefixed current tag (`:500-528`): a republished-digest update or *up to date*; `CreateReviewOrUpToDateResult` is never reached, so the result is *up to date* | refuted |
| 3 | Issue line numbers `:211-229`/`:217` | The method is at `:88-110` on the current head (line drift, same code) | refuted |
| 4 | The same-year rule is documented and tested as intended | `docs/areas/update-detection.md:38`, `:88-91`; tests `UpdateDetectionServiceYearPrefixedTagUsesSameYearSuccessors` and the `2019-CU33-GDR1-ubuntu-20.04` test above it in `UpdateDetectionServiceTests.cs` | confirmed |
| 5 | Later-year tags reach the evaluation | `RegistryTagQueryHelper.ShouldKeepVersionCandidate` (`:131-143`) uses `TryCompareDisplayVersionTags` → `CompareYearPrefixedTags`, which compares the year first, so `2025-01-05` ≥ `2024-12-15` is kept | confirmed |
| 6 | `CompareYearPrefixedTags` orders across years correctly | `VersionTagResolutionHelper.cs:355-371`: year first, then natural suffix order | confirmed |
| 7 | Calendar tags share the empty year-variant family | `ResolveYearPrefixedVariantFamilyKey` (`:404-419`): a rest without a letter yields `""`; test `...IsMatchingYearPrefixedVariantFamilyMatchesCalendarTags` | confirmed |
| 8 | `2019-CU32` also has the empty family key, so the family check alone cannot tell calendar from product-year tags | `IsYearPrefixedServicingSegment` (`:426-430`) skips `CU32`, the rest is empty → `""` | confirmed |
| 9 | A calendar tag is neither semantic nor a version line, so rule 3 does not catch it first | `NumericVersionTagExpression` (`:46`) and `NumericVersionLineTagExpression` (`:55`) need `.` separators | confirmed |
| 10 | The UI filter is the only other same-year rule | `grep currentYear\|candidateYear src/DockerUpdateGuard`: `UpdateDetectionService.cs` and `TagCandidateViewHelper.cs:209-212` only | confirmed |
| 11 | `TagCandidateViewHelper` is internal and visible to the test project | `src/DockerUpdateGuard/AssemblyInfo.cs:3` `InternalsVisibleTo("DockerUpdateGuard.Tests")` | confirmed |

## Acceptance criteria

- AC1: A calendar current tag gets *update available* for a calendar tag of a later year: current `2024-12-15`
  (running digest `sha256:a`), tags `2024-12-15` (`sha256:a`) and `2025-01-05` (`sha256:b`, published later) →
  status `UpdateAvailable`, `RecommendedTag` `2025-01-05`, summary `Newer tag '2025-01-05' is available`.
- AC2: Calendar candidates across years are ordered highest first: current `2024-11-01`, candidates `2024-12-15`,
  `2025-01-05`, `2025-02-10` (all published later, other digests) → recommended `2025-02-10`, candidates in the order
  `2025-02-10`, `2025-01-05`, `2024-12-15`.
- AC3: A product-year current tag (suffix with a letter) never gets a later-year candidate: current
  `2019-CU32-ubuntu-20.04`, only other tag `2022-CU1-ubuntu-20.04` → `UpToDate`, summary
  `No newer year-based version was found`; the existing tests of fact 4 pass unchanged.
- AC4: A later year needs both tags to be calendar tags: current `2024-12-15` with only `2025-CU1` → `UpToDate`; current
  `2019-CU32` with only `2025-01-05` → `UpToDate`.
- AC5: `VersionTagResolutionHelper.IsMatchingYearPrefixedLine` returns true for two year-prefixed tags of the same year
  (`2019-CU32-ubuntu-20.04`, `2019-CU33-ubuntu-20.04`) and for two calendar tags of different years (`2024-12-15`,
  `2025-01-05`), false when either tag of different years has a letter in its suffix (`2024-12-15`/`2025-CU1`,
  `2019-CU32`/`2022-CU1`), and false when either tag is not year-prefixed (`2024-12-15`/`1.2.3`).
- AC6: The runtime container page's candidate filter follows the same rule:
  `TagCandidateViewHelper.FilterRuntimeAvailableTagCandidates` with current tag `2024-12-15` (no resolved version, no
  publication time) keeps `2025-01-05` and drops `2024-11-01`; with current tag `2019-CU32-ubuntu-20.04` it drops
  `2022-CU1-ubuntu-20.04` and keeps `2019-CU33-ubuntu-20.04`.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `UpdateDetectionServiceCalendarTagRecommendsLaterYearSuccessor` | `src/Tests/DockerUpdateGuard.Tests/UpdateDetectionServiceTests.cs` |
| AC2 | `UpdateDetectionServiceCalendarTagOrdersCandidatesAcrossYears` | `src/Tests/DockerUpdateGuard.Tests/UpdateDetectionServiceTests.cs` |
| AC3 | `UpdateDetectionServiceProductYearTagIgnoresLaterYearSuccessor`; existing `UpdateDetectionServiceYearPrefixedTagUsesSameYearSuccessors` and the CU-line test above it, unchanged | `src/Tests/DockerUpdateGuard.Tests/UpdateDetectionServiceTests.cs` |
| AC4 | `UpdateDetectionServiceCalendarTagIgnoresLaterYearProductTag`, `UpdateDetectionServiceProductYearTagIgnoresLaterYearCalendarTag` | `src/Tests/DockerUpdateGuard.Tests/UpdateDetectionServiceTests.cs` |
| AC5 | `VersionTagResolutionHelperIsMatchingYearPrefixedLineMatchesSameYear`, `VersionTagResolutionHelperIsMatchingYearPrefixedLineMatchesCalendarTagsAcrossYears`, `VersionTagResolutionHelperIsMatchingYearPrefixedLineRejectsLetteredTagsAcrossYears` (both pairs), `VersionTagResolutionHelperIsMatchingYearPrefixedLineRejectsNonYearPrefixedTag` | `src/Tests/DockerUpdateGuard.Tests/VersionTagResolutionHelperTests.cs` |
| AC6 | `TagCandidateViewHelperFilterRuntimeAvailableTagCandidatesKeepsLaterYearCalendarTag`, `TagCandidateViewHelperFilterRuntimeAvailableTagCandidatesDropsLaterYearProductTag` | `src/Tests/DockerUpdateGuard.Tests/TagCandidateViewHelperTests.cs` (new) |

## Approach

- Add `VersionTagResolutionHelper.IsMatchingYearPrefixedLine` (Signatures): false unless both tags parse with
  `TryParseYearPrefixedTag`; true when the years are equal; otherwise true only when neither suffix contains a letter
  (`suffix.Any(char.IsLetter) == false` for both). It decides the year only; the year-variant family stays with
  `IsMatchingYearPrefixedVariantFamily`. Placed in the `Methods` region after `IsMatchingYearPrefixedVariantFamily`.
- `UpdateDetectionService.GetHigherYearPrefixedCandidates`: replace `candidate.Year == currentYear` by
  `VersionTagResolutionHelper.IsMatchingYearPrefixedLine(currentTag, candidate.Tag.Tag)`; drop the now unused
  `currentYear` parameter and its `<param>`, and in `EvaluateYearPrefixedUpdate` the argument, with `out var
  currentYear` becoming `out _`. The tuple shape and the rest of the filter stay.
- `TagCandidateViewHelper.HasSameOrNewerComparableVersion`: replace the year-prefixed block (`:209-214`) by
  `return VersionTagResolutionHelper.IsMatchingYearPrefixedLine(currentComparableTag, candidateComparableTag) &&
  VersionTagResolutionHelper.CompareYearPrefixedTags(candidateComparableTag, currentComparableTag) >= 0;` — the helper
  is false for non-year-prefixed tags, which the former trailing `return false` covered, so `CompareYearPrefixedTags`
  is never called on a tag it would throw for.
- No new user-visible text: the existing summaries are reused.

Early draft PR: no

## Options considered

- **A. Keep the same-year rule** (no change) — lost: date-stamped tags stay wrongly *up to date* at every year change.
- **B. Any later year of the same family** (the issue's `>=` / `CompareYearPrefixedTags` suggestion) — lost: it
  recommends product-line jumps (SQL Server 2019 → 2022) automatically, unlike the major-upgrade policy for semantic
  tags.
- **D. Same-year rule, later year named in the details** — lost: a routine date-stamped release never becomes
  *update available*.
- **Calendar detection by the empty year-variant family** instead of "no letter in the suffix" — lost: `2019-CU32`
  has the empty family as well (fact 8).
- **The rule inline in both callers** — lost: one shared helper avoids the duplicated condition.

The Product Manager chose C (escalation answer, logged).

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Images/Helper/VersionTagResolutionHelper.cs` | new `IsMatchingYearPrefixedLine` |
| DockerUpdateGuard | `Images/UpdateDetectionService.cs` | year filter via the helper; `currentYear` parameter removed |
| DockerUpdateGuard | `UI/TagCandidateViewHelper.cs` | year-prefixed comparison via the helper |

## Signatures (for the Dev's skeleton)

- `public static bool IsMatchingYearPrefixedLine(string tag, string candidateTag)` in `VersionTagResolutionHelper`,
  XML-documented (`<param>` for both, `<returns>`), with `ArgumentException.ThrowIfNullOrWhiteSpace` on both like
  `IsMatchingYearPrefixedVariantFamily`.

The private `GetHigherYearPrefixedCandidates` loses its `int currentYear` parameter (Approach); no test calls it.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/UpdateDetectionServiceTests.cs` (AC1–AC4, existing `CreateService()`)
- `src/Tests/DockerUpdateGuard.Tests/VersionTagResolutionHelperTests.cs` (AC5)
- `src/Tests/DockerUpdateGuard.Tests/TagCandidateViewHelperTests.cs` (AC6, new class `TagCandidateViewHelperTests`)

Existing test call sites of a changed signature: none (only a private method changes). No existing test has an input
a new criterion handles differently: the year-prefixed tests use lettered suffixes or a single year.

Files no unit test can cover: none.

## Areas

- `docs/areas/update-detection.md` (2332 words, +65 words): *Variant families and ordering* (line 38) — the calendar
  tag defined and compared across years; *Evaluation rules* rule 4 (lines 88-91) — same year, or for a calendar current
  tag calendar tags of a later year; *Related decisions* — 0061.

## Documentation updates

- Dev, `docs/areas/update-detection.md`:
  - line 38-41: year-prefixed tags are comparable within the same year-variant family and the same year, except that
    two **calendar tags** (year-prefixed, suffix without a letter, e.g. `2024-12-15`) are comparable across years,
    ordered by year, then suffix;
  - rule 4: candidates are tags of the same year-variant family above the current tag of the same year, or, when the
    current tag is a calendar tag, calendar tags of any year; "no other year is ever recommended" becomes "a later
    year is recommended only between calendar tags";
  - *Related decisions*: add [0061](../decisions/0061-calendar-year-prefixed-tags-are-compared-across-years.md).
- Sweep: `docs/ARCHITECTURE.md`, `README.md`, `docs/areas/web-ui.md`, `docs/areas/base-images.md` name no year rule
  (`grep -i year`); `docs/areas/base-images.md:52` only names year-prefixed tags as a form, unchanged. No changelog
  file exists; the release-notes label is the orchestrator's on the PR.

## Architecture check

No guarantee of `.squad/project.md` or `docs/ARCHITECTURE.md` changes: evaluation stays pure (no I/O), the candidate
cap and the digest and publication conditions are unchanged.

## Security considerations

- [x] No limits: no new parsing of input; the existing tag grammar and regex timeouts apply.
- [x] Exceptions reaching a user-visible message: none new; `CompareYearPrefixedTags` runs only after the helper
  confirmed both tags are year-prefixed.

## Decision records

- `docs/decisions/0061-calendar-year-prefixed-tags-are-compared-across-years.md` (Proposed)

## Out of scope / follow-ups

- Date-stamped tags with a lettered variant (`2024-12-15-alpine`) keep the same-year rule (record 0061, Consequences).