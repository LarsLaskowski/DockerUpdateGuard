# Routing

The pipeline is the same for issues and features; only the input differs (a GitHub issue for
`squad-issue`, a feature idea plus `spec.md` for `squad-spec`). The orchestrator — the session that runs
the skill — launches the members, passes documents between them (subagents cannot talk to each other
directly), performs every Git and GitHub action (including follow-up issues the Lead decides on) and
records every step in the work folder's `log.md` with `python3 .squad/tools/squad-log.py` — each subagent
launch with its model and effort (`--agent`) and the usage its task notification reports, one row per launch,
added later with `--amend-last` when the notification follows the hand-back (`--step "<step>"` reaches a row
that is no longer the member's last, as after parallel launches; `--unreported` marks a launch whose
notification never came), corrected with `--replace-last` and reduced to the round's share with `--continued`
for a continued member (`--continued-from "<step>"` when the agent continued is not the member's last fresh
launch) — (after step 10, in the "Squad working record" comment that replaces it). Step numbers below are the ones the skills use.

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
agent instructions: `.squad/` (`team.md`, `routing.md`, tools), `.claude/`, `AGENTS.md`, `CLAUDE.md`
and `.github/copilot-instructions.md`.
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
  `<!-- project:… -->` blocks: `AGENTS.md`, `docs/CONTRIBUTING.md`, `docs/ARCHITECTURE.md`, `.github/ISSUE_TEMPLATE/bug_report.md`,
  `.github/pull_request_template.md`, `docs/decisions/README.md` and `docs/areas/README.md`. Text outside the
  project blocks is the template's. `.github/copilot-instructions.md` is generated from `AGENTS.md`: its own
  title and introduction, then the body of `AGENTS.md` from the first `##` heading on, which `config-check.py`
  keeps identical — a change to that body (a project block, too) is made in both files. `CLAUDE.md` is managed:
  it only imports `AGENTS.md` (`@AGENTS.md`), so Claude Code reads the same rules as every other agent.
- **Seeded** — created once and owned by the repository from then on: `.squad/stack.md`,
  `.squad/project.md`, `.squad/tools/squad_settings.py`,
  `.claude/settings.json`, `SECURITY.md`, `docs/UNIT_TESTS.md` and the stack profile's CI, CodeQL, Dependabot and tool
  configuration files.

## Squad lessons

Every lesson from step 12 is filed once, where it can actually be fixed:

| The lesson concerns | Filed as | Fixed by |
| ------------------- | -------- | -------- |
| a **managed** file, or the template part of a **marked** file (squad rules, agents, skills, tools, the template sections of `AGENTS.md`) | an issue labelled `squad` in the template repository named in `.squad/template.json` (`repository`), titled `[Squad] <lesson> (from <this repository>#<issue>)` and linking the run | a PR in the template repository, then a refresh of every repository that uses the template (`adopt-template`) — never a local edit, which the next refresh would overwrite |
| **project knowledge**: `.squad/stack.md`, `.squad/project.md`, `.squad/tools/squad_settings.py`, a `<!-- project:… -->` block, another seeded file | an issue labelled `squad` in this repository | a small squad-maintenance PR in this repository, checked with `.squad/tools/config-check.py` |

One run's lessons go into at most one issue per destination. A lesson that is general — one that would change
the template — is filed in the template repository, not in the product repository, and the session attaches
the template repository first if it is not yet part of the session (with the access it needs to create the
issue, e.g. `add_repo`). Only if attaching is refused (no access) it files that issue in this repository with
the label `squad-upstream` and tells the user, who moves it to the template repository; it is never worked
here. Once the template repository has fixed the lesson, the product repository adopts the change with
`adopt-template` (*Template-managed files*).

## Tiers

In step 2 the Lead classifies the change and justifies the tier in `plan.md` (for `docs` and `patch`, in its result). The
tier follows the change, not only the paths it touches. When in doubt, the higher tier applies (`docs` < `patch` <
`trivial` < `standard` < `security`); Security or the Reviewer may raise the tier at any point (never lower it).

