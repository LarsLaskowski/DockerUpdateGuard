# 0001: Quality gates before the pull request

- **Status:** Accepted
- **Date:** 2026-10-03
- **Source:** Squad adopted from Squad-Spec-Repository-Template
- **Supersedes:** —

## Context

Formatting slips, analyzer findings and missing tests that only surface in CI or in the SonarQube Cloud
analysis of the pull request cost an extra fix-and-push round each, and every round costs reviewer
attention. The squad has a Code Officer whose sole job is to leave nothing of that kind behind, and the
`create-pr` skill runs the same checks outside the squad.

## Options considered

1. **Keep everything in CI** — a safety net for every contributor, but findings keep arriving late.
2. **Check locally and in CI** — earliest feedback plus a safety net; some checks run twice.
3. **Check locally first, CI as the system of record** — format, analyzer gate and coverage gate run
   before the push (`.squad/stack.md`); CI keeps the build, the tests and the code analysis.

## Decision

Option 3. Before a push: *Format*, the *Analyzer gate* (no diagnostic of any severity in a changed file)
and the *Coverage gate* (at least 80 % line coverage on new/changed production code and overall) from
`.squad/stack.md` pass. CI still builds, tests and runs the code analysis (e.g. SonarQube Cloud), which
stays the system of record for the quality gate. Squad and agent tooling (`.squad/**`, `.claude/**`) is
excluded from the coverage measure in CI; it is developer tooling, not production code.

## Consequences

- Findings appear while coding; the Code Officer clears them before the review.
- A push that skips the local gates is only caught by what CI still checks. The PR template checklist and
  the `create-pr` skill require the local gates; adding a CI step back is the remedy if unchecked code
  starts reaching `main`.
- The local analyzers may differ from the CI quality profile, and some checks (duplication, security
  hotspots, taint analysis) only run in CI; such findings arrive in squad step 11.
- The 80 % rule applies to new or changed lines, so it does not force retroactive tests on old code.
