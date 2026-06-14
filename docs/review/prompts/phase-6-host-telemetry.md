# Phase 6 — Host / Telemetry / Infrastructure

> **Start in a new chat:** "Read and execute `docs/review/prompts/phase-6-host-telemetry.md`."

## Context

App bootstrap and cross-cutting concerns: `Program.cs`, DI wiring, host logging,
application initialization (incl. automatic EF migrations at startup),
OpenTelemetry setup, and infrastructure helpers.

**Scope: 17 files.** Modules: `App` (root: Program/Application*/Host*/
ServiceCollectionExtensions), `App/Infrastructure` (2), `Telemetry` (10).

## Your file list (authoritative from the matrix)

```powershell
Select-String -Path docs/review/file-inventory.md -Pattern '\| P6 \|' |
  ForEach-Object { ($_.Line -split '\|')[3].Trim().Trim('`') }
```

## Criteria

Read [../criteria.md](../criteria.md). Particular focus:

- **K2 Architecture:** DI lifetimes correct (Singleton/Scoped/Transient), no
  captive dependencies; sensible ordering of the pipeline/middleware.
- **K4 Resilience:** Behavior of the automatic migration at startup (error case,
  race with multiple instances); robust background service registration.
- **K11 Observability:** telemetry naming conventions (`Telemetry*Names.cs`)
  used consistently; validators (`TelemetryOptionsValidator`) correct; sensible
  trace/metric coverage of critical paths; appropriate log levels.
- **K3 Security:** no secrets in the startup log; OTLP endpoint validation.

## Workflow

1. **Triage** → indicators + status in [../file-inventory.md](../file-inventory.md).
2. **Deep-dive** for 🔬 → findings `F-NNN` in [../findings.md](../findings.md)
   (section "Phase 6"), link, status ✅.
3. Update [../progress.md](../progress.md) row P6.

## Completion condition

```powershell
(Select-String -Path docs/review/file-inventory.md -Pattern '\| P6 \|.*\| ⬜ \|').Count  # must be 0
```
