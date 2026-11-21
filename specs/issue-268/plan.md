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
| AC1 | `DockerHubClientGetBaseUriAbsoluteNonDockerHubRegistryReturnsApiBaseUrl` (`Registry` `https://registry.example.internal`, `ApiBaseUrl` `https://hub.mirror.example.test/`); `ServiceCollectionExtensionsAddDockerUpdateGuardHostRegistersExpectedServices` adapted: add `DockerHub:ApiBaseUrl` = `https://hub.mirror.example.test/`, keep the absolute `Registry`, assert the `BaseAddress` is the `ApiBaseUrl` (built: `Registry` set to `docker.io`, since resolving the typed client validates the options); `ServiceCollectionExtensionsAddDockerUpdateGuardHostAbsoluteNonDockerHubRegistryUsesApiBaseUrlAsBaseAddress` (absolute `Registry` `https://registry.example.test/`, client from `IHttpClientFactory`, which skips options validation; added in step 5, approved in step 9) | `DockerHubClientTests.cs`; `ServiceCollectionExtensionsTests.cs` |
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

`INSTALL.md`, `DOCKER.md` and the compose files do not mention the key (fact 12): no edit. `docs/ARCHITECTURE.md` lists the post-configure normalizations (startup step 1 and the options bullet): both add `DockerHub:Registry` and link 0052 — found in step 8, approved in step 9.

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

## Challenge notes

Plan challenge (mode `plan`) for issue #268, `/home/user/DockerUpdateGuard/specs/issue-268/plan.md`.

I found nothing that needs changing: the plan holds up. These are the checks I ran myself:

- **Root cause.** `GetBaseUri` (`src/DockerUpdateGuard/DockerHub/DockerHubClient.cs:128-145`) returns an absolute non-Docker-Hub `Registry` in place of `ApiBaseUrl`. The composition root uses it as `BaseAddress` (`src/DockerUpdateGuard/ServiceCollectionExtensions.cs:76`), and `GetApiBaseUri` falls back to it (`:936`). There are no other callers.
- **The current validation.** It is only a non-empty check plus the `@` rule for an absolute value (`src/DockerUpdateGuard/Configuration/DockerUpdateGuardOptionsValidator.cs:108-115`). This matches plan fact 7.
- **Callers of `IsSupportedDockerHubHost`.** After the change, its only callers are the two in `IsSupportedRegistry` (`:365`, `:368`), and both already exclude blank values. Removing `ThrowIfNullOrWhiteSpace` is safe, and the set of served registries stays the same.
- **Other places that read the setting.** `DockerHub.Registry` is read only in the validator and the two account gates (`:1222`, `:1277`). Outside the area and decision documents, the key appears only in `appsettings.json:16` and `README.md:259`, both `docker.io`. So the plan's scope is complete.
- **Existing tests that conflict with the new rule:**
  - `ServiceCollectionExtensionsTests.cs:41/76`: the plan adapts it. The test builds the provider without resolving the options, so keeping the absolute `Registry` there does not trip `ValidateOnStart`. With `ApiBaseUrl` set, the adapted test fails on today's code.
  - `DockerUpdateGuardOptionsValidatorTests.cs:1165/1181`: the plan drops the `Registry` value and its assertion.
  - `Data/TestDevelopmentConfiguration.cs:23` and the supported-set array at `DockerUpdateGuardOptionsValidatorTests.cs:500-503`: the plan covers both.
  - All other `Registry = "ghcr.io"`/`mcr…` values in the tests are on image references or on hand-built `DockerHubOptions`. None goes through the validator.
- **The two existing `GetBaseUri` tests** (`DockerHubClientTests.cs:776`, `:794`) give the same results under the new body, so leaving them unchanged is correct.
- **The defensive account-gate test** (`DockerHubClientTests.cs:346-361`, registry `ghcr.io`) builds its options directly and stays valid for AC6.
- **Trimming in the post-configure step.** It fits the existing class (`Configuration/DockerUpdateGuardOptionsPostConfigure.cs:13-21`). A value of only whitespace trims to empty and still gets "must be configured".
- **Tier.** `security` is right: the change concerns where the token derived from the PAT is sent.
- **Need.** This is a real change, not "works as designed": the accepted set of six hosts follows the issue's recommendation, and the Product Manager's decision in the issue backs the breaking change.

## Security notes

Security plan review for issue #268 (`specs/issue-268/plan.md`, record `docs/decisions/0052-the-docker-hub-registry-key-only-names-the-account-discovery-host.md`, branch head 14a09a8). The plan is approved with one non-blocking finding.

**Tier and listed areas**
- **Tier:** `security` is correct. The change alters where the PAT and the token derived from it are sent, and adds an allow-list on a configured value.
- **Listed areas:** the three areas named under *Security areas touched* (Secrets in configuration, Configured URLs and their user info, Registry and Docker Scout calls) are complete. I checked the rest of `.squad/project.md` *Security areas*. The plan adds no new failure text that carries remote or configured data: the AC2 failure is fixed text and the value is never interpolated into it. So *Failure messages and remote text* is not touched.

