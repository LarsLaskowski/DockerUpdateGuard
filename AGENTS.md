# AGENTS.md

Project guidance for Codex/GPT and other agents that read `AGENTS.md` when working in this repository. These rules mirror `CLAUDE.md` and `.github/copilot-instructions.md`; keep all three in sync —
everything from the first `##` heading on is identical in all three files. This file is a summary; the
binding, detailed references are [`ARCHITECTURE.md`](/docs/ARCHITECTURE.md) (how the system is put
together and why), [`CONTRIBUTING.md`](/docs/CONTRIBUTING.md) (workflow, PR conventions, versioning),
[`UNIT_TESTS.md`](/docs/UNIT_TESTS.md) (test conventions — **unit tests are mandatory for new code**) and
[`.squad/stack.md`](/.squad/stack.md) (toolchain and commands). Read them before making a non-trivial
change; when this file and one of them appear to disagree, treat that as a sync bug to fix, not as
license to pick either one.

## What this project is

<!-- project:begin overview -->
DockerUpdateGuard is an ASP.NET Core Razor Components (Blazor Server) web application that tracks what is
actually running in Docker, compares it with registry metadata, and shows where updates, vulnerabilities and
shared base-image dependencies need attention. It runs as a Docker container against PostgreSQL.

[`ARCHITECTURE.md`](/docs/ARCHITECTURE.md) is the binding architecture reference: solution layout, composition
root and startup sequence, configuration model, data layer, integration clients, the background scan engine,
the UI layer, telemetry, security posture, and the CI/deployment pipeline. Read it before making structural
changes (new projects, new background jobs, new integration clients, changes to the composition root or entity
model) and update it in the same change whenever it goes out of date — it must never contain open questions
or stale claims.
<!-- project:end overview -->

## Golden rules

- **Never commit or push to `main`** — no one, not even with approval. Every change goes through a
  separate branch and a pull request.
- **Commits and pushes to a feature branch are always allowed** without asking: commit finished work and
  push it to the current feature branch (creating that branch off `main` if needed), so nothing is lost
  when a session ends. Force-pushing or otherwise rewriting published history, deleting branches, and
  creating tags (a `v*` tag may trigger a release) still need explicit user approval.
- **Pull requests are only opened by the squad or by the user.** The `squad-issue` and `squad-spec`
  skills open a PR after the Lead's approval (tier `docs`: after a clean review); outside the squad, a PR
  is opened only when the user explicitly asks for one (e.g. by running the `create-pr` skill). Never open
  a PR on your own initiative.
- Run *Format* from [`.squad/stack.md`](/.squad/stack.md) after editing code and before building; CI is
  not meant to find formatting issues. In the squad skills only the Code Officer runs it.
- A changed file may not carry **any analyzer diagnostic of any severity**, including info-level ones that
  never show up as build warnings but that the CI code analysis (e.g. SonarQube Cloud) reports. Check with
  the *Analyzer gate* from `.squad/stack.md` and fix every finding before considering the work done (in
  the squad skills, the Code Officer owns this).
