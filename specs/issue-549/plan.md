# Plan: Remote timestamps without an offset are read as local time

Source: Issue #549
Tier: standard — a behavior change in how remote timestamps are read (a value without an offset moves from the host's zone to UTC); no security control is created, changed or removed.
Security areas touched: Registry and Docker Scout calls (untrusted JSON of `DockerHubClient.cs` and `OciRegistryClient.cs`, the OCI bearer token's `issued_at` that sets its cache expiry); Docker Engine access (untrusted Engine JSON read in `DockerInstanceClient.cs`, parsing only)

## Problem / root cause

Every remote timestamp is parsed with `DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, out …)`, which
uses `DateTimeStyles.None`: a value without an offset gets the host's local offset, and a value with an offset keeps
it, although every target is named and documented as UTC. Sites:

- `DockerHub/DockerHubClient.cs:433` (`TryGetDateTimeOffset`: tag `last_pushed` line 218, `last_updated` lines 289
  and 1365) and `:748` (configuration blob `created`);
- `Images/OciRegistryClient.cs:528` (`TryReadCreatedAtUtc`: manifest and configuration `created`, the tag's
  publication time and the image configuration's creation time) and `:415` (token `issued_at`, `DateTimeStyles.None`
  spelled out);
- related, same defect, found on the way: `Docker/DockerInstanceClient.cs:737` (`TryParseTimestamp`: stats `read`
  line 476, image inspect and history `Created` lines 779 and 794) and `Images/DotNetReleaseMetadataService.cs:90`
  (`TryParseTimestamp`: `latest-release-date`, `eol-date`, both date-only, lines 191 and 194; `eol-date` decides
  "This channel is no longer supported" in `RuntimeContainerScanOrchestrator.cs:473-474`).

On a host with `TZ` other than UTC the times shift by the zone's offset; west of UTC an offset-less epoch `created`
lands after `1970-01-01T00:00:00Z` and passes the epoch filter of `TryReadPublishedAtUtc` (`OciRegistryClient.cs:514`).

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | The six sites above parse without `AssumeUniversal`; no other `DateTime(Offset).(Try)Parse` exists in the production projects | `grep -rn "DateTime\(Offset\)\?\.\(Try\)\?Parse"` over `src/DockerUpdateGuard*`, the lines above | confirmed |
| 2 | Without a style, an offset-less value takes the local offset; `DateTimeStyles.AssumeUniversal \| DateTimeStyles.AdjustToUniversal` reads it as UTC and returns every value (`Z`, `+02:00`, date-only) with offset zero and the same instant; `null`, empty and unparseable values still fail; 9 fractional digits parse in both | probe in the scratchpad (.NET 11.0.100, `dotnet run probe.cs`) under `Etc/UTC` and `Etc/GMT+5`: `2024-05-01T10:00:00` → `-05:00` without style, `+00:00` with; `2024-05-01T12:00:00+02:00` → `10:00:00+00:00`; `2024-05-01` → `00:00:00+00:00` | confirmed |
| 3 | On Linux, `Environment.SetEnvironmentVariable("TZ", "Etc/GMT+5")` followed by `TimeZoneInfo.ClearCachedData()` switches `TimeZoneInfo.Local` in the running process, and restoring `TZ` plus another `ClearCachedData()` switches it back | same probe: `TimeZoneInfo.Local` read `Etc/GMT+5 -05:00`, then `Etc/UTC` after the restore | confirmed |
| 4 | The test projects never run tests in parallel, so a process-wide zone switch inside one test affects no other | `src/Tests/*/MSTestSettings.cs`: `[assembly: DoNotParallelize]` | confirmed |
| 5 | CI runs the tests on Linux (`TZ` is honoured there, not on Windows) | `.github/workflows/*.yml`: every job `runs-on: ubuntu-latest` | confirmed |
| 6 | Existing tests feed only `Z` or date-only values and run in UTC, so none sees a different result | timestamp literals of `src/Tests/DockerUpdateGuard.Tests/*.cs` all end in `Z` or are dates (`eol-date`, `latest-release-date`) | confirmed |
| 7 | `DateTimeOffset` equality compares instants only, so an assertion of the value alone does not see a wrong offset | .NET semantics, shown by the probe (`10:00-05:00` vs `15:00Z` equal instants) | confirmed |

## Acceptance criteria

All with the process's local zone switched to `Etc/GMT+5` (UTC−5) for the test; "in UTC" means the instant is the
value's wall time in UTC **and** `Offset` is `TimeSpan.Zero` (both asserted, fact 7).

- AC1: Docker Hub — a tag lookup whose `last_pushed` has no offset reports a publication time in UTC; an image
  configuration whose `created` has no offset reports a creation time in UTC.
- AC2: Other registries — a tag lookup whose configuration blob `created` has no offset reports a publication time in
  UTC; one whose `created` is `1970-01-01T00:00:00` (no offset) leaves the publication time unknown.
- AC3: Docker Engine — a resource sample whose stats `read` has no offset is recorded at that time in UTC.
- AC4: .NET release feed — a channel whose `eol-date` is a date without offset reports its end of life as 00:00 UTC of
  that date.
- AC5: The shared parser reads a value without an offset (date-time or date-only) as UTC, returns a value with an
  offset (`Z`, `+02:00`, `-05:00`) as the same instant with offset zero, and answers `null` for `null`, empty,
  whitespace and unparseable input.
- AC6: The OCI token's `issued_at` is read through the shared parser (an offset-less value is UTC).

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `DockerHubClientGetTagAsyncLastPushedWithoutOffsetReadsUtcAsync`, `DockerHubClientGetImageConfigurationAsyncCreatedWithoutOffsetReadsUtcAsync` | `src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs` |
| AC2 | `OciRegistryClientPublicationTimeGetTagAsyncConfigCreatedWithoutOffsetReadsUtcAsync`, `OciRegistryClientPublicationTimeGetTagAsyncEpochCreatedWithoutOffsetLeavesTimeUnknownAsync` | `src/Tests/DockerUpdateGuard.Tests/OciRegistryClientPublicationTimeTests.cs` |
| AC3 | `DockerInstanceClientCollectContainerResourceUsageAsyncStatsReadWithoutOffsetReadsUtcAsync` | `src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientTests.cs` |
| AC4 | `DotNetReleaseMetadataServiceGetChannelReleaseAsyncEndOfLifeDateWithoutOffsetReadsUtcMidnightAsync` | `src/Tests/DockerUpdateGuard.Tests/DotNetReleaseMetadataServiceTests.cs` |
| AC5 | `ExternalTimestampTryParseUtcWithoutOffsetReadsUtc` (DataRows `2024-05-01T10:00:00`, `2024-05-01`), `ExternalTimestampTryParseUtcWithOffsetReturnsSameInstantInUtc` (DataRows `Z`, `+02:00`, `-05:00`), `ExternalTimestampTryParseUtcUnreadableValueReturnsNull` (DataRows `null`, empty, whitespace, `not a time`) | `src/Tests/DockerUpdateGuard.Tests/ExternalTimestampTests.cs` |
| AC6 | check: the Reviewer confirms in step 8 that `GetTokenExpirationUtc` calls `ExternalTimestamp.TryParseUtc` and no other parse; the parser's behavior is AC5's | — |

AC6 has no client-level test: its effect is an expiry relative to `DateTimeOffset.UtcNow`, which the client reads
directly, and a test would depend on the real clock (`docs/UNIT_TESTS.md`, *Time in tests*).

Every test of AC1–AC5 switches the zone with `LocalTimeZoneScope` (*Test files*) in a `using` block around the act
only; AC1–AC4 fail on the current code (offset `-05:00`, instant five hours late; AC2's epoch case has a time), AC5
fails on the skeleton.

## Approach

1. New `Infrastructure/ExternalTimestamp.cs`, an internal static helper with one method that calls
   `DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp)`
   and returns the timestamp or `null` (fact 2). One helper for all four types, so the rule exists once.
2. `DockerHubClient`: `TryGetDateTimeOffset` returns `ExternalTimestamp.TryParseUtc(TryGetString(element, propertyName))`;
   line 748 calls the helper instead of `DateTimeOffset.TryParse`.
3. `OciRegistryClient`: `TryReadCreatedAtUtc` calls the helper; `GetTokenExpirationUtc` uses
   `ExternalTimestamp.TryParseUtc(issuedAtValue) ?? DateTimeOffset.UtcNow`, the out-of-range handling of the
   `AddSeconds` `catch` unchanged.
4. `DockerInstanceClient` and `DotNetReleaseMetadataService`: the private `TryParseTimestamp` is removed and its
   callers call the helper.
5. `using System.Globalization;` goes from a file where it is no longer used.

Unreadable values stay unknown exactly as today; no field is added or dropped. No user-visible text changes.

Early draft PR: no

## Options considered

- **Assume UTC and convert to offset zero** (chosen) — see the record.
- **Reject values without an offset** (the issue's "or rejected") — lost: the .NET feed's dates have no offset by
  format and would all become unknown, disabling the end-of-life note; an unknown publication time disables the
  published-since bounds and age rules for the tag.
- **`AssumeUniversal` alone, offsets kept** — lost: a value with an explicit non-zero offset would still reach the
  `*Utc` properties with that offset; `AdjustToUniversal` costs nothing.
- **Only the two registry clients' `created` and `last_pushed`** (the issue's scope) — lost: leaves the identical
  defect at four sites; the shared helper makes each a one-line change.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Infrastructure/ExternalTimestamp.cs` | new helper |
| DockerUpdateGuard | `DockerHub/DockerHubClient.cs` | two parse sites call the helper |
| DockerUpdateGuard | `Images/OciRegistryClient.cs` | two parse sites call the helper |
| DockerUpdateGuard | `Docker/DockerInstanceClient.cs` | private `TryParseTimestamp` removed, callers use the helper |
| DockerUpdateGuard | `Images/DotNetReleaseMetadataService.cs` | private `TryParseTimestamp` removed, callers use the helper |

## Signatures (for the Dev's skeleton)

- `internal static class ExternalTimestamp` in `src/DockerUpdateGuard/Infrastructure/ExternalTimestamp.cs`
  (namespace `DockerUpdateGuard.Infrastructure`), in a `Static methods` region:
  `public static DateTimeOffset? TryParseUtc(string? value)`

One parameter, no nested type, no keyword name; the `Try…` name returning a nullable follows the existing private
`TryParseTimestamp` and `TryReadCreatedAtUtc`, which pass the *Analyzer gate*. The skeleton changes no call site; the
Dev switches the callers in step 6.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/ExternalTimestampTests.cs` (new), and the four existing files of *Tests per
  criterion*.
- New test helper `src/Tests/DockerUpdateGuard.Tests/Helper/LocalTimeZoneScope.cs` (Tester):
  `internal sealed class LocalTimeZoneScope : IDisposable` with `public LocalTimeZoneScope(string timeZoneId)`. The
  constructor keeps the current `TZ` value, sets `TZ` to the id and calls `TimeZoneInfo.ClearCachedData()`; when
  `TimeZoneInfo.Local.Id` is then not the id (Windows, a missing zone database) it restores and throws
  `InvalidOperationException`, so a test never passes vacuously in UTC. `Dispose` restores the kept value and clears
  the cache again (facts 3–5). The tests use `Etc/GMT+5` (UTC−5, no daylight saving time).

Existing test code that calls a changed signature: none (only private members change).

Files of the change no unit test can cover: none.

## Areas

- `docs/areas/integration-failures.md` (+75 words; `plan-check.py` reports no area size warning): a new `### Remote timestamps` section after *Remote text fields*
  stating the rule — a remote timestamp without an offset is read as UTC (a date alone as 00:00 UTC), every one is kept
  as UTC, an unreadable one is unknown — and naming its sources (Docker Hub `last_pushed`, `last_updated`, `created`;
  other registries' `created` and token `issued_at`; the Docker Engine's stats and image times; the .NET release
  feed's dates); a *Related decisions* entry for 0066.

## Documentation updates

- `docs/areas/integration-failures.md` — as under *Areas* (Dev).
- `docs/UNIT_TESTS.md (Test doubles +20 words)` — a row for `LocalTimeZoneScope` (`Helper/`): the host's local time
  zone, switched for the scope through `TZ`, failing where the switch does not take (Tester).
- No `README.md`, `docs/ARCHITECTURE.md` or `.squad/project.md` change: no configuration, flow or guarantee changes;
  the sweep of `docs/areas/*.md` and `docs/ARCHITECTURE.md` for `UTC`, `offset`, `time zone`, `last_pushed`,
  `issued_at` found only statements this change keeps true (`registries.md` lines 66–79 and 179, `web-ui.md` 37–39).
  The release-notes label is the orchestrator's (`bug`).

## Architecture check

No guarantee of `.squad/project.md` is touched: failure reasons stay fixed (no new message), update evaluation still
performs no I/O, layering is kept (the helper sits in the host's `Infrastructure/` beside `ExternalJson`).

## Security considerations

- [ ] No limits: the helper adds no bound; the parsed strings are those the clients already read.
- [ ] Exceptions that can reach a user-visible message: none — `TryParse` does not throw for these styles (the
  combination is valid for `DateTimeOffset`, fact 2), and an unreadable value stays unknown as today.
- The token cache expiry (`issued_at`) only moves from the host's zone to UTC; the 401 handling that drops a cached
  token is unchanged.

## Decision records

- `docs/decisions/0066-remote-timestamps-without-an-offset-are-read-as-utc.md` (Proposed)

## Out of scope / follow-ups

- Moving `OciRegistryClient`'s token expiry to an injected `TimeProvider` (which would let AC6 get a client-level
  test): not needed for this fix.
