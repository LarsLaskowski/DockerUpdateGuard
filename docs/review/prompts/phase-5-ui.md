# Phase 5 — Components / UI / wwwroot (Blazor)

> **Start in a new chat:** "Read and execute `docs/review/prompts/phase-5-ui.md`."

## Context

The presentation layer: Razor components, UI helper logic, and static assets.
Dashboard, Observed Images, Runtime Containers, Docker Instances, Shared Base
Images, Scan History.

**Scope: 59 files.** Modules: `App/Components` (31), `App/UI` (24),
`App/wwwroot` (4).

## Your file list (authoritative from the matrix)

```powershell
Select-String -Path docs/review/file-inventory.md -Pattern '\| P5 \|' |
  ForEach-Object { ($_.Line -split '\|')[3].Trim().Trim('`') }
```

## Criteria

Read [../criteria.md](../criteria.md). Particular focus:

- **K3 Security:** XSS — unchecked `MarkupString`/`@((MarkupString))` usage,
  rendering registry/container strings without encoding.
- **K2 Architecture:** Do components access the DbContext/HTTP clients directly
  instead of going through services? Separation of markup ↔ logic. Sensible component
  lifetime, `IDisposable`/`await using` for subscriptions.
- **K1 Correctness:** render logic, null/empty states, loading/error states in the
  UI, correct status/indicator display.
- **K9 Conventions:** consistent Razor style; lean `@code` blocks.

## Workflow

1. **Triage** of all 59 files → indicators + status in [../file-inventory.md](../file-inventory.md).
   CSS/SVG under `wwwroot` are usually quickly ✅ (Sec./Correct. = —).
2. **Deep-dive** for 🔬 → findings `F-NNN` in [../findings.md](../findings.md)
   (section "Phase 5"), link, status ✅.
3. Update [../progress.md](../progress.md) row P5.

## Completion condition

```powershell
(Select-String -Path docs/review/file-inventory.md -Pattern '\| P5 \|.*\| ⬜ \|').Count  # must be 0
```
