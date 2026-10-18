---
name: squad-issue
description: Use when the user asks to fix a bug reported in a specific GitHub issue of this repository — not for an issue the user calls a feature or that carries a feature/enhancement label, which goes to squad-spec (.squad/routing.md, Bug or feature). Runs the squad pipeline — Lead plans and picks a tier, the Devil's Advocate challenges the plan, Security reviews plan and diff of security-tier changes, Tester writes failing tests first, Dev implements until the Coverage gate passes, each member formats its files, Code Officer clears analyzer findings, Reviewer reviews, the PR is approved by checklist with the Lead deciding what is open — and opens a PR referencing the issue.
---

# Squad Issue

Fix a bug reported in a GitHub issue with the squad defined in `.squad/`. A feature — the user calls it
one, or the issue is labelled `feature`/`enhancement` — runs through `squad-spec` instead (`.squad/routing.md`,
*Bug or feature*). You are the **orchestrator**: you launch
the members as subagents, pass their outputs on (they cannot talk to each other), enforce the tiers and
loop limits from [`.squad/routing.md`](../../../.squad/routing.md), and perform every Git and GitHub
action yourself — including follow-up issues the Lead decides on.

- **You never do a member's work.** You do not edit production code, tests or documentation the plan
  assigns to the Dev, and do not fix analyzer findings. Whatever a check of yours finds goes to its owner
  (production code → `squad-dev`, tests → `squad-tester`, analyzer-only edits → `squad-code-officer`) and
  through the steps that follow it. *Format* is a deterministic command, not a member's judgment: you may
  run it on the files of the change when *Format check* fails (step 7). Besides read-only checks (`--check`,
  the gate scripts, tests) you only write the squad's bookkeeping: `log.md` (through
  `python3 .squad/tools/squad-log.py`), `tasks.md` check marks (features) and the *Challenge notes* you
  append to `plan.md` verbatim (step 2). The exceptions are tiers `docs` and `patch`, where you apply the
  Lead's exact edits yourself (`.squad/routing.md`, *Tiers*), and a review finding, blocking or not, whose
  fix a reviewer states verbatim and that touches only documentation, a decision record, an area document,
  `README.md` or a configuration example (step 8, *Verbatim documentation fixes*).
- **Hand each member only what it needs.** The plan (for a feature: its task rows), the test names, the
  base ref and the gate output for the head. Not the log, not the issue thread (the Lead and the Devil's
  Advocate read the issue themselves), not earlier reports in full — one line on what the previous member
  handed back is enough. Every fresh launch starts with an empty context, so what you pass is what it
  costs.
- **Continue a member for its next round.** For a fix round or a delta round of a member already launched
  in this run (Dev, Tester, Reviewer, Security), continue that agent — `SendMessage` to its agent id, or
  the Agent tool's resume — with the findings and the new head SHA, instead of a fresh launch that reloads
  40 k to 50 k tokens of instructions and code it already knows (`.squad/routing.md`, *Continuing a
  member*). Keep the agent id of each member's last launch. Launch fresh for the first launch of a mode,
  after a session restart, and when the previous launch ended on an API error. Log a continuation as a
  launch of that member with its notification's usage and `--continued`: the usage block reports the agent's
  tokens since its first launch (tool uses and duration for the round alone), the script writes this round's
  share and marks the trailer. When the agent you continue is not the member's last fresh launch (a member
  ran several task groups), use `--continued-from "<step cell of that launch>"` instead — the step cell of the
  agent's fresh launch, not of a later continued row of it. A continued launch is a new row, so it takes no
  `--step`: that option only selects the row `--amend-last` or `--replace-last` changes, and `squad-log.py`
  refuses it on a new row. After five
  continuations of one agent or once its reported tokens pass 300,000, launch the member fresh with a short
  hand-over (*Continuing a member*). A continued member keeps its role's limits (`.squad/team.md`).
- **The squad does not change itself in a product PR.** An issue or feature PR never touches `.squad/`
  (`team.md`, `routing.md`, tools), `.claude/`, `AGENTS.md`, `CLAUDE.md` or `.github/copilot-instructions.md`. Lessons about the squad are filed in step 12 as `.squad/routing.md`,
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
  break each other (`.squad/routing.md`). Launch them one after another; only `squad-devils-advocate` and
  `squad-security` may run together in steps 2–3, and `squad-reviewer` and `squad-security` in step 8,
  because they are read-only and the reviewer's own build happens in a scratch copy.
