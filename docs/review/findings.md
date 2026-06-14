# Review Findings

Central findings list. Each finding receives a stable ID `F-NNN` and is referenced
from the [Review Matrix](file-inventory.md) (column *Findings*).

Severity levels see [criteria.md](criteria.md#severity-levels-for-findingsmd):
🔴 High · 🟠 Medium · 🟡 Low · 🔵 Info.

Status per finding: 🆕 open · 🛠️ in progress · ✅ fixed · 🚫 wontfix/accepted.

---

## Template (copy per finding)

```
### F-NNN — <Short title>
- **Severity:** 🔴/🟠/🟡/🔵
- **Criterion:** K?  (e.g., K3 Security)
- **Phase:** P?
- **File(s):** `path/to/file.cs:Line`
- **Status:** 🆕
- **Finding:** What the issue is.
- **Impact:** Why it matters.
- **Recommendation:** Concrete proposed fix.
```

---

## Phase 1 — Docker / DockerHub / Portainer / Configuration

Summary: **0 🔴 · 3 🟠 · 7 🟡 · 2 🔵**. The security core is solidly built
(DockerHub credentials only via HTTPS auth endpoints, no secrets in logs,
allow-listed Portainer actions, read-only Docker Engine access). Findings relate to
hardening/resilience rather than acute vulnerabilities.

### F-001 — DockerInstanceClient creates a new HttpClient per call
- **Severity:** 🟡
- **Criterion:** K7 (Performance/Resources)
- **Phase:** P1
- **File(s):** `src/DockerUpdateGuard/Docker/DockerInstanceClient.cs:237-274` (and usages at `:959,:1033,:1122,:1159,:1193`)
- **Status:** 🆕
- **Finding:** The default factory `CreateHttpClient` instantiates a new `HttpClientHandler`/`SocketsHttpHandler` + `HttpClient` per engine call, which is disposed immediately via `using`. For `tcp`/`http(s)` endpoints this is the known "new HttpClient per request" anti-pattern (no connection pooling, TLS handshake churn). `PortainerClient` correctly uses `IHttpClientFactory`.
- **Impact:** At higher frequency/multiple instances there's a risk of socket/TIME_WAIT accumulation and repeated TLS handshakes. Given the configured scan intervals (minutes) it's practically limited; nevertheless it's a K7 violation (within a single operation the client is reused correctly).
- **Recommendation:** Reuse a pooled `SocketsHttpHandler` per instance or use named `IHttpClientFactory` clients with `ConfigurePrimaryHttpMessageHandler`; the Unix/NPipe `ConnectCallback` remains representable there.

### F-002 — Silent TLS bypass when SkipCertificateValidation is set
- **Severity:** 🟠
- **Criterion:** K3 (Security)
- **Phase:** P1
- **File(s):** `src/DockerUpdateGuard/Docker/DockerInstanceClient.cs:256-259`
- **Status:** 🆕
- **Finding:** If `SkipCertificateValidation = true`, `HttpClientHandler.DangerousAcceptAnyServerCertificateValidator` is set — without any log output. The certificate bypass is not visible/auditable at runtime.
- **Impact:** MITM risk for `tcp`+TLS engines; an accidentally enabled bypass remains unnoticed. It's opt-in via admin configuration, thus not 🔴.
- **Recommendation:** Log a `Warning` during client construction (instance name + "certificate validation disabled"); strengthen the README note (line 147).

### F-003 — Portainer credentials may be sent over plaintext HTTP
- **Severity:** 🟡
- **Criterion:** K3
- **Phase:** P1
- **File(s):** `src/DockerUpdateGuard/Configuration/DockerUpdateGuardOptionsValidator.cs:191-196`; `src/DockerUpdateGuard/Portainer/PortainerClient.cs:304-318`
- **Status:** 🆕
- **Finding:** The validator accepts both `http` and `https` for `Portainer:BaseUrl`. `CreateAuthenticatedClientAsync` sends `X-API-Key`, username/password (`POST /api/auth`) or the bearer JWT schema-agnostic — over `http://` this is plaintext. No warning is emitted. The README (line 166) even documents `http` as acceptable.
- **Impact:** API tokens/passwords/JWTs can be intercepted on non-TLS connections. Often used behind VPN/localhost, hence 🟡.
- **Recommendation:** At minimum warn if credentials are configured with `http` scheme; ideally enforce `https` (or provide an explicit opt-in flag for plaintext).

### F-004 — Secret fields declared as `record` → plaintext in generated `ToString()`
- **Severity:** 🟡
- **Criterion:** K3
- **Phase:** P1
- **File(s):** `src/DockerUpdateGuard/Portainer/Data/PortainerLoginRequest.cs:10-11`; `src/DockerUpdateGuard/Portainer/Data/PortainerAuthResponse.cs:9`
- **Status:** 🆕
- **Finding:** `PortainerLoginRequest` (record) contains `Password`, and `PortainerAuthResponse` (record) contains `Jwt`. The compiler-generated `ToString()` exposes all members in plaintext. Currently these objects are not logged — the risk is latent (e.g., future structured logging of `{Request}`/`{Response}`).
- **Impact:** Latent leakage of password/JWT in logs/exceptions. Similar pattern: anonymous `new { username, password }` in `DockerHubClient.RequestAccessTokenAsync` (`DockerHubClient.cs:1250-1254`).
- **Recommendation:** Override `ToString()` or avoid modeling credentials as `record`; consider an analyzer rule to detect secrets in `ToString()`.

### F-005 — `MaxParallelRequests` is a dead configuration field
- **Severity:** 🟡
- **Criterion:** K7 / K10
- **Phase:** P1
- **File(s):** `src/DockerUpdateGuard/Configuration/DockerHubOptions.cs:36-37`
- **Status:** 🆕
- **Finding:** `DockerHub:MaxParallelRequests` is declared (`[Range(1,32)]`), validated in the options validator (`<=0`) and documented in README (line 109)/appsettings — but not read by any production code (repo-wide only declaration/validator/doc/tests). Registry parallelism is effectively unthrottled.
- **Impact:** A misleading throttle setting; real parallelism is unlimited, which may conflict with the Docker Hub quota budget managed in `Scanning`.
- **Recommendation:** Enforce it (e.g., apply a `SemaphoreSlim` throttle around outgoing Docker Hub/registry calls) or remove the setting and its README entry.

### F-006 — `RetryCount` has no effect; no retry/backoff implemented
- **Severity:** 🟠
- **Criterion:** K4 / K10
- **Phase:** P1
- **File(s):** `src/DockerUpdateGuard/Configuration/ScanningOptions.cs:73-76`
- **Status:** 🆕
- **Finding:** `Scanning:RetryCount` is documented as "Retry count for transient failures" (README line 134) but is not consumed by any production code. There is no retry/backoff logic for transient errors against Engine/Registry/Portainer — errors are logged and discarded until the next interval.
- **Impact:** Pretended resilience; transient network errors abort the scan immediately. For a tool that continuously contacts flaky external registries, this is a real resilience gap.
- **Recommendation:** Implement bounded retry with backoff honoring `RetryCount` (e.g., resilience handler on `IHttpClientFactory`) or remove the setting and documentation.

### F-007 — `[Range]` DataAnnotations are not enforced (`ValidateDataAnnotations` missing)
- **Severity:** 🟡
- **Criterion:** K10
- **Phase:** P1
- **File(s):** `src/DockerUpdateGuard/ServiceCollectionExtensions.cs:43-45`; `src/DockerUpdateGuard/Configuration/*Options.cs`
- **Status:** 🆕
- **Finding:** `AddOptions<DockerUpdateGuardOptions>().Bind(...).ValidateOnStart()` does not call `.ValidateDataAnnotations()`. Only the custom `DockerUpdateGuardOptionsValidator` runs, which checks lower bounds (`<=0`/`<0`) and required fields. All `[Range]` upper bounds (timeouts ≤300, `MaxParallelRequests` ≤32, interval caps) are not enforced.
- **Impact:** Out-of-range values can pass start validation; `[Range]` attributes misleadingly suggest enforcement that does not exist. Real impact is small (admin-controlled values).
- **Recommendation:** Add `.ValidateDataAnnotations()` (including recursive validation of nested options) or incorporate upper bounds into the custom validator and remove the attributes.

### F-008 — DockerHubClient: unused os/arch parameters; manifest list selection ignores platform
- **Severity:** 🟡
- **Criterion:** K1 (Correctness)
- **Phase:** P1
- **File(s):** `src/DockerUpdateGuard/DockerHub/DockerHubClient.cs:217-227,939-960`
- **Status:** 🆕
- **Finding:** `ParseTag(element, operatingSystem, architecture)` does not evaluate `operatingSystem`/`architecture`; they are passed through from `GetTagAsync`/`GetTagsAsync` but never used. `GetConfigDigestFromManifestListAsync` picks the **first** manifest with a digest from a multi-arch manifest list (no platform matching).
- **Impact:** For multi-arch images, config/base image extraction may reflect the wrong platform; the os/arch API signature suggests filtering that does not occur (dead path).
- **Recommendation:** Implement platform selection properly (match os/arch against the manifest list's `platform` block) or remove the parameters.

### F-009 — DockerHub access token cache tied to scope lifetime
- **Severity:** 🔵
- **Criterion:** K4 / K2
- **Phase:** P1
- **File(s):** `src/DockerUpdateGuard/DockerHub/DockerHubClient.cs:62-73,1199-1237`; `src/DockerUpdateGuard/ServiceCollectionExtensions.cs:54-59,109-110`
- **Status:** 🆕
- **Finding:** `DockerHubClient` is registered via `AddHttpClient<DockerHubClient>` (transient); `IDockerHubClient`/`IRegistryMetadataClient` are `AddScoped(... GetRequiredService<DockerHubClient>())`. The token cache (`_accessToken`, `_accessTokenExpiresAtUtc`, `_tokenRefreshLock`) therefore exists per scope and is rebuilt for each new scope.
- **Impact:** Caching applies within a scope; across scopes each cycle re-authenticates with Docker Hub → extra login requests against the quota budget. Not a correctness error, but worth reviewing.
- **Recommendation:** Confirm intended reuse; if cross-scope caching is desired, move the token cache into a singleton service.

### F-010 — Portainer filter built via string interpolation as JSON
- **Severity:** 🟡
- **Criterion:** K1 / K3
- **Phase:** P1
- **File(s):** `src/DockerUpdateGuard/Portainer/PortainerClient.cs:94-95`
- **Status:** 🆕
- **Finding:** `FindContainerIdAsync` builds the Docker filter using `Uri.EscapeDataString($"{{\"name\":[\"{containerName}\"]}}")`. `containerName` is interpolated into the JSON string without JSON escaping; `Uri.EscapeDataString` only protects URL transport, not the JSON structure.
- **Impact:** Special characters (`"`, `\`) break the filter or could alter the JSON structure. Docker name rules restrict container names — practical injection risk is low but the pattern is fragile.
- **Recommendation:** Produce the filter via `JsonSerializer.Serialize` of a typed object (correct escaping) and then URL-encode.

### F-011 — PortainerClient lacks automated tests (critical action path)
- **Severity:** 🟠
- **Criterion:** K8
- **Phase:** P1
- **File(s):** `src/DockerUpdateGuard/Portainer/PortainerClient.cs` (entire)
- **Status:** 🆕
- **Finding:** There are no tests for `PortainerClient` (no `PortainerClientTests`). Untested are: auth selection (PAT vs username/password JWT), action allow-list, endpoint/container resolution and error paths — despite destructive container actions (stop/kill/restart).
- **Impact:** Regression risk on a security-/impact-critical path (container lifecycle + credential handling) without a safety net.
- **Recommendation:** Add tests with stubbed `IHttpClientFactory`/`HttpMessageHandler`: allow-list rejection, PAT vs login path, "missing JWT" error, endpoint auto-resolve, container-not-found.

### F-012 — Image environment variables (potential secrets) are read
- **Severity:** 🔵
- **Criterion:** K3
- **Phase:** P1
- **File(s):** `src/DockerUpdateGuard/Docker/DockerInstanceClient.cs:741-779`; `src/DockerUpdateGuard/DockerHub/DockerHubClient.cs:1097-1120`
- **Status:** 🆕
- **Finding:** From image inspect/config blob `config.Env` is copied into `EnvironmentVariables`. Image env vars often contain secrets (DB passwords, API keys). They are consumed downstream in `DerivedBaseRuntimeDetector` and `ImageScanOrchestrator` (Phase 3).
- **Impact:** The capture itself is not a leak; it becomes relevant if raw values are logged, persisted, or shown in the UI downstream.
- **Recommendation:** In P3/P4/P5 verify that `EnvironmentVariables` are not logged/stored/displayed in plaintext (only use for detection, then discard). This is noted here as a handoff hint.

## Phase 2 — Vulnerabilities

Summary: **0 🔴 · 4 🟠 · 3 🟡 · 1 🔵**. The providers are cleanly built on
`IHttpClientFactory` (pooled handlers, no socket churn) and `IOptionsMonitor`;
Scout authenticates exclusively over HTTPS endpoints (`hub.docker.com`,
`api.scout.docker.com`), errors are logged and degraded to `ExternalOperationResult`
instead of crashing the background service; `CancellationToken` is passed through;
the enrichment loop is sequential (no unbounded parallelism). Focus of the findings:
**Docker Scout correctness** (severity mapping case-sensitive, registry ignored),
**missing package granularity** of the findings (dead unique index), **test coverage**
(Scout/default path untested) as well as smaller hardening/resilience points.

### F-013 — Docker Scout severity mapping is case-sensitive → silent `NotSet`
- **Severity:** 🟠
- **Criterion:** K1 (Correctness)
- **Phase:** P2
- **File(s):** `src/DockerUpdateGuard/Vulnerabilities/DockerScoutVulnerabilityProvider.cs:122-132`
- **Status:** 🆕
- **Finding:** `MapSeverity` switches directly on exactly upper-cased strings
  (`"LOW"`/`"MEDIUM"`/`"HIGH"`/`"CRITICAL"`) without normalization; everything else →
  `NotSet`. The Trivy provider, by contrast, normalizes (`ToUpperInvariant()` + stripping
  the `SEVERITY_` prefix, `:108-122`). If Docker Scout returns the severity in
  lower/mixed case (common in the Scout world, e.g. `critical`/`high`), every
  severity silently falls back to `NotSet`.
- **Impact:** Severity is the central triage field. A silent loss makes all Scout
  findings appear as `NotSet` → under-prioritization, without any error signal.
- **Recommendation:** Map case-insensitively as in Trivy (`ToUpperInvariant()` before the
  `switch`), log unknown values; add a test with mixed casing.

### F-014 — Docker Scout ignores the registry → wrong/colliding lookups
- **Severity:** 🟠
- **Criterion:** K1 (Correctness)
- **Phase:** P2
- **File(s):** `src/DockerUpdateGuard/Vulnerabilities/DockerScoutVulnerabilityProvider.cs:76-86,226-228`
- **Status:** 🆕
- **Finding:** `FetchVulnerabilitiesAsync` builds the URL solely from
  `ParseNamespaceAndRepo(imageReference.Repository)` (`…/v1/repositories/{ns}/{repo}/tags/{tag}/vulnerabilities`)
  and discards `imageReference.Registry` entirely. An image `ghcr.io/acme/api`
  is thus queried against the Docker Hub/Scout path `acme/api`. The
  Trivy provider does it correctly: `GetArtifactRepository` prepends the registry for
  non-`docker.io` (`:129-138`) — there is even an explicit test for this.
- **Impact:** For non-Docker-Hub images either `NotFound` or — worse —
  vulnerabilities of a **foreign, identically named** Docker Hub image being
  attributed to the wrong image.
- **Recommendation:** Limit Scout to `docker.io` images (otherwise return `Unsupported`/
  `NotConfigured`) or include the registry in the lookup; add tests
  for non-Hub references.

### F-015 — Findings lose package granularity; the planned unique index is ineffective
- **Severity:** 🟠
- **Criterion:** K1 / K6
- **Phase:** P2 (persistence impact in P3/P4)
- **File(s):** `src/DockerUpdateGuard/Vulnerabilities/VulnerabilityAdvisoryData.cs:8-37`; `src/DockerUpdateGuard/Vulnerabilities/Data/TrivyVulnerability.cs:8-42`; `src/DockerUpdateGuard/Vulnerabilities/Data/ScoutVulnerabilityItem.cs:8-36`; cross-reference: `src/DockerUpdateGuard/Images/VulnerabilityEnrichmentService.cs:172-195` (P3), `src/DockerUpdateGuard.Data/Configurations/VulnerabilityFindingConfiguration.cs:60-67` (P4)
- **Status:** 🆕
- **Finding:** Neither the advisory model (`VulnerabilityAdvisoryData`) nor the
  provider DTOs capture the affected package or the fix version
  (`PkgName`/`InstalledVersion`/`FixedVersion`). The persisted
  `VulnerabilityFinding` carries `AffectedPackage`/`FixedVersion`, but the write path
  never sets them. The unique index `UNIQUE(ImageVersionId, AdvisoryId,
  AffectedPackage, FixedVersion)` was evidently intended as dedup per (advisory, package, fix)
  — since the last two columns are **always NULL**, it never takes effect under
  PostgreSQL (default `NULLS DISTINCT`) nor under SQLite. Consequence: the same CVE
  across multiple packages becomes multiple identical findings; the message "provider
  reported N finding(s)" (`:193-195`) overcounts; the index enforces nothing. In addition,
  the "deactivate + new row" flow (`:170,:174`) appends new, never-colliding inactive
  rows per cycle, which — for lack of a cascade (`ScanRun` deletion =
  `SetNull`, P4) — are never cleaned up → unbounded table growth.
- **Impact:** CVEs listed twice in the UI and overcounted finding counts; a
  unique index that guarantees nothing; long-term growth of the
  `VulnerabilityFindings` table. No crash (NULLs are treated as distinct).
- **Recommendation:** Add `PkgName`/`InstalledVersion`/`FixedVersion` to the DTOs and
  `VulnerabilityAdvisoryData` and map them to
  `AffectedPackage`/`FixedVersion` when persisting; alternatively deduplicate on
  `AdvisoryId` before persisting. Optionally set the index to `AreNullsDistinct(false)` or filter on
  `IsActive`. Provide cleanup for inactive findings. **Note for P3/P4.**

### F-016 — Provider paths insufficiently tested (Scout/default not at all, Trivy only happy path)
- **Severity:** 🟠
- **Criterion:** K8 (Tests)
- **Phase:** P2
- **File(s):** `src/DockerUpdateGuard/Vulnerabilities/DockerScoutVulnerabilityProvider.cs` (entire, no tests); `src/DockerUpdateGuard/Vulnerabilities/DefaultVulnerabilityProvider.cs` (no tests); `src/Tests/DockerUpdateGuard.Tests/TrivyVulnerabilityProviderTests.cs` (only success + empty response)
- **Status:** 🆕
- **Finding:** Answer to the K8 guiding question "are all three provider paths tested?":
  **no**. For `DockerScoutVulnerabilityProvider` there is no test at all
  (repo-wide search for `Scout` in `src/Tests` → empty) — untested remain
  two-step auth, `NotFound`/auth error path, credential gate and the
  (faulty) severity mapping. `DefaultVulnerabilityProvider` (path `Provider=None`/
  disabled) is not directly tested. Trivy only tests 2xx + `{"results":[]}`; not
  covered: non-2xx → `Failed`, missing `TrivyBaseUrl` → `NotConfigured`,
  exception/timeout, severity normalization. Moreover, the wire contracts are pinned only to
  self-written stubs (`StubHttpMessageHandler`) — a deviation from the
  real Trivy Twirp or Scout REST API would pass the tests but fail in production.
- **Impact:** Regressions and contract drift on the error-prone
  parsing/auth paths of external inputs remain undetected.
- **Recommendation:** Add provider tests per status (`Succeeded`/`NotConfigured`/`NotFound`/
  `Failed`) + severity normalization + empty/partial responses; for Trivy
  additionally a contract/integration check against a real Trivy server.

### F-017 — Cancellation is swallowed as a scan error
- **Severity:** 🟡
- **Criterion:** K4 / K5
- **Phase:** P2
- **File(s):** `src/DockerUpdateGuard/Vulnerabilities/TrivyVulnerabilityProvider.cs:201-206`; `src/DockerUpdateGuard/Vulnerabilities/DockerScoutVulnerabilityProvider.cs:171-176`; cross-reference: `src/DockerUpdateGuard/Images/VulnerabilityEnrichmentService.cs:215-222` (P3)
- **Status:** 🆕
- **Finding:** The broad `catch (Exception)` also converts
  `OperationCanceledException`/`TaskCanceledException` from genuine token cancellation
  (graceful shutdown) into a `Failed` result including an `Error` log and a failed/partial
  ScanRun. (Timeouts also surface as `TaskCanceledException` — those
  we want as `Failed`; genuine cancellation should however propagate.)
- **Impact:** During shutdown, misleading failed/partial scans and
  error noise arise; genuine errors are harder to distinguish from shutdown.
- **Recommendation:** Before the general `catch`, add a
  `catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }`.

### F-018 — Scout credential DTOs as `record` → secrets in the generated `ToString()`
- **Severity:** 🟡
- **Criterion:** K3 (Security)
- **Phase:** P2
- **File(s):** `src/DockerUpdateGuard/Vulnerabilities/Data/HubLoginRequest.cs:8-23`; `src/DockerUpdateGuard/Vulnerabilities/Data/HubLoginResponse.cs:8-18`
- **Status:** 🆕
- **Finding:** Same pattern as [F-004](#f-004--secret-fields-declared-as-record--plaintext-in-generated-tostring):
  `HubLoginRequest` (record) contains `Password` (Docker Hub PAT), `HubLoginResponse`
  (record) the `Token` (JWT). The compiler-generated `ToString()` emits both in
  plaintext. Currently the objects are not logged — the risk is latent
  (future `{Request}`/`{Response}` structured logging).
- **Impact:** Latent leakage of Docker Hub PAT/JWT in logs/exceptions.
- **Recommendation:** Redact `ToString()` or do not model credentials as `record`;
  optionally an analyzer rule against secret-in-`ToString`. (Cross-reference F-004.)

### F-019 — `TrivyBaseUrl` only checked for presence, not as an absolute http/https URI
- **Severity:** 🟡
- **Criterion:** K3 / K10
- **Phase:** P2
- **File(s):** `src/DockerUpdateGuard/Configuration/DockerUpdateGuardOptionsValidator.cs:69-72`; `src/DockerUpdateGuard/Vulnerabilities/TrivyVulnerabilityProvider.cs:150-162`; `src/DockerUpdateGuard/Configuration/VulnerabilityOptions.cs:25`
- **Status:** 🆕
- **Finding:** The validator enforces `TrivyBaseUrl` when `Provider=Trivy`, but —
  unlike the Portainer/Docker `BaseUrl` (`:191-196,:220-232`) — checks neither absolute
  URI nor scheme. A malformed value (e.g. without scheme) fails only at
  runtime (caught → `Failed`) instead of at startup; `http://` (the example `http://trivy:4954`
  documented in README line 214) transmits image coordinates in plaintext;
  there is no scheme/SSRF guardrail (admin-configured, hence low).
- **Impact:** Late/opaque misconfiguration; plaintext transport as
  the default example; no scheme guardrail.
- **Recommendation:** At startup check `Uri.TryCreate(..., UriKind.Absolute)` + http/https scheme
  (analogous to Portainer), warn on `http` and document TLS.

### F-020 — Active provider fixed at startup, but config read live
- **Severity:** 🔵
- **Criterion:** K2 / K10
- **Phase:** P2
- **File(s):** `src/DockerUpdateGuard/ServiceCollectionExtensions.cs:81-107` (P6)
- **Status:** 🆕
- **Finding:** The DI `switch` selects the `IVulnerabilityProvider` implementation
  **once at startup** from `Enabled`/`Provider`. `VulnerabilityRefreshBackgroundService`
  and `VulnerabilityEnrichmentService`, however, read `Enabled` live, and the providers
  read `TrivyBaseUrl`/credentials/timeout live via `IOptionsMonitor`. A
  runtime change `Enabled` false→true (start = disabled) does not swap the implementation:
  it remains the `DefaultVulnerabilityProvider`, which then reports
  `NotConfigured` for every image (ScanRun = Partial). Mixed static/dynamic model.
- **Impact:** Surprising reconfiguration behavior (restart required); mostly cosmetic,
  but a footgun.
- **Recommendation:** Document that provider/enable changes require a restart,
  or register all providers and resolve them at call time based on the current
  options.

## Phase 3 — Images

Summary: **0 🔴 · 5 🟠 · 4 🟡 · 2 🔵**. The core logic is overall carefully
built: tag parsing follows the Docker heuristic, the digest comparison
consistently uses the manifest-list digest (platform-independent, matching the
stored RepoDigest), cancellation is passed through, the background base
(`ScheduledBackgroundService`) catches errors cleanly (the service survives), and
**image env vars are never logged** (positive closure of
[F-012](#f-012--image-environment-variables-potential-secrets-are-read):
`DerivedBaseRuntimeDetector` only evaluates them for detection, `ImageHostLoggingExtensions`
logs no secrets). Focus of the findings: **SemVer edge cases** (pre-release/
variant-suffix ordering), **registry performance** (no token caching in the OCI path,
sequential tag fan-outs – there is no parallelism anywhere in the repo, which
also makes [F-005](#f-005--maxparallelrequests-is-a-dead-configuration-field) moot),
**scan resilience** (batch abort on single errors; findings are deleted before
the scan and not restored on errors) as well as
**data materialization in hot paths**.

### F-021 — Pre-release/variant-suffix numbers are discarded → identical versions compare "equal"; SemVer pre-release ordering missing
- **Severity:** 🟠
- **Criterion:** K1 (Correctness)
- **Phase:** P3
- **File(s):** `src/DockerUpdateGuard/Images/Helper/VersionTagResolutionHelper.cs:388-401` (`NormalizeVariantSegment`), `:334-356` (`TryParseVersionTagComponents`), `:167-186` (`TryCompareVersionTags`)
- **Status:** 🆕
- **Finding:** A tag's suffix (`-rc1`, `-alpine3.19`) is normalized into a "variant family"
  by `NormalizeVariantSegment` keeping **only the leading letters** per segment
  (`rc1`→`rc`, `alpine3.19`→`alpine`). The version comparison (`TryCompareVersionTags`)
  compares only `Major.Minor.Patch`. Consequence: `1.2.3-rc1`, `1.2.3-rc2`,
  `1.2.3-rc10` are **equal** (family `rc`, version `(1,2,3)`); a pre-release is never
  promoted to a higher pre-release of the same version. Additionally, a pre-release
  (`1.2.3-rc1`, family `rc`) lies in a **different** family than the GA release (`1.2.3`/`1.2.4`,
  family `""`) — the pre-release→GA jump is never offered as an update. The same applies to
  sub-versions of a base (`1.2.3-alpine3.18` vs `1.2.3-alpine3.19` → equal). SemVer
  pre-release precedence (`1.2.3-rc1 < 1.2.3`) is not implemented.
- **Impact:** Users on RC/beta/pre-release or sub-versioned variant tags
  miss updates (RC increments, base-OS bumps at the same app version, and the
  GA release). Since these are different tag strings, the digest-change path
  does not catch them either. The criteria explicitly list pre-release edge cases.
- **Recommendation:** Treat the suffix as an ordered pre-release (preserve the numeric part,
  e.g. `rc.1` < `rc.2`) and model SemVer-compliant pre-release-vs-GA precedence; alternatively
  use an established SemVer library. Add tests with `-rcN`/`-betaN` and pre-release→GA.

### F-022 — Unhandled `OverflowException` for overly long version components
- **Severity:** 🟡
- **Criterion:** K1 / K4
- **Phase:** P3
- **File(s):** `src/DockerUpdateGuard/Images/Helper/VersionTagResolutionHelper.cs:350-352,320-321,264`
- **Status:** 🆕
- **Finding:** `TryParseVersionTagComponents`/`TryParseVersionLineTag` parse the regex groups
  (`\d+`, unbounded) via `int.Parse`. A registry tag with a ≥10-digit component (e.g.
  `99999999999.0.0`) throws `OverflowException` — contrary to the `Try…` contract, which should
  return `false`. The methods are called from `UpdateDetectionService.Evaluate` and the
  tag filters in a LINQ `Where` chain over **registry-supplied** tags.
- **Impact:** A single unusual tag aborts the evaluation of the entire image
  (caught higher up → snapshot `Failed`/ScanRun `Partial`). Low probability of occurrence
  (10-digit `X.Y.Z` component), but external input + `Try` contract breach.
- **Recommendation:** Use `int.TryParse` and return `false` on failure (analogous to
  `DerivedBaseRuntimeDetector`, which consistently uses `Version.TryParse`).

### F-023 — OciRegistryClient without token caching: every request goes through 401→token→retry, multiplied across the per-tag fan-out
- **Severity:** 🟠
- **Criterion:** K7 / K5
- **Phase:** P3
- **File(s):** `src/DockerUpdateGuard/Images/OciRegistryClient.cs:1093-1127` (`SendRegistryRequestAsync`), `:1165-1189` (`GetBearerTokenAsync`), `:647-752` (`GetTagsAsync`), `:720` (per-tag `GetTagAsync`)
- **Status:** 🆕
- **Finding:** `SendRegistryRequestAsync` sends **every** request unauthenticated first,
  receives 401, fetches a bearer token and retries — without any caching (no field, no
  reuse across the scan). `GetTagsAsync` inspects up to `MaximumTags`
  (250 runtime / 150 base image) tags, **each individually** via `GetTagAsync`→manifest; multi-arch
  manifests trigger a second manifest fetch (platform digest). A repository scan
  against a non-Docker-Hub registry (ghcr/quay/mcr/own) thus generates several hundred
  to thousands of HTTP requests, ~3x inflated by the re-challenge. Repo-wide there is
  moreover **no parallelism/throttling** (no `Task.WhenAll`/`SemaphoreSlim`), i.e. the
  load is sequential (quota-friendly) but slow – and [F-005](#f-005--maxparallelrequests-is-a-dead-configuration-field)
  has no point of attack here.
- **Impact:** Slow scans and risk of registry rate limiting (ghcr/quay enforce
  limits) on bulk scans. Docker Hub images are not affected (own client with tag API
  that already provides digests in the list).
- **Recommendation:** Cache and reuse the bearer token per registry (and per scan/instance);
  for pure digest probes consider `HEAD` instead of `GET`; introduce a request/parallelism budget
  (cf. F-005). Side finding: `GetContentDigest` (`:429-434`) uses `values.SingleOrDefault()` —
  multiple `Docker-Content-Digest` headers (a misconfigured proxy) would throw `InvalidOperationException`;
  `FirstOrDefault` is more robust.

### F-024 — `ScanAllAsync` aborts the entire batch if the pre/post-processing of an item throws
- **Severity:** 🟠
- **Criterion:** K4 / K5
- **Phase:** P3
- **File(s):** `src/DockerUpdateGuard/Images/ImageScanOrchestrator.cs:185-190,207-231,357-365`; `src/DockerUpdateGuard/Images/RuntimeContainerScanOrchestrator.cs:433-457,478-497,685-693`
- **Status:** 🆕
- **Finding:** In `ImageScanOrchestrator.ScanAsync`, `SingleAsync` (image deleted between
  listing and scan → throws `InvalidOperationException`), the initial `ScanRun` `SaveChanges`,
  `DeleteSupersededObservedFindingsAsync` and the final `SaveChanges`/telemetry sit **outside**
  the inner `try` (`:233-355`). `ScanAllAsync` calls `ScanAsync` in a `foreach` **without**
  a per-item `catch` — an exception propagates out and skips all remaining images
  of that cycle. Identical pattern in `RuntimeContainerScanOrchestrator.ScanInstanceAsync`
  (`ScanRun` save + `DeactivateRuntimeFindingsAsync` before the `try`; final save afterwards). The
  background base does catch the exception (the service survives), but a permanently failing
  early-ordered item **starves** all subsequent items permanently (order = PK insertion).
- **Impact:** A deleted image / a transient DB error on one entry prevents
  the scanning of all subsequent entries — per cycle and, on a persistent error, permanently.
- **Recommendation:** Wrap the per-item call in `ScanAllAsync` in `try/catch` (or make `ScanAsync`/
  `ScanInstanceAsync` themselves exception-free); use `SingleOrDefaultAsync` + null check instead of `SingleAsync`.

### F-025 — Findings are deleted/deactivated before the scan and not restored on errors → alerts disappear transiently
- **Severity:** 🟠
- **Criterion:** K1 / K6
- **Phase:** P3
- **File(s):** `src/DockerUpdateGuard/Images/ImageScanOrchestrator.cs:231,445-450`; `src/DockerUpdateGuard/Images/RuntimeContainerScanOrchestrator.cs:497,712-729`; counter-example: `src/DockerUpdateGuard/Images/VulnerabilityEnrichmentService.cs:168-170`
- **Status:** 🆕
- **Finding:** `ImageScanOrchestrator.DeleteSupersededObservedFindingsAsync` uses
  `ExecuteDeleteAsync` (immediate, non-rollbackable) **at the start** of the scan, before any
  registry work. `RuntimeContainerScanOrchestrator.DeactivateRuntimeFindingsAsync` runs before
  the `try` and is committed with the final `SaveChanges` — even if container discovery
  fails. A transient registry/Docker outage thus deletes or deactivates **all**
  active update findings of an image/instance without creating new ones. Contrast:
  `VulnerabilityEnrichmentService` correctly deactivates **only** in the success branch per image (`:168-170`).
- **Impact:** During a brief Docker/registry outage, the dashboard loses all "update available"
  markers of the affected instance/image until a next successful scan recreates them
  (flickering alerts).
- **Recommendation:** Delete/deactivate findings only **after** a successful re-evaluation (analogous to
  the vulnerability path) or wrap delete+recreate in a transaction.

### F-026 — `ResourceStatisticsCollector` materializes the instance's entire sample history per cycle
- **Severity:** 🟠
- **Criterion:** K7 / K6
- **Phase:** P3
- **File(s):** `src/DockerUpdateGuard/Images/ResourceStatisticsCollector.cs:167-176`
- **Status:** 🆕
- **Finding:** `CollectForInstanceAsync` loads **all** `RuntimeContainerResourceSamples` of the instance
  (`Where(instance).OrderByDescending(RecordedAtUtc).AsNoTracking().ToListAsync()`), only to determine
  via `GroupBy(ContainerId).First()` the most recent sample per container for the delta calculation.
  The table grows between cleanups; with a short sampling interval, more and more rows are loaded
  per run. (The instance sample correctly uses `FirstOrDefaultAsync`.)
- **Impact:** Growing memory/CPU cost in a frequent periodic path, scaling
  with retention × frequency × container count; avoidable.
- **Recommendation:** Load only the most recent sample per container (grouped/window query or filter to
  a narrow time window).

### F-027 — `ScanCleanupBackgroundService` loads all rows to be deleted into memory before `RemoveRange`
- **Severity:** 🟡
- **Criterion:** K7 / K6
- **Phase:** P3
- **File(s):** `src/DockerUpdateGuard/Images/ScanCleanupBackgroundService.cs:103-167`
- **Status:** 🆕
- **Finding:** Each category (old samples, snapshots, findings, scan runs, tag candidates) is
  materialized via `ToListAsync()` and then `RemoveRange`'d. Cleanup specifically targets large backlogs,
  so very large quantities are loaded into memory. `ExecuteDeleteAsync` (in dependency
  order or with DB cascade) avoids the materialization. (The materialization may be deliberate
  to let EF handle the dependent deletions across the graph.)
- **Impact:** Unnecessary materialization; at scale it undermines the purpose of the cleanup.
  Rarely executed (`CleanupIntervalMinutes`), hence 🟡.
- **Recommendation:** Switch to `ExecuteDeleteAsync` in dependency order or use the FK cascade
  rules (P4).

### F-028 — Year-prefixed updates across year boundaries are downgraded to `NeedsReview`
- **Severity:** 🟡
- **Criterion:** K1 (Correctness)
- **Phase:** P3
- **File(s):** `src/DockerUpdateGuard/Images/UpdateDetectionService.cs:211-229` (esp. `:217`)
- **Status:** 🆕
- **Finding:** `GetHigherYearPrefixedCandidates` filters `tagYear == currentYear` — a `2025-*`
  successor to `2024-*` is thus never recognized as `UpdateAvailable`; the case falls into the
  digest/`NeedsReview` path. The method name ("higher year-prefixed candidates") suggests the
  opposite.
- **Impact:** Signal downgrade across year boundaries (real updates appear only as "manual
  review"). Recovers as a review, no data loss.
- **Recommendation:** Clarify intent; for the desired cross-year update, allow `tagYear >= currentYear`
  (or comparison via `CompareYearPrefixedTags`) and test it.

### F-029 — `ImageRegistrationService` searches with an untrimmed name but creates a trimmed one
- **Severity:** 🟡
- **Criterion:** K1 (Correctness)
- **Phase:** P3
- **File(s):** `src/DockerUpdateGuard/Images/ImageRegistrationService.cs:70,77`
- **Status:** 🆕
- **Finding:** `RegisterAsync` searches via `SingleOrDefaultAsync(e => e.Name == request.Name)`
  (untrimmed) but creates `Name = request.Name.Trim()`. A name with surrounding whitespace
  does not match the trimmed stored value → duplicate insert or violation of a unique index
  on `Name`.
- **Impact:** Inconsistent upsert semantics; depending on the constraint, a duplicate entry or a
  `SaveChanges` error.
- **Recommendation:** Trim before the lookup (`var name = request.Name.Trim();`) and use the same value for
  both search and creation.

### F-030 — `_observedImageScanLocks` grows unboundedly
- **Severity:** 🔵
- **Criterion:** K7 / K5
- **Phase:** P3
- **File(s):** `src/DockerUpdateGuard/Images/ImageScanOrchestrator.cs:39,163-166`
- **Status:** 🆕
- **Finding:** The static `ConcurrentDictionary<Guid, SemaphoreSlim>` of per-image scan locks
  never removes entries. For deleted/newly discovered observed images, stale entries remain.
- **Impact:** Small, bounded growth (one `SemaphoreSlim` per scanned image GUID).
  Practically uncritical, since observed images have few/stable IDs.
- **Recommendation:** Optional: clean up locks when an observed image is removed, or use a
  time-/size-bounded cache.

### F-031 — `DockerHubBaseImageResolver` is dead code
- **Severity:** 🔵
- **Criterion:** K2 (Architecture)
- **Phase:** P3
- **File(s):** `src/DockerUpdateGuard/Images/DockerHubBaseImageResolver.cs`; cross-reference `src/DockerUpdateGuard/ServiceCollectionExtensions.cs:118`
- **Status:** 🆕
- **Finding:** `DockerHubBaseImageResolver` (only delegates to `IDockerHubClient.ResolveBaseImagesAsync`)
  is registered or referenced nowhere (repo-wide search → only the file itself). `IBaseImageResolver`
  is registered to `RegistryBaseImageResolver` (`ServiceCollectionExtensions.cs:118`).
- **Impact:** Orphaned code; confusion about the active resolver path. No runtime error.
- **Recommendation:** Remove it or, if intended as an alternative adapter, document/register it.

## Phase 4 — Data (EF Core)

Summary: **0 🔴 · 0 🟠 · 1 🟡 · 0 🔵**. The data layer is consistently
clean and coherent. The model snapshot is in sync with entities/configurations
(confirmed via `dotnet ef migrations has-pending-model-changes`: "No changes have been
made to the model since the last migration"); lookup/time-series fields are indexed,
unique constraints (Registry+Repository, DockerInstance `Name`, Portainer endpoint per
instance, digest triple) are present and, thanks to the `null→""` `ValueConverter`, are
effective even with missing digests; cascade/SetNull/Restrict behavior is well thought out; enums
are versioned with explicit values (`NotSet = 0`). Read paths use `AsNoTracking`
+ server-side projections (no N+1); `SharedBaseImageQueryService` provides correct
aggregates (edge cases "no hits"/"multiple usage" checked); get-or-create handles
races via the unique indexes (`DbUpdateException` → re-query). DI lifetimes (Scoped)
fit, no UI/host dependency (no `ProjectReference` in the `.csproj`).

**Handoffs resolved:** _F-012_ (P1) — the data layer persists **no**
image env vars; `ImageVersion.MetadataJson` contains only tag metadata
(`JsonSerializer.Serialize(tagResult.Data)` in `ImageScanOrchestrator.cs:241,296`),
not `config.Env`. _F-015_ (P2) — captured as the data-layer manifestation **F-021**; the
"unbounded table growth" feared there is **bounded by the retention window**
(deactivation sets `ResolvedAtUtc`, `ScanCleanupBackgroundService` cleans up).

Note (not a finding): the snapshot `ProductVersion` is `10.0.7`, the EF package
(`Directory.Packages.props`) is `10.0.8` — purely cosmetic, will be rewritten on the next
migration. The design-time factory with hardcoded `postgres/postgres` credentials
is acceptable (only `dotnet ef` tooling, never runtime).

### F-021 — VulnerabilityFindings: unique index over nullable columns is ineffective
- **Severity:** 🟡
- **Criterion:** K6 (Data access)
- **Phase:** P4
- **File(s):** `src/DockerUpdateGuard.Data/Configurations/VulnerabilityFindingConfiguration.cs:32-36,60-67`; `src/DockerUpdateGuard.Data/Entities/VulnerabilityFinding.cs:38,43`; migration `src/DockerUpdateGuard.Data/Migrations/InitialCreate.cs:223-224,456-459`; snapshot `…/DockerUpdateGuardDbContextModelSnapshot.cs:707-720,759-760`
- **Status:** 🆕
- **Finding:** The unique index `UNIQUE(ImageVersionId, AdvisoryId,
  AffectedPackage, FixedVersion)` intended as dedup includes the two **nullable** columns
  `AffectedPackage`/`FixedVersion`. Both are never set by the write path
  (`VulnerabilityEnrichmentService`, P3) → always `NULL`. Under PostgreSQL
  (default `NULLS DISTINCT`) as under SQLite, `NULL` values are treated as distinct, so the
  index **never** takes effect. Notably, the same domain layer already solves the problem
  elsewhere correctly — `ImageVersionConfiguration`, `TagCandidateConfiguration`
  and `RuntimeContainerTagSelectionConfiguration` map their `Digest` via a
  `ValueConverter<string?,string>` (`null→""`), so that the respective unique index holds despite
  "missing" values. This idiom was not applied here. (Data-layer
  manifestation of **F-015**; the correctness core there — DTOs/advisory model do not capture
  package/fix version — remains the actual root cause.)
- **Impact:** The unique index guarantees nothing; if a provider returns the same CVE
  for multiple packages, multiple identical active findings arise (overcount in
  "provider reported N finding(s)" and in the UI). Pure write/index overhead without
  benefit. _No_ unbounded growth (see the correction to F-015 above).
- **Recommendation:** Either apply the existing `null→""` converter to `AffectedPackage`/
  `FixedVersion` (the index becomes effective), or set the index to `NULLS NOT DISTINCT` via Npgsql
  `HasIndex(...).AreNullsDistinct(false)`, or filter the
  index on `IsActive`. A prerequisite for real dedup benefit remains that the
  DTOs carry package + fix version (F-015) and the write path populates them.

## Phase 5 — Components / UI / wwwroot

Summary: **0 🔴 · 3 🟠 · 2 🟡 · 0 🔵**. The presentation layer is
mostly cleanly structured: components **never** access the
`DbContext` or HTTP clients directly, only via `IApplicationViewService`
or the command/orchestrator services; markup and logic are separated into
`*.razor`/`*.razor.cs` pairs (no fat inline `@code` blocks), all
read paths use `AsNoTracking` + server-side projections. **No XSS via
`MarkupString`** (no usage anywhere in the repo) — all registry/container/
advisory strings are rendered HTML-encoded via `@expression`, including the
numerically (invariant culture) generated SVG sparkline paths; the SVG assets contain
no script, the only `behavior:` hit in `bootstrap.min.css` is
`scroll-behavior` (false positive in the vendored third-party file). Event subscriptions
(`NavigationManager.LocationChanged`, `DashboardRefreshState.Changed`) are correctly
unsubscribed via `IDisposable` in `MainLayout`/`Dashboard`; detail pages use
`OnParametersSetAsync` (reload on route change), the write paths have clean
`try/catch/finally` with `_errorMessage`/`_isBusy`. Focus of the findings: **a
single unvalidated external-URL `href` sink** (advisory link), **DbContext sharing/
concurrency** across the long-lived Blazor circuit and **N+1 synchronous queries** in the
list projections, plus two UX correctness points (server timezone, loading-vs-
not-found).

**Cross-ref (not a separate finding):** `ApplicationViewService.HasSameOrNewerComparableVersion`
(`src/DockerUpdateGuard/UI/ApplicationViewService.cs:423-428`) carries the same
cross-year restriction (`candidateYear == currentYear`) as [F-028](#f-028--year-prefixed-updates-across-year-boundaries-are-downgraded-to-needsreview)
(P3) — a `2025-*` candidate is not filtered as "available"
for a `2024-*` image. See F-028 for cause and fix.

### F-032 — Shared scoped DbContext across the Blazor circuit; view-service lock does not cover write/scan paths
- **Severity:** 🟠
- **Criterion:** K2 / K5
- **Phase:** P5
- **File(s):** `src/DockerUpdateGuard/ServiceCollectionExtensions.cs:127-128`; `src/DockerUpdateGuard.Data/ServiceCollectionExtensions.cs:27`; `src/DockerUpdateGuard/UI/ApplicationViewService.cs:37,42,1078-1090`; `src/DockerUpdateGuard/UI/RuntimeContainerTagSelectionService.cs:28,60-83`; `src/DockerUpdateGuard/Components/Pages/ObservedImages.razor.cs:158-162`; `src/DockerUpdateGuard/Components/Pages/RuntimeContainers.razor.cs:361`
- **Status:** 🆕
- **Finding:** The `DockerUpdateGuardDbContext` is registered via `AddDbContext` (Scoped); in Blazor Server the scope corresponds to the SignalR **circuit** (potentially open for hours), i.e. **a single DbContext** is shared across the entire circuit lifetime by all scoped services (`ApplicationViewService`, `RuntimeContainerTagSelectionService`, `IImageScanOrchestrator`, `IRuntimeContainerScanOrchestrator`, `IImageRegistrationService` …). `ApplicationViewService` serializes only its **own** read accesses via a private `SemaphoreSlim _dbContextLock` (`:1078-1090`); the other services do **not** participate in this lock. EF Core throws `InvalidOperationException` ("A second operation was started on this context instance…") as soon as a second operation starts on the same context while another is running. Since the continuations consistently use `ConfigureAwait(false)` (run on the thread pool, not on the circuit dispatcher), an overlap is real: e.g. when the topbar in `MainLayout` (subscribed to `LocationChanged`/`DashboardRefreshState.Changed`, `_ = InvokeAsync(LoadSummaryAsync)`) starts a dashboard read while a UI-triggered write/scan path (`RegisterAsync`→`ScanAsync`, `TriggerScanAsync`→`ScanAllAsync`, `SaveSelectionAsync`/`ClearSelectionAsync`) writes on the same context (all use the **circuit** scope, not a fresh one like the background services).
- **Impact:** Sporadic `InvalidOperationException` (aborted render/scan action, possibly circuit teardown) on temporal overlap of read and write/scan; the private lock conveys a deceptive thread safety that only applies reader-against-reader. Additionally: long-lived, shared DbContext = permanently open DB connection per circuit (change-tracker growth mitigated by `AsNoTracking`).
- **Recommendation:** Switch the Blazor UI to `IDbContextFactory<DockerUpdateGuardDbContext>` (`AddDbContextFactory`/`AddPooledDbContextFactory`) and use a short-lived `await using var db = factory.CreateDbContext()` per operation — this eliminates the lock and sharing risk entirely. At minimum, extend `_dbContextLock` to all UI DbContext users, or trigger UI scans not inline on the circuit context but via the background pipeline.

### F-033 — N+1 synchronous queries in the view service's list projections
- **Severity:** 🟠
- **Criterion:** K7 / K6
- **Phase:** P5
- **File(s):** `src/DockerUpdateGuard/UI/ApplicationViewService.cs:665-668` (4×/observed image, via `GetLatestObservedScanStatus` `:1711-1717`, `GetLatestObservedScanMessage` `:1724-1730`); `:1175` (1×/runtime container); `:971-977` (2×/Docker instance, via `GetLatestRuntimeScanStatus` `:1737-1743`)
- **Status:** 🆕
- **Finding:** In the list projections, **synchronous** EF Core queries are issued per row against the shared DbContext. `GetObservedImagesCoreAsync` runs four synchronous queries per image in the `.Select` over the already-materialized `observedImages` list (`UpdateFindings.Count(...)` + `VulnerabilityFindings.Count(...)` + two `FirstOrDefault()` status/message reads) → **4·N** round trips. `GetRuntimeContainersCoreAsync` (also consumed by the dashboard and the instance detail page) calls `VulnerabilityFindings.Count(...)` synchronously per snapshot (1·N). `GetDockerInstancesAsync` issues two synchronous queries per instance (2·N). The synchronous calls block one thread-pool thread each (sync-over-async) while holding the `_dbContextLock` (F-032). Counter-example in the same file: `LoadBaseImageRelationshipsByChildVersionAsync`/`GetBaseImagesCoreAsync` (`:1219-1229,:1437-1447`) correctly load the finding counts in **one** grouped `async` query.
- **Impact:** Latency/load of the main pages (Observed Images, Runtime Containers, Dashboard, Docker Instances) scales linearly with the row count; noticeable in a monitoring tool with many containers/images. Not a correctness error, but an avoidable scaling weakness plus a blocked thread per call.
- **Recommendation:** Load the per-row metrics upfront in **one** grouped `async` query each (`GroupBy(...).Select(g => g.Count())` or a window query for the latest ScanRun status) and map them via dictionary — analogous to the existing pattern for base-image findings; use `*Async` variants throughout.

### F-034 — Unvalidated external advisory URL rendered as `href` (javascript: XSS sink)
- **Severity:** 🟠
- **Criterion:** K3 (Security)
- **Phase:** P5
- **File(s):** `src/DockerUpdateGuard/Components/Pages/MyImageDetail.razor:223-226`; data path `src/DockerUpdateGuard/UI/VulnerabilityFindingViewData.cs:53` ← `ApplicationViewService.MapVulnerabilityFinding` (`:94`) ← `VulnerabilityFinding.ReferenceUrl` (provider Trivy/Docker Scout, P2)
- **Status:** 🆕
- **Finding:** `<MudLink Href="@context.ReferenceUrl" Target="_blank">@context.AdvisoryId</MudLink>` renders the **external, unvalidated** advisory URL directly as `href`. Blazor does HTML-encode the attribute value (no breaking out of the attribute), **but does not validate the URL scheme** — a `javascript:` URL from the advisory data would be preserved as `href` and executed on click in the admin context (DOM/stored XSS). The `ReferenceUrl` originates from the vulnerability providers and is persisted and rendered unchanged; there is no `http(s)` scheme allow-list anywhere. This is the **only** place in the UI where an external string lands as an `href` (all other values are rendered as encoded text). Side finding: `Target="_blank"` without `rel="noopener noreferrer"` (reverse tabnabbing; largely mitigated by modern browsers).
- **Impact:** Stored/DOM XSS sink if a provider/feed delivers a malicious `ReferenceUrl` — made easier if the Trivy endpoint runs over `http://` (cf. [F-019](#f-019--trivybaseurl-only-checked-for-presence-not-as-an-absolute-httphttps-uri)). The target audience is authenticated admins and the provider APIs are semi-trusted, hence not 🔴.
- **Recommendation:** Check for an absolute `http`/`https` scheme before rendering (`Uri.TryCreate(..., UriKind.Absolute)` + scheme allow-list); otherwise render as plain text instead of a link. Add `rel="noopener noreferrer"`. Ideally validate centrally when persisting the advisory.

### F-035 — Timestamps with `ToLocalTime()` in Blazor Server → server timezone instead of user timezone
- **Severity:** 🟡
- **Criterion:** K1 (Correctness)
- **Phase:** P5
- **File(s):** `src/DockerUpdateGuard/Components/Pages/Dashboard.razor:196`; `DockerInstances.razor:128`; `MyImageDetail.razor:112,280,284`; `ObservedImageDetail.razor:119`; `RuntimeContainers.razor:85`; `ScanHistory.razor:49`; `RuntimeContainerDetail.razor:54,113,255,317,437`; `src/DockerUpdateGuard/Components/Layout/MainLayout.razor.cs:164-167`; `src/DockerUpdateGuard/UI/ResourceUsageChartBuilder.cs:135`
- **Status:** 🆕
- **Finding:** Timestamps are consistently rendered with `DateTimeOffset.ToLocalTime().ToString("g")`. In **Blazor Server** this code runs on the **server**; `ToLocalTime()` therefore converts to the server's timezone, not the browser's/user's. For remote users or a server in UTC/a different TZ, all times are displayed incorrectly — and without a TZ indicator.
- **Impact:** Misleading "Started/Published/Recorded/Saved/Checked" times for every user outside the server timezone; affects practically all list and detail pages as well as the resource charts. No data error, purely a display issue.
- **Recommendation:** Display UTC with an explicit suffix or convert client-side to the browser's TZ (JS interop/`TimeProvider`/relative times) and centralize it in a shared formatter — matching the data model that is already consistently UTC-based (`*Utc`).

### F-036 — Detail pages conflate loading and not-found state (perpetual spinner for a missing resource)
- **Severity:** 🟡
- **Criterion:** K1 (Correctness)
- **Phase:** P5
- **File(s):** `src/DockerUpdateGuard/Components/Pages/DockerInstanceDetail.razor:14-18` (+ `.razor.cs:76-85`); `MyImageDetail.razor:16-20` (+ `.razor.cs:100-109`); `ObservedImageDetail.razor:14-18` (+ `.razor.cs:110-119`); `RuntimeContainerDetail.razor:19-24` (+ `.razor.cs:201-210`)
- **Status:** 🆕
- **Finding:** All four detail pages do not distinguish loading and not-found state: `@if (_detail is null) { "Loading…" }`. The view-service methods (`GetObservedImageDetailAsync`/`GetRuntimeContainerDetailAsync`/`GetDockerInstanceDetailAsync`) also return `null` for a **non-existent** ID → `_detail` stays `null` → the page shows "Loading…" **permanently** for deleted/invalid IDs (e.g. a stale bookmark/link after cleanup). Additionally, there is no `try/catch` in the read pages' `OnParametersSetAsync` — a service exception propagates to the Blazor error boundary instead of being reported inline (unlike the write paths, which do this cleanly).
- **Impact:** A confusing perpetual spinner instead of "not found" for a missing resource; loading errors are not visible inline to the user. Purely a UX/state issue (falls within the K1 focus "loading/error states in the UI").
- **Recommendation:** Three-state rendering as in `ObservedImages.razor` (loading → empty/not-found → data): a separate loading flag and a "not found" view; optionally `try/catch` with an `_errorMessage` alert.

## Phase 6 — Host / Telemetry / Infrastructure

Summary: **0 🔴 · 1 🟠 · 4 🟡 · 1 🔵**. The host/cross-cutting scaffolding is
mostly clean: **DI lifetimes are correct, no captive dependencies** —
all singletons (`ApplicationTelemetry`, `DockerInstanceClient`, `PortainerClient`,
all three `IVulnerabilityProvider` implementations) depend exclusively on
singletons (`IHttpClientFactory`, `IOptionsMonitor`, `ILogger`) or on a
static `Func` factory; the providers correctly use `IOptionsMonitor` instead of
`IOptionsSnapshot`. The **middleware order** in `Program.cs` is convention-compliant
(ExceptionHandler→HSTS→HTTPS only outside Development, then StatusCodePages→
Antiforgery→StaticAssets→RazorComponents). The **initialization runs before `app.Run()`**,
i.e. the hosted services start only after the migration completes (no request serving
during the migration). **The OTLP endpoint is validated** (absolute http/https URI,
`TelemetryOptionsValidator.TryCreateEndpoint`), telemetry is cleanly disabled via enable flags,
and **no secrets are logged at startup** (only counters/account name;
the connection string is not logged). `ApplicationTelemetry` passes the `DbContext`
through as a parameter (no captive DbContext in the singleton) and the observable gauges are
updated after each scan/cleanup via `RefreshInventoryMetricsAsync` (not frozen).
Focus of the findings: **startup resilience of the auto-migration** (no retry/no
instance coordination), **telemetry coverage & dead name constants** (central
`scan.run`/Portainer/CVE spans never started, `TelemetryLogPropertyNames` unused)
as well as smaller resilience/DI points.

**Note (not a separate finding):** The `.Telemetry.csproj` sets — like all project files —
`Deterministic=False` (a consequence of the wildcard `AssemblyVersion("1.0.*")`, documented under
[F-039](#f-039--dockerupdateguardcsproj-duplicate-property--non-deterministic-release-build)).
No duplicate `GenerateDocumentationFile` here; otherwise consistent project conventions.

### F-040 — Critical paths without their own trace spans: `scan.run`/Portainer/CVE/persistence never started
- **Severity:** 🟡
- **Criterion:** K11 (Observability)
- **Phase:** P6
- **File(s):** `src/DockerUpdateGuard.Telemetry/TelemetryActivityNames.cs:13,28,33,38,43`; `src/DockerUpdateGuard.Telemetry/TelemetryTagNames.cs:33,38,43`; spans in use: `src/DockerUpdateGuard/Docker/DockerInstanceClient.cs:953`, `src/DockerUpdateGuard/DockerHub/DockerHubClient.cs:506,566,714`; cross-reference metrics `src/DockerUpdateGuard/ApplicationTelemetry.cs:111-127`
- **Status:** 🆕
- **Finding:** Of the seven declared `TelemetryActivityNames`, only **two** are ever
  started as a custom span: `DockerHubRequest` (`DockerHubClient.cs:506,566,714`) and
  `DockerEngineRequest` (`DockerInstanceClient.cs:953`). A repo-wide search confirms **zero**
  usages for `ScanRun` (`scan.run`), `CveProviderRequest`, `PortainerRequest`,
  `PortainerAction` and `PersistenceOperation`. Thus there is no parent span for the
  central scan lifecycle: the orchestrators do record metrics
  (`ApplicationTelemetry.RecordScanRun`), but open **no** `scan.run` activity under
  which the DockerHub/Engine child spans and a scan's DB work could be correlated.
  The **security-critical Portainer action path** (stop/kill/restart) and the
  CVE provider calls also have no spans — despite reserved names. In parallel, the
  tag constants `ActionType`, `ErrorClass` and `ScanId` (`TelemetryTagNames.cs:33,38,43`)
  are set nowhere. (Auto-instrumentation for ASP.NET Core/HttpClient is active via
  `AddAspNetCoreInstrumentation`/`AddHttpClientInstrumentation`; Portainer actions
  are additionally logged via `PortainerClientLogging` — the loss concerns *traces*, not *logs*.)
- **Impact:** Without a `scan.run` root span, scans cannot be traced end-to-end; the
  existing child spans hang uncorrelated under the ASP.NET/background context. For an
  observability-centric tool, a noticeable gap in the trace coverage of critical paths
  (K11 guiding question), especially for the destructive Portainer path.
- **Recommendation:** In the three orchestrators, wrap the scan run in a `scan.run` span (`ActivityKind.Internal`/
  `Server`) (with `ScanId`/`ScanType`/`ResultStatus` tags) and instrument the
  Portainer actions with `PortainerAction` spans (incl. `ActionType`); otherwise
  remove the unused constants (cf. F-041).

### F-041 — Dead telemetry name constants: `TelemetryLogPropertyNames` entirely unused
- **Severity:** 🟡
- **Criterion:** K11 / K9
- **Phase:** P6
- **File(s):** `src/DockerUpdateGuard.Telemetry/TelemetryLogPropertyNames.cs` (entire); cross-reference actual log templates `src/DockerUpdateGuard/HostLoggingExtensions.cs:47-56` and the remaining `*LoggingExtensions`
- **Status:** 🆕
- **Finding:** The class `TelemetryLogPropertyNames` (eight constants, some aliases of
  `TelemetryTagNames`) is referenced **nowhere** in production code (repo-wide search for
  `TelemetryLogPropertyNames.` → empty). Structured logging runs throughout via
  source-generated `[LoggerMessage]` templates with **ad-hoc** named placeholders
  (e.g. `{ObservedImages}`, `{RuntimeContainers}` in `HostLoggingExtensions.cs:49`), not
  via these central property names. The intended "shared, centrally maintained
  property naming convention" is thus not realized. (Sibling to
  [F-040](#f-040--critical-paths-without-their-own-trace-spans-scanrunportainercvepersistence-never-started):
  there too, activity/tag names are only partially wired up.)
- **Impact:** Dead code that suggests a consistency that does not exist; log property
  names drift apart between the `*LoggingExtensions` (no single source of truth).
  Purely maintainability/convention-related, no runtime error.
- **Recommendation:** Either switch the `[LoggerMessage]` templates to the central names
  (for stable, correlatable log attributes across the OTLP logging pipeline) or remove the unused
  class + unused `TelemetryTagNames`/`TelemetryActivityNames` members.

### F-042 — Auto-migration at startup: no retry, no instance coordination, no error handling
- **Severity:** 🟠
- **Criterion:** K4 (Resilience)
- **Phase:** P6
- **File(s):** `src/DockerUpdateGuard/ApplicationInitializationExtensions.cs:36-37`; `src/DockerUpdateGuard/ServiceCollectionExtensions.cs:49-52`
- **Status:** 🆕
- **Finding:** `InitializeDockerUpdateGuardAsync` calls `dbContext.Database.MigrateAsync()` **without
  try/catch**; an exception propagates uncaught from `Main` → the process exits.
  The Npgsql registration (`UseNpgsql`, `:51`) sets **no** `EnableRetryOnFailure`/no
  execution strategy. In the documented container scenario (the app starts alongside its
  PostgreSQL instance), the DB is often not yet connection-ready at boot → `MigrateAsync`
  throws → crash; recovery hinges solely on the container restart policy (crash loop until the
  DB is ready). Additionally, there is **no migration coordination across multiple instances**:
  EF Core takes no cross-instance lock for `MigrateAsync`; under horizontal scaling,
  two instances starting simultaneously can both read "migration X is missing" and apply it —
  one fails (duplicate in `__EFMigrationsHistory` or "relation already exists") and crashes.
  PostgreSQL's transactional DDL prevents data corruption (a failed migration rolls
  back), but not the crash of the losing instance.
- **Impact:** Crash loop on delayed DB availability at boot; startup race in
  multi-instance operation. No data loss (transactional DDL), but a real
  startup-resilience gap exactly in the area addressed by the prompt.
- **Recommendation:** (1) Wait/retry for DB availability at startup (`EnableRetryOnFailure` and/or
  a bounded wait-for-DB before `MigrateAsync`); (2) serialize migration across multiple instances
  (a PostgreSQL advisory lock around `MigrateAsync`, or move migration out of app startup into a
  dedicated init job/leader); (3) catch migration errors specifically and log them with clear
  diagnostics instead of as a raw startup exception.

### F-043 — Eager startup discovery with `CancellationToken.None`, redundant to the hosted services
- **Severity:** 🟡
- **Criterion:** K4 / K5
- **Phase:** P6
- **File(s):** `src/DockerUpdateGuard/ApplicationInitializationExtensions.cs:39-44`; cross-reference hosted services `src/DockerUpdateGuard/ServiceCollectionExtensions.cs:129-135`
- **Status:** 🆕
- **Finding:** After the migration, the initialization synchronously runs
  `SynchronizeConfiguredInstancesAsync`, `SynchronizeAccountImagesAsync` and
  `RefreshInventoryMetricsAsync` — each with **`CancellationToken.None`**. The same
  discovery work is done anyway by the registered hosted services
  (`DockerInstanceDiscoveryBackgroundService`, `DockerHubAccountImageDiscoveryBackgroundService`)
  periodically; the eager call is thus largely redundant. `CancellationToken.None` means:
  even a SIGTERM during a slow startup sync (Docker Hub network) does not abort the operation
  — readiness is delayed until all three steps (bounded by the HttpClient timeout)
  complete. On the positive side: the network errors themselves are **degraded** rather than thrown
  (`SynchronizeAccountImagesAsync` checks `ExternalOperationResult.Status`, `:188-194`;
  `SynchronizeConfiguredInstancesAsync` is pure config/DB work) — so an unreachable
  Docker Hub/instance does **not** crash the startup.
- **Impact:** Delayed/blocked readiness at boot, not abortable on shutdown;
  duplicate initial execution of the discovery. No correctness/security consequence.
- **Recommendation:** Pass the app-lifetime token (`IHostApplicationLifetime.ApplicationStopping`) instead of
  `CancellationToken.None`; leave the initial discovery to the hosted services
  (e.g. "RunOnStartup" in `ScheduledBackgroundService`) or time-box it, so that startup
  does not hang on external services.

### F-044 — `Telemetry:Instance` is mapped to `deployment.environment.name` (semantic mismatch)
- **Severity:** 🟡
- **Criterion:** K11 / K12
- **Phase:** P6
- **File(s):** `src/DockerUpdateGuard.Telemetry/TelemetryServiceCollectionExtensions.cs:221-225`; `src/DockerUpdateGuard.Telemetry/TelemetryResourceAttributeNames.cs:13`; `src/DockerUpdateGuard.Telemetry/TelemetryOptions.cs:30-32`
- **Status:** 🆕
- **Finding:** The option `TelemetryOptions.Instance` is documented as "Logical deployment **instance**
  name" but is set as the resource attribute **`deployment.environment.name`**
  (`ConfigureResource`, `:221-225`). Per the OpenTelemetry semantic conventions,
  `deployment.environment.name` denotes the *environment* (e.g. `production`/`staging`), while an
  *instance* is expressed via `service.instance.id`. If an operator sets `Instance="node-1"`
  per the docs, this value appears in the backend as the deployment environment "node-1" —
  misleading. Either the option is misnamed (meant: environment) or the attribute is
  chosen wrong (meant: instance ID).
- **Impact:** Misattributed telemetry resources; dashboards/filters by "environment"
  or "instance" find nothing or mix the dimensions. Purely a convention/
  correctness issue of the observability metadata.
- **Recommendation:** Clarify the intent and align: either rename the option to `Environment` and
  keep it on `deployment.environment.name`, or switch the attribute to `service.instance.id`
  (possibly offer both concepts separately). Side point: `GetServiceVersion` (`:249`)
  reads the version only from the environment variable `DockerUpdateGuard__DisplayVersion` (not from
  `IConfiguration`); a `DisplayVersion` set via `appsettings` is ignored for telemetry
  (the fallback to `InformationalVersion` is correct) — check for consistency.

### F-045 — Two `DockerHubClient` instances per scope → duplicated token cache (amplifies F-009)
- **Severity:** 🔵
- **Criterion:** K2 (Architecture)
- **Phase:** P6
- **File(s):** `src/DockerUpdateGuard/ServiceCollectionExtensions.cs:54-59,109-111`
- **Status:** 🆕
- **Finding:** `DockerHubClient` is registered as **transient** via `AddHttpClient<DockerHubClient>`.
  Both `IDockerHubClient` (`:109`) and one of the `IRegistryMetadataClient`
  registrations (`:110`) resolve it via `serviceProvider.GetRequiredService<DockerHubClient>()`
  in **separate** scoped factories — each scoped registration caches its own instance,
  and since the type is transient, **two** different `DockerHubClient` objects arise per scope
  (one behind `IDockerHubClient`, one in the `IRegistryMetadataClient` enumerable). Each holds
  its own token cache + `SemaphoreSlim`, so the duplicate Docker Hub authentication described in
  [F-009](#f-009--dockerhub-access-token-cache-tied-to-scope-lifetime) is
  doubled again per scope.
- **Impact:** Additional login requests against the Docker Hub quota budget; slightly more
  allocation. No correctness error.
- **Recommendation:** Resolve `DockerHubClient` once per scope (e.g. `AddScoped<DockerHubClient>`
  as the base and have the facades point at it) or — together with F-009 — move the token cache into
  a singleton service, so that instance count and scope binding become irrelevant.

## Phase 7 — Tests

Summary: **0 🔴 · 4 🟠 · 1 🟡 · 0 🔵**. The test suite is mostly
**high quality**: real assertions throughout instead of smoke tests (incl. precise
NSubstitute `Received` verifications and log-EventId checks), clean
**deterministic** HTTP test doubles (`SequenceHttpMessageHandler`/`StubHttpMessageHandler`
with URI-mapped, cloned responses; `TimeoutHttpMessageHandler` via real
cancellation instead of wall-clock), and the **core paths of the clients are broadly covered**:
`DockerInstanceClient` tests the error paths (disabled→`NotConfigured`,
ssh→`Unsupported`, timeout→`Failed` with a dedicated log, missing RepoDigest),
`DockerHubClient`/`OciRegistryClient` test pagination, 401→token→retry,
platform matching (OCI) and base-image recursion, the orchestrators cover
success/partial/derived findings and **per-container resilience**, and
`InstanceDiscoveryService`/`DockerHubAccountImageDiscoveryService`/`ScanCleanupBackgroundService`/
`*ReleaseMetadataService` are solid (incl. cascade delete, skip paths,
`NotFound` paths). The options validator and DI registration are thoroughly
checked (the latter incidentally confirms **F-031**: `RegistryBaseImageResolver` is the
active `IBaseImageResolver`). **K9 naming** is consistently compliant
(`{Class}Tests` / `{Class}{Scenario}{ExpectedResult}`).

Focus of the findings: **gaps precisely on the correctness core paths marked as risky in P1–P3**
— the image-reference parser is practically untested with a single test
(F-046), the SemVer/pre-release/overflow edge cases from
[F-021](#f-021--pre-releasevariant-suffix-numbers-are-discarded--identical-versions-compare-equal-semver-pre-release-ordering-missing)/[F-022](#f-022--unhandled-overflowexception-for-overly-long-version-components)
are missing, and the cross-year restriction from [F-028](#f-028--year-prefixed-updates-across-year-boundaries-are-downgraded-to-needsreview)
is even pinned as expected behavior by two tests (F-047), the
scan-resilience defects [F-024](#f-024--scanallasync-aborts-the-entire-batch-if-the-prepost-processing-of-an-item-throws)/[F-025](#f-025--findings-are-deleteddeactivated-before-the-scan-and-not-restored-on-errors--alerts-disappear-transiently)
are not protected by regression tests (F-048), the Blazor UI is checked only via
reflection-helper tests instead of rendered components (F-049), and the
**test DB fidelity** (SQLite/InMemory instead of PostgreSQL, `EnsureCreated` instead of
migrations) hides PostgreSQL-specific defects and provides a deceptive safety in the
concurrency test against [F-032](#f-032--shared-scoped-dbcontext-across-the-blazor-circuit-view-service-lock-does-not-cover-writescan-paths) (F-050).

**Test gaps already captured elsewhere (cross-reference, not a new finding):**
[F-011](#f-011--portainerclient-lacks-automated-tests-critical-action-path) —
`PortainerClient` (destructive container action path) has **no** test counterpart;
[F-016](#f-016--provider-paths-insufficiently-tested-scoutdefault-not-at-all-trivy-only-happy-path) —
`DockerScoutVulnerabilityProvider`/`DefaultVulnerabilityProvider` untested, Trivy
only happy path. Both remain valid in P7 and are referenced in the matrix at the
affected test/source files.

### F-046 — `ImageReferenceParser` is practically untested with a single test case
- **Severity:** 🟠
- **Criterion:** K8 (Tests)
- **Phase:** P7
- **File(s):** `src/Tests/DockerUpdateGuard.Tests/ImageReferenceParserTests.cs` (1 test); cross-reference source `src/DockerUpdateGuard/Images/ImageReferenceParser.cs`
- **Status:** 🆕
- **Finding:** `ImageReferenceParserTests` contains **exactly one** test case
  (`…ParseWrappedMicrosoftRegistryReferenceNormalizesRegistry`) for the narrowly scoped
  "docker.io/mcr.microsoft.com/…" unwrap case. `ImageReferenceParser` is the
  entry point of nearly all image processing (tag/digest logic P3, provider lookups
  P2, orchestrators) and handles a wide variety of Docker reference forms. Untested
  remain, among others: implicit registry/repository defaults (`nginx` → `docker.io/library/nginx`,
  implicit `:latest`), digest references (`repo@sha256:…`), combined tag+digest
  (`repo:tag@sha256:…`), registry with port (`localhost:5000/app`), multi-part
  repository paths (`ghcr.io/org/team/app`), casing and malformed/empty inputs.
- **Impact:** A regression in the central reference normalization would propagate
  across all downstream paths (wrong registry/repo/tag/digest → wrong
  lookups, wrong update/CVE attribution) and would remain undetected by the test net.
  This directly contradicts the K8 guiding question "are the correctness core paths covered?".
- **Recommendation:** Add data-driven tests (`[DataRow]`) over the typical reference forms
  — implicit defaults, digest/tag+digest references, registry-with-port,
  multi-part repos, as well as negative/edge cases (empty/invalid input).

### F-047 — Tag/digest correctness core: pre-release/overflow edge cases untested; cross-year restriction (F-028) pinned as expected
- **Severity:** 🟠
- **Criterion:** K8 (Tests)
- **Phase:** P7
- **File(s):** `src/Tests/DockerUpdateGuard.Tests/VersionTagResolutionHelperTests.cs` (3 tests); `src/Tests/DockerUpdateGuard.Tests/UpdateDetectionServiceTests.cs` (`…YearCuTagOnlyUsesSameYearSuccessors` :155-190, `…YearPrefixedTagUsesSameYearSuccessors` :196-227)
- **Status:** 🆕
- **Finding:** The tag/digest area marked in P3 as the highest correctness risk is
  solidly tested in the happy path (digest change, SemVer successor, MCR variant family,
  latest-alias-up-to-date, 50-cap), but leaves exactly the **edge cases found in P3
  untested**: no test for pre-release ordering (`-rc1` < `-rc2`, pre-release→GA) or
  variant sub-versions (`-alpine3.18` vs `-alpine3.19`) from
  [F-021](#f-021--pre-releasevariant-suffix-numbers-are-discarded--identical-versions-compare-equal-semver-pre-release-ordering-missing),
  and no test for the `OverflowException` `Try` contract breach with ≥10-digit
  version components from
  [F-022](#f-022--unhandled-overflowexception-for-overly-long-version-components).
  More seriously: `UpdateDetectionServiceYearCuTagOnlyUsesSameYearSuccessors` and
  `…YearPrefixedTagUsesSameYearSuccessors` **pin the faulty
  cross-year restriction from
  [F-028](#f-028--year-prefixed-updates-across-year-boundaries-are-downgraded-to-needsreview)
  as correct expected behavior** (they assert that a `2022-*` successor to
  a `2019-*` image is specifically **not** recommended).
- **Impact:** The most error-prone correctness paths (from P3 with five 🟠 findings)
  have no regression net for their edge cases; a fix for F-028 would moreover **fail** against the
  existing tests, which actively hinders the fix and cements the misbehavior.
- **Recommendation:** Add negative/edge-case tests for F-021 (`-rcN`/`-betaN` ordering, pre-release→GA,
  variant sub-versions) and F-022 (overly long component → `false`/no exception);
  when fixing F-028, switch the two year-line tests to the desired
  cross-year behavior (instead of fixing the current state in place).

### F-048 — Scan-resilience defects (F-024 batch abort, F-025 transient finding loss) without regression test
- **Severity:** 🟠
- **Criterion:** K8 (Tests)
- **Phase:** P7
- **File(s):** `src/Tests/DockerUpdateGuard.Tests/ImageScanOrchestratorTests.cs`; `src/Tests/DockerUpdateGuard.Tests/RuntimeContainerScanOrchestratorTests.cs`
- **Status:** 🆕
- **Finding:** The orchestrator suites are overall strong and cover the
  **per-container** resilience exemplarily (`…ContinuesAfterContainerProcessingFailureAsync`:
  one broken container → `Partial`, the rest run on). What remains uncovered, however, is
  exactly the two resilience defects found in P3: (a)
  [F-024](#f-024--scanallasync-aborts-the-entire-batch-if-the-prepost-processing-of-an-item-throws)
  — an exception **outside** the inner `try` (e.g. `SingleAsync` on an image deleted between
  listing and scan, or a DB error on the initial `ScanRun` save)
  aborts **all subsequent items** in `ScanAllAsync`/`ScanInstanceAsync`; all
  batch tests, however, use only **one** instance or skip the pre-`try` throw. (b)
  [F-025](#f-025--findings-are-deleteddeactivated-before-the-scan-and-not-restored-on-errors--alerts-disappear-transiently)
  — no test seeds **active** findings and then makes discovery/registry
  fail, to check whether existing alerts survive the transient error
  (per F-025 they do not).
- **Impact:** Two defects with user impact (starving subsequent items;
  flickering "update available" alerts) can regress unnoticed or remain
  unverified on a fix — exactly on the paths the review marked as 🟠.
- **Recommendation:** (1) A multi-instance/multi-item batch test in which the **first** item throws in the
  pre-`try` region, and verification that the remaining items are scanned nonetheless. (2) A test with
  pre-existing active findings + a failing discovery that proves
  active findings are deleted/deactivated only after a successful re-evaluation.

### F-049 — Blazor UI tested only via reflection helpers; rendered components (incl. F-034 sink) untested
- **Severity:** 🟡
- **Criterion:** K8 (Tests)
- **Phase:** P7
- **File(s):** `src/Tests/DockerUpdateGuard.Tests/DashboardTests.cs`, `MainLayoutTests.cs`, `NavMenuTests.cs`, `MyImagesTests.cs`, `MyImageDetailTests.cs`, `RuntimeContainersTests.cs`; cross-reference `src/Tests/DockerUpdateGuard.Tests/DockerUpdateGuard.Tests.csproj` (no bUnit package)
- **Status:** 🆕
- **Finding:** All component tests are **reflection unit tests of private
  `.razor.cs` helpers** (color mappers, route mapping, section titles,
  `GetProtectedAssetCount`, `BuildSparklinePath`). The helpers are well data-driven
  covered (case-insensitivity, null/blank), but **not** a single component is
  actually rendered (no bUnit in the project). Thus the render risks found in P5 have
  **zero test coverage**: the unvalidated advisory `href` `javascript:` sink
  [F-034](#f-034--unvalidated-external-advisory-url-rendered-as-href-javascript-xss-sink),
  the `ToLocalTime()` server timezone
  [F-035](#f-035--timestamps-with-tolocaltime-in-blazor-server--server-timezone-instead-of-user-timezone)
  and the loading-vs-not-found conflation
  [F-036](#f-036--detail-pages-conflate-loading-and-not-found-state-perpetual-spinner-for-a-missing-resource).
  The reflection access also couples the tests to private method names (brittle).
- **Impact:** Render/markup regressions — including the only
  security-relevant XSS sink of the UI — remain undetected. Lower than the
  correctness core paths, since the underlying defects are already captured as P5 findings
  and this is the presentation layer — hence 🟡.
- **Recommendation:** Add bUnit (or Playwright component tests), at least for the
  advisory-link sink (F-034: a `javascript:` URL is **not** rendered as `href`) and the
  three-state detail pages (F-036); then replace the reflection-helper tests with real
  render assertions.

### F-050 — Test DB fidelity: SQLite/InMemory instead of PostgreSQL hides F-021 and provides deceptive F-032 safety
- **Severity:** 🟠
- **Criterion:** K8 (Tests — determinism/significance)
- **Phase:** P7
- **File(s):** `src/Tests/DockerUpdateGuard.Tests/Data/SqliteTestDatabase.cs`; `src/Tests/DockerUpdateGuard.Data.Tests/Data/SqliteTestDatabase.cs` (`EnsureCreated`); `src/Tests/DockerUpdateGuard.Tests/ApplicationViewServiceTests.cs` (`UseInMemoryDatabase`, esp. `…ConcurrentReadsCompleteWithoutDbContextOverlapAsync` :1234-1281); `src/Tests/DockerUpdateGuard.Tests/ScanCleanupBackgroundServiceTests.cs` (`UseInMemoryDatabase`)
- **Status:** 🆕
- **Finding:** The data/query/orchestrator tests run against **in-memory SQLite**
  (schema via `EnsureCreated`, **not** via the migrations) or the **EF InMemory** provider —
  never against the production PostgreSQL. Consequences: (a) PostgreSQL-specific semantics like
  `NULLS DISTINCT` — the cause of the ineffective unique index in
  [F-021](#f-021--vulnerabilityfindings-unique-index-over-nullable-columns-is-ineffective)
  — is **not reproducible**; (b) the **migrations themselves** are run by no test
  (only the model via `EnsureCreated`), so migration↔model drift would remain
  undetected test-side; (c) `ApplicationViewServiceConcurrentReadsCompleteWithoutDbContextOverlapAsync`
  runs on the InMemory provider, which does **not** enforce EF Core's "a second operation was started on this
  context" guard — the test **cannot in principle** prove the real shared-DbContext defect
  [F-032](#f-032--shared-scoped-dbcontext-across-the-blazor-circuit-view-service-lock-does-not-cover-writescan-paths)
  that its name suggests (deceptive safety); (d)
  the N+1 synchronous queries from
  [F-033](#f-033--n1-synchronous-queries-in-the-view-services-list-projections)
  "work" against InMemory and are thus not surfaced.
- **Impact:** Several confirmed defects (F-021, F-032) are **in principle undiscoverable** through the chosen
  test infrastructure; one test even carries a name that
  promises protection against F-032 without being able to deliver it. This undermines the
  significance of the data-layer tests.
- **Recommendation:** For the critical data-layer/concurrency paths, test against a real
  PostgreSQL (Testcontainers or similar), build the schema via `Database.MigrateAsync()`
  instead of `EnsureCreated()` (which also tests the migrations), and either
  run the concurrency test against a relational provider with an active
  single-operation guard or honestly mark/remove it as non-meaningful.

## Phase 8 — Root / Konfiguration / Doku

Summary: **0 🔴 · 1 🟠 · 6 🟡 · 1 🔵**. **No secrets checked in** —
`appsettings.json`/`launchSettings.json` contain only empty strings, the
release pipeline correctly uses GitHub `secrets.*` (no plaintext). The
**README config reference is consistent** with `appsettings.json` and the options code
(P1/P6): all keys, defaults and `[Range]`/`Required` markers match
(`DisplayVersion` is genuinely consumed via `IConfiguration`/env in `NavMenu.razor.cs:97,104`;
the `Telemetry:ServiceName` requirement is covered by `TelemetryOptionsValidator:58`).
The central package versions (`Directory.Packages.props`) are consistent
(EF Core 10.0.8 throughout), the rulesets are plausible (Debug lenient,
Release `RHxxxx`→Error). Focus of the findings: **CI/container drift after the
Azure DevOps→GitHub Actions migration** (fc81f4f) — the release pipeline pulls the
digest of the **wrong** base image, `.slnx` points to the deleted
`azure-pipelines.yml`, the new workflows are missing from the solution & review matrix —
as well as **documentation contradictions** (license, Copilot instructions) and **container hardening**
(root, `.dockerignore`).

> **Note on matrix maintenance:** `azure-pipelines.yml` (P8 line 19) was
> deleted in fc81f4f and replaced by `.github/workflows/ci.yml` + `release.yml`.
> These two actually existing files were missing from the matrix; they were added as
> rows `4a`/`4b` and reviewed in place of the Azure pipeline (see **F-036**).

### F-032 — Container image runs as root (no `USER` in the Dockerfile)
- **Severity:** 🟡
- **Criterion:** K3 (Security)
- **Phase:** P8
- **File(s):** `src/DockerUpdateGuard/Dockerfile:27-51` (runtime stage without `USER`); `src/DockerUpdateGuard/entrypoint.sh:17`; cross-reference docs `DOCKER.md:100,154`
- **Status:** 🆕
- **Finding:** The runtime stage sets no `USER`; the process runs as **root**. `entrypoint.sh` imports CA certificates specifically only when `id -u = 0` or the trust store is writable (`:17`) — the function is thus designed for the root run; `DOCKER.md:100,154` likewise documents this as "typically when run as root". The app is then started via `exec` **without dropping privileges** in the same root context.
- **Impact:** Best-practice/CIS violation (container not run as root). Honest assessment: the dominant escalation path is the mounted `docker.sock` (the README's main scenario) — socket access alone allows host takeover regardless of the container UID, a non-root user does **not** fully fix that. Root, however, unnecessarily widens the blast radius for non-socket attack surfaces (file system, trust-store persistence). Hence 🟡.
- **Recommendation:** Set a non-root `USER` (numeric UID); move the CA import into the build stage or run it as an optional, documented root init step; document socket access via group GID instead of root.

### F-033 — Release pipeline extracts the digest of the wrong base image → inconsistent OCI label
- **Severity:** 🟠
- **Criterion:** K1 (Correctness) / K12
- **Phase:** P8
- **File(s):** `.github/workflows/release.yml:52-57,68-70`; `src/DockerUpdateGuard/Dockerfile:2,29-33`
- **Status:** 🆕
- **Finding:** The step "Extract base runtime digest" pulls and inspects `mcr.microsoft.com/dotnet/runtime:10.0-alpine` (`release.yml:55-56`) and feeds its digest as `BASE_RUNTIME_DIGEST` (`:68-70`). The Dockerfile, however, builds the runtime stage on `mcr.microsoft.com/dotnet/aspnet:10.0-alpine` (`Dockerfile:2`, not overridden) and writes the labels `org.opencontainers.image.base.name="…/aspnet:10.0-alpine"` and `org.opencontainers.image.base.digest="${BASE_RUNTIME_DIGEST}"` (`:32-33`). Name (`aspnet`) and digest (`runtime`) thus belong to **different** images; the referenced digest is not even present in the final image.
- **Impact:** The published image carries a **wrong provenance statement**. This is especially relevant because DockerUpdateGuard itself tracks base-image digests: if it scans its own image, it reads contradictory labels. SBOM/supply-chain analyses are misled. No runtime error (the app runs on aspnet), hence 🟠 instead of 🔴.
- **Recommendation:** In the digest step, pull/inspect `mcr.microsoft.com/dotnet/aspnet:10.0-alpine` (identical to the Dockerfile's `BASE_RUNTIME`); ideally derive the image tag from a shared source (build arg), so that name and digest cannot diverge.

### F-034 — License contradiction: README says "proprietary", LICENSE.md is MIT
- **Severity:** 🟡
- **Criterion:** K12 (Documentation)
- **Phase:** P8
- **File(s):** `README.md:277`; `LICENSE.md:1-3`
- **Status:** 🆕
- **Finding:** `README.md:277` states: "This repository is distributed under the **proprietary terms** described in LICENSE.md." `LICENSE.md`, however, contains the **MIT license** (permissive, "Permission is hereby granted, free of charge…", Copyright (c) 2026 e-networld).
- **Impact:** A direct contradiction between the README and the actual license file. For users/third parties it is legally unclear whether the work is MIT-licensed or proprietary — materially relevant for use/distribution, even though it is not a code defect.
- **Recommendation:** Determine the state intended by the project owner and align both places (either correct the README to MIT or replace `LICENSE.md` with the proprietary text).

### F-035 — `.github/copilot-instructions.md` outdated: describes the repo as an empty skeleton
- **Severity:** 🟡
- **Criterion:** K12 (Documentation)
- **Phase:** P8
- **File(s):** `.github/copilot-instructions.md:36,59-76` (esp. `:36,:70`)
- **Status:** 🆕
- **Finding:** `:36` claims: "The repository is currently a solution skeleton with project wiring in place, but **almost no implementation files yet**." The repo actually contains a complete implementation (~330 files: Docker/DockerHub/Portainer clients, EF Core data layer incl. migrations, Blazor UI, telemetry). The "Current state note" block (`:59-76`) is phrased throughout in founding/template mode ("when the solution is created", "If the project adds EF Core migrations…") and references a foreign project **SeriesOverwatch** (`:70`), even though migrations have long existed (`InitialCreate`). `csharp.instructions.md` also uses template examples (`F1Server`, `PacketAnalyzer`) — defensible as a style illustration, but an indication of unmaintained copy-paste origin.
- **Impact:** AI assistants and new contributors are misinformed about the project state; the migration instruction references an unrelated project.
- **Recommendation:** Update the "Current state"/skeleton section to the real state, replace the SeriesOverwatch reference with the project's own migration convention.

### F-036 — `.slnx`/CI drift after Azure→GitHub Actions migration
- **Severity:** 🟡
- **Criterion:** K12 / Build-CI
- **Phase:** P8
- **File(s):** `DockerUpdateGuard.slnx:6`; `.github/workflows/ci.yml`, `.github/workflows/release.yml` (not in `.slnx`); deleted: `azure-pipelines.yml` (commit fc81f4f)
- **Status:** 🆕
- **Finding:** The CI/CD migration (fc81f4f) removed `azure-pipelines.yml` and replaced it with GitHub Actions workflows, but did not update the solution: `DockerUpdateGuard.slnx:6` still references `<File Path="azure-pipelines.yml" />` (a missing entry in Visual Studio/`dotnet sln`). The new workflows `ci.yml`/`release.yml` are **not** listed in the `.slnx` (the "Copilot" folder lists only the two instruction files). Additionally, the review matrix listed the deleted `azure-pipelines.yml` and omitted the workflows (see the matrix note above).
- **Impact:** Dangling reference in the solution; the actually effective pipeline is not part of the solution view. Purely structural, no runtime error.
- **Recommendation:** In `.slnx`, remove the `azure-pipelines.yml` entry and add `.github/workflows/ci.yml` + `release.yml` (e.g. under "Solution items").

### F-037 — CI does not verify formatting (the documented `reihitsu-format` step is missing from the pipeline)
- **Severity:** 🟡
- **Criterion:** Build-CI / K12
- **Phase:** P8
- **File(s):** `.github/workflows/ci.yml:27-39`; `.github/workflows/release.yml:33-44`; intended docs `README.md:48`, `.github/copilot-instructions.md:24,30`
- **Status:** 🆕
- **Finding:** The README, Copilot instructions and the Serena memories list `reihitsu-format ./` as a **mandatory** step before every build. Both workflows, however, run only `restore` → `build` → `test`; there is **no** format/lint verification step (no `reihitsu-format --check`/`dotnet format --verify-no-changes`). Formatting drift is not detected in CI. (The `RHxxxx` analyzer rules do apply as errors at build time in the release ruleset — but pure formatting is not fully covered by that.)
- **Impact:** The formatting documented as mandatory is not enforced; style/format regressions can reach `main` unnoticed. Low functional impact.
- **Recommendation:** Add a verification step (e.g. `reihitsu-format` in check mode if available, otherwise `dotnet format --verify-no-changes`) that fails the build on drift.

### F-038 — `.dockerignore` minimal: no protection against leaking `appsettings.*.json`/certificates in local builds
- **Severity:** 🟡
- **Criterion:** K3 (Security)
- **Phase:** P8
- **File(s):** `.dockerignore:1-8`; cross-reference `src/DockerUpdateGuard/Dockerfile:21-22`
- **Status:** 🆕
- **Finding:** `.dockerignore` excludes only `.vs/.git/bin/obj/node_modules/.idea`, `*.md` and `.gitignore`. It does **not** exclude `appsettings.*.json` (e.g. `appsettings.Development.json`), `*.pfx`/`*.crt`/`*.pem` or a `certs/` directory. The Dockerfile copies the entire context (`COPY . .`, `:21`) and publishes (`:22`); the Web SDK includes `appsettings.*.json` by default as content in the publish output (`/app/publish`). With a **local** `docker build` with an existing, populated `appsettings.Development.json` or locally placed certificates, these end up in the final image.
- **Impact:** Potential baking-in of developer secrets/keys into the image during local builds. CI builds are not affected (fresh checkout; `appsettings.Development.json` is excluded via `.gitignore:363`). Hence 🟡.
- **Recommendation:** Harden `.dockerignore`: add `**/appsettings.*.json` (except the base `appsettings.json` if deliberately desired), `*.pfx`, `*.crt`, `*.pem`, `**/certs/` and `docs/`.

### F-039 — `DockerUpdateGuard.csproj`: duplicate property & non-deterministic release build
- **Severity:** 🔵
- **Criterion:** K9 / K12
- **Phase:** P8
- **File(s):** `src/DockerUpdateGuard/DockerUpdateGuard.csproj:8,11,18,24`; `SharedAssemblyInfo.cs:13`
- **Status:** 🆕
- **Finding:** `<GenerateDocumentationFile>True` is declared twice (`:8` and `:11`). `<Deterministic>False` is set for Debug **and** Release (`:18,:24`) — necessary because `SharedAssemblyInfo.cs:13` uses `AssemblyVersion("1.0.*")` (wildcard), which precludes deterministic builds. Consequence: release builds are not reproducible, and `AssemblyVersion` is always `1.0.<auto>` (the real version is correctly in `InformationalVersion`/`FileVersion`/`DisplayVersion`).
- **Impact:** Cosmetic redundancy; non-reproducible release assemblies. Practically uncritical, since display/diagnostics run via `InformationalVersion`/`DisplayVersion`.
- **Recommendation:** Remove the duplicate `GenerateDocumentationFile` line. If reproducible builds are desired: use a fixed `AssemblyVersion` (e.g. from `$(Version)`) instead of `1.0.*` and leave `Deterministic` at its default (`true`).
