# Plan: Observed-image interval lookup blocks on a synchronous database query

Source: Issue #441
Tier: standard — the interval lookup of every job becomes asynchronous and cancellable, a behavior change of the scan engine that creates, changes or removes no security control.
Security areas touched: none

## Problem / root cause

`OwnImageBaseRefreshBackgroundService.GetInterval()` (`src/DockerUpdateGuard/Images/OwnImageBaseRefreshBackgroundService.cs:55`)
counts the enabled observed images with the synchronous `dbContext.ObservedImages.Count(...)` (line 60), and the
contract it implements is synchronous and has no token: `protected abstract TimeSpan GetInterval()`
(`src/DockerUpdateGuard/Images/ScheduledBackgroundService.cs:52`), called synchronously before every wait (line 190,
through `GetIntervalSafely`, line 86–110) and for the start log (line 178). The database context retries with
`EnableRetryOnFailure` (`src/DockerUpdateGuard/ServiceCollectionExtensions.cs:66`); on the synchronous path EF Core's
execution strategy sleeps between attempts without any token, so during an outage the job's thread is blocked for
all retries, and a shutdown waits for them.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | The observed-image lookup runs `ObservedImages.Count` synchronously and without the stopping token (issue) | `OwnImageBaseRefreshBackgroundService.cs:55–64`, count at line 60 | confirmed |
| 2 | The base class calls the lookup synchronously, without a token, before every wait and for the start log | `ScheduledBackgroundService.cs:52`, `:90`, `:178`, `:190` | confirmed |
| 3 | The database context uses a retrying execution strategy | `ServiceCollectionExtensions.cs:66` (`EnableRetryOnFailure(MaxConnectionRetryCount, MaxConnectionRetryDelaySeconds)`) | confirmed |
| 4 | EF Core's synchronous execution path waits between retries with `Thread.Sleep`, which no token interrupts | EF Core source `src/EFCore/Storage/ExecutionStrategy.cs` (`ExecuteImplementation`); the 10.0.14 assembly in the NuGet cache references `Sleep` | confirmed |
| 5 | EF Core's asynchronous path passes the query's token to each attempt and to the delay between retries, so a cancelled token ends the retries | to verify: the AC2 test, which fails on today's synchronous count and passes only when the retries stop on cancellation — Tester, step 5 | unverified |
| 6 | A failing lookup no longer stops the host, a cancellation during shutdown is not logged (issue, #281) | `ScheduledBackgroundService.cs:86–110`; decision 0025 | confirmed |
| 7 | Only the observed-image job's lookup touches the database; the other six return their configured interval | `GetInterval()` of the six other jobs in `src/DockerUpdateGuard/Images/*BackgroundService.cs` all return `GetConfiguredInterval(_optionsMonitor.CurrentValue)` | confirmed |
| 8 | The query's token reaches the command interceptor unchanged on the asynchronous path (AC1 test double) | to verify: the AC1 test, Tester, step 5 | unverified |

## Acceptance criteria

- AC1: The observed-image interval lookup counts the enabled observed images through the asynchronous query path
  and hands the job's stopping token to that query; no synchronous query is executed for the lookup.
- AC2: When the job is stopped while the retrying execution strategy repeats a failing count, the lookup ends
  without a further attempt, the job's task completes without fault, and no lookup warning (event 2005) is logged.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | OwnImageBaseRefreshBackgroundServiceExecuteAsyncIntervalLookupCountsWithStoppingTokenAsync | src/Tests/DockerUpdateGuard.Tests/OwnImageBaseRefreshBackgroundServiceTests.cs |
| AC2 | OwnImageBaseRefreshBackgroundServiceExecuteAsyncShutdownDuringCountRetriesStopsRetryingAsync | src/Tests/DockerUpdateGuard.Tests/OwnImageBaseRefreshBackgroundServiceTests.cs |

Cases, for the Tester (both through `TestOwnImageBaseRefreshBackgroundService.RunAsync` on a seeded SQLite
in-memory database, as the existing tests of the file; `CreateServiceProvider` gets a parameter for the context
options it needs):

- AC1: a `DbCommandInterceptor` registered on the job's context records every reader execution — synchronous
  (`ReaderExecuting`) and asynchronous (`ReaderExecutingAsync`, with its token). The test cancels the run after the
  first wait started (`WaitRecordingTimeProvider`) and asserts: at least one asynchronous execution whose token is
  the run's token, no synchronous one, and the wait equals the dynamic interval of the seeded count.
- AC2: no schema (every count fails), and a test execution strategy on the job's context (a subclass of EF Core's
  `ExecutionStrategy` with a retry count of at least 3, a zero delay, `ShouldRetryOn` returning `true` and
  counting its calls, cancelling the run's `CancellationTokenSource` on its first call). Asserts: `ShouldRetryOn`
  was called exactly once, `RunAsync` completes without exception, no entry with event id 2005 or 2004. On today's
  code the synchronous path retries to the limit and logs 2005, so the test fails first. No real-clock wait.

## Approach

1. `ScheduledBackgroundService`: replace `GetInterval()` by the abstract `GetIntervalAsync(CancellationToken)`;
   the private `GetIntervalSafely` becomes `private async Task<TimeSpan> GetIntervalSafelyAsync(string
   backgroundServiceName, CancellationToken stoppingToken)`, which awaits `GetIntervalAsync(stoppingToken)` with
   `.ConfigureAwait(false)` inside the existing `try`; both `catch` clauses, the fallback and the log stay as they
   are. `ExecuteAsync` awaits it into a local before the start log (inside the `IsEnabled` branch) and before every
   wait.
2. `OwnImageBaseRefreshBackgroundService.GetIntervalAsync`: `async`, `CreateAsyncScope()` with
   `await using (scope.ConfigureAwait(false))` as `ExecuteCoreAsync` does, and
   `CountAsync(entity => entity.IsEnabled, stoppingToken).ConfigureAwait(false)`; the calculation is unchanged.
3. The six other jobs (`DockerInstanceDiscovery`, `DockerHubAccountImageDiscovery`, `RuntimeContainerRefresh`,
   `ResourceStatisticsRefresh`, `VulnerabilityRefresh`, `ScanCleanup`): non-`async` override returning
   `Task.FromResult(GetConfiguredInterval(_optionsMonitor.CurrentValue))`; an options validation exception still
   throws synchronously and is caught by the `await` in step 1.

No new user-visible output; log messages and event ids are unchanged.

## Options considered

1. **Replace the synchronous hook by `GetIntervalAsync(CancellationToken)` in the base class (chosen)** — one hook,
   the token reaches the query, and the six configuration jobs cost one `Task.FromResult` each.
2. **Keep `GetInterval()` and add a virtual `GetIntervalAsync` that defaults to it, overridden only by the
   observed-image job** — two hooks for one value; the observed-image job would still have to implement the
   abstract synchronous one with code nobody calls.
3. **Keep the synchronous hook and block on `CountAsync(...).GetAwaiter().GetResult()` inside it** — the token
   would cancel the retries, but the job still blocks a thread-pool thread on asynchronous I/O (sync over async),
   which the issue asks to remove.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Images/ScheduledBackgroundService.cs` | abstract hook async with token, `GetIntervalSafelyAsync`, awaited calls |
| DockerUpdateGuard | `Images/OwnImageBaseRefreshBackgroundService.cs` | asynchronous, cancellable count |
| DockerUpdateGuard | `Images/{DockerInstanceDiscovery,DockerHubAccountImageDiscovery,RuntimeContainerRefresh,ResourceStatisticsRefresh,VulnerabilityRefresh,ScanCleanup}BackgroundService.cs` | override renamed, returns `Task.FromResult` |
| DockerUpdateGuard.Tests | `Helper/TestScheduledBackgroundService.cs` | override adapted (Dev, step 4) |

## Signatures (for the Dev's skeleton)

- `protected abstract Task<TimeSpan> GetIntervalAsync(CancellationToken stoppingToken);` in `ScheduledBackgroundService`
- Deleted: the abstract `GetInterval()` of `ScheduledBackgroundService` and its seven overrides.
- `protected override async Task<TimeSpan> GetIntervalAsync(CancellationToken stoppingToken)` in `OwnImageBaseRefreshBackgroundService`
- `protected override Task<TimeSpan> GetIntervalAsync(CancellationToken stoppingToken)` in each of the six other jobs

The `Skeleton` throw body would break every passing test of the scheduled loop, so (*Skeleton* in `.squad/stack.md`,
a deviation the Dev reports) the skeleton writes `ScheduledBackgroundService` and the six configuration jobs in full,
and the observed-image override as today's synchronous count wrapped in `Task.FromResult`, on which AC1 and AC2 fail. Names follow the `Async` suffix and parameter naming of the existing `ExecuteCoreAsync(CancellationToken stoppingToken)`.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/OwnImageBaseRefreshBackgroundServiceTests.cs` (AC1, AC2); the test doubles
  (command interceptor, execution strategy) go one per file under `src/Tests/DockerUpdateGuard.Tests/Helper/`.

Existing test code that calls a changed signature: `src/Tests/DockerUpdateGuard.Tests/Helper/TestScheduledBackgroundService.cs`
(`GetInterval()` override → `GetIntervalAsync(CancellationToken)` returning `Task.FromResult`, the `_intervalSource`
call unchanged) — the Dev adapts it in step 4, mechanically. The tests of `ScheduledBackgroundServiceTests.cs` and
the two existing tests of `OwnImageBaseRefreshBackgroundServiceTests.cs` keep their inputs and expectations: a
synchronous throw from the hook and a faulted task are caught alike.

Files no unit test can cover: none.

## Areas

- `docs/areas/scan-engine.md`, *Background jobs*: the interval read is cancelled by a shutdown, also a database count
  the retry policy is repeating; the job then ends without a warning.

## Documentation updates

- `docs/areas/scan-engine.md` (Dev): the bullet "The interval is read again before every wait …" gains one sentence:
  a shutdown cancels a read in progress, including the observed-image count while the database retry policy repeats
  it, and the job ends without the warning.
- `docs/ARCHITECTURE.md` §8 (Dev): `GetInterval()` → `GetIntervalAsync(CancellationToken)` in its three mentions,
  with "receives the stopping token".
- `docs/decisions/0025-a-failing-interval-lookup-keeps-the-jobs-last-known-interval.md` (Lead, done in this step): the
  hook's name updated in place (unreleased: no `v*` tag contains its commit 2befa82).
- README, configuration: none (no setting changes). No changelog file exists.

## Architecture check

The scan engine's guarantee that a failing job never stops the host (decision 0025) is kept: the existing `catch`
clauses wrap the awaited lookup. Shutdown gets faster, never slower; no other guarantee of `.squad/project.md` is touched.

## Security considerations

- [x] No limits.
- [x] None: no exception reaches a user-visible message; the lookup failure is logged as before.

## Decision records

- none: no decision beyond the obvious fix; decision 0025 is updated in place for the hook's new name.

## Out of scope / follow-ups

- none
