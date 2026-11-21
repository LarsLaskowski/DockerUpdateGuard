# Plan: DockerHub:Registry has one meaning and is validated as a Docker Hub host

Source: Issue #268
Tier: security — the change removes the path by which `DockerHub:Registry` redirected the Docker Hub login with the PAT and its bearer token to another host, which is a change to the handling of credentials, and adds an allow-list on a configured value.
Security areas touched: Secrets in configuration (where the PAT-derived token is sent); Configured URLs and their user info (`DockerUpdateGuardOptionsValidator.cs`, the `@` rule no longer covers `Registry`); Registry and Docker Scout calls (`DockerHub/DockerHubClient.cs`, the web API base address)

## Problem / root cause

`DockerHubClient.GetBaseUri` (`src/DockerUpdateGuard/DockerHub/DockerHubClient.cs:128-145`) returns `Registry` instead
of `ApiBaseUrl` when `Registry` is an absolute URL whose host is not a Docker Hub host; the composition root sets it as
the Docker Hub `HttpClient.BaseAddress` (`src/DockerUpdateGuard/ServiceCollectionExtensions.cs:76`), so every web API
call and the PAT login (`CreateLoginUri`, `:925-937`) go there. `ValidateDockerHubOptions`
(`src/DockerUpdateGuard/Configuration/DockerUpdateGuardOptionsValidator.cs:106-116`) accepts any non-empty `Registry`
(an absolute one only gets the `@` rule), so a value such as `ghcr.io` leaves the account listing *unsupported*
(`DockerHubClient.cs:1222`, `:1277`) with only a log entry.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | `Registry` defaults to `docker.io`, summary "Registry host name" | `Configuration/DockerHubOptions.cs:10-13` | confirmed |
| 2 | README calls it "Registry host handled by the Docker Hub integration", required | `README.md:123` (issue says `:108`) | confirmed |
| 3 | The served hosts are a fixed list matched on the image reference's registry | `IsSupportedDockerHubHost` `DockerHubClient.cs:376-386` (six hosts), `IsSupportedRegistry` `:356-369`, `CanHandle` `:1539`, `GetTagAsync` `:1437`; the issue's line numbers are stale | confirmed |
| 4 | The current-user lookup and account listing answer *unsupported* for a non-Docker-Hub `Registry` | `DockerHubClient.cs:1222-1227`, `:1277-1282`; `DockerHubAccountImageDiscoveryService.cs:278-285` ends the run | confirmed |
| 5 | Discovered repositories are stored under `docker.io` | `ParseRepository` `DockerHubClient.cs:344` | confirmed |
| 6 | An absolute non-Docker-Hub `Registry` replaces `ApiBaseUrl` | `GetBaseUri` `:134-142`, used at `ServiceCollectionExtensions.cs:76` and as the fallback of `GetApiBaseUri` `:936` (read live, reached only when `BaseAddress` is null, which the composition root never leaves) — a second caller the issue does not name | confirmed |
| 7 | Validation only requires a non-empty value | Validator `:108-116` also applies the `@` rule to an absolute `Registry` (decision 0012) | refuted |
| 8 | Reloaded options are validated by the same rules | `docs/areas/hosting.md`, *Configuration reload*; the account gate is therefore reachable only with options that bypass validation | confirmed |
| 9 | Existing `GetBaseUri` tests are at `DockerHubClientTests.cs:73`, `:91` | They are at `:776` (`Registry = https://docker.com/`) and `:794`; both results are the same under the new rule, so they stay unchanged | refuted |
| 10 | The development-sample test is at `DockerUpdateGuardOptionsValidatorTests.cs:403-407` | It is at `:499-503`; the sample `Data/TestDevelopmentConfiguration.cs:23` sets `Registry` to `https://hub.docker.com`, which the new rule rejects (the test reads it, never validates it): a related defect in the test data | refuted |
| 11 | A test pins the redirect | `ServiceCollectionExtensionsTests.cs:41`, `:76` assert `BaseAddress == Registry` | confirmed |
| 12 | No other document mentions the key | `grep` of `INSTALL.md`, `DOCKER.md`, `docs/ARCHITECTURE.md`, `docs/*.yml`: none; `docs/areas/hosting.md:50` and decision 0012 do | confirmed |
| 13 | Decision 0012 is released | `git tag --contains 083002b` lists v1.3.8, so it is append-only and amended by 0052 | confirmed |
| 14 | There is no changelog file | repository root and `docs/` listing | confirmed |

