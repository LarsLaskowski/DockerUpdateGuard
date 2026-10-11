# Routing

The pipeline is the same for issues and features; only the input differs (a GitHub issue for
`squad-issue`, a feature idea plus `spec.md` for `squad-spec`). The orchestrator — the session that runs
the skill — launches the members, passes documents between them (subagents cannot talk to each other
directly), performs every Git and GitHub action (including follow-up issues the Lead decides on) and
records every step in the work folder's `log.md` with `python3 .squad/tools/squad-log.py` — each subagent
launch with its model and effort (`--agent`) and the usage its task notification reports, added later with
`--amend-last` when the notification follows the hand-back and corrected with `--replace-last` — (after step 10, in the "Squad working record"
comment that replaces it). Step numbers below are the ones the skills use.

## Work folder

- Issue: `specs/issue-<number>/` — `plan.md`, `log.md`
- Feature: `specs/feature-<short-slug>/` — `spec.md`, `plan.md`, `tasks.md`, `log.md`
- The work folder lives **only on the work branch**: it is committed and pushed after every step so a
  crashed session can resume, and in step 10 its content is posted as a "Squad working record" comment
  (on the issue, or on the PR for a feature without issue) and the folder is removed before the PR opens.
  `main` never contains `specs/` working records.
- Decision records (both): `docs/decisions/NNNN-title.md` — the lasting *why*, written by the Lead; this
  is what a reader months later looks at. The lasting *what* of a change in behavior is its area document
  (`docs/areas/<area>.md`), named in the plan under *Areas* and updated in the same pull request.

## Scope of a product PR

An issue or feature PR changes the product and its documentation only. It never touches the squad or the
agent instructions: `.squad/` (`team.md`, `routing.md`, tools), `.claude/` and `CLAUDE.md`.
Lessons about the squad are collected in step 12 and filed where they can be fixed (*Squad lessons*
below), never fixed in the product PR. `python3 .squad/tools/scope-check.py` reports any such file in the
diff; the orchestrator runs it in step 7 and the Reviewer in step 8, and a finding is blocking.

One exception: `.squad/stack.md` and `.squad/project.md` describe the product, not the squad. A product PR
updates them when the change itself makes them untrue — a new build command, a new security area, a new
guarantee or coupling point — and the Reviewer treats a stale entry there like stale documentation.

## Template-managed files

Many squad, instruction and documentation files come from the Squad-Spec-Repository-Template repository and
are refreshed from there with its `adopt-template` skill (`.squad/template.json` records the template
repository, its commit and the stack profile). Three kinds:

- **Managed** — overwritten on every refresh: `.squad/team.md`, `.squad/routing.md`, `.squad/tools/*.py`
  except `squad_settings.py`, `.squad/tools/.gitignore`,
  `.claude/agents/squad-*.md`, `.claude/hooks/`, the template's skills under `.claude/skills/`,
  `.github/ISSUE_TEMPLATE/feature_request.md`, `docs/decisions/_template.md`, `docs/areas/_template.md`,
  `specs/README.md` and `specs/_template/`.
- **Marked** — rebuilt on every refresh, keeping the repository's content inside
  `<!-- project:… -->` blocks: `CLAUDE.md`, `docs/CONTRIBUTING.md`, `docs/ARCHITECTURE.md`, `.github/ISSUE_TEMPLATE/bug_report.md`,
  `.github/pull_request_template.md`, `docs/decisions/README.md` and `docs/areas/README.md`. Text outside the
  project blocks is the template's.
- **Seeded** — created once and owned by the repository from then on: `.squad/stack.md`,
  `.squad/project.md`, `.squad/tools/squad_settings.py`,
  `.claude/settings.json`, `SECURITY.md`, `docs/UNIT_TESTS.md` and the stack profile's CI, CodeQL, Dependabot and tool
  configuration files.

## Squad lessons

Every lesson from step 12 is filed once, where it can actually be fixed:

| The lesson concerns | Filed as | Fixed by |
| ------------------- | -------- | -------- |
| a **managed** file, or the template part of a **marked** file (squad rules, agents, skills, tools, the template sections of `CLAUDE.md`) | an issue labelled `squad` in the template repository named in `.squad/template.json` (`repository`), titled `[Squad] <lesson> (from <this repository>#<issue>)` and linking the run | a PR in the template repository, then a refresh of every repository that uses the template (`adopt-template`) — never a local edit, which the next refresh would overwrite |
| **project knowledge**: `.squad/stack.md`, `.squad/project.md`, `.squad/tools/squad_settings.py`, a `<!-- project:… -->` block, another seeded file | an issue labelled `squad` in this repository | a small squad-maintenance PR in this repository, checked with `.squad/tools/config-check.py` |

