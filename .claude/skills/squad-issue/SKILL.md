---
name: squad-issue
description: Use when the user asks to fix a bug reported in a specific GitHub issue of this repository — not for an issue the user calls a feature or that carries a feature/enhancement label, which goes to squad-spec (.squad/routing.md, Bug or feature). Runs the squad pipeline — Lead plans and picks a tier, the Devil's Advocate challenges the plan, Security reviews the plan when a security area is touched and the diff of security-tier changes, Tester writes failing tests first, Dev implements until the Coverage gate passes, each member formats its files, Code Officer clears analyzer findings, Reviewer reviews, the PR is approved by checklist with the Lead deciding what is open — and opens a PR referencing the issue.
---

# Squad Issue

Fix a bug reported in a GitHub issue with the squad defined in `.squad/`. A feature runs through
`squad-spec` instead (`.squad/routing.md`, *Bug or feature*). You are the **orchestrator**: you launch the
members as subagents, pass their outputs on (they cannot talk to each other), enforce the tiers and loop
limits of [`.squad/routing.md`](../../../.squad/routing.md) — the rules this procedure points to — and perform
every Git and GitHub action yourself, including follow-up issues the Lead decides on.

- **You never do a member's work.** Production code, tests and the documentation the plan assigns go to their
  owner (production code → `squad-dev`, tests → `squad-tester`, analyzer-only edits → `squad-code-officer`),
  whatever your own checks find. Besides read-only checks you write only:
  - `log.md`, through `squad-log.py` (step 1), and `tasks.md` check marks (features);
  - in `plan.md` (for a feature also `spec.md`): the *Challenge notes* and *Security notes* you append
    verbatim (*Plan challenge*), a tier raise (*Tiers*) and an area added to *Security areas touched*
    (*Unlisted security areas*);
  - the Lead's exact edits on tiers `docs` and `patch` (*Tiers*), a fix marked `verbatim` (*Verbatim
    documentation fixes*), *Format* on the files of the change (step 7), merge commits (*Merge conflicts*),
    and the record status of step 9 (*PR approval*).
