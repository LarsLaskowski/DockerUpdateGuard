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

## Challenge notes

Devil's Advocate, plan mode, issue #393 (`/home/user/DockerUpdateGuard/specs/issue-393/plan.md`)

I checked the plan's main claims and they hold:
- **Parse sites:** a grep for `JsonDocument.Parse`, `Deserialize`, `ReadFromJson` and `Utf8JsonReader` over `src/DockerUpdateGuard*` finds exactly the plan's sites. `RemoteJsonDocument.cs:29` covers the DockerHub, OCI and DockerInstance calls, and the other ten sites are the ones the plan lists.
- **Mapping:** `FailureReason.cs:78` maps any `JsonException` to `invalid response` (fact 11).
- **Token cache:** `DockerHubAccessTokenCache.GetOrRequestAsync` (`:132`–`:177`) has no catch around `ResolveTokenExpiry`, so the AC3 "throws" wording is right.
- **Tests:** the AC3 test names match real public methods, and the existing test files exist.
- **Throwing parses (fact 15, part one):** a grep for throwing `X.Parse` and `new Uri` calls on remote values finds only config values and the regex-matched `int.Parse` (`VersionTagResolutionHelper.cs:343`).
- **Approach:** rewriting at the parse site is the least invasive option, and the scope matches the issue.

One objection.

1. **major** — Fact 15, and the "non-JSON exceptions are not covered by design" line under *Security considerations*, are refuted for the helper this plan builds. `ExternalJson.ReadFromJsonAsync` (`HttpContent.ReadFromJsonAsync`, used at `PortainerClient.cs:290`, `:414`, `:632` and `DockerScoutVulnerabilityProvider.cs:355`, `:474`) reads the response's `Content-Type` charset before parsing. When the charset is unknown it throws `InvalidOperationException("The character set provided in ContentType is invalid.")`, and the inner `ArgumentException` quotes the remote header value raw.
   - **What I ran:** a scratch console on SDK 10.0.404, `net10.0`, with `CharSet = "evil" + ESC + "[31mX"`. The inner message was `'evil<ESC>[31mX' is not a supported encoding name. … (Parameter 'name')`, with the ESC byte kept.
   - **Where it is logged:** the Portainer and Docker Scout handlers (`PortainerClient.cs:604`, `DockerScoutVulnerabilityProvider.cs:621`, among others) log this exception with its inner exception.
   - **Limits:** I set the header on the client side only, not through a real HTTP response. I assume the response header parser rejects CR, LF and NUL (not tested), so this probably cannot forge a log line. The header size limit should bound the length, but an ESC sequence would still reach the log.
   - **Why major:** it is a sibling path that writes remote text into the log through the very helper the plan adds, and the plan's security reasoning rests on the refuted fact. The Lead must choose one of these:
     - Also catch, in `ExternalJson.ReadFromJsonAsync`, an `InvalidOperationException` whose inner exception is an `ArgumentException` (the charset refusal). Rethrow it with a fixed message and no inner exception, and add an AC with a test whose body has a `Content-Type` charset containing ESC.
     - Or name this path explicitly as out of scope and open a linked issue now, and correct fact 15 and the *Security considerations* line.

## Security notes

Security review of the plan for issue #393 (mode `plan`). I read `specs/issue-393/plan.md` and `docs/decisions/0045-json-parse-failures-of-external-data-carry-a-fixed-message-with-their-position.md` in /home/user/DockerUpdateGuard.

**Verdict: changes required.** The approach is sound, but one bypass class I measured is missed.

