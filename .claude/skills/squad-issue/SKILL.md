---
name: squad-issue
description: Use when the user asks to fix a specific GitHub issue in this repository. Runs the squad pipeline — Lead plans and picks a tier, the Devil's Advocate challenges the plan, Security reviews security-relevant plans, Tester writes failing tests first, Dev implements to 80% coverage, Code Officer clears format and analyzer findings, Reviewer (+ Security) review, Lead approves — and opens a PR referencing the issue.
---

# Squad Issue

Fix a reported GitHub issue with the squad defined in `.squad/`. You are the **orchestrator**: you launch
the members as subagents, pass their outputs on (they cannot talk to each other), enforce the tiers and
loop limits from [`.squad/routing.md`](../../../.squad/routing.md), and perform every Git and GitHub
action yourself — including follow-up issues the Lead decides on.

- **You never do a member's work.** You do not edit production code, tests or documentation the plan
  assigns to the Dev, do not run the formatter, and do not fix analyzer findings — not even a one-line `sed`.
  Whatever a check of yours finds goes to its owner (production code → `squad-dev`, tests →
  `squad-tester`, formatting/analyzer-only edits → `squad-code-officer`) and through the steps that follow
  it. Besides read-only checks (`--check`, the analyzer and coverage scripts, tests) you only write the
  squad's bookkeeping: `log.md` and `tasks.md` check marks (features). Never production code, tests or `docs/`.
- **The squad does not change itself in a product PR.** An issue or feature PR never touches `.squad/`
  (including `history.md` and `decisions.md`), `.claude/`, `.github/skills/`, `.agents/skills/`, `CLAUDE.md`, `AGENTS.md`
  or `.github/copilot-instructions.md`. Lessons about the squad are filed in step 12 as `.squad/routing.md`,
  *Squad lessons*, says — template-managed files in the template repository, project knowledge here. If the
  change itself genuinely needs one of those files (e.g. a new build command every contributor must know),
  the Lead escalates instead and the Product Manager decides: `.squad/stack.md` and `.squad/project.md` may
  change in the product PR (*Scope of a product PR*); a template-managed file is changed in the template
  repository; any other squad or instruction file (e.g. a project block) goes into a separate
  squad-maintenance PR.
- **Working records stay off `main`.** `specs/<folder>/` exists only on the work branch, so it survives a
  crashed session. Before the PR (step 10) its content is posted as a comment and the folder is removed;
  the lasting reasoning lives in `docs/decisions/`.
- **One build at a time.** Never run two members that build or test (`squad-dev`, `squad-tester`,
  `squad-code-officer`, the reviewers' verification runs) in parallel: they share `bin/` and `obj/` and
  break each other (`.squad/routing.md`). Launch them one after another; only `squad-reviewer` and
  `squad-security` may run together in step 8, because both are read-only and the reviewer's own build
  happens in a scratch copy.
- **Commits and pushes** to the work branch are always allowed (`CLAUDE.md`, golden rules): commit and
  push `specs/<folder>/log.md` right after intake (step 1), so a stop hook or a crashed session finds no
  untracked files, and after every further completed step. PRs are merged with *Squash and merge*, so only
  the PR title and description reach `main`; intermediate commit messages may name the step, but never
  contain secrets. Interim work-in-progress commits — e.g. demanded by a stop hook while a member is still
  working — are fine for the same reason. Stage with plain `git add -A`: ignored paths such as `TestResults/`
  are skipped anyway, and an exclusion pathspec for an ignored path makes `git add` fail and stage
  nothing. Never commit to `main`.
- **GitHub access:** use the GitHub MCP tools (`mcp__github__*`) for issues, comments, labels and pull
  requests. In these sessions the `gh` CLI only works as `gh api repos/<owner>/<repo>/...`; `gh issue`,
  `gh pr` and `gh search` fail (GraphQL is blocked and search is not scoped to the repository).
- **Pull request:** invoking this skill is the user's approval for opening the PR in step 10, once the
  Lead has approved it (tier `docs`: once the latest review round is clean).
- **Product Manager:** the user is only contacted when the Lead returns `RESULT: ESCALATE` (relay the
  question verbatim with its options and wait) or for confirming a public issue comment on
  `RESULT: NO CHANGE`.
- Everything that ends up in the repository or on GitHub is written in **English**.

## Steps

1. **Intake.** Read the issue in full, including comments; note the reported environment (versions of the
   software involved, image tag or release, host OS, configuration). If it is closed, stop and report that. Start
   from a clean working tree on a new branch off the latest `main`, e.g.
   `fix-issue-<number>-<short-slug>` (or the branch the session prescribes). Create
   `specs/issue-<number>/log.md` from `specs/_template/log.md`, commit and push it; append one table row
   per step. Only you
   (the orchestrator) write `log.md`, one row per append, each row ending in the file's line ending — subagents report and
   you record, so rows never merge or end up with mixed line endings.
2. **Plan.** Launch `squad-lead` in mode `plan` with the issue text and the work folder. It returns one of:
   - `RESULT: DONE` — for tier **`docs`** (see its definition in `.squad/routing.md`), a short result (tier, the files and lines to change, acceptance
     criteria) that you record as the first plan row in `log.md`, then continue with step 6 (Dev), the
     read-only check from the `docs` row in `.squad/routing.md`, one review round in step 8, and step 10
     directly — no Security, skeleton, tests, coverage, Code Officer or Lead approval. Otherwise:
     `plan.md` with the **tier** (`trivial` / `standard` / `security`), acceptance
     criteria, the signatures of new or changed API, required documentation updates (`README.md`,
     `docs/`), and `Proposed` decision records. Continue with the steps the tier requires.
   - `RESULT: NO CHANGE` — show the proposed issue comment to the user, post it only after confirmation
     (append the log as a collapsed "Squad working record" block), remove the work folder with a commit
     and push, and stop. No PR; the branch stays as it is, and you tell the user so.
   - `RESULT: ESCALATE` — ask the user, then relaunch the Lead with the answer.

   **Plan challenge** (`standard` and `security` only, once). Launch `squad-devils-advocate` with the issue
   text and the work folder. On `VERDICT: OBJECTIONS …`, launch `squad-lead` in mode `revise` with the
   objections; it answers each one in the plan's *Challenge* section (accepted and the plan revised, or
   rejected with a reason) and may narrow the scope, raise the tier or switch to `RESULT: NO CHANGE`.
   `RESULT: NO CHANGE` and `RESULT: ESCALATE` are handled as above; after a raised tier, continue with that
   tier's steps. There is no second challenge round and no veto. Record the verdict (also a clean
   `NO OBJECTIONS`) and the Lead's answer in `log.md`.
