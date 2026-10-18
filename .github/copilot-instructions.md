# Copilot instructions

Project guidance for GitHub Copilot when working in this repository. These rules mirror
[`AGENTS.md`](/AGENTS.md): everything from the first `##` heading on is generated from it by the template's
`adopt-template` skill (Claude Code's `@` import lines aside — Copilot reads
`.github/instructions/*.instructions.md` through their `applyTo` front matter), and
`.squad/tools/config-check.py` fails when the two differ. Change both files together. This file is a
summary; the binding, detailed references are [`ARCHITECTURE.md`](/docs/ARCHITECTURE.md) (how the system is
put together and why), [`CONTRIBUTING.md`](/docs/CONTRIBUTING.md) (workflow, PR conventions, versioning),
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
  skills open a PR once it is approved (`.squad/routing.md`, *PR approval*); outside the squad, a PR is
  opened only when the user explicitly asks for one (e.g. by running the `create-pr` skill). Never open a PR
  on your own initiative.
- **A change made outside the squad that touches a security area** listed in
  [`.squad/project.md`](/.squad/project.md) (*Security areas*) is reviewed before it is merged: `create-pr` adds
  Security's diff review to its review loop, and a pull request opened another way (by hand, from a plain
  session) gets a `review-pr` run whose findings are worked in that pull request.
- Run *Format* from [`.squad/stack.md`](/.squad/stack.md) after editing code and before building; CI is
  not meant to find formatting issues. In the squad skills the member that changed the files runs it on
  them before handing back (`.squad/team.md`, *Shared rules*).
- A changed file may not carry **any analyzer diagnostic of any severity**, including info-level ones that
  never show up as build warnings but that the CI code analysis (e.g. SonarQube Cloud) reports. Check with
  the *Analyzer gate* from `.squad/stack.md` and fix every finding before considering the work done (in
  the squad skills, the Code Officer owns this).
