# Routing

The pipeline is the same for issues and features; only the input differs (a GitHub issue for
`squad-issue`, a feature idea plus `spec.md` for `squad-spec`). The orchestrator — the session that runs
the skill — launches the members, passes documents between them (subagents cannot talk to each other
directly), performs every Git and GitHub action (including follow-up issues the Lead decides on) and
records every step in the work folder's `log.md` (after step 10, in the "Squad working record" comment
that replaces it). Step numbers below are the ones the skills use.

## Work folder

- Issue: `specs/issue-<number>/` — `plan.md`, `log.md`
- Feature: `specs/feature-<short-slug>/` — `spec.md`, `plan.md`, `tasks.md`, `log.md`
- The work folder lives **only on the work branch**: it is committed and pushed after every step so a
  crashed session can resume, and in step 10 its content is posted as a "Squad working record" comment
  (on the issue, or on the PR for a feature without issue) and the folder is removed before the PR opens.
  `main` never contains `specs/` working records.
- Decision records (both): `docs/decisions/NNNN-title.md` — the lasting *why*, written by the Lead; this
  is what a reader months later looks at.

## Scope of a product PR

An issue or feature PR changes the product and its documentation only. It never touches the squad or the
agent instructions: `.squad/` (charters, `history.md`, `decisions.md`, tools), `.claude/`,
`.github/skills/`, `.agents/skills/`, `CLAUDE.md`, `AGENTS.md`, `.github/copilot-instructions.md`.
Lessons about the squad are collected in step 12 and filed where they can be fixed (*Squad lessons*
below), never fixed in the product PR. The Reviewer reports any such file in a product PR as a blocking
finding.

One exception: `.squad/stack.md` and `.squad/project.md` describe the product, not the squad. A product PR
updates them when the change itself makes them untrue — a new build command, a new security area, a new
guarantee or coupling point — and the Reviewer treats a stale entry there like stale documentation.

## Template-managed files

Many squad, instruction and documentation files come from the Squad-Spec-Repository-Template repository and
are refreshed from there with its `adopt-template` skill (`.squad/template.json` records the template
repository, its commit and the stack profile). Three kinds:

- **Managed** — overwritten on every refresh: `.squad/team.md`, `.squad/routing.md`, the charters in
  `.squad/agents/*/charter.md`, `.squad/tools/*.py` except `squad_settings.py`, `.squad/tools/.gitignore`,
  `.claude/agents/squad-*.md`, `.claude/hooks/session-start.sh`, the template's skills under
  `.claude/skills/`, `.agents/skills/` and `.github/skills/`,
  `.github/ISSUE_TEMPLATE/feature_request.md`, `docs/decisions/_template.md`, `specs/README.md` and
  `specs/_template/`.
- **Marked** — rebuilt on every refresh, keeping the repository's content inside
  `<!-- project:… -->` blocks: `CLAUDE.md`, `AGENTS.md`, `.github/copilot-instructions.md`,
  `docs/CONTRIBUTING.md`, `docs/ARCHITECTURE.md`, `.github/ISSUE_TEMPLATE/bug_report.md`,
  `.github/pull_request_template.md` and `docs/decisions/README.md`. Text outside the project blocks is the template's.
- **Seeded** — created once and owned by the repository from then on: `.squad/stack.md`,
  `.squad/project.md`, `.squad/decisions.md`, the `history.md` files, `.squad/tools/squad_settings.py`,
  `.claude/settings.json`, `SECURITY.md`, `docs/UNIT_TESTS.md` and the stack profile's CI, CodeQL, Dependabot and tool
  configuration files.

## Squad lessons

Every lesson from step 12 is filed once, where it can actually be fixed:

| The lesson concerns | Filed as | Fixed by |
| ------------------- | -------- | -------- |
| a **managed** file, or the template part of a **marked** file (squad rules, charters, agents, skills, tools, the shared sections of the instruction files) | an issue labelled `squad` in the template repository named in `.squad/template.json` (`repository`), titled `[Squad] <lesson> (from <this repository>#<issue>)` and linking the run | a PR in the template repository, then a refresh of every repository that uses the template (`adopt-template`) — never a local edit, which the next refresh would overwrite |
| **project knowledge**: `.squad/stack.md`, `.squad/project.md`, `.squad/tools/squad_settings.py`, a `<!-- project:… -->` block, another seeded file | an issue labelled `squad` in this repository | a small squad-maintenance PR in this repository, checked with `.squad/tools/config-check.py` |

One run's lessons go into at most one issue per destination. If the session cannot create an issue in the
template repository (no access), it files that issue in this repository with the label `squad-upstream`
and tells the user, who moves it to the template repository; it is never worked here.

## Tiers

In step 2 the Lead classifies the change and justifies the tier in `plan.md` (for `docs`, in its result). When in doubt, the higher
tier applies; Security or the Reviewer may raise the tier at any point (never lower it).

