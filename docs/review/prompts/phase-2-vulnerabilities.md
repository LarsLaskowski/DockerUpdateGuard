# Phase 2 — Vulnerabilities (external scan providers)

> **Start in a new chat:** "Read and execute `docs/review/prompts/phase-2-vulnerabilities.md`."

## Context

This phase reviews the vulnerability integration: connectivity to external providers
(`None`, `DockerScout`, `Trivy`), retrieval, parsing, and persistence of
vulnerability data. External network input + config-driven provider selection.

**Scope: 18 files.** Module: `App/Vulnerabilities`.

## Your file list (authoritative from the matrix)

```powershell
Select-String -Path docs/review/file-inventory.md -Pattern '\| P2 \|' |
  ForEach-Object { ($_.Line -split '\|')[3].Trim().Trim('`') }
```

## Criteria

Read [../criteria.md](../criteria.md). Particular focus:

- **K3/K4 Security & Resilience:** `TrivyBaseUrl` as an external endpoint (SSRF,
  timeout, TLS), robust behavior on provider failure/partial response, no
  credential/token leak in logs.
- **K1 Correctness:** Parsing of provider responses (severity mapping, missing
  fields, version/CVE mapping). Behavior with `Provider=None`/`Enabled=false`.
- **K5 Async:** `CancellationToken` propagated, parallelism bounded.
- **K8 Tests:** Are all three provider paths tested (including error/empty cases)?

## Workflow

1. **Triage** of all 18 files → indicators + status in [../file-inventory.md](../file-inventory.md).
2. **Deep-dive** for 🔬 → findings `F-NNN` in [../findings.md](../findings.md)
   (section "Phase 2"), link in the matrix, status ✅.
3. Update [../progress.md](../progress.md) row P2.

## Project conventions

See [../criteria.md](../criteria.md) (K9). Review/docs only, no unrelated
code changes without approval.

## Completion condition

```powershell
(Select-String -Path docs/review/file-inventory.md -Pattern '\| P2 \|.*\| ⬜ \|').Count  # must be 0
```
