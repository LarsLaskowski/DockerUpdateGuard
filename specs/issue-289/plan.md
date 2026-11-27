# Plan: Major-upgrade age check reads the injected TimeProvider

Source: Issue #289
Tier: standard — the time source of the major-upgrade age gate changes (production logic of `UpdateDetectionService`), with no security control created, changed or removed.
Security areas touched: none

## Problem / root cause

`UpdateDetectionService.IsMajorUpgradeRecommendable` (`src/DockerUpdateGuard/Images/UpdateDetectionService.cs:671`)
computes `establishedBeforeUtc` from `DateTimeOffset.UtcNow`. The service takes only the options monitor
(`:41`), so neither tests nor callers can control "now": `UpdateDetectionServiceTests.CreateRelativeTimestamp`
(`:1288`) builds tag timestamps from the real clock too, and a boundary (`== threshold`) cannot be tested
deterministically. `docs/UNIT_TESTS.md` (*Time in tests*) requires changed time-dependent service logic to take an
injected `TimeProvider`.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | The age gate reads `DateTimeOffset.UtcNow`; it is the only `UtcNow` in the file (no `DateTime.UtcNow`) | `UpdateDetectionService.cs:671`; grep `UtcNow` over the file: one hit (the issue's lines 593-635 are now 640-697) | confirmed |
| 2 | The constructor takes only `IOptionsMonitor<DockerUpdateGuardOptions>`; the service is registered scoped | `UpdateDetectionService.cs:41`; `ServiceCollectionExtensions.cs:150` (the issue says `:117`) | confirmed |
| 3 | Issue: "No `TimeProvider` is registered anywhere in the host" | `ServiceCollectionExtensions.cs:130`: `services.TryAddSingleton(TimeProvider.System);` in `AddApplicationServices`, already used by `RequestDeadlineHandler` (`:93`, `:101`) and a dozen services (`PortainerClient`, `ImageRegistrationService`, the background services); no registration change is needed | refuted |
| 4 | Boundary semantics today: the first next-major release defers only when strictly newer than the threshold (`> establishedBeforeUtc`, `:684`); the current line's latest release must be `<= establishedBeforeUtc` and `<= firstMajorReleaseAtUtc` (`:694-695`) | code read | confirmed |
| 5 | Given `first <= threshold` and `current <= first`, the condition `current <= threshold` is implied; it can decide no result alone (related observation, no change: the issue forbids other policy changes, and it still decides first under `&&`, so it is no dead branch) | `:684-695` | confirmed |
| 6 | Construction sites: `UpdateDetectionServiceTests.CreateService(ScanningOptions)` (`:1280`, the only one in that file), 23 in `RuntimeContainerScanOrchestratorTests.cs`, 2 in `RuntimeContainerScanOrchestratorFailureTests.cs`; no production `new UpdateDetectionService` | grep `new UpdateDetectionService` (the issue names 3) | confirmed |
| 7 | `UpdateDetectionServiceTests.cs` uses `DateTimeOffset.UtcNow` only in `CreateRelativeTimestamp` (`:1290`); its fixed fixture dates are 2021-11-28 … 2025-06-10 | grep | confirmed |
| 8 | The orchestrator test files read no `UtcNow`, set no `MajorUpgrade*` option (default 30 days) and use fixed fixture dates up to 2026-07-04 — so a fixed clock later than 2026-08-03 gives each of them today's real-clock result | grep `UtcNow`, `MajorUpgrade`, `new DateTimeOffset(` over both files | confirmed |
| 9 | The existing composition test `ServiceCollectionExtensionsAddDockerUpdateGuardHostRegistersExpectedServices` resolves `IRuntimeContainerScanOrchestrator`, whose constructor takes `IUpdateDetectionService` (`RuntimeContainerScanOrchestrator.cs:133`), so it constructs `UpdateDetectionService` through the container and fails if `TimeProvider` cannot be resolved | `ServiceCollectionExtensionsTests.cs:66` | confirmed |
| 10 | The test convention for a clock is a per-class `CreateClock()` returning `Substitute.For<TimeProvider>()` with `GetUtcNow().Returns(...)` | `ContainerActionServiceTests.cs:1067-1074`, `PortainerClientTests.cs:2835`, `docs/UNIT_TESTS.md` *Time in tests* | confirmed |

## Acceptance criteria

- AC1: `UpdateDetectionService` takes `TimeProvider timeProvider` as its second constructor parameter, stored in a
  readonly `_timeProvider` field, and the age gate computes the threshold as
  `_timeProvider.GetUtcNow().AddDays(-MajorUpgradeMinimumAgeDays)`; `UtcNow` no longer appears in the file.
- AC2: The host resolves `UpdateDetectionService` with the existing `TimeProvider.System` singleton registration
  (`ServiceCollectionExtensions.cs:130`, unchanged).
- AC3: Every test that constructs `UpdateDetectionService` passes an NSubstitute `TimeProvider` whose `GetUtcNow()`
  returns a fixed instant; `UpdateDetectionServiceTests` builds its relative tag timestamps from that instant, and
  `UtcNow` no longer appears in `UpdateDetectionServiceTests.cs`. Every existing test keeps its expected result.
- AC4: A next-major first release (`<major>.0.0`) published exactly `MajorUpgradeMinimumAgeDays` days before the
  clock's "now", with the current line's latest release also exactly at that threshold, is recommended (both
  comparisons inclusive).
- AC5: The same first release one second younger than the threshold is deferred (*up to date*, details "Tag '…'
  introduces a new major version and is not recommended yet").
- AC6: The same tag list and options evaluated by a service whose clock answers a time inside the waiting period
  is deferred, and recommended once the clock answers a time past it — no other input changes.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | AC4, AC6 (they fail unless the threshold comes from the injected clock); `check:` Reviewer greps `UtcNow` in `UpdateDetectionService.cs` (no hit) | `src/Tests/DockerUpdateGuard.Tests/UpdateDetectionServiceTests.cs` |
| AC2 | `ServiceCollectionExtensionsAddDockerUpdateGuardHostRegistersExpectedServices` (existing, unchanged, fact 9) | `src/Tests/DockerUpdateGuard.Tests/ServiceCollectionExtensionsTests.cs` |
| AC3 | the existing tests of the three files, adapted (*Test files*); `check:` Reviewer greps `UtcNow` in `UpdateDetectionServiceTests.cs` (no hit) | the three files below |
| AC4 | `UpdateDetectionServiceSemverMajorUpgradeExactlyAtMinimumAgeIsRecommended` | `src/Tests/DockerUpdateGuard.Tests/UpdateDetectionServiceTests.cs` |
| AC5 | `UpdateDetectionServiceSemverMajorUpgradeOneSecondBelowMinimumAgeIsDeferred` | `src/Tests/DockerUpdateGuard.Tests/UpdateDetectionServiceTests.cs` |
| AC6 | `UpdateDetectionServiceSemverDeferredMajorUpgradeIsRecommendedAfterClockAdvances` | `src/Tests/DockerUpdateGuard.Tests/UpdateDetectionServiceTests.cs` |

## Approach

1. `UpdateDetectionService`: add the field `_timeProvider` (`#region Fields`, XML doc "Clock of the age conditions"),
   the constructor parameter and its `<param>`, and replace `DateTimeOffset.UtcNow` at `:671` by
   `_timeProvider.GetUtcNow()`. No other line of the policy changes (fact 4 stays as it is).
2. Composition root: no change (fact 3). The issue's `TryAddSingleton(TimeProvider.System)` exists already.
3. Tests, fixed instant `2030-01-01T00:00:00Z` (`TimeSpan.Zero`) in every adapted file. It is later than every
   fixture date by more than the default 30 days (facts 7, 8), so no existing result changes, and later than the real
   clock, so AC4 and AC6 fail on the unfixed code (the real clock sees their releases as future ones).
   - `UpdateDetectionServiceTests`: a `private static readonly DateTimeOffset` for the instant, `CreateClock(DateTimeOffset now)`
     returning the substitute, `CreateService(ScanningOptions)` passing `CreateClock(<instant>)`, an overload
     `CreateService(ScanningOptions, TimeProvider)` for AC6, and `CreateRelativeTimestamp` built from the instant
     (an overload taking a `TimeSpan` or seconds offset for AC5 is the Tester's choice). Add `using NSubstitute;`.
   - Tag lists (default options: age 30 days, release count 2). The current tag is the latest release of its line
     (a later `1.x` would be an in-line successor), and a candidate must be published strictly after the current
     tag (`IsCandidatePublishedAfterBaseline`, `:404-408`) while the first release is looked up in all tags
     (`GetMajorLineFirstRelease`), so the lists are:
     - AC4: current `1.4.0` and `2.0.0` both at exactly now − 30 days, `2.0.1` at now − 10 days, `2.0.2` at now − 5
       days (two candidates after the baseline); *update available*, recommended `2.0.2`.
     - AC5: `1.4.0` at now − 200 days, `2.0.0` at now − 30 days + 1 second, `2.0.1` at now − 10 days; *up to date*
       with the deferred details naming `2.0.1`.
     - AC6: `1.4.0` at now − 200 days, `2.0.0` at now − 20 days, `2.0.1` at now − 10 days; the clock at now defers,
       a second service whose clock answers now + 11 days recommends `2.0.1`.
   - Orchestrator test files: each gets a `private static UpdateDetectionService CreateUpdateDetectionService(IOptionsMonitor<DockerUpdateGuardOptions> optionsMonitor)`
     that passes its own fixed clock, and every `new UpdateDetectionService(optionsMonitor)` becomes a call of it.
     No assertion changes.

No new user-visible output; texts are unchanged. Behavior in production is unchanged: the registered clock is
`TimeProvider.System`.

Early draft PR: no

## Options considered

- **Inject `TimeProvider` into the constructor** — chosen: matches `docs/UNIT_TESTS.md` and the other services
  (fact 10), keeps `IUpdateDetectionService` stable.
- **Pass the evaluation time as a parameter of `IUpdateDetectionService.Evaluate`** (the issue's alternative) — lost:
  it changes the interface and every caller (`RuntimeContainerScanOrchestrator`) for no gain the injected clock does
  not give; evaluation stays I/O-free either way, reading the clock is no I/O.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| `DockerUpdateGuard` | `Images/UpdateDetectionService.cs` | constructor, field, age threshold |
| `DockerUpdateGuard.Tests` | `UpdateDetectionServiceTests.cs` | clock helpers, adapted helpers, 3 new tests |
| `DockerUpdateGuard.Tests` | `RuntimeContainerScanOrchestratorTests.cs`, `RuntimeContainerScanOrchestratorFailureTests.cs` | construction through a helper with a fixed clock |

## Signatures (for the Dev's skeleton)

- Changed: `public UpdateDetectionService(IOptionsMonitor<DockerUpdateGuardOptions> options, TimeProvider timeProvider)`
  (two parameters, one line). The old constructor is removed. The skeleton stores the argument in `_timeProvider`
  (S4487 pragma until step 6) and leaves `IsMajorUpgradeRecommendable` reading `UtcNow`; the registration it needs
  exists already (fact 3), so AC2's test runs green from the skeleton on.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/UpdateDetectionServiceTests.cs`
- `src/Tests/DockerUpdateGuard.Tests/RuntimeContainerScanOrchestratorTests.cs`
- `src/Tests/DockerUpdateGuard.Tests/RuntimeContainerScanOrchestratorFailureTests.cs`

Existing call sites of the changed constructor (fact 6): the **Dev** adapts all 26 in step 4 mechanically by passing
`TimeProvider.System` as second argument, so *Build* passes; the **Tester** replaces them in step 5 by the fixed
clocks of *Approach* 3 (AC3). `ServiceCollectionExtensionsTests.cs` is unchanged.

Step 5: AC5's test cannot fail on the unfixed code (the real clock also defers a release in 2029) and is deferred to
step 6; AC4 and AC6 fail on the skeleton. Adapted existing tests built from the 2030 instant (e.g.
`UpdateDetectionServiceSemverRecommendsEstablishedMajorUpgrade`) also fail on the skeleton and pass after step 6.

Files of the change no unit test can cover: none.

## Areas

- `docs/areas/update-detection.md` (-20 words): *Major-upgrade policy* — the bullet "The age conditions are measured
  against the wall clock at evaluation time; …" now says they are measured against the application's clock at
  evaluation time and that a release published exactly the configured number of days before counts as old enough
  (both age conditions inclusive), keeping the later-scan sentence; *Known gaps* — the #289 entry is removed.

## Documentation updates

- `docs/areas/update-detection.md` — as *Areas*, by the Dev once the code is green.
- `docs/ARCHITECTURE.md` — none: `UpdateDetectionService` stays "a pure evaluator (no network or database I/O)"
  (`:328`, `:589`); swept for clock, `TimeProvider`, `UtcNow`: no statement about this service's time source.
- `.squad/project.md` — none (the no-I/O guarantee at `:65` stays true). `docs/UNIT_TESTS.md` — none (no new shared
  double; *Time in tests* already prescribes this). `README.md` — none (no configuration change). No changelog file
  exists; the PR carries its release-notes label (CONTRIBUTING).

## Architecture check

Guarantee "Update evaluation and tag comparison perform no I/O" (`.squad/project.md:65`, `ARCHITECTURE.md:589`):
preserved — reading `TimeProvider` is no I/O, and evaluation now depends only on its inputs and the injected clock.
The composition root keeps its single `TimeProvider.System` registration.

## Security considerations

- [x] No limits.
- [x] Exceptions reaching a user-visible message: none (no new throw path).

## Decision records

- none: no decision beyond the obvious fix (the project's convention in `docs/UNIT_TESTS.md` decides it).

## Out of scope / follow-ups

- Other services still reading `DateTimeOffset.UtcNow` move when they are changed (`docs/UNIT_TESTS.md`).
- The implied condition of fact 5 stays; no follow-up proposed.

## Challenge notes

Devil's Advocate, mode plan, specs/issue-289 (/home/user/DockerUpdateGuard/specs/issue-289/plan.md).

The plan holds up. I checked each factual claim against the code:
- Fact 3 (the issue's claim is refuted) is right. `src/DockerUpdateGuard/ServiceCollectionExtensions.cs:130` already has `services.TryAddSingleton(TimeProvider.System);`, and `:150` registers the service as scoped. No registration change is needed.
- Fact 1 is right. The only `UtcNow` in `src/DockerUpdateGuard/Images/UpdateDetectionService.cs` is at `:671`, and the boundary comparisons are `>` at `:684` and `<=` at `:694-695`, as fact 4 says.
- The tag-list designs give the results the plan expects:
  - `GetHigherVersionCandidates` (`:59-75`) sorts by version, highest first, so `nextMajorCandidates[0]` (`:633`) is the highest next-major tag. The deferred details in AC5 therefore name `2.0.1`, and AC4 recommends `2.0.2`.
  - In AC4, `2.0.0` is published at the same time as `1.4.0`. `IsCandidatePublishedAfterBaseline` (`:404-408`, strict `>`) therefore leaves it out of the candidates, which leaves two (`2.0.1`, `2.0.2`) and meets the release count of 2. `GetMajorLineFirstRelease` (`:460-473`) still finds `2.0.0`.
  - In AC6, with the clock at now + 11 days, `2.0.0` is 31 days old, so `2.0.1` is recommended.
  - AC4 and AC6 fail on the unfixed code because the real clock sees the 2029-12 releases as in the future. The plan already notes that AC5 cannot fail before the fix.
- The 2030-01-01 fixed instant is safe for the existing tests. The fixed fixture dates in `UpdateDetectionServiceTests.cs` (2021 to 2025) are already more than 30 days old under today's real clock. The major-upgrade tests use only `CreateRelativeTimestamp` (`:680-974`), so their dates move with the instant.
- The S4487 skeleton pragma is one of the ids allowed in `.squad/stack.md:245`.

One minor objection:

1. minor: the number of constructor call sites is wrong. Fact 6 and *Test files* say 26, with 23 in `RuntimeContainerScanOrchestratorTests.cs`. `grep -o "UpdateDetectionService("` finds 22 there (`:400` … `:3177`), 2 in `RuntimeContainerScanOrchestratorFailureTests.cs` and 1 in `UpdateDetectionServiceTests.cs:1280`, so 25 in total. Why it is minor: the Dev and Tester replace every hit of the grep anyway, so the plan does not need to change. Instruction for the Dev and Tester: adapt every `new UpdateDetectionService(` hit that `grep -rn "new UpdateDetectionService(" src` lists (25 today) rather than working towards a count. The Reviewer confirms that the grep shows no one-argument call left.
