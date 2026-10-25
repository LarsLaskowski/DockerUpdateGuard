---
name: squad-spec
description: Use when the user wants to develop a new feature in this repository spec-driven with the squad — from a description, or from a GitHub issue the user calls a feature (implement feature 12) or that carries a feature/enhancement label (.squad/routing.md, Bug or feature). The Lead writes the spec first and the Devil's Advocate challenges it before any plan exists; then the Lead writes plan and tasks against the spec's acceptance criteria and picks a tier, Security reviews the plan when a security area is touched and the diff of security-tier changes, Tester writes failing tests first, Dev implements until the Coverage gate passes, each member formats its files, Code Officer clears analyzer findings, Reviewer reviews, the PR is approved by checklist with the Lead deciding what is open, then a PR is opened.
---

# Squad Spec

Build a feature with the squad defined in `.squad/`, **spec first**: what the feature does for its users is
written, challenged and settled in `spec.md` before anyone plans how to build it. The spec is the only
source of the acceptance criteria; `plan.md` and `tasks.md` refer to them by id, and
`python3 .squad/tools/plan-check.py <folder>` checks that nothing is lost on the way. You are the
**orchestrator**, with the same role and rules as in the `squad-issue` skill (its introduction) — read it
first. Steps 1–3 below replace its steps 1–3 (step 2 in two parts, 2a *Spec* and 2b *Plan*: use those step
cells in `log.md`); steps 4–12 follow `squad-issue` with the changes listed after them.

## Steps 1–3

1. **Intake.** With a GitHub issue, read it in full, including comments and labels, and decide bug or feature
   (`.squad/routing.md`, *Bug or feature*) — the user calling it a feature or invoking this skill settles
   it; a bug fix switches to `squad-issue` now. A closed issue: stop and report that. A run that waits for
   an outside author's agreement continues on its work branch (`.squad/orchestration.md`, *Outside authors*). Run
   `git fetch origin main --tags`, create the branch off the latest `main` (or the branch the session
   prescribes) and the work folder `specs/feature-<short-slug>/` (from an issue
   `specs/feature-<number>-<short-slug>/`) with `log.md` from `specs/_template/log.md`; commit and push. Log
   as `squad-issue` step 1 says.
2a. **Spec.** Launch `squad-lead` in mode `spec` with the issue text (or the user's description) and the work
   folder. It writes `spec.md` from `specs/_template/spec.md` and returns `RESULT: DONE`, `RESULT: NO CHANGE`
   (the feature exists already or contradicts an accepted decision: with an issue, as *Outcome "no change"*;
   otherwise report it to the user) or `RESULT: ESCALATE` (relay the question verbatim, then continue the Lead
   with the answer). Run `python3 .squad/tools/plan-check.py <folder> --spec` after every Lead write; findings
   as *Loop limits* (Plan check) says. Its size warning goes back to the Lead once (mode `revise`): it shortens
   the spec, or keeps it and gives the reason in its result, which you log.

   **Spec challenge.** Launch `squad-devils-advocate` in mode `spec` with the issue text and the work folder,
   for every feature. Minor objections only: append them verbatim to `spec.md` as a *Challenge notes*
   section; the Lead answers them while it plans. A major objection: continue `squad-lead` in mode `revise`
   on the spec with all objections; it answers each in the spec's *Challenge* section and may narrow the
   scope, escalate or return `RESULT: NO CHANGE`. One round, no veto (*Loop limits*). An issue by an outside
   author: its spec is agreed on the issue now (*Outside authors*).
2b. **Plan.** Continue the Lead's last launch in mode `plan` with the work folder (*Continuing a member*): it
   closes the spec (answers its *Challenge notes*, sets its status) and writes `plan.md` and `tasks.md`. A
   feature is never `docs` or `patch`. Run `python3 .squad/tools/plan-check.py <folder>` after every Lead
   write, before anyone else reads the plan; findings as *Loop limits* says. Then the **plan challenge** with
   `squad-devils-advocate` in mode `plan` when *Plan challenge* calls for it (for a feature: only when the
   plan weighs options), routed as `squad-issue` step 2 says.
3. **Plan security review** whenever *Security areas touched* is not "none": `squad-security` in mode `plan`
   reviews `spec.md` and `plan.md` together, in parallel with the plan challenge, and the loop of
   `squad-issue` step 3 applies. Run `plan-check.py` again after every Lead revision.

## Steps 4–12: as in `squad-issue`, with these changes

- **Steps 4–6** run per task group — the *Group* column of `tasks.md`, in table order; tick tasks off as they
  are done. The Tester's tests first are the tests *Tests per criterion* names for the group's
  criteria. The next task group continues each member's last agent of the same mode, as do the fix and delta
  rounds (*Continuing a member*); log such a launch with `--continued-from "<step cell of that agent's fresh
  launch>"`. The documentation rows are the Dev's, done once the last group's *Coverage gate* passes (`squad-issue`
  step 6; *Documentation* in `squad-dev.md`).
- **Code-free changes:** as *Changes without production or test code*; *Verification without tests* then
  covers each criterion of the spec.
- **Decision records:** features usually involve real design choices, so expect a record in
  `docs/decisions/` (extend an existing unreleased record on the topic rather than adding one). The behavior
  belongs in the area documents the spec names, user-facing configuration in `README.md`, a changed flow or
  guarantee in `docs/ARCHITECTURE.md` — not in `spec.md`, which leaves with the working record.
- **Step 8:** the Reviewer checks the diff against the spec's scenarios, edge cases and criteria, and that the
  area documents now say what the spec's *Area documents* section promised.
- **Step 10:** the working record (`spec.md`, `plan.md`, `tasks.md`, `log.md`) is posted to the feature
  request issue if one exists (`Closes #<n>` in the PR), otherwise as the first comment on the PR right after
  opening it; every log row and amend after it goes through `squad-log.py … --stdin` on that comment's log
  table.