**What I checked**
- **Removing the redirect fixes the root cause.**
  - `GetBaseUri` (`src/DockerUpdateGuard/DockerHub/DockerHubClient.cs:128-145`) is the only path by which `Registry` reached `HttpClient.BaseAddress`. That is `ServiceCollectionExtensions.cs:76`, plus the live fallback in `GetApiBaseUri` (`:934-937`), which `CreateLoginUri` (`:925-928`) and the pagination origin use.
  - After step 2, both callers resolve to `ApiBaseUrl`. That value keeps `ValidateAbsoluteHttpUri` (http/https only, plus the `@` rule, `DockerUpdateGuardOptionsValidator.cs:160-179`).
  - Nothing else uses `DockerHub.Registry` except the two account gates (`:1222`, `:1277`). I grepped `src/DockerUpdateGuard` for `DockerHub.Registry` and `.Registry` on options.
- **Dropping the `@` branch is safe.** The exact-match allow-list rejects every value that contains a scheme, `@` or `#`. The failure text never contains the value, so a URL with user info cannot leak through it. The AC2 test row `https://admin#secret@registry.example` pins this.
- **Every request that could carry the token uses one origin.** The post-configure trim and the validator are registered through `TryAddEnumerable` with `ValidateOnStart` (`ServiceCollectionExtensions.cs:53-57`), and reloads go through the same validator. Once `GetBaseUri` ignores `Registry`, the login, the bearer-token requests and pagination all start from the `ApiBaseUrl` origin.
- **Account gates.** The values that pass validation are only the six fixed hosts. So the raw `Registry` value that the gates write to the log and into the *unsupported* message (`:1224-1226`, `:1279-1281`) cannot carry user info. With options that skip validation, the behaviour is the same as today.
- **Bypass forms of the guard (a library-behaviour claim, so I measured it).** The plan claims that `string.Trim` followed by an `OrdinalIgnoreCase` exact match rejects non-ASCII look-alikes. I tested every non-ASCII character that could case-fold onto a letter of the six hosts, plus invisible and control characters:
  - dotless ı U+0131
  - İ U+0130
  - long ſ U+017F (against `registry`)
  - Kelvin sign U+212A (against `docker`)
  - zero-width space U+200B
  - BOM U+FEFF
  - NUL

  Command: `dotnet run probe.cs` (.NET SDK 11.0.100). The script is `/tmp/claude-0/-home-user-DockerUpdateGuard/4ab3b567-2f5d-5268-874c-a9db9f9f67c0/scratchpad/oic/probe.cs`. It compares each probe against the six names with `string.Equals(..., StringComparison.OrdinalIgnoreCase)`. Output:
  ```
  0064 006F 0063 006B 0065 0072 002E 0131 006F -> False
  0130 006E 0064 0065 0078 002E 0064 006F 0063 006B 0065 0072 002E 0069 006F -> False
  0072 0065 0067 0069 017F 0074 0072 0079 002D 0031 002E 0064 006F 0063 006B 0065 0072 002E 0069 006F -> False
  0064 006F 0063 212A 0065 0072 002E 0069 006F -> False
  0044 004F 0043 004B 0045 0052 002E 0049 004F -> True
  0064 006F 0063 006B 0065 0072 002E 0069 006F 200B -> False
  FEFF 0064 006F 0063 006B 0065 0072 002E 0069 006F -> False
  0064 006F 0063 006B 0065 0072 002E 0069 006F 0000 -> False
  0064 006F 0063 006B 0065 0072 002E 0069 006F -> True        (" docker.io ".Trim())
  0200B 0064 ... -> False                                     ("U+200Bdocker.io".Trim())
  FEFF 0064 ... -> False                                      ("U+FEFFdocker.io".Trim())
  ```
  - **Result:** the claim holds. ASCII case variants and surrounding Unicode whitespace (NBSP included) are accepted. Every look-alike is rejected, and so are the zero-width, BOM and NUL forms; `Trim` does not strip ZWSP or BOM.
  - **Missed bypass classes:** none. Also, as the plan says, after step 2 a wrongly accepted form could only switch on the account listing against `ApiBaseUrl`. It could never move a request to another host.
- **Resource bounds:** none are claimed.

**Finding**

[NON-BLOCKING] Plan, *Signatures*: `public static IReadOnlyList<string> Names { get; }` on the new public `DockerHubHosts`. If this is backed by a `string[]` (or a `List<string>`), any caller can cast it back and change the allow-list at runtime. That list now drives both the startup/reload validation of `Registry` and the client's set of served registries.
- **Smallest fix:** back `Names` with a read-only collection, for example `Array.AsReadOnly([...])` or a `ReadOnlyCollection<string>` built once in a static field. The signature stays unchanged.
- **Recommendation:** fix it now, as a one-line note for the Dev in *Approach* step 1. It needs no separate test.