- New or changed production code needs **at least 80 % line coverage**, and overall coverage must stay
  at least 80 % (*Coverage gate* in `.squad/stack.md`, see [`UNIT_TESTS.md`](/docs/UNIT_TESTS.md#code-coverage)).
<!-- stack:begin golden-rules -->
- Add new packages via **Central Package Management** (`Directory.Packages.props`); do not put version
  numbers in individual `.csproj` files.
- Every C# project uses the **Reihitsu.Analyzer** and the **SonarAnalyzer.CSharp** rules, so SonarQube
  issues surface in the local build, not first in the CI analysis. A build must finish with **zero
  Reihitsu (`RH####`) warnings and errors**.
- Wrap every type's members in `#region` blocks **as you write the code** — never leave a type
  un-regioned and never add the regions only after an analyzer warning.
<!-- stack:end golden-rules -->

## Commit messages

- Keep the subject line to a single summary of **no more than 80 characters** and do not end it with a
  period.
- Do not write the message in the first person.
- Keep the body to **3–5 sentences**, depending on the number of changes.

## Pull requests

- Title and description are always written in **English**, regardless of the language used in the
  conversation.

## Commands

<!-- stack:begin commands -->
Run from the repository root, where the solution file lives (exact commands, with the solution name, in
`.squad/stack.md`):

```bash
dotnet restore
reihitsu-format ./                                          # dotnet tool install -g Reihitsu.Cli
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
python3 .squad/tools/analyzer-check.py                      # analyzer gate
python3 .squad/tools/coverage-check.py                      # coverage gate, after a coverage run
```
<!-- stack:end commands -->

All commands, with what each one checks, are listed in [`.squad/stack.md`](/.squad/stack.md).

## Architecture

<!-- project:begin architecture -->
- `src/DockerUpdateGuard` — main ASP.NET Core host (`Microsoft.NET.Sdk.Web`); composition root; references the
  data and telemetry projects. Web startup and dependency wiring stay here.
- `src/DockerUpdateGuard.Data` — data-access layer; EF Core with PostgreSQL (`Npgsql.EntityFrameworkCore.PostgreSQL`).
- `src/DockerUpdateGuard.Telemetry` — shared observability layer; OpenTelemetry hosting, OTLP export, ASP.NET
  Core / HTTP / runtime instrumentation.
- `src/Tests/DockerUpdateGuard.Tests` — tests for the host/application layer; EF Core InMemory + NSubstitute.
- `src/Tests/DockerUpdateGuard.Data.Tests` — tests for the data layer; EF Core SQLite.
<!-- project:end architecture -->

## Project configuration

<!-- stack:begin configuration -->
- **Target framework** as set in the project files (see `.squad/stack.md`); **nullable reference types**, **implicit usings**, and
  **documentation XML** generation are all enabled.
- **Central Package Management** via `Directory.Packages.props`; never put versions in individual
  `.csproj` files.
- **Reihitsu.Analyzer** and **SonarAnalyzer.CSharp** are dev dependencies in every project (via
  `Directory.Build.props`).
- **Solution format** is `.slnx` (XML-based) at the repository root.
<!-- stack:end configuration -->
<!-- project:begin configuration -->
- **Target framework** `net10.0` in the runtime projects; one solution, `DockerUpdateGuard.slnx`, at the
  repository root.
- Runtime projects disable generated assembly info and link `SharedAssemblyInfo.cs` from the repository root.
- Runtime and test projects use per-configuration rulesets from `rules/DockerUpdateGuard.Debug.ruleset` and
  `rules/DockerUpdateGuard.Release.ruleset`; rules SonarAnalyzer.CSharp disables by default (e.g. `S3776`) are
  enabled there.
- SonarCloud rule suppressions are centralized in the shared `src/GlobalSuppressions.cs` (linked into each
  project), never scattered per file.
- Tests live under `src/Tests`, not a top-level `tests` folder; keep new test projects there.
- EF Core migrations follow the SeriesOverwatch pattern: the first migration is `InitialCreate`, later ones
  `Update1`, `Update2`, …; migration files are renamed to drop the timestamp prefix, while the generated
  `[Migration("yyyyMMddHHmmss_Name")]` attribute stays unchanged.
<!-- project:end configuration -->

## Code style

<!-- stack:begin code-style -->
File-scoped namespaces; one top-level type per file; `using` outside namespace (System first); Allman
braces, always required; 4-space indent; `var` preferred; language keywords over BCL types; LINQ method
syntax only; `== false` instead of `!`; `is null` / `is not null`; no primary constructors; constructor
injection with `_camelCase` readonly fields; `#region` blocks grouped by member kind (an interface's
region named after the interface, its description not ending in "implementation"); XML docs on all
members (English, no `<remarks>`); `.ConfigureAwait(false)` in library/service code.
<!-- stack:end code-style -->
<!-- project:begin code-style -->
The detailed C# code-style rules (naming, regions, formatting, XML docs, null handling, suppressed analyzer
rules) in [`.github/instructions/csharp.instructions.md`](/.github/instructions/csharp.instructions.md) are
binding; together with *Writing code* in `.squad/stack.md` they describe one set of rules (a conflict between
them is a sync bug to fix):

@.github/instructions/csharp.instructions.md

- CRLF line endings and no final newline (`.editorconfig`).
- Tests: MSTest's `Assert` / `CollectionAssert` (no FluentAssertions); **NSubstitute** is the mocking library
  here, used only where a collaborator crosses an infrastructure boundary; EF Core InMemory / SQLite for data
  tests (`docs/UNIT_TESTS.md`). Test classes `{TypeUnderTest}Tests`, methods
  `{TypeUnderTest}{Scenario}{ExpectedResult}`, always with assertion messages.
<!-- project:end code-style -->

## Testing

<!-- stack:begin testing -->
**Unit tests are mandatory for newly written code.** MSTest with its own `Assert` / `CollectionAssert` (no
FluentAssertions); test doubles as `.squad/project.md` (*Test doubles*) and `docs/UNIT_TESTS.md` prescribe —
real objects and hand-written fakes/stubs unless the project names a mocking library. Classes
`{TypeUnderTest}Tests`, methods `{Class}{Scenario}{ExpectedResult}` in PascalCase **without underscores**;
always pass an assert message.
<!-- stack:end testing -->
Full conventions, including the project's test doubles and the checklist to run before committing a new
test, are in [`UNIT_TESTS.md`](/docs/UNIT_TESTS.md).

## Related skills

Project-specific workflow skills live under `.claude/skills/`, mirrored identically under
`.agents/skills/` (Codex/GPT) and `.github/skills/` (GitHub Copilot):

- `create-pr` — verify (format, build, tests, analyzer and coverage gates), review the change locally,
  then open a PR following [`.github/pull_request_template.md`](/.github/pull_request_template.md).
- `squad-issue` — fix a GitHub issue with the squad: the Lead plans and picks a tier
  (`docs` / `trivial` / `standard` / `security`), the Devil's Advocate challenges `standard`/`security`
  plans once, Security reviews security-relevant plans, the Tester writes failing tests first, the Dev
  implements to ≥ 80 % coverage, the Code Officer clears format and analyzer diagnostics, Reviewer and
  Security review the diff, the Lead approves, then a PR referencing the issue is opened.
- `squad-spec` — the same squad pipeline for a new feature, planned as `spec.md`, `plan.md` and
  `tasks.md` in a working folder under `specs/`.
- `review-pr` — review an open pull request against this project's stack, analyzer, security and
  unit-test conventions, and post the findings with an explicit verdict.

Review runs as a subagent defined in `.claude/agents/squad-reviewer.md` (read-only, pinned to Opus, fresh
context). `create-pr` and the squad skills call it *before* pushing, so a change is reviewed while it is
still local; `review-pr` calls the same agent for a pull request that is already open. The review
checklist, the integration-surface sweep, the blocking/non-blocking severity model and the "round 1 is a
full review, later rounds review only the delta" rule live in that one file, so they are identical either
way. An agent without subagent support follows the same file inline.

The squad skills run a multi-role pipeline defined in [`.squad/`](/.squad/team.md) — Lead (plan, decisions,
PR approval), Devil's Advocate (one plan challenge), Security (plan and diff), Tester (tests first,
coverage), Dev, Code Officer (format, analyzers) and Reviewer — as subagents under
`.claude/agents/squad-*.md`, with the loop limits and escalation rules in
[`.squad/routing.md`](/.squad/routing.md). Stack commands live in [`.squad/stack.md`](/.squad/stack.md),
the project's guarantees, security areas and integration surface in
[`.squad/project.md`](/.squad/project.md). Their working records (`plan.md`, `log.md`, for features also
`spec.md` and `tasks.md`) live under `specs/` on the work branch only; before the PR they are posted as a
comment on the issue and removed, so `main` keeps no working records. An issue or feature PR never changes
the squad or these instructions (`.squad/` except `stack.md` and `project.md`, `.claude/`,
`.github/skills/`, `.agents/skills/`, `CLAUDE.md`, `AGENTS.md`, `.github/copilot-instructions.md`): squad
lessons are filed as GitHub issues labelled `squad` and never fixed in a product PR. The squad and these
rules come from the template repository named in `.squad/template.json`: a lesson about a template-managed
file becomes an issue there and is rolled out with its `adopt-template` skill; a lesson about project
knowledge (`.squad/stack.md`, `.squad/project.md`, a project block) becomes an issue here and is worked in
a squad-maintenance PR checked with `python3 .squad/tools/config-check.py` (`.squad/routing.md`,
*Squad lessons*). The user acts as Product Manager
and is only asked when the Lead escalates. Pull requests are merged with *Squash and merge*, so only the
PR title and description reach `main`.

