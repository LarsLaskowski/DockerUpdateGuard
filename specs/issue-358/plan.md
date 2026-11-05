# Plan: Undecodable JSON strings from registries and Docker Engines become an "invalid response"

Source: Issue #358
Status: Draft
Tier: standard — changes how the Docker Hub, OCI and Docker Engine clients answer a remote JSON body they cannot read (a fixed *failed* result instead of an escaping error or "unexpected error"); a parser's validation is input handling, not a security control (`.squad/routing.md`, *Tiers*).
Security areas touched: Registry and Docker Hub calls (`DockerHubClient`, `OciRegistryClient`: untrusted JSON); Docker Engine access (`DockerInstanceClient`: untrusted JSON); Logging and telemetry of external data (two new warning log entries that carry a remote-derived target name and the exception)

## Problem / root cause

`System.Text.Json` accepts a JSON string that does not decode as Unicode text when it parses a document and only
fails when the string is read: `JsonElement.GetString()`, `JsonProperty.Name`, `ValueEquals` and
`TryGetDateTimeOffset` throw `InvalidOperationException` for a lone surrogate escape (`\ud800`, `\udc00`, a reversed
pair) and for bytes that are not UTF-8 (fact 1–3). The three clients parse every body with `JsonDocument.ParseAsync`
and read strings afterwards, so:

- the five Docker Hub calls `GetCurrentUserAsync`, `GetRepositoriesAsync`, `GetRepositoryAsync`, `GetTagAsync`,
  `GetTagsAsync` (`src/DockerUpdateGuard/DockerHub/DockerHubClient.cs:1187-1521`) and the three OCI calls `GetTagAsync`,
  `GetTagsAsync`, `GetImageConfigurationAsync` (`src/DockerUpdateGuard/Images/OciRegistryClient.cs:1329-1559`) have no
  handler at all: the error escapes the client, exactly as a malformed body (`JsonException`) does today;
- the calls that do catch — every public Docker Engine call (`DockerInstanceClient.cs:1068`, `:1157`, `:1196`,
  `:1235`, `:1292`), `DockerHubClient.ResolveBaseImagesAsync` (`:1548`) and `GetImageConfigurationAsync` (`:1614`),
  `OciRegistryClient.ResolveBaseImagesAsync` (`:1483`) — pass it to `FailureReason.FromException`
  (`src/DockerUpdateGuard/Infrastructure/FailureReason.cs:51-88`), which maps only `JsonException` to
  `invalid response`, so the undecodable body reads `unexpected error`.

