---
name: squad-spec
description: Use when the user wants to develop a new feature in this repository spec-driven with the squad — from a description, or from a GitHub issue the user calls a feature (implement feature 12) or that carries a feature/enhancement label (.squad/routing.md, Bug or feature). Lead writes spec, plan and tasks and picks a tier, the Devil's Advocate challenges them, Security reviews plan and diff of security-tier changes, Tester writes failing tests first, Dev implements until the Coverage gate passes, each member formats its files, Code Officer clears analyzer findings, Reviewer reviews, the PR is approved by checklist with the Lead deciding what is open, then a PR is opened.
---

# Squad Spec

Build a feature with the squad defined in `.squad/`. Tiers, pipeline, loop limits, escalation rules,
commit/push rules and the orchestrator role are identical to the `squad-issue` skill — follow its steps
1–12 with these changes:

- **Step 1 — intake, work folder and branch:** with a GitHub issue, read it in full, including comments
  and labels, and decide bug or feature as `.squad/routing.md`, *Bug or feature*, says — the user calling it
  a feature or invoking this skill settles it; a bug fix switches to `squad-issue` now. A closed issue: stop
  and report that. Work folder `specs/feature-<short-slug>/` (from an issue
  `specs/feature-<number>-<short-slug>/`) with `log.md` from `specs/_template/log.md`; branch named like the
  folder off the latest `main` (or the branch the session prescribes); run `git fetch origin main --tags` first (`scope-check.py` compares against the local `origin/main`; the
  release tags tell `decision-check.py` and the Lead which decision records are released).
  Log rows exactly as `squad-issue` step 1 says: a launch row is written without any metric option — no
  `--agent` either, which `squad-log.py` rejects without numbers — and completed with `--amend-last` once
  the task notification is in; never with placeholder numbers.
- **Step 2 — plan:** `squad-lead` in mode `plan`, launched with the issue text (or the user's description)
  and the work folder, writes `spec.md` (behavior, acceptance criteria, out of
  scope), `plan.md` and `tasks.md`, launched with the agent file's model and effort like every other Lead
  launch (log it with `--agent squad-lead`): the plan challenge and, on `security`, the Security plan review
  catch what a plan misses, and a plan on `high` did not spare them (`.squad/team.md`, *Members*). A feature is never `docs` or `patch` and rarely `trivial`. It is more likely than a bug
  fix to need a product decision — the Lead escalates whenever the request does not settle user-visible
  behavior. `RESULT: NO CHANGE` means the feature already exists or contradicts an accepted decision; with an
  issue, handle it as `squad-issue` step 2 says (proposed issue comment, posted only after the user's
  confirmation), otherwise report it to the user. The plan challenge covers `spec.md`, `plan.md` and
  `tasks.md` together.
- **Decision records:** features usually involve real design choices, so expect a record in
  `docs/decisions/` (extend an existing unreleased record on the topic rather than adding a new one); the behavior
  the feature adds belongs in the area document (`docs/areas/`) named in the plan, not in the record; the Lead also updates `docs/ARCHITECTURE.md` when the feature changes a flow or
  guarantee.
- **Steps 4–6 and code-free changes:** a plan may declare steps 4, 5 and the *Coverage gate* not applicable
  as described in *Changes without production or test code* in `.squad/routing.md`; its *Verification without
  tests* section then says where each acceptance criterion is verified, and you log the skipped steps.
- **Step 3** reviews `spec.md` and `plan.md` together.
- **Steps 4–6** run per task or group of tasks from `tasks.md`; tick tasks off as they are done. Run
  Tester and Dev one after another, never in parallel (see *Concurrency* in `.squad/routing.md`). Continue
  a member for the fix and delta rounds of its task group (*Continue a member* in `squad-issue`); the next
  task group starts with a fresh launch, so a member's context does not grow over the whole feature. A fix
  round for an earlier task group whose agent is no longer the member's last fresh launch is logged with
  `--continued-from "<step cell of that group's launch>"`, so its tokens are subtracted from the right agent.
- **Step 10 — pull request:** the working record (`spec.md`, `plan.md`, `tasks.md`, `log.md`) is posted
  to the feature request issue if one exists (`Closes #<n>` in the PR), otherwise as the first comment on
  the PR right after opening it; the folder is removed before the PR as in `squad-issue`. The PR opens as a
  draft and is marked ready for review in step 11 as in `squad-issue` (*Draft until green*). Behavior that
  must stay documented belongs in `README.md`, `docs/ARCHITECTURE.md` or a decision record, not in
  `spec.md`.
