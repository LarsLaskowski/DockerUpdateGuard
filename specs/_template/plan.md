# Plan: <title>

Source: Issue #<number> | [spec.md](spec.md)
Status: Draft | Revised (n) | Approved by Security
Tier: trivial | standard | security  (tiers `docs` and `patch` use no plan.md) — <one-sentence justification>

## Problem / root cause

For a bug: the cause, with file and line. For a feature: a summary of `spec.md`.
Each factual claim of the issue, checked against the code: confirmed or refuted.

## Acceptance criteria

- [ ] AC1: ... (the Tester turns each one into at least one unit test; in a plan without production or test
  code, *Verification without tests* below says how it is verified instead)

Content the issue supplies verbatim (an image, SVG, configuration, a fixture) was rendered or exercised once
before "identical to the issue" became a criterion; a mismatch with the evident intent is an escalation
question here. A criterion that fixes an error text names the exact text the tests compare. A criterion
about a container image or another build output names the *Image build check* from `.squad/stack.md` as its
verification, run by the Dev before the review (*Criteria verified only in CI* in `.squad/routing.md`) — a
build from the full working tree verifies nothing about the image.

## Behavior coverage

For a feature: each bullet of `spec.md`'s *Behavior* mapped to the acceptance criterion that covers it, or
"out of scope" — the Tester checks that every field a criterion names is asserted. Otherwise delete this section.

## Verification without tests

Only when the change touches no production or test code, in any tier: steps 4, 5 and the *Coverage gate*
are not applicable (*Changes without production or test code* in `.squad/routing.md`). Every acceptance
criterion is then verified here instead of by a test: where and by whom (workflow step, PR dry run,
read-only check). Otherwise delete this section.

## Approach

## Options considered

Each alternative weighed and why it lost — or "one option: <why the fix is the only reasonable one>". The
orchestrator launches the Devil's Advocate only for a plan that weighs options, adds or extends a decision
record, or is `security` (*Plan challenge* in `.squad/routing.md`).

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |

## Signatures (for the Dev's skeleton)

Every new or changed member, with its full signature — or "none".

## Test files

Named strictly by the convention in *Layout* of [`.squad/stack.md`](../../.squad/stack.md) and
[UNIT_TESTS.md](../../docs/UNIT_TESTS.md) — no combined file, no alternatives.

Existing test code that calls a changed signature (factories, helpers): the call sites and who adapts them
(`.squad/routing.md`, *Loop limits*) — or "none".

Files of the change no unit test can cover (a browser script without a test runner, a generated file), each
with the reason and the coverage exclusion that follows (`.squad/routing.md`, *Code a unit test cannot
cover*) — or "none".

## Areas

The area documents in `docs/areas/` this change touches (`docs/areas/README.md` lists them), and what changes in
each — or "none" (no change in behavior). A new area: name and scope for the index, a Lead decision. A change
in behavior updates its area document in this pull request.

## Documentation updates

`README.md` (configuration table, env vars), `docs/*.md` including the area documents — or "none". Every documentation edit has exactly
one owner (Dev, Tester or Lead); the Lead's approval step only touches status, index rows and bookkeeping.

## Architecture check

Which guarantees from `docs/ARCHITECTURE.md` are touched and how they are preserved.

## Security considerations

- [ ] Every byte or length limit on parsed input says whether it applies before or after decoding (a
  replacement such as U+FFFD can multiply the size of raw bytes) — or "no limits".
- [ ] Every exception that can reach a user-visible failure reason or message is listed, with the text
  that reaches the user (never a raw runtime exception message) — or "none".

## Decision records

- `docs/decisions/NNNN-title.md` (Proposed) — or "none: no decision beyond the obvious fix"

## Challenge

Left out when the plan is written. The Lead adds it in mode `revise` only after a major Devil's Advocate
objection: each objection and the answer (accepted — what changed; or rejected — why). When the challenge
returns only minor objections, the orchestrator appends them verbatim as a *Challenge notes* section instead,
which Dev and Tester follow (or report a deviation) and the Reviewer checks. A clean challenge
(`NO OBJECTIONS`) is recorded only in `log.md`.

## Security review

`security` tier only, left out when the plan is written: each `CHANGES_REQUIRED` finding and how the
revision resolves it.

## Out of scope / follow-ups