| Tier | When | Pipeline |
| ---- | ---- | -------- |
| `docs` | Issues only (never a feature). The diff changes only product documentation: `README.md`, `SECURITY.md`, Markdown under `docs/` except `docs/decisions/` and `docs/areas/`, the project blocks of the marked files, and `.squad/stack.md` or `.squad/project.md` as documentation (see below). Any other file — a code comment, a config or CI file, a decision record, an area document — makes it `trivial` or higher. `scope-check.py --tier docs` checks exactly this. | Lead plans briefly (no `plan.md`: tier, exact edits and acceptance criteria go into its result, recorded as the first `log.md` row); the orchestrator applies the edits itself; `scope-check.py --tier docs` and *Format check* pass; one Reviewer round reads the diff against the first `log.md` row (no build) — a blocking finding is fixed, checked and delta-reviewed the same way; the orchestrator opens the PR once the latest round is clean. Steps 3–7 and 9 are skipped. |
| `patch` | Issues only (never a feature). The Lead can name the exact edits, and the diff stays within `PATCH_MAX_LINES` changed lines outside `specs/` (default 30, `squad_settings.py`): no change to production logic or control flow (an attribute, a literal, a condition rewritten equivalently, a configuration value, a script line), no new dependency, no new permission, no schema change. An edit in a security area, a CI or build file is allowed and makes the tier `patch/security`. `scope-check.py --tier patch` checks the line budget; the reviewer confirms that no logic changed and raises the tier otherwise. | Lead plans briefly as for `docs` (no `plan.md`: tier, exact edits, acceptance criteria and, per edit, the existing test or check that covers it — or the one test the Tester adds — go into its result, recorded as the first `log.md` row); the orchestrator applies the edits itself; when an edit to production code has no test that pins it, `squad-tester` writes the test the Lead named, once (the only extra launch). Step 7 runs only the gates of *Gates of a patch* below for the kinds of file the diff touches (the orchestrator runs *Format* on its own edits); one review round reads the diff against the first `log.md` row: `squad-reviewer`, or on `patch/security` `squad-security` in mode `diff` instead (not both) — a blocking finding is fixed by the orchestrator, checked and delta-reviewed the same way; the orchestrator opens the PR once the latest round is clean. No plan challenge, Security plan review, skeleton, tests first, Code Officer or step 9. |
| `trivial` | Documentation that does not qualify as `docs`, code comments, log or UI wording, configuration defaults, or a documentation change that needs a decision record — no change to behavior or control flow | Steps 3, 4 and 5 skipped (no Security, no tests-first); code check, Reviewer and PR approval (step 9) still run. Tests and coverage are still required if production code changes. |
| `standard` | A behavior change that touches none of the security areas below | Plan challenge in step 2 when the plan weighs options (*Plan challenge*); steps 3 and the Security review skipped — the Reviewer covers the security checklist and raises the tier if a security area is touched after all |
| `security` | Touches one of the security areas listed in `.squad/project.md` (*Security areas*), Docker/CI or build configuration, or adds/updates a dependency — beyond what `patch/security` allows | Full pipeline; the plan challenge (when the plan changes production code) and the Security plan review run in parallel, answered by one Lead revision |

`.squad/stack.md` and `.squad/project.md` describe the product, so an issue about them is a product PR and
counts as documentation for the `docs` tier — except an edit that changes *Security areas* or a command the
pipeline runs: that changes what the squad enforces and is `patch/security` when it fits that tier's budget,
`security` otherwise. A squad-maintenance PR (*Squad
lessons*) is only for lessons the squad itself raised, never for an issue a person filed about the product.

### Gates of a patch

A `patch` is small by definition, so its step 7 runs the checks its files need and nothing else; the
complete analysis, and for a `patch` without production code the full suite and the overall coverage
figure, are CI's job on the draft pull request (*Draft until green*). The orchestrator picks the rows that
match the diff and runs every gate they name, without judgment; when the diff spans two kinds, both rows
apply. Every `patch` ends with `scope-check.py --tier patch`.