The reasoning behind code decisions — why something was built the way it was — is recorded by the Lead
as one decision record per decision in [`docs/decisions/`](/docs/decisions/README.md) (append-only,
superseded rather than rewritten), not in `ARCHITECTURE.md`. Read the relevant records before changing
code they cover, and do not contradict an accepted record without superseding it.

Two rules these skills enforce that are easy to get wrong:

- **A pull request documents the change, not how it was produced.** The internal review loop — its
  pass count, its findings, the commits that resolved them — never appears in the PR title, body or
  commit messages.
- **A finding posted as a review comment gets worked in that pull request**, blocking or not. It is
  never deferred to "the next change that touches this code": no such change is scheduled, and the
  session holding the context to act on it will not exist later. If it really should not be fixed
  here, reply with the reason or open a linked issue now — then resolve the thread.

## Pull requests, contributing and architecture

Follow [`CONTRIBUTING.md`](/docs/CONTRIBUTING.md) for branch/PR naming (`[area] Description`), the PR
checklist in [`.github/pull_request_template.md`](/.github/pull_request_template.md), and the
stability policy. Consult [`ARCHITECTURE.md`](/docs/ARCHITECTURE.md) before changing the behavior it
describes — the guarantees listed in [`.squad/project.md`](/.squad/project.md) are deliberate, not
incidental behavior.