- New or changed production code needs unit tests until the *Coverage gate* in `.squad/stack.md` passes:
  **at least 80 % line coverage** on new or changed lines and overall (the thresholds live in
  `.squad/tools/squad_settings.py`, see [`UNIT_TESTS.md`](/docs/UNIT_TESTS.md#code-coverage)).
<!-- stack:begin golden-rules -->
- Add new packages via **Central Package Management** (`Directory.Packages.props`); do not put version
  numbers in individual `.csproj` files.
- Every C# project uses the **Reihitsu.Analyzer** and the **SonarAnalyzer.CSharp** rules, so SonarQube
  issues surface in the local build, not first in the CI analysis. A build must finish with **zero
  Reihitsu (`RH####`) warnings and errors**.
- Wrap every type's members in `#region` blocks **as you write the code** — never leave a type
  un-regioned and never add the regions only after an analyzer warning.
<!-- stack:end golden-rules -->

## Commit messages and pull requests

- Commit subject: one summary of **no more than 80 characters**, no trailing period, not in the first
  person. A body only where the subject does not explain the change; pull requests are squash-merged, so
  branch commits never reach `main` and may simply name the pipeline step.
- Pull request title `[area] Description` (areas in `docs/CONTRIBUTING.md`), description of **3–5
  sentences** on what changed and why: they become the commit on `main`.
- Everything written to the repository or to GitHub — code, comments, documentation, commit messages, PR
  titles and descriptions, review comments — is in **English**, regardless of the conversation's language.

## Commands

<!-- stack:begin commands -->
Run from the repository root, where the solution file lives, in this order: *Restore*, *Format*, *Build*,
*Test*, *Analyzer gate*, and after *Test with coverage* the *Coverage gate*. Use the commands of those names
in `.squad/stack.md` as they stand, never a copy from memory: they carry the solution name, the build
configuration the project tests in, and the formatter's `--force` (`reihitsu-format` asks for confirmation
for more than 25 files and, without a terminal, formats nothing). The formatter is installed with
`dotnet tool install -g Reihitsu.Cli`.
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
always pass an assert message. Timing through an injected `TimeProvider`, never the real clock (the one
exception, a time bound tested as the ratio of two durations, never an absolute one, is in
`docs/UNIT_TESTS.md`).
<!-- stack:end testing -->
Full conventions, including the project's test doubles and the checklist to run before committing a new
test, are in [`UNIT_TESTS.md`](/docs/UNIT_TESTS.md).

## Related skills

Project-specific workflow skills live under `.claude/skills/` and run in Claude Code:

- `create-pr` — verify (format, build, tests, analyzer and coverage gates), review the change locally,
  then open a PR following [`.github/pull_request_template.md`](/.github/pull_request_template.md).
- `squad-issue` — fix a bug reported in a GitHub issue with the squad: the Lead plans and picks a tier
  (`docs` / `patch` / `trivial` / `standard` / `security`), the Devil's Advocate challenges a plan once when
  `.squad/routing.md` calls for it, Security reviews the plan whenever a security area is touched and the
  diff of a change to a security control, the Tester writes failing tests first, the Dev implements until the
  *Coverage gate* passes, the Code Officer clears analyzer diagnostics, the Reviewer reviews the diff, and
  once the PR is approved by checklist (the Lead decides what is open) a PR referencing the issue opens.
- `squad-spec` — the same squad pipeline for a new feature, spec first: the Lead writes `spec.md` (scenarios,
  edge cases, the acceptance criteria) and the Devil's Advocate challenges it before `plan.md` and `tasks.md`
  are written against it, in a working folder under `specs/`; also for an issue the user calls a feature or that is
  labelled `feature`/`enhancement` (`.squad/routing.md`, *Bug or feature*).
- `decision-consolidate` — merge unreleased decision records (Superseded chains, records on one topic) into
  one record each, delete the obsolete ones, lift behavior that sits in records into the area documents and fix
  links and index; released records stay untouched.
- `review-pr` — review an open pull request against this project's stack, analyzer, security and
  unit-test conventions, and post the findings with an explicit verdict.

Every review — before the push in `create-pr` and the squad skills, on an open pull request in `review-pr` —
runs the read-only subagent `.claude/agents/squad-reviewer.md`, which holds the checklist, the severity model
and the round semantics. The squad's members (`.squad/team.md`) run as subagents under
`.claude/agents/squad-*.md`; a hook denies them Git and GitHub writes, so only the orchestrating session
commits, pushes and posts. The pipeline, its limits and escalations are in [`.squad/routing.md`](/.squad/routing.md),
the project's guarantees, security areas and integration surface in [`.squad/project.md`](/.squad/project.md).
Working records stay under `specs/` on the work branch. A product PR never changes the squad or these
instructions (`.squad/stack.md` and `.squad/project.md` excepted); squad lessons become `squad` issues where they can be fixed (`.squad/routing.md`, *Squad
lessons*). The user acts as Product Manager and is asked only when the Lead escalates.

The reasoning behind code decisions — why something was built the way it was — is recorded by the Lead
as one decision record per decision in [`docs/decisions/`](/docs/decisions/README.md) (unreleased records are
edited in place, released ones are append-only and superseded), not in `ARCHITECTURE.md`. What holds today —
formats, limits, error behavior, guarantees — is written once in the area document of the change in
[`docs/areas/`](/docs/areas/README.md), which the same pull request updates. Read the relevant records and area
documents before changing code they cover, and do not contradict an accepted record without changing it
(unreleased) or superseding it (released).

Two rules these skills enforce that are easy to get wrong:

- **A pull request documents the change, not how it was produced.** The internal review loop — its
  pass count, its findings, the commits that resolved them — never appears in the PR title, body or
  commit messages.
- **A finding posted as a review comment gets worked in that pull request**, blocking or not, now, in
  that pull request — never deferred to "the next change that touches this code". If it
  really should not be fixed here, reply with the reason or open a linked issue now — then resolve the thread.

## Pull requests, contributing and architecture

Follow [`CONTRIBUTING.md`](/docs/CONTRIBUTING.md) for branch/PR naming (`[area] Description`), the PR
checklist in [`.github/pull_request_template.md`](/.github/pull_request_template.md), and the
stability policy. Consult [`ARCHITECTURE.md`](/docs/ARCHITECTURE.md) before changing the behavior it
describes — the guarantees listed in [`.squad/project.md`](/.squad/project.md) are deliberate, not
incidental behavior.