The control-character replacement (`ResponseBodyExcerpt.ReplaceControlCharacters`) receives the result of
`GetString()` and never runs.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | `JsonDocument.Parse` / `ParseAsync` accept a string with a lone surrogate escape (`\ud800`, upper-case `\uD800`, `\udc00`, `\ud800\ud800`, `\ud800a`, `\udc00\ud800`); `GetString()`, `JsonProperty.Name`, `ValueEquals`, `TryGetDateTimeOffset`, `ToString()` then throw `InvalidOperationException`; `TryGetProperty` and `NameEquals` do not throw; a valid pair `\ud83d\ude00` and `\u001b` decode | Scratch file app on .NET 10.0.14 (`dotnet run m.cs`, `m3.cs`, `m4.cs` in the Lead's scratchpad), each case printed | confirmed |
| 2 | Raw bytes that are not UTF-8 inside a string (`0xFF`, overlong `0xC0 0xAF`, truncated `0xE2 0x82`, encoded surrogate `0xED 0xA0 0x80`, `0xF4 0x90 0x80 0x80`) also parse and make `GetString()` throw `InvalidOperationException` (inner `DecoderFallbackException`); a raw 4-byte emoji decodes | Same scratch measurement | confirmed |
| 3 | `JsonSerializer.Deserialize` turns the same input into `JsonException` (inner `InvalidOperationException`), so Trivy, Docker Scout and Portainer, which deserialize, already report `invalid response` | Scratch `m2.cs`; `TrivyVulnerabilityProvider.cs:372`, `DockerScoutVulnerabilityProvider.cs:265`, `:369`, `PortainerClient.cs:226`, `:262`, `:321` | confirmed |
| 4 | Issue claim: `GetString()` throws `InvalidOperationException` and `Deserialize` `JsonException` before any control-character replacement | Facts 1, 3; e.g. `DockerHubClient.cs:924`, `:936`, `DockerInstanceClient.cs:608` call `ReplaceControlCharacters(… GetString())` | confirmed |
| 5 | Issue claim: `GetRepositoriesAsync` has no catch around parsing and the error throws out of the client, as malformed JSON does | `DockerHubClient.cs:1233-1308`: no `try`; the same holds for the other four Docker Hub calls and the three OCI calls named above | confirmed |
| 6 | `FailureReason.FromException` maps `InvalidOperationException` to `unexpected error` | `FailureReason.cs:51-88` | confirmed |
| 7 | Callers of the uncaught calls: `RuntimeContainerScanOrchestrator.cs:692` turns the error into `Container '<name>' could not be processed: <reason>`, `ImageScanOrchestrator.cs:764` into `Observed image scan failed: <reason>`; `DockerHubAccountImageDiscoveryService.cs:182`, `:277` have no handler, so one unreadable tag page aborts the whole account discovery run | Code read | confirmed |
| 8 | An OCI tag whose own lookup answers anything but *succeeded* is listed by name only (`AppendTagResult`, `OciRegistryClient.cs:864-883`; called per tag from `AppendInspectedTagsAsync`, `:1280-1316`) | Code read | confirmed |
| 9 | `JsonDocument` limits nesting to 64 levels by default, so a recursive walk of a parsed document is bounded | Scratch `m4.cs`: 65 nested arrays throw a `JsonException` subclass at parse | confirmed |
| 10 | RFC 7493 (I-JSON) §2.1 forbids strings and member names that contain surrogates not part of a pair or invalid UTF-8; RFC 8259 §8.2 calls their behavior unpredictable | RFC text | confirmed |
| 11 | Event ids 3213 (`DockerHubClientLogging`, last 3212) and 2100 (`ImageHostLoggingExtensions`, last 2099) are unused | `grep -rn "EventId = 3213\|EventId = 2100" src` returns nothing | confirmed |
| 12 | Record 0009 is released, so it is amended by a new record, not edited | `git tag --contains` of its adding commit lists `v1.3.8` | confirmed |
| 13 | Issue #272 (same milestone) owns transport errors, timeouts and JSON of an unexpected shape (`InvalidOperationException` from a wrong `ValueKind`, `FormatException` of the Docker Hub JWT) escaping the same Docker Hub and OCI calls | `gh api …/issues/272` | confirmed |

## Acceptance criteria

The texts below are compared exactly; `<…>` is the value the call was given (`imageReference.FullReference`,
`imageReference.Repository`, the account name, the instance's `Name`), `<registry>` the normalized OCI registry.

- [ ] AC1: `RemoteJsonDocument.ParseAsync` returns the parsed document when every string value and every property
  name at every depth decodes — including the escape `\ud83d\ude00`, `\u001b`, a raw 4-byte UTF-8 character and an
  empty object — and throws `JsonException` (and disposes the document) when any one does not: a lone high surrogate
  escape `\ud800` and upper-case `\uD800`, a lone low surrogate escape `\udc00`, the reversed pair `\udc00\ud800`, a
  high surrogate escape followed by a plain character `\ud800a`, the same escape in a property name, in an array
  element and three levels deep, the raw bytes `0xFF` and `0xED 0xA0 0x80` in a string; malformed JSON still throws
  `JsonException`.
- [ ] AC2: When the success body of the request holds a string that does not decode (a lone surrogate escape) or is
  malformed JSON, the Docker Hub calls answer *failed* instead of throwing, with
  `Docker Hub current user lookup failed: invalid response` (`GetCurrentUserAsync`),
  `Docker Hub repository listing failed for '<account>': invalid response` (`GetRepositoriesAsync`, also when only the
  second page is affected), `Repository metadata lookup failed for '<repository>': invalid response`
  (`GetRepositoryAsync`), `Tag metadata lookup failed for '<reference>': invalid response` (`GetTagAsync`, also when
  the body of the Docker Hub login is affected) and `Tag lookup failed for '<repository>': invalid response`
  (`GetTagsAsync`); each logs one warning with event id 3213 that carries the exception.
- [ ] AC3: Under the same conditions the OCI calls answer *failed* with
  `Registry tag lookup failed for '<reference>': invalid response` (`GetTagAsync`, also when the registry's token
  body is affected), `Tag lookup failed for '<registry>/<repository>': invalid response` (`GetTagsAsync`, the tag
  listing body) and `Registry image configuration lookup failed for '<reference>': invalid response`
  (`GetImageConfigurationAsync`, the configuration blob); each logs one warning with event id 2100 that carries the
  exception. A tag listing in which only one listed tag's manifest is affected succeeds and lists that tag by name
  only.
- [ ] AC4: A configuration blob with a lone surrogate escape in a label value makes `ResolveBaseImagesAsync` of both
  registry clients answer `Base image chain resolution failed for '<reference>': invalid response` and
  `DockerHubClient.GetImageConfigurationAsync` answer `Image configuration lookup failed for '<reference>': invalid
  response` (today `unexpected error`).
- [ ] AC5: A lone surrogate escape in the Engine's answer makes the Docker Engine calls answer *failed* with
  `invalid response` (today `unexpected error`): `Docker container discovery failed for '<instance>': invalid
  response` (container list), `Docker container resource sampling failed for '<instance>': invalid response` (a
  container's stats body), `Docker image inspect failed for '<instance>': invalid response`, `Docker image history
  failed for '<instance>': invalid response` and `Docker host info request failed for '<instance>': invalid response`.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `RemoteJsonDocumentParseAsyncWhenEveryStringDecodesReturnsDocumentAsync` (data rows: pair escape, `\u001b`, raw emoji bytes, `{}`); `RemoteJsonDocumentParseAsyncWhenAStringDoesNotDecodeThrowsJsonExceptionAsync` (one data row per undecodable form of AC1, raw-byte rows built from byte arrays); `RemoteJsonDocumentParseAsyncWhenJsonIsMalformedThrowsJsonExceptionAsync` | `src/Tests/DockerUpdateGuard.Tests/RemoteJsonDocumentTests.cs` (new) |
| AC2 | `DockerHubClientGetCurrentUserAsyncWhenBodyHasLoneSurrogateReturnsInvalidResponseAsync`, `DockerHubClientGetRepositoriesAsyncWhenSecondPageHasLoneSurrogateReturnsInvalidResponseAsync`, `DockerHubClientGetRepositoriesAsyncWhenBodyIsMalformedReturnsInvalidResponseAsync`, `DockerHubClientGetRepositoryAsyncWhenDescriptionHasLoneSurrogateReturnsInvalidResponseAsync`, `DockerHubClientGetTagAsyncWhenBodyHasLoneSurrogateReturnsInvalidResponseAsync`, `DockerHubClientGetTagAsyncWhenLoginBodyHasLoneSurrogateReturnsInvalidResponseAsync`, `DockerHubClientGetTagsAsyncWhenBodyHasLoneSurrogateReturnsInvalidResponseAsync` (each asserts status, exact message and the logged warning with its exception) | `src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs` |
| AC3 | `OciRegistryClientGetTagAsyncWhenManifestHasLoneSurrogateReturnsInvalidResponseAsync`, `OciRegistryClientGetTagAsyncWhenTokenBodyHasLoneSurrogateReturnsInvalidResponseAsync`, `OciRegistryClientGetTagsAsyncWhenTagListHasLoneSurrogateReturnsInvalidResponseAsync`, `OciRegistryClientGetTagsAsyncWhenOneManifestHasLoneSurrogateListsThatTagByNameAsync`, `OciRegistryClientGetImageConfigurationAsyncWhenConfigHasLoneSurrogateReturnsInvalidResponseAsync` | `src/Tests/DockerUpdateGuard.Tests/OciRegistryClientTests.cs` |
| AC4 | `DockerHubClientResolveBaseImagesAsyncWhenConfigHasLoneSurrogateReturnsInvalidResponseAsync`, `DockerHubClientGetImageConfigurationAsyncWhenConfigHasLoneSurrogateReturnsInvalidResponseAsync`; `OciRegistryClientResolveBaseImagesAsyncWhenConfigHasLoneSurrogateReturnsInvalidResponseAsync` | `DockerHubClientTests.cs`; `OciRegistryClientTests.cs` |
| AC5 | `DockerInstanceClientDiscoverContainersAsyncWhenBodyHasLoneSurrogateReturnsInvalidResponseAsync`, `DockerInstanceClientCollectContainerResourceUsageAsyncWhenStatsBodyHasLoneSurrogateReturnsInvalidResponseAsync`, `DockerInstanceClientInspectImageAsyncWhenBodyHasLoneSurrogateReturnsInvalidResponseAsync`, `DockerInstanceClientGetImageHistoryAsyncWhenBodyHasLoneSurrogateReturnsInvalidResponseAsync`, `DockerInstanceClientGetHostMemoryTotalAsyncWhenBodyHasLoneSurrogateReturnsInvalidResponseAsync` | `src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientTests.cs` |

Every fixture takes an existing well-formed body of the same test class and puts the escape into a field the call
reads (a lone surrogate in a field it never reads must fail the same way, which AC1 covers). Escapes are written in
C# raw string literals as `PortainerClientTests.cs:1839` does, and each test asserts that its input still contains
the backslash of the escape (`.squad/team.md`, *Escapes in files*); after *Format* the Tester checks them with
`grep -n '\\u' <file>`. No test pins the behavior #272 will change (transport errors, timeouts, wrong value types).

## Approach

1. New `src/DockerUpdateGuard/Infrastructure/RemoteJsonDocument.cs`: `ParseAsync` parses with
   `JsonDocument.ParseAsync` (default options), then walks the document once — every property name (`JsonProperty.Name`)
   and every string value (`GetString()`) of every object and array, recursively (depth bounded by fact 9), the
   decoded strings discarded. An `InvalidOperationException` from that walk disposes the document and becomes
   `throw new JsonException(UndecodableStringMessage, exception)`; the message is fixed text, never shown to a user
   (`FailureReason` maps the type).
2. Every `JsonDocument.ParseAsync` call in `DockerHubClient.cs` (11), `OciRegistryClient.cs` (5) and
   `DockerInstanceClient.cs` (6) becomes `RemoteJsonDocument.ParseAsync(stream, token)` with the same token argument.
   The Reviewer checks that `grep -n "JsonDocument.ParseAsync"` over the three files finds nothing. The Docker Hub
   JWT payload parse (`ResolveTokenExpiry`, `JsonDocument.Parse` of bytes, reads only an integer) stays.
3. The five Docker Hub and three OCI calls of AC2/AC3 get one `try` that starts after their existing guards
   (argument checks, unsupported registry, invalid path or tag) and ends with the method, and one
   `catch (JsonException exception)` that returns `CreateInvalidResponseResult<T>(exception, operationName,
   targetName, failureMessage)`: the helper logs the new warning and returns
   `ExternalOperationResult<T>.Failed($"{failureMessage}: {FailureReason.InvalidResponse}")`. Operation and target
   names follow the existing `DockerHubRequestFailed` calls of each method (`current-user`, the account, the
   repository, the full reference); for OCI the target is `<registry>/<repository>` for the listing and the full
   reference otherwise. No other exception type is caught there — that is #272.
4. The calls that already catch (AC4, AC5) need no change of their own: the `JsonException` from step 1 maps to
   `invalid response` through `FailureReason`.

User-visible output: only the failure messages fixed in AC2–AC5, in the existing `<prefix>: <reason>` layout.

## Options considered

1. **Chosen: validate every string once where the body is parsed and raise `JsonException`; catch `JsonException`
   in the eight registry calls without a handler.** One place decides that an undecodable body is a parse failure,
   and the existing fixed reason `invalid response` follows everywhere.
2. **Map `InvalidOperationException` to `invalid response` in `FailureReason`** — the runtime throws it for unrelated
   misuse too ([0009](../../docs/decisions/0009-integration-results-carry-fixed-failure-reasons.md), option 4), and it
   does nothing for the calls without a handler.
3. **Convert `InvalidOperationException` to `JsonException` around each read** — would also cover wrong value types,
   but the read blocks contain further HTTP calls (manifest lists, stats, the inspect of each image), so a boundary
   there relabels non-JSON errors; about 25 sites to wrap, and wrong value types are #272's.
4. **Read tolerantly: treat an undecodable string as absent or as U+FFFD** — keeps the rest of the body, but needs an
   own JSON string decoder or a replacement for every accessor (label names included); one missed accessor throws
   again, and such a body is not interoperable JSON (fact 10).
5. **Catch every exception in the eight calls now** — that is #272, which carries the Product Manager's decision on
   timeouts and messages; doing it here pre-empts its design.
6. **Only the parse helper, leave the handlers to #272** — the defect of the title (an error escaping the client)
   would stay.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Infrastructure/RemoteJsonDocument.cs` (new) | parse and validate |
| DockerUpdateGuard | `DockerHub/DockerHubClient.cs` | parse helper at 11 sites; `try`/`catch (JsonException)` in 5 calls; new helper |
| DockerUpdateGuard | `DockerHub/DockerHubClientLogging.cs` | new log method 3213 |
| DockerUpdateGuard | `Images/OciRegistryClient.cs` | parse helper at 5 sites; `try`/`catch (JsonException)` in 3 calls; new helper |
| DockerUpdateGuard | `Images/ImageHostLoggingExtensions.cs` | new log method 2100 |
| DockerUpdateGuard | `Docker/DockerInstanceClient.cs` | parse helper at 6 sites |

## Signatures (for the Dev's skeleton)

- `internal static class RemoteJsonDocument` with
  `private const string UndecodableStringMessage = "A JSON string of the remote response does not decode as Unicode text";`,
  `public static async Task<JsonDocument> ParseAsync(Stream stream, CancellationToken cancellationToken)` and
  `private static void EnsureStringsDecode(JsonElement element)`.
- `DockerHubClient`: `private ExternalOperationResult<T> CreateInvalidResponseResult<T>(JsonException exception, string operationName, string targetName, string failureMessage)`.
- `OciRegistryClient`: `private ExternalOperationResult<T> CreateInvalidResponseResult<T>(JsonException exception, string operationName, string targetName, string failureMessage)`.
- `DockerHubClientLogging`: `[LoggerMessage(EventId = 3213, Level = LogLevel.Warning, Message = "Docker Hub {OperationName} response for {TargetName} could not be read as JSON")] public static partial void DockerHubResponseInvalid(this ILogger logger, Exception exception, string operationName, string targetName);`
- `ImageHostLoggingExtensions`: `[LoggerMessage(EventId = 2100, Level = LogLevel.Warning, Message = "Registry {OperationName} response for {TargetName} could not be read as JSON")] public static partial void OciRegistryResponseInvalid(this ILogger logger, Exception exception, string operationName, string targetName);`

No public signature changes. The skeleton rewrites no existing file beyond adding these members.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/RemoteJsonDocumentTests.cs` (new)
- `src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs`
- `src/Tests/DockerUpdateGuard.Tests/OciRegistryClientTests.cs`
- `src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientTests.cs`

Existing test call sites of a changed signature: none. Existing tests kept unchanged: no existing fixture holds a lone
surrogate escape or bytes that are not UTF-8 (`grep` over `src/Tests` finds only the valid pair at
`PortainerClientTests.cs:1839`, which Portainer deserializes), and none expects an error from a malformed body.

Files no unit test can cover: none.

## Areas

- `docs/areas/registries.md` (*Failure model of integrations*, *Remote text fields*, *Limits and errors*, *Known gaps*):
  an undecodable JSON string is an invalid response; the registry calls of AC2/AC3 answer it and malformed JSON as
  *failed*.
- `docs/areas/docker-instances.md`: no change; its *Limits and errors* refers to the shared failure model.

## Documentation updates

All by the Dev, once the code is green:

- `docs/areas/registries.md`, *Failure model of integrations*: `invalid response` covers malformed JSON **and** a body
  in which any string value or property name does not decode as Unicode text (a lone surrogate escape such as
  `\ud800`, or bytes that are not UTF-8) — the whole body is refused, whatever field holds it.
- Same file, *Remote text fields*: the clause "a lone surrogate becomes U+FFFD" no longer describes JSON fields; say
  that a JSON answer with such a string is an invalid response instead (the excerpt rule of the previous subsection
  stays).
- Same file, *Limits and errors*: the bullet "Some failures still surface as errors …" loses "malformed JSON"; it
  states that the Docker Hub current-user, account listing, repository, tag and tag-listing calls and the OCI tag,
  tag-listing and configuration calls answer an unreadable body as *failed* with `invalid response`, and that a
  transport error, a timeout or a JSON value of the wrong type still surfaces as an error there (#272). *Known gaps*,
  #272 entry: "malformed JSON" becomes "JSON values of an unexpected type".
- Same file, *Related decisions*: add record 0019.
- Sweep done: `docs/ARCHITECTURE.md` (*Failure messages carry fixed reasons*, line 421) names no reason list and stays;
  `docs/areas/docker-instances.md:183-187` refers to the shared model; no other area document names `invalid
  response` except `container-actions.md:122` (Portainer, unchanged).

## Architecture check

The guarantee "integration clients never throw for expected failures" (`.squad/project.md`) is strengthened for the
eight registry calls and unchanged elsewhere; the outcome model and the orchestrators' branching stay as they are.
Fixed failure reasons (record 0009) keep their set; no new reason. Layering unchanged: the helper lives in the host's
`Infrastructure` next to `FailureReason` and `ResponseBodyExcerpt`.

## Security considerations

- [x] No limits: the walk is bounded by the document `JsonDocument` already built and its 64-level depth; no byte or
  length limit is added or changed.
- [x] Exceptions that can reach a user-visible message: the new `JsonException` and a malformed body's
  `JsonException` reach results only as `invalid response` (through `CreateInvalidResponseResult` or
  `FailureReason`); their messages are only logged with the exception, as record 0009 does for every failure. The
  log entries' target names are values the call was given (configuration, a parsed reference, a base-image label
  value already cleaned by #353).

## Decision records

- `docs/decisions/0019-remote-json-with-undecodable-strings-is-an-invalid-response.md` (Proposed; amends 0009).

## Out of scope / follow-ups

- #272 keeps transport errors, timeouts, JSON values of an unexpected type and the Docker Hub JWT's `FormatException`
  in the eight registry calls; the orchestrator comments on #272 that malformed and undecodable JSON are handled by
  #358.
- Follow-up issue (proposed): **"[BUG] Image labels that differ only in case fail the image configuration lookup"** —
  `DockerHubClient.cs:933-937` and `OciRegistryClient.cs:630-634` build the labels with `ToDictionary(…,
  StringComparer.OrdinalIgnoreCase)`, which throws `ArgumentException` for two labels whose names differ only in case
  (Docker labels are case-sensitive) or a repeated name; the Docker Hub lookup then answers `unexpected error`, the
  OCI lookup throws out of the client. Expected: decide which value wins and keep the lookup succeeding.
- Follow-up issue (proposed): **"[BUG] Docker Engine answers of an unexpected JSON shape read 'unexpected error'"** —
  `DockerInstanceClient.cs:579-580`, `:806-819`, `:866-867` call `GetString()` on array items without checking their
  kind, and `:967`, `:1038`, `:1115` enumerate the root as an array; a wrong type gives `unexpected error`. #272
  covers only the registry clients.
- The release feeds (`DotNetReleaseMetadataService`) report an undecodable body as `unexpected error`; they are no
  registry or Engine client and their text reaches no message (`docs/areas/registries.md`, *Remote text fields*).
