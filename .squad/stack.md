# Stack: .NET

The toolchain and the exact commands of this repository. Every squad member, skill and instruction file
refers to the entries below by their *italic name*. Seeded from the Squad-Spec-Repository-Template `dotnet`
profile and owned by this repository: keep it true when the build changes.

## Toolchain

- .NET SDK `10.0.x` (target framework `net10.0`), solution `DockerUpdateGuard.slnx`.
- Formatter: `reihitsu-format` (`dotnet tool install -g Reihitsu.Cli`), on the same release line as the
  **Reihitsu.Analyzer** version pinned in `Directory.Packages.props`; otherwise the formatter can revert
  code the analyzer considers correct.
- Analyzers: **SonarAnalyzer.CSharp** in every project via `Directory.Build.props` (the Sonar C# rules locally,
  so SonarQube issues surface before the push), **Reihitsu.Analyzer** referenced by each project file, both
  configured by the rulesets in `rules/`; the MSTest analyzers come with the MSTest package in the test projects.
- The SessionStart hook `.claude/hooks/session-start.sh` installs the formatter, sets `DOTNET_ROOT` and
  restores the solution in remote sessions.

## Layout

- Production code: `src/` — `DockerUpdateGuard` (host, composition root, UI, scan engine),
  `DockerUpdateGuard.Data` (EF Core / PostgreSQL), `DockerUpdateGuard.Telemetry` (OpenTelemetry);
  `src/GlobalSuppressions.cs` holds the centralized suppressions, `rules/` the per-configuration rulesets.
- Tests: `src/Tests/DockerUpdateGuard.Tests/` (host/application layer) and
  `src/Tests/DockerUpdateGuard.Data.Tests/` (data layer), one test class per type under test in
  `{TypeUnderTest}Tests.cs` (or `…RenderTests` / `…PersistentStateTests` when split, `docs/UNIT_TESTS.md`).

## Commands

| Name | Command |
| ---- | ------- |
| *Restore* | `dotnet restore DockerUpdateGuard.slnx` |
| *Format* (in the squad: by the member that changed the files, `.squad/team.md`) | `reihitsu-format --force ./` |
| *Format check* | `reihitsu-format --check ./` |
| *Build* | `dotnet build DockerUpdateGuard.slnx -c Release --no-restore` |
| *Test* | `dotnet test DockerUpdateGuard.slnx -c Release --no-build` |
| *Single test* | `dotnet test src/Tests/DockerUpdateGuard.Tests/DockerUpdateGuard.Tests.csproj --filter "FullyQualifiedName~ClassName.MethodName"` |
| *Test (project)* | `dotnet test src/Tests/<Project>.Tests -c Release --no-build` (the test project of the changed code; a `patch` that changes only test code, `.squad/routing.md` *Gates of a patch* — it writes no coverage report, so a `patch` that changes production code runs *Test with coverage* instead) |
| *Test with coverage* | `rm -rf TestResults && dotnet test DockerUpdateGuard.slnx -c Release --no-build --collect:"XPlat Code Coverage" --results-directory ./TestResults` (clears earlier runs: the coverage gate merges every report it finds) |
| *Coverage gate* | `python3 .squad/tools/coverage-check.py` |
| *Analyzer gate* | `python3 .squad/tools/analyzer-check.py` |
| *Sonar gate* | `python3 .squad/tools/sonar-check.py branch -- bash -c 'dotnet sonarscanner begin /k:"LarsLaskowski_DockerUpdateGuard" /o:"networlddev" /d:sonar.token="$SONAR_TOKEN" /d:sonar.branch.name="$(git branch --show-current)" /d:sonar.projectBaseDir="$PWD" && dotnet build DockerUpdateGuard.slnx -c Release --no-incremental && dotnet sonarscanner end /d:sonar.token="$SONAR_TOKEN"'` (after *Test with coverage*; needs `SONAR_TOKEN` and the `dotnet-sonarscanner` tool, see *Sonar gate* below) |
| *Image build check* | `docker build -f src/DockerUpdateGuard/Dockerfile .` where Docker runs; otherwise the restore-order reproduction under *Image build check* below (`.squad/routing.md`, *Criteria verified only in CI*) |

## Analyzer gate

`analyzer-check.py` runs a full, non-incremental Release build with a SARIF error log per project and
reports every non-suppressed Roslyn diagnostic — `RH####`, `S####`, `MSTEST####`, `CA####`, at **any
severity**, including info-level ones that never appear as build warnings but that SonarQube Cloud
imports — located in a file changed since the merge base with `origin/main`. Its solution comes from
`.squad/tools/squad_settings.py`. In addition the build must show **zero `RH####` warnings and errors**
anywhere. Do not grep console build output instead: an incremental build prints no warnings at all.

Easy to get wrong by hand: `#region` blocks on every type, XML documentation on every member, no
underscores in member names, a region for an interface implementation named after the interface.

