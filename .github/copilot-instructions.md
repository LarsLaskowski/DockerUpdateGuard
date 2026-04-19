# DockerUpdateGuard Project Instructions

This file describes project-specific conventions and configuration for DockerUpdateGuard.
Copilot and other AI assistants must follow these guidelines when working in this repository.

## Commit Messages

- The first line should be a one-line summary of no more than 80 characters
- Do not end the subject line with a period
- Do not write the text in the first person
- Keep the main body to a maximum of 3–5 sentences, depending on the number of changes

## Git workflow

- Never run `git commit` or `git push` without explicit user approval.
- Read-only Git commands are fine.
- Keep commit subjects to a single line under 80 characters and do not end them with a period.

## Build, test, and lint

Use the standard .NET solution flow once the solution is added:

- Restore: `dotnet restore DockerUpdateGuard.slnx`
- Build: `dotnet build DockerUpdateGuard.slnx -c Release --no-restore`
- Test all test projects: `dotnet test tests\\**\\*.csproj -c Release --no-build --logger trx --collect:"XPlat Code Coverage"`
- Run a single test: `dotnet test tests\\DockerUpdateGuard.Tests\\DockerUpdateGuard.Tests.csproj --filter "FullyQualifiedName~Namespace.ClassName.MethodName"`

There is usually no separate lint command in this project style. Static analysis runs during `dotnet build` through project-level analyzers and rulesets.

## High-level architecture

Treat this as a multi-project .NET solution rather than a single-project app:

- `src\\` contains the runtime projects
- `tests\\` contains separate MSTest projects
- `rules\\` contains shared rulesets and `StyleCop.json`
- the repo root typically also contains the `.slnx` file, `azure-pipelines.yml`, and Docker-related files

Keep responsibilities split across projects instead of collapsing everything into the host application:

| Project | Responsibility |
| --- | --- |
| `*.WebApi` or main host project | Web host, controllers/endpoints, DI wiring, startup |
| `*.Data` | EF Core DbContext, entities, repositories, migrations |
| `*.Telemetry` or `*.Observability` | Logging, metrics, tracing, diagnostics setup |
| `*.Tests` | Unit and integration tests |

The main host project should reference supporting projects for data access and infrastructure concerns instead of implementing those concerns inline.

## Key conventions

- Target the latest stable .NET version used by the solution template. The reference project currently uses `net10.0`.
- Enable nullable reference types, implicit usings, and XML documentation files in every main project.
- Use the XML-based `.slnx` solution format.
- Link shared files like `SharedAssemblyInfo.cs` and `rules\\StyleCop.json` into each project when the solution is created.
- Use `StyleCop.Analyzers` and `Reihitsu.Analyzer` as build-time analyzers.
- Use MSTest with `coverlet.collector` for tests.
- Prefer MSTest's `Assert` and `CollectionAssert` APIs directly instead of FluentAssertions.
- Name test classes `{Feature}Tests` and test methods `{Class}_{Scenario}_{ExpectedResult}`.
- Always include assertion messages in tests.

If the project adds EF Core migrations, follow the same migration pattern as SeriesOverwatch:

- first migration: `InitialCreate`
- later migrations: `Update1`, `Update2`, `Update3`, ...
- rename migration files to remove the timestamp prefix
- keep the generated `[Migration("yyyyMMddHHmmss_Name")]` attribute unchanged