**What holds**
- **Tier and areas:** `security` is the right tier for a guard on remote text that reaches the logs. The listed *Security areas touched* match the parse sites.
- **Parse sites are complete (fact 7):** I grepped `src/` (tests excluded) for `JsonDocument.Parse`, `JsonSerializer.*`, `ReadFromJson`, `DeserializeAsync`, `GetFromJson`, `JsonNode.Parse`, `Utf8JsonReader` and `JsonConverter`. The only parses of external JSON are the ten sites the plan lists, and every other `RemoteJsonDocument.ParseAsync` caller goes through the helper.
- **The rewrite removes the message, `Path` and inner exception whatever form the input takes.** That covers facts 1–4. I checked the library behavior the design rests on (fact 9) in a scratch `net10.0` console project on SDK 10.0.404, run with `dotnet run`:
  - `new JsonException("Remote JSON is not valid at line 0, byte 3 (counted from 0)", null, 0, 3)` printed `Rewritten: Remote JSON is not valid at line 0, byte 3 (counted from 0) | Path=null`.
  - `JsonSerializer.Deserialize<Dictionary<string,int>>` on a wrong-typed value printed `Type: System.Text.Json.JsonException line 0`.
- **No limit is claimed**, so no resource-bound test is needed.

**[BLOCKING] `ExternalJson.ReadFromJsonAsync` still writes remote text into the log through the Content-Type charset**

Where the plan says it: fact 15, and *Security considerations* ("Not covered by design: … non-JSON exceptions (fact 15)").

Fact 15 is incomplete. `HttpContent.ReadFromJsonAsync<T>` looks up the response's `Content-Type` charset before it parses anything. An unknown charset throws `InvalidOperationException` ("The character set provided in ContentType is invalid."). Its inner `ArgumentException` quotes the remote charset verbatim. The helper only catches `JsonException`, so this exception passes through unchanged. The handlers catch every exception and log it with its inner exception: `Portainer/PortainerClient.cs:602`, `:728`, `:786` and `Vulnerabilities/DockerScoutVulnerabilityProvider.cs:619`.

Measurement, same scratch project:
- A `TcpListener` answered `HTTP/1.1 200 OK` with header `Content-Type: application/json; charset="x<ESC>[31mFORGED " + 2000 × 'A'` and body `{"a":1}`.
- `new HttpClient().GetAsync(...)`, then `r.Content.ReadFromJsonAsync<Dictionary<string,int>>(CancellationToken.None)`.
- Output (CR/LF/ESC shown as `<CR>`/`<LF>`/`<ESC>`):
  `ContentType parsed: yes`
  `2310 chars: System.InvalidOperationException: The character set provided in ContentType is invalid.<LF> ---> System.ArgumentException: 'x<ESC>[31mFORGED AAAAAAAA…`

So a real HTTP response gets a raw ESC and about 2 KB of remote text into the log entries of Portainer and Docker Scout through the exact call the helper wraps. (Through a real response, an unquoted charset holding ESC was dropped by header parsing; the quoted form gets through.)

Required change:
1. In `ExternalJson.ReadFromJsonAsync`, also catch `InvalidOperationException exception when (exception.InnerException is ArgumentException)` and throw `new InvalidOperationException(exception.Message)` with no inner exception (the message is the framework's fixed text). This keeps today's failure reason (`unexpected error`). If the Lead would rather map it to `invalid response`, throw `new JsonException("Remote JSON is not valid")` instead and record that choice in 0045.
2. Add a criterion (e.g. AC6) and an `ExternalJsonTests` test: content whose charset is a quoted string holding ESC, built at run time, gives an exception with no inner exception and no ESC in `ToString()`.
3. Correct fact 15 and the guard-forms bullet of *Security considerations* to name this class.
4. Add a line to 0045's *Consequences* saying the charset refusal also carries no remote text.

**Other bypass forms I checked (no finding)**
- `JsonElement` accessors (`GetString`, `GetInt32`, `GetProperty`, `GetDateTimeOffset`) throw fixed texts.
- `Convert.FromBase64String` (Docker Scout `:299`, token cache) throws a fixed `FormatException`.
- Converter exceptions inside the serializer arrive wrapped as `JsonException` and are dropped by the rewrite.
- The undecodable-string refusal has fixed text (fact 13).
- The release index and `RemoteJsonDocument` read the stream directly, so no charset is involved there.

Measurement scratch project: /tmp/claude-0/-home-user-DockerUpdateGuard/8940b4d8-2df4-5275-bade-ddc711775f06/scratchpad/m393/Program.cs (it now holds the HTTP-server version).