3. **Plan security review** (`security` tier only). Launch `squad-security` in mode `plan`. On
   `CHANGES_REQUIRED`, launch `squad-lead` in mode `revise` and repeat. After the **2nd** rejection launch
   `squad-lead` in mode `decide` (scope down, split into issues, abort, or escalate).
4. **Skeleton** (only if the plan adds or changes API). Launch `squad-dev` in mode `skeleton`: the planned
   signatures built as *Skeleton* in `.squad/stack.md` describes (bodies fail when called), plus the existing
   test call sites the plan assigns to the Dev for an incompatible signature change, so the tests of step 5
   compile.
5. **Tests first** (skipped for `trivial`). Launch `squad-tester` in mode `tests-first`. Confirm yourself
   that the new tests compile and fail on the current code (unless the Tester justified why one cannot).
   A fix without a reproducing test is only acceptable when the bug genuinely needs a live external
   system — then the PR says so.
6. **Implement and cover.** Launch `squad-dev` in mode `implement` with the plan and the test names; it
   also makes the documentation updates the plan lists. If the Dev disputes a test, launch `squad-lead`
   in mode `decide`; the Tester changes a test only if the Lead says so. Then launch `squad-tester` in
   mode `coverage`; repeat Dev/Tester until the *Coverage gate* (after *Test with coverage*, both in
   `.squad/stack.md`) passes (≥ 80 % on new/changed production code and overall). Lines reported as not unit-testable go to
   `squad-lead` in mode `decide`; an accepted gap is recorded in `log.md`.
7. **Code check.** Launch `squad-code-officer` with the base ref — the only member that runs
   the formatter and clears analyzer diagnostics. Then verify yourself, without formatting, with the
   commands from `.squad/stack.md`: *Format check* exits 0, the *Analyzer gate* passes (no diagnostic of
   any severity in a changed file), *Test* is green with the same tests, and the *Coverage gate* still
   passes. Structural items handed back go to `squad-dev` (or
   `squad-tester`), followed by another code check. This is the gate before the PR; CI is not meant to find anything here.
8. **Review.** Launch `squad-reviewer` (round 1, full) and — for `standard` and `security` —
   `squad-security` in mode `diff`, in parallel, against the base ref. Pass both the work folder
   (`specs/<folder>/`) so they check the plan's acceptance criteria and tier (tier `docs`: the first
   `log.md` row, since there is no `plan.md`); either may raise the tier. Tier `docs`: a blocking finding
   goes to `squad-dev`, then the read-only check and a delta round, then step 10. Blocking
   findings → their owner fixes them (`squad-dev` for production code, `squad-tester` for tests) → steps 6
   (coverage) and 7 again → **a new review round on the delta is mandatory** before step 9; never go from
   a blocking finding straight to PR approval. The same holds for a non-blocking finding the Lead decides
   to fix now: any change to production code, tests or `docs/` after a review round needs a delta round. At most **2 fix rounds** after round 1; then `squad-lead`
   in mode `decide`. Non-blocking
   findings: the Lead decides per finding — fix now, or you open a linked GitHub issue now.
