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

## Pull requests

- Write the PR title and description in English, regardless of the language used in the conversation.
- Do not mention Claude, Anthropic, Copilot, or any AI assistant in the PR title or description.
- Do not include AI session links, "Co-Authored-By" trailers for AI assistants, "Generated with ..." notices, or any other reference indicating the PR was created with AI assistance.
- Follow the PR template in `.github/PULL_REQUEST_TEMPLATE.md`. Use its sections and do not add extra sections beyond it.
- Do not add a "Validation", "Verification", "Testing", or similar section that lists `reihitsu-format`, `dotnet build`, `dotnet test`, or other build/test commands. Build and tests run automatically as PR checks, so restating them in the description is unnecessary.

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

The repository contains a complete implementation: Docker/DockerHub/Portainer clients, an EF Core data layer with migrations, a Blazor UI, and a telemetry layer. The architecture is defined by the solution layout, project references, and package choices:

| Path | Role |
| --- | --- |
| `src\DockerUpdateGuard` | Main ASP.NET Core host (`Microsoft.NET.Sdk.Web`); composition root; references the data and telemetry projects |
| `src\DockerUpdateGuard.Data` | Data-access layer; EF Core with PostgreSQL via `Npgsql.EntityFrameworkCore.PostgreSQL` |
| `src\DockerUpdateGuard.Telemetry` | Shared observability layer; OpenTelemetry hosting, OTLP export, ASP.NET Core, HTTP, and runtime instrumentation |
| `src\Tests\DockerUpdateGuard.Tests` | Tests for the main host/application layer; references the web project and uses EF Core InMemory plus NSubstitute |
| `src\Tests\DockerUpdateGuard.Data.Tests` | Tests for the data layer; references the data project and uses EF Core SQLite |

Web startup and dependency wiring stay in the main host project; persistence stays in `.Data`; observability stays in `.Telemetry`.

## Key conventions

- The repository uses the XML-based `.slnx` solution format.
- Runtime projects target `net10.0`, enable nullable reference types, implicit usings, and XML documentation files.
- Runtime projects disable generated assembly info and instead link `SharedAssemblyInfo.cs` from the repository root.
- Runtime and test projects also use per-configuration rulesets from `rules\DockerUpdateGuard.Debug.ruleset` and `rules\DockerUpdateGuard.Release.ruleset`.
- `Reihitsu.Analyzer` are part of the standard project setup.
- Tests are under `src\Tests`, not a top-level `tests` folder. Keep new test projects there.
- The current test stack is MSTest with `coverlet.collector`.
- Detailed C# formatting and style rules live in `.github\instructions\csharp.instructions.md`. Follow that file for naming, region layout, XML docs, and null-handling preferences.
- Prefer MSTest's `Assert` and `CollectionAssert` APIs directly instead of FluentAssertions.
- Name test classes `{Feature}Tests` and test methods `{Class}{Scenario}{ExpectedResult}`.
- Always include assertion messages in tests.

## EF Core migrations

The project already has EF Core migrations; follow the same pattern for new ones:

- first migration: `InitialCreate`
- later migrations: `Update1`, `Update2`, `Update3`, ...
- rename migration files to remove the timestamp prefix
- keep the generated `[Migration("yyyyMMddHHmmss_Name")]` attribute unchanged
