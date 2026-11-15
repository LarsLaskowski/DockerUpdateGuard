# Plan: Shipped appsettings.json names its Docker instance

Source: Issue #288
Tier: trivial — a configuration default changes (one JSON value) plus regression tests and documentation; no control flow changes, and the diff exceeds the `patch` line budget.
Security areas touched: Docker Engine access (the shipped configuration now starts with its enabled socket instance instead of refusing to start)

## Problem / root cause

`src/DockerUpdateGuard/appsettings.json:55` ships `DockerInstances[0].Name` as `""`. `DockerUpdateGuardOptionsValidator.ValidateDockerInstances` (`src/DockerUpdateGuard/Configuration/DockerUpdateGuardOptionsValidator.cs:461-466`) rejects a blank name, and `ServiceCollectionExtensions.cs:56` (`ValidateOnStart()`) turns that into a startup failure unless another source names the instance.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | Shipped instance has an empty name, socket `BaseUrl`, `Enabled: true` | `appsettings.json:53-72` (issue said 51-70) | confirmed |
| 2 | Blank name fails validation | validator lines 461-466 (issue said 276-281) | confirmed |
| 2a | Failure text is `'DockerUpdateGuard:DockerInstances:Name' must be configured` | actual: `'Name' must be configured for every entry of 'DockerUpdateGuard:DockerInstances'` (decision 0013) | refuted |
| 3 | Validation runs at startup | `ServiceCollectionExtensions.cs:53-56` | confirmed |
| 4 | Compose files set `Local Docker`; they need no edit | `docs/docker-compose.minimal.yml:42`, `.trivy.yml:52`, `.full.yml:65` | confirmed |
| 5 | README recommends `Local Docker`, `Name` default "none" | `README.md:198`, `:186` (issue said 175, 163) | confirmed |
| 6 | Besides the name, the only shipped failure is the empty connection string | validator read against `appsettings.json` (Portainer and vulnerabilities disabled, `CertificatePath` blank, ranges in bounds); AC1/AC3 tests prove it | confirmed |
| 8 | Instances are identified by trimmed name, so nothing stored is renamed | `docs/areas/docker-instances.md`, *Configured instances* | confirmed |
| 9 | No image build input changes (no *Image build check*) | `appsettings.json` stays the same content item | confirmed |

## Acceptance criteria

- AC1: Loading the shipped `src/DockerUpdateGuard/appsettings.json` plus an in-memory `ConnectionStrings:DockerUpdateGuard`, binding `DockerUpdateGuardOptions` from section `DockerUpdateGuard` and validating with `DockerUpdateGuardOptionsValidator` over the same configuration succeeds; on failure the assert message lists every failure.
- AC2: The shipped `DockerUpdateGuard:DockerInstances` holds exactly one entry: `Name` `Local Docker`, `BaseUrl` `unix:///var/run/docker.sock`, `Enabled` `true`.
- AC3: The shipped file without a connection string fails validation with exactly one failure, `'DockerUpdateGuard:ConnectionString' or 'ConnectionStrings:DockerUpdateGuard' must be configured` (the shipped connection string stays empty).
- AC4: An explicitly empty or blank instance name is still rejected with the unchanged message.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | DockerUpdateGuardOptionsValidatorShippedAppSettingsWithConnectionStringSucceeds | src/Tests/DockerUpdateGuard.Tests/DockerUpdateGuardOptionsValidatorTests.cs |
| AC2 | DockerUpdateGuardOptionsValidatorShippedAppSettingsConfigureLocalDockerSocketInstance | src/Tests/DockerUpdateGuard.Tests/DockerUpdateGuardOptionsValidatorTests.cs |
| AC3 | DockerUpdateGuardOptionsValidatorShippedAppSettingsWithoutConnectionStringReturnsOnlyConnectionStringFailure | src/Tests/DockerUpdateGuard.Tests/DockerUpdateGuardOptionsValidatorTests.cs |
| AC4 | DockerUpdateGuardOptionsValidatorDockerInstanceWithoutNameReturnsFailureNamingSection (existing, unchanged; gets `[TestCategory("AC4")]`) | src/Tests/DockerUpdateGuard.Tests/DockerUpdateGuardOptionsValidatorTests.cs |

AC1 and AC2 fail on the unfixed file.

## Approach

- Dev: `appsettings.json:55` becomes `"Name": "Local Docker",`. Nothing else in the file changes; the validator is untouched.
- Tester (step 6, mode `coverage`; steps 4-5 skipped by tier): the three new tests build `ConfigurationBuilder().SetBasePath(HostProjectDirectory.GetPath()).AddJsonFile("appsettings.json", optional: false)`, AC1 adding `AddInMemoryCollection` with the connection string, then bind and validate with the one-argument constructor.
- `GetHostProjectPath()` moves from the telemetry tests into a shared helper instead of a second copy.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | appsettings.json | instance name |
| DockerUpdateGuard.Tests | DockerUpdateGuardOptionsValidatorTests | three tests, one category |
| DockerUpdateGuard.Tests | Helper/HostProjectDirectory.cs (new) | shared host project path |
| DockerUpdateGuard.Tests | TelemetryServiceCollectionExtensionsTests | uses the helper |

## Signatures (for the Dev's skeleton)

- `internal static class HostProjectDirectory` (namespace `DockerUpdateGuard.Tests.Helper`)
- `internal static string GetPath()` — body of today's `GetHostProjectPath()`, its existence assert included.
- Deleted: `TelemetryServiceCollectionExtensionsTests.GetHostProjectPath()` is removed (private).

## Test files

- src/Tests/DockerUpdateGuard.Tests/DockerUpdateGuardOptionsValidatorTests.cs
- src/Tests/DockerUpdateGuard.Tests/Helper/HostProjectDirectory.cs

Existing call site: `TelemetryServiceCollectionExtensionsTests.cs:137` (`GetHostProjectPath()`), adapted by the Tester.

Files no unit test can cover: none.

## Areas

- `docs/areas/docker-instances.md`: describes the shipped default instance.
- `docs/areas/hosting.md`: states the shipped settings pass validation once a connection string is supplied; the *Known gaps* entry for #288 goes.

## Documentation updates

Dev, after the code:

- `docs/areas/docker-instances.md`, *Configured instances*: add that the shipped `appsettings.json` configures one enabled instance at index `0`, `Local Docker` on `unix:///var/run/docker.sock`, whose keys `DockerUpdateGuard__DockerInstances__0__…` override. *Limits and errors* (lines 174-175): delete the sentence about the empty shipped `Name`.
- `docs/areas/hosting.md`, *Validation*: add that the shipped `appsettings.json` passes validation once a connection string is supplied (its connection string is deliberately empty). *Known gaps*: delete the #288 entry (lines 234-236).
- `README.md`: the "Recommended Linux socket configuration" table (lines 196-200) becomes the shipped instance at index `0` (heading and column name say "shipped"); the `Name` row (line 186) keeps default "none" for further entries.
- `DOCKER.md`, *Linux Docker socket usage*: note that the shipped `appsettings.json` already contains this instance.
- Compose files, `docs/ARCHITECTURE.md`: none (sweep found nothing).

## Architecture check

Configuration errors still fail startup; rules, messages and the empty shipped connection string are unchanged; one entry at index `0` keeps index overrides working.

## Security considerations

- [x] No limits.
- [x] Exceptions: none new.
- Without a mounted socket the shipped instance is unreachable, as in today's compose setups that name it.

## Decision records

- none: no decision beyond the obvious fix; the rejected empty-array alternative (issue) goes in the PR description.

## Out of scope / follow-ups

none