| The diff touches | Gates |
| ---------------- | ----- |
| documentation only (Markdown, the project blocks) | *Format check* |
| a shell script, a workflow or a Dockerfile | *Analyzer gate* with `--lint-only` (shellcheck, actionlint, hadolint and the shell rules on the changed files, no build), plus the check the Lead's result names for the edit (a pin check, `bash -n`, a dry run) |
| a configuration file or a data file the product reads (YAML, JSON, a catalog) | the check the Lead's result names (a loader test, a schema check), run as *Test (project)* when it is a test |
| test code only | *Format check*, *Analyzer gate*, *Test (project)* for the test project(s) of the changed code (`stack.md`) |
| production code | *Format check*, *Analyzer gate*, *Test with coverage* and the *Coverage gate* — not *Test (project)*: it writes no coverage report, and the gate reads the reports of the full run, the overall figure included |

A gate the row names that the stack does not have (`stack.md` lists no *Test (project)*) is replaced by the
next broader one (*Test*); the orchestrator logs which rows applied.

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

### Criteria verified only in CI

A criterion that only CI can verify in full — a container image the sandbox cannot build, a release build
on another platform — is still checked here as far as the sandbox allows, before the review and not after
the PR. A change that touches an input the image build is sensitive to (the project or manifest files, a
content item the SDK discovers at restore time, a static web asset, the Dockerfile itself; *Image build
check* in `.squad/stack.md` names them for the stack) runs the *Image build check* from `stack.md`, which
reproduces the Dockerfile's own order — manifests first, restore, then the sources, build or publish
without a restore — and the plan names that check as the criterion's verification with the Dev as its
owner. A build or publish from the full working tree does not count: it restores with every source present
and can hold an output the image's restore from the project files alone never produces (Vandox#24: a
framework script the plan had seen in a full-tree publish was missing from the image, and only the CI
release build caught it after the PR was open). A red CI on such a criterion after that check is worked in
step 11 as usual.

### Code a unit test cannot cover

A file of the change that no unit test can cover — a browser script the stack has no test runner for, a
generated file, process startup glue — is found in the plan, not after the PR: the Lead lists such files in
`plan.md` with the reason, the Tester confirms or contests the list in its report, and the Lead's decision
is a decision record. Such a file is excluded from the coverage **measure** only, in every place that
computes it (`sonar.coverage.exclusions` for the CI code analysis, and `COVERAGE_EXCLUDES` in
`.squad/tools/squad_settings.py` where `COVERAGE_PATHSPECS` includes the file; *Coverage exclusions* in
`.squad/stack.md` names where the repository sets them) — never from the analysis (`sonar.exclusions`, a
suppression): its rules still apply, and those no local gate reports arrive in step 11 (Vandox#24: own
browser scripts without a Node build counted as new code without coverage, 66.8 %, and failed the CI
quality gate after the PR was open).

## Pipeline