- **Commits and pushes** to the work branch are always allowed (`AGENTS.md`, golden rules): commit and
  push `specs/<folder>/log.md` right after intake (step 1), so a stop hook or a crashed session finds no
  untracked files, and after every further completed step. PRs are merged with *Squash and merge*, so only
  the PR title and description reach `main`; intermediate commit messages may name the step, but never
  contain secrets. Interim work-in-progress commits — e.g. demanded by a stop hook while a member is still
  working — are fine for the same reason; the commit that closes the round gets a final subject. Before the
  PR they cost nothing; after it (step 11) every push re-triggers CI and code analysis, so batch the
  edits of a round and commit them once. Stage with plain `git add -A`: ignored paths such as `TestResults/`
  are skipped anyway, and an exclusion pathspec for an ignored path makes `git add` fail and stage
  nothing. Never commit to `main`.
- **GitHub access:** use the GitHub MCP tools (`mcp__github__*`) for issues, comments, labels and pull
  requests. In these sessions the `gh` CLI only works as `gh api repos/<owner>/<repo>/...`; `gh issue`,
  `gh pr` and `gh search` fail (GraphQL is blocked and search is not scoped to the repository).
- **Pull request:** invoking this skill is the user's approval for opening the PR in step 10, once the
  Lead has approved it (tiers `docs` and `patch`: once the latest review round is clean); it opens as a draft
  and is marked ready for review in step 11, once CI and the code analysis are clean.
- **Product Manager:** the user is only contacted when the Lead returns `RESULT: ESCALATE` (relay the
  question verbatim with its options and wait) or for confirming a public issue comment on
  `RESULT: NO CHANGE`.
- Everything that ends up in the repository or on GitHub is written in **English**.

## Steps

