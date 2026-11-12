# Plan: Share the Docker Hub web API token across scopes

Source: Issue #25
Tier: security — moves the Docker Hub bearer token from a per-scope instance field into a process-wide store keyed by the credentials, a change to how a credential is held and handled.
Security areas touched: Secrets in configuration (the Docker Hub PAT, fingerprinted for the cache key); Registry and Docker Scout calls (bearer tokens, `DockerHub/DockerHubClient.cs`)

## Problem / root cause

`DockerHubClient` keeps the web API token in instance fields (`_tokenRefreshLock`, `_accessTokenExpiresAtUtc`,
`_accessToken`, `src/DockerUpdateGuard/DockerHub/DockerHubClient.cs:84,94,99`), filled by `GetAccessTokenAsync`
(`:980-1023`). The client is registered with `AddHttpClient<DockerHubClient>` (`ServiceCollectionExtensions.cs:73-79`),
which makes it transient, and the scoped factories at `:114-115` resolve one instance per scope. Every new scope (a
scan, account discovery run, Blazor circuit) therefore starts with an empty cache and logs in again. Not a correctness
error; it spends logins against Docker Hub's limits.

Related defect found on the way: the expiry checks use the real clock (`DateTimeOffset.UtcNow`, `:202`, `:1084`)
instead of the injected `TimeProvider`; the move below fixes that for the new type.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | `DockerHubClient` is transient (typed client) | `ServiceCollectionExtensions.cs:73` `AddHttpClient<DockerHubClient>`; typed clients are registered transient by `Microsoft.Extensions.Http` | confirmed |
| 2 | Token cache is per instance, so per scope | `DockerHubClient.cs:84,94,99` instance fields; `ServiceCollectionExtensions.cs:114-115` scoped factories calling `GetRequiredService<DockerHubClient>()` | confirmed |
| 3 | Issue line numbers (`:62-73`, `:1199-1237`, `:54-59`, `:109-110`) | Code has moved: fields `:81-99`, token logic `:980-1085`, registration `:73-79`, `:114-115` (stale lines, same content) | refuted |
| 4 | Two instances per scope (#6) amplify it | #6 closed: no scope resolves both `IDockerHubClient` and `IRegistryMetadataClient`; its closing comment defers to this issue | refuted |
| 5 | Not a correctness error | Each scope logs in once and reuses its token (`GetAccessTokenAsync` double-checked under `_tokenRefreshLock`, `:984-998`) | confirmed |
| 6 | `UserName` and `Pat` are read live; `ApiBaseUrl` is fixed at startup, so the login URL is constant per process | `ApplyAuthenticationAsync` reads `_optionsMonitor.CurrentValue` (`:944`); `docs/areas/registries.md`, *Configuration reload*; base address set once in `ServiceCollectionExtensions.cs:75` | confirmed |
| 7 | The Retry-After deferral is checked after the cached-token check and inside the lock | `DockerHubClient.cs:994-1002` | confirmed |
| 8 | `TimeProvider` is registered as a singleton | `ServiceCollectionExtensions.cs:102` | confirmed |
| 9 | A token payload that is not base64 or not JSON throws out of `ResolveTokenExpiry` today, and nothing usable is cached | `:180-203`; `_accessToken` is set before the throw but the expiry stays `default`, so `HasValidAccessToken` is false | confirmed |
| 10 | The Docker Scout provider (singleton) already caches its Docker Hub token process-wide | `DockerScoutVulnerabilityProvider.cs:70-75,300-335`; `docs/areas/vulnerabilities.md:132-134` | confirmed |
| 11 | Lifetime of the token `v2/users/login` returns (its `exp`) | to verify: not needed by any criterion (the cache follows `exp` whatever it is); no session has Docker Hub credentials; none owns it | unverified |
| 12 | Login waiters hold no `MaxParallelRequests` slot, so a shared login lock cannot deadlock with the per-instance throttle | `SendGetAsync` (`:857-867`) authenticates before `SendThrottledAsync`; the login itself takes one slot of the instance doing it | confirmed |

## Acceptance criteria

- AC1: Two `DockerHubClient` instances that share one `DockerHubAccessTokenCache` send one login for their
  authenticated web API requests while the token is valid; the second instance sends its request with the first
  instance's token.
- AC2: A cached token is reused only while the clock of the injected `TimeProvider` is more than one minute before its
  expiry — the token's `exp` claim, or five minutes after it was stored when it has none; after that the next call logs
  in again.
- AC3: A cached token is never returned for other credentials: a call with a different user name, or with a different
  PAT, logs in again and replaces the entry.
- AC4: Concurrent calls on an empty or expired cache log in exactly once and all receive that token; a login that
  fails or is deferred by a Retry-After window stores nothing, so the next call logs in again.
- AC5: A caller cancelled while waiting for another caller's login throws `OperationCanceledException`; the other
  login completes and is stored, and the next call is not blocked.
- AC6: The host registers one `DockerHubAccessTokenCache` singleton, used by the Docker Hub clients of all scopes.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | DockerHubClientTwoInstancesSharingTokenCacheLogInOnceAsync | src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs |
| AC2 | DockerHubAccessTokenCacheGetOrRequestAsyncTokenWithinOneMinuteOfExpiryRequestsAgainAsync, DockerHubAccessTokenCacheGetOrRequestAsyncTokenWithoutExpiryRequestsAgainAfterFiveMinutesAsync, DockerHubAccessTokenCacheGetOrRequestAsyncValidTokenIsReusedAsync | src/Tests/DockerUpdateGuard.Tests/DockerHubAccessTokenCacheTests.cs |
| AC3 | DockerHubAccessTokenCacheGetOrRequestAsyncChangedPatRequestsAgainAsync, DockerHubAccessTokenCacheGetOrRequestAsyncChangedUserNameRequestsAgainAsync | src/Tests/DockerUpdateGuard.Tests/DockerHubAccessTokenCacheTests.cs |
| AC4 | DockerHubAccessTokenCacheGetOrRequestAsyncConcurrentCallersRequestOnceAsync, DockerHubAccessTokenCacheGetOrRequestAsyncFailedRequestStoresNothingAsync, DockerHubClientLoginRetryAfterDeferralWithSharedCacheStoresNothingAsync | src/Tests/DockerUpdateGuard.Tests/DockerHubAccessTokenCacheTests.cs; the last in src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs |
| AC5 | DockerHubAccessTokenCacheGetOrRequestAsyncCancelledWaitThrowsAndReleasesNothingAsync | src/Tests/DockerUpdateGuard.Tests/DockerHubAccessTokenCacheTests.cs |
| AC6 | ServiceCollectionExtensionsDockerHubAccessTokenCacheIsSharedSingleton | src/Tests/DockerUpdateGuard.Tests/ServiceCollectionExtensionsTests.cs |

Cache tests drive the clock with `Substitute.For<TimeProvider>()` (as `LoginRetryAfterRegistryTests`) and build the
token as a three-segment string with a base64url JSON payload carrying `exp`, or without it. Concurrency (AC4, AC5)
uses a request delegate that waits on a `TaskCompletionSource` the test completes, never a delay.

## Approach

- New `src/DockerUpdateGuard/DockerHub/DockerHubAccessTokenCache.cs`, a public sealed class implementing
  `IDisposable`, with the injected `TimeProvider`, one `SemaphoreSlim(1, 1)` login lock, and one private immutable
  entry (token, expiry, trimmed user name, SHA-256 of the UTF-8 PAT) held in one field that is read and replaced as a
  whole (`Volatile.Read`/`Volatile.Write`). The PAT itself is never stored.
- `GetOrRequestAsync`: if the entry matches the user name (ordinal) and the PAT fingerprint
  (`CryptographicOperations.FixedTimeEquals`) and its expiry is more than one minute after `GetUtcNow()`, return it
  without the lock. Otherwise wait for the lock with the caller's token, check again, then call `requestToken`; on a
  successful, non-blank token resolve its expiry first (the moved `ResolveTokenExpiry`/`DecodeBase64Url`, fallback
  `GetUtcNow()` plus five minutes) and only then store the new entry; return the delegate's result unchanged. A failed
  result stores nothing and leaves the old entry as it is (it no longer matches or has expired anyway). An exception
  from the delegate or the expiry parse propagates as today (fact 9), the lock is released in `finally`.
- `DockerHubClient` takes the cache as a new last constructor parameter. `GetAccessTokenAsync` becomes one call to
  `GetOrRequestAsync` whose delegate does what the locked part does today: the `IsLoginDeferred` check returning
  `CreateLoginDeferredMessage`, else `RequestAccessTokenAsync` with the delegate's cancellation token. Removed from the
  client: `_tokenRefreshLock`, `_accessToken`, `_accessTokenExpiresAtUtc`, `HasValidAccessToken`, `ResolveTokenExpiry`,
  `DecodeBase64Url`, and the lock's disposal in `Dispose` (the throttle's stays).
