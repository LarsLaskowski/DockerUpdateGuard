# Review Progress

Phase status at a glance. The source of truth for *files* remains the
[review matrix](file-inventory.md); this table is the phase overview.

Status: ⬜ not started · 🔬 in progress · ✅ completed

| Phase | Scope | Files | Prompt | Status | Reviewed | Findings |
|-------|--------|--------:|--------|--------|--------:|--------:|
| **P1** | Docker / DockerHub / Portainer / Configuration | 33 | [prompts/phase-1-docker-security.md](prompts/phase-1-docker-security.md) | ✅ | 33/33 | 12 |
| **P2** | Vulnerabilities | 18 | [prompts/phase-2-vulnerabilities.md](prompts/phase-2-vulnerabilities.md) | ✅ | 18/18 | 8 |
| **P3** | Images | 60 | [prompts/phase-3-images.md](prompts/phase-3-images.md) | ✅ | 60/60 | 11 |
| **P4** | Data (EF Core) | 65 | [prompts/phase-4-data.md](prompts/phase-4-data.md) | ✅ | 65/65 | 1 |
| **P5** | Components / UI / wwwroot | 59 | [prompts/phase-5-ui.md](prompts/phase-5-ui.md) | ✅ | 59/59 | 5 |
| **P6** | Host / Telemetry / Infrastructure | 17 | [prompts/phase-6-host-telemetry.md](prompts/phase-6-host-telemetry.md) | ✅ | 17/17 | 6 |
| **P7** | Tests | 52 | [prompts/phase-7-tests.md](prompts/phase-7-tests.md) | ✅ | 52/52 | 5 |
| **P8** | Root / Configuration / Docs | 28 | [prompts/phase-8-config-docs.md](prompts/phase-8-config-docs.md) | ✅ | 28/28 | 8 |
| | **Total** | **332** | | | **332/332** | **56** |

> **P8 note:** The original list comprised 26 files including `azure-pipelines.yml`,
> which was **deleted** in the course of the CI/CD migration (commit fc81f4f). The
> effectively active workflows `.github/workflows/ci.yml` + `release.yml` were missing from the matrix
> and were added as rows `4a`/`4b` → P8 now counts 28 matrix rows (27 real
> files + the Azure row marked as "removed"). Details: F-036.
>
> **Overall review completed:** all phases P1–P8 are ✅, no ⬜ row remains in
> the [matrix](file-inventory.md) (332/332 reviewed). **Overall severity summary
> across all 56 findings: 0 🔴 · 21 🟠 · 28 🟡 · 7 🔵.** No 🔴 finding — the security
> and correctness core is solid; the 🟠 findings concern resilience/hardening
> (missing retry/backoff, scan-batch abort, transient finding loss),
> correctness edge cases (SemVer pre-release/cross-year, Scout severity/registry) and
> test gaps (provider/parser/resilience paths, test-DB fidelity).
>
> **P7 note:** Finding IDs are not globally unique across the phases
> (P3/P4 share `F-021`; P5/P8 share `F-032`–`F-036`). Phase 7 therefore
> deliberately uses the collision-free range **F-046–F-050**.

## Overall review completion condition

Done when **no ⬜ row** remains in the [matrix](file-inventory.md)
(countable) and every phase above is ✅.

Quick count (PowerShell):

```powershell
# open rows total
(Select-String -Path docs/review/file-inventory.md -Pattern '\| ⬜ \|').Count
# open rows of a phase, e.g. P1
(Select-String -Path docs/review/file-inventory.md -Pattern '\| P1 \|.*\| ⬜ \|').Count
```