SonarQube Cloud's own quality profile can still report `S####` rules the local default profile does not,
and its non-Roslyn checks (hotspots, taint analysis, duplication) have no local equivalent: the *Sonar gate*
below is the first to report their issues and hotspots, and squad step 11 reads the quality gate,
duplication included.

Own browser scripts (`wwwroot/js/*.js`, no Node build) have no local analyzer; the repository has none today.
SonarQube Cloud's JavaScript rules would report on one first in the *Sonar gate* below, so a new one is written to those
rules from the start. The one that bit (`javascript:S2486`): a `catch` block that ignores the exception holds
exactly one whole-line comment and nothing else — a comment before a `return` inside the block is still flagged.

Changed shell scripts (`*.sh`), GitHub workflows (`.github/workflows/*.yml`) and Dockerfiles are checked with
`shellcheck`, `actionlint` and `hadolint`; the SessionStart hook installs the three as pinned, checksum-verified
binaries (`.claude/hooks/install-linters.sh`), and the gate reports NOT RUN for one that is missing (a refused
download, for example). The gate itself checks two SonarQube Cloud shell rules shellcheck does not report:
`shelldre:S7679` (a positional parameter `$1`…`$9` used as a word of its own inside a function — assign it to a
local variable first) and `shelldre:S7688` (`[ … ]` instead of `[[ … ]]`). Other `shelldre:*` findings are first
reported by the *Sonar gate* below.

*Sonar gate* runs SonarQube Cloud's own analysis on the work branch before the pull request (squad step 7):
`sonar-check.py` runs the scanner with the branch name, waits for the analysis and fails on every open issue or
security hotspot on a line the branch changed, so the rules no local analyzer has stop surfacing only after the
pull request. It runs on the committed change (the scanner uploads the working tree, so it refuses uncommitted or
untracked files other than the coverage reports) and needs `SONAR_TOKEN` in the session's environment (an environment secret) and network access to the
scanner's download hosts (`api.sonarcloud.io`, `scanner.sonarcloud.io` and the package registry); without them it
reports NOT CHECKED, and step 11 reads the draft pull request with `python3 .squad/tools/sonar-check.py pr <number>`
instead. A finding of a rule no local analyzer reports is noted in this section, in the same change, instead of a
squad lesson.
The .NET scanner is the `dotnet-sonarscanner` tool, installed globally with `dotnet tool install -g dotnet-sonarscanner`
(a local install writes an untracked `dotnet-tools.json` to the repository root, which the gate refuses); its `begin`
arguments mirror the CI step, plus the branch name and the repository root as base
directory, and it needs the full, non-incremental build between `begin` and `end`. The local coverage report is
Cobertura, which the scanner does not read, so the branch analysis carries no coverage figure: the *Coverage
gate* covers that.

## Image build check

`src/DockerUpdateGuard/Dockerfile` copies `Directory.Packages.props`, `Directory.Build.props` and the three
production project files first, restores `src/DockerUpdateGuard/DockerUpdateGuard.csproj`, then copies the
sources (filtered by `.dockerignore`) and publishes. The .NET SDK decides at restore time what the publish output
holds (content items, static web assets, the `_framework/` scripts of a Blazor app), so a publish from the full
working tree can contain files the image never gets (Vandox#24: `_framework/blazor.web.js` was in the local
publish and missing from the image). The publish here restores again, which narrows that gap, but a change to the
copy or restore lines can widen it. Where Docker runs, build the image. Where it does not (the sandbox),
reproduce the Dockerfile's order in a scratch directory; every change to a `.csproj`, `Directory.*.props`, a
`.razor` or other content item, a static web asset or the Dockerfile runs it before the review:

```bash
scratch="$(mktemp -d)"
cp --parents Directory.Packages.props Directory.Build.props \
  src/DockerUpdateGuard/DockerUpdateGuard.csproj \
  src/DockerUpdateGuard.Data/DockerUpdateGuard.Data.csproj \
  src/DockerUpdateGuard.Telemetry/DockerUpdateGuard.Telemetry.csproj "$scratch"
(cd "$scratch" && dotnet restore src/DockerUpdateGuard/DockerUpdateGuard.csproj)
git ls-files -z | xargs -0 -I{} cp --parents {} "$scratch"
(cd "$scratch" && dotnet publish src/DockerUpdateGuard/DockerUpdateGuard.csproj -c Release -o ./publish)
ls "$scratch"/publish/wwwroot/_framework/blazor.web.js
```

Keep the first copy equal to the `COPY` lines before `RUN dotnet restore` in the Dockerfile, and the publish
equal to its `RUN dotnet publish` (add `--no-restore` here if the Dockerfile gets it). A publish that fails, or
an output that is missing, is the defect the CI release build would report after the PR is open.

## Coverage exclusions