| # | Step | Owner | Exit condition |
| - | ---- | ----- | -------------- |
| 1 | Intake | Orchestrator | Branch off `main`, work folder and `log.md` created (rows appended with `squad-log.py`), committed and pushed |
| 2 | Plan | Lead, Devil's Advocate | `plan.md` with tier, acceptance criteria, signatures of new/changed API, the options considered, affected areas (`docs/areas/`), doc updates; decision records `Proposed` and indexed. Or outcome **no change** (see below). `standard`/`security`: one plan challenge by the Devil's Advocate when *Plan challenge* below calls for it; major objections answered by the Lead in the plan's *Challenge* section, minor ones recorded as *Challenge notes* |
| 3 | Plan security review | Security | `security` tier only, launched together with the plan challenge on the same plan commit. `APPROVED` → 4; `CHANGES_REQUIRED` → one Lead revision answers it together with the major objections, then a Security delta confirmation of the revision |
| 4 | Skeleton | Dev | Only when the plan adds or changes API: compile-only signatures built as *Skeleton* in `.squad/stack.md` describes, *Build* passes |
| 5 | Tests first | Tester | Only the tests the plan names for its acceptance criteria (one or a few per criterion); they compile and **fail** on the current code or the skeleton. A test that cannot fail yet is not written here: guards, pins of existing behavior, determinism and bound tests wait for step 6 |
| 6 | Implementation + coverage | Dev, Tester | All tests green; *Coverage gate* from `.squad/stack.md` passes; doc updates from the plan done, including the area documents. The Tester's coverage pass adds tests only for lines the gate reports uncovered, a resource bound the plan names, or a test a reviewer asks for |
| 7 | Code check | Orchestrator, Code Officer | Dev and Tester ran *Format* on their files before handing back. The orchestrator runs *Format check*, *Analyzer gate*, *Test*, *Coverage gate* and `scope-check.py`. A *Format check* failure the orchestrator clears itself (*Format*, then `git restore` of every file outside the change); only analyzer diagnostics or style findings that need reading launch the Code Officer (analyzer-only edits; structural items go to the Dev or Tester), then the gates run again. Exit: every gate passes on the head, same tests green |
| 8 | Review | Reviewer (+ Security on `security`) | They get the code SHA and the orchestrator's gate output of step 7 (they do not re-run the gates); later commits during the round may touch only `specs/` and do not make it stale. No blocking findings → 9; blocking → owner fixes (Dev: code, Tester: tests), back to 6, then a mandatory delta round — except a finding, blocking or not, whose fix is a verbatim documentation edit: the orchestrator applies it (*Verbatim documentation fixes*), no launch, no build, no delta round |
| 9 | PR approval | Orchestrator, Lead when needed | The orchestrator checks: the latest review round is clean and covers every change to production code, tests and `docs/` since it ran (only `specs/` bookkeeping and the verbatim documentation fixes of that round, *Verbatim documentation fixes*, may follow it); the gates of step 7 pass on the head; the area documents and documentation updates the plan names are in the diff; then sets each `Proposed` record of this change and its index row to `Accepted` (`decision-check.py` passes). The Lead is launched in mode `approve-pr` only after this checklist passed in full (a due delta round runs first) and only when a decision is open: a plan deviation in a member's report, a Lead decision recorded during steps 3–8 (loop limit, dispute, accepted gap, finding accepted unfixed), non-blocking findings that touch code and are not yet decided, or a change to `docs/ARCHITECTURE.md` or `.squad/project.md` that the plan does not name or the Reviewer did not find consistent. Otherwise "approved by checklist" is logged → 10 |
| 10 | Pull request | Orchestrator | Working record posted as comment (a long `plan.md` may be given as a permalink to the last commit that contains it plus a summary), `specs/<folder>/` removed and `scope-check.py --no-specs` clean, PR opened **as a draft** (*Draft until green*; merged later with *Squash and merge*) |
| 11 | After the PR | Dev, Code Officer, Reviewer | No merge conflict with the base (*Merge conflicts*), CI green and the CI code analysis (e.g. SonarQube Cloud) passed with no new issue on the head, then the PR marked ready for review (*Draft until green*); review comments worked |
| 12 | Wrap-up | Orchestrator | Squad lessons filed as one issue per destination (*Squad lessons*), or "no lessons" logged; run metrics (launches with their continuations, tokens, tool uses and seconds per role, plus the Orchestrator row: wall time, time not covered by launches, gate runs — `squad-log.py --summary`; exit code 3 names launch rows still without usage, which are amended first with `--amend-last`, `--step` for an earlier row, or marked `--unreported`; the table is posted as printed, never summed by hand or left out) appended to the working record and reported to the user |

A change without production or test code (tier `docs`, or any other tier whose plan declares it, see
*Changes without production or test code*) skips steps 4 and 5; step 6 is the Dev's edits alone (tier
`docs`: the orchestrator's), and the *Coverage gate* is not run, because such a change cannot alter
coverage. Step 7 still runs *Format check*, the *Analyzer gate* and `scope-check.py`; for tier `docs` only
*Format check* and `scope-check.py --tier docs`. Tier `patch` runs only the steps its row in *Tiers* names.

