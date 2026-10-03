# Squad Decisions

Process decisions about how the squad works in this repository — not decisions about the product code
(those are decision records in `docs/decisions/`). Append-only: add a dated entry, never rewrite an old
one. Changed only in squad-maintenance PRs.

- 2026-10-03 — Squad adopted from Squad-Spec-Repository-Template (`adopt-template`), stack profile
  `dotnet`. Reason: one shared, stack-neutral squad and rule set across all repositories; project
  knowledge lives in `.squad/stack.md`, `.squad/project.md` and the `<!-- project:… -->` sections of the
  instruction files.
