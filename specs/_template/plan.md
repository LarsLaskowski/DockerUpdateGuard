# Plan: <title>

Source: Issue #<number> | [spec.md](spec.md)
Status: Draft | Revised (n) | Approved by Security
Tier: trivial | standard | security  (tier `docs` uses no plan.md) — <one-sentence justification>

## Problem / root cause

For a bug: the cause, with file and line. For a feature: a summary of `spec.md`.
Each factual claim of the issue, checked against the code: confirmed or refuted.

## Acceptance criteria

- [ ] AC1: ... (the Tester turns each one into at least one unit test)

## Approach

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |

## Signatures (for the Dev's skeleton)

Every new or changed member, with its full signature — or "none".

## Test files

Named strictly by the convention in *Layout* of [`.squad/stack.md`](../../.squad/stack.md) and
[UNIT_TESTS.md](../../docs/UNIT_TESTS.md) — no combined file, no alternatives.

Existing test code that calls a changed signature (factories, helpers): the call sites, and who adapts them —
the Dev in step 4 (skeleton) when the old signature goes away, so the suite keeps building; the Tester in
step 5 when old and new signature coexist (`.squad/routing.md`, *Loop limits*). "None" if no existing
test is affected.

## Documentation updates

`README.md` (configuration table, env vars), `docs/*.md` — or "none".

## Architecture check

Which guarantees from `docs/ARCHITECTURE.md` are touched and how they are preserved.

## Security considerations

## Decision records

- `docs/decisions/NNNN-title.md` (Proposed) — or "none: no decision beyond the obvious fix"

## Challenge

Left out when the plan is written. The Lead adds it in mode `revise` only after Devil's Advocate
objections: each objection and the answer (accepted — what changed; or rejected — why). A clean challenge
(`NO OBJECTIONS`) is recorded only in `log.md`.

## Out of scope / follow-ups
