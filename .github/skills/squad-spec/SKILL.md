---
name: squad-spec
description: Use when the user wants to develop a new feature in this repository spec-driven with the squad. Lead writes spec, plan and tasks and picks a tier, the Devil's Advocate challenges them, Security reviews security-relevant plans, Tester writes failing tests first, Dev implements to 80% coverage, Code Officer clears format and analyzer findings, Reviewer (+ Security) review, Lead approves, then a PR is opened.
---

# Squad Spec

Build a feature with the squad defined in `.squad/`. Tiers, pipeline, loop limits, escalation rules,
commit/push rules and the orchestrator role are identical to the `squad-issue` skill — follow its steps
1–12 with these changes:

- **Step 1 — work folder and branch:** `specs/feature-<short-slug>/` with `log.md` from
  `specs/_template/log.md`; branch `feature-<short-slug>` off the latest `main` (or the branch the session
  prescribes).
- **Step 2 — plan:** `squad-lead` in mode `plan` writes `spec.md` (behavior, acceptance criteria, out of
  scope), `plan.md` and `tasks.md`. A feature is never `docs` and rarely `trivial`. It is more likely than a bug
  fix to need a product decision — the Lead escalates whenever the request does not settle user-visible
  behavior. `RESULT: NO CHANGE` means the feature already exists or contradicts an accepted decision; report
  that to the user instead of commenting on an issue. The plan challenge covers `spec.md`, `plan.md` and
  `tasks.md` together.
- **Decision records:** features usually involve real design choices, so expect at least one record in
  `docs/decisions/`; the Lead also updates `docs/ARCHITECTURE.md` when the feature changes a flow or
  guarantee.
- **Step 3** reviews `spec.md` and `plan.md` together.
- **Steps 4–6** run per task or group of tasks from `tasks.md`; tick tasks off as they are done. Run
  Tester and Dev one after another, never in parallel (see *Concurrency* in `.squad/routing.md`).
- **Step 10 — pull request:** the working record (`spec.md`, `plan.md`, `tasks.md`, `log.md`) is posted
  to the feature request issue if one exists (`Closes #<n>` in the PR), otherwise as the first comment on
  the PR right after opening it; the folder is removed before the PR as in `squad-issue`. Behavior that
  must stay documented belongs in `README.md`, `docs/ARCHITECTURE.md` or a decision record, not in
  `spec.md`.
