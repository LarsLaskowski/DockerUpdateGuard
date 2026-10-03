---
name: create-pr
description: Use when the user asks to open/create a pull request for changes on this branch. Runs local verification (format, build, test), reviews the change with the squad-reviewer subagent, then pushes the branch and opens a PR following this repo's pull request template.
---

# Create PR

Use this skill to prepare and open a pull request for changes made in this
repository.

All user-facing output you create — branch name, commit message, PR title and
body — is written in **English**, regardless of the language the user wrote
in.

## Steps

1. **Verify the working tree**: run `git status --short --branch` and
   `git diff` to confirm what will be included, and confirm the `origin`
   remote exists. Do not include unrelated or uncommitted work the user
   didn't ask for. If there are no relevant local changes and no unpushed
   commits, stop and say so plainly.
2. **Create a branch** if you are still on `main` (or another base branch) —
   never commit directly to it. Derive a short kebab-case name from the work
   (e.g. `add-season-aggregates`, `fix-path-mapping`), or use the name the
   user supplied. If you are already on a feature branch, stay on it.
3. **Verify tests exist** for what the diff changes. Per
   [`UNIT_TESTS.md`](../../../docs/UNIT_TESTS.md), unit tests are mandatory for
   new/changed behavior, not optional — if the diff adds or changes logic
   without a corresponding test, write one before proceeding (following
   `UNIT_TESTS.md`'s naming, test-double and assert-message conventions)
   rather than opening the PR without coverage.
4. **Run local verification** before pushing, from the repository root,
   with the commands from [`.squad/stack.md`](../../../.squad/stack.md):
   - *Restore* (if the stack has one) and *Format*
   - *Build* — it must finish without errors and without the warnings
     `stack.md` lists as forbidden
   - *Analyzer gate* — no analyzer diagnostic of any severity in a changed
     file; treat each as a failure
   - *Test with coverage* and *Coverage gate* — at least 80 % line coverage
     on new/changed production code and overall
   - `python3 .squad/tools/config-check.py` when the diff touches `.claude/`,
     `.github/skills/`, `.agents/skills/` or an instruction file — Claude Code
     silently drops an agent or skill whose front matter does not parse, and
     the skill copies and instruction files must match
   Fix any failures before proceeding — do not open a PR with failing checks,
   unformatted code or outstanding analyzer diagnostics. This step is the gate
   before the PR; CI is not meant to find anything here.
5. **Commit** with a subject line of at most 80 characters, not written in
   the first person and without a trailing period, and a body of 3–5
   sentences explaining *what* changed and *why* if it is not obvious from
   the diff. Stage only the files that belong to this task.
6. **Run the internal review loop** (see below) and resolve what it finds.
   This happens *before* the push, so the pull request opens on a reviewed
   change instead of collecting review rounds afterwards.
7. **Push** the branch: `git push -u origin <branch-name>`.
8. **Open the PR** using the repository's template at
   `.github/pull_request_template.md`:
   - base branch `main`, unless the user explicitly requests a different base
   - title `[area] Description` per
     [`CONTRIBUTING.md`](../../../docs/CONTRIBUTING.md) — area is one of the
     areas CONTRIBUTING lists, capitalized, no period at the end, no issue number
     in the description, under 70 characters
   - fill in Description, Issues (link the related issue if one exists, with
     `Closes #<number>`), Reviewer Notes and Test Plan, and check off the
     checklist items that are actually true (don't check items you haven't
     verified) — including the unit-test, formatting, analyzer, coverage,
     documentation and dependency items, not just the general ones
   - wrap the body in a HEREDOC so the formatting survives
9. Report the branch name and the PR URL back to the user.

## What the pull request says — and what it doesn't

The pull request documents **the change**, not how the change was produced.

- Reviewer Notes tell a reviewer where to look and why the approach was
  chosen: the components touched, any guarantee from
  [`ARCHITECTURE.md`](../../../docs/ARCHITECTURE.md) the change comes near,
  and a smoke test if one is worth running.
- Do **not** mention the internal review loop anywhere in the PR — not how
  many passes ran, not what they found, not which commits resolved their
  findings. That loop is a working step inside this session, not part of the
  change's history, and a reader of the PR has no use for it.
- Describe the finished state of the change, not the sequence of corrections
  that got there.

## The internal review loop

The review happens here, in this session, against the local branch — not as
a round trip through pull request comments. Each pass is delegated to the
`squad-reviewer` subagent, which runs on Opus with a fresh
context and the repository's full review checklist. That checklist lives in
`.claude/agents/squad-reviewer.md`; an agent without subagent
support follows the same file inline, so the review is the same either way.

1. **Pass 1** — launch `squad-reviewer` (subagent_type
   `squad-reviewer`, model `opus`). Tell it the base ref, the
   head to review, and that this is round 1.
2. **Act on the verdict**:
   - `APPROVE` → done, go push.
   - Blocking findings → fix each one minimally and commit. Do not widen the
     change beyond what the finding requires.
   - Non-blocking findings → **do not open another round for them**. Fix one
     if it is trivial and already in scope. Otherwise open a GitHub issue for
     it **now**, in this session, and link that issue under the PR's Next
     Steps — a note that only exists in this conversation is lost the moment
     the session ends, so it is not a way to carry a finding forward.
3. **Pass n+1** — launch a fresh `squad-reviewer` and give it
   the round number, the previous round's findings, and the commits that
   fixed them. It reviews the delta only, per its own instructions.
4. **Stop** at the first pass that reports no blocking findings. Cap the loop
   at **three passes**: if blocking findings remain after the third, stop and
   report the open findings to the user rather than continuing to iterate —
   at that point the change needs a decision, not another round.

Two rules keep this loop finite, and they are the point of the whole
arrangement:

- **Later passes review the delta, never the whole diff again.** A fresh full
  review of unchanged code always finds something new.
- **Only blocking findings start a new pass.** Non-blocking findings are
  resolved or turned into an issue, not iterated on.

## Findings that arrive after the push

If a review lands on the pull request after it is open — from a human
reviewer, from an automated code review, or from the `review-pr` skill — work
those findings in this session, in this pull request. Do not defer a posted
finding to "the next change that touches this code": there is no such change
on the horizon, and the session holding the context needed to act on it will
not exist later. `review-pr` describes how to answer and close out each
posted comment.

## Notes

- Prefer non-interactive commands only.
- Do not amend existing commits unless the user explicitly asks.
- If a PR already exists for the branch, push the new commits and report the
  existing URL instead of opening a duplicate.
- If push or PR creation fails, stop and report the failure clearly instead
  of continuing as if it succeeded.
- Never force-push over another contributor's commits without explicit
  confirmation.
- If the change touches a guarantee, security area or integration-surface
  entry in [`.squad/project.md`](../../../.squad/project.md), a configuration
  key, or the Docker/CI setup, make
  sure the corresponding documentation — [`README.md`](../../../README.md),
  [`ARCHITECTURE.md`](../../../docs/ARCHITECTURE.md),
  [`SECURITY.md`](../../../SECURITY.md) — was updated in the same PR (see the
  template checklist). See
  [`CONTRIBUTING.md`](../../../docs/CONTRIBUTING.md) for the full workflow and
  stability policy this skill follows.