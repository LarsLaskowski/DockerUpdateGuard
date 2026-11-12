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