# Phase 3 — Images (tag/digest logic, base image chains)

> **Start in a new chat:** "Read and execute `docs/review/prompts/phase-3-images.md`."

## Context

The largest logic surface of the app: resolution of tags/digests, SemVer comparison,
alias tags (`latest`), derived base runtime detection, update evaluation, and
shared base image dependencies. This is where the core verdicts "up to date /
outdated / manual check" are produced.

**Scope: 60 files.** Module: `App/Images` (incl. subfolders such as `Enums`).

## Your file list (authoritative from the matrix)

```powershell
Select-String -Path docs/review/file-inventory.md -Pattern '\| P3 \|' |
  ForEach-Object { ($_.Line -split '\|')[3].Trim().Trim('`') }
```

## Criteria

Read [../criteria.md](../criteria.md). Particular focus:

- **K1 Correctness:** SemVer parsing & ordering, digest comparison, alias resolution
  (`latest` → concrete version at the same digest), `UpdateEvaluationStatus` logic,
  edge cases (pre-release, missing tags, ambiguous digests).
- **K5 Async/Concurrency:** parallelism on registry queries, quota/request budget,
  `CancellationToken`.
- **K6 Data access:** how `ObservedImage`/`ImageVersion`/`TagCandidate` are loaded
  (N+1?), tracking behavior.
- **K7 Performance:** hot paths during mass scans, allocations, caching.

> Due to the size (60 files), split into sub-sessions if needed — the matrix tracks
> progress, you can resume at the first ⬜ P3 row at any time.

## Workflow

1. **Triage** → indicators + status in [../file-inventory.md](../file-inventory.md).
2. **Deep-dive** for 🔬 → findings `F-NNN` in [../findings.md](../findings.md)
   (section "Phase 3"), link, status ✅.
3. Update [../progress.md](../progress.md) row P3.

## Completion condition

```powershell
(Select-String -Path docs/review/file-inventory.md -Pattern '\| P3 \|.*\| ⬜ \|').Count  # must be 0
```