**Continuing a member.** A fix round or a delta round of the same member within the same run continues
that member's last launch (the harness keeps a launched subagent's context: `SendMessage` to its agent id,
or the Agent tool's resume) with the findings and the new head SHA, instead of a fresh launch that reloads
`AGENTS.md`, `stack.md`, `project.md`, the plan and the code. A fresh launch stays the rule for the first
launch of a mode, after a session restart (the agent id is gone) and when the previous launch ended on an
API error. The log records a continuation as a launch of that member (`squad-log.py … --continued`): the
notification reports the agent's tokens since its first launch but its tool uses and duration for the round
alone, so the script subtracts the earlier tokens only, writes this round's share and marks the trailer; the
summary counts every token once and shows how many launches were continuations. When a member ran several
task groups in fresh launches and the agent of an earlier one is continued, log it with
`--continued-from "<step cell of that launch>"`, so the tokens of the right agent are subtracted.

Each continuation reloads the agent's whole conversation, so its cost grows with every round. Once an agent
has been continued five times or its reported tokens pass 300,000, the next round is a fresh launch with a
short hand-over instead — the plan or task group, what the earlier rounds settled, the open findings and the
head SHA — which is cheaper than another continuation.

Commits and pushes to the work branch happen right after intake (`specs/<folder>/log.md`, so a stop hook
or a crashed session finds no untracked files) and after every further completed step; with *Squash and
merge* only the PR title and description reach `main`, so intermediate commits may describe the step.
They never contain secrets. A stop hook may demand a commit while a member is still working; that interim
commit ("Work in progress: …") is fine, and the commit that closes the round gets a final subject. A member
whose edits were committed that way finds a clean `git status` when it hands back; that is expected, and it
reports its changes against the head SHA it was given (`git diff <sha>`), not against the working tree. Before
the PR such commits cost nothing; after it every push re-triggers CI and the code analysis, so edits are
batched and committed once per round. After the PR the orchestrator waits for the running member's hand-back
before it ends its turn; when a hook still forces a push of a half-finished tree, that head is logged as work in
progress and neither CI nor the code analysis is judged on it.

### Draft until green

Every pull request — from the squad skills, `create-pr` or `adopt-template` — is opened as a **draft**
(`draft: true`). It stays a draft while anything on its head is still to be fixed, and is marked ready for
review (`update_pull_request` with `draft: false`) only when all of this holds on the current head:

- the pull request merges cleanly into its base branch (*Merge conflicts* below);
- every CI check run and commit status has finished, and none failed or was cancelled (a pending check is
  waited for, not counted as passed);
- the CI code analysis (e.g. SonarQube Cloud) has reported on **this** head, its quality gate passed and it
  lists **no new issue** on the pull request — a passed gate with open new issues (bugs, code smells,
  security hotspots to review) is still rework, worked like a failing check. "This head" is checked, not
  assumed: the commit SHA of the analysis (SonarQube Cloud: `api/project_pull_requests/list?project=<key>`,
  or the `head_sha` of its check run) equals the PR head; a `Quality Gate passed` comment or an issue list
  without that match may belong to an earlier head while the newest analysis still runs, and settles
  nothing about this one;
- no commit for a known failure or finding is still to be pushed, and after a fix in the squad skills the
  re-approval of step 9 has passed on this head.

Each failure is fixed on the branch as step 11 describes, and the check starts again on the new head. When
a check fails for reasons outside the change (red on the base branch too), or the code analysis cannot be
read, the PR stays a draft and the user is told what blocks it. Once ready, the PR is not turned back into
a draft; review comments and later CI failures are worked as before.

### Merge conflicts

