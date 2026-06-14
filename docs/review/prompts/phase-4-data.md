# Phase 4 — Data (EF Core: entities, configurations, migrations, queries, repos)

> **Start in a new chat:** "Read and execute `docs/review/prompts/phase-4-data.md`."

## Context

The data layer (`src/DockerUpdateGuard.Data`, EF Core / PostgreSQL):
entities, Fluent configurations, migrations, the DbContext, repositories, and
query services. Focus on model/migration consistency and query efficiency.

**Scope: 65 files.** Modules: `Data/Entities` (31), `Data/Configurations` (15),
`Data/Migrations` (9), `Data/Queries` (4), `Data/Repositories` (2) + the Data root
(DbContext, ServiceCollectionExtensions, design factory, csproj).

## Your file list (authoritative from the matrix)

```powershell
Select-String -Path docs/review/file-inventory.md -Pattern '\| P4 \|' |
  ForEach-Object { ($_.Line -split '\|')[3].Trim().Trim('`') }
```

## Criteria

Read [../criteria.md](../criteria.md). Particular focus:

- **K6 Data access:** Are migrations consistent with the current model (snapshot vs.
  entities/configurations)? Are indexes/unique constraints present for lookup fields
  (digests, tags, endpoints)? Is nullable/required mapped correctly?
  Is cascade/delete behavior sensible?
- **K1 Correctness:** Query services (`SharedBaseImageQueryService`) — do they return
  correct aggregates? Edge cases (no matches, multiple usage).
- **K7 Performance:** `AsNoTracking` for read paths, projections instead of
  full materialization, no N+1.
- **K2 Architecture:** Data layer without UI/host dependencies; DI registration
  (`ServiceCollectionExtensions`) with correct lifetimes.

## Workflow

1. **Triage** → indicators + status in [../file-inventory.md](../file-inventory.md).
   Entities/enums are often quickly ✅; configurations/queries/migrations more closely.
2. **Deep-dive** for 🔬 → findings `F-NNN` in [../findings.md](../findings.md)
   (section "Phase 4"), link, status ✅.
3. Update [../progress.md](../progress.md) row P4.

## Completion condition

```powershell
(Select-String -Path docs/review/file-inventory.md -Pattern '\| P4 \|.*\| ⬜ \|').Count  # must be 0
```
