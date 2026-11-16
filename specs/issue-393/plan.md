# Plan: Log entries of remote-JSON failures carry no remote text from the parser's message

Source: Issue #393
Tier: security — the change adds output escaping for remote text that reaches the logs (a guard per `.squad/routing.md`, *Tiers*), at every parse of external JSON.
Security areas touched: Logging and telemetry of external data; Failure messages and remote text; Registry and Docker Scout calls (`Infrastructure/RemoteJsonDocument.cs`, `DockerHub/DockerHubAccessTokenCache.cs`); External process execution (Trivy's JSON output); Container actions (`Portainer/PortainerClient.cs`)

## Problem / root cause

`System.Text.Json` builds its parse error messages from the input (verified facts 1–4), and the handlers log the
exception object as it was thrown. The parse sites hand that exception on unchanged:
`Infrastructure/RemoteJsonDocument.cs:29` (Docker Hub, OCI and Docker Engine clients), `DockerHub/DockerHubAccessTokenCache.cs:226`,
`Vulnerabilities/DockerScoutVulnerabilityProvider.cs:299`, `:355`, `:474`, `Portainer/PortainerClient.cs:290`, `:414`, `:632`,
`Images/DotNetReleaseMetadataService.cs:166` and `Vulnerabilities/TrivyVulnerabilityProvider.cs:375`. Every handler that
logs the exception (`DockerHubOperationFailed` 3213, `OciRegistryOperationFailed` 2100, `DockerInstance*Failed`
3104–3110 and `DockerInstanceResourceSampleSkipped`, `PortainerCapabilityException` 3306, `PortainerActionException`
3310, `DockerScoutScanError` 3414, `DockerScoutTokenExpiryUnreadable` 3418, `TrivyScanError` 3423,
`DotNetReleaseMetadataLoadFailed` 2101, and the generic handlers above them) writes the remote text into the log.

## Verified facts

Measurements: scratch console project on SDK 10.0.404, `net10.0`, run by the Lead (step 2), outside the working tree.

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | `{"a":nul` CR LF `FAKE LOG LINE}` gives a message with the CR LF and the forged line | Measured, `JsonDocument.ParseAsync`: `'nul` CR LF `FAKE LOG LINE}' is an invalid JSON literal. … LineNumber: 0 \| BytePositionInLine: 8.`; the serializer gives the same plus `Path: $.a` | confirmed |
| 2 | `{"a":tru` + 100,000 `A` gives a 100,102-character message | Measured: `JsonDocument` message length 100,102 (whole body quoted); the serializer cuts at its buffer (16,489 characters) | confirmed |
| 3 | An ESC byte (`U+001B`) is kept | Measured: `{"a":fals` `U+001B` `[31mRED}` quotes the raw `U+001B`; a control character inside a string or at a value start is printed as `'0x1B'`, not raw | confirmed |
| 4 | Further forms: the serializer's `Path` holds decoded dictionary keys (CR LF kept), and its inner `JsonReaderException` repeats the quote | Measured: `{"k\r\nFAKE LINE":"x"}` into `Dictionary<string,int>` gives `Path: $['k` CR LF `FAKE LINE']`, line 0, byte 21; inner exception type `JsonReaderException` with the quote | confirmed |
| 5 | The issue's `RuntimeContainerScanOrchestrator.cs:703` handler logs the exception it catches | Moved by #493/#495: now `Images/RuntimeContainerScanOrchestrator.cs:820` (`RuntimeContainerProcessingFailed` with the exception); the fix works at the parse sites, so no handler needs to change | confirmed |
| 6 | The Docker Engine, registry (#358) and other handlers log the exception object | `Docker/DockerInstanceClient.cs:1050`, `:1147`, `:1231`, `:1270`, `:1309`, `:1366`; registry wrappers log 3213/2100; `Portainer/PortainerClient.cs:604`, `:730`, `:788`; `DockerScoutVulnerabilityProvider.cs:310`, `:621`; `TrivyVulnerabilityProvider.cs:387`; `DotNetReleaseMetadataService.cs:205` | confirmed |
| 7 | The parse sites listed under *Problem* are all parses of external JSON in production code | `grep` for `JsonDocument.Parse`, `JsonSerializer.Deserialize`, `ReadFromJson`, `JsonNode.Parse`, `Utf8JsonReader` over `src/DockerUpdateGuard*`; no custom `JsonConverter`; the other `JsonSerializer` calls serialize | confirmed |
| 8 | `JsonDocument.Parse` throws `JsonReaderException`, a subclass of `JsonException`; `catch (JsonException)` catches it | Measured (type name printed) | confirmed |
| 9 | A `JsonException` built with `(message, null, line, byte)` has exactly that message, and `ToString()` starts with it | Measured | confirmed |
| 10 | Body `nul` CR LF `FAKE LOG LINE` fails at line 0, byte 3 in `JsonDocument.Parse`/`ParseAsync`, `JsonSerializer.Deserialize` and `ReadFromJsonAsync` (array and object targets) | Measured | confirmed |
| 11 | Results and scan runs are unaffected: `FailureReason.FromException` maps any `JsonException` to `invalid response` | `Infrastructure/FailureReason.cs:78` | confirmed |
| 12 | `HttpContent.ReadFromJsonAsync<T>(CancellationToken)` and `(options: null, CancellationToken)` both use the web defaults; `JsonSerializer.Deserialize<T>(string)` the general defaults | `System.Net.Http.Json` source (`JsonHelpers.s_defaultSerializerOptions`); pinned by AC4 | confirmed |
| 13 | The refusal of an undecodable string carries a fixed message, and its inner `InvalidOperationException` only fixed text with at most the hex of the bad bytes | Measured: `Cannot read incomplete UTF-16 JSON text as string with missing low surrogate.`; `Cannot transcode invalid UTF-8 JSON text to UTF-16 string.` (inner `Unable to translate bytes [C3] at index 0 …`) | confirmed |
| 14 | `integration-failures.md` says release-feed text "never reaches a log" | Refuted today (the release index parse error reaches 2101 with the body); true again after the fix, so the sentence stays | refuted |
| 15 | No call site uses `EnsureSuccessStatusCode` or a throwing `Parse` on remote values, which would put remote text into another exception's message | `grep` over `src/DockerUpdateGuard` (only `int.Parse` of a regex-matched year) | confirmed |

## Acceptance criteria

The fixed message is `Remote JSON is not valid at line <l>, byte <b> (counted from 0)`, with `<l>` and `<b>` the
parser's `LineNumber` and `BytePositionInLine` in invariant digits, or `Remote JSON is not valid` when either is
unknown. "Rewritten" below means: exactly type `JsonException`, that message, `Path` null, `InnerException` null,
`LineNumber`/`BytePositionInLine` kept from the parser's exception.

- AC1: A parse failure from each `ExternalJson` method (`ParseAsync`, `Parse`, `ReadFromJsonAsync`, `Deserialize`) and
  from `RemoteJsonDocument.ParseAsync` is rewritten, for the inputs of facts 1–3 (expected `… line 0, byte 8 …`,
  `… line 0, byte 8 …`, `… line 0, byte 9 …`) and, on the serializer methods, the dictionary key of fact 4
  (`… line 0, byte 21 …`); the message contains no CR, LF or `U+001B`.
- AC2: `ExternalJson.CreateParseFailure` turns a `JsonException` without line or byte position into one with message
  `Remote JSON is not valid`, no path and no inner exception.
- AC3: With the body (or token payload) `nul` CR LF `FAKE LOG LINE`, the exception each of these logs or throws is
  rewritten with message `Remote JSON is not valid at line 0, byte 3 (counted from 0)`: Docker Hub 3213, OCI 2100, Docker
  Engine discovery 3104, Portainer capability check 3306, Docker Scout scan 3414 and token expiry 3418, Trivy 3423,
  .NET release index 2101, and the exception `DockerHubAccessTokenCache.GetOrRequestAsync` throws for such a payload.
- AC4: Valid JSON is read as before: `ReadFromJsonAsync` matches property names case-insensitively (web defaults),
  `Deserialize` does not; `ParseAsync` and `Parse` return the document.
- AC5: The refusal of an undecodable string by `RemoteJsonDocument.ParseAsync` keeps its message and its
  `InvalidOperationException` inner exception (existing test, unchanged).

## Tests per criterion

Inputs with CR, LF or `U+001B` are built at run time (`(char)0x1B`, `"\r\n"` concatenated), never as a `\u` escape in a
literal (*Known pitfalls*, `.squad/stack.md`); each such test asserts first that its input still contains the
character. JSON escapes (fact 4) stay in a raw string literal and the test asserts the backslash is there.

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `ExternalJsonParseAsyncWhenJsonIsMalformedThrowsFixedMessageAsync`, `ExternalJsonParseWhenJsonIsMalformedThrowsFixedMessage`, `ExternalJsonReadFromJsonAsyncWhenJsonIsMalformedThrowsFixedMessageAsync`, `ExternalJsonDeserializeWhenJsonIsMalformedThrowsFixedMessage` (each a data row per input) | `src/Tests/DockerUpdateGuard.Tests/ExternalJsonTests.cs` |
| AC1 | `RemoteJsonDocumentParseAsyncWhenJsonIsMalformedThrowsFixedMessageAsync` | `src/Tests/DockerUpdateGuard.Tests/RemoteJsonDocumentTests.cs` |
| AC2 | `ExternalJsonCreateParseFailureWithoutPositionReturnsMessageWithoutPosition` | `src/Tests/DockerUpdateGuard.Tests/ExternalJsonTests.cs` |
| AC3 | `DockerHubClientGetTagAsyncWhenBodyIsMalformedLogsFixedJsonMessageAsync` | `src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs` |
| AC3 | `OciRegistryClientGetTagsAsyncWhenBodyIsMalformedLogsFixedJsonMessageAsync` | `src/Tests/DockerUpdateGuard.Tests/OciRegistryClientTests.cs` |
| AC3 | `DockerInstanceClientDiscoverContainersAsyncWhenBodyIsMalformedLogsFixedJsonMessageAsync` | `src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientTests.cs` |
| AC3 | `PortainerClientCheckCapabilityAsyncWhenLoginBodyIsMalformedLogsFixedJsonMessageAsync` | `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs` |
| AC3 | `DockerScoutVulnerabilityProviderGetVulnerabilitiesAsyncWhenScanBodyIsMalformedLogsFixedJsonMessageAsync` | `src/Tests/DockerUpdateGuard.Tests/DockerScoutVulnerabilityProviderTests.cs` |
| AC3 | `DockerScoutTokenExpiryMalformedPayloadLogsFixedJsonMessage` | `src/Tests/DockerUpdateGuard.Tests/DockerScoutTokenExpiryTests.cs` |
| AC3 | `TrivyVulnerabilityProviderGetVulnerabilitiesAsyncWhenReportIsMalformedLogsFixedJsonMessageAsync` | `src/Tests/DockerUpdateGuard.Tests/TrivyVulnerabilityProviderTests.cs` |
| AC3 | `DotNetReleaseMetadataServiceGetChannelReleaseAsyncWhenIndexIsMalformedLogsFixedJsonMessageAsync` | `src/Tests/DockerUpdateGuard.Tests/DotNetReleaseMetadataServiceTests.cs` |
| AC3 | `DockerHubAccessTokenCacheGetOrRequestAsyncWhenPayloadIsMalformedThrowsFixedJsonMessageAsync` | `src/Tests/DockerUpdateGuard.Tests/DockerHubAccessTokenCacheTests.cs` |
| AC4 | `ExternalJsonReadFromJsonAsyncWhenNamesDifferInCaseReadsValueAsync`, `ExternalJsonDeserializeWhenNamesDifferInCaseLeavesValueUnset`, `ExternalJsonParseAsyncWhenJsonIsValidReturnsDocumentAsync`, `ExternalJsonParseWhenJsonIsValidReturnsDocument` | `src/Tests/DockerUpdateGuard.Tests/ExternalJsonTests.cs` |
| AC5 | `RemoteJsonDocumentParseAsyncWhenAStringDoesNotDecodeThrowsJsonExceptionAsync` (exists, unchanged) | `src/Tests/DockerUpdateGuard.Tests/RemoteJsonDocumentTests.cs` |

## Approach

- New `Infrastructure/ExternalJson.cs`, an internal static class: each method calls the parser exactly as the call
  site does today (`JsonDocument.ParseAsync(stream, cancellationToken: …)`, `JsonDocument.Parse(bytes)`,
  `content.ReadFromJsonAsync<T>(cancellationToken)`, `JsonSerializer.Deserialize<T>(json)`, no new options) inside
  `try`/`catch (JsonException exception)` that throws `CreateParseFailure(exception)`. Only `JsonException` is caught;
  cancellation, I/O and every other exception pass unchanged. `CreateParseFailure` builds the message of the criteria
  (a private constant for each text, `string.Create(CultureInfo.InvariantCulture, …)`) and returns
  `new JsonException(message, null, exception.LineNumber, exception.BytePositionInLine)`.
- `RemoteJsonDocument.ParseAsync` calls `ExternalJson.ParseAsync` and keeps its string check and its refusal unchanged
  (AC5); its class summary says parse failures carry no remote text.
- Call sites: `DockerHubAccessTokenCache.cs:226` and `DockerScoutVulnerabilityProvider.cs:299` use `ExternalJson.Parse`;
  `DockerScoutVulnerabilityProvider.cs:355`, `:474` and `PortainerClient.cs:290`, `:414`, `:632` use
  `ExternalJson.ReadFromJsonAsync<T>(content, cancellationToken)`; `TrivyVulnerabilityProvider.cs:375` uses
  `ExternalJson.Deserialize<TrivyReport>`; `DotNetReleaseMetadataService.cs:166` uses `ExternalJson.ParseAsync` (no string
  check there: 0019 leaves the release feeds out). A `using System.Net.Http.Json;` left unused goes.
- No handler and no log event changes. No new user-visible output.

## Options considered

See [0045](../../docs/decisions/0045-json-parse-failures-of-external-data-carry-a-fixed-message-with-their-position.md):
cleaning at each of the ~30 log events (one missed event leaks), cutting and cleaning the parser's message with the
excerpt rule (still 500 characters of remote text, path and inner exception left over), and cleaning every exception in
the logging pipeline (an exception's message cannot be changed after it is thrown; dropping it loses all stack traces)
lost to the rewrite at the parse sites. Adding the undecodable-string check to the token payloads and the release index
was not taken: it changes what they accept, which is not this issue.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Infrastructure/ExternalJson.cs` | new |
| DockerUpdateGuard | `Infrastructure/RemoteJsonDocument.cs` | parse through `ExternalJson`, summary |
| DockerUpdateGuard | `DockerHub/DockerHubAccessTokenCache.cs`, `Vulnerabilities/DockerScoutVulnerabilityProvider.cs`, `Portainer/PortainerClient.cs`, `Vulnerabilities/TrivyVulnerabilityProvider.cs`, `Images/DotNetReleaseMetadataService.cs` | call sites |

## Signatures (for the Dev's skeleton)

New `internal static class ExternalJson` in `src/DockerUpdateGuard/Infrastructure/ExternalJson.cs` (namespace
`DockerUpdateGuard.Infrastructure`), region `Static methods`:

- `public static async Task<JsonDocument> ParseAsync(Stream stream, CancellationToken cancellationToken)`
- `public static JsonDocument Parse(byte[] utf8Json)`
- `public static async Task<T?> ReadFromJsonAsync<T>(HttpContent content, CancellationToken cancellationToken)`
- `public static T? Deserialize<T>(string json)`
- `public static JsonException CreateParseFailure(JsonException exception)`

Names checked against *Writing code*: no keyword parameter names, at most two parameters, no nested type. No existing
signature changes.

## Test files

`src/Tests/DockerUpdateGuard.Tests/ExternalJsonTests.cs` (new) and the existing files of *Tests per criterion*.

Existing test code that calls a changed signature: none. `RemoteJsonDocumentParseAsyncWhenJsonIsMalformedThrowsJsonExceptionAsync`
stays valid (it asserts `ThrowsAsync<JsonException>`); the Tester leaves it as it is, the new AC1 test holds the
message.

Files of the change no unit test can cover: none.

## Areas

- Telemetry, *Credentials and external data*: the parse-failure exception and its message.
- Integration failures, *Fixed failure reasons*: a pointer from "the error itself is logged".

## Documentation updates

All by the Dev, against the code as built:

- `docs/areas/telemetry.md`, *Credentials and external data*: after "the exception with its message, which can name a
  registry, Docker or database host", state that a JSON parse failure of a remote answer, a token payload, the .NET
  release index or Trivy's report is logged with the fixed message of AC1/AC2 (exact text), no path and no inner
  exception, and so carries no remote text; add 0045 under *Related decisions*.
- `docs/areas/integration-failures.md`, *Fixed failure reasons*, first bullet: "The error itself is logged with the
  failure" gets "(a JSON parse failure with a fixed message, [Telemetry](telemetry.md#credentials-and-external-data))".
- `docs/ARCHITECTURE.md`, §11, *Failure messages carry fixed reasons*: one sentence that external JSON is parsed through
  `Infrastructure\ExternalJson` (beneath `RemoteJsonDocument`), whose parse failures carry a fixed message with the
  position only, linking [record 0045](decisions/0045-json-parse-failures-of-external-data-carry-a-fixed-message-with-their-position.md).
- `.squad/project.md` (the Dev reports the exact text, the orchestrator writes it): `Infrastructure/ExternalJson.cs`
  added to the files of *Failure messages and remote text*; under *Integration surface*, *A new integration client or
  external call*, the bullet "remote text through `ResponseBodyExcerpt` …" gains "and remote JSON through `ExternalJson`
  (`RemoteJsonDocument` where strings must decode)".

Sweep: `docs/areas/*.md`, `README.md` and `docs/ARCHITECTURE.md` for "exception", "logged with" and "error message";
no other statement says what a logged parse failure contains (`container-actions.md` 251 and `docker-instances.md` 203
stay true). No changelog in this repository.

## Architecture check

No guarantee of `.squad/project.md` is weakened: results keep the fixed reason `invalid response` (fact 11), the log
loses only remote text. Layering unchanged (the helper is in the host's `Infrastructure`).

## Security considerations

- [x] No limits: no byte or length limit is added or changed; the message length is fixed apart from two numbers.
- [x] Exceptions reaching a user-visible failure reason or message: the rewritten `JsonException`, which still maps to
  `invalid response` (fact 11); no message text reaches a user.
- Guard forms (fact 1–4, 13): the quoted literal remainder, single-character quotes, the serializer path, the inner
  `JsonReaderException` — all replaced, since the rewrite drops the message, path and inner exception whatever the
  form. Not covered by design: the undecodable-string refusal (fixed text, fact 13) and non-JSON exceptions (fact 15).

## Decision records

- `docs/decisions/0045-json-parse-failures-of-external-data-carry-a-fixed-message-with-their-position.md` (Proposed)

## Out of scope / follow-ups

- None.