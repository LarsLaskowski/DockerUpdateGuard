# Plan: Meet CA1848 (LoggerMessage delegates) of the shared .NET rule baseline

Source: Issue #426
Tier: trivial — log calls move to `[LoggerMessage]` methods with unchanged level, template and arguments; no behavior or control flow changes.
Security areas touched: build configuration (`.editorconfig`); logging of external data (`Images/ImageHostLoggingExtensions.cs` gains three warnings) — content unchanged, no control created, changed or removed

## Problem / root cause

CA1848 fires at `src/DockerUpdateGuard/Images/ResourceStatisticsCollector.cs:130` and `src/Tests/DockerUpdateGuard.Tests/TelemetryServiceCollectionExtensionsTests.cs:67`; `DOTNET_RULES_PENDING` holds it at `suggestion`.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | The issue's DotNet and NGINX sites are converted on the base (EventIds 2101, 2102) | per-class `*Logging.cs` files, commit 81ea8e3 | confirmed |
| 2 | Only the two sites above remain in the solution; the issue missed none | SARIF of all five projects after 81ea8e3; grep of `src/` for `.Log…(`, `BeginScope(` | confirmed |
| 3 | Without the entry CA1848 is a warning, so an error | SDK 10.0.404 `analysislevel_10_recommended.globalconfig` line 219 | confirmed |
| 4 | The logging generator reaches the test project | `dotnet msbuild -getItem:Analyzer` lists `Microsoft.Extensions.Logging.Generators.dll` | confirmed |
| 5 | EventId 2103 is free | grep `EventId = ` over `src/` | confirmed |
| 6 | `ResourceStatisticsCollector` has no tests | grep `src/Tests` | confirmed |
| 7 | Moving the two contracts within namespace `DockerUpdateGuard.Images` changes no call site | extension calls `_logger.<Name>LoadFailed(exception)` | confirmed |
| 8 | `docs/ARCHITECTURE.md` lines 377–380 (a `*Logging.cs` per client and job) is stale: `Images` shares one class, `OciRegistryClient` included (related defect, fixed here) | read of the files | confirmed |

## Acceptance criteria

- AC1: When an enabled, configured instance's resource collection does not succeed, `ResourceStatisticsCollector.CollectAsync` logs one `Warning` with EventId 2103 and the message `Resource statistics collection failed for Docker instance <name>: <operation message>`.
- AC2: The release-metadata load failures keep EventIds 2101/2102, level and template after the move; the two per-class files are gone.
- AC3: The telemetry pipeline test logs through a `[LoggerMessage]` method, assertions unchanged.
- AC4: The build reports no CA1848 at its `AnalysisLevel` severity.
- AC5: Neither `DOTNET_RULES_PENDING` nor `.editorconfig` names CA1848.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | ResourceStatisticsCollectorCollectAsyncWhenCollectionFailsLogsWarningWithEventId | `src/Tests/DockerUpdateGuard.Tests/ResourceStatisticsCollectorTests.cs` |
| AC2 | check: Reviewer compares the moved declarations (step 8) | — |
| AC3 | CI: *Test with coverage* in the step-7 stamp | — |
| AC4 | CI: *Build* and *Analyzer gate* in the step-7 stamp | — |
| AC5 | check: `python3 .squad/tools/dotnet-rules.py --check` exits 0, grep finds no `CA1848` in both files (Reviewer) | — |

AC1 test: in-memory context and options with one enabled instance "edge", NSubstitute `IDockerInstanceClient` returning `Failed` with "Docker instance 'edge' returned 500: boom", `TestLogger`; asserts one `Warning` entry, `EventId.Id` 2103, message `Resource statistics collection failed for Docker instance edge: Docker instance 'edge' returned 500: boom`.

## Approach

1. `Images/ImageHostLoggingExtensions.cs`, end of `Methods`: `DotNetReleaseMetadataLoadFailed` (2101) and `NginxReleaseMetadataLoadFailed` (2102) moved verbatim, then new `ResourceStatisticsCollectionFailed` (2103, `Warning`, template `Resource statistics collection failed for Docker instance {DockerInstanceName}: {Message}`). Delete the two per-class files.
2. `ResourceStatisticsCollector.cs:130` calls it with `dockerInstance.Name`, `result.Message`.
3. `TelemetryServiceCollectionExtensionsTests` becomes `partial` and gets, in a `Static methods` region, a private static partial `[LoggerMessage]` method (`Information`, same template, no EventId: no application event) called at line 67; guard and assertions stay.
4. At the start of step 6 the orchestrator sets `squad_settings.py` line 26 to `DOTNET_RULES_PENDING = {"CA1716": "#427"}`; the Dev runs `python3 .squad/tools/dotnet-rules.py`, dropping `.editorconfig` lines 547–548 only.

Owners: Dev — 1, 2, 4, and 3, which stops compiling only through 4 (mechanical, no assertion touched, in its report: *Loop limits*); Tester — the AC1 test and a check of 3.

## Options considered

- Per-class `ResourceStatisticsCollectorLogging.cs`, PR 429's files kept: lost — two layouts in one folder; the collector is image-host background work like the rest of the shared class.
- Split `ImageHostLoggingExtensions.cs` per class: lost — about 50 contracts moved, beyond the issue.
- Drop the test's log call: lost — its only record through the OpenTelemetry provider.

## Affected projects and types

See *Approach*.

## Signatures (for the Dev's skeleton)

None (no skeleton). New: ImageHostLoggingExtensions.ResourceStatisticsCollectionFailed(this ILogger logger, string dockerInstanceName, string? message), public static partial void. The two per-class logging classes are removed; their methods keep name and signature.

## Test files

`src/Tests/DockerUpdateGuard.Tests/ResourceStatisticsCollectorTests.cs` (new).

Existing test code that calls a changed signature: none.

Files no unit test can cover: none.

## Areas

none — the failed-collection warning in `docker-instances.md` stays as written.

## Documentation updates

- `docs/ARCHITECTURE.md` section 10, lines 377–380 (Dev): the layout as built — clients under `Docker/`, `DockerHub/`, `Portainer/`, `Vulnerabilities/` and the `Infrastructure/` retry handler have their own `*Logging.cs`; the host, `Images/` and the components share `HostLoggingExtensions.cs`, `Images/ImageHostLoggingExtensions.cs`, `Components/ComponentLoggingExtensions.cs`; CA1848 rejects ad-hoc calls.
- Sweep: `telemetry.md` (only 1000–1006/1010) and `.squad/stack.md` line 87 stay true; no changelog.

## Architecture check

Fixed event IDs (`telemetry.md`, *Logs*) now hold everywhere; log content is unchanged, no guarantee weakened.

## Security considerations

- [x] no limits.
- [x] none.

## Decision records

- none: no decision beyond the obvious fix — the existing folder grouping, stated in `docs/ARCHITECTURE.md`.

## Out of scope / follow-ups

- CA1716 (#427) stays pending.
