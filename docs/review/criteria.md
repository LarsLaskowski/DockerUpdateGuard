# Review Criteria (K1–K12)

Reference for all phases. Every file is evaluated along these axes. In the
matrix file ([file-inventory.md](file-inventory.md)) the criteria are grouped into **four
focus columns**; the details are listed here.

Traffic light per axis: 🟢 ok · 🟡 note/minor weakness · 🔴 finding · — n/a.

## Focus "Security" (matrix column *Sec.*)

| ID | Axis | Specifically checked |
|----|------|----------------------|
| K3 | Security | Secrets/credentials in plain text (DockerHub `Pat`, Portainer `Password`/`ApiToken`), logging of secrets, `SkipCertificateValidation`/TLS bypass, Docker socket access, registry/Portainer auth, command/path/header injection, SSRF on configurable URLs |
| K4 | Error Handling & Resilience | HTTP timeouts set, retry/backoff logic, swallowed exceptions (`catch {}`), background-service crash behavior, error paths for external services (Trivy/Scout/Portainer/Engine) |

## Focus "Correctness" (matrix column *Correct.*)

| ID | Axis | Specifically checked |
|----|------|----------------------|
| K1 | Correctness/Logic | Faulty conditions, edge cases, null behavior, tag/digest comparison logic, SemVer resolution, alias tag handling (`latest`) |
| K5 | Async/Concurrency | `CancellationToken` propagation, `async void`, `.Result`/`.Wait()` deadlocks, parallelism limits, quota-window calculation, thread safety of shared state |
| K6 | Data Access (EF Core) | N+1 queries, tracking vs. `AsNoTracking`, migration consistency with the model, index/constraint coverage, nullable/required mapping |
| K7 | Performance/Resources | Allocations in hot paths, `HttpClient` reuse (no socket exhaustion), sampling frequency, unnecessary materialization |

## Focus "Architecture & Maintainability" (matrix column *Arch.*)

| ID | Axis | Specifically checked |
|----|------|----------------------|
| K2 | Architecture & Layering | Clean separation Host ↔ Data ↔ Telemetry, no layer violations (UI does not access DbContext directly), DI cleanliness, lifetime correctness (Singleton/Scoped) |
| K9 | Conventions/Style | File-scoped namespaces, one top-level type per file, `#region` blocks, XML docs on public/internal/private, Allman braces, `var` preferred, no `this.`, `== false` style, Reihitsu conformance |
| K10 | Configuration & Validation | Options validators present, sensible defaults, required fields enforced, README/config consistency |
| K11 | Observability | Telemetry naming conventions (see `Telemetry*Names.cs`), traces/metrics present in critical paths, sensible log levels |
| K12 | Documentation | XML doc completeness & correctness, README.md/DOCKER.md up to date against code |

## Focus "Tests" (matrix column *Tests*)

| ID | Axis | Specifically checked |
|----|------|----------------------|
| K8 | Tests | Coverage of critical paths, meaningfulness (real assertions instead of smoke), missing negative/edge cases, test naming `{Feature}Tests` / `{Class}{Scenario}{ExpectedResult}`, determinism (no flakiness due to time/network) |

## Severity Levels for Findings (findings.md)

| Level | Meaning |
|-------|---------|
| 🔴 **High** | Security vulnerability, data loss, correctness error with user impact |
| 🟠 **Medium** | Functional error in edge case, resilience/performance weakness, missing critical tests |
| 🟡 **Low** | Convention violation, documentation gap, minor maintainability weakness |
| 🔵 **Info** | Observation/improvement idea without a concrete error |
