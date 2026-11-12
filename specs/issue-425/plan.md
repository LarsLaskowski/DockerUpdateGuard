# Plan: Meet CA1000 (static members on generic types) of the shared .NET rule baseline

Source: Issue #425
Tier: trivial — the factories move to a non-generic class and every call site is rewritten mechanically; no behavior or control flow changes.
Security areas touched: build configuration (`.editorconfig`, the `squad:dotnet-rules` block, CA1000 back to its `AnalysisLevel` severity); call-site rewrites in `Docker/DockerInstanceClient.cs`, `DockerHub/DockerHubClient.cs`, `Images/OciRegistryClient.cs`, `Vulnerabilities/DockerScoutVulnerabilityProvider.cs`, `Vulnerabilities/TrivyVulnerabilityProvider.cs` — no control created, changed or removed

## Problem / root cause

`src/DockerUpdateGuard/Infrastructure/ExternalOperationResult{T}.cs` lines 35, 49, 59, 69, 79, 89 declare public static factories on the generic `ExternalOperationResult<T>` (CA1000). The rule is held at `suggestion` by `DOTNET_RULES_PENDING` (`.squad/tools/squad_settings.py` line 26, `.editorconfig` lines 546–547).

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | CA1000 fires on the six factories, in this file only | read of the file; no other generic type in `src/` declares a public or protected static member (grep) | confirmed |
| 2 | Without the pending entry, CA1000 is a warning under `latest-recommended`, so an error with `TreatWarningsAsErrors` | scratch library with `AnalysisLevel` `latest-recommended`: `warning CA1000` on a generic type's static factory | confirmed |
| 3 | "the type argument is inferred": only `Succeeded(T data)` can infer `T`; the five message factories take no `T` | C# infers no type argument from a return type | refuted |
| 4 | A generic method whose type parameter is not in its parameter list (S4018) raises no diagnostic | same scratch build with SonarAnalyzer.CSharp 10.35.0.4138: only CA1000 reported; S4018 is not in `.editorconfig` | confirmed |
| 5 | 333 call sites: 156 production (10 files), 177 test (6 files); all single-line `ExternalOperationResult<X>.Name(` | grep over `src/` | confirmed |
| 6 | Nothing outside the file constructs `ExternalOperationResult<T>` (`new`, reflection) | grep `new ExternalOperationResult`, `Activator` | confirmed |
| 7 | Reihitsu reports nothing on the moved factories or the rewritten calls | to verify: Code Officer, step 7 *Analyzer gate* | unverified |

## Acceptance criteria

- AC1: `ExternalOperationResult.Succeeded<T>(data)` gives status `Succeeded`, that data and no message; `NotConfigured`, `Unsupported`, `NotFound`, `Failed` and `Unknown` `<T>(message)` give their status, that message and default data.
- AC2: `ExternalOperationResult<T>` declares no externally visible static member; the build reports no CA1000.
- AC3: `DOTNET_RULES_PENDING` has no `CA1000` entry, and the `squad:dotnet-rules` block of `.editorconfig` no CA1000 line.
- AC4: every existing test passes unchanged in its assertions.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | ExternalOperationResultSucceededCarriesDataWithoutMessage, ExternalOperationResultFailureFactoriesCarryStatusAndMessageWithoutData (all five factories, each with its own status) | `src/Tests/DockerUpdateGuard.Tests/ExternalOperationResultTests.cs` |
| AC2 | CI: *Build* and *Analyzer gate* in the step-7 stamp, with CA1000 at its `AnalysisLevel` severity | — |
| AC3 | check: `python3 .squad/tools/dotnet-rules.py --check` exits 0, and grep finds no `CA1000` in `squad_settings.py` or `.editorconfig` (Reviewer, step 8) | — |
| AC4 | CI: *Test with coverage* in the step-7 stamp | — |

## Approach

1. New `Infrastructure/ExternalOperationResult.cs`: the six factories (region `Static methods`, `<typeparam>` docs) call the internal constructor.
2. `ExternalOperationResult{T}.cs`: an internal constructor sets the now get-only properties.
3. Every call site `ExternalOperationResult<X>.Name(args)` becomes `ExternalOperationResult.Name<X>(args)`, the type argument kept on all of them (also `Succeeded`: inference from a `List<…>` argument would give a different, invariant `T`). Dev, test call sites included, no assertion touched.
4. At the start of step 6 the orchestrator sets `squad_settings.py` line 26 to `DOTNET_RULES_PENDING = {"CA1716": "#427", "CA1848": "#426"}`; the Dev then runs `python3 .squad/tools/dotnet-rules.py`, which drops `.editorconfig` lines 546–547 only.

## Options considered

- Static members kept `internal` on the generic type (CA1000 checks only externally visible members): lost — meets the rule's letter only, and the issue asks for the move.
- Public `init` setters instead of an internal constructor: lost — any caller could build inconsistent results.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `ExternalOperationResult` (new), `ExternalOperationResult<T>`, 10 files of fact 5 | factories moved, call sites |
| DockerUpdateGuard.Tests | 6 files of fact 5, `ExternalOperationResultTests` (new) | call sites, AC1 tests |

## Signatures (for the Dev's skeleton)

- `public static class ExternalOperationResult`
- `public static ExternalOperationResult<T> Succeeded<T>(T data)`
- `public static ExternalOperationResult<T> NotConfigured<T>(string message)`, and `Unsupported<T>`, `NotFound<T>`, `Failed<T>`, `Unknown<T>` with the same parameter
- `internal ExternalOperationResult(ExternalOperationStatus status, T? data, string? message)` on `ExternalOperationResult<T>`
- `public ExternalOperationStatus Status { get; }`, `public T? Data { get; }`, `public string? Message { get; }`

Deleted: the six static factories and the private `Create` of `ExternalOperationResult<T>`.

## Test files

`src/Tests/DockerUpdateGuard.Tests/ExternalOperationResultTests.cs` (Tester, step 6).

Existing test call sites: `DockerHubAccountImageDiscoveryServiceTests.cs`, `Helper/ConcurrencyTrackingVulnerabilityProvider.cs`, `ImageScanOrchestratorTests.cs`, `RegistryBaseImageResolverTests.cs`, `RuntimeContainerScanOrchestratorTests.cs`, `VulnerabilityEnrichmentServiceTests.cs` — adapted by the Dev (old signature goes away), checked by the Tester.

Files no unit test can cover: none.

## Areas

none (no change in behavior).

## Documentation updates

- `docs/ARCHITECTURE.md` §6 (line 219), Dev: after `(Infrastructure\ExternalOperationResult{T}.cs)` add that results are created through the factories of the static `ExternalOperationResult` (`Infrastructure\ExternalOperationResult.cs`).
- Sweep: `docs/areas/`, `docs/UNIT_TESTS.md`, `.squad/project.md` and `.squad/stack.md` name only the type, which stays. No changelog exists.

## Architecture check

No guarantee touched: clients answer the same results.

## Security considerations

- [x] no limits.
- [x] none: no message text changes.

## Decision records

- none: no decision beyond the obvious fix.

## Out of scope / follow-ups

CA1716 (#427), CA1848 (#426).
