# Plan: Keep the BaseUrl path prefix in Portainer API requests

Source: Issue #412
Tier: standard — the request addresses of the Portainer client change (behavior), and no security control is created, changed or removed: no guard, allow-list or credential handling changes, only the path the existing requests resolve to.
Security areas touched: Configured URLs and their user info (where the Portainer `BaseUrl` sends the password and API key), Container actions (`Portainer/PortainerClient.cs`), Insecure Portainer HTTP (the warning's scheme check reads the new base address)

## Problem / root cause

`PortainerClient.CreateAuthenticatedClientAsync` sets `client.BaseAddress = new Uri(options.BaseUrl!)`
(`src/DockerUpdateGuard/Portainer/PortainerClient.cs:652`), and every request passes a root-relative path, which by
RFC 3986 reference resolution replaces the base address's whole path: `/api/auth` (`:621` login, `:675` the login URI
of the Retry-After registry), `/api/system/status` (`:469`), `/api/endpoints` (`:405`), `/api/endpoints/{id}`
(`:334`), the container lookup (`:281`) and the action (`:564`). A `BaseUrl` of `https://host/portainer/` is therefore
called at `https://host/api/…`. Even with relative paths, a `BaseUrl` without trailing slash (`https://host/portainer`)
would lose its last segment, so the base address needs a trailing slash too.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | The client sends absolute paths such as `/api/auth` and `/api/endpoints` (issue) | `PortainerClient.cs:281, 334, 405, 469, 564, 621, 675` | confirmed |
| 2 | A `BaseUrl` with a path prefix is called without the prefix (issue) | root-relative references replace the base path (RFC 3986 §5.2.2, used by `new Uri(Uri, string)` and `HttpClient` for a relative request URI); `BaseAddress` set unchanged at `:652` | confirmed |
| 3 | Validation accepts a Portainer `BaseUrl` with a path | `DockerUpdateGuardOptionsValidator.cs:356-375` checks configured, absolute http/https, `@`, plaintext HTTP only | confirmed |
| 4 | The login Retry-After window is keyed on scheme, host, port and path of the login URI | `Infrastructure/LoginRetryAfterRegistry.cs:59` (`SchemeAndServer \| Path`) | confirmed |
| 5 | The existing tests use a `BaseUrl` without path and compare full request URIs (`https://portainer.example.test/api/…`), which the fix leaves unchanged | `PortainerClientTests.cs:30-80`, `:125-130` | confirmed |
| 6 | The Docker instance client already normalizes its base address to a trailing slash (`EnsureTrailingSlash`, private) | `Docker/DockerInstanceClient.cs:405-415` | confirmed |
| 7 | No other Portainer request site exists (no other `HttpClientName` user builds a URL) | `grep "api/" src/DockerUpdateGuard/Portainer` lists only the sites of row 1 | confirmed |

Related defect found on the way: none beyond row 4, which follows the fix (decision 0046, *Consequences*).

## Acceptance criteria