| Tier | When | Pipeline |
| ---- | ---- | -------- |
| `docs` | Issues only (never a feature). Only product Markdown documentation changes — `README.md`, `docs/` except `docs/decisions/`, `SECURITY.md`, and inside their `<!-- project:… -->` blocks `docs/CONTRIBUTING.md`, `docs/ARCHITECTURE.md`, the bug report and the pull request template — and no other file at all (template-managed files such as the feature-request template are fixed in Squad-Spec-Repository-Template, see *Template-managed files*): not even a comment in production or test code (that is `trivial`), no build, CI, Docker or config file, and never squad or instruction files (`.squad/`, `.claude/`, `.github/skills/`, `.agents/skills/`, `CLAUDE.md`, `AGENTS.md`, `.github/copilot-instructions.md`). A change that needs a decision record is not `docs` either. | Lead plans briefly (no `plan.md`: tier, change list and acceptance criteria go into its result, recorded as the first `log.md` row); steps 3–7 and 9 skipped; the Dev makes the edits; the orchestrator verifies read-only (*Format check* in `.squad/stack.md`); one Reviewer round, which checks the diff against the first `log.md` row — a blocking finding goes to the Dev, then the read-only check and a delta round; the orchestrator opens the PR once the latest round is clean. |
| `trivial` | Documentation that does not qualify as `docs`, code comments, log or UI wording, configuration defaults, or a documentation change that needs a decision record — no change to behavior or control flow | Steps 3, 4 and 5 skipped (no Security, no tests-first); code check, Reviewer and Lead approval still run. Tests and coverage are still required if production code changes. |
| `standard` | A behavior change that touches none of the security areas below | Plan challenge in step 2; step 3 skipped; Security reviews only the diff (step 8) |
| `security` | Touches one of the security areas listed in `.squad/project.md` (*Security areas*), Docker/CI or build configuration, or adds/updates a dependency | Full pipeline, including the plan challenge in step 2 |

## Pipeline

| # | Step | Owner | Exit condition |
| - | ---- | ----- | -------------- |
| 1 | Intake | Orchestrator | Branch off `main`, work folder and `log.md` created, committed and pushed |
| 2 | Plan | Lead, Devil's Advocate | `plan.md` with tier, acceptance criteria, signatures of new/changed API, doc updates; decision records `Proposed`. Or outcome **no change** (see below). `standard`/`security`: one plan challenge by the Devil's Advocate, every objection answered by the Lead in the plan's *Challenge* section |
| 3 | Plan security review | Security | `APPROVED` → 4; `CHANGES_REQUIRED` → Lead revises, back to 3 (`security` tier only) |
| 4 | Skeleton | Dev | Only when the plan adds or changes API: compile-only signatures built as *Skeleton* in `.squad/stack.md` describes, *Build* passes |
| 5 | Tests first | Tester | Tests for every acceptance criterion; they compile and **fail** on the current code |
| 6 | Implementation + coverage | Dev, Tester | All tests green; ≥ 80 % line coverage on new/changed code and overall (*Coverage gate* in `.squad/stack.md`); doc updates from the plan done |
| 7 | Code check | Code Officer | *Format* and *Analyzer gate* from `.squad/stack.md` pass (no diagnostic of any severity in changed files); same tests green; no structural change |
| 8 | Review | Reviewer + Security | No blocking findings → 9; blocking → owner fixes (Dev: code, Tester: tests), back to 6, then a mandatory delta round (Security only for `standard`/`security`) |
| 9 | PR approval | Lead | Latest review round without a blocking finding not covered by a recorded Lead decision, and covering every change to production code, tests and `docs/` except `specs/` bookkeeping and the Lead's own approval edits (record status, index, `docs/ARCHITECTURE.md` link); plan fulfilled, coverage met, decision records `Accepted` and indexed → `APPROVED` → 10 |
| 10 | Pull request | Dev (via orchestrator) | Working record posted as comment, `specs/<folder>/` removed, PR opened (merged later with *Squash and merge*) |
| 11 | After the PR | Dev, Code Officer, Reviewer | CI green, SonarQube Cloud quality gate passed, review comments worked |
| 12 | Wrap-up | Orchestrator | Squad lessons filed as one issue per destination (*Squad lessons*), or "no lessons" logged; user informed |

Commits and pushes to the work branch happen right after intake (`specs/<folder>/log.md`, so a stop hook
or a crashed session finds no untracked files) and after every further completed step; with *Squash and
merge* only the PR title and description reach `main`, so intermediate commits may describe the step.
They never contain secrets.

## Concurrency

Only one member that builds or runs tests may work at a time: concurrent builds and test runs share
build output and caches and break each other (see *Concurrency* in `.squad/stack.md` for what this stack
shares). In step 8, `squad-reviewer` and `squad-security` may run together because
both are read-only and the reviewer builds in a scratch copy. No member experiments (mutation tests,
trial edits) in the repository working tree — use a scratch `git worktree` instead.

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
  decides: narrow the scope, split into separate issues, or escalate to the Product Manager.
- **Review ↔ Dev (steps 6–8):** review pass 1 is a full review; at most **2 further fix-and-review
  rounds**, each reviewing only the delta. Blocking findings still open after that go to the Lead, who
  decides: accept with justification, split into a follow-up issue, abort, or escalate.
- **Existing tests affected by a signature change:** when the plan changes a constructor or another
  signature that existing test code calls (a test factory or helper), the plan lists those call sites and
  who adapts them. If the old signature is removed or changed incompatibly, the existing tests stop
  building as soon as the skeleton exists, so the **Dev** adapts exactly the listed call sites in step 4
  (*Skeleton*) — mechanically, to the new signature, without touching an assertion — so *Build* passes
  before the Tester starts. If old and new signature coexist (e.g. an added overload), nothing breaks, and
  the **Tester** moves the listed call sites to the new signature in step 5 where the plan asks for it.
  This is the only case in which the Dev edits test code; the Tester checks the Dev's edit of those call
  sites in its coverage step (only those sites, no assertion weakened).
- **Dev ↔ Tester disagreements:** if the Dev believes a step-5 test is wrong, the Lead decides (the test
  is not changed silently). Not counted against a loop limit.
- **Code check needs a structural change** (or breaks build/tests): the Code Officer's edit is reverted
  and the item goes to the Dev (or Tester for tests), then the code check runs again. Not counted
  against a loop limit.
- **Coverage below 80 %:** Dev and Tester iterate; lines that cannot be covered by a unit test go to the
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

Fixed in the same change or opened as a linked GitHub issue now (Lead decides which) — never deferred to
"a later change".
