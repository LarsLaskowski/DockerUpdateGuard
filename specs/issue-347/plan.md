# Plan: Control characters in remote text reach logs and displayed messages

Source: Issue #347
Status: Draft
Tier: security — replacing control characters in remote text before it reaches a log entry or a displayed message is output escaping, which `.squad/routing.md` (*Tiers*) names a guard.
Security areas touched: Logging and telemetry of external data; Registry and Docker Hub calls (OCI and Docker Hub failure messages); Docker Engine access (Engine failure messages); External process execution (Trivy's error line)

## Problem / root cause

#324 routed every remote text that enters a failure message through one helper,
`src/DockerUpdateGuard/Infrastructure/ResponseBodyExcerpt.cs`, but the helper only cuts:
`Truncate` (lines 64-86) returns the text, or its prefix plus `…`, character for character. Its four kinds of input are

- response body excerpts: `ReadAsync` (line 53) → `Truncate`, called by `DockerInstanceClient.cs:873`, `:926`, `:995`,
  `:1069`, `:1235`, `OciRegistryClient.cs:681`, `DockerHubClient.cs:230`, `DotNetReleaseMetadataService.cs:155`,
  `NginxReleaseMetadataService.cs:124`;
- the OCI reason phrase: `OciRegistryClient.cs:683`;
- the origin of a rejected next-page link: `PaginationLinkGuard.cs:73`;
- Trivy's last error line: `TrivyVulnerabilityProvider.cs:268`, logged at `:339` (`TrivyCommandFailed`, event 3421) and
  returned at `:343`.

So a U+000A, U+000D, U+001B (start of a terminal escape sequence), U+2028 or U+202E the remote side sends goes
unchanged into the result message, from there into scan-run errors, stored assessments and the pages, and into the
plain-text console log. The fix is in that one helper.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | Response body excerpts, the reason phrase, rejected origins and Trivy's line reach messages unchanged apart from the cut | `ResponseBodyExcerpt.cs:64-86` (no character is changed); call sites in *Problem* | confirmed |
| 2 | Every one of these texts already passes `ResponseBodyExcerpt.Truncate`, so the helper is the one shared place the issue asks for | `grep -rn "ResponseBodyExcerpt\." src/DockerUpdateGuard`: the nine `ReadAsync` calls, three `Truncate` calls and `ReadAsync` → `Truncate` (line 53); no other reader of an error body (`grep ReadAsStringAsync` finds only the NGINX success path, `NginxReleaseMetadataService.cs:130`) | confirmed |
| 3 | The OCI and Docker Hub token failure messages (token request URI, built from the registry's `WWW-Authenticate` realm) reach the result only through a body excerpt | `OciRegistryClient.cs:1061` wraps the message in `CreateAuthenticationFailureResponse` (`:691`), whose content is read by `CreateFailureResultAsync` (`:681`) | confirmed |
| 4 | Issue: rejected-link origins can carry control characters | Scratch measurement (.NET 10, `Uri.TryCreate(base, link)` then `Scheme://IdnHost`): U+000A, U+0001, U+001B, U+0085 and `%0A` in a host make the link unparseable; U+202E is dropped by the IDN mapping (`http://a` U+202E `b.example/` gives `http://ab.example`); `mailto:`/`news:` links give an empty or plain host. No tested form puts a control character into the origin; the origin still passes the helper, so a form not tested is cleaned too | refuted |
| 5 | Issue: control characters reach log entries | Trivy logs the excerpt (`TrivyVulnerabilityProvider.cs:339`); the host keeps the default providers of `WebApplication.CreateBuilder` (no `ClearProviders`, `Program.cs`), so the console logger writes the message text; OTLP carries it as a log body | confirmed |
| 6 | Issue: control characters distort the UI | No `MarkupString` in `src/DockerUpdateGuard` (Blazor encodes text, so no markup injection); line breaks collapse under `white-space: normal`; a bidirectional override (U+202E) reverses the text after it in the browser. Confirmed for bidi and zero-width characters; line breaks matter only in logs | confirmed |
| 7 | `Rune.GetUnicodeCategory` gives `Control` for U+0000-U+001F, U+007F, U+0085, U+009B; `LineSeparator` for U+2028; `ParagraphSeparator` for U+2029; `Format` for U+00AD, U+061C, U+180E, U+200B, U+200D, U+202E, U+2066, U+FEFF, U+FFF9, U+E0041, U+1D173; `SpaceSeparator` for U+00A0; and `string.EnumerateRunes` yields U+FFFD for a lone U+D800 or U+DC00 | Scratch measurement on .NET 10 (`Rune.GetUnicodeCategory(new Rune(cp))`; `"a" + U+D800 + "b" + U+DC00 + "c"` enumerated to U+0061, U+FFFD, U+0062, U+FFFD, U+0063) | confirmed |
| 8 | `HttpResponseMessage.ReasonPhrase` accepts U+001B and U+0007 but throws `FormatException` for U+000A, so a test can set an escape in a reason phrase directly | Scratch measurement on .NET 10 | confirmed |
| 9 | Whether `SocketsHttpHandler` passes a reason phrase with U+001B from the wire | to verify: not load-bearing (the helper cleans whatever arrives), so no measurement is planned; no owner, no step | unverified |
| 10 | Record 0009 is unreleased, so it is extended in place | `git ls-remote --tags origin`: last tag `v1.3.7` (252b2eb); `git merge-base --is-ancestor 0458e3c 252b2eb` fails (0458e3c added the record) | confirmed |
| 11 | No existing test feeds a control character into an excerpt, so every #324 test keeps its expectation | `grep` over `src/Tests` for bodies and error lines with `\n`, `\r`, `\t`: only `TrivyVulnerabilityProviderTests.cs:393` (`first line\n  <line>  \n`), whose last line holds none after the split and trim | confirmed |

## Acceptance criteria

The control characters are the code points of Unicode general category Cc (control), Cf (format), Zl (line
separator) and Zp (paragraph separator), as `System.Text.Rune.GetUnicodeCategory` reports them.

- [ ] AC1: `ResponseBodyExcerpt.Truncate` returns its result with every control character replaced by one U+0020 —
  a code point outside the Basic Multilingual Plane (two UTF-16 code units, e.g. U+E0041) by a single U+0020 — and
  every lone surrogate (U+D800-U+DFFF without its partner) by U+FFFD; every other character, the truncation marker
  `…` included, is kept. Cases, each alone between `a` and `b` (`a`, code point, `b`, `maxLength` 10): U+000A,
  U+000D, U+0009, U+000B, U+000C, U+0000, U+001B, U+007F, U+0085, U+009B, U+2028, U+2029, U+202E, U+2066, U+200B,
  U+FEFF, U+061C, U+E0041 → `a b`; lone U+D800 and lone U+DC00 → `a` U+FFFD `b`. Kept unchanged: U+00A0, U+00E9,
  U+1F600, U+2026, U+0020.
- [ ] AC2: The cut is decided on the text as received, before the replacement: `…` is appended exactly when the
  received text is longer than `maxLength`. Cases: `a` U+E0041 `bc` with `maxLength` 3 → `a …` (the pair is
  kept whole by the existing surrogate rule and replaced after the cut, the marker stays); `a` U+E0041 `b` with
  `maxLength` 4 → `a b` without marker; `x` repeated 499 times plus U+000A with `maxLength` 500 → the 499 `x`
  plus one space, no marker. The existing `Truncate` and `ReadAsync` tests pass unchanged.
- [ ] AC3: `ResponseBodyExcerpt.ReadAsync` applies the same replacement to the body it reads: a body
  `line1` U+000D U+000A `line2` U+001B `[31m` gives `line1  line2 [31m` (two spaces, then one).
- [ ] AC4: A Trivy run that exits 1 with the error output `first line` U+000A `forged` U+000D `entry` U+001B
  `[2K` U+2028 `INFO fake` U+000A returns *failed* with the message
  `Trivy exited with code 1: forged entry [2K INFO fake`, and its one `TrivyCommandFailed` entry (event 3421)
  contains neither U+000D, U+001B nor U+2028. (The split on U+000A and the trim of `GetErrorExcerpt` are unchanged;
  the replacement is applied by `Truncate` at line 268.)
- [ ] AC5: A Docker Engine container discovery answered 400 with the body `{"message":"bad` U+000A
  `request"}` returns *failed* with `Docker instance 'Production' returned 400: {"message":"bad request"}`
  (one space where the line break was).
- [ ] AC6: `GetTagsAsync("mcr.microsoft.com", "mssql/server")` answered 400 with the reason phrase `Server`
  U+001B `Error` and the body `oops` U+2028 `forged` returns *failed* with the message
  `Tag lookup failed for 'mcr.microsoft.com/mssql/server': 400 Server Error - oops forged`.

Every test builds its control characters at run time (`char.ConvertFromUtf32(0x…)`, `((char)0xD800).ToString()`
for a lone surrogate), never as a `\u` escape in a literal, which *Format* rewrites into the character
(`.squad/stack.md`, *Known pitfalls*), and asserts that its input contains the code point it is about
(`Assert.Contains`), so an input that lost it fails instead of passing on clean text.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `ResponseBodyExcerptTruncateWhenTextHasControlCharacterReplacesItWithSpace` (DataRow per code point of AC1, given as `int`), `ResponseBodyExcerptTruncateWhenTextHasLoneSurrogateReplacesItWithReplacementCharacter` (DataRow `0xD800`, `0xDC00`), `ResponseBodyExcerptTruncateWhenTextHasPrintableCharactersKeepsThem` (DataRow per kept code point) | `src/Tests/DockerUpdateGuard.Tests/ResponseBodyExcerptTests.cs` |
| AC2 | `ResponseBodyExcerptTruncateWhenCutTextHasSupplementaryControlCharacterKeepsMarker`, `ResponseBodyExcerptTruncateWhenFittingTextHasSupplementaryControlCharacterAddsNoMarker`, `ResponseBodyExcerptTruncateWhenTextAtLimitEndsWithControlCharacterAddsNoMarker` | `src/Tests/DockerUpdateGuard.Tests/ResponseBodyExcerptTests.cs` |
| AC3 | `ResponseBodyExcerptReadAsyncWhenBodyHasControlCharactersReplacesThemAsync` | `src/Tests/DockerUpdateGuard.Tests/ResponseBodyExcerptTests.cs` |
| AC4 | `TrivyVulnerabilityProviderGetVulnerabilitiesAsyncWhenErrorLineHasControlCharactersReplacesThemAsync` | `src/Tests/DockerUpdateGuard.Tests/TrivyVulnerabilityProviderTests.cs` |
| AC5 | `DockerInstanceClientDiscoverContainersAsyncWhenErrorBodyHasLineBreakReplacesItAsync` | `src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientTests.cs` |
| AC6 | `OciRegistryClientGetTagsAsyncWhenReasonAndBodyHaveControlCharactersReplacesThemAsync` | `src/Tests/DockerUpdateGuard.Tests/OciRegistryClientTests.cs` |

AC5 and AC6 use the existing helpers of their test classes (`DelegateHttpMessageHandler`, `RunOperationAsync` in
`DockerInstanceClientTests`; `DelegateHttpMessageHandler` with a response built like `CreateLongErrorResponse` in
`OciRegistryClientTests`).
AC1's first test fails today for every row; AC2's first and third fail today (the raw text comes back); AC2's second
passes today only for the marker and fails on the space.

## Approach

1. `ResponseBodyExcerpt.Truncate` keeps its signature and its cut (null → empty, fit → whole text, otherwise prefix
   with the surrogate rule plus `TruncationMarker`). The replacement runs on the kept part, after the cut and before
   the marker is appended: the marker decision is made on the received length (AC2), and since every replaced code
   point becomes one UTF-16 code unit, the result is never longer than before, so every bound of #324 holds.
2. New `ResponseBodyExcerpt.ReplaceControlCharacters(string text)`: enumerates `text.EnumerateRunes()`; a rune
   whose `Rune.GetUnicodeCategory` is `Control`, `Format`, `LineSeparator` or `ParagraphSeparator` is written as
   `ControlCharacterReplacement` (U+0020); a lone surrogate arrives as `Rune.ReplacementChar` from the enumerator and
   is written as U+FFFD (fact 7); every other rune is written as it is. When no rune is replaced it returns `text`
   itself (no allocation on the common path). `Truncate` calls it; `ReadAsync` is covered through `Truncate`.
3. No call site changes: the four inputs (*Problem*) already pass `Truncate`, and everything downstream — result
   messages, scan-run errors, stored assessments, log entries, OTLP, the pages — receives the cleaned text.
4. Not changed: whitespace runs are not collapsed (`\r\n` becomes two spaces), Trivy's line selection (split on
   U+000A, trim, last non-empty line), the `no error output` and withheld texts, and the OCI check that omits an
   empty body (`string.IsNullOrWhiteSpace(body)`: a body of only control characters now becomes spaces and is
   omitted like a body of whitespace).

No new user-visible output: messages keep their layout, with spaces in place of the replaced characters.

### Forms the guard must handle

The consumers are plain-text log sinks (the console logger, a terminal, a log shipper that splits on line breaks)
and the browser. The forms that start a line or reorder text there, and the guard's behavior on each:

| Form | Code points | Category | Behavior |
| ---- | ----------- | -------- | -------- |
| C0 line breaks | U+000A, U+000B, U+000C, U+000D | Cc | space |
| Unicode line breaks | U+0085 | Cc | space |
| Unicode line and paragraph separator | U+2028, U+2029 | Zl, Zp | space |
| Terminal escape sequences (CSI, OSC) | U+001B (7-bit), U+009B (8-bit CSI), U+0007 (OSC terminator) | Cc | space (the printable rest such as `[2K` stays, harmless) |
| Other C0/C1 controls, NUL, DEL, tab | U+0000-U+001F, U+007F-U+009F | Cc | space |
| Bidirectional controls | U+061C, U+200E, U+200F, U+202A-U+202E, U+2066-U+2069 | Cf | space |
| Zero-width and invisible characters | U+00AD, U+180E, U+200B-U+200D, U+2060, U+FEFF, U+FFF9-U+FFFB | Cf | space |
| Tag characters and other supplementary format characters | U+E0001, U+E0020-U+E007F, U+1D173-U+1D17A | Cf | one space per code point |
| Broken UTF-16 | lone U+D800-U+DFFF | — | U+FFFD |
| Invalid UTF-8 in a body | — | — | already U+FFFD from the `StreamReader` decode |

Not covered, deliberately: combining marks (Mn, "zalgo" text) and private-use or unassigned code points, which
neither break a line nor reorder text.

## Options considered

Weighed in record 0009, options 16-21: replace with a space in the shared helper after the cut (chosen); remove the
characters; write them as visible `\u` escapes; clean at the sinks (log processor, console formatter, pages);
replace only line breaks; replace before the cut. Also weighed here: **rename `Truncate`** (e.g. to a name that says
"cut and clean") — the name undersells the method once it cleans, but a rename moves twelve call sites and four test
names for no change in behavior; the summary says both, and the new public `ReplaceControlCharacters` names the
second step.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Infrastructure/ResponseBodyExcerpt.cs` | `Truncate` replaces control characters after the cut; new `ReplaceControlCharacters`, new constant `ControlCharacterReplacement`; class summary "Bounds remote-derived text and replaces its control characters before it reaches a failure message" |
| DockerUpdateGuard.Tests | `ResponseBodyExcerptTests.cs`, `TrivyVulnerabilityProviderTests.cs`, `DockerInstanceClientTests.cs`, `OciRegistryClientTests.cs` | new tests (*Tests per criterion*) |

## Signatures (for the Dev's skeleton)

In `internal static class ResponseBodyExcerpt` (`src/DockerUpdateGuard/Infrastructure/ResponseBodyExcerpt.cs`):

- `public const char ControlCharacterReplacement = ' ';` (region `Constants`)
- `public static string ReplaceControlCharacters(string text)` (region `Methods`) — summary "Replace every control,
  format, line or paragraph separator character of a text with a space and every lone surrogate with U+FFFD";
  returns "The text itself when nothing is replaced, otherwise the cleaned copy"
- `public static string Truncate(string? text, int maxLength)` — signature unchanged; summary and `<returns>`
  extended to "…, with its control characters replaced".

The skeleton body of `ReplaceControlCharacters` is the stack's `Skeleton` body; `Truncate` is not touched in the
skeleton (its existing tests must stay green; the new tests fail on the unchanged cut).

## Test files

- `src/Tests/DockerUpdateGuard.Tests/ResponseBodyExcerptTests.cs`
- `src/Tests/DockerUpdateGuard.Tests/TrivyVulnerabilityProviderTests.cs`
- `src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientTests.cs`
- `src/Tests/DockerUpdateGuard.Tests/OciRegistryClientTests.cs`

Existing test code that calls a changed signature: none (no signature changes). Existing tests kept unchanged: the
`Truncate`/`ReadAsync` tests of `ResponseBodyExcerptTests`, the `…BoundedExcerpt…` tests of the five clients, the
Trivy error-line tests and the `PaginationLinkGuardTests` origin tests — their inputs hold no control character
(fact 11).

Files of the change no unit test can cover: none.

## Areas

- `docs/areas/registries.md` — *Failure model of integrations*: excerpts have their control characters replaced;
  *Pagination links*: the origin text is cleaned the same way.
- `docs/areas/vulnerabilities.md` — *Trivy*: the error line is cleaned.
- `docs/areas/telemetry.md` — *Credentials and external data*: remote text in logs is cleaned.

## Documentation updates

Owned by the Dev, written once the code is green, against that code. Found by a sweep of `docs/areas`,
`docs/ARCHITECTURE.md`, `README.md` and `docs/*.md` for `excerpt`, `500 characters`, `300 characters`,
`reason phrase`, `error line`, `bounded`:

- `docs/areas/registries.md` line 28-33 (*Failure model of integrations*): after the sentence on the reason phrase,
  one sentence that in every excerpt — body, reason phrase, rejected origin and Trivy's error line — each control
  character (Unicode categories Cc, Cf, Zl, Zp: line breaks, tabs, escape characters, bidirectional and zero-width
  marks) is replaced by a space after the cut and a broken surrogate by U+FFFD, so a remote text never spans lines
  in a log or reorders a page; this is the one place that states the rule, the others link it.
- `docs/areas/registries.md` line 105 (*Pagination links*): "The origin text is cut to 300 characters followed by
  `…`" gains "and cleaned like an excerpt" with a link to the failure model.
- `docs/areas/vulnerabilities.md` line 105 (*Trivy*): "<last line of the error output cut to 500 characters
  followed by `…`" gains "with control characters replaced" and the link to the failure model.
- `docs/areas/telemetry.md` line 108 (*Credentials and external data*): the list of what logs may carry says the
  bounded excerpt, the rejected origin and Trivy's error messages arrive with control characters replaced
  ([Registries](registries.md#failure-model-of-integrations)).
- `docs/ARCHITECTURE.md`, section 11, bullet *Failure messages carry fixed reasons*: one sentence that remote text
  a message carries (body excerpts, reason phrase, rejected origin, Trivy's line) passes one helper,
  `Infrastructure\ResponseBodyExcerpt`, which bounds it and replaces its control characters, linking record 0009.
- No hit needs a change in `docs/areas/docker-instances.md:176` and `docs/areas/base-images.md:123` (they link the
  failure model) or `docs/areas/vulnerabilities.md:133` (links *Pagination links*).

## Architecture check

No guarantee of `.squad/project.md` is touched: clients still return `ExternalOperationResult<T>` and never throw for
these failures; the helper stays in `src/DockerUpdateGuard/Infrastructure` (host layer, no new dependency); the
fixed-reason model of record 0009 is unchanged and extended. The posture bullet in `docs/ARCHITECTURE.md` (section
11) is updated by the Dev (*Documentation updates*).

## Security considerations

- [x] Limits: the length bounds of #324 apply to UTF-16 code units of the decoded text and are decided before the
  replacement; the replacement maps each code point to one code unit, so it never lengthens a text (a
  supplementary code point shrinks by one). The byte bound of `ReadAsync` (about 8 KiB read) is unchanged.
- [x] Exceptions reaching a user-visible message: none new. `ReplaceControlCharacters` throws for nothing but a null
  argument, which `Truncate` never passes (null is handled before); `Rune` enumeration does not throw on ill-formed
  UTF-16 (fact 7).
- The guard is complete for the texts that pass the helper (fact 2) and for the forms in *Forms the guard must
  handle*. Remote text in structured fields does not pass it (out of scope, follow-up below).

## Decision records

- `docs/decisions/0009-integration-results-carry-fixed-failure-reasons.md` (Accepted, unreleased, extended in place):
  context paragraph for #347, options 16-21, one sentence in *Decision*, one consequence, issue #347 under *Source*.
  Index row unchanged (title and status still hold).

## Out of scope / follow-ups

- Proposed follow-up issue (the orchestrator creates it):
  - Title: `[BUG] Control characters in remote metadata fields reach logs and messages`
  - Body: "#347 replaces control characters in the remote text that passes `ResponseBodyExcerpt` (response body
    excerpts, the OCI reason phrase, rejected-link origins, Trivy's error line). Text the remote side sends in
    structured fields does not pass it and reaches logs and messages unchanged, e.g. the image reference or ID a
    Docker Engine reports, interpolated into `Docker image inspect for '<reference>' returned …`
    (`DockerInstanceClient.cs:875`, from `RuntimeContainerScanOrchestrator.cs:1076-1078`), and likewise container
    names, labels, registry tag names and vulnerability advisory texts. Expected: decide which of these fields can
    carry control characters at all and route those through the shared helper's replacement before they are
    logged, stored or shown. Security area: Logging and telemetry of external data."
- Collapsing whitespace runs, and stripping the printable rest of an escape sequence (`[2K`): not needed for the
  guard.