1. **Intake.** Read the issue in full, including comments and labels, and decide bug or feature first
   (`.squad/routing.md`, *Bug or feature*): a feature switches to `squad-spec` now, before any branch or
   work folder exists; note the reported environment (versions of the
   software involved, image tag or release, host OS, configuration). If it is closed, stop and report that. Start
   from a clean working tree on a new branch off the latest `main` (run `git fetch origin main --tags` first: a
   stale local `origin/main` makes `scope-check.py` report files that are not yours, and a checkout without the
   release tags makes every decision record look unreleased), e.g.
   `fix-issue-<number>-<short-slug>` (or the branch the session prescribes). Create
   `specs/issue-<number>/log.md` from `specs/_template/log.md`, commit and push it (the first `squad-log.py` row replaces the heading placeholder with
   the work folder's name). Only you (the
   orchestrator) write `log.md`, one row per step, always with
   `python3 .squad/tools/squad-log.py issue-<number> "<step>" "<member>" "<result>"` — it keeps the line
   ending, escapes pipes and control characters, and stamps the row with date and time of day (UTC), so the
   wrap-up can measure your own share of the run. For a subagent launch add the metrics:
   `--agent squad-<role> --tokens <subagent_tokens> --tool-uses <tool_uses> --seconds <duration_ms / 1000>`
   (a decimal such as `495.4` is rounded to whole seconds; `--agent` reads the model and effort from the agent file; `--launch <model>/<effort>` only when the launch
   overrode them). The three numbers come from the usage block of the launch's **task notification**, not
   from the member's hand-back message, and that notification may arrive after you have already read the
   report: write the row as soon as the report is in, **without any metric option** (no `--agent`,
   `--tokens`, `--tool-uses` or `--seconds`). `--agent` (or `--launch`) is rejected without `--tokens`,
   `--tool-uses` and `--seconds`, so the first row of a launch carries none of them; model and effort are
   recorded by the later `--amend-last`. Never use placeholder numbers to get past that check. Add the
   metrics when the notification arrives with
   `python3 .squad/tools/squad-log.py issue-<number> --amend-last "<member>" --agent squad-<role> --tokens …
   --tool-uses … --seconds …` — `--amend-last` is the only way to add them later, never editing `log.md` by
   hand or placeholder numbers (the script refuses fewer than 1,000 tokens). **Every number you pass is
   copied from the usage block of a task notification you have in front of you** — never an estimate, a
   figure from memory, a round number or one from the member's hand-back; without that notification, write
   the row without any metric option and wait, or mark it `--unreported` once it cannot come. A trailer with wrong numbers is corrected with `--replace-last "<member>"` and the
   same options, never with an extra row, which would count the launch twice. The Product Manager's answer
   or a result the user produced (a measurement on their host) is a row with the member cell
   `Product Manager` or `User` and no metric option; like your own `Orchestrator` rows it is no launch. One row per launch, also for
   two members that ran in parallel: the script refuses a member cell that names two, because `--amend-last`
   could not tell their usage apart. When the notification of a parallel launch arrives after you have
   written a later row of that member (the Security plan review's notification after its delta row, the
   Devil's Advocate's after the Lead's revision), add `--step "<step>"` with the exact step cell of the
   launch's row: `--amend-last "<member>" --step "<step>" …` amends that row instead of the member's last one.
   A notification that never arrives (the launch ended on an error, the session was resumed) is marked with
   `--amend-last "<member>" --unreported --agent squad-<role>` and no numbers, so the summary counts the
   launch and names it instead of refusing the table; when that notification turns up after all, the row
   already carries a trailer, so a second `--amend-last` refuses it ("without a launch trailer") and
   `--replace-last "<member>"` with the numbers turns the mark into the full trailer. A continued launch
   (*Continue a member*) is logged with the same options plus `--continued` (or `--continued-from "<step>"`
   with the step cell of the fresh launch, never `--step`, see there), with the notification's numbers copied unchanged: they are cumulative, and the script
   computes the round's share. The wrap-up sums the trailers per role. After every member's report, commit and push the files it left in the work
   folder (the Lead's records, the Tester's tests) yourself, so the next report does not list them as
   untracked noise.
2. **Plan.** Launch `squad-lead` in mode `plan` with the issue text and the work folder. It returns one of:
   - `RESULT: DONE` — for tier **`docs`** (definition in `.squad/routing.md`), a short result (tier, the
     exact edits, acceptance criteria) that you record as the first plan row in `log.md`; apply the edits
     yourself, run `python3 .squad/tools/scope-check.py --tier docs` and *Format check*, then one review
     round in step 8 and step 10 directly — no Security, skeleton, tests, coverage, Code Officer or Lead
     approval.
     For tier **`patch`** the result also names, per edit, the existing test or check that covers it, and
     `patch/security` when a security area, CI or build file is touched: apply the edits yourself; when the
     Lead named a test to add, launch `squad-tester` in mode `coverage` with that test name once; run the
     gates of *Gates of a patch* in `.squad/routing.md` for the kinds of file the diff touches and nothing
     more (*Format* on your own edits; a script, workflow or Dockerfile gets the *Analyzer gate* with
     `--lint-only` and no build; test code gets *Test (project)* for its test project, not the whole suite;
     production code gets *Test with coverage* and the *Coverage gate*, which reads the full run's reports) and
     `python3 .squad/tools/scope-check.py --tier patch`, and log which rows applied; then one review
     round in step 8 — `squad-reviewer`, or on `patch/security` `squad-security` in mode `diff` instead of
     it — and step 10 directly. No challenge, plan security review, skeleton, tests first, Code Officer or
     step 9. Otherwise:
     `plan.md` with the **tier** (`trivial` / `standard` / `security`), acceptance
     criteria, the signatures of new or changed API, the affected area documents (`docs/areas/`), required documentation updates (`README.md`,
     `docs/`), and `Proposed` decision records. Continue with the steps the tier requires.
   - `RESULT: NO CHANGE` — show the proposed issue comment to the user, post it only after confirmation
     (append the log as a collapsed "Squad working record" block), remove the work folder with a commit
     and push, and stop. No PR; the branch stays as it is, and you tell the user so.
   - `RESULT: ESCALATE` — ask the user, then relaunch the Lead with the answer.

   **Plan challenge** (`standard` and `security` only, at most once; `.squad/routing.md`, *Plan challenge*).
   Launch `squad-devils-advocate` with the issue text and the work folder only when the plan changes
   production code **and** its *Options considered* names an alternative, it adds or extends a decision
   record, or the tier is `security`; otherwise log "challenge skipped: no options". On `security`, launch
   it together with step 3 on the same plan commit. On `VERDICT: OBJECTIONS 0 major, …` (on `security`: and
   Security's `APPROVED`), append the minor objections verbatim to `plan.md` as a *Challenge notes* section
   and go on without a Lead launch; Dev and Tester get that section with the plan. Non-blocking findings of Security's
   `APPROVED` plan review are appended verbatim as a *Security notes* section the same way; a note that leaves the
   plan a choice goes to `squad-lead` in mode `revise` before step 4 (one launch together with any major
   objection), so neither a member nor you decide it. On a major objection, launch `squad-lead` in mode `revise` with
   all objections (on `security` together with Security's `CHANGES_REQUIRED` findings, if any); it answers
   each one in the plan's *Challenge* section (accepted and the plan revised, or rejected with a reason) and
   may narrow the scope, raise the tier or switch to `RESULT: NO CHANGE`. `RESULT: NO CHANGE` and
   `RESULT: ESCALATE` are handled as above; after a raised tier, continue with that tier's steps. There is
   no second challenge round and no veto. Record the verdict (also a clean `NO OBJECTIONS`) and the Lead's
   answer in `log.md`.
3. **Plan security review** (`security` tier only). Launch `squad-security` in mode `plan`, in parallel with
   the plan challenge when there is one (both are read-only). On `CHANGES_REQUIRED`, launch **one**
   `squad-lead` in mode `revise` with the findings and the challenge's major and minor objections, then
   `squad-security` once more to confirm the revision (a delta round); a Lead revision made only for a major
   objection is confirmed the same way. A further `CHANGES_REQUIRED` gets one more revision. After the
   **2nd** rejection launch
   `squad-lead` in mode `decide` (scope down, split into issues, abort, escalate, or — for a pure wording defect, or a defect confined to the plan's resource section that changes no scope — accept and fix it followed by exactly one `squad-security` delta confirmation).
4. **Skeleton** (only if the plan adds or changes API; not applicable when the plan declares the change free of
   production and test code — see *Changes without production or test code* in `.squad/routing.md`, which also
   skips step 5 and the *Coverage gate* in step 6 and requires the plan's *Verification without tests* section;
   log the skipped steps and run the verification named there instead). Launch `squad-dev` in mode `skeleton`: the planned
   signatures built as *Skeleton* in `.squad/stack.md` describes (its body is the default; whether a zero-value
   stub is allowed is decided there alone, and a stub the Dev writes is reported as a deviation), plus the existing
   test call sites the plan assigns to the Dev for an incompatible signature change, so the tests of step 5
   compile.
5. **Tests first** (skipped for `trivial`). Launch `squad-tester` in mode `tests-first`: only the tests the
   plan names for its acceptance criteria, each failing on the current code or the skeleton; guards and pins
   of existing behavior wait for the coverage pass. A plan-named acceptance-criterion test that cannot fail on the
   skeleton comes back in the hand-back as **deferred**. Confirm yourself that the new tests compile and fail on
   the current code (unless the Tester justified why one cannot).
   A fix without a reproducing test is only acceptable when the bug genuinely needs a live external
   system — then the PR says so.
6. **Implement and cover.** (A plan whose acceptance criteria can be verified only in CI says so, names the
   owner of each such criterion and expects the CI loop of step 11.) For a change without production or test code this is the Dev's edits alone: no
   `squad-tester`, no *Test with coverage*, no *Coverage gate*. Launch `squad-dev` in mode `implement` with the plan and the test names; it
   also makes the documentation updates the plan lists and may adapt a test call site that no longer
   compiles only because of a signature or field change it made itself (`.squad/routing.md`, *Loop limits*). If the Dev disputes a test, launch `squad-lead`
   in mode `decide`; the Tester changes a test only if the Lead says so. Then run *Test with coverage* and
   the *Coverage gate* yourself (both in `.squad/stack.md`). Launch `squad-tester` in mode `coverage` only
   when the gate fails, the Dev reports uncovered new lines, the plan names a resource bound still without
   its test, a deferred plan test of step 5 is still open (also when the gate already passes), or a reviewer
   asks for a test — and pass that reason; if the gate already passes, no deferred plan test is open and the only
   uncovered lines are accepted gaps, skip the pass and log why in `log.md`. Repeat Dev/Tester until the gate passes.
   Before step 7, search the test files (`grep`) for every test the plan names; a name without a match is a
   deferred plan test still open, whatever the Tester's report called it (a test it held back as a guard or pin
   waits for this pass too), so launch `squad-tester` in mode `coverage` with the missing names. Lines reported as not unit-testable go to
   `squad-lead` in mode `decide`; an accepted gap is recorded in `log.md`.
7. **Code check.** Before the first review round, fetch `main` and merge it when the work branch lacks its
   latest commit (`git merge-base --is-ancestor origin/main HEAD` fails), as *Merge conflicts* in
   `.squad/routing.md` says — renumber a decision record whose number `main` took meanwhile, with its index row
   and every link to it. Then run the gates yourself first, with the commands from `.squad/stack.md`: *Format check*,
   the *Analyzer gate* (no diagnostic of any severity in a changed file), *Test*, the *Coverage gate* (not
   for a change without production or test code) and `python3 .squad/tools/scope-check.py`. Keep the output:
   the reviewers get it in step 8. If everything passes, step 7 is done without launching anyone. Dev and
   Tester ran *Format* on their files before handing back; when *Format check* still fails, run *Format*
   yourself, restore every file it changed outside the change with `git restore -- <file>`, and check
   again. Launch `squad-code-officer` with the base ref only for analyzer diagnostics or style findings that
   need reading; structural items it hands back go to `squad-dev` (or `squad-tester`), a scope finding to its
   owner, and a file it reports as formatted outside the change you restore — and run the gates again until
   they pass. This is the gate before the PR; CI is
   not meant to find anything here.
8. **Review.** Launch `squad-reviewer` (round 1, full) and — for `security` only —
   `squad-security` in mode `diff`, in parallel, against the base ref. Pass them the work folder
   (`specs/<folder>/`) so they check the plan's acceptance criteria and tier (tiers `docs` and `patch`: the first
   `log.md` row, since there is no `plan.md`) and the gate output of step 7 for this head, so they do not
   build and test again; either may raise the tier. Tiers `docs` and `patch`: you fix a blocking finding
   yourself, run the checks of step 2 again, then a delta round, then step 10. Blocking findings → their owner fixes them (`squad-dev` for production code, `squad-tester` for tests) → steps 6
   (coverage) and 7 again → **a new review round on the delta is mandatory** before step 9; never go from
   a blocking finding straight to PR approval. The same holds for a non-blocking finding the Lead decides
   to fix now: any change to production code, tests or `docs/` after a review round needs a delta round. The
   one exception, blocking or not, is a finding the reviewer marks `verbatim`: its fix is the reviewer's
   complete replacement text and touches only documentation, a decision record, an area document,
   `README.md` or a configuration example. You apply it yourself, without a Dev, Tester or Lead launch and
   without a build: apply exactly the text the reviewer proposed, confirm with `git diff` that the file
   changed in nothing else, run the documentation gates (*Format check*, `scope-check.py`,
   `decision-check.py`) and log the finding, the file and the commit. That is the reviewer's own text from
   the reviewer's own round, so it needs no confirming delta round; it is read again only when a delta round
   is due anyway for another change, and a blocking finding resolved this way makes the round clean. A
   verbatim fix the reviewer flags as changing a behavior statement an acceptance criterion relies on is
   not applied: launch `squad-lead` in mode `decide`. Anything you changed beyond the proposed text is an
   ordinary fix with a delta round. At most **2 fix
   rounds** after round 1; then `squad-lead`
   in mode `decide`. Delta rounds continue the reviewer's earlier launch (*Continue a member*). Pass each
   reviewer the exact code SHA to review and tell it that later commits may only touch `specs/`. Avoid
   commits while a round runs; a commit a stop hook demands that touches only `specs/` (log rows) does not
   make the round stale, any other commit after that SHA does. A reviewer reports the SHA it reviewed first,
   and a report for another code head is a stale round. A reviewer or security subagent that stops
   on an API error (for example `529 Overloaded`, or `429` for a rate limit — relaunch it once the limit has
   reset) is relaunched unchanged, at most twice; after that report a blocker to the user. Non-blocking findings are collected for step 9.
9. **PR approval.** First run the checklist yourself (`.squad/routing.md`, step 9), in full: the latest
   review round is clean and nothing but `specs/` bookkeeping and the verbatim documentation fixes of that
   round (step 8, logged) changed since it ran — when a delta round is
   due, run it first, never launch the Lead to find that out; the gates of step 7 pass on this head;
   the area documents and documentation updates the plan names are in the diff; `main` has not moved since
   the merge of step 7, or it is merged again first (*Merge conflicts* in `.squad/routing.md`: the gates of
   step 7 again, and steps 7–8 for a resolution that rewrote code or a statement the merge made untrue).
   Then set each `Proposed`
   decision record of this change and its index row in `docs/decisions/README.md` (the Lead added it with
   the record) to `Accepted` and run `python3 .squad/tools/decision-check.py` (bookkeeping, like `log.md`). Launch `squad-lead` in mode
   `approve-pr` — with the base ref, the gate output, the review outcome and the open points — only when a
   decision is open: a plan deviation in a member's report, a Lead decision recorded during steps 3–8, a
   non-blocking finding that touches code and is not yet decided (fix now, or you open a linked GitHub issue
   now), or a change to `docs/ARCHITECTURE.md` or `.squad/project.md` that the plan does not name or that the
   Reviewer did not find consistent with it. Otherwise log "approved by checklist" and continue. A fix
   the Lead orders goes back to step 6 or 8 (counting against the review loop limit); `NOT APPROVED` without
   a fix means the Lead decides or escalates. After the PR (step 11) a re-approval is asked only once CI on
   the new head has finished and passes, with the finished check-run list attached.
10. **Pull request** (Dev role, performed by you). First move the working record off the branch: post
    `plan.md` (none for tiers `docs` and `patch`) and `log.md` as one comment on the issue (each inside a collapsed `<details>` block, headed
    "Squad working record"). When `plan.md` is so long that re-typing it through a tool call is impractical,
    post a permalink to the last commit that contains it (`https://github.com/<owner>/<repo>/blob/<sha>/specs/issue-<number>/plan.md`)
    plus a summary of the tier, acceptance criteria, decisions and challenge outcome instead; that commit
    stays reachable through the PR's history. Then `git rm -r specs/issue-<number>/`, confirm with
    `python3 .squad/tools/scope-check.py --no-specs`, commit ("Remove squad working record"), and push. Later
    log rows (steps 11–12) are appended by editing that comment. In a cloud session the work branch may
    already have a pull request created for it automatically: list the open PRs for the head branch first
    (`list_pull_requests` with `head: "<owner>:<branch>"`) and, if there is one, update its title and body
    (`update_pull_request`, with `draft: true` when it is not a draft) instead of creating a second one, which
    fails with "A pull request already exists". Otherwise open the PR **as a draft**
    (`draft: true`, `.squad/routing.md`, *Draft until green*) from
    [`.github/pull_request_template.md`](../../../.github/pull_request_template.md): title per
    `docs/CONTRIBUTING.md` — `[area] Description`, where `area` is one of the areas
    CONTRIBUTING lists, capitalized — not a lowercase class or file name. It becomes the squash
    commit subject on `main`. Body describing the bug, the fix and the
    reproducing test, `Closes #<number>` under Issues, links to the decision records. Next Steps lists
    **only linked GitHub issues** (create them now) or "None" — never an unlinked "revisit later". Follow the `create-pr` skill's template rules, but
    do **not** run its internal review loop — step 8 replaced it. If the fix is not fully verifiable
    without a real external system, say so. Read the body back right after creating or updating the PR
    (`pull_request_read`, method `get`) and compare its links and code spans with what you sent; a mangled one
    is fixed with `update_pull_request` now, before step 11.
11. **After the PR.** Subscribe to the PR's activity right after opening it (`subscribe_pr_activity` when
    available; otherwise check the CI and code-analysis (e.g. SonarQube Cloud) results yourself before finishing) — a session
    that ends with an unwatched PR has not completed this step. On every look at the PR, read its mergeability
    first: a conflict with the base branch keeps CI from starting and is merged and resolved before anything
    else (`.squad/routing.md`, *Merge conflicts*). Stay with the PR until it merges cleanly, CI is green and the
    code analysis (e.g. SonarQube Cloud) passed with no new issue on the head, then mark it ready for review
    (`update_pull_request` with `draft: false`) — never earlier, and after a fix only once its re-approval
    (step 9) has passed on the new head, see `.squad/routing.md`, *Draft until green*:
    - code-analysis findings → `squad-code-officer` (structural ones → `squad-dev`);
    - failing build or tests → `squad-dev` (test defects → `squad-tester`);
    - review comments (human, automated, `review-pr`) → `squad-dev`, worked in this PR, blocking or not.

    While a member is running, wait for its hand-back before ending your turn: after the PR every push costs a
    CI cycle. When a stop hook still forces a commit and push of a half-finished tree, log that head as work in
    progress (a row naming the SHA) and do not judge CI or the code analysis on it; the commit that closes the
    round is the head to judge.

    Continue the member that made the change and the reviewer of the last round where their agent ids are
    still known (*Continue a member*). Each fix goes through steps 7–8 again (delta review; `scope-check.py --no-specs` from now on), with at
    most 2 fix rounds per failure before the Lead decides. A re-approval (step 9) runs the same checklist once
    CI on the new head has finished and passes. Before you decide that a code-analysis finding persists or is
    fixed, or that its quality gate passed, match the analysis to the head: compare the commit SHA of the
    SonarQube analysis (`api/project_pull_requests/list?project=<key>`, or the `head_sha` of its check run)
    with the PR head (`.squad/routing.md`, *Draft until green*). A `Quality Gate passed` comment or an open
    issue list can belong to an earlier head while the analysis of the newest head is still running; it is
    no evidence either way — wait for the analysis of the current head. When the plan's acceptance criteria
    can only be verified in CI (for example a container image the sandbox cannot build), a red CI on exactly
    those criteria is a normal outcome of this step once the *Image build check* ran before the review
    (`.squad/routing.md`, *Criteria verified only in CI*): the plan names the owner (Dev for Dockerfile,
    compose and workflow files), and every fix gets a delta review round. The work folder is gone by now: give the Reviewer, Security and the Lead the plan (tiers
    `docs` and `patch`: the first log row; features:
    also `spec.md` and `tasks.md`) from the "Squad working record" comment, or via
    `git show <commit-before-removal>:specs/<folder>/<file>`, and record each log row by editing that
    comment. Edit a restored working record in one batch: interim commits that a stop hook demands while it
    is half edited each re-trigger the code analysis on the open PR. Never skip, disable or weaken a test to
    get green.
12. **Wrap-up (mandatory).** Collect what this run taught about the squad itself (a rule that was
    unclear or contradictory, a tool that misbehaved, an agent that could not be launched, a step that
    had to be improvised), each with the role it concerns and a concrete proposal, and file them as
    `.squad/routing.md`, *Squad lessons*, says: lessons about template-managed files as **one** issue
    labelled `squad` in the template repository named in `.squad/template.json` (a general lesson belongs there, never in this
    repository: attach that repository to the session first if needed, with the access to create the issue;
    only if attaching is refused, file it here with the label `squad-upstream`), lessons about project knowledge
    as **one** issue labelled `squad` in this repository (create the labels if missing). Link the issues
    from the working record comment. Do **not** edit `.squad/`, `.claude/` or the instruction files.
    Then run `python3 .squad/tools/squad-log.py issue-<number> --summary` on the log (restore it from the
    working record comment if the folder is gone). Exit code 3 means the summary ends with launch rows that
    carry no usage: add it with `--amend-last` (with `--step "<step>"` for a row that is no longer the
    member's last) from each launch's task notification, or mark a launch whose notification never arrived
    with `--unreported`, and run the summary again — a table is posted only as the script prints it, never
    typed from memory, summed by hand or with rows left out, and a run without a table is not wrapped up.
    Append the tables to the working record comment under
    a "Squad run metrics" heading: launches, tokens, tool uses and seconds per role are what tunes the
    squad's models and effort over time, and the **Orchestrator** row (wall time from the first to the last
    row, the part no launch covers, the gate runs) shows your own share.
    Report the branch, the PR URL, the tier, the `squad` issues (or "no lessons"), the metrics tables with the
    Orchestrator row and any escalation or Lead decision to the user. If there is genuinely nothing to learn,
    append a `| <date> <time> | 12 Wrap-up | Orchestrator | no lessons |` row to the working record comment
    instead of opening an issue — the step itself is never skipped.

## What the pull request says — and what it doesn't

The PR title and description document the change, not how it was produced: the bug, the fix and the test
that pins it down. Plan revisions, review rounds and their findings never appear there.
(The working record is the "Squad working record" comment on the issue; the lasting reasoning is in
`docs/decisions/`.)

## Notes

- Only you call the session-management tools (`create_session`, `send_message`, `archive_session`); members
  never do, and a member that called one anyway is reported in the step-12 lessons.
- A plan or fixture with escapes (`\u0041`, an escaped surrogate pair): before you commit it, check the
  bytes on disk (`grep -n '\\u' <file>`); the Write and Edit tools resolve single-backslash escapes
  (*Escapes in files* in `.squad/team.md`).
- Prefer non-interactive commands only. If push or PR creation fails, stop and report it.
- Never close the issue manually; `Closes #<number>` closes it on merge.
