# Phase 7 — Tests

> **Start in a new chat:** "Read and execute `docs/review/prompts/phase-7-tests.md`."

## Context

The two MSTest projects. This phase assesses test coverage and quality and
mirrors it against the risks found in P1–P6 (are the critical paths
tested?).

**Scope: 52 files.** Modules: `Tests/DockerUpdateGuard.Tests` (46),
`Tests/DockerUpdateGuard.Data.Tests` (6).

## Your file list (authoritative from the matrix)

```powershell
Select-String -Path docs/review/file-inventory.md -Pattern '\| P7 \|' |
  ForEach-Object { ($_.Line -split '\|')[3].Trim().Trim('`') }
```

## Criteria

Read [../criteria.md](../criteria.md). Main axis **K8 Tests**:

- **Meaningfulness:** real assertions instead of smoke; does the test actually
  exercise the behavior or just "run through"?
- **Coverage:** are the correctness core paths (tag/digest logic from P3, providers from
  P2, query services from P4) covered? Call out missing negative/edge cases.
- **Determinism:** no flakiness over time/network/order; test doubles
  (HTTP handler helper) used correctly.
- **K9 Naming:** `{Feature}Tests` / `{Class}{Scenario}{ExpectedResult}`.

> Optional: use the SonarQube coverage skill (`sonar-coverage`) to back up
> coverage gaps objectively — then record findings as `F-NNN` as usual.

## Workflow

1. **Triage** of all 52 files → indicators (especially the Tests column) + status in
   [../file-inventory.md](../file-inventory.md).
2. **Deep-dive** for 🔬 → findings `F-NNN` in [../findings.md](../findings.md)
   (section "Phase 7"), link, status ✅. Missing-test gaps are also
   findings (severity typically 🟠).
3. Update [../progress.md](../progress.md) row P7.

## Completion condition

```powershell
(Select-String -Path docs/review/file-inventory.md -Pattern '\| P7 \|.*\| ⬜ \|').Count  # must be 0
```