- AC1: With `BaseUrl` `https://portainer.example.test/portainer/` and user name and password, an action with a configured `EndpointId` `7` sends exactly, in this order: `POST https://portainer.example.test/portainer/api/auth`, `GET …/portainer/api/system/status`, `GET …/portainer/api/endpoints/7`, `GET …/portainer/api/endpoints/7/docker/containers/json?all=true&filters=…` and `POST …/portainer/api/endpoints/7/docker/containers/<id>/restart`; no request goes to a path outside `/portainer/`.
- AC2: With `BaseUrl` `https://portainer.example.test/portainer/` and an `ApiToken`, a capability check without `EndpointId` sends `GET …/portainer/api/system/status`, `GET …/portainer/api/endpoints` and `GET …/portainer/api/endpoints/7`.
- AC3: The `BaseUrl` values `https://portainer.example.test/portainer` (no trailing slash), `https://portainer.example.test/portainer/#!/home` (fragment), `https://portainer.example.test/portainer/?x=1` (query) and `https://portainer.example.test/my%20portainer/` (percent-encoded segment) send the login to `https://portainer.example.test/portainer/api/auth` (the last one to `https://portainer.example.test/my%20portainer/api/auth`, the escape kept as it is).
- AC4: A login answered 429 with `Retry-After` for `BaseUrl` `https://portainer.example.test/a/` does not defer the login of a Docker instance with the same user name and `BaseUrl` `https://portainer.example.test/b/`; the login of a second instance with `https://portainer.example.test/a` (no slash) is deferred.
- AC5: A `BaseUrl` without path (`https://portainer.example.test`) keeps today's addresses (`https://portainer.example.test/api/…`).

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `PortainerClientExecuteContainerActionAsyncWithBaseUrlPathSendsEveryRequestBelowThePathAsync` | `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs` |
| AC2 | `PortainerClientCheckCapabilityAsyncWithBaseUrlPathListsEnvironmentsBelowThePathAsync` | `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs` |
| AC3 | `PortainerClientCheckCapabilityAsyncWithBaseUrlPathVariantLogsInBelowThePathAsync` (one `DataRow` per `BaseUrl` with its expected login URI) | `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs` |
| AC4 | `PortainerClientLoginRetryAfterWindowAppliesOnlyToTheSameBaseUrlPathAsync` | `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs` |
| AC5 | the existing tests comparing `StatusUrl`, `EndpointsUrl`, `EndpointCheckUrl`, `AuthUrl`, `ContainersUrl` and `RestartActionUrl` (e.g. `PortainerClientExecuteContainerActionAsyncWithUsernamePasswordLogsInAndUsesBearerTokenAsync`) — they pass before and after the fix | `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs` |

The AC1–AC4 tests record the requests with `SequenceHttpMessageHandler` (or `DelegateHttpMessageHandler`) and compare
the full `RequestUri` strings; `CreateRoutedResponse`, which routes on `AbsolutePath`, does not match a prefixed path.

## Approach

- In `CreateAuthenticatedClientAsync`, set `client.BaseAddress` from a new private static helper (`CreateBaseAddress`,
  in *Static methods*): it parses `BaseUrl`, takes `GetLeftPart(UriPartial.Path)` (scheme, user info, host, port and
  path, escapes as they are; query and fragment dropped) and appends `/` when the path does not end with one. No
  `UriBuilder`: its `Path` setter re-escapes, which AC3's percent-encoded row guards against.
- Drop the leading `/` of every request path in row 1: `api/auth`, `api/system/status`, `api/endpoints`,
  `api/endpoints/{endpointId}`, `api/endpoints/{endpointId}/docker/containers/json?…`,
  `api/endpoints/{endpointId}/docker/containers/{id}/{action}`, and `new Uri(client.BaseAddress, "api/auth")` for the
  registry's login URI. The digits-only `EndpointId`, the 64-hex container ID and the fixed action names stay the only
  variable parts, so no dot segment can climb above the prefix.
- The plaintext-HTTP warning keeps reading `client.BaseAddress.Scheme`; the masked URL in messages stays the configured
  `BaseUrl` (`UrlRedaction.RedactUserInfo(options.BaseUrl)`), so no message text changes.
- Validation is unchanged. No new user-visible output.

## Options considered

