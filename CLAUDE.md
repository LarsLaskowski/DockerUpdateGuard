# DockerUpdateGuard — Claude Instructions

This file describes project-specific conventions for DockerUpdateGuard.
Claude must follow these guidelines when working in this repository.

The detailed C# code-style rules (naming, regions, formatting, XML docs,
null handling, suppressed analyzer rules) are imported below and are
**binding**:

@.github/instructions/csharp.instructions.md

## Git workflow

- Never run `git commit` or `git push` without explicit user approval.
- Read-only Git commands are fine.

## Commit messages

- First line: one-line summary of no more than 80 characters.
- Do not end the subject line with a period.
- Do not write in the first person.
- Keep the body to a maximum of 3–5 sentences, depending on the number of changes.

## Build, test, and format

Use the solution file at the repository root (`DockerUpdateGuard.slnx`):

- Restore: `dotnet restore DockerUpdateGuard.slnx`
- Format source: `reihitsu-format ./`
- Build: `dotnet build DockerUpdateGuard.slnx -c Release --no-restore`
- Run all tests: `dotnet test src\Tests\**\*.csproj -c Release --no-build --logger trx --collect:"XPlat Code Coverage"`
- Run one test project: `dotnet test src\Tests\DockerUpdateGuard.Tests\DockerUpdateGuard.Tests.csproj -c Release --no-build`
- Run one test method: `dotnet test src\Tests\DockerUpdateGuard.Tests\DockerUpdateGuard.Tests.csproj --filter "FullyQualifiedName~Namespace.ClassName.MethodName"`

Run `reihitsu-format ./` after source changes and before building. The command
is a .NET tool installable with `dotnet tool install -g Reihitsu.Cli` if missing.

There is no separate lint command beyond formatting. Static analysis runs during
build via the configured rulesets and analyzers.

## High-level architecture

| Path | Role |
| --- | --- |
| `src\DockerUpdateGuard` | Main ASP.NET Core host (`Microsoft.NET.Sdk.Web`); composition root; references the data and telemetry projects |
| `src\DockerUpdateGuard.Data` | Data-access layer; EF Core with PostgreSQL via `Npgsql.EntityFrameworkCore.PostgreSQL` |
| `src\DockerUpdateGuard.Telemetry` | Shared observability layer; OpenTelemetry hosting, OTLP export, ASP.NET Core / HTTP / runtime instrumentation |
| `src\Tests\DockerUpdateGuard.Tests` | Tests for the host/application layer; references the web project; EF Core InMemory + NSubstitute |
| `src\Tests\DockerUpdateGuard.Data.Tests` | Tests for the data layer; references the data project; EF Core SQLite |

Web startup and dependency wiring stay in the main host project; persistence
stays in `.Data`; observability stays in `.Telemetry`.

## Key conventions

- The repository uses the XML-based `.slnx` solution format.
- Runtime projects target `net10.0`, enable nullable reference types, implicit usings, and XML documentation files.
- Runtime projects disable generated assembly info and link `SharedAssemblyInfo.cs` from the repository root.
- Runtime and test projects use per-configuration rulesets from `rules\DockerUpdateGuard.Debug.ruleset` and `rules\DockerUpdateGuard.Release.ruleset`.
- `Reihitsu.Analyzer` is part of the standard project setup.
- Tests live under `src\Tests`, not a top-level `tests` folder. Keep new test projects there.
- The test stack is MSTest with `coverlet.collector`.
- Prefer MSTest's `Assert` and `CollectionAssert` APIs directly instead of FluentAssertions.
- Name test classes `{Feature}Tests` and test methods `{Class}{Scenario}{ExpectedResult}`.
- Always include assertion messages in tests.
- Centralize SonarCloud rule suppressions in the shared `src\GlobalSuppressions.cs` (linked into each project) rather than scattering per-file suppressions.

## EF Core migrations

If the project adds EF Core migrations, follow the SeriesOverwatch pattern:

- first migration: `InitialCreate`
- later migrations: `Update1`, `Update2`, `Update3`, ...
- rename migration files to remove the timestamp prefix
- keep the generated `[Migration("yyyyMMddHHmmss_Name")]` attribute unchanged
