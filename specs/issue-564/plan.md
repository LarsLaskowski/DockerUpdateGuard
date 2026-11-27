# Plan: Image history creation time is read as Unix seconds

Source: Issue #564
Tier: standard — a parser of the Docker Engine's history answer changes what it reads (behavior), and no security control is created, changed or removed.
Security areas touched: none

## Problem / root cause

`DockerInstanceClient.ParseImageHistoryEntry` (`src/DockerUpdateGuard/Docker/DockerInstanceClient.cs:783`) reads
`Created` as `ExternalTimestamp.TryParseUtc(TryGetString(element, "Created"))`. `TryGetString` (lines 546–560)
returns null unless the property's `ValueKind` is `String`, and the Engine sends `Created` of a history entry as an
int64 Unix time in seconds, so `DockerImageHistoryEntryData.CreatedAtUtc` is null for every entry of a real Engine.
The tests (`DockerInstanceClientTests.cs:652`, `:1704`) feed `"Created": "2025-06-02T12:00:00Z"` and assert no
creation time, so nothing noticed.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | `ParseImageHistoryEntry` reads `Created` only as a JSON string | `DockerInstanceClient.cs:783` with `TryGetString` (`:546–560`, `ValueKind == JsonValueKind.String`) | confirmed |
| 2 | The Engine API answers `GET /images/{name}/history` with `Created` as `integer`, `format: int64` | moby `v20.10.24` `api/swagger.yaml` (API version 1.41, the one the client calls: `v1.41/images/…/history`, `:880`), `HistoryResponseItem`, lines 7680–7729, downloaded to the scratchpad | confirmed |
| 3 | The value is Unix seconds; an entry without a creation time gets `0` from today's Engine and the zero `time.Time` (`-62135596800`) from v20.10 | moby `master` `daemon/images/image_history.go:48–51` and `daemon/containerd/image_history.go:87–88` (`created = h.Created.Unix()` only when `h.Created != nil`, else 0); `v20.10.24` `daemon/images/image_history.go:54` (`h.Created.Unix()` of a non-pointer `time.Time`) | confirmed |
| 4 | The tests feed string `Created` values and assert no `CreatedAtUtc` | `DockerInstanceClientTests.cs:638–688`, `:1699–1741` | confirmed |
| 5 | `docs/areas/docker-instances.md` (*Image inspection and history*, line 119) says history creation time is read | read | confirmed |
| 6 | No code reads `DockerImageHistoryEntryData.CreatedAtUtc` today (the defect has no user-visible effect yet); `DerivedBaseRuntimeDetector` uses `CreatedBy` and `Comment` only | `grep CreatedAtUtc src/DockerUpdateGuard`, `DerivedBaseRuntimeDetector.cs:78` | confirmed |
| 7 | No related defect: image inspect `Created` (`:768`) and stats `read` (`:475`) are strings in the Engine API (RFC 3339), so they are read correctly; the container listing's int64 `Created` is not read at all | swagger `Image.Created` `type: "string"` (line 1538), stats example `read: "2015-01-08T22:57:31.547920715Z"` (line 6204); `ParseContainer` (`:445–466`) | confirmed |
| 8 | `DateTimeOffset.FromUnixTimeSeconds` throws `ArgumentOutOfRangeException` below `-62135596800` and above `253402300799` | .NET API documentation of the method; AC4's cases `253402300800` and `long.MaxValue` exercise the guard against it | confirmed |
| 9 | `JsonElement.TryGetInt64` returns false for a number with a fraction or exponent part or outside int64 | to verify: the Tester's AC3 cases `1398108230.5` and `1e30` return null without a failure, step 5 (the .NET documentation says false for a value that "cannot be represented as an Int64") | unverified |
| 10 | Record 0066 is unreleased and asks that every remote timestamp be read through the shared helper | `git tag --contains e9b45e3` empty; 0066 *Consequences*, last item | confirmed |
| 11 | `docs/areas/integration-failures.md` (*Remote timestamps*) lists the fields read through the rule; history `Created` is not among them | lines 101–107 | confirmed |

## Acceptance criteria

- AC1: An image history answer whose entries carry `Created` as an integer reports each entry's `CreatedAtUtc` as
  that many seconds after 1970-01-01T00:00:00Z, with offset zero; for the sample under *Approach*:
  2014-04-21T19:23:50Z, 2014-04-21T19:23:42Z, 2013-06-13T21:03:50Z.