A pull request with a conflict against its base branch gets no merge commit, so CI usually never starts on
it: its checks stay missing or pending and waiting for them waits forever. Every look at the PR — after
opening it, on each PR event, on each check-in and before marking it ready — therefore reads its
mergeability first (`pull_request_read`, method `get`): `mergeable: false` or `mergeable_state: "dirty"` is a
conflict; `mergeable: null` means GitHub is still computing it, so read it again shortly instead of counting
it as clean. Checks that have not started a few minutes after a push are a reason to read it again, not to
keep waiting.

A conflict is fixed before anything else on the PR: fetch the base (`git fetch origin main`), merge it into
the work branch (`git merge origin/main`, never a rebase or force-push), resolve each conflict keeping the
intent of both sides, regenerate lockfiles and generated files with the stack's tooling instead of editing
them by hand, run the gates the merged files need (*Gates of a patch* names them by kind of file) and push
the merge commit. In the squad skills the orchestrator runs the merge and commits; a conflict in production
or test code is resolved by the member that owns it (Dev or Tester), and a resolution that rewrites code
rather than taking both sides unchanged is a fix that goes through steps 7–8 again. When both sides changed
the same logic and either choice loses behavior, the Lead decides (the user, outside the squad skills). The
check of *Draft until green* then starts again on the new head.

While a merge waits for the owning member's resolution, the working tree holds conflict markers and is not
committed: a stop hook that asks for a commit meanwhile is answered with that explanation (merge in progress,
resolution pending with the named member), and the merge commit follows once the conflicts are resolved and
the gates pass. A record number that `main` took in the meantime is renumbered on the work branch — the
record file, its index row and every link to it, in records, area documents and the docs — and
`decision-check.py` must pass on the merged head (it reports a number used twice and a link to a record file
that does not exist).

A long run does not wait for the PR to meet a moved base: before the first review round (step 7) and again
before PR approval (step 9), the orchestrator fetches `main` and merges it when the work branch lacks its
latest commit (`git merge-base --is-ancestor origin/main HEAD` fails), so the reviewers see the merged code and
the security statements of the change are checked against what `main` holds now. A merge that took both sides
unchanged needs the gates of step 7 again, not a new review round; a resolution that rewrote code, or a
statement of the change that the merged `main` made untrue, goes through steps 7–8 as a fix.

## Plan challenge

The Devil's Advocate is launched only when there is something to challenge: the plan changes production code
**and** either weighs more than one option (its *Options considered* names an alternative, or it adds or
extends a decision record) or the tier is `security`. Otherwise the orchestrator logs "challenge skipped: no
options" and goes on; the Reviewer still checks the plan's assumptions in step 8.

The Devil's Advocate classifies every objection as `major` (the plan would build the wrong thing or miss the
defect) or `minor`, each with one sentence why, so the orchestrator routes without judgment:

- **Only minor objections** (on `security`: and Security approved): no Lead launch. The orchestrator appends them verbatim to `plan.md` as a
  *Challenge notes* section (bookkeeping, like `log.md`); Dev and Tester follow them or report a deviation,
  and the Reviewer checks them in round 1.
- **A major objection:** the Lead is launched in mode `revise` with all objections and answers each one in
  the plan's *Challenge* section, the minor ones included.

On `security` the plan challenge and the Security plan review (step 3) run **in parallel** on the same plan
commit; one Lead revision then answers the major objections and the `CHANGES_REQUIRED` findings together,
followed by one Security delta confirmation of the revision. When Security approves and the objections are
minor, neither the Lead nor Security is launched again.

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
shares). In steps 2–3, `squad-devils-advocate` and `squad-security` (mode `plan`) may run together, and in
step 8 `squad-reviewer` and `squad-security`, because they are read-only and the reviewer builds in a
scratch copy. No member experiments (mutation tests,
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

- **Plan challenge (step 2):** at most one Devil's Advocate round, no veto. Minor objections alone go into
  the plan's *Challenge notes* without a Lead launch; after a major one the Lead answers every objection
  (accept and revise, or reject with a reason); a rejected objection is not raised again.
- **Plan ↔ Security (steps 2–3):** at most 2 rejections; on `security` the first revision answers the plan
  challenge and the first `CHANGES_REQUIRED` together. After the 2nd `CHANGES_REQUIRED` the Lead
  decides: narrow the scope, split into separate issues, escalate to the Product Manager, or — when the
  remaining defect is a pure wording defect, or a defect confined to the plan's resource section (limits,
  bounds, sizes) whose fix changes no scope — accept and fix it, followed by exactly one Security delta
  confirmation of the fix (a further rejection then goes to the Product Manager).