## Acceptance criteria

- AC1: `DockerHubClient.GetBaseUri` returns `ApiBaseUrl` for every `Registry` value, an absolute non-Docker-Hub URL
  included, and the composition root gives the Docker Hub `HttpClient` the `ApiBaseUrl` as `BaseAddress` when
  `Registry` is such a URL.
- AC2: The validator rejects a `Registry` that, trimmed, is not exactly one of `docker.io`, `index.docker.io`,
  `registry-1.docker.io`, `hub.docker.com`, `docker.com`, `www.docker.com` (case-insensitive) — a foreign host, an
  absolute URL (also of a Docker Hub host), a host with port, path or user info — with exactly the failure
  `'DockerUpdateGuard:DockerHub:Registry' must be a Docker Hub registry host (docker.io, index.docker.io, registry-1.docker.io, hub.docker.com, docker.com, www.docker.com)`,
  one failure for the key, never containing the value. A blank value keeps
  `'DockerUpdateGuard:DockerHub:Registry' must be configured`.
- AC3: The validator accepts each of the six hosts in any case and with surrounding whitespace.
- AC4: The options post-configure step trims `DockerHub:Registry`; an empty value stays empty.
- AC5: `DockerHubHosts.IsDockerHubHost` is true for the six hosts in any case and false for null, blank, a value with
  whitespace, scheme, port or path; the Docker Hub client serves the same registries as before.
- AC6: The account listing and current-user lookup still answer *unsupported* for a non-Docker-Hub `Registry`.
- AC7: README, the `DockerHubOptions` XML docs, `docs/areas/registries.md` and `docs/areas/hosting.md` state the new
  meaning, and the *Known gaps* entry for #268 is gone.
- AC8: *Format*, *Build*, *Test*, *Analyzer gate* and *Coverage gate* pass.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `DockerHubClientGetBaseUriAbsoluteNonDockerHubRegistryReturnsApiBaseUrl` (`Registry` `https://registry.example.internal`, `ApiBaseUrl` `https://hub.mirror.example.test/`); `ServiceCollectionExtensionsAddDockerUpdateGuardHostRegistersExpectedServices` adapted: add `DockerHub:ApiBaseUrl` = `https://hub.mirror.example.test/`, keep the absolute `Registry`, assert the `BaseAddress` is the `ApiBaseUrl` | `DockerHubClientTests.cs`; `ServiceCollectionExtensionsTests.cs` |
| AC2 | `DockerUpdateGuardOptionsValidatorDockerHubRegistryNonDockerHubHostFailsValidation` (rows `ghcr.io`, `registry.example.internal`, `localhost`); `DockerUpdateGuardOptionsValidatorDockerHubRegistryAbsoluteUrlFailsValidation` (rows `https://registry.example.internal`, `https://docker.io`, `https://admin#secret@registry.example`, asserting no failure contains `secret`); `DockerUpdateGuardOptionsValidatorDockerHubRegistryHostWithPortPathOrUserInfoFailsValidation` (rows `docker.io:443`, `docker.io/library`, `user@docker.io`, `docker.io.`) | `DockerUpdateGuardOptionsValidatorTests.cs` |
| AC3 | `DockerUpdateGuardOptionsValidatorDockerHubRegistryDockerHubHostPassesValidation` (rows: each host, `DOCKER.IO`, `" docker.io "`, `"\tHub.Docker.Com\n"`) — cannot fail on today's code, deferred to step 6 | `DockerUpdateGuardOptionsValidatorTests.cs` |
| AC4 | `DockerUpdateGuardOptionsPostConfigurePostConfigureTrimsDockerHubRegistry`; `DockerUpdateGuardOptionsPostConfigurePostConfigureKeepsEmptyDockerHubRegistry` | `DockerUpdateGuardOptionsPostConfigureTests.cs` |
| AC5 | `DockerHubHostsIsDockerHubHostValueReturnsExpected` (rows: six hosts, `INDEX.DOCKER.IO`, null, `""`, `" "`, `" docker.io"`, `https://docker.io`, `docker.io:443`, `docker.io/x`, `ghcr.io`); existing `DockerHubClientSupportsRegistryHostlessOrPortValueReturnsExpected` unchanged | `DockerHubHostsTests.cs`; `DockerHubClientTests.cs` |
| AC6 | existing `DockerHubClientCallsWhenRegistryIsNotDockerHubReturnUnsupportedAsync` (rows `CurrentUser`, `Repositories`), unchanged | `DockerHubClientTests.cs` |
| AC7 | manual: the Reviewer, step 8, on the stamped head | — |
| AC8 | CI: `gate-stamp.py` in step 7 | — |