One run's lessons go into at most one issue per destination. A lesson that is general — one that would change
the template — is filed in the template repository, not in the product repository, and the session attaches
the template repository first if it is not yet part of the session (with the access it needs to create the
issue, e.g. `add_repo`). Only if attaching is refused (no access) it files that issue in this repository with
the label `squad-upstream` and tells the user, who moves it to the template repository; it is never worked
here. Once the template repository has fixed the lesson, the product repository adopts the change with
`adopt-template` (*Template-managed files*).

## Tiers

In step 2 the Lead classifies the change and justifies the tier in `plan.md` (for `docs`, in its result). When in doubt, the higher
tier applies; Security or the Reviewer may raise the tier at any point (never lower it).

| Tier | When | Pipeline |
| ---- | ---- | -------- |
| `docs` | Issues only (never a feature). The diff changes only product documentation: `README.md`, `SECURITY.md`, Markdown under `docs/` except `docs/decisions/` and `docs/areas/`, the project blocks of the marked files, and `.squad/stack.md` or `.squad/project.md` as documentation (see below). Any other file — a code comment, a config or CI file, a decision record, an area document — makes it `trivial` or higher. `scope-check.py --tier docs` checks exactly this. | Lead plans briefly (no `plan.md`: tier, exact edits and acceptance criteria go into its result, recorded as the first `log.md` row); the orchestrator applies the edits itself; `scope-check.py --tier docs` and *Format check* pass; one Reviewer round reads the diff against the first `log.md` row (no build) — a blocking finding is fixed, checked and delta-reviewed the same way; the orchestrator opens the PR once the latest round is clean. Steps 3–7 and 9 are skipped. |
| `trivial` | Documentation that does not qualify as `docs`, code comments, log or UI wording, configuration defaults, or a documentation change that needs a decision record — no change to behavior or control flow | Steps 3, 4 and 5 skipped (no Security, no tests-first); code check, Reviewer and PR approval (step 9) still run. Tests and coverage are still required if production code changes. |
| `standard` | A behavior change that touches none of the security areas below | Plan challenge in step 2; steps 3 and the Security review skipped — the Reviewer covers the security checklist and raises the tier if a security area is touched after all |
| `security` | Touches one of the security areas listed in `.squad/project.md` (*Security areas*), Docker/CI or build configuration, or adds/updates a dependency | Full pipeline, including the plan challenge in step 2 |

`.squad/stack.md` and `.squad/project.md` describe the product, so an issue about them is a product PR and
counts as documentation for the `docs` tier — except an edit that changes *Security areas* or a command the
pipeline runs: that changes what the squad enforces and is `security`. A squad-maintenance PR (*Squad
lessons*) is only for lessons the squad itself raised, never for an issue a person filed about the product.

### Changes without production or test code

For **any** tier, a plan may declare steps 4 (*Skeleton*), 5 (*Tests first*) and the *Coverage gate* of step 6
**not applicable** when the change touches no production or test code (for example a workflow, a Dockerfile,
build or CI configuration, or documentation only). The tier is not lowered by this: a `security` change
keeps its plan challenge, steps 3 and 8 and every other step. The Lead then names in `plan.md`, under
*Verification without tests*, where each acceptance criterion is verified instead (a workflow verification
step, a PR dry run, a build of the image, a read-only check) and who runs it; the orchestrator logs the
skipped steps with a pointer to that section. The Reviewer checks that the statement exists, that it covers
every acceptance criterion and that the diff really contains no production or test code — otherwise the
skip is a blocking finding. If production or test code changes after all, steps 4–6 apply again.

## Pipeline

