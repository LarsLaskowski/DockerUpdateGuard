# Plan: Do not log in again before a login's Retry-After has passed

Source: Issue #320
Tier: standard — a behavior change in when the Docker Hub and Portainer clients send their logins; it creates, changes or removes no security control (no guard on what an attacker may reach, no change to how credentials are stored or sent).
Security areas touched: Registry and Docker Scout calls (`DockerHub/DockerHubClient.cs`); Container actions (`Portainer/PortainerClient.cs`); Failure messages and remote text (new failure messages built from a remote `Retry-After`, `Infrastructure/TransientHttpRetryHandler.cs` refactored)

## Problem / root cause

- Docker Hub: `GetAccessTokenAsync` (`src/DockerUpdateGuard/DockerHub/DockerHubClient.cs:947-985`) returns a failed
  token result without remembering it, so the next authenticated request (`ApplyAuthenticationAsync`, line 907, from
  every `SendGetAsync`, line 833) calls `RequestAccessTokenAsync` (line 994) and posts `v2/users/login` again. The
  client is a typed `HttpClient` client (transient) resolved per scope (`ServiceCollectionExtensions.cs:73,112-113`),
  so any per-instance memory would die with the scan or page operation.
- Portainer: `CreateAuthenticatedClientAsync` (`src/DockerUpdateGuard/Portainer/PortainerClient.cs:288-350`) posts
  `/api/auth` (line 317) for every `GetCapabilityAsync` (line 385) and `ExecuteActionAsync` (line 463) and keeps
  nothing of a failed login.
- The retry handler gives up on a non-idempotent 429 whose `Retry-After` exceeds 30 s
  (`TransientHttpRetryHandler.cs:280-306`, decision 0008 option 7), but the callers' next login is outside it.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | Docker Hub `GetAccessTokenAsync` caches no failure and no `Retry-After` | `DockerHubClient.cs:966-974` returns `tokenResult` on failure, only success is stored (976-977) | confirmed |