## Approach

1. New public static class `DockerHubHosts` (`src/DockerUpdateGuard/DockerHub/DockerHubHosts.cs`): `Names`, the six
   hosts in the order of AC2, and `IsDockerHubHost`, an exact `OrdinalIgnoreCase` match against `Names`; null or
   blank gives false, no trimming (callers trim). `DockerHubClient.IsSupportedDockerHubHost` delegates to it (its
   `ThrowIfNullOrWhiteSpace` goes: both callers already exclude blank values), so the client's served set is
   unchanged.
2. `GetBaseUri` becomes `new Uri(options.ApiBaseUrl)` after the null guard; signature and both callers stay.
3. `DockerUpdateGuardOptionsPostConfigure` also trims `options.DockerHub.Registry` when it is not null or empty
   (decision 0049's pattern); its class summary names both normalizations.
4. `ValidateDockerHubOptions`: blank → the existing "must be configured"; otherwise
   `DockerHubHosts.IsDockerHubHost(options.Registry.Trim()) == false` → the AC2 failure, its host list built from
   `DockerHubHosts.Names` joined with `", "`. The `@` branch for `Registry` is removed: any URL already fails the
   host rule, and the new failure carries no value. The validator trims itself too, so a direct call (tests,
   options built by hand) judges the value as the pipeline would.
5. The account gates (`:1222`, `:1277`) stay as they are, as the defensive check.

Guard forms (an exact match on a fixed list after `string.Trim`): plain host in any ASCII case → accepted; surrounding
Unicode whitespace → trimmed, accepted; inner whitespace, a scheme, `host:port`, a path, user info, a trailing dot, a
query or fragment, a non-ASCII look-alike (`İ`, Cyrillic letters, the Kelvin sign) → not equal to any entry, rejected.
Whatever the match decides, `Registry` reaches no request URL any more, so a form wrongly accepted could only enable
the account listing against `ApiBaseUrl`, never move a request.

No new user-visible output beyond the AC2 failure text.

## Options considered

- Host list location: a shared helper (chosen) vs a second public check on `DockerHubClient` (the validator would
  depend on the client, and `SupportsRegistry` accepts URLs) vs a copy in the validator (two lists drift). Record 0052.
- Accepted set: the client's six hosts (chosen, the issue's recommendation, no existing host value breaks) vs the four
  of the issue's example message (a second list; `docker.com` configurations would stop starting). Record 0052.
- Absolute Docker Hub URLs normalized to their host — rejected: the Product Manager's decision makes the key a host.
- Trimming only in the validator and the account gate — rejected for the post-configure step, which hands every
  consumer and log entry the trimmed value (decision 0049).
- Deleting `GetBaseUri` and inlining `new Uri(ApiBaseUrl)` at both callers — rejected: two call sites, the
  issue's criterion names the method, and its tests stay.
- Switching the account gate to `IsDockerHubHost` — rejected: unreachable with validated options, and the issue keeps
  it as it is.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `DockerHub/DockerHubHosts.cs` | new |