| # | Step | Owner | Exit condition |
| - | ---- | ----- | -------------- |
| 1 | Intake | Orchestrator | Branch off `main`, work folder and `log.md` created (rows appended with `squad-log.py`), committed and pushed |
| 2 | Plan | Lead, Devil's Advocate | `plan.md` with tier, acceptance criteria, signatures of new/changed API, affected areas (`docs/areas/`), doc updates; decision records `Proposed` and indexed. Or outcome **no change** (see below). `standard`/`security`: one plan challenge by the Devil's Advocate, every objection answered by the Lead in the plan's *Challenge* section |
| 3 | Plan security review | Security | `APPROVED` → 4; `CHANGES_REQUIRED` → Lead revises, back to 3 (`security` tier only) |
| 4 | Skeleton | Dev | Only when the plan adds or changes API: compile-only signatures built as *Skeleton* in `.squad/stack.md` describes, *Build* passes |
| 5 | Tests first | Tester | Tests for every acceptance criterion; they compile and **fail** on the current code |
| 6 | Implementation + coverage | Dev, Tester | All tests green; *Coverage gate* from `.squad/stack.md` passes; doc updates from the plan done, including the area documents |
| 7 | Code check | Orchestrator, Code Officer | The orchestrator runs *Format check*, *Analyzer gate*, *Test*, *Coverage gate* and `scope-check.py`. Only when one of them fails is the Code Officer launched (format and analyzer-only edits; structural items go to the Dev or Tester), then the gates run again. Exit: every gate passes on the head, same tests green |
| 8 | Review | Reviewer (+ Security on `security`) | They get the head SHA and the orchestrator's gate output of step 7 (they do not re-run the gates). No blocking findings → 9; blocking → owner fixes (Dev: code, Tester: tests), back to 6, then a mandatory delta round |
| 9 | PR approval | Orchestrator, Lead when needed | The orchestrator checks: the latest review round is clean and covers every change to production code, tests and `docs/` since it ran (only `specs/` bookkeeping may follow it); the gates of step 7 pass on the head; the area documents and documentation updates the plan names are in the diff; then sets each `Proposed` record of this change and its index row to `Accepted` (`decision-check.py` passes). The Lead is launched in mode `approve-pr` only when a decision is open: a plan deviation in a member's report, a Lead decision recorded during steps 3–8 (loop limit, dispute, accepted gap, finding accepted unfixed), non-blocking findings not yet decided, or a change to `docs/ARCHITECTURE.md` or `.squad/project.md`. Otherwise "approved by checklist" is logged → 10 |
| 10 | Pull request | Orchestrator | Working record posted as comment (a long `plan.md` may be given as a permalink to the last commit that contains it plus a summary), `specs/<folder>/` removed and `scope-check.py --no-specs` clean, PR opened (merged later with *Squash and merge*) |
| 11 | After the PR | Dev, Code Officer, Reviewer | CI green, the CI code analysis (e.g. SonarQube Cloud) passed, review comments worked |
| 12 | Wrap-up | Orchestrator | Squad lessons filed as one issue per destination (*Squad lessons*), or "no lessons" logged; run metrics (launches, tokens, tool uses and seconds per role, `squad-log.py --summary`) appended to the working record; user informed |

A change without production or test code (tier `docs`, or any other tier whose plan declares it, see
*Changes without production or test code*) skips steps 4 and 5; step 6 is the Dev's edits alone (tier
`docs`: the orchestrator's), and the *Coverage gate* is not run, because such a change cannot alter
coverage. Step 7 still runs *Format check*, the *Analyzer gate* and `scope-check.py`; for tier `docs` only
*Format check* and `scope-check.py --tier docs`.

Commits and pushes to the work branch happen right after intake (`specs/<folder>/log.md`, so a stop hook
or a crashed session finds no untracked files) and after every further completed step; with *Squash and
merge* only the PR title and description reach `main`, so intermediate commits may describe the step.
They never contain secrets. A stop hook may demand a commit while a member is still working; that interim
commit ("Work in progress: …") is fine, and the commit that closes the round gets a final subject. Before
the PR such commits cost nothing; after it every push re-triggers CI and the code analysis, so edits are
batched and committed once per round.

## Reading issues and pull requests

In the remote sessions GraphQL is blocked, so `gh issue view`, `gh pr view` and `gh search` fail. A member
that reads an issue or a pull request itself (the Lead checking the issue's claims, the Devil's Advocate,
the Reviewer) uses `gh api repos/<owner>/<repo>/issues/<n>` and `.../issues/<n>/comments` (for a pull
request `.../pulls/<n>`), or the GitHub MCP `issue_read` where the member has the tool. Members never post
to GitHub; the orchestrator does.

## Reading third-party repositories