| 2 | Every later authenticated request of the same instance logs in again | `HasValidAccessToken` (1041-1045) is false without a token, so 951/961 fall through to the login | confirmed |
| 3 | Portainer posts `/api/auth` for every capability check and action, no JWT cache | `PortainerClient.cs:317`, called from 385 and 463; no field holds a JWT | confirmed |
| 4 | Logins inside the window come from the callers, not from the retry handler (#316) | `TransientHttpRetryHandler.cs:298-305` returns the 429 at once when `Retry-After` > 30 s; decision 0008, option 7 and *Consequences* | confirmed |
| 5 | A place that outlives a client instance is needed for Docker Hub | `AddHttpClient<DockerHubClient>` registers a transient typed client; `IDockerHubClient` and `IRegistryMetadataClient` each resolve their own instance per scope (`ServiceCollectionExtensions.cs:73,112-113`) | confirmed |
| 6 | `PortainerClient` is a singleton | `ServiceCollectionExtensions.cs:104` | confirmed |
| 7 | The 429 with its `Retry-After` header reaches the client when the handler does not retry it (or the retries are used up) | `TransientHttpRetryHandler.cs:354-366` returns the response unchanged | confirmed |
| 8 | A typed client's extra constructor parameter is resolved from the container | to verify: AC7's test resolves both clients from the container (Tester, step 5; red until step 6). Basis: `ITypedHttpClientFactory` activates the client with `ActivatorUtilities`, which takes every parameter but the `HttpClient` from the provider | unverified |
| 9 | A Portainer authentication failure's message reaches the result unchanged | `PortainerAuthenticationException : ReportableException`; `FailureReason.cs:53-56` returns its message | confirmed |
| 10 | A Docker Hub token failure's message reaches the operation's result | `ApplyAuthenticationAsync` (929-932) → synthetic 401 with the message as body (`CreateAuthenticationFailureResponse`, 203-211) → `CreateFailureResultAsync` (221-229) puts it after `Body:` | confirmed |
| 11 | Decision 0008 is released | it is contained in `v1.3.8` (`git tag --contains`) | confirmed |

Related observation (not fixed here): `PortainerClient` sends absolute paths (`/api/auth`, `/api/endpoints`, …), so a
`BaseUrl` with a path prefix (`https://host/portainer/`) is reached without its prefix — proposed as a follow-up issue.

## Acceptance criteria

The clock is the injected `TimeProvider`; `<until>` is the end of the window in UTC formatted
`yyyy-MM-ddTHH:mm:ssZ` (invariant culture, seconds truncated).

- AC1: After the Docker Hub login answered 429 with a positive `Retry-After` (delta or date), an authenticated Docker
  Hub request made before the window ends — through the same client instance or through another instance sharing the
  registry — sends no request at all (no `v2/users/login`, no web API request) and answers *failed* with a message that
  contains `Docker Hub authentication token request for '<user>' not sent: Docker Hub answered HTTP 429 and asked to wait until <until>`.
- AC2: Once the clock reaches the end of the window, the next authenticated Docker Hub request logs in again.
- AC3: A Docker Hub login answered 429 without `Retry-After`, or with a non-429 failure (401, 503) that carries one,
  opens no window: the next authenticated request logs in again, as today.
- AC4: After the Portainer login answered 429 with a positive `Retry-After`, a capability check or an action of any
  Docker instance whose login goes to the same URL with the same user name, made before the window ends, sends no
  request and answers with the authentication error
  `Portainer authentication for '<masked BaseUrl>' not sent: Portainer answered HTTP 429 and asked to wait until <until>`
  — as capability message `Portainer connection error: <that text>` and as action message
  `Action '<action>' failed: <that text>`.
- AC5: Once the clock reaches the end of the window, the next Portainer capability check or action logs in again.
- AC6: The registry keeps a window per login URL (without user info, query and fragment) and user name (ordinal): a
  different user name or host is not deferred; a later window never shortens a longer one; a successful login removes
  the entry; a window that has passed is removed when read.
- AC7: The composition root registers the registry as a singleton: the Docker Hub clients of two scopes and the
  Portainer client get the same instance.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | DockerHubClientLoginRetryAfterDeltaDefersNextRequestOfSameInstance, DockerHubClientLoginRetryAfterDateDefersRequestOfOtherInstance | `src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs` |
| AC2 | DockerHubClientLoginRetryAfterPassedLogsInAgain | `src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs` |
| AC3 | DockerHubClientLoginTooManyRequestsWithoutRetryAfterLogsInAgain, DockerHubClientLoginNonTooManyRequestsWithRetryAfterLogsInAgain (data rows 401 and 503) | `src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs` |
| AC4 | PortainerClientLoginRetryAfterDefersCapabilityCheck, PortainerClientLoginRetryAfterDefersActionOfOtherInstance | `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs` |
| AC5 | PortainerClientLoginRetryAfterPassedLogsInAgain | `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs` |
| AC6 | LoginRetryAfterRegistryOtherUserNameIsNotDeferred, LoginRetryAfterRegistryOtherHostIsNotDeferred, LoginRetryAfterRegistryUserInfoAndQueryAreIgnored, LoginRetryAfterRegistryShorterWindowKeepsLongerOne, LoginRetryAfterRegistrySuccessRemovesWindow, LoginRetryAfterRegistryPassedWindowIsNotDeferred | `src/Tests/DockerUpdateGuard.Tests/LoginRetryAfterRegistryTests.cs` |
| AC7 | ServiceCollectionExtensionsLoginRetryAfterRegistryIsSharedSingleton | `src/Tests/DockerUpdateGuard.Tests/ServiceCollectionExtensionsTests.cs` |

Clock: `Substitute.For<TimeProvider>()` with `GetUtcNow()` configured, as `TransientHttpRetryHandlerTests` does; AC2
and AC5 move it to the end of the window.

## Approach

1. New `LoginRetryAfterRegistry` (`Infrastructure/`): a `ConcurrentDictionary<string, DateTimeOffset>` keyed by
   `loginUri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped)`, a line feed and
   the user name. `RecordLoginResponse`: a success status removes the entry; a 429 whose `Retry-After` gives a positive
   wait stores `now + wait`, keeping the later of an existing and the new instant (`AddOrUpdate`); anything else leaves
   the entry as it is. `IsLoginDeferred`: true with the stored instant while `now` is before it; otherwise removes the
   entry and returns false. No cap on the wait (decision 0022, option 5); the runtime already treats a delta beyond
   `Int32` seconds as absent, so `now + wait` cannot overflow.
2. New internal static `RetryAfterHeader.GetWait` (`Infrastructure/`) holds the body of today's
   `TransientHttpRetryHandler.GetRetryAfter` (lines 208-233) with `now` as a parameter; the handler's `GetRetryAfter`
   calls it with `_timeProvider.GetUtcNow()`. No behavior change; `TransientHttpRetryHandlerTests` cover it unchanged.
3. Docker Hub: login URI is `new Uri(GetApiBaseUri(), "v2/users/login")`, user name the trimmed `UserName`. In
   `GetAccessTokenAsync`, inside the lock after the second `HasValidAccessToken` check, a deferred login returns
   `Failed` with AC1's text; `RequestAccessTokenAsync` passes every login response to `RecordLoginResponse` before it
   evaluates the status.
4. Portainer: login URI is `new Uri(client.BaseAddress, "/api/auth")`, user name `Username` as configured. In
   `CreateAuthenticatedClientAsync`, after the insecure-HTTP warning and before the post, a deferred login throws
   `PortainerAuthenticationException` with AC4's text (`maskedBaseUrl` as today); the login response goes to
   `RecordLoginResponse` before its status is evaluated. API-token instances never log in and are unaffected.
5. Composition root: `services.TryAddSingleton(TimeProvider.System)` and
   `services.AddSingleton<LoginRetryAfterRegistry>()`.
6. No new log events: the 429 itself is logged as today (Docker Hub `DockerHubRequestFailed`, Portainer
   `PortainerAuthFailed`), and the deferred failure reaches the existing failure logs and results with its text.

## Options considered

See [decision 0022](../../docs/decisions/0022-a-logins-retry-after-window-is-remembered-process-wide.md): a shared
registry (chosen) against refusing the send in the retry handler, per-client state, a Portainer JWT cache, a capped
window, a persisted window, and remembering a 429 without `Retry-After` or a 503. Also weighed: keeping the old
constructors next to new ones to spare the test call sites — lost, because two public constructors that both accept an
`HttpClient` make `ActivatorUtilities` refuse the typed client, and a constructor that creates its own registry
silently undoes the fix.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Infrastructure/LoginRetryAfterRegistry.cs` | new |
| DockerUpdateGuard | `Infrastructure/RetryAfterHeader.cs` | new, body moved from the handler |
| DockerUpdateGuard | `Infrastructure/TransientHttpRetryHandler.cs` | `GetRetryAfter` delegates |
| DockerUpdateGuard | `DockerHub/DockerHubClient.cs` | constructor, deferral and recording |
| DockerUpdateGuard | `Portainer/PortainerClient.cs` | constructor, deferral and recording |
| DockerUpdateGuard | `ServiceCollectionExtensions.cs` | two registrations |

## Signatures (for the Dev's skeleton)

- `public sealed class LoginRetryAfterRegistry` (namespace `DockerUpdateGuard.Infrastructure`)
- `public LoginRetryAfterRegistry(TimeProvider timeProvider)`
- `public bool IsLoginDeferred(Uri loginUri, string userName, out DateTimeOffset retryAt)`
- `public void RecordLoginResponse(Uri loginUri, string userName, HttpResponseMessage response)`
- `internal static class RetryAfterHeader` (namespace `DockerUpdateGuard.Infrastructure`)
- `internal static TimeSpan? GetWait(HttpResponseMessage? response, DateTimeOffset now)`
- Changed: `public DockerHubClient(HttpClient httpClient, ILogger<DockerHubClient> logger, IOptionsMonitor<DockerUpdateGuardOptions> optionsMonitor, LoginRetryAfterRegistry loginRetryAfterRegistry)`
- Changed: `public PortainerClient(IHttpClientFactory httpClientFactory, ILogger<PortainerClient> logger, LoginRetryAfterRegistry loginRetryAfterRegistry)`

The old constructors are removed. Names checked against *Writing code* in `.squad/stack.md`: no `Flags`/`Enum`
suffix, no nested type.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/LoginRetryAfterRegistryTests.cs` (new)
- `src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs`
- `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs`
- `src/Tests/DockerUpdateGuard.Tests/ServiceCollectionExtensionsTests.cs`

Existing call sites of the changed constructors, adapted by the Dev in step 4 (mechanically, passing
`new LoginRetryAfterRegistry(TimeProvider.System)`): `DockerHubClientTests.cs` lines 1030 and 2058,
`RegistryBaseImageResolverTests.cs` line 110, and the 32 `new PortainerClient(` sites in `PortainerClientTests.cs`.
Existing tests otherwise unchanged: none of them sends a 429 with `Retry-After` to a login.

Files no unit test can cover: none.

## Areas

- Integration failures: new subsection on logins inside a `Retry-After` window; the #320 *Known gap* removed.
- Registries: Docker Hub web API authentication and *Failed* outcomes.
- Container actions: *Authentication*.

## Documentation updates

All by the Dev, against the code as built:

- `docs/areas/integration-failures.md`: a subsection *Logins inside a Retry-After window* after *Retry of transient
  HTTP failures* (which logins, the key, what opens, extends and ends a window, the fail-fast text, in-memory only,
  only a 429 the client receives); delete the *Known gaps* entry for #320 (and the heading if it is then empty); add
  0022 to *Related decisions*.
- `docs/areas/registries.md`: *Authentication*, Docker Hub web API bullet — the deferral, linking the new subsection;
  *Limits and errors*, *Failed* — a login deferred by a server's window; the "Docker Hub rate limiting (HTTP 429)"
  bullet also links the new subsection.
- `docs/areas/container-actions.md`: *Authentication* — the deferral and its error text next to "The login is repeated
  for every operation".
- `docs/ARCHITECTURE.md` §6, after the `TransientHttpRetryHandler` paragraph: one sentence on the singleton
  `LoginRetryAfterRegistry` shared by the Docker Hub and Portainer clients, linking Integration failures and 0022.

## Architecture check

Layering holds: the registry lives in the host project's `Infrastructure/` next to the retry handler, registered in the
composition root. The guarantee "integration calls answer expected failures with an outcome and a fixed reason"
holds: the deferral is a composed message (fixed text, configured user name or masked URL, a formatted instant).
The single-instance assumption of the scan engine is what makes an in-memory registry sufficient.

## Security considerations

- [x] No limits on parsed input: `Retry-After` is parsed by the runtime as today; the registry holds one entry per
  configured login target and user name, removed on success or expiry.
- [x] Exceptions reaching a user-visible message: `PortainerAuthenticationException` with AC4's text (masked
  `BaseUrl`, never the password); the Docker Hub failed result with AC1's text (user name, never the PAT). The instant
  comes from the parsed header, formatted by the client, never as remote text. The registry key may contain the user
  name and is never logged or shown.

## Decision records

- `docs/decisions/0022-a-logins-retry-after-window-is-remembered-process-wide.md` (Proposed)

## Out of scope / follow-ups

- The Docker Hub registry pull token (`GET auth.docker.io/token`) is no login of this issue; it is idempotent and keeps
  the handler's clamped wait.
- Caching the Portainer JWT (decision 0022, option 4).
- Proposed follow-up issue — title: `[Portainer] Keep the BaseUrl path prefix in API requests`; body: `PortainerClient
  sends absolute paths such as /api/auth and /api/endpoints, so a BaseUrl behind a reverse-proxy prefix
  (https://host/portainer/) is called without the prefix. Resolve the request paths relative to BaseUrl, or reject a
  BaseUrl with a path at validation. Found while planning #320.`