| DockerUpdateGuard | `DockerHub/DockerHubClient.cs` | `GetBaseUri` body, `IsSupportedDockerHubHost` delegates |
| DockerUpdateGuard | `Configuration/DockerUpdateGuardOptionsValidator.cs` | `Registry` rule |
| DockerUpdateGuard | `Configuration/DockerUpdateGuardOptionsPostConfigure.cs` | trims `Registry` |
| DockerUpdateGuard | `Configuration/DockerHubOptions.cs` | XML docs of `Registry` and `ApiBaseUrl` |

## Signatures (for the Dev's skeleton)

- `public static class DockerHubHosts` (namespace `DockerUpdateGuard.DockerHub`)
- `public static IReadOnlyList<string> Names { get; }`
- `public static bool IsDockerHubHost(string? host)`

`GetBaseUri` keeps its signature; only its body changes (step 6). No existing file is rewritten by the skeleton.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/DockerHubHostsTests.cs` (new)
- `src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs`
- `src/Tests/DockerUpdateGuard.Tests/DockerUpdateGuardOptionsValidatorTests.cs`
- `src/Tests/DockerUpdateGuard.Tests/DockerUpdateGuardOptionsPostConfigureTests.cs`
- `src/Tests/DockerUpdateGuard.Tests/ServiceCollectionExtensionsTests.cs`

Existing tests whose input the change handles differently, adapted by the Tester in step 5 (no signature changes):
- `ServiceCollectionExtensionsAddDockerUpdateGuardHostRegistersExpectedServices` — becomes AC1's test (above).
- `DockerUpdateGuardOptionsValidatorHttpUrlsWithAtSignOutsideUserInfoReturnFailures` — drop the `Registry` value and
  its `@` assertion (its URL case moves to AC2's test).
- `Data/TestDevelopmentConfiguration.cs:23` → `docker.io`, and the supported set of
  `DockerUpdateGuardOptionsValidatorDevelopmentConfigurationSampleExposesAllHostOptions` loses `https://hub.docker.com`.

Kept unchanged: the two `GetBaseUri` tests (same results), the `SupportsRegistry` and unsupported-call tests.

Files no unit test can cover: none.

## Areas

- docs/areas/registries.md (+30 words): *Pagination links* and *Configuration reload* drop "or of an absolute
  `DockerHub:Registry` that replaces it"; *Docker Hub account discovery* says `Registry` names the Docker Hub host
  of the account, validated at startup and on reload; *Configuration reload* replaces "`Registry` non-empty" by the
  host rule and the AC2 failure, and `ApiBaseUrl` as the only setting of the web API address; *Limits and errors*
  keeps the *unsupported* listing as reachable only with unvalidated options; the #268 *Known gaps* entry goes; 0052
  joins *Related decisions*.
- docs/areas/hosting.md (-15 words): *Validation*, the `@` rule no longer lists `DockerHub:Registry`.

## Documentation updates

All by the Dev, once the code is green:
- `README.md:123-124`: `Registry` — "Docker Hub registry host used for account discovery (a Docker Hub host such as
  `docker.io`)"; `ApiBaseUrl` — add that it is the only way to change the Docker Hub web API address.
- `DockerHubOptions` XML docs of `Registry` (host of account discovery, one of the Docker Hub hosts) and `ApiBaseUrl`.
- The area documents above.
- The PR description names the breaking change (an absolute `Registry` moves to `ApiBaseUrl`) — the orchestrator.

`docs/ARCHITECTURE.md`, `INSTALL.md`, `DOCKER.md` and the compose files do not mention the key (fact 12): no edit.

## Architecture check

No guarantee of `.squad/project.md` is touched; `ApiBaseUrl` stays fixed at startup, the served registries and the
`docker.io` storage of discovered repositories are unchanged, and the composition root keeps its wiring.

## Security considerations

- [x] No limits.
- [x] Exceptions reaching a message: none new; the AC2 failure is fixed text and never contains the value, which may
  hold user info.
- The PAT-derived token now goes only to the `ApiBaseUrl` origin, which keeps its own validation and `@` rule.

## Decision records

- `docs/decisions/0052-the-docker-hub-registry-key-only-names-the-account-discovery-host.md` (Proposed), amending the
  released 0012.

## Out of scope / follow-ups

- Narrowing the accepted set to the four registry hosts (record 0052, option 2).