The GitHub MCP tools and `gh api` are scoped to the repositories of the session and answer 403 for any
other repository (for example a third-party action). A member that has to verify such an action's pin or
source (the Lead, the Devil's Advocate, Security) uses plain Git over HTTPS instead:
`git ls-remote --tags https://github.com/<owner>/<repo>` resolves a tag to its commit, and a shallow clone
of that tag (`git clone --depth 1 --branch <tag> https://github.com/<owner>/<repo>`, into a scratch
directory outside the working tree) lets the member read the source and compare the commit with the pin.

## Concurrency

Only one member that builds or runs tests may work at a time: concurrent builds and test runs share
build output and caches and break each other (see *Concurrency* in `.squad/stack.md` for what this stack
shares). In step 8, `squad-reviewer` and `squad-security` may run together because
both are read-only and the reviewer builds in a scratch copy. No member experiments (mutation tests,
trial edits, baseline comparisons) in the repository working tree — use a scratch `git worktree` instead;
`git stash` is a Git write operation and forbidden. The members' `PreToolUse` hook
(`.claude/hooks/git-guard.py`) denies Git and GitHub writes and answers with the reason; a member that
needs one reports it to the orchestrator.

## Outcome "no change"

If the Lead concludes in step 2 (also after answering the Devil's Advocate) that no code change is needed — duplicate, cannot be reproduced, works as
designed (e.g. covered by an accepted decision record), or out of scope — it returns
`RESULT: NO CHANGE` with a proposed issue comment. The orchestrator shows the comment to the Product
Manager and posts it only after confirmation (a public statement on the issue). The orchestrator removes
the work folder with a commit and pushes; no PR is opened, the branch stays without one, and the user is
told so.

## Loop limits

- **Plan challenge (step 2):** exactly one Devil's Advocate round, no veto. The Lead answers every
  objection (accept and revise, or reject with a reason); a rejected objection is not raised again.
- **Plan ↔ Security (steps 2–3):** at most 2 rejections. After the 2nd `CHANGES_REQUIRED` the Lead
  decides: narrow the scope, split into separate issues, escalate to the Product Manager, or — when the
  remaining defect is a pure wording defect — accept and fix it, followed by exactly one Security delta
  confirmation of the fix (a further rejection then goes to the Product Manager).
- **Review ↔ Dev (steps 6–8):** review pass 1 is a full review; at most **2 further fix-and-review
  rounds**, each reviewing only the delta. Blocking findings still open after that go to the Lead, who
  decides: accept with justification, split into a follow-up issue, abort, or escalate.
- **Existing tests affected by a signature change:** the plan lists the existing test call sites of a
  changed signature (factories, helpers) and who adapts them. The **Dev** adapts them in step 4 when the
  old signature goes away (so *Build* passes before the Tester starts), and in any later step when a call
  site stops compiling only because of a signature or field change the Dev made itself — mechanically, to
  the new signature, no assertion touched, listed in the Dev's report. The **Tester** moves them in step 5
  when old and new signature coexist, and checks the Dev's edits in its coverage step (only call sites that
  no longer compiled, no assertion weakened). These are the only cases in which the Dev edits test code.
- **Dev ↔ Tester disagreements:** if the Dev believes a step-5 test is wrong, the Lead decides (the test
  is not changed silently). Not counted against a loop limit.
- **Code check needs a structural change** (or breaks build/tests): the Code Officer's edit is reverted
  and the item goes to the Dev (or Tester for tests), then the code check runs again. Not counted
  against a loop limit.
- **Coverage gate fails:** Dev and Tester iterate; lines that cannot be covered by a unit test go to the
  Lead, whose decision is recorded in `log.md`.
- **After the PR (step 11):** CI or quality-gate failures and review comments are fixed on the same
  branch and go through steps 7–8 again (delta review). The same limit of 2 fix rounds applies per
  failure; after that the Lead decides.

## Escalation to the Product Manager

The Lead escalates only when it cannot decide responsibly on its own: the requirement is ambiguous, the
fix needs a product decision (behavior change visible to users, breaking a documented guarantee in
`docs/ARCHITECTURE.md` or an accepted decision record), or a loop limit was hit and none of the Lead's
options is clearly right. The escalation is one concise question with the options and the Lead's
recommendation. Follow-up GitHub issues the Lead decides on are created by the orchestrator and listed in
the PR under Next Steps.

## Non-blocking findings

Fixed in the same change or opened as a linked GitHub issue now — never deferred to "a later change". The
Lead decides which in step 9 (one launch for all of them); a finding fixed now gets a delta round.
