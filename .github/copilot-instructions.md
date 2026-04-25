# DockerUpdateGuard Project Instructions

This file describes project-specific conventions and configuration for DockerUpdateGuard.
Copilot and other AI assistants must follow these guidelines when working in this repository.

## Commit messages

- The first line should be a one-line summary of no more than 80 characters
- Do not end the subject line with a period
- Do not write the text in the first person
- Keep the main body to a maximum of 3–5 sentences, depending on the number of changes

## Git workflow

- Never run `git commit` or `git push` without explicit user approval.
- Read-only Git commands are fine.
- Keep commit subjects to a single line under 80 characters and do not end them with a period.

## Build, test, and lint

Use the solution file at the repository root:

- Restore: `dotnet restore DockerUpdateGuard.slnx`
- Format source: `reihitsu-format ./`
- Build: `dotnet build DockerUpdateGuard.slnx -c Release --no-restore`
- Run all tests: `dotnet test src\Tests\**\*.csproj -c Release --no-build --logger trx --collect:"XPlat Code Coverage"`
- Run one test project: `dotnet test src\Tests\DockerUpdateGuard.Tests\DockerUpdateGuard.Tests.csproj -c Release --no-build`
- Run one test method: `dotnet test src\Tests\DockerUpdateGuard.Tests\DockerUpdateGuard.Tests.csproj --filter "FullyQualifiedName~Namespace.ClassName.MethodName"`

Run `reihitsu-format ./` after source changes and before running a build. The command is available as a .NET tool and can be installed with `dotnet tool install -g Reihitsu.Cli` if it is missing.

There is no dedicated lint command at the moment beyond formatting. Static analysis runs during build through the configured rulesets and analyzers.

## High-level architecture

The repository is currently a solution skeleton with project wiring in place, but almost no implementation files yet. The architecture is defined by the solution layout, project references, and package choices:

| Path | Role |
| --- | --- |
| `src\DockerUpdateGuard` | Main ASP.NET Core host (`Microsoft.NET.Sdk.Web`); references the data and telemetry projects |
| `src\DockerUpdateGuard.Data` | Data-access layer; prepared for EF Core with PostgreSQL via `Npgsql.EntityFrameworkCore.PostgreSQL` |
| `src\DockerUpdateGuard.Telemetry` | Shared observability layer; prepared for OpenTelemetry hosting, OTLP export, ASP.NET Core, HTTP, and runtime instrumentation |
| `src\Tests\DockerUpdateGuard.Tests` | Tests for the main host/application layer; references the web project and uses EF Core InMemory plus NSubstitute |
| `src\Tests\DockerUpdateGuard.Data.Tests` | Tests for the data layer; references the data project and uses EF Core SQLite |

The main host project is the composition root. It is expected to keep web startup and dependency wiring, while persistence stays in `.Data` and observability stays in `.Telemetry`.

## Key conventions

- The repository uses the XML-based `.slnx` solution format.
- Runtime projects target `net10.0`, enable nullable reference types, implicit usings, and XML documentation files.
- Runtime projects disable generated assembly info and instead link `SharedAssemblyInfo.cs` from the repository root.
- Runtime projects also link `rules\StyleCop.json` and use per-configuration rulesets from `rules\DockerUpdateGuard.Debug.ruleset` and `rules\DockerUpdateGuard.Release.ruleset`.
- `StyleCop.Analyzers` and `Reihitsu.Analyzer` are part of the standard project setup.
- Tests are under `src\Tests`, not a top-level `tests` folder. Keep new test projects there.
- The current test stack is MSTest with `coverlet.collector`.
- Detailed C# formatting and style rules live in `.github\instructions\csharp.instructions.md`. Follow that file for naming, region layout, XML docs, and null-handling preferences.

## Current state note

- Target the latest stable .NET version used by the solution template. The reference project currently uses `net10.0`.
- Enable nullable reference types, implicit usings, and XML documentation files in every main project.
- Link shared files like `SharedAssemblyInfo.cs` and `rules\\StyleCop.json` into each project when the solution is created.
- Use `StyleCop.Analyzers` and `Reihitsu.Analyzer` as build-time analyzers.
- Use MSTest with `coverlet.collector` for tests.
- Prefer MSTest's `Assert` and `CollectionAssert` APIs directly instead of FluentAssertions.
- Name test classes `{Feature}Tests` and test methods `{Class}{Scenario}{ExpectedResult}`.
- Always include assertion messages in tests.

If the project adds EF Core migrations, follow the same migration pattern as SeriesOverwatch:

- first migration: `InitialCreate`
- later migrations: `Update1`, `Update2`, `Update3`, ...
- rename migration files to remove the timestamp prefix
- keep the generated `[Migration("yyyyMMddHHmmss_Name")]` attribute unchanged
