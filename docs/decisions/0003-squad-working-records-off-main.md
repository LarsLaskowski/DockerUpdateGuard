# 0003: Squad working records stay off main, and product PRs never change the squad

- **Status:** Accepted
- **Date:** 2026-10-03
- **Source:** Squad adopted from Squad-Spec-Repository-Template
- **Supersedes:** —

## Context

The squad writes a plan and a log per issue or feature under `specs/`. Kept on `main`, these records
accumulate without being read again, while the lasting reasoning already lives in decision records. Squad
lessons fixed inside a product PR mix two unrelated changes and are easy to lose in a later template
refresh.

## Options considered

1. **Keep `specs/` on `main`** — full history in the repository; grows with every change and duplicates
   the decision records.
2. **Working records only on the work branch** — posted as a "Squad working record" comment on the issue
   (or the PR) before the PR opens, then removed; `main` keeps only `specs/README.md` and the templates.

## Decision

Option 2. In addition, an issue or feature PR never changes the squad or the agent instructions
(`.squad/` except `stack.md` and `project.md`, `.claude/`, `.github/skills/`, `.agents/skills/`,
`CLAUDE.md`, `AGENTS.md`, `.github/copilot-instructions.md`). Lessons about the squad become a GitHub issue
labelled `squad`: lessons about template-managed files in the template repository
(LarsLaskowski/Squad-Spec-Repository-Template), fixed there and rolled out with `adopt-template`; lessons
about project knowledge in the product repository, worked in a separate squad-maintenance PR.

## Consequences

- `main` stays free of per-change working records; the issue comment keeps them findable.
- A crashed session can still resume, because the work folder is committed on the work branch.
- Squad fixes need their own PR, even when they are small.