- **Hand each member only what it needs:** the plan (for a feature: its task rows and `spec.md`), the test
  names, the base ref, the code SHA and the gate output for the head. Not the log, not the issue thread (the
  Lead and the Devil's Advocate read the issue themselves), not earlier reports in full — one line on what the
  previous member handed back is enough.
- **Continue members** for their next round as *Continuing a member* says; keep the agent id of each member's
  last launch.
- **Scope:** a product PR never touches the squad or the instruction files (*Scope of a product PR*);
  `scope-check.py` enforces it, and squad lessons are filed in step 12.
- **One build at a time:** launch the members that build or test one after another (*Concurrency*).
- **Commits:** commit and push as *Commits* and *The stop hook* say; never commit to `main`. Stage with plain
  `git add -A`: ignored paths are skipped anyway, and an exclusion pathspec for an ignored path makes
  `git add` fail and stage nothing.
- **GitHub:** write through the GitHub MCP tools (`mcp__github__*`: issues, comments, labels, pull requests);
  read as `.squad/team.md` (*Reading GitHub*) says.
- **Pull request:** invoking this skill is the user's approval for opening the PR in step 10, once step 9
  approved it (tiers `docs` and `patch`: once the latest review round is clean). It opens as a draft and is
  marked ready in step 11 (*Draft until green*).
- **Product Manager:** the user is contacted only when the Lead returns `RESULT: ESCALATE` (relay the
  question verbatim with its options and wait) or to confirm a public issue comment on `RESULT: NO CHANGE`.

## Steps

1. **Intake.** Read the issue in full, including comments and labels, and decide bug or feature first
   (*Bug or feature*): a feature switches to `squad-spec` now, before any branch or work folder exists. Note
   the reported environment (versions, image tag or release, host OS, configuration). A closed issue: stop
   and report that. Run `git fetch origin main --tags`, then start from a clean working tree on a new branch
   off the latest `main`, e.g. `fix-issue-<number>-<short-slug>` (or the branch the session prescribes).
   Create `specs/issue-<number>/log.md` from `specs/_template/log.md`, commit and push it.

   **Logging.** Only you write `log.md`, one row per step and per launch, always with
   `python3 .squad/tools/squad-log.py issue-<number> "<step>" "<member>" "<result>"` — never by hand.
   - Write a launch's row as soon as its report is in, with `--agent squad-<role>` alone (usage pending), a continued one too (`--continued` only on the amend). When
     its task notification arrives, add the usage with `--amend-last "<member>" --agent squad-<role> --tokens …
     --tool-uses … --seconds …` (`--step "<step>"` for a row that is no longer the member's last).
   - **Every number is copied from the usage block of a task notification you have in front of you** —
     never an estimate, a figure from memory or the member's hand-back. A notification whose summary says the
     agent is waiting on its own background work is interim: amend with the numbers of the final notification
     of the same task id (`--replace-last`, with `--step` if the row is no longer the member's last, if the interim
     ones are already in). A repeated hand-back message is ignored; only the latest notification of a task id
     counts. A notification that never comes is marked `--unreported`; a wrong trailer is corrected with
     `--replace-last`, never with a second row.
   - A continued launch gets `--continued` (or `--continued-from "<step cell of the fresh launch>"` when that
     agent is not the member's last fresh launch) on the call that carries its numbers — the amend of its pending
     row — with the notification's numbers unchanged (only the tokens are cumulative). Name a fix, delta or revise round as such in its step
     cell; a continuation at another step than its fresh launch draws a warning (a new mode is a fresh launch).
   - Rows of the Orchestrator, the Product Manager and the User take no metric option.
   - After step 10 every call — row, amend, summary — needs `--stdin` (step 10); without it, it fails.
   - Everything else, and what each refusal means: `python3 .squad/tools/squad-log.py --help`.

   After every member's report, commit and push the files it left in the work folder.
2. **Plan.** Launch `squad-lead` in mode `plan` with the issue text and the work folder. It returns one of:
   - `RESULT: DONE` for tier **`docs`** or **`patch`**: record the result as the first plan row of `log.md`
     and run that tier's row of *Tiers* — apply the edits yourself; for `patch` launch `squad-tester` in mode
     `coverage` once when the Lead named a test to add, run the rows of *Gates of a patch* the diff needs and
     log which applied; `scope-check.py --tier docs` or `--tier patch` — then one review round (step 8) and
     step 10.
   - `RESULT: DONE` with `plan.md`: run `python3 .squad/tools/plan-check.py issue-<number>` before anyone else
     reads it and after every Lead write; findings go back as *Loop limits* (Plan check) says. Then the steps
     the tier requires.
   - `RESULT: NO CHANGE` — as *Outcome "no change"*, and stop.
   - `RESULT: ESCALATE` — ask the user, then relaunch the Lead with the answer.

   **Plan challenge.** Launch `squad-devils-advocate` in mode `plan` with the issue text and the work folder
   when *Plan challenge* calls for it, together with step 3 on the same plan commit when that runs; otherwise
   log "challenge skipped: no options". Route its verdict as *Plan challenge* says: append minor objections
   and Security's non-blocking findings verbatim; on a major objection, or a Security note that leaves the
   plan a choice, launch `squad-lead` in mode `revise` with all objections (and Security's `CHANGES_REQUIRED`
   findings, if any). After a raised tier, continue with that tier's steps; `NO CHANGE` and `ESCALATE` as
   above. Log the verdict (also a clean `NO OBJECTIONS`) and the Lead's answer.
3. **Plan security review** whenever *Security areas touched* is not "none" (*Tiers*). Launch
   `squad-security` in mode `plan`, in parallel with the plan challenge. On `CHANGES_REQUIRED`, one
   `squad-lead` revision (with the challenge's objections), then `squad-security` once more on the revision; a
   revision made only for a major objection is confirmed the same way. After the 2nd rejection, `squad-lead`
   in mode `decide` (*Loop limits*, Plan ↔ Security).
4. **Skeleton** (only if the plan adds or changes API). Launch `squad-dev` in mode `skeleton`: the planned
   signatures and the existing test call sites the plan assigns to the Dev. A plan that declares the change
   free of production and test code skips steps 4 and 5 (*Changes without production or test code*): log the
   skipped steps and run the plan's *Verification without tests* instead.
5. **Tests first** (not for `trivial`). Launch `squad-tester` in mode `tests-first`. Confirm yourself that the
   new tests compile and fail on the current code (unless the Tester justified why one cannot); note the
   tests it hands back as **deferred**. A fix without a reproducing test is acceptable only when the bug
   genuinely needs a live external system — then the PR says so.
6. **Implement and cover.** For a change without production or test code this is the Dev's edits alone: no
   `squad-tester`, no *Test with coverage*, no *Coverage gate*. Otherwise launch `squad-dev` in mode `implement`
   — a fresh launch, never a continuation of its `skeleton` agent — with the plan and the test names. If the
   Dev disputes a test, launch `squad-lead` in mode `decide`; the Tester changes a test only if the Lead says
   so. Run *Test with coverage* and the *Coverage gate* yourself. Launch `squad-tester` in mode `coverage`,
   with the reason, only when the gate fails, the Dev reports uncovered new lines, a resource bound of the
   plan has no test yet, a deferred test is open (also when the gate passes) or a reviewer asks for a test;
   otherwise log why the pass is skipped. Repeat until the gate passes. Before step 7, `grep` the test files
   for every test the plan names; a name without a match goes to `squad-tester` in mode `coverage`. Lines
   reported as not unit-testable go to `squad-lead` in mode `decide`.
7. **Code check.** `git fetch origin main` and merge `main` first when it moved (*Merge conflicts*). Run the gates of step 7 (*Pipeline*;
   for a change without code and for `patch` their sections) with the commands from `.squad/stack.md`,
   once they pass, commit, then run the *Sonar gate*; keep the output for step 8. When *Format check* fails, run *Format*,
   `git restore -- <file>` every file it changed outside the change, and check again. Launch
   `squad-code-officer` with the base ref only for analyzer diagnostics, Sonar findings or style findings that
   need reading; structural items it hands back go to `squad-dev` (or `squad-tester`), a scope finding to its
   owner, a file it formatted outside the change you restore. Run the gates again until they pass.
8. **Review.** Launch `squad-reviewer` (round 1, full) and — on tier `security` — `squad-security` in mode
   `diff`, in parallel (on `patch/security` only `squad-security`, *Tiers*), with the base ref, the exact code
   SHA, the work folder (tiers `docs` and `patch`: the first `log.md` row) and the gate output of step 7; tell
   them that later commits may only touch `specs/`, and avoid commits while the round runs (*The stop hook*). A
   report for another code head is a stale round. Then:
   - a tier raise: *Tiers*; an *unlisted security area*: *Unlisted security areas*; a fix marked `verbatim`:
     *Verbatim documentation fixes*;
   - blocking findings → their owner (`squad-dev`, `squad-tester`) → steps 6 and 7 again (tiers `docs` and
     `patch`: you fix them and rerun the checks of step 2) → **a delta round, mandatory**, continuing the
     reviewer; at most 2 fix rounds, then `squad-lead` in mode `decide` (*Loop limits*);
   - non-blocking findings: *Non-blocking findings*, collected for step 9.

   A reviewer or `squad-security` that stops on an API error (`529 Overloaded`; `429`: once the limit has reset) is relaunched
   unchanged, at most twice; after that report a blocker to the user. A safeguard stop (`invalid_request`, category
   `cyber`) is not relaunched: escalate to the Product Manager at once (*Loop limits*, Safeguard stop).
9. **PR approval.** Run *PR approval* (`.squad/routing.md`): the checklist in full, then the record status
   and `decision-check.py`. Launch `squad-lead` in mode `approve-pr` as a fresh launch (never a continuation
   of its plan agent) — with the base ref, the gate output, the review outcome and the open points (after the
   PR also the finished check-run list) — only for the open decisions it lists; otherwise log "approved by
   checklist".
10. **Pull request.** Post `plan.md` (none on tiers `docs` and `patch`) and `log.md` as one comment on the
    issue, headed "Squad working record", each inside a collapsed `<details>` block — or, for a `plan.md` too
    long to re-type, a permalink to the last commit that contains it
    (`https://github.com/<owner>/<repo>/blob/<sha>/specs/issue-<number>/plan.md`) plus a summary of tier,
    criteria, decisions and challenge outcome. Then `git rm -r specs/issue-<number>/`, confirm with
    `python3 .squad/tools/scope-check.py --no-specs`, commit ("Remove squad working record") and push. Later
    log rows go into that comment: pipe its log table into `squad-log.py … --stdin` and paste the log it prints
    back (nothing is restored into the repository). Open the PR as `create-pr` step 8 says, without its review loop; a PR the
    session already has for the branch (`list_pull_requests` with `head: "<owner>:<branch>"`) gets its title
    and body updated instead (`update_pull_request`, `draft: true`). The body describes the bug, the fix and
    the reproducing test (or that the fix is not verifiable without a real external system), `Closes
    #<number>` under Issues and the decision records; Next Steps lists **only linked GitHub issues** (create
    them now) or "None".
11. **After the PR.** Subscribe to the PR's activity right away (`subscribe_pr_activity`; without it, check CI
    and the code analysis yourself before finishing) — a session that ends with an unwatched PR has not
    completed this step. Stay with the PR until *Draft until green* holds, then mark it ready. Route:
    - a merge conflict → *Merge conflicts*, before anything else;
    - code-analysis findings (`sonar-check.py pr <number>` lists them) → `squad-code-officer` (structural
      ones → `squad-dev`);
    - failing build or tests → `squad-dev` (test defects → `squad-tester`);
    - review comments (human, automated, `review-pr`) → `squad-dev`, worked in this PR, blocking or not.

    Each fix goes through steps 7–8 again (delta review; `scope-check.py --no-specs` from now on), continuing
    the member and the last round's reviewer, at most 2 fix rounds per failure before the Lead decides, and
    gets the re-approval of step 9. A red CI on a criterion the plan verifies only in CI is a normal outcome
    here (*Criteria verified only in CI*). The work folder is gone: give the Reviewer, Security and the Lead
    the plan (tiers `docs` and `patch`: the first log row; for a feature also `spec.md`, `tasks.md`) from the
    working-record comment or `git show
    <commit-before-removal>:specs/<folder>/<file>`, and edit that comment in one batch. Never skip, disable or
    weaken a test to get green.
12. **Wrap-up (mandatory).** Collect what this run taught about the squad itself — an unclear or
    contradictory rule, a misbehaving tool, an agent that could not be launched, a step you had to improvise —
    each with the role it concerns and a concrete proposal, file them as *Squad lessons* says (create the
    labels if missing) and link them from the working-record comment; with nothing to learn, add a `no
    lessons` row instead. Run `python3 .squad/tools/squad-log.py issue-<number> --summary` (`--stdin` with the comment's
    log table); exit code 3 names launch rows still without usage — amend them from their
    notifications or mark them `--unreported`, and run it again. Append the tables exactly as printed (a run
    without them is not wrapped up) to the comment under "Squad run metrics", and report the branch, the PR
    URL, the tier, the `squad` issues (or "no lessons"), the metrics tables and any escalation or Lead
    decision to the user.

## Notes

- The PR documents the change, not how it was produced (`AGENTS.md`, *Related skills*).
- A member that called a session-management tool is a step-12 lesson.
- Text you write or copy verbatim (notes, verbatim fixes, a plan table) keeps its escapes: `.squad/team.md`,
  *Escapes in files*.
- Prefer non-interactive commands only. If push or PR creation fails, stop and report it.
- Never close the issue manually; `Closes #<number>` closes it on merge.