- AC2: A history entry whose `Created` is a string is still read by `ExternalTimestamp.TryParseUtc` (a value
  without an offset as UTC, one with an offset as the same instant with offset zero).
- AC3: A history entry whose `Created` is `0`, negative, above `253402300799`, a number that is not an int64
  (`1398108230.5`, `1e30`), `null`, a boolean, or absent has `CreatedAtUtc` null; the history still succeeds and
  the entry's other fields are read.
- AC4: `ExternalTimestamp.TryFromUnixSeconds` returns the instant for `1` and `253402300799`, and null for `0`,
  `-1`, `-62135596800`, `253402300800`, `long.MinValue` and `long.MaxValue`, without throwing.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `DockerInstanceClientGetImageHistoryAsyncWhenCreatedIsUnixSecondsReadsUtcInstantsAsync` (the sample, all three entries) | `src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientTests.cs` |
| AC2 | `DockerInstanceClientGetImageHistoryAsyncWhenCreatedIsTextReadsUtcInstantAsync` (DataRows `2025-06-02T12:00:00`, `2025-06-02T14:00:00+02:00`, expected 12:00Z both) | `src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientTests.cs` |
| AC3 | `DockerInstanceClientGetImageHistoryAsyncWhenCreatedIsNoUnixSecondsReturnsUnknownCreationTimeAsync` (one DataRow per value of AC3, the raw JSON text put in place of the first sample entry's `1398108230`, absent: the property removed; asserts null, `Succeeded`, and that entry's `Comment`/`Tags`) | `src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientTests.cs` |
| AC4 | `ExternalTimestampTryFromUnixSecondsInRangeReturnsUtcInstant`, `ExternalTimestampTryFromUnixSecondsOutOfRangeReturnsNull` | `src/Tests/DockerUpdateGuard.Tests/ExternalTimestampTests.cs` |

## Approach

1. `ExternalTimestamp` gets `TryFromUnixSeconds(long seconds)`: for `seconds` in `1 ..
   DateTimeOffset.MaxValue.ToUnixTimeSeconds()` it returns `DateTimeOffset.FromUnixTimeSeconds(seconds)` (offset zero),
   otherwise null. The range check comes first, so the method never throws. `0` and negatives are unknown: `0` is what
   the Engine sends for an entry without a creation time (fact 3), negatives are the zero time of older Engines or
   before 1970, which no image layer has. The class summary changes from "deliver as text" to "deliver as text or as
   Unix seconds".
2. `DockerInstanceClient` gets a private static `TryGetHistoryCreatedAtUtc(JsonElement element)`: when `Created` is a
   `Number`, `TryGetInt64` and then `ExternalTimestamp.TryFromUnixSeconds` (a failed `TryGetInt64` is null); otherwise
   `ExternalTimestamp.TryParseUtc(TryGetString(element, "Created"))` as today, which covers a string and returns null
   for `null`, a boolean or an absent property. `ParseImageHistoryEntry` uses it for `CreatedAtUtc`. The existing
   `TryGetInt64(JsonElement, params string[])` helper is not used: it returns `0` for a failed read, which the range
   check would turn into null anyway, but it hides the property kind; the new helper reads the property once.
3. Real sample (fixture of AC1 and AC3): the `ImageHistory` example of the Engine API definition (fact 2), as JSON,
   with the one personal value — a maintainer's name and e-mail address in the second entry's `CreatedBy` — replaced
   by a generated one. The Tester puts it verbatim into a raw string literal constant `HistorySample` of
   `DockerInstanceClientTests`:

   ```json
   [
     {
       "Id": "3db9c44f45209632d6050b35958829c3a2aa256d81b9a7be45b362ff85c54710",
       "Created": 1398108230,
       "CreatedBy": "/bin/sh -c #(nop) ADD file:eb15dbd63394e063b805a3c32ca7bf0266ef64676d5a6fab4801f2e81e2a5148 in /",
       "Tags": ["ubuntu:lucid", "ubuntu:10.04"],
       "Size": 182964289,
       "Comment": ""
     },
     {
       "Id": "6cfa4d1f33fb861d4d114f43b25abd0ac737509268065cdfd69d544a59c85ab8",
       "Created": 1398108222,
       "CreatedBy": "/bin/sh -c #(nop) MAINTAINER Example Maintainer <maintainer@example.test>",
       "Tags": [],
       "Size": 0,
       "Comment": ""
     },
     {
       "Id": "511136ea3c5a64f264b78b5433614aec563103b4d4702f3ba7d4d2698e22c158",
       "Created": 1371157430,
       "CreatedBy": "",
       "Tags": ["scratch12:latest", "scratch:latest"],
       "Size": 0,
       "Comment": "Imported from -"
     }
   ]
   ```

   Variants it covers: integer `Created`, the extra fields `Id` and `Size`, an empty `CreatedBy`, an empty and a
   two-element `Tags`, a non-empty `Comment`. The other fields are public image IDs, a file hash of a public image and
   public tags; the generated name and address share nothing with the replaced ones. AC3's cases are this sample with
   one value replaced (variants the source shows, fact 3, and malformed numbers); AC2 keeps hand-typed strings, since
   no Engine sends them.
