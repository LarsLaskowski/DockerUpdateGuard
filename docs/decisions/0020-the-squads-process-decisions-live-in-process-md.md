# 0020: The squad's process decisions live in process.md

- **Status:** Accepted
- **Date:** 2026-11-06
- **Area:** —
- **Source:** Squad refreshed from Squad-Spec-Repository-Template
- **Supersedes:** [0001](0001-quality-gates-before-the-pull-request.md), [0002](0002-squash-merge-pull-requests.md), [0003](0003-squad-working-records-off-main.md) (the released process records of earlier template versions it retires; an unreleased record that amended one of them is deleted with them)

## Context

Earlier versions of the squad template numbered their process decisions — squad working records off `main`,
squash merges, quality gates before the pull request — into this log, and every refresh that changed one of their
rules added a record that superseded or amended it. They are decisions of the template, not of this product, yet
they took numbers here, and every reader of the index walked past them.

## Options considered

1. **Keep numbering them** — the log stays the one place for every decision; each template change adds another
   record to it.
2. **One managed file** — [`process.md`](process.md), overwritten by every refresh, without a number or an index
   row and not checked by `decision-check.py`; this log holds the product's decisions only.

## Decision

Option 2. The squad's process decisions are kept in [`process.md`](process.md), which supersedes the records listed
above; a later change to them reaches that file with the next refresh, never this log.

## Consequences

- The index lists the product's decisions only.
- A process decision is changed in the template repository; a local edit of `process.md` is lost on the next refresh.
- The superseded records stay as they are (released records are append-only) and state what held when they were
  released.
