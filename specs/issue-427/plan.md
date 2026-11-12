# Plan: Meet CA1716 (identifiers matching keywords) of the shared .NET rule baseline

Source: Issue #427
Tier: trivial — a namespace and its folder are renamed with every reference; no behavior or control flow changes.
Security areas touched: build configuration (`.editorconfig`, the pending CA1716 line leaves the `squad:dotnet-rules` block) — no control created, changed or removed

## Problem / root cause

The namespace `DockerUpdateGuard.Components.Shared` ends in the Visual Basic keyword `Shared`; CA1716 reports it once, at `src/DockerUpdateGuard/Components/Shared/MetricGridSkeleton.razor.cs:3`. `DOTNET_RULES_PENDING` (`.squad/tools/squad_settings.py:26`) holds it at `suggestion` (`.editorconfig:545–547`).

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | Seven code-behind files declare the namespace; the `.razor` files take it from the folder (no `@namespace` anywhere) | grep: `namespace DockerUpdateGuard.Components.Shared;` in the seven `Components/Shared/*.razor.cs`; grep `@namespace` in `src/DockerUpdateGuard`: no hit | confirmed |
| 2 | Users: `Components/_Imports.razor:9` and seven test files (`using`, line 3 or 5) | grep over the repository, `bin`/`obj`/`TestResults` excluded | confirmed |
| 3 | No CSS isolation file, route, Dockerfile, `.dockerignore`, workflow, area document or record names the folder; `docs/ARCHITECTURE.md:364` does | grep over `src`, `docs`, `.github`, `README.md` | confirmed |
| 4 | `Common` is no C# or Visual Basic keyword and no existing namespace segment | keyword lists; grep `Common` in `src/DockerUpdateGuard` namespaces: no hit | confirmed |
| 5 | With an empty pending dict, `dotnet-rules.py` writes no pending section, so `.editorconfig` lines 544–547 (blank, header, comment, setting) go | `unset_rules` returns `[]` without lines (`.squad/tools/dotnet-rules.py:237`); `pending_errors` accepts any dict (`:122–125`) | confirmed |
| 6 | The baseline does not set CA1716; after the change it runs at its `AnalysisLevel` severity | grep `CA1716` in `.squad/dotnet-rules.editorconfig`: no hit | confirmed |

## Acceptance criteria

- AC1: The folder `src/DockerUpdateGuard/Components/Shared/` is `src/DockerUpdateGuard/Components/Common/` with the same fourteen files, the namespace is `DockerUpdateGuard.Components.Common`, and no file outside `specs/` names `Components.Shared` or `Components/Shared` (`Components\Shared`).
- AC2: The build reports no CA1716 and no IDE0130.
- AC3: Neither `DOTNET_RULES_PENDING` nor `.editorconfig` names CA1716.
- AC4: The existing component tests pass unchanged apart from their `using`.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | check: grep for `Components.Shared`, `Components/Shared`, `Components\Shared` outside `specs/`, `bin`, `obj`, `TestResults` finds nothing (Reviewer, step 8) | — |
| AC2 | CI: *Build* and *Analyzer gate* in the step-7 stamp | — |
| AC3 | check: `python3 .squad/tools/dotnet-rules.py --check` exits 0; grep finds no `CA1716` in both files (Reviewer) | — |
| AC4 | CI: *Test with coverage* and *Coverage gate* in the step-7 stamp | — |

No new test: nothing behaves differently.

## Approach

1. `git mv src/DockerUpdateGuard/Components/Shared src/DockerUpdateGuard/Components/Common` (a pure rename, contents unchanged except step 2).
2. In the seven `*.razor.cs`: `namespace DockerUpdateGuard.Components.Shared;` → `namespace DockerUpdateGuard.Components.Common;`.
3. `Components/_Imports.razor`: line 9 is removed and `@using DockerUpdateGuard.Components.Common` goes in before `@using DockerUpdateGuard.Components.Layout` (line 8), keeping the order sorted.
4. The seven test files (`MetricGridSkeletonTests`, `PageHeaderSkeletonTests`, `TableSkeletonTests`, `VulnerabilityAssessmentCardTests`, `VulnerabilityFindingsTableTests`, `VulnerabilityRescanControlTests`, `VulnerabilitySeverityChipsTests`): `using DockerUpdateGuard.Components.Shared;` → `using DockerUpdateGuard.Components.Common;` (sort position unchanged).
5. At the start of step 6 the orchestrator sets `.squad/tools/squad_settings.py` line 26 to `DOTNET_RULES_PENDING = {}`; the Dev runs `python3 .squad/tools/dotnet-rules.py`, which drops `.editorconfig` lines 544–547 only.
6. The Dev runs the *Image build check* (moved `.razor` content items), in the sandbox the restore-order reproduction of `.squad/stack.md`.

Owners: Dev — 1–3, 5 (the script run), 6 and the documentation update; step 4 is mechanical test adaptation forced by step 1, done by the Dev in the same edit and named in its report (*Loop limits*); Orchestrator — the `squad_settings.py` line.

`Common` was chosen over `Widgets` or `BuildingBlocks`: it keeps the meaning of "shared" and covers both skeletons and vulnerability widgets.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Components/Shared/*` (7 components) | folder and namespace renamed |
| DockerUpdateGuard | `Components/_Imports.razor` | `@using` follows |
| DockerUpdateGuard.Tests | seven component test files | `using` follows |

## Signatures (for the Dev's skeleton)

None: the seven component types keep their names and members; only their namespace changes. No skeleton step.

## Test files

No new test file. The seven test files of step 4 change only their `using`, adapted by the Dev.

Files no unit test can cover: none.

## Areas

None: no behavior changes.

## Documentation updates

- `docs/ARCHITECTURE.md:364` — `` `Components\Shared` `` becomes `` `Components\Common` `` (Dev).

## Architecture check

The UI layer layout of `docs/ARCHITECTURE.md` §9 is kept; only the folder name changes. No guarantee of `.squad/project.md` is touched.

## Security considerations

- [x] No limits.
- [x] No exception reaches a user: none.

## Decision records

- none: no decision beyond the obvious rename.

## Out of scope / follow-ups

- none.