1. Resolve below the path of `BaseUrl` — chosen (decision 0046).
2. Reject a `BaseUrl` with a path at validation (the issue's second option) — lost: Portainer behind a path prefix
   could not be used at all, and a `BaseUrl` pasted from the browser with `#!/home` would stop startup.
3. Share one base-address helper with `DockerInstanceClient.EnsureTrailingSlash` in `Infrastructure/` — lost: that
   helper uses `UriBuilder` and keeps the query, its callers resolve other request forms, and moving it widens the
   change into the Docker Engine client for no behavior gain.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Portainer/PortainerClient.cs` | base address with trailing slash, relative request paths |
| DockerUpdateGuard.Tests | `PortainerClientTests.cs` | tests of AC1–AC4 |

## Signatures (for the Dev's skeleton)

None: no public or internal member changes; the new helper is private and described in *Approach*. No skeleton step.

## Test files

`src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs` (existing class `PortainerClientTests`).

Existing test code that calls a changed signature: none.

Files of the change no unit test can cover: none.

## Areas

`docs/areas/container-actions.md` — request addresses resolve below `BaseUrl`'s path (see *Documentation updates*).

## Documentation updates

All by the Dev, against the code as built:

- `docs/areas/container-actions.md`, *Transport security* (first bullet): every request goes below the path of
  `BaseUrl`, which counts as a directory (a missing trailing slash is added; query and fragment are not sent), so a
  Portainer behind a reverse-proxy prefix is configured with that prefix; the request paths named in *Capability
  check* (`GET /api/endpoints/<id>`) and *Actions* (steps 2 and 3) are relative to it — say so once, beside the first
  of them; link [0046](../decisions/0046-portainer-requests-keep-the-path-of-the-base-url.md). *Authentication*: the
  login goes to `api/auth` below `BaseUrl`.
- `docs/areas/integration-failures.md`, *Logins inside a Retry-After window*, first bullet: the Portainer login
  (`/api/auth`) becomes `api/auth` below the Portainer `BaseUrl`, so the window follows its path.
- `README.md`, Portainer table, row `BaseUrl`: "Absolute Portainer URL, the address its UI is reached at (a reverse-proxy
  path prefix included, without `/api`); `https` unless `AllowInsecureHttp` is set".
- Sweep result: `docs/areas/hosting.md` (*Validation*) lists the key among URL rules — unchanged, validation is
  unchanged; `docs/ARCHITECTURE.md` `PortainerClient` row (`:240`) states no address form — unchanged; `INSTALL.md`,
  `DOCKER.md` name Portainer without addresses — unchanged. The PR template asks for no changelog.

## Architecture check

No guarantee of `.squad/project.md` is touched: failure reasons stay fixed (no message text changes), the layering is
unchanged, no I/O moves. `docs/ARCHITECTURE.md` needs no update (no flow or guarantee changes).

## Security considerations

- [x] No limits.
- [x] Exceptions reaching a user-visible message: none new; the authentication messages keep naming the masked
  configured `BaseUrl`.
- Credentials (password, API key) now go to the configured path instead of the host root, narrowing where they are
  sent; the user info of `BaseUrl` is carried in the base address exactly as today. Relative paths are built only from
  fixed text and the already-validated digit and hex values, so none can resolve outside the `BaseUrl` path.

## Decision records

- `docs/decisions/0046-portainer-requests-keep-the-path-of-the-base-url.md` (Proposed), indexed.

## Out of scope / follow-ups

- Validation that a `BaseUrl` path ends in `/api` (likely a mistake after this change): not proposed; the README row
  states the expected value.

## Security notes

Security plan review: issue #412, mode `plan`, round 1

Files reviewed:
- /home/user/DockerUpdateGuard/specs/issue-412/plan.md
- /home/user/DockerUpdateGuard/docs/decisions/0046-portainer-requests-keep-the-path-of-the-base-url.md
- /home/user/DockerUpdateGuard/src/DockerUpdateGuard/Portainer/PortainerClient.cs
- /home/user/DockerUpdateGuard/src/DockerUpdateGuard/Configuration/DockerUpdateGuardOptionsValidator.cs
- /home/user/DockerUpdateGuard/src/DockerUpdateGuard/Infrastructure/LoginRetryAfterRegistry.cs
- /home/user/DockerUpdateGuard/src/DockerUpdateGuard/ServiceCollectionExtensions.cs
- /home/user/DockerUpdateGuard/docs/areas/container-actions.md and /home/user/DockerUpdateGuard/docs/areas/integration-failures.md

**Tier**
`standard` is correct. The change adds no guard and does not change or remove one. Validation, the digits-only `EndpointId` check (`PortainerClient.cs:328-332`, `IsDigitsOnly` at `:259`), the container-ID hex check (`:245-249`) and credential handling all stay the same. Only the path that existing requests resolve to changes, and the password and API key now go below the path the operator configured instead of the host root, so where credentials are sent gets narrower. I see no reason to raise the tier.

**Measurement (library behavior the plan relies on)**
The plan's approach depends on how `Uri.GetLeftPart(UriPartial.Path)`, the trailing slash and relative-reference resolution in `HttpClient` behave, so I measured them. I wrote `/tmp/claude-0/-home-user-DockerUpdateGuard/207e5d44-75b0-549c-bfb5-8c2e007ec025/scratchpad/m412/probe.cs`. It builds the base address the way the plan describes, sends `GetAsync("api/auth")` through a handler that records the request URI, and computes the registry key the way `LoginRetryAfterRegistry.cs:59` does (`SchemeAndServer | Path`). Command: `dotnet run probe.cs` (SDK 10.0.404). Output, shortened to base → request URI:
- `https://portainer.example.test/portainer` → `https://portainer.example.test/portainer/api/auth`
- `…/portainer/#!/home` and `…/portainer/?x=1` → `…/portainer/api/auth` (query and fragment dropped)
- `…/my%20portainer/` → `…/my%20portainer/api/auth`
- `https://portainer.example.test` → `https://portainer.example.test/api/auth` (AC5 unchanged)
- `https://user:p%40ss@portainer.example.test:9443/portainer` → `https://user:p%40ss@portainer.example.test:9443/portainer/api/auth`, registry key `https://portainer.example.test:9443/portainer/api/auth` (user info is kept as today and left out of the key)
- `…/a/../b` → `…/b/api/auth`; `…/a%2Fb/` → `…/a%2Fb/api/auth`; `…/a%2e%2e/` → `…/a../api/auth`; `…/%41bc` → `…/Abc/api/auth`
- `api/endpoints/7/docker/containers/json?all=true&filters=%7B…%7D` against base `https://h/p` → `https://h/p/api/endpoints/7/docker/containers/json?all=true&filters=%7B%22id%22%3A%5B%22ab%22%5D%7D` (the escaped filter is kept)

This confirms the plan's claims: AC1–AC5, and AC4's registry behavior (`/a` and `/a/` share a window, `/a/` and `/b/` do not). No relative request path can climb above the prefix. The only variable segments are the digits-only `EndpointId` (checked before the first request that uses it, `:328`; the listed one comes from an `int`, `:426`), the 64-character hex container ID and the fixed action names. Dot segments in `BaseUrl` itself are resolved by `Uri` exactly as they are today, and `BaseUrl` is operator-trusted configuration.

**Findings**

1. [NON-BLOCKING] *Security areas touched* leaves out **Insecure Portainer HTTP** (`.squad/project.md`: the `AllowInsecureHttp` opt-in and its warning, `Portainer/PortainerClient.cs`). The plan changes how `client.BaseAddress` is built (`PortainerClient.cs:652`), and the warning's scheme check at `:654` reads that value. *Approach* already covers this ("The plaintext-HTTP warning keeps reading `client.BaseAddress.Scheme`"), and the measurement above shows `GetLeftPart(UriPartial.Path)` keeps the scheme, so nothing changes in behavior. Smallest fix: add "Insecure Portainer HTTP (the warning's scheme check reads the new base address)" to *Security areas touched*. Recommendation: fix now.

