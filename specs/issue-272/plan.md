# Plan: Registry clients answer transport errors, timeouts and malformed answers as *failed*

Source: Issue #272
Tier: standard — a behavior change of the nine registry calls (errors become *failed* outcomes) that creates, changes or removes no security control.
Security areas touched: Registry and Docker Scout calls (`DockerHub/DockerHubClient.cs`, `DockerHub/DockerHubAccessTokenCache.cs`, `Images/OciRegistryClient.cs`); Failure messages and remote text (new failure messages through `Infrastructure/FailureReason.cs`); Secrets in configuration (failures around the PAT login must not carry the PAT or a token); Logging and telemetry of external data (`DockerHub/DockerHubClientLogging.cs`, warnings carrying the exception)

## Problem / root cause

#358 (decision [0019](../../docs/decisions/0019-remote-json-with-undecodable-strings-is-an-invalid-response.md)) put
the eight Docker Hub and OCI calls behind one private `ExecuteAsync<T>` per client, but it catches only
`JsonException` (`src/DockerUpdateGuard/DockerHub/DockerHubClient.cs:656-671`,
`src/DockerUpdateGuard/Images/OciRegistryClient.cs:1196-1211`). Every other exception raised inside escapes: an
`HttpRequestException`/`IOException` left after the retries, an `HttpClient.Timeout` (`TaskCanceledException` with
the caller's token not cancelled), an `InvalidOperationException` from a `JsonElement` accessor on a value of another
kind (`OciRegistryClient.cs:821` `tagElement.GetString()`, `:589` and `DockerHubClient.cs:763` environment entries,
`TryGetProperty` on a root that is not an object), and a `FormatException` from
`DockerHubAccessTokenCache.DecodeBase64Url` (`DockerHub/DockerHubAccessTokenCache.cs:89-99`, called by
`ResolveTokenExpiry` `:197-220`). The ninth call, `DockerHubClient.GetImageConfigurationAsync`, has its own handler
`catch (Exception exception) when (exception is not OperationCanceledException)` (`DockerHubClient.cs:1537`), so a
timeout escapes there as if the caller had cancelled, and it maps with `timeout: null` (no seconds).

Related defects found on the way: `ResolveTokenExpiry` throws `InvalidOperationException` for an `exp` that is not a
number or a payload that is not an object, and `ArgumentOutOfRangeException` for an `exp` beyond year 9999
(`:209-215`); `OciRegistryClient.GetTokenExpirationUtc` throws `InvalidOperationException` for an `expires_in` that
is not a number (`:553`). The area document promises that mistyped fields are unknown values
([Registries](../../docs/areas/registries.md#limits-and-errors)); these fields are fixed here (AC6).

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | The eight calls of the issue have no exception handling | All run through `ExecuteAsync` (`DockerHubClient.cs:1189,1243,1341,1417,1477`; `OciRegistryClient.cs:1424,1484,1500`), catching only `JsonException` (settled in part by #358) | refuted |
| 2 | `ResolveTokenExpiry`/base64 decoding is in `DockerHubClient.cs:161-184` | Moved to `DockerHubAccessTokenCache.cs:89-99,197-220`; runs inside the wrapped calls via `ApplyAuthenticationAsync` → `GetAccessTokenAsync` (`DockerHubClient.cs:515-570`); the claim holds at the new place | confirmed |
| 3 | `ReadTagNamesAsync` throws `InvalidOperationException` for a non-string tag | `OciRegistryClient.cs:821`, no `ValueKind` check; `JsonElement.GetString` throws for kinds other than String/Null (System.Text.Json documentation) | confirmed |
| 4 | Both clients have a public `ResolveBaseImagesAsync` | Not in `IDockerHubClient.cs` nor `IRegistryMetadataClient.cs`; no such method in either client. The public calls are the six Docker Hub and three OCI calls of AC1 | refuted |
| 5 | Docker Hub `GetImageConfigurationAsync` lets a timeout escape | `DockerHubClient.cs:1537` filter `is not OperationCanceledException` | confirmed |
| 6 | OCI logs an ad-hoc `LogWarning` | No `LogWarning` in `OciRegistryClient.cs`; event 2100 `OciRegistryOperationFailed` in `Images/ImageHostLoggingExtensions.cs:742` | refuted |
| 7 | Orchestrators record the raw `exception.Message` | They map with `FailureReason.FromException(exception, null, …)` (`ImageScanOrchestrator.cs:764-768`, `RuntimeContainerScanOrchestrator.cs:692-694`): a fixed reason, but the whole image or container fails (fixed reason since 0009) | refuted |
| 8 | An `HttpClient.Timeout` surfaces as `TaskCanceledException` (inner `TimeoutException`) with the caller's token not cancelled | .NET `HttpClient` documentation; `client.Timeout` set from `DockerHub:RequestTimeoutSeconds` for both clients (`ServiceCollectionExtensions.cs:76,82`) | confirmed |
| 9 | The retry handler rethrows the last error | `TransientHttpRetryHandler.cs:159` (transient = `HttpRequestException`/`TimeoutException`), `:368` `throw;` | confirmed |
| 10 | Messages may carry the exception's message | Guarantee *fixed reasons* (`.squad/project.md`), [0009](../../docs/decisions/0009-integration-results-carry-fixed-failure-reasons.md): fixed reasons only | refuted |
| 11 | Docker Scout shows the pattern | `DockerScoutVulnerabilityProvider.cs:523-531`: rethrow when the caller cancelled, else `FailureReason.FromException(ex, timeout, …)` (moved from `:440-449`) | confirmed |
| 12 | `FailureReason.FromException` gives `timed out after <n> seconds`, `connection failed`, `invalid response` (`JsonException`), `unexpected error` otherwise | `Infrastructure/FailureReason.cs:50-82` | confirmed |
| 13 | A login stopped by the cache's login timeout throws today, pinned by a test | `DockerHubAccessTokenCache.cs:141-144` (timeout = `_httpClient.Timeout`, `DockerHubClient.cs:564`); `DockerHubClientTests.cs:2659` asserts `OperationCanceledException` | confirmed |
| 14 | An OCI tag whose lookup fails is listed by name only | `AppendInspectedTagsAsync` → `GetTagAsync` (own wrapper) → `AppendTagResult` (`OciRegistryClient.cs:840-859,1150-1190`) | confirmed |
| 15 | The OCI token failure message names the token request URI with its query | `OciRegistryClient.cs:1115`; documented in [Integration failures](../../docs/areas/integration-failures.md#excerpts-of-remote-text); the query holds the challenge's `service`/`scope`, no credential (anonymous only); unchanged here | confirmed |
| 16 | 0019 is unreleased | Its adding commit 56a449f is in no tag (`git tag --contains`) | confirmed |

## Acceptance criteria

The nine calls are Docker Hub `GetCurrentUserAsync`, `GetRepositoriesAsync`, `GetRepositoryAsync`, `GetTagAsync`,
`GetTagsAsync`, `GetImageConfigurationAsync` and OCI `GetTagAsync`, `GetTagsAsync`, `GetImageConfigurationAsync`.
Each failure message is `<call message>: <reason>`, with the call messages as they stand: `Docker Hub current user
lookup failed`, `Docker Hub repository listing failed for '<account>'`, `Repository metadata lookup failed for
'<repository>'`, `Tag metadata lookup failed for '<reference>'`, `Tag lookup failed for '<repository>'`, `Image
configuration lookup failed for '<reference>'`; OCI `Registry tag lookup failed for '<reference>'`, `Tag lookup
failed for '<registry>/<repository>'`, `Registry image configuration lookup failed for '<reference>'` (`<reference>`
is `ImageReference.FullReference`).

- AC1: When sending a request raises `HttpRequestException`, or reading a response body raises `IOException`, each
  of the nine calls returns *failed* with `<call message>: connection failed` and logs exactly one warning (3213 Docker
  Hub, 2100 OCI) carrying that exception.
- AC2: When a request raises `TaskCanceledException` with an inner `TimeoutException` while the caller's token is not
  cancelled, and the `HttpClient.Timeout` is 30 seconds, each of the nine calls returns *failed* with `<call message>:
  timed out after 30 seconds` and logs one warning; a Docker Hub login stopped by the login timeout makes
  `GetCurrentUserAsync` return *failed* with `Docker Hub current user lookup failed: timed out after 30 seconds`.
- AC3: When the caller's token is cancelled during a request, each of the nine calls throws
  `OperationCanceledException` and logs no 3213/2100 warning — also OCI `GetTagsAsync` cancelled during a tag's
  manifest lookup.
- AC4: A JSON value of an unexpected kind returns *failed* with `<call message>: unexpected error` and one warning: OCI
  `GetTagsAsync` on a `tags/list` body `{"tags": [1, 2]}`, Docker Hub `GetTagsAsync` on a page body `[]`, OCI
  `GetImageConfigurationAsync` on a configuration blob whose `config.Env` is `[1]`.
- AC5: A Docker Hub login whose token's payload segment is not base64url (`header.@@@.signature`) makes `GetTagAsync`
  return *failed* with `Tag metadata lookup failed for '<reference>': invalid response`, logs one 3213 warning, and
  the next call logs in again.
- AC6: A Docker Hub token whose payload `exp` is a string, or beyond 253402300799, or whose payload is a JSON array,
  is kept for the fallback five minutes; an OCI token answer whose `expires_in` is a string lets the lookup succeed.
- AC7: With the PAT `dckr_pat_FAKE272SECRET` and the login token of AC5 or a valid one, no result message and no
  formatted log message of the failures of AC1, AC2 and AC5 contains the PAT or the token.
- AC8: An OCI `GetTagsAsync` whose `tags/list` names `1.0` and `2.0`, where the manifest request of `1.0` raises
  `HttpRequestException`, succeeds with `1.0` without digest and `2.0` with its digest, and logs one 2100 warning.
- AC9: The argument guards before the wrapper still throw: Docker Hub `GetRepositoriesAsync(" ")`
  `ArgumentException`; OCI `GetTagAsync(null!)` and Docker Hub `GetImageConfigurationAsync(null!)`
  `ArgumentNullException` (pins of the catch-all; they pass today).

## Tests per criterion

"Per call" tests take the call as a `[DataRow]`, the Docker Hub ones through the existing `RunCallAsync` helper
(`DockerHubClientTests.cs:3021`, extended by the Tester to `Tags` and `ImageConfiguration`), an OCI one written the
same way.

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `DockerHubClientCallTransportErrorReturnsConnectionFailedAsync`, `DockerHubClientCallBodyReadErrorReturnsConnectionFailedAsync`; adapted `DockerHubClientGetImageConfigurationAsyncWhenTransportFailsReportsConnectionFailedAsync` (3212 → 3213) | `src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs` |
| AC1 | `OciRegistryClientCallTransportErrorReturnsConnectionFailedAsync`, `OciRegistryClientCallBodyReadErrorReturnsConnectionFailedAsync` | `src/Tests/DockerUpdateGuard.Tests/OciRegistryClientTests.cs` |
| AC2 | `DockerHubClientCallTimeoutReturnsTimedOutAsync`; adapted `DockerHubClientLoginBodyStalledReleasesTokenCacheAfterTimeoutAsync` (failed result instead of the exception) | `DockerHubClientTests.cs` |
| AC2 | `OciRegistryClientCallTimeoutReturnsTimedOutAsync` | `OciRegistryClientTests.cs` |
| AC3 | `DockerHubClientCallCallerCancelledThrowsOperationCanceledExceptionAsync` | `DockerHubClientTests.cs` |
| AC3 | `OciRegistryClientCallCallerCancelledThrowsOperationCanceledExceptionAsync`, `OciRegistryClientGetTagsAsyncCallerCancelledDuringTagLookupThrowsOperationCanceledExceptionAsync` | `OciRegistryClientTests.cs` |
| AC4 | `DockerHubClientGetTagsAsyncArrayBodyReturnsUnexpectedErrorAsync` | `DockerHubClientTests.cs` |
| AC4 | `OciRegistryClientGetTagsAsyncNonStringTagReturnsUnexpectedErrorAsync`, `OciRegistryClientGetImageConfigurationAsyncNonStringEnvironmentEntryReturnsUnexpectedErrorAsync` | `OciRegistryClientTests.cs` |
| AC5 | `DockerHubClientGetTagAsyncAccessTokenPayloadNotBase64ReturnsInvalidResponseAsync` | `DockerHubClientTests.cs` |
| AC6 | `DockerHubAccessTokenCacheGetOrRequestAsyncUnreadableExpiryKeepsTokenFiveMinutesAsync` (rows: string `exp`, `exp` 253402300800, array payload) | `src/Tests/DockerUpdateGuard.Tests/DockerHubAccessTokenCacheTests.cs` |
| AC6 | `OciRegistryClientGetTagAsyncTokenExpiresInNotNumberSucceedsAsync` | `OciRegistryClientTests.cs` |
| AC7 | `DockerHubClientCallFailureMessagesOmitPatAndTokenAsync` (rows: transport error, timeout, token payload not base64url) | `DockerHubClientTests.cs` |
| AC8 | `OciRegistryClientGetTagsAsyncTagLookupTransportErrorListsTagByNameAsync` | `OciRegistryClientTests.cs` |
| AC9 | `DockerHubClientGetRepositoriesAsyncBlankAccountThrowsArgumentExceptionAsync`, `DockerHubClientGetImageConfigurationAsyncNullReferenceThrowsArgumentNullExceptionAsync` | `DockerHubClientTests.cs` |
| AC9 | `OciRegistryClientGetTagAsyncNullReferenceThrowsArgumentNullExceptionAsync` | `OciRegistryClientTests.cs` |

Doubles: `DelegateHttpMessageHandler` or `RetryProbeHttpMessageHandler` (`FailureFactory`) throwing the exception; the
body-read error as a `StreamContent` over a `Pipe` reader whose writer completes with an `IOException`; the timeout
without waiting, as a handler throwing `new TaskCanceledException("…", new TimeoutException())` with
`HttpClient.Timeout = 30 s`; the caller's cancellation as a handler that cancels the caller's source and throws
`OperationCanceledException` with its token. No new hand-written double.

## Approach

1. **Wrapper, both clients.** `ExecuteAsync<T>` gains a trailing `CancellationToken cancellationToken` parameter
   (private) and every call passes its token. Body: `catch (OperationCanceledException) when
   (cancellationToken.IsCancellationRequested) { throw; }`, then `catch (Exception exception)`: log the client's
   existing warning (`DockerHubOperationFailed` 3213 / `OciRegistryOperationFailed` 2100) with the exception and
   return `Failed<T>($"{failureMessage}: {FailureReason.FromException(exception, _httpClient.Timeout,
   cancellationToken)}")`. No `throw` in the second block (S2139), no change to `FailureReason`.
2. **Docker Hub `GetImageConfigurationAsync`.** Replace its `try`/`catch` by `ExecuteAsync(nameof(GetImageConfigurationAsync),
   imageReference.FullReference, $"Image configuration lookup failed for '{imageReference.FullReference}'", …,
   cancellationToken)`; the guard, unsupported and dot-segment checks stay before it. Delete
   `DockerHubImageConfigurationLookupFailed` (3212) from `DockerHubClientLogging.cs`, now unused.
3. **Token payload not base64url.** `DockerHubAccessTokenCache.DecodeBase64Url` catches the `FormatException` of
   `Convert.FromBase64String` and throws `new JsonException("The access token payload is not base64url", exception)`;
   the wrapper reports it as `invalid response`. Nothing is cached (the throw happens before `Volatile.Write`, the lock
   is released in `finally`).
4. **Expiry fields read tolerantly (AC6).** `ResolveTokenExpiry` reads `exp` only when the payload root is an object,
   `exp` is a `Number`, `TryGetInt64` succeeds and the value lies within `DateTimeOffset.FromUnixTimeSeconds`' range
   (−62135596800 … 253402300799); otherwise the fallback lifetime applies. `OciRegistryClient.GetTokenExpirationUtc`
   reads `expires_in` only when it is a `Number`.
5. Unchanged: argument guards stay before the wrapper; 404 → *not found* and other statuses → *failed* with status and
   excerpt; `TransientHttpRetryHandler`; a failed listing page fails the whole listing (no partial result); an OCI tag
   whose own lookup fails is listed by name only, and a caller cancellation inside it propagates through both wrappers.

No new user-visible layout: the messages are the existing call messages plus a fixed reason.

## Options considered

The alternatives are in [0029](../../docs/decisions/0029-registry-calls-answer-every-error-but-the-callers-cancellation-as-failed.md):
catch only the listed exception types (the issue's sketch); report `InvalidOperationException` as `invalid response`;
read every JSON value tolerantly; carry the exception's message (the issue's allowance, contradicting 0009); stop an
OCI listing at the first tag that times out; report a non-base64 token payload as `unexpected error` or map
`FormatException` in the wrapper. Each lost for the reason recorded there.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `DockerHub/DockerHubClient.cs` | wrapper widened, token parameter, `GetImageConfigurationAsync` through the wrapper |
| DockerUpdateGuard | `DockerHub/DockerHubClientLogging.cs` | event 3212 deleted |
| DockerUpdateGuard | `DockerHub/DockerHubAccessTokenCache.cs` | base64 failure as `JsonException`, tolerant `exp` |
| DockerUpdateGuard | `Images/OciRegistryClient.cs` | wrapper widened, token parameter, tolerant `expires_in` |
| DockerUpdateGuard.Tests | the three test files below | new and adapted tests |

## Signatures (for the Dev's skeleton)

None: every changed member is private (the two `ExecuteAsync` methods, `DecodeBase64Url`, `ResolveTokenExpiry`,
`GetTokenExpirationUtc`), and the public static logging method DockerHubImageConfigurationLookupFailed is deleted.
No skeleton step.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs`
- `src/Tests/DockerUpdateGuard.Tests/OciRegistryClientTests.cs`
- `src/Tests/DockerUpdateGuard.Tests/DockerHubAccessTokenCacheTests.cs`

Existing tests whose input the change handles differently, adapted by the Tester in step 5:
`DockerHubClientGetImageConfigurationAsyncWhenTransportFailsReportsConnectionFailedAsync` (asserts event 3212 → 3213;
message unchanged) and `DockerHubClientLoginBodyStalledReleasesTokenCacheAfterTimeoutAsync` (asserts the
`OperationCanceledException` → the *failed* result of AC2; the cache-release part stays).
`DockerHubAccessTokenCacheGetOrRequestAsyncLoginTimeoutCancelsLoginAndStoresNothingAsync` stays: the cache itself
still throws. No changed signature is called from test code.

Files no unit test can cover: none.

## Areas

- [Registries](../../docs/areas/registries.md): *Limits and errors* states the new rule; the *Known gaps* entry of
  #272 goes; *Authentication* says how a login timeout is answered.
- [Integration failures](../../docs/areas/integration-failures.md): the escape bullet goes; `invalid response` also
  covers the token payload.

## Documentation updates

All by the Dev, against the built code:

- `docs/areas/registries.md`, *Limits and errors*: replace the bullet "The Docker Hub current-user, account listing, …
  answer an unreadable body … as *failed* with `invalid response`" and the bullet "Some failures still surface as
  errors instead of outcomes: …" by one rule: the nine calls answer an error raised during the call as *failed*
  `<call message>: <reason>` — `connection failed` (transport error or body read error left after the retries),
  `timed out after <n> seconds` (`RequestTimeoutSeconds`), `invalid response` (malformed JSON, a string that does not
  decode, a Docker Hub token payload that is not base64url), `unexpected error` (anything else, such as a JSON value of
  an unexpected kind in a list or as the root) — logging one warning (3213 Docker Hub, 2100 OCI) with the error; the
  caller's cancellation is thrown; a listing fails as a whole; an OCI tag whose own lookup fails is listed by name only.
  Extend "missing or mistyped JSON fields are treated as unknown values" with the token expiry fields (`exp`,
  `expires_in`).
- `docs/areas/registries.md`, *Known gaps*: delete the #272 entry. *Authentication*, Docker Hub web API: "is cancelled
  and surfaces as *timed out*" → the call answers *failed* with `timed out after <n> seconds`. *Related decisions*:
  add 0029.
- `docs/areas/integration-failures.md`: *Limits and errors*, delete "Some registry calls still let errors escape …";
  *Fixed failure reasons*, `invalid response` also covers a Docker Hub access token whose payload is not base64url.
- `docs/ARCHITECTURE.md:223`: "The outcomes and where errors still escape are specified in" → "The outcomes are
  specified in".
- Sweep result: `scan-engine.md`, `base-images.md`, `update-detection.md`, `image-catalog.md` speak of errors in
  general terms only — no change. No README, configuration or `.squad/project.md` change.

## Architecture check

Strengthens the guarantee *Integration calls answer expected failures with an outcome and a fixed reason* for the
registry clients; reasons stay fixed (0009); layering and the `ExternalOperationResult<T>` model are unchanged.

## Security considerations

- [x] No limits: no bound on parsed input changes.
- [x] Exceptions reaching a message: every exception inside the nine calls reaches only the fixed reason of
  `FailureReason.FromException` (`connection failed`, `timed out after <n> seconds`, `timed out`, `invalid response`,
  `unexpected error`); a `ReportableException` would pass its composed text, and none is raised in these clients. The
  exception, which can name a host, goes to the log entry only (0009). The PAT and tokens are in no message (AC7).

## Decision records

- [0029](../../docs/decisions/0029-registry-calls-answer-every-error-but-the-callers-cancellation-as-failed.md)
  (Proposed, indexed).
- [0019](../../docs/decisions/0019-remote-json-with-undecodable-strings-is-an-invalid-response.md) (unreleased):
  its consequence "… still escape … until #272" now points to 0029 (done by the Lead).

## Out of scope / follow-ups

- The OCI token failure message keeps naming the token request URI with its `service`/`scope` query (fact 15); it
  carries no credential and is documented.
- An empty JWT payload segment (`header..signature`) raises the `ArgumentException` of `DecodeBase64Url`'s guard and
  now reads `unexpected error`; Docker Hub issues no such token.

## Challenge notes

Devil's Advocate, mode plan, specs/issue-272 (/home/user/DockerUpdateGuard/specs/issue-272/plan.md)

I checked the plan against the code and it holds up. Every claim I tested is correct:
- Both `ExecuteAsync` wrappers catch only `JsonException`: DockerHubClient.cs:1075-1090 and OciRegistryClient.cs:1196-1211.
- Docker Hub `GetImageConfigurationAsync` filters with `is not OperationCanceledException` and passes `timeout: null`: DockerHubClient.cs:1537-1541.
- The login timeout uses a linked source and leaves the caller's token alone (DockerHubAccessTokenCache.cs:141-144). The wrapper, with `FromException(…, _httpClient.Timeout, token)`, therefore returns `timed out after 30 seconds`. The adapted test at DockerHubClientTests.cs:2659 already sets `Timeout = 30 s`.
- `DecodeBase64Url` turns `@@@` into `@@@=`, which throws `FormatException` (:89-99).
- `TryGetProperty` on a root that is an array throws, so AC4's Docker Hub `[]` page fails as planned (DockerHubClient.cs:545). OCI `GetString()` on numbers throws at :589 and :821.
- The nested OCI `GetTagsAsync` → `GetTagAsync` passes the caller's token, so AC3 and AC8 hold (OciRegistryClient.cs:1150-1190).
- Event 3212 is used only by DockerHubClient.cs:1539, DockerHubClientLogging.cs:130 and the test at DockerHubClientTests.cs:1453. No document lists it.
- No other production consumer relies on these exceptions. RegistryBaseImageResolver branches on the result status, and Scout has its own handler. No other test pins a thrown exception except the two the plan adapts and the cache test it keeps.
- The catch-all keeps the argument guards throwing, because they sit outside the wrapper.

Objections:

1. minor — OCI token expiry has a second field that can break: `issued_at` (OciRegistryClient.cs:549-556). AC6 makes `expires_in` tolerant but leaves `issued_at` alone. `DateTimeOffset.TryParse` accepts `issued_at` "9999-12-31T23:59:59Z" (and "0001-01-01T00:00:00Z"). The next step, `issuedAtUtc.AddSeconds(expiresInSeconds) - _tokenExpiryBuffer`, then throws `ArgumentOutOfRangeException`. After this change that turns the lookup into *failed* `unexpected error` instead of an unknown value, which goes against registries.md:253 ("mistyped fields are treated as unknown values"). Why minor: the catch-all already stops the exception escaping, so only the outcome differs. Instruction for the Dev: in step 4, also fall back to the current time (the existing behaviour for an unreadable `issued_at`) when `issued_at` plus `expires_in` would leave `DateTimeOffset`'s range. Instruction for the Tester: add a row to `OciRegistryClientGetTagAsyncTokenExpiresInNotNumberSucceedsAsync` with `issued_at` "9999-12-31T23:59:59Z" and assert that the lookup succeeds.

2. minor — The issue's criterion "a JWT whose payload is not valid base64/JSON yields Failed" is tested only for the base64 half (AC5). A payload that decodes as base64url but is not JSON reaches `JsonDocument.Parse` (DockerHubAccessTokenCache.cs:207). The `JsonException` it raises is caught by today's wrapper, so the case passes today, but nothing pins it. Why minor: there is no defect, only a regression pin missing from the issue's own criterion. Instruction for the Tester: add a row to `DockerHubClientGetTagAsyncAccessTokenPayloadNotBase64ReturnsInvalidResponseAsync` whose payload segment is the base64url of `not json`. Assert `invalid response` and that the next call logs in again.

## Security notes

Security plan review: specs/issue-272/plan.md and docs/decisions/0029-registry-calls-answer-every-error-but-the-callers-cancellation-as-failed.md (mode `plan`, round 1)

I found no blocking issues. There are two non-blocking findings below.

**Tier and listed security areas**
- `standard` is the right tier. Nothing here adds, changes or removes a security control in the sense of the `security` row (.squad/routing.md:87). The change is about how errors are handled.
- The tolerant `exp` and `expires_in` reading does change how long an access token stays cached. Now a token with an unreadable expiry is kept for the existing 5-minute fallback, where today an exception escapes. That is not a credential-handling control: the server still decides whether the token is valid, and the same fallback already applies when `exp` is missing.
- The base64url failure still caches nothing. The throw happens before `Volatile.Write`, and the lock is released in `finally` (DockerHubAccessTokenCache.cs, `GetOrRequestAsync`).
- The four security areas the plan lists are complete. Docker Scout, `TransientHttpRetryHandler`, `PaginationLinkGuard` and `FailureReason` are not changed.

**Checks that passed (what I read)**
- **Fixed reasons only.** `FailureReason.FromException` (src/DockerUpdateGuard/Infrastructure/FailureReason.cs) passes exception text only for a `ReportableException`. A grep for `new ReportableException` / `: ReportableException` finds only DockerInstanceClient, ImageRegistrationService and PortainerAuthenticationException, none of them reachable from the nine calls. Every other exception gets a fixed reason, so result messages cannot carry remote text or credentials.
- **Caller cancellation.** `catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }` comes before the catch-all, and the caller's token is passed into both wrappers, so cancellation still propagates through the nested OCI wrappers (`GetTagsAsync` → `GetTagAsync`). AC3 covers this, including cancellation during a tag lookup.
- **PAT and tokens.** The PAT goes only into the Basic `Authorization` header (DockerHubClient.cs `GetRegistryTokenAsync`) and the login request. Transport exceptions do not carry headers or bodies. The new `JsonException("The access token payload is not base64url", FormatException)` has a fixed message, and the `FormatException` from `Convert.FromBase64String` does not repeat its input. The OCI token request URI (fact 15) carries only `service`/`scope`, is already documented, and is unchanged.
- **Logging.** Events 3213 and 2100 keep their templates (`OperationName`, `TargetName`). No argument name is on the sensitive-name list. 3212, which is deleted, logged the same `FullReference`. The exceptions that now go to 3213/2100 were previously escaping to the orchestrators' handlers, so nothing new reaches the log.
- **Measurement rule.** No security property depends on measured library behavior. Whether a cancellation is rethrown is decided by the caller's token, not by the shape of the `TaskCanceledException` from `HttpClient.Timeout`; fact 8 only affects which fixed reason is chosen. I ran no measurement.

**Non-blocking findings**

1. **AC7 checks the formatted log message but not the logged exception chain.** The plan's security section says the exception goes to the log only, so the exception (and its `ToString()`, also exported over OTLP) is what the log actually carries. For the transport and timeout rows the test double makes the exception, so checking it would prove nothing. For the AC5 row, though, the code under test makes the exception: the new `JsonException` and its inner `FormatException`.
   - Smallest fix: in `DockerHubClientCallFailureMessagesOmitPatAndTokenAsync`, for the "token payload not base64url" row, also assert that the captured log entry's `Exception?.ToString()` contains neither `dckr_pat_FAKE272SECRET` nor the token.
   - Recommendation: fix now (Tester, step 5).

2. **OCI `issued_at` + `expires_in` can overflow, so the AC6 rule is not fully met.** `OciRegistryClient.GetTokenExpirationUtc` (src/DockerUpdateGuard/Images/OciRegistryClient.cs:548-556) parses `issued_at` from the remote answer and calls `issuedAtUtc.AddSeconds(expiresInSeconds)`. With `issued_at` near 9999-12-31 this throws `ArgumentOutOfRangeException`. The plan already fixes the matching Docker Hub case (`exp` beyond 253402300799). After this change that OCI answer becomes a failed result reading `unexpected error`, not an escape, so it is not a security defect. It does contradict the rule AC6 cites: "mistyped fields are unknown values".
   - Smallest fix, in approach step 4: use the fallback lifetime when `issued_at` is more than `int.MaxValue` seconds before `DateTimeOffset.MaxValue`, or compute the sum in a `checked` range test.
   - Recommendation: fix now if the Lead extends AC6 by one row; otherwise open a linked issue.

**Note, no finding: resource bound in 0029, consequence 3**
An OCI listing whose tag lookups time out now costs one timeout per inspected tag instead of failing at the first one. Without query bounds that is up to 1000 pages × 100 tags (`PaginationLinkGuard.MaxListingPages` = 1000) × `RequestTimeoutSeconds` (≤ 300). That risk already exists: a registry that answers each manifest just before the timeout with a 5xx gets the same per-tag cost today, because non-success tags are already listed by name. So the change adds no new denial-of-service class, and 0029 records the trade-off. A total time budget per listing would be general hardening and belongs in a separate issue, not in this change.

Files reviewed:
- /home/user/DockerUpdateGuard/specs/issue-272/plan.md
- /home/user/DockerUpdateGuard/docs/decisions/0029-registry-calls-answer-every-error-but-the-callers-cancellation-as-failed.md
- /home/user/DockerUpdateGuard/src/DockerUpdateGuard/Infrastructure/FailureReason.cs
- /home/user/DockerUpdateGuard/src/DockerUpdateGuard/DockerHub/DockerHubAccessTokenCache.cs
- /home/user/DockerUpdateGuard/src/DockerUpdateGuard/DockerHub/DockerHubClient.cs
- /home/user/DockerUpdateGuard/src/DockerUpdateGuard/DockerHub/DockerHubClientLogging.cs
- /home/user/DockerUpdateGuard/src/DockerUpdateGuard/Images/OciRegistryClient.cs