9. **PR approval.** Launch `squad-lead` in mode `approve-pr` with the base ref, the build/test/coverage
   output and the review outcome — including the result of the **latest** review round, which must have
   no blocking finding that is not covered by a recorded Lead decision, and must cover every change to
   production code, tests and `docs/` since it ran (only `specs/` bookkeeping and the Lead's own approval edits —
   record status, the index, a link from `docs/ARCHITECTURE.md` — may follow it; a fix for a blocking
   finding always needs a delta round, also in a decision record). `NOT APPROVED` → back to step 6 or 8 (counting against the review loop
   limit) or let the Lead decide/escalate. On `APPROVED`, the decision records are `Accepted` and indexed
   in `docs/decisions/README.md`.
10. **Pull request** (Dev role, performed by you). First move the working record off the branch: post
    `plan.md` (none for tier `docs`) and `log.md` as one comment on the issue (each inside a collapsed `<details>` block, headed
    "Squad working record"), then `git rm -r specs/issue-<number>/`, commit ("Remove squad working
    record"), and push. Later log rows (steps 11–12) are appended by editing that comment. Then open the
    PR from
    [`.github/pull_request_template.md`](../../../.github/pull_request_template.md): title per
    `docs/CONTRIBUTING.md` — `[area] Description`, where `area` is one of the areas
    CONTRIBUTING lists, capitalized — not a lowercase class or file name. It becomes the squash
    commit subject on `main`. Body describing the bug, the fix and the
    reproducing test, `Closes #<number>` under Issues, links to the decision records. Next Steps lists
    **only linked GitHub issues** (create them now) or "None" — never an unlinked "revisit later". Follow the `create-pr` skill's template rules, but
    do **not** run its internal review loop — step 8 replaced it. If the fix is not fully verifiable
    without a real external system, say so.
11. **After the PR.** Subscribe to the PR's activity right after opening it (`subscribe_pr_activity` when
    available; otherwise check the CI and code-analysis (e.g. SonarQube Cloud) results yourself before finishing) — a session
    that ends with an unwatched PR has not completed this step. Stay with the PR until CI is green and the
    code-analysis quality gate (e.g. SonarQube Cloud) passes:
    - code-analysis findings → `squad-code-officer` (structural ones → `squad-dev`);
    - failing build or tests → `squad-dev` (test defects → `squad-tester`);
    - review comments (human, automated, `review-pr`) → `squad-dev`, worked in this PR, blocking or not.

    Each fix goes through steps 7–8 again (delta review), with at most 2 fix rounds per failure before the
    Lead decides. The work folder is gone by now: give the Reviewer, Security and the Lead the plan (tier
    `docs`: the first log row; features:
    also `spec.md` and `tasks.md`) from the "Squad working record" comment, or via
    `git show <commit-before-removal>:specs/<folder>/<file>`, and record each log row by editing that
    comment. Never skip, disable or weaken a test to get green.
12. **Wrap-up (mandatory).** Collect what this run taught about the squad itself (a rule that was
    unclear or contradictory, a tool that misbehaved, an agent that could not be launched, a step that
    had to be improvised), each with the role it concerns and a concrete proposal, and file them as
    `.squad/routing.md`, *Squad lessons*, says: lessons about template-managed files as **one** issue
    labelled `squad` in the template repository named in `.squad/template.json` (attach that repository to the session if
    needed; without access, file it here with the label `squad-upstream`), lessons about project knowledge
    as **one** issue labelled `squad` in this repository (create the labels if missing). Link the issues
    from the working record comment. Do **not** edit `.squad/`, `.claude/` or the instruction files.
    Report the branch, the PR URL, the tier, the `squad` issues (or "no lessons") and any escalation or
    Lead decision to the user. If there is genuinely nothing to learn,
    append a `| <date> | 12 Wrap-up | Orchestrator | no lessons |` row to the working record comment
    instead of opening an issue — the step itself is never skipped.

## What the pull request says — and what it doesn't

The PR title and description document the change, not how it was produced: the bug, the fix and the test
that pins it down. Plan revisions, review rounds and their findings never appear there.
(The working record is the "Squad working record" comment on the issue; the lasting reasoning is in
`docs/decisions/`.)

## Notes

- Prefer non-interactive commands only. If push or PR creation fails, stop and report it.
- Never close the issue manually; `Closes #<number>` closes it on merge.
