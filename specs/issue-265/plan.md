# Plan: DockerHub:MaxParallelRequests limits each client instance instead of the whole process

Source: Issue #265
Tier: standard — a behavior change (one process-wide, reload-following limit instead of a per-instance throttle) that creates, changes or removes no security control: the cap keeps Docker Hub from rate-limiting us, it guards nothing an attacker could reach.
Security areas touched: Registry and Docker Scout calls (`DockerHub/DockerHubClient.cs`: how its requests are sent)

## Problem / root cause

The throttle is a per-instance field, `DockerHubClient._requestThrottle` (`src/DockerUpdateGuard/DockerHub/DockerHubClient.cs:89`),
a `SemaphoreSlim` sized once from `optionsMonitor.CurrentValue.DockerHub.MaxParallelRequests` in the constructor (`:115-116`)
and taken in `SendThrottledAsync` (`:911-926`). `DockerHubClient` is a transient typed client
(`ServiceCollectionExtensions.cs:71-77`) resolved once per DI scope (`:115-116`), so every scope has its own allowance and a
reload reaches only instances created after it.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | The throttle is a per-instance `SemaphoreSlim`, sized once in the constructor, used by `SendThrottledAsync` and disposed in `Dispose` | `DockerHubClient.cs:89`, `:115-116`, `:911-926`, `:1559-1562` (the issue's line numbers `:84`, `:114-115`, `:850-865` are stale; the content matches) | confirmed |
| 2 | `DockerHubClient` is transient (typed client) | `ServiceCollectionExtensions.cs:71-77` `AddHttpClient<DockerHubClient>` | confirmed |
| 3 | One scope holds two `DockerHubClient` instances ("2 ×" in the failure scenario) | As the issue comment says: only `DockerHubAccountImageDiscoveryService` takes `IDockerHubClient` (`DockerHubAccountImageDiscoveryService.cs:70`), resolved alone in the discovery job's scope (`DockerHubAccountImageDiscoveryBackgroundService.cs:79`); every other scope reaches the client only through `IEnumerable<IRegistryMetadataClient>` of `RegistryMetadataService` (`RegistryMetadataService.cs:28`) | refuted |
| 4 | N concurrent operations can have N × `MaxParallelRequests` requests in flight | one scoped `DockerHubClient` per scope (fact 3), one semaphore per instance (fact 1) | confirmed |
| 5 | A reloaded `MaxParallelRequests` reaches only later instances | read once in the constructor, `:115` | confirmed |
| 6 | `OciRegistryClient` has no limit | no `SemaphoreSlim` or throttle in `Images/OciRegistryClient.cs` | confirmed |
| 7 | The login waits for a slot while holding a lock (the issue's `_tokenRefreshLock`) | the field is renamed: that field no longer exists: since #25 the lock is `DockerHubAccessTokenCache._loginLock` (`DockerHubAccessTokenCache.cs:50`, `:148-177`), a singleton (`ServiceCollectionExtensions.cs:104`); the login sends through `SendThrottledAsync` (`DockerHubClient.cs:1036`) inside it | confirmed |
| 8 | No slot holder ever waits for the login lock or for a second slot, so a process-wide limit of 1 cannot deadlock | authentication runs before the slot is taken (`SendGetAsync`, `:806` before `:813`); every slot is released when `SendAsync` returns headers (`ResponseHeadersRead`, `:917-925`), before the body read and before the next request (`GetImageConfigDigestAsync` `:627` then `:646`) | confirmed |
| 9 | A slot is held across the retries and `Retry-After` waits of a request | `TransientHttpRetryHandler` is inside the typed client's pipeline (`ServiceCollectionExtensions.cs:77`), so inside `_httpClient.SendAsync` (`:917`) | confirmed |
| 10 | `RequestTimeoutSeconds` does not bound the slot wait, except for the login | it is `HttpClient.Timeout` (`ServiceCollectionExtensions.cs:74`), which starts inside `SendAsync`, after `WaitAsync` (`:913`); the login's own time limit covers its slot wait (`DockerHubAccessTokenCache.cs:158-161`, `registries.md:155`) | confirmed |
| 11 | `IOptionsMonitor.OnChange` fires on a configuration reload | `AddOptions<DockerUpdateGuardOptions>().Bind(optionsSection)` (`ServiceCollectionExtensions.cs:53-55`) registers the configuration change token source; the area document already lists other live keys | confirmed |
| 12 | `TestOptionsMonitor<T>` cannot simulate a reload | `CurrentValue` get-only, `OnChange` returns `NullDisposable` without keeping the listener (`Data/TestOptionsMonitor{TOptions}.cs`) | confirmed |
| 13 | The Docker Scout provider's Docker Hub login does not pass through `DockerHubClient` | own `HttpClient`, `DockerScoutVulnerabilityProvider.cs:267`; default `https://hub.docker.com/v2/users/login` (`VulnerabilityOptions.cs:33`); related, out of scope | confirmed |
| 14 | No test disposes a `DockerHubClient` | no `using`/`Dispose` on a `DockerHubClient` under `src/Tests` | confirmed |

## Acceptance criteria

- AC1: However many `DockerHubClient` instances share the limiter, the Docker Hub requests they have in flight together never exceed `MaxParallelRequests`, and that number is reached.
- AC2: Raising `MaxParallelRequests` through a reload while requests wait admits waiting requests up to the new limit at once, without a release and without a new limiter or client.
- AC3: Lowering `MaxParallelRequests` through a reload admits no new request until the in-flight count is below the new limit.
- AC4: A wait for a slot that is cancelled (while queued, or with a token already cancelled) throws `OperationCanceledException` and takes no slot: the slot count afterwards is as before the wait.
- AC5: A Docker Hub request that fails — an exception from the transport, or a non-success status — releases its slot.
- AC6: With `MaxParallelRequests` 1 and a PAT, concurrent authenticated requests of two client instances (the login included) all complete.
- AC7: `DockerHubRequestLimiter` is registered as a singleton that the `DockerHubClient` instances of different scopes share; `DockerHubClient` holds no `SemaphoreSlim` and is no `IDisposable`.
- AC8: The README row of `MaxParallelRequests` reads exactly: `Maximum number of Docker Hub requests in flight at once for the whole process (1–32); applied on configuration reload`.
- AC9: `docs/areas/registries.md` describes the process-wide, reload-following limit, and its *Known gaps* entry for #265 is gone (with it the half-sentence "and the client instances one scan uses").

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `DockerHubRequestLimiterConcurrentWaitsNeverExceedLimitAsync`, `DockerHubClientTwoInstancesShareProcessWideLimitAsync` | `DockerHubRequestLimiterTests.cs`, `DockerHubClientTests.cs` |
| AC2 | `DockerHubRequestLimiterLimitRaisedOnReloadAdmitsWaitingRequestsAsync` | `DockerHubRequestLimiterTests.cs` |
| AC3 | `DockerHubRequestLimiterLimitLoweredOnReloadAdmitsNoNewRequestUntilBelowLimitAsync` | `DockerHubRequestLimiterTests.cs` |
| AC4 | `DockerHubRequestLimiterCancelledWaitLeaksNoSlotAsync`, `DockerHubRequestLimiterPreCancelledWaitTakesNoFreeSlotAsync` | `DockerHubRequestLimiterTests.cs` |
| AC5 | `DockerHubClientTransportFailureReleasesSlotAsync`, `DockerHubClientNonSuccessResponseReleasesSlotAsync` | `DockerHubClientTests.cs` |
| AC6 | `DockerHubClientAuthenticatedRequestsWithLimitOneCompleteAsync` | `DockerHubClientTests.cs` |
| AC7 | `ServiceCollectionExtensionsDockerHubRequestLimiterIsSharedSingleton` | `ServiceCollectionExtensionsTests.cs` |
| AC8 | manual: the Reviewer compares the README row with the text of AC8 in step 8 | `README.md` |
| AC9 | manual: the Reviewer reads `registries.md` against *Documentation updates* in step 8 | `docs/areas/registries.md` |

Cases the tests hold (no wall-clock waits; a free slot is granted synchronously, so `IsCompleted` of a returned task is
a valid probe):

- AC1 limiter: limit 2, five waits → exactly two tasks complete; one release → exactly three.
- AC1 client: two clients, one limiter at limit 2, a stub handler that holds each request until the test opens its
  gate and counts concurrent calls; six requests across both clients → the handler sees exactly 2 while the gate is
  closed, at most 2 throughout, and all six succeed after it opens.
- AC2: limit 1, one held, two waiting; reload to 3 → both waiting tasks complete with no release.
- AC3: limit 2, two held, one waiting; reload to 1; first release → still waiting; second release → admitted.
- AC4: limit 1, one held; a queued wait cancelled → throws; release; a new wait completes at once and the next one
  waits (the cancelled wait took nothing). Free slot and a cancelled token → throws, and a following wait completes
  at once.
- AC5: limit 1; the handler throws `HttpRequestException` (the call answers *failed*) / answers 500; afterwards
  `WaitAsync` on the shared limiter completes at once.
- AC6: limit 1, PAT and user name set, login answered with a token; four concurrent `GetCurrentUserAsync` over two
  clients sharing limiter and token cache all succeed (a hang fails the test through MSTest `[Timeout]`).
- AC7: the root and two scopes resolve one limiter, the clients of both scopes hold it (field by type, as the token
  cache test does), and `typeof(DockerHubClient)` declares no `SemaphoreSlim` field and does not implement `IDisposable`.

## Approach

New `DockerHub/DockerHubRequestLimiter.cs`, a sealed singleton:

- State under one `System.Threading.Lock`: the in-flight count, the current limit, a FIFO queue of waiters
  (`TaskCompletionSource` with `RunContinuationsAsynchronously`).
- The limit is `Math.Max(1, MaxParallelRequests)`, read from `CurrentValue` in the constructor and then taken from
  the argument of the `OnChange` listener the constructor registers; `WaitAsync` and `Release` read no options.
- `WaitAsync`: an already-cancelled token → a cancelled task, nothing taken. Count below the limit and no one queued →
  count + 1 and a completed task (synchronous grant). Otherwise enqueue, register the cancellation outside the lock;
  the cancellation callback removes the waiter under the lock only if it is still queued and cancels it. A granted
  waiter keeps its slot even if its token fires later (its caller's `finally` releases it).
- `Release`: count − 1 (a release with count 0 throws `InvalidOperationException`), then admit queued waiters while
  count < limit. The `OnChange` listener sets the limit and admits the same way; a lowered limit admits nobody until
  the count drops below it.
- `Dispose` disposes the `OnChange` registration only.

`DockerHubClient`: the constructor takes the limiter; `_requestThrottle`, its sizing and `Dispose` go, and the class
drops `IDisposable`. `SendThrottledAsync` keeps its shape: `await _requestLimiter.WaitAsync(cancellationToken)`, then
`SendAsync` in `try`, `Release()` in `finally` — the slot spans the same part of a request as today (fact 9). Composition
root: `services.AddSingleton<DockerHubRequestLimiter>();` beside `DockerHubAccessTokenCache`.

Bound and seam: the bound is on Docker Hub over HTTP; tests observe it through the stub handler's concurrent-call count
(AC1, client) and through the completion of the limiter's tasks (AC1–AC4).

## Options considered

The record lists them ([0030](../../docs/decisions/0030-docker-hub-request-parallelism-is-limited-process-wide-and-follows-reloads.md)):
a singleton limiter called where the client sends (chosen); a `DelegatingHandler` with the same gate (the issue's
option 2: outside the retry handler only more wiring, inside it a change to how retries hold slots that nobody
decided); `ConcurrencyLimiter` (fixed permit limit, no live reload); one singleton `SemaphoreSlim` (cannot shrink);
a singleton `DockerHubClient` (rejected in 0023). An interface for the limiter was not taken: the other process-wide
helpers the client uses (`LoginRetryAfterRegistry`, `DockerHubAccessTokenCache`) are concrete, and tests use the real one.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `DockerHub/DockerHubRequestLimiter.cs` | new |
| DockerUpdateGuard | `DockerHub/DockerHubClient.cs` | constructor, `SendThrottledAsync`, `_requestThrottle` and `Dispose` removed, no `IDisposable` |
| DockerUpdateGuard | `ServiceCollectionExtensions.cs` | singleton registration |

## Signatures (for the Dev's skeleton)

- `public sealed class DockerHubRequestLimiter : IDisposable` (namespace `DockerUpdateGuard.DockerHub`)
- `public DockerHubRequestLimiter(IOptionsMonitor<DockerUpdateGuardOptions> optionsMonitor)`
- `public Task WaitAsync(CancellationToken cancellationToken)`
- `public void Release()`
- `public void Dispose()`
- `public DockerHubClient(HttpClient httpClient, ILogger<DockerHubClient> logger, IOptionsMonitor<DockerUpdateGuardOptions> optionsMonitor, LoginRetryAfterRegistry loginRetryAfterRegistry, DockerHubAccessTokenCache accessTokenCache, DockerHubRequestLimiter requestLimiter)`
- `public sealed class DockerHubClient : IDockerHubClient, IRegistryMetadataClient`
- Deleted: `DockerHubClient.Dispose()` is removed, with the private field `_requestThrottle`.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/DockerHubRequestLimiterTests.cs` (new)
- `src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs`
- `src/Tests/DockerUpdateGuard.Tests/ServiceCollectionExtensionsTests.cs`

Test doubles the Tester extends: `Data/TestOptionsMonitor{TOptions}.cs` keeps the listeners `OnChange` receives and gets
`public void Reload(TOptions currentValue)`, which replaces `CurrentValue` and calls them;
`Helper/ConcurrencyTrackingHttpMessageHandler.cs` gets a gated mode (requests held until the test opens the gate) and
the current concurrent count. Their `.squad/project.md` lines still fit; a changed one is reported as text.

Existing call sites of the changed constructor, adapted by the Tester (step 5): `DockerHubClientTests.cs:1065`
(`DockerHubClientSendHonorsMaxParallelRequestsAsync`, inputs unchanged) and the helper at `:3122` (a limiter from the
same options monitor), `DockerScoutVulnerabilityProviderTests.cs:1207`, `RegistryBaseImageResolverTests.cs:112`. No
existing test has an input a criterion handles differently.

Files no unit test can cover: none.

## Areas

`docs/areas/registries.md` (Registries): the parallelism rule, its reload behavior, the timeout note, *Known gaps*, the
related decisions — see below.

## Documentation updates

All by the Dev, against the code as built:

- `README.md:126`: the `MaxParallelRequests` description becomes the text of AC8.
- `docs/areas/registries.md`, *Docker Hub request budget and parallelism*: the `MaxParallelRequests` bullet says the
  cap is one for the whole process, over every Docker Hub client instance and scope (web API, login, token service,
  registry); a slot is held from sending until the response headers arrive, retries and `Retry-After` waits included,
  the body read not; waiters are admitted in arrival order; a reload applies to the next admission (raised: waiters
  admitted at once; lowered: requests in flight finish, new ones wait until the count is below the new value); the wait
  honors cancellation. Other registries and the Docker Scout provider's requests are not capped.
- Same file, *Request timeout*: the wait for a Docker Hub request slot is not part of `RequestTimeoutSeconds`, except
  for the login (*Authentication*).
- Same file, *Configuration reload*: `DockerHub:MaxParallelRequests` is read live (for the next admission), no longer
  "for new client instances".
- Same file, *Known gaps*: the #265 bullet is removed; *Related decisions*: a 0030 line.
- `docs/ARCHITECTURE.md`, §6 after the `DockerHubAccessTokenCache` sentence (line 252): "The singleton
  `DockerHubRequestLimiter` (`DockerHub\`) bounds the Docker Hub requests in flight for the whole process", linking
  [Registries](areas/registries.md#docker-hub-request-budget-and-parallelism) and 0030.
- `.squad/project.md`: no change (no new security-area file; the test-double lines stay true unless the Tester reports
  otherwise).

## Architecture check

Layering holds: the limiter lives in the host project beside the token cache, wired in the composition root. Untouched:
`TransientHttpRetryHandler`, `RequestTimeoutSeconds` as the per-request total (the slot wait was outside it before as
well), the outcome model of `ExternalOperationResult<T>` (a cancelled wait stays a thrown cancellation, 0029), and the
token cache's single login with its time limit (0023).

## Security considerations

- [x] No limits on parsed input.
- [x] Exceptions reaching a user-visible message: none new. `OperationCanceledException` from a cancelled wait is thrown
  as the caller's cancellation is today; inside a login it is the login's time limit, answered `timed out after <n>
  seconds` as now. `InvalidOperationException` from an unmatched `Release` is a programming error that cannot occur on
  the `try`/`finally` path.

## Decision records

- `docs/decisions/0030-docker-hub-request-parallelism-is-limited-process-wide-and-follows-reloads.md` (Proposed)

## Out of scope / follow-ups

- Requests to other registries stay unlimited (Product Manager's decision in the issue).
- `Scanning:DockerHubRequestLimit*` stays a scheduling hint.
- Related, found while planning: the Docker Scout provider logs in to Docker Hub (`hub.docker.com/v2/users/login` by
  default) through its own `HttpClient`, outside the limit. Proposed issue — title "Docker Scout's Docker Hub login is
  not counted against DockerHub:MaxParallelRequests"; body: "`DockerScoutVulnerabilityProvider` sends its Docker Hub
  login (`Vulnerabilities:DockerScoutLoginUrl`, default `https://hub.docker.com/v2/users/login`) through its own
  `HttpClient`, so it bypasses the process-wide `DockerHubRequestLimiter` of #265. Decide whether that login (and only
  it, not the Scout API) should take a slot."