- **Review ↔ Dev (steps 6–8):** review pass 1 is a full review; at most **2 further fix-and-review
  rounds**, each reviewing only the delta. Blocking findings still open after that go to the Lead, who
  decides: accept with justification, split into a follow-up issue, abort, or escalate. A round is counted
  per review round that returned blocking findings, not per launch: everything done to resolve that round's
  findings — the Dev's fix, and a test or test double the Tester repairs because the fix exposed it as wrong —
  is one fix round, followed by one delta review. A defect a reviewer finds in a test is a finding of its
  round like any other and counts the same way.
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
  Lead, whose decision is recorded in `log.md`. A whole file no unit test can cover was listed in the plan
  (*Code a unit test cannot cover*); one that surfaces only here, or only in the CI code analysis, is a
  plan defect the Lead records with the decision.
- **After the PR (step 11):** CI or quality-gate failures and review comments are fixed on the same
  branch and go through steps 7–8 again (delta review). The same limit of 2 fix rounds applies per
  failure; after that the Lead decides.

## Escalation to the Product Manager

The Lead escalates only when it cannot decide responsibly on its own: the requirement is ambiguous (a
parser, decoder or reader of an external format without a real sample of that format in the test data or
on the issue is one — the Lead asks for the sample, or for the confirmation to build from the description
with the risk recorded, before it plans), the fix needs a product decision (behavior change visible to
users, breaking a documented guarantee in `docs/ARCHITECTURE.md` or an accepted decision record), or a loop
limit was hit and none of the Lead's options is clearly right. The escalation is one concise question with the options and the Lead's
recommendation. Follow-up GitHub issues the Lead decides on are created by the orchestrator and listed in
the PR under Next Steps.

## Verbatim documentation fixes

A review finding — blocking or non-blocking — whose fix the reviewer states verbatim (file, the sentence as it
stands, the sentence as it should read, marked `verbatim` in the report) and that touches only documentation,
a decision record, an area document, `README.md` or a configuration example (never production or test code)
is applied by the orchestrator exactly as proposed (as in tier `docs`), checked with `git diff` to contain
nothing else, and logged with file and commit. It needs no Dev, Tester or Lead launch, no build and no
confirming delta round: the text is the reviewer's own, from the reviewer's own round (Vandox#194 and
Vandox#19 each spent an Opus launch on confirming two commas and one sentence; in the 14 Vandox runs after
that, 6 of 8 blocking findings were such documentation edits, each worked by a Dev launch, a gate run and a
delta round). It is read again only when a delta round is due anyway for another change; any edit beyond the
proposed text, and a fix the reviewer cannot state verbatim, is an ordinary fix for its owner with a delta
round. One exception: a fix that changes a behavior statement an acceptance criterion or a guarantee in
`.squad/project.md` relies on is not applied; the reviewer says so, and the Lead decides (mode `decide`).
The step 7 gates that apply to documentation (*Format check*, `scope-check.py`, `decision-check.py`) run
before the PR; the blocking finding counts as resolved once the text is in and logged.

## Non-blocking findings

Fixed in the same change or opened as a linked GitHub issue now — never deferred to "a later change".

- A non-blocking finding with a verbatim documentation fix goes the way of *Verbatim documentation fixes*
  above: applied by the orchestrator, no launch, no delta round.
- Every other non-blocking finding — one that touches code, or one the orchestrator would turn into a
  follow-up issue — is decided by the Lead in step 9 (one launch for all of them); a finding fixed now gets
  a delta round.