- `ServiceCollectionExtensions`: `services.AddSingleton<DockerHubAccessTokenCache>();` next to `LoginRetryAfterRegistry`.
- No new log entry, message or configuration key; no user-visible text changes. The registry pull-token path and
  `OciRegistryClient` are untouched.

## Options considered

1. **Singleton cache keyed by user name and PAT fingerprint (chosen).**
2. **`DockerHubClient` as a singleton** — lost: a typed `HttpClient` captured in a singleton never rotates its handler,
   and it would turn the `MaxParallelRequests` throttle process-wide, which is #265's decision with its own reload rules.
3. **Static fields in the client** — lost: global state tests cannot reset (as in 0022, option 3).
4. **Share the Docker Scout token** — lost: `DockerScoutLoginUrl` is configurable, so the tokens are not interchangeable.
5. **Also drop the token on a 401 to a request that carried it** — lost: a 401 for an unreadable resource would cost a
   login per request, more than today; recorded as the way to revisit.
6. **Keep it per scope (no change)** — lost: the extra logins remain, and #6 was closed on the promise of this fix.

Details in the record.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `DockerHub/DockerHubAccessTokenCache.cs` | new |
| DockerUpdateGuard | `DockerHub/DockerHubClient.cs` | constructor parameter; token state and expiry parsing moved out |
| DockerUpdateGuard | `ServiceCollectionExtensions.cs` | singleton registration |