4. The two existing history tests stay unchanged: they assert no creation time, and their string input is read as
   before (AC2).

Early draft PR: no

## Options considered

- **`0` as 1970-01-01T00:00:00Z** (a reproducible build with `SOURCE_DATE_EPOCH=0`): lost — the Engine sends `0` also
  for an entry without a creation time (fact 3), and the two cannot be told apart; unknown claims nothing false.
- **Conversion as a private helper of `DockerInstanceClient` only**: lost — record 0066 has every remote timestamp read
  through `ExternalTimestamp` (fact 10), and the helper there is tested without an HTTP fixture (AC4).

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Infrastructure/ExternalTimestamp.cs` | new `TryFromUnixSeconds`, summary |
| DockerUpdateGuard | `Docker/DockerInstanceClient.cs` | private `TryGetHistoryCreatedAtUtc`, used by `ParseImageHistoryEntry` |

## Signatures (for the Dev's skeleton)

- `public static DateTimeOffset? TryFromUnixSeconds(long seconds)` in `internal static class ExternalTimestamp`
  (region `Static methods`, after `TryParseUtc`; XML docs with `<param>` and `<returns>`). Checked: no keyword name,
  one parameter, no nested type.

`TryGetHistoryCreatedAtUtc` is private (*Approach* 2) and not in the skeleton.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientTests.cs` (AC1–AC3, constant `HistorySample`)
- `src/Tests/DockerUpdateGuard.Tests/ExternalTimestampTests.cs` (AC4)

Existing test code that calls a changed signature: none (no signature changes).

Files of the change no unit test can cover: none.

## Areas

- `docs/areas/docker-instances.md (+20 words)` — *Image inspection and history*: the history creation time is the
  Engine's Unix time in seconds; `0`, a negative or an unreadable value is unknown.
- `docs/areas/integration-failures.md (+15 words)` — *Remote timestamps*: the list gains the image history `Created`,
  read as Unix seconds in UTC (a text value by the rule above).

## Documentation updates

- `docs/areas/docker-instances.md`, `docs/areas/integration-failures.md` — as *Areas* says, by the Dev against the
  code as built.
- `docs/decisions/0066-remote-timestamps-without-an-offset-are-read-as-utc.md` — none: the change follows its last
  consequence and changes no decision in it.
- Sweep: `docs/ARCHITECTURE.md` (`DockerInstanceClient` row, line 251; image history, line 338) and
  `docs/areas/base-images.md` (history signals, lines 87–105) name no creation time and stay; `README.md` has no
  setting involved. No changelog file exists; the PR template asks for unit tests and the area documents (above).

## Architecture check

No guarantee of `docs/ARCHITECTURE.md` or `.squad/project.md` is touched: the client reads one more value kind of an
answer it already receives; requests, transports, timeouts and failure reasons stay.

## Security considerations

- [x] No limits: the range check bounds a parsed integer before the conversion, so an out-of-range value is unknown,
  not an exception.
- [x] None: no new exception; without the range check `ArgumentOutOfRangeException` would fail the whole history as
  `unexpected error`, which AC3 and AC4 exclude.

## Decision records

- none: the one choice (`0` and negatives are unknown) follows the Engine's own placeholder, nothing reads the value
  yet, and the rule is stated in the area documents; the placement follows record 0066.

## Out of scope / follow-ups

- The container listing's int64 `Created` is not read; nothing needs it (fact 7).