2. [NON-BLOCKING] *Approach* says the helper keeps "escapes as they are", and AC3 says "the escape kept as it is". The measurement shows this is true for `%20` and `%2F`, but `Uri` unescapes unreserved characters (`%41` → `A`, `%2e%2e` → `..`, shown as a literal segment, not resolved). There is no security effect: the result stays below the configured host, and today's code canonicalizes the same way. The risk is that the area document repeats the broader claim. Smallest fix: in *Documentation updates* for `docs/areas/container-actions.md`, do not state that every escape of `BaseUrl` is sent unchanged; keep only "query and fragment are not sent". Recommendation: fix now (a wording change to the plan).

**Other checks, no finding**
- Credentials: the login body and `X-API-Key` now go to `BaseUrl` path + `api/auth` / `api/…`. That is the address the operator configured, and decision 0046's *Context* names the old behavior as the defect.
- Messages and logging: no message text or log call changes, and `maskedBaseUrl` still comes from the configured `BaseUrl` through `UrlRedaction.RedactUserInfo`, so no *Logger arguments* concern.
- Decision 0046: it agrees with the plan, it is indexed (`docs/decisions/README.md:109`), and its *Consequences* state the Retry-After key change and the upgrade break for a `BaseUrl` ending in `/api`.
- No dependency, CI, Docker or build change.

