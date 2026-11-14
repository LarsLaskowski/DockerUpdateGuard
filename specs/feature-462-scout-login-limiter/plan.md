# Plan: Count Docker Scout's Docker Hub login against DockerHub:MaxParallelRequests

Source: Issue #462 | [spec.md](spec.md)
Tier: standard — a behavior change (the Scout login now waits for a Docker Hub request slot) that creates, changes or removes no security control: the limiter is a client-side cap against upstream throttling, not a guard.
Security areas touched: Registry and Docker Scout calls (`Vulnerabilities/DockerScoutVulnerabilityProvider.cs`)

## Problem / root cause

See [spec.md](spec.md), *Problem*.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | The Scout login is sent through a factory client without the limiter | `DockerScoutVulnerabilityProvider.cs:267` (`client.PostAsJsonAsync`), client from `_httpClientFactory.CreateClient()` at 483; constructor 88–91 takes no limiter | confirmed |
| 2 | Default `DockerScoutLoginUrl` is `https://hub.docker.com/v2/users/login` | `Configuration/VulnerabilityOptions.cs:33` | confirmed |
| 3 | `PostAsJsonAsync` buffers the body (ResponseContentRead) inside the call, so `client.Timeout` (`Vulnerabilities:RequestTimeoutSeconds`, line 485) covers headers and body | `HttpClient.PostAsync` default completion option; provider line 485 | confirmed |
| 4 | The `Retry-After` window is checked before `AuthenticateAsync`, under `_tokenSemaphore` | provider lines 302, 315–323 | confirmed |
| 5 | The Docker Hub client checks its window before its throttled send | `DockerHubClient.cs` login: `IsLoginDeferred` precedes `SendThrottledAsync` (~1003 / ~1036) | confirmed |
| 6 | `DockerHubRequestLimiter` is a DI singleton with `WaitAsync(CancellationToken)` and `Release()`; a cancelled wait takes no slot | `ServiceCollectionExtensions.cs:105`; `DockerHubRequestLimiter.cs`; `DockerHubRequestLimiterTests` cancel tests | confirmed |
| 7 | The provider is a DI singleton, so the container injects the singleton limiter | `ServiceCollectionExtensions.cs:112` | confirmed |
| 8 | No slot holder waits for the provider's `_tokenSemaphore`: only the provider takes it, and `DockerHubClient` never calls the provider | `grep _tokenSemaphore` (provider only); `DockerHubClient.cs` has no Scout reference | confirmed |
| 9 | A caller's cancellation while waiting propagates: `GetVulnerabilitiesAsync` rethrows `OperationCanceledException` when the caller's token is cancelled | provider `catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)` | confirmed |
| 10 | Record 0030 is unreleased | `git merge-base --is-ancestor 6b97e2a v1.3.8` → not an ancestor; no tag contains #463 | confirmed |

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | DockerScoutVulnerabilityProviderLoginWithAllSlotsTakenWaitsUntilSlotFreedAsync; DockerScoutVulnerabilityProviderLoginHoldsSlotUntilBodyReadAsync; DockerScoutVulnerabilityProviderRefreshLoginAfterUnauthorizedHoldsSlotAsync | src/Tests/DockerUpdateGuard.Tests/DockerScoutVulnerabilityProviderTests.cs |
| AC1 | ServiceCollectionExtensionsDockerScoutProviderSharesDockerHubRequestLimiter | src/Tests/DockerUpdateGuard.Tests/ServiceCollectionExtensionsTests.cs |
| AC2 | DockerScoutVulnerabilityProviderLoginOutcomeFreesSlotAsync (data rows: success, HTTP 500, HTTP 429, unreadable body, `HttpRequestException`, `TaskCanceledException` not from the caller's token) | src/Tests/DockerUpdateGuard.Tests/DockerScoutVulnerabilityProviderTests.cs |
| AC3 | DockerScoutVulnerabilityProviderCachedTokenLookupWithAllSlotsTakenCompletesAsync; DockerScoutVulnerabilityProviderLoginInsideRetryAfterWindowWithAllSlotsTakenFailsWithoutLoginAsync | src/Tests/DockerUpdateGuard.Tests/DockerScoutVulnerabilityProviderTests.cs |
| AC4 | DockerScoutVulnerabilityProviderCancelledWhileWaitingForSlotSendsNoLoginAsync; DockerScoutVulnerabilityProviderLoginWithAllSlotsTakenWaitsUntilSlotFreedAsync (also AC1) | src/Tests/DockerUpdateGuard.Tests/DockerScoutVulnerabilityProviderTests.cs |

Test seam for the bound (no real-time wait, `docs/UNIT_TESTS.md`): each provider test passes its own
`DockerHubRequestLimiter` with `MaxParallelRequests = 1` and observes the slot through it — the test takes the slot
with `WaitAsync`, or probes with `WaitAsync` and checks whether the returned task completed (a completed probe is
released, a pending one cancelled).
- *Waits until freed*: the test holds the slot, calls `GetVulnerabilitiesAsync` without awaiting (it reaches the
  limiter synchronously: the token semaphore is free), asserts the task is incomplete and the handler saw no
  request, releases, awaits: one login, result *succeeded*.
- *Holds until body read*: the login response's content stream waits on a `TaskCompletionSource`; the handler signals
  once the login arrived; a probe is then pending; the gate opens, the lookup ends, the probe completes.
- *Refresh*: the handler probes on every login request (first and the one after a Scout 401); every probe is pending.
- *Cached token / window*: with the slot held by the test, the second lookup completes (cached: *succeeded*, one
  login in total; window: *failed* with the deferred message of `DockerHubClient.CreateLoginDeferredMessage`, the
  only login being the first one, answered 429 with `Retry-After: 120`).
- *Cancelled*: slot held, lookup started, its token cancelled: the lookup throws `OperationCanceledException`, the
  handler saw nothing; after the test releases, a probe completes at once (no leaked slot).
- *Outcome frees slot*: after each lookup, a probe completes at once.

## Approach

`DockerScoutVulnerabilityProvider` gets a private readonly field `_requestLimiter` (`DockerHubRequestLimiter`, XML
doc "Process-wide Docker Hub request limiter"), set from a new last constructor parameter. In `AuthenticateAsync`,
before the existing `using (var response = await client.PostAsJsonAsync(...))` block:
`await _requestLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);`, then the whole existing `using` block
inside `try`, with `_requestLimiter.Release();` in `finally`. The slot therefore covers sending, the buffered body,
`RecordLoginResponse` and the JSON read of the buffered body, and `client.Timeout` keeps covering the call as today
(spec N2, *Challenge* 1). The wait sits after the window check of `GetOrCreateTokenAsync` (fact 4), so a deferred
login and a cached token take no slot (AC3); the Scout API requests (`FetchVulnerabilitiesAsync`) are untouched.
The provider does not dispose the limiter (DI owns it). No new user-visible output.

## Options considered

1. Wait and release around the whole login call in `AuthenticateAsync` (chosen): smallest change, keeps the timeout
   over the body.
2. Hold the slot until the headers only (`ResponseHeadersRead`, as `DockerHubClient`): lost in the spec's *Challenge*
   1 — it drops the timeout over the login body.
3. A `DelegatingHandler` on the provider's client: its client also sends the Scout API requests, which #462 leaves
   uncounted; a separate named client only for the login adds wiring for one call.
4. Send the Scout login through `DockerHubClient` and its token cache: out of scope (spec), merges two tokens.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Vulnerabilities/DockerScoutVulnerabilityProvider.cs` | limiter field, constructor parameter, slot around the login |
| DockerUpdateGuard.Tests | the files under *Test files* | new tests, call sites |

## Signatures (for the Dev's skeleton)

Changed constructor (the skeleton rewrites the existing one; `DockerScoutVulnerabilityProvider.cs` is not final):

`public DockerScoutVulnerabilityProvider(IHttpClientFactory httpClientFactory, IOptionsMonitor<DockerUpdateGuardOptions> optionsMonitor, ILogger<DockerScoutVulnerabilityProvider> logger, LoginRetryAfterRegistry loginRetryAfterRegistry, DockerHubRequestLimiter requestLimiter)`

The parameter `requestLimiter` is documented "Process-wide Docker Hub request limiter". No other API changes.

## Test files

- src/Tests/DockerUpdateGuard.Tests/DockerScoutVulnerabilityProviderTests.cs
- src/Tests/DockerUpdateGuard.Tests/ServiceCollectionExtensionsTests.cs

Existing call sites of the constructor, adapted by the Dev in step 4 (the old signature goes away), mechanically with
`new DockerHubRequestLimiter(optionsMonitor)` from the options monitor already at hand: 
`DockerScoutVulnerabilityProviderTests.cs:600` and `:1463` (`CreateProvider`), `DockerScoutTokenExpiryTests.cs:158`,
`VulnerabilityProviderResolverTests.cs:83`. The Tester then adds an optional `DockerHubRequestLimiter? requestLimiter`
parameter to `CreateProvider` for the new tests. No existing test has an input a criterion handles differently: with
the default limit 4 and one lookup at a time, every slot is free.

Files no unit test can cover: none.

## Areas

- Registries: the Scout login counts against `MaxParallelRequests`.
- Vulnerabilities: the Scout login may wait for a Docker Hub slot.

## Documentation updates

All by the Dev, once the code is green:
- `docs/areas/registries.md`, *Docker Hub request budget and parallelism*: the cap also counts the Docker Scout
  provider's login, whose slot covers the whole login call, its buffered body read included; the last sentence
  becomes "Requests to other registries and the Docker Scout provider's vulnerability requests are not capped."
  *Request timeout*: "except for the login" becomes "except for the Docker Hub client's login", plus: the Docker Scout
  login's slot wait lies outside `Vulnerabilities:RequestTimeoutSeconds`.
- `docs/areas/vulnerabilities.md`, *Docker Scout*, login bullet: the login takes a Docker Hub request slot
  (`DockerHub:MaxParallelRequests`, link to Registries) and waits for one after the `Retry-After` check; lookups with a
  cached token and the Scout API requests take none.
- `docs/ARCHITECTURE.md` line ~255: the limiter bounds the Docker Hub requests of the whole process, the Docker Scout
  provider's Docker Hub login included.
- `README.md` line 126 (`MaxParallelRequests`): "… for the whole process, the Docker Scout login included (1–32) …".
- No changelog file exists; the decision record is mine (below).

## Architecture check

The guarantee "one Docker Hub limit for the process" (ARCHITECTURE.md, the limiter paragraph; 0030) is extended, not
weakened. No lock-order risk (fact 8). The startup sequence and composition root are unchanged: DI resolves the new
parameter from the existing singleton.

## Security considerations

- [x] No limits on parsed input change.
- [x] Exceptions: a cancelled wait propagates as `OperationCanceledException` only on the caller's cancellation (fact
  9), with no message; `Release()`'s `InvalidOperationException` cannot occur (it runs only after a granted wait); all
  other exceptions take today's path (`Docker Scout scan failed: <FailureReason>`). Credentials, URLs and logs are
  unchanged.

## Decision records

- `docs/decisions/0030-docker-hub-request-parallelism-is-limited-process-wide-and-follows-reloads.md` (Accepted,
  unreleased, extended in place: it records the same decision — which requests the one limit counts): option 6 with
  its alternatives, *Decision* and *Consequences* updated.

## Out of scope / follow-ups

As the spec's *Out of scope*.
