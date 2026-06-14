# DockerUpdateGuard – Code Review Workspace

Self-contained workspace for the complete repo review. Goal: assess **all 330
tracked files** against fixed criteria, with seamless traceability.

## Files in this folder

| File | Purpose |
|-------|-------|
| [README.md](README.md) | This overview / starting point |
| [criteria.md](criteria.md) | Evaluation criteria K1–K12 + severity levels (methodology) |
| [file-inventory.md](file-inventory.md) | **Review matrix** – all 330 files, status & traffic lights per focus area. Single Source of Truth. |
| [findings.md](findings.md) | Findings list `F-NNN`, grouped by phase, with severity |
| [progress.md](progress.md) | Phase progress at a glance |
| [prompts/](prompts/) | A self-contained start prompt per phase (P1–P8) |

## How to start a phase in a **new chat**

1. Open the matching prompt under `prompts/phase-N-*.md`.
2. In the new chat, write: **"Read and execute `docs/review/prompts/phase-1-docker-security.md`."**
   (or paste the prompt content directly).
3. The prompt is self-contained: it references the criteria, matrix, and findings list
   and includes the file selection, workflow, and completion condition. A cold start
   is enough – no knowledge from previous chats is needed.

## Phases (ordered by risk)

| Phase | Scope | Files |
|-------|--------|--------:|
| P1 | Docker / DockerHub / Portainer / Configuration (security core) | 33 |
| P2 | Vulnerabilities (external providers) | 18 |
| P3 | Images (tag/digest logic, base-image chains) | 60 |
| P4 | Data – EF Core (Entities, Configurations, Migrations, Queries, Repos) | 65 |
| P5 | Components / UI / wwwroot (Blazor) | 59 |
| P6 | Host / Telemetry / Infrastructure | 17 |
| P7 | Tests | 52 |
| P8 | Root / Configuration / Docs | 26 |

Phases are independent and can run in any order / in parallel in
separate chats, since every file belongs to **exactly one** phase.

## Per-file workflow (triage → deep dive)

1. **Triage:** open the file, set the traffic lights (Sec./Correct./Arch./Tests),
   set the status in the matrix to ✅ – or, for 🟡/🔴, initially 🔬.
2. **Deep dive** only for 🔬 files: line-by-line analysis; create the finding as `F-NNN` in
   [findings.md](findings.md) and link it in the matrix *Findings* column.
3. Status after the deep dive to ✅. **No file stays ⬜.**

## Completion

The overall review is done when no ⬜ row remains in the matrix and, in
[progress.md](progress.md), all phases are ✅. Counting commands are in progress.md.
