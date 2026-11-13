# Plan: A failing interval lookup no longer stops a background job or the host

Source: Issue #281
Tier: standard — a behavior change of the background job loop (error handling of the interval lookup) that creates, changes or removes no security control.
Security areas touched: Logging and telemetry of external data (`Images/ImageHostLoggingExtensions.cs`: a new warning that carries the exception of a failed interval lookup)

## Problem / root cause

`src/DockerUpdateGuard/Images/ScheduledBackgroundService.cs` calls `GetInterval()` outside any `try`: at line 133
for the start-up log line and at line 145 before every wait. Only `ExecuteStartupSafelyAsync` (71–90) and
`ExecuteSafelyAsync` (97–120) catch. An exception of `GetInterval()` therefore leaves `ExecuteAsync`, the job's loop
ends, and with the default `BackgroundServiceExceptionBehavior.StopHost` the host stops. The lookup throws
`OptionsValidationException` after a reload that fails validation (all seven jobs read
`IOptionsMonitor<DockerUpdateGuardOptions>.CurrentValue`) and a database exception in
`OwnImageBaseRefreshBackgroundService.GetInterval()` (`Count` at line 56).

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | `GetInterval()` runs outside error handling at the start-up log line and before every wait | `ScheduledBackgroundService.cs:131-134`, `:145`; catches only at 76-89, 104-119 | confirmed |
| 2 | Every job's `GetInterval()` reads `IOptionsMonitor<DockerUpdateGuardOptions>.CurrentValue` | the issue lists six files; `VulnerabilityRefreshBackgroundService.cs` (`GetInterval`, `VulnerabilityRefreshIntervalMinutes`) is the seventh, missing from the issue's list | confirmed |
| 3 | The observed-image refresh queries the database synchronously on every lookup | `OwnImageBaseRefreshBackgroundService.cs:51-60`, `Count` at 56, no token | confirmed |
| 4 | After an invalid reload `CurrentValue` throws `OptionsValidationException` on every read, caches nothing, and returns the new value once the configuration is valid again | scratch experiment (in-memory provider, `Validate`): two reads after the invalid reload threw, the read after a valid reload returned it | confirmed |
| 5 | A reload of a JSON file that fails validation does not crash the process by itself (the monitor's change callback throws on the reload thread) | scratch experiment with `AddJsonFile(reloadOnChange: true)`: process alive, monitor throws, recovers on a valid write; `IConfigurationRoot.Reload()` called directly throws `AggregateException` to its caller | confirmed |
| 6 | `IOptions<T>.Value` is not a startup snapshot: created on its first read, which throws when that read comes after an invalid reload | scratch experiment: first `IOptions<O>.Value` after the invalid reload threw; the issue's option (a) fails as worded | confirmed |
| 7 | Options are validated by `DockerUpdateGuardOptionsValidator` with `ValidateOnStart()` | `ServiceCollectionExtensions.cs:53-56` | confirmed |
| 8 | No `BackgroundServiceExceptionBehavior` is configured | no match for `HostOptions`/`BackgroundServiceExceptionBehavior` in `src/DockerUpdateGuard` | confirmed |
| 9 | `TimeProvider` is already registered, and the jobs are registered with `AddHostedService<T>`, which resolves a new constructor parameter itself | `ServiceCollectionExtensions.cs:102` (`TryAddSingleton(TimeProvider.System)`), `:135-141`; the issue's "update their registrations" is not needed | confirmed |
| 10 | `Task.Delay(TimeSpan, TimeProvider, CancellationToken)` with a non-system provider calls `CreateTimer(callback, state, dueTime = interval, period = infinite)` and completes when the callback runs | scratch experiment with a `TimeProvider` subclass: `dueTime 00:05:00`, completed after the callback | confirmed |
| 11 | Validation failure messages name keys, ranges, configured instance names and, for a client certificate, its path; no credential value | `DockerUpdateGuardOptionsValidator.cs:72-490` (`failures.Add` texts; 490 names `CertificatePath`) | confirmed |
| 12 | Event ID 2005 is free | no `EventId = 2005` in `src/` | confirmed |
| 13 | The docs name "Open question 1" in both area documents | both documents carry the entry under *Known gaps* (`scan-engine.md:191-194`, `hosting.md:205-209`), no *Open questions* section | refuted |
| 14 | `ServiceCollectionExtensionsAddDockerUpdateGuardHostRegistersExpectedServices` constructs all seven jobs; their constructors now read `CurrentValue`, so its configuration must validate | to verify: the Dev runs that test after the skeleton (step 4); its input at `ServiceCollectionExtensionsTests.cs:31-48` sets `Registry`, `Vulnerabilities:Enabled` defaults to `false` | unverified |

## Acceptance criteria

- AC1: When `GetInterval()` throws (any exception except a cancellation while the stopping token is cancelled), at the
  start-up log line or before a wait, the exception does not leave `ExecuteAsync`: the job logs warning event 2005
  with its name, the exception and the interval it falls back to, waits its last interval read successfully, and runs
  again after that wait.
- AC2: A reloaded configuration that fails validation does not end the job's task; the job keeps waiting its last
  valid interval, and once a valid configuration is loaded the next wait uses the new interval.
- AC3: A database error while the observed-image refresh counts the enabled images does not end the job's task; the
  job waits its last known (dynamic) interval.
- AC4: When the first interval lookup of a job fails, the job waits the initial interval it read from its
  configuration when it was created; for the observed-image refresh that is `OwnImageBaseScanIntervalMinutes`.
- AC5: A cancellation thrown by the lookup while the stopping token is cancelled ends the job quietly: no event
  2005, no event 2004, the task does not fault.
- AC6: The area documents, `docs/ARCHITECTURE.md` and the telemetry list of what logs carry describe the new
  behavior, and the two *Known gaps* entries for #281 are gone.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | ScheduledBackgroundServiceExecuteAsyncIntervalLookupThrowsBeforeWaitUsesLastKnownIntervalAsync, ScheduledBackgroundServiceExecuteAsyncIntervalLookupThrowsAtStartupLogDoesNotFaultAsync | src/Tests/DockerUpdateGuard.Tests/ScheduledBackgroundServiceTests.cs |
| AC2 | ScheduledBackgroundServiceExecuteAsyncIntervalLookupRecoversUsesNewIntervalAsync; ScanCleanupBackgroundServiceExecuteAsyncReloadFailsValidationKeepsLastValidIntervalAsync, ScanCleanupBackgroundServiceExecuteAsyncReloadValidAgainUsesNewIntervalAsync | src/Tests/DockerUpdateGuard.Tests/ScheduledBackgroundServiceTests.cs; src/Tests/DockerUpdateGuard.Tests/ScanCleanupBackgroundServiceTests.cs |
| AC3 | OwnImageBaseRefreshBackgroundServiceExecuteAsyncObservedImageCountFailsUsesLastKnownIntervalAsync | src/Tests/DockerUpdateGuard.Tests/OwnImageBaseRefreshBackgroundServiceTests.cs |
| AC4 | ScheduledBackgroundServiceExecuteAsyncFirstIntervalLookupThrowsUsesInitialIntervalAsync; OwnImageBaseRefreshBackgroundServiceExecuteAsyncFirstIntervalLookupFailsUsesOwnImageBaseScanIntervalAsync | src/Tests/DockerUpdateGuard.Tests/ScheduledBackgroundServiceTests.cs; src/Tests/DockerUpdateGuard.Tests/OwnImageBaseRefreshBackgroundServiceTests.cs |
| AC5 | ScheduledBackgroundServiceExecuteAsyncIntervalLookupCancelledDuringShutdownIsNotLoggedAsync | src/Tests/DockerUpdateGuard.Tests/ScheduledBackgroundServiceTests.cs |
| AC6 | check: the Reviewer reads the four documents of *Documentation updates* against the code in step 8 | — |

How the tests observe a wait: the job gets an NSubstitute `TimeProvider` (`docs/UNIT_TESTS.md`, *Time in tests*)
whose `CreateTimer` records `dueTime` and the callback with its state and signals the test; a wait "elapses" when the
test invokes the recorded callback (fact 10). The interval waited is the recorded `dueTime`; "does not fault" is the
job's task (`RunAsync` or `ExecuteTask`) still running before the test cancels, and completing without exception
after. AC2 uses a real `OptionsMonitor<DockerUpdateGuardOptions>` from a `ServiceCollection` with an in-memory
configuration bound and the real `DockerUpdateGuardOptionsValidator` registered; a reload is simulated by setting the
key (`DockerUpdateGuard:Scanning:CleanupIntervalMinutes` = `0`, then a valid value) and calling
`IOptionsMonitorCache<DockerUpdateGuardOptions>.TryRemove(Options.DefaultName)` — what the monitor does on a change —
not `IConfigurationRoot.Reload()`, which throws to its caller (fact 5). AC3 makes the count fail after a first
successful lookup with enabled images enough for a dynamic interval above the floor (e.g. the SQLite test database
closed between two lookups). AC1–AC4 assert event 2005 with the job name and level `Warning`; every assertion has a
message.

## Approach

`ScheduledBackgroundService`:

- New private fields `_timeProvider` and `_lastKnownInterval`; the constructor sets `_lastKnownInterval` to
  `initialInterval`.
- New private method `GetIntervalSafely(string backgroundServiceName, CancellationToken stoppingToken)`: calls
  `GetInterval()`, stores and returns the result; `catch (OperationCanceledException) when
  (stoppingToken.IsCancellationRequested)` returns `_lastKnownInterval` without logging; `catch (Exception)` logs
  `BackgroundServiceIntervalLookupFailed(exception, backgroundServiceName, _lastKnownInterval.TotalMinutes)` and
  returns `_lastKnownInterval`. It never throws.
- `ExecuteAsync` uses it at line 133 (inside the existing `IsEnabled` check) and line 145; the wait becomes
  `Task.Delay(interval, _timeProvider, stoppingToken)`. Nothing else in the loop changes: no overlap, a running wait
  is neither shortened nor extended, the cleanup's first full pass still follows one interval.

The jobs: each constructor takes `TimeProvider timeProvider` as its last parameter and passes it with its initial
interval to the base. The six jobs with a fixed key get a private static helper that turns `DockerUpdateGuardOptions`
into their interval, used by the constructor (`optionsMonitor.CurrentValue`) and by `GetInterval()`, so the key is
written once. The observed-image refresh passes `TimeSpan.FromMinutes(optionsMonitor.CurrentValue.Scanning.OwnImageBaseScanIntervalMinutes)`
(no database access in the constructor); its `GetInterval()` is unchanged.

Log layout: event 2005, `Warning`, message `Scheduled background service {BackgroundServiceName} could not determine
its interval and waits its last known interval of {FallbackIntervalMinutes} minutes`, the exception attached; no
other configuration value as a parameter.

## Options considered

1. Catch in the base class, last known interval seeded by a constructor-supplied initial interval — chosen.
2. Virtual `GetFallbackInterval()` reading `IOptions<T>.Value` (the issue's recommendation) — lost: the value is
   created on first read and throws after an invalid reload (fact 6).
3. `BackgroundServiceExceptionBehavior.Ignore` — lost: the job's loop stays dead (the issue rules it out).
4. Handling in each job's `GetInterval()` — lost: seven copies.
5. Keep the whole last valid configuration for every consumer — lost: beyond the issue, changes every options read.

Details in [0025](../../docs/decisions/0025-a-failing-interval-lookup-keeps-the-jobs-last-known-interval.md).

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Images/ScheduledBackgroundService.cs` | constructor, fields, safe lookup, `TimeProvider` wait |
| DockerUpdateGuard | `Images/ImageHostLoggingExtensions.cs` | event 2005 |
| DockerUpdateGuard | the seven `Images/*BackgroundService.cs` jobs | constructor parameter, initial interval |
| DockerUpdateGuard.Tests | `Helper/TestScheduledBackgroundService.cs`, `Helper/TestScanCleanupBackgroundService.cs` | base call adapted; extended for the new tests |

## Signatures (for the Dev's skeleton)

Deleted: the constructor `ScheduledBackgroundService(ILogger logger)` is removed and replaced by

- `protected ScheduledBackgroundService(ILogger logger, TimeProvider timeProvider, TimeSpan initialInterval)`
- `public DockerInstanceDiscoveryBackgroundService(ILogger<DockerInstanceDiscoveryBackgroundService> logger, IOptionsMonitor<DockerUpdateGuardOptions> optionsMonitor, IServiceScopeFactory serviceScopeFactory, TimeProvider timeProvider)`
- `public DockerHubAccountImageDiscoveryBackgroundService(ILogger<DockerHubAccountImageDiscoveryBackgroundService> logger, IOptionsMonitor<DockerUpdateGuardOptions> optionsMonitor, IServiceScopeFactory serviceScopeFactory, TimeProvider timeProvider)`
- `public OwnImageBaseRefreshBackgroundService(ILogger<OwnImageBaseRefreshBackgroundService> logger, IOptionsMonitor<DockerUpdateGuardOptions> optionsMonitor, IServiceScopeFactory serviceScopeFactory, TimeProvider timeProvider)`
- `public RuntimeContainerRefreshBackgroundService(ILogger<RuntimeContainerRefreshBackgroundService> logger, IOptionsMonitor<DockerUpdateGuardOptions> optionsMonitor, IServiceScopeFactory serviceScopeFactory, TimeProvider timeProvider)`
- `public ResourceStatisticsRefreshBackgroundService(ILogger<ResourceStatisticsRefreshBackgroundService> logger, IOptionsMonitor<DockerUpdateGuardOptions> optionsMonitor, IServiceScopeFactory serviceScopeFactory, TimeProvider timeProvider)`
- `public VulnerabilityRefreshBackgroundService(ILogger<VulnerabilityRefreshBackgroundService> logger, IOptionsMonitor<DockerUpdateGuardOptions> optionsMonitor, IServiceScopeFactory serviceScopeFactory, TimeProvider timeProvider)`
- `public ScanCleanupBackgroundService(ILogger<ScanCleanupBackgroundService> logger, IOptionsMonitor<DockerUpdateGuardOptions> optionsMonitor, IServiceScopeFactory serviceScopeFactory, TimeProvider timeProvider)`
- `public static partial void BackgroundServiceIntervalLookupFailed(this ILogger logger, Exception exception, string backgroundServiceName, double fallbackIntervalMinutes)` (in `ImageHostLoggingExtensions`, event 2005)

The old three-parameter job constructors are removed with them. Names follow the existing `BackgroundService…`
log methods and the `_camelCase` field rule.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/ScheduledBackgroundServiceTests.cs` (existing)
- `src/Tests/DockerUpdateGuard.Tests/ScanCleanupBackgroundServiceTests.cs` (existing)
- `src/Tests/DockerUpdateGuard.Tests/OwnImageBaseRefreshBackgroundServiceTests.cs` (new)

Existing test code that calls a changed signature: `Helper/TestScheduledBackgroundService.cs` (base call) and
`Helper/TestScanCleanupBackgroundService.cs` (base call) — the **Dev** adapts both in step 4, mechanically: pass
`TimeProvider.System` and, for the first, its interval as initial interval; their own constructors and the existing
tests stay unchanged. The **Tester** then extends both helpers in step 5 (a `TimeProvider` and an interval sequence
that can throw for the first, a `TimeProvider` and a way to run `ExecuteAsync` for the second). The existing tests
keep inputs no criterion handles differently. `ServiceCollectionExtensionsTests` resolves the jobs (fact 14): if its
configuration fails validation now, the Dev adds the missing valid keys to its input, no assertion touched.

Files no unit test can cover: none.

## Areas

- Scan engine: *Background jobs* and *Failure isolation* gain the fallback; the #281 *Known gaps* entry goes; record
  0025 under *Related decisions*.
- Hosting and configuration: *Configuration reload* states what an invalid reload does now; its #281 *Known gaps*
  entry goes.
- Telemetry: *Credentials and external data* lists the new warning's exception.

## Documentation updates

All by the Dev, against the code as built:

- `docs/areas/scan-engine.md` *Background jobs*, bullet "The interval is read again before every wait": when the read
  fails (an invalid reloaded configuration; for the observed-image refresh also a database error) the job logs a
  warning and waits its last interval read successfully — before its first successful read, the interval of the
  configuration it read when the host started (observed-image refresh: `OwnImageBaseScanIntervalMinutes`).
  *Failure isolation*: a failing interval read stops neither the job, the other jobs nor the host; runs during an
  invalid configuration fail and are logged as any failed run. Remove the #281 *Known gaps* entry; add 0025 to
  *Related decisions*.
- `docs/areas/hosting.md` *Configuration reload*, the bullet "A reloaded configuration is validated…": replace
  "(see *Known gaps*)" with the outcome — the host keeps running, background jobs keep their last known interval
  ([Scan engine](scan-engine.md)), other reads fail where they are made and are logged by their run or page. Remove
  the #281 *Known gaps* entry.
- `docs/areas/telemetry.md` *Credentials and external data*, the sentence on "the exception with its message": add
  the warning of a failed interval lookup, whose message can name the database host or carry the configuration
  validation failures (key names, configured instance names, a client certificate path).
- `docs/ARCHITECTURE.md` §8 *Background job engine*: the base class takes an initial interval and a `TimeProvider`,
  catches a failing `GetInterval()`, logs it and keeps the last known interval, so neither the job nor the host stops;
  link record 0025.

No README, configuration key or migration change; the PR template asks for nothing else.

## Architecture check

Guarantees in `.squad/project.md` are untouched. The scan engine's *Failure isolation* promise ("does not stop the
job, the other jobs or the host") is restored for the interval read. Layering unchanged: the composition root already
registers `TimeProvider`. Run overlap, wait semantics and the cleanup's delayed first pass are unchanged (*Approach*).

## Security considerations

- [x] no limits.
- [x] Exceptions reaching a user-visible message: none — the warning goes to the log only. What it may carry is
  fact 11 plus, for a database error, the provider's message (can name the database host, as event 2004 already
  does for the same database errors in a failed run).

## Decision records

- `docs/decisions/0025-a-failing-interval-lookup-keeps-the-jobs-last-known-interval.md` (Proposed)

## Out of scope / follow-ups

- Other readers of `CurrentValue` (orchestrators, pages) still fail while the configuration is invalid; they run
  inside run-level or UI error handling (the issue keeps them).
- `ScanCleanupBackgroundService` keeps reading `DateTimeOffset.UtcNow` for its cutoffs: this change does not touch its
  time-dependent logic.
- Related, proposed follow-up issue: "Observed-image interval lookup blocks on a synchronous database query" — the
  lookup's `Count` takes no stopping token and runs synchronously on the job's thread, so during a database outage
  the retrying execution strategy delays both the next wait and host shutdown.