## Challenge notes

I found nothing wrong in /home/user/DockerUpdateGuard/specs/issue-412/plan.md. I checked its claims against the code and ran the proposed address building with dotnet, and every claim holds.

- **Request paths (rows 1 and 7):** the only root-relative request paths are at `src/DockerUpdateGuard/Portainer/PortainerClient.cs` lines 281, 334, 405, 469, 564, 621 and 675, and the base address is set at :652. Nothing else in the app builds a Portainer request address. `InstanceDiscoveryService.cs:157` only stores a masked copy of `BaseUrl`.
- **Validation (row 3):** `DockerUpdateGuardOptionsValidator.cs:356-375` accepts a `BaseUrl` with a path, as the plan says.
- **Login wait window (row 4):** the key at `Infrastructure/LoginRetryAfterRegistry.cs:59` uses scheme, host, port and path, so the window follows the prefixed login address. AC4 is correct.
- **Safe variable parts:** the action checks the container ID with `IsContainerId` before Portainer is contacted (:751). The endpoint ID is checked as digits only in `CheckEnvironmentAsync` (:329) before the lookup and the action use it. So no added path segment can climb above the prefix.
- **AC1 request order:** the order is right with `EndpointId` set. `ResolveEndpointAsync` returns the configured ID (:448-451), then `/endpoints/7` is checked, then the container lookup runs, then the action is sent.
- **Test URI comparison:** `SequenceHttpMessageHandler` compares `AbsoluteUri` (`Helper/SequenceHttpMessageHandler.cs:54, 102, 112`), which keeps the escapes. AC3's `my%20portainer` row can therefore be asserted exactly as the plan writes it.
- **The proposed base address, run on .NET:** taking the address up to the path and adding a missing trailing slash gives these results:
  - `/portainer`, `/portainer/#!/home`, `/portainer/?x=1` and `/portainer?x=1#f` all send the login to `/portainer/api/auth`.
  - `/my%20portainer/` keeps `%20`.
  - A `BaseUrl` with no path, or `https://h/#!/2/docker/containers`, stays `https://h/api/auth` (AC5).
  - The address with user info (`https://a:S3cret@h:9443`) still carries the user info, so the existing tests at `PortainerClientTests.cs:1424/1480` still pass.
  - The escaped container-list query is kept unchanged.
  - The wait-window key for `/a` and `/a/` is the same.
- **Documentation:** the sections the plan lists exist: `docs/areas/container-actions.md` lines 49, 61, 90, 136 and 140, `docs/areas/integration-failures.md:136-139`, and the `README.md:217` row. No other document names the `/api/…` paths.
- **Known upgrade break:** a `BaseUrl` ending in `/api` becomes `/api/api/auth` after this change. Decision 0046 (*Consequences*) and the README row already cover it, and the plan lists a validation check for it as out of scope on purpose.