Two places compute coverage and must agree (`.squad/routing.md`, *Code a unit test cannot cover*): the
*Coverage gate* reads `COVERAGE_PATHSPECS` and `COVERAGE_EXCLUDES` in `.squad/tools/squad_settings.py`
(`src/*.cs` and `src/*.razor`, minus the tests and the generated EF Core migrations), and SonarQube Cloud reads
`sonar.coverage.exclusions`, passed as `/d:sonar.coverage.exclusions=…` in the *Begin SonarQube analysis*
step of `.github/workflows/ci.yml` (today `.squad/**`, `.claude/**` and
`src/DockerUpdateGuard.Data/Migrations/**`). A file no unit test can cover (`wwwroot/js/**`, a generated file) is
added to both with a decision record — never to `sonar.exclusions`, which would drop it from the analysis too.

## Writing code

- File-scoped namespaces; one top-level type per file; `using` outside the namespace (System first).
- Wrap every type's members in `#region` blocks **as you write the code**, grouped by member kind
  (`Constants`, `Fields`, `Constructors`, `Properties`, `Events`, `Methods`, …); a region that groups an
  interface implementation is named after the interface (e.g. `#region IParser`) and its description
  does not end with the word "implementation".
- XML documentation on all members (English, no `<remarks>`).
- Nullable reference types, `var`, language keywords over BCL types, LINQ method syntax only,
  `== false` instead of `!`, `is null` / `is not null`, no primary constructors, constructor injection
  with `_camelCase` readonly fields, `.ConfigureAwait(false)` in library/service code.
- Guards an analyzer asks for (e.g. `if (_logger.IsEnabled(...))` for CA1873) are written by the Dev,
  not added later by the Code Officer.

## Logger arguments

CodeQL's *Clear text storage of sensitive information* (`cs/cleartext-storage-of-sensitive-information`) judges
a value by its **name**: a variable, field, constant, property or parameter whose lower-case name contains
`password`, `passwd`, `trusted`, `account…id`, `accnt…id`, `account…key`, `accnt…key`, `license…key`,
`licence…key` or `voucher…code` counts as sensitive (`csharp/ql/lib/semmle/code/csharp/security/SensitiveActions.qll`
in `github/codeql`). For names there is no exemption: `PasswordHash` and a constant `EnvWebPasswordHash` that
holds only the name of an environment variable count too (the `hashed`/`encrypted`/`crypt` exemption applies
to string values, not to names). Such a name passed to a log call fails the PR in CI (`.github/workflows/codeql.yml`),
and no local gate reports it. The remedy is a neutral name for the value (a constant `WebLoginHashVariable` for
the variable name, for example) whose text a test pins, never a suppression.

## Writing tests

See `docs/UNIT_TESTS.md` — MSTest with **NSubstitute** for collaborators that cross an infrastructure boundary,
EF Core InMemory / SQLite for data tests. The rules the MSTest and Sonar analyzers enforce and the Code Officer cannot fix
without handing back: pass `TestContext.CancellationToken` to every call that accepts a token
(MSTEST0049 / S8949), and use the specific `Assert` member instead of `Assert.IsTrue(...)` or
`StringAssert` (MSTEST0037 / MSTEST0046).

## Skeleton

New members with their full signature, XML docs and `#region` blocks, bodies
`throw new InvalidOperationException("Skeleton: not implemented");` (never `NotImplementedException`, which
RH2003 rejects as an error in Release), so the solution builds and the tests compile and fail.
Constructors store their arguments and never throw: a test harness builds these types, so only member bodies
fail.
The `Skeleton` body above is the default. A zero-value stub is allowed only where it would break a test that
exists and passes today: the new tests must still fail on the stub, and the Dev reports it as a deviation.

## Dependencies

NuGet packages through **Central Package Management**: the version goes into `Directory.Packages.props`,
never into a `.csproj`. SonarAnalyzer.CSharp is referenced for every project in `Directory.Build.props`.

## Concurrency

Builds and test runs share `bin/` and `obj/`; two at once break each other. `analyzer-check.py` serializes
itself with a lock (`obj/analyzer-check.lock`), plain builds do not.

## Known pitfalls

- `reihitsu-format` fails with ".NET location: Not found" when `DOTNET_ROOT` is unset — the SessionStart
  hook sets it; otherwise prefix the command with
  `DOTNET_ROOT="$(dirname "$(readlink -f "$(command -v dotnet)")")"`.
- `reihitsu-format` asks for confirmation for more than 25 files; `--force` skips the prompt, which a
  non-interactive session cannot answer.
- `reihitsu-format` rewrites a `\u` escape in a C# string literal into the character it names, so a literal
  U+2028 then breaks the compilation and a test about an escape tests the wrong input. After *Format*, check
  the escapes of the changed files on disk (`grep -n '\\u' <file>`, *Escapes in files* in `.squad/team.md`);
  where the formatter keeps rewriting one, build the character at run time (`char.ConvertFromUtf32(0x2028)`)
  instead of writing it as an escape in a literal.