## Signatures (for the Dev's skeleton)

- `public sealed class DockerHubAccessTokenCache : IDisposable` (namespace `DockerUpdateGuard.DockerHub`)
- `public DockerHubAccessTokenCache(TimeProvider timeProvider)`
- `public async Task<ExternalOperationResult<string>> GetOrRequestAsync(string userName, string personalAccessToken, Func<CancellationToken, Task<ExternalOperationResult<string>>> requestToken, CancellationToken cancellationToken)`
- `public void Dispose()`
- `public DockerHubClient(HttpClient httpClient, ILogger<DockerHubClient> logger, IOptionsMonitor<DockerUpdateGuardOptions> optionsMonitor, LoginRetryAfterRegistry loginRetryAfterRegistry, DockerHubAccessTokenCache accessTokenCache)`

Deleted: the four-parameter `DockerHubClient` constructor is removed.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/DockerHubAccessTokenCacheTests.cs` (new)
- `src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs`
- `src/Tests/DockerUpdateGuard.Tests/ServiceCollectionExtensionsTests.cs`

Existing call sites of the removed constructor, adapted by the Dev in step 4, mechanically, passing a
`new DockerHubAccessTokenCache(TimeProvider.System)` in a `using`: `DockerHubClientTests.cs:1038` and `:2418`
(`CreateClient`), `DockerScoutVulnerabilityProviderTests.cs:1205`, `RegistryBaseImageResolverTests.cs:110`. The
Tester adds an optional `DockerHubAccessTokenCache? accessTokenCache` parameter to `CreateClient` in step 5 for AC1
and AC4. Existing tests keep their inputs: each creates its own cache, so per-instance behavior they pin (e.g.
`DockerHubClientLoginRetryAfterDeltaDefersNextRequestOfSameInstanceAsync`,
`DockerHubClientGetCurrentUserAsyncWhenAnotherCallRefreshesTokenLogsInOnlyOnceAsync`) is unchanged. The assertion
`AreNotSame(firstClient, secondClient)` in `ServiceCollectionExtensionsLoginRetryAfterRegistryIsSharedSingleton` stays true.

Files no unit test can cover: none.

## Areas

- Registries: *Authentication*, Docker Hub web API bullet — the token's process-wide scope, its key, one login at a
  time, and what a reload of `UserName`/`Pat` does.

## Documentation updates

- `docs/areas/registries.md`, *Authentication*, Docker Hub web API (Dev): the token is kept in memory for the whole
  process and shared by every client instance and scope, reused only for the trimmed user name and PAT it was obtained
  with (a reload of either makes the next request log in again), one login at a time; a failed or deferred login
  keeps nothing; link [0023](../../docs/decisions/0023-the-docker-hub-web-api-token-is-cached-process-wide-per-credentials.md).
- `docs/ARCHITECTURE.md` §6, after the `LoginRetryAfterRegistry` paragraph (Dev): one sentence on the singleton
  `DockerHubAccessTokenCache` (`DockerHub\`) holding the Docker Hub web API token for all scopes, linking Registries and 0023.
- `.squad/project.md`, *Security areas*, *Registry and Docker Scout calls* (Dev reports the exact text, the
  orchestrator writes it): add `DockerHub/DockerHubAccessTokenCache.cs` to its files.
- Sweep: `docs/areas/*.md` and `docs/ARCHITECTURE.md` searched for "singleton", "per scope", "client instance";
  `registries.md:142` ("also in other client instances") and `:186`, `:201`, `:242` (throttle, #265) stay true. No
  README key and no changelog file exist for this change.

## Architecture check

Composition root stays the only wiring place; the new type lives in the host project next to its client (layering
guarantee kept). The fixed-reason guarantee (0009) is untouched: no new message. Single-instance scan engine
assumption unchanged; the cache is in memory only.

## Security considerations

- [x] No limits: the token payload parse is unchanged code moved, with its existing behavior on bad input.
- [x] Exceptions reaching a user-visible reason: none new; a malformed token payload still escapes as an error as
  `docs/areas/registries.md`, *Limits and errors*, already states.
- The PAT is not copied: only its SHA-256 fingerprint is held, compared in constant time; nothing of the token, PAT or
  fingerprint is logged.
- A token is never returned for credentials other than those it was obtained with (AC3), so a reload to other
  credentials cannot reuse it.
- Accepted: a token refused early by Docker Hub is reused by all scopes until one minute before its expiry (option 5,
  record *Consequences*); a PAT change or restart clears it.
- The login lock is process-wide: a hanging login blocks other authenticated web API requests for at most its
  `RequestTimeoutSeconds`; waiters honor their own cancellation (AC5).

## Decision records

- `docs/decisions/0023-the-docker-hub-web-api-token-is-cached-process-wide-per-credentials.md` (Proposed)

## Out of scope / follow-ups

- Process-wide `MaxParallelRequests`: #265.
- Dropping the token on a 401: option 5, revisit per the record; no issue now.
- The Docker Scout provider's real-clock expiry: unchanged.

## Challenge notes

Devil's Advocate, plan mode, specs/issue-25 (/home/user/DockerUpdateGuard/specs/issue-25/plan.md)

The plan holds up. I checked its claims against the code:
- Typed-client registration: `ServiceCollectionExtensions.cs:73-79`.
- Scoped factories: `:114-115`.
- Instance token fields: `DockerHubClient.cs:84,94,99`.
- Real-clock expiry checks: `:202`, `:1084`.
- The deferral check runs inside the lock, after the second cached-token check: `:994-1002`.
- `SendGetAsync` authenticates before it takes a throttle slot (`:857-867`), so a shared login lock cannot deadlock with the per-instance throttle.

All four call sites of the removed constructor are listed: `DockerHubClientTests.cs:1038` and `:2418`, `DockerScoutVulnerabilityProviderTests.cs:1205`, `RegistryBaseImageResolverTests.cs:110`. No test reaches the moved private members by reflection. The only reflection on a `ResolveTokenExpiry*` method is in `DockerScoutTokenExpiryTests.cs:142`, and it targets the Scout provider, which does not change. Record 0023 and its index line (`docs/decisions/README.md:86`) already exist.

Two minor objections:

1. minor — The step-4 instruction "passing a `new DockerHubAccessTokenCache(TimeProvider.System)` in a `using`" cannot work at `DockerHubClientTests.cs:2418`. There it sits inside the static helper `CreateClient`, which returns the client and is called about 53 times. A `using` there would dispose the cache's `SemaphoreSlim` before the client uses it, and the first authenticated call would fail with `ObjectDisposedException`. Instruction for the Dev: use a `using` only at the inline sites (`:1038`, `DockerScoutVulnerabilityProviderTests.cs:1205`, `RegistryBaseImageResolverTests.cs:110`). In `CreateClient`, create the cache without disposing it, the same way those tests already leave the client undisposed. Alternatively, have the optional `accessTokenCache` parameter the Tester adds be owned and disposed by the calling test. Do not dispose it inside the helper.

2. minor — The security-considerations bullet "a hanging login blocks other authenticated web API requests for at most its `RequestTimeoutSeconds`" understates the bound. The login goes through the throttle of the instance doing it (`RequestAccessTokenAsync` calls `SendThrottledAsync`, `DockerHubClient.cs:1047`, which waits on `_requestThrottle` at `:919` before the HTTP timeout starts). So every scope waits on the shared lock for that instance's throttle queue plus the timeout. Instruction for the Dev and Reviewer: if this bound appears in `registries.md` or record 0023, word it as "for as long as the login of the instance holding the lock takes (its throttle wait plus `RequestTimeoutSeconds`)", not as a flat `RequestTimeoutSeconds`.

## Security notes

Security plan review: specs/issue-25/plan.md (issue #25, tier `security`). The plan needs one change before it can go ahead.

The tier is right: this changes how a credential is held. Both touched areas are listed under *Security areas touched*: Secrets in configuration (the PAT fingerprint) and Registry and Docker Scout calls (the bearer token). Nothing else is touched. There are no new log calls, messages or configuration keys, no change to parsing limits, and no CI, Docker or dependency change.

## Blocking

**B1. The plan's bound on a hanging login is wrong. With a process-wide lock, one stalled login can block every scope with no time limit.**

- **The claim.** Plan, *Security considerations*, last bullet, repeated in record 0023 under *Consequences*: "a hanging login blocks other authenticated web API requests for at most its `RequestTimeoutSeconds`".
- **Why it is wrong.** `RequestAccessTokenAsync` sends the login through `SendThrottledAsync`, which uses `HttpCompletionOption.ResponseHeadersRead` (`src/DockerUpdateGuard/DockerHub/DockerHubClient.cs`, `SendThrottledAsync`). It then reads the body with `RemoteJsonDocument.ParseAsync(responseStream, cancellationToken)`, guarded only by the caller's token. `TransientHttpRetryHandler` buffers only the request content (`src/DockerUpdateGuard/Infrastructure/TransientHttpRetryHandler.cs:318`), not the response. `HttpClient.Timeout` stops applying once the headers have arrived, so a login response that sends its headers and then stalls keeps the lock until the lock holder's caller cancels. A background scan's caller may never cancel.
- **Measurement.** I ran a scratch console app, `/tmp/claude-0/-home-user-DockerUpdateGuard/0c03e925-bebf-5692-8483-8cda327431e0/scratchpad/probe/Program.cs`, with `timeout 120 dotnet run -c Release` on .NET SDK 10.0.404. It sets `HttpClient.Timeout = 2s` and a loopback server that sends `200`, `Content-Length: 100` and a partial body, then stalls. The body is read with `JsonDocument.ParseAsync(stream, CancellationToken.None)`, as `RemoteJsonDocument` does. Output:
  ```
  headers after 0.0s status 200
  body read still pending after 8.1s (HttpClient.Timeout = 2s)
  ```
- **Why it matters now.** Today such a stall blocks only the logins of one scope's client instance. After this change the lock is process-wide: every authenticated Docker Hub web API request of every scan, discovery run and circuit waits behind it. Waiters can cancel their own wait (AC5), but background scans may pass a token that never fires. Nothing in the plan tests the bound.
- **Required change.** Give the login delegate a real time limit that covers the whole `RequestAccessTokenAsync`, including the body read. For example, link a `CancellationTokenSource` to the delegate's token and cancel it after `RequestTimeoutSeconds`, built from the injected `TimeProvider` (`new CancellationTokenSource(TimeSpan, TimeProvider)`), or read the login response completely within the timeout. Then:
  - Add an acceptance criterion and a Tester test: a login whose body never completes releases the lock after the timeout, stores nothing, and the next caller logs in.
  - Keep the *Security considerations* bullet and 0023 *Consequences* sentence only once that holds; otherwise reword both so they state the real behavior.

## Non-blocking

**N1. The cache key leaves out the login origin.** The token is keyed only by user name and PAT fingerprint. `LoginRetryAfterRegistry`, which has the same process-wide reach, keys by login URI plus user name. There is no defect today: `ApiBaseUrl` is fixed at startup and `ServiceCollectionExtensions.cs:73` is the only `DockerHubClient` registration (fact 6; I found no other production construction). But the cache is a public singleton, and a token cached for one origin would otherwise be sent as a bearer to another. Smallest fix: add a `Uri loginUri` parameter to `GetOrRequestAsync` (from `CreateLoginUri()`) and compare it in the entry match, plus one test. Recommendation: fix now, since it costs one parameter and one test.

## Checked and fine

- Only a SHA-256 fingerprint of the PAT is held, never the PAT itself, compared with `FixedTimeEquals`. Docker Hub PATs are high-entropy, so an unsalted hash kept in memory is acceptable.
- The user name is compared ordinally after trimming. A stricter match than needed only costs an extra login, never a wrong token.
- The fast path without the lock reads one immutable entry with `Volatile.Read`/`Volatile.Write` and checks the credentials before returning it, so AC3 holds there as well as under the lock.
- A failed or deferred login stores nothing. The Retry-After check stays under the lock, after the second cache check, as in today's `:994-1002`.
- A malformed token payload still throws and stores nothing, matching fact 9 and the *Limits and errors* section of `docs/areas/registries.md`.
- A token kept for its full lifetime and reused when refused early is accepted in option 5 and the record, and is no longer-lived than the Docker Scout provider's token.

The plan's other library-behavior facts (typed client lifetime, `TimeProvider` registered as a singleton) are backed by code references. I measured only the timeout claim behind B1. Fact 11 (the token's lifetime) is honestly marked unverified and no security property depends on it.

Files: /home/user/DockerUpdateGuard/specs/issue-25/plan.md, /home/user/DockerUpdateGuard/docs/decisions/0023-the-docker-hub-web-api-token-is-cached-process-wide-per-credentials.md, /home/user/DockerUpdateGuard/src/DockerUpdateGuard/DockerHub/DockerHubClient.cs, /home/user/DockerUpdateGuard/src/DockerUpdateGuard/Infrastructure/TransientHttpRetryHandler.cs, /home/user/DockerUpdateGuard/src/DockerUpdateGuard/ServiceCollectionExtensions.cs
