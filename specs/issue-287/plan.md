# Plan: Wait for the migration advisory lock against its own deadline

Source: Issue #287
Tier: standard — the lock wait and the migrator's failure logging change behavior; the advisory lock is a concurrency control, not a security control, so nothing is created, changed or removed that decides what an attacker may reach.
Security areas touched: Database and migrations (the lock around migrations, `DatabaseMigrator.cs`); Logging and telemetry of external data (new events in `HostLoggingExtensions.cs`, carrying no connection details)

## Problem / root cause

`src/DockerUpdateGuard/DatabaseMigrator.cs:177-181` takes the lock with the blocking `SELECT pg_advisory_lock(...)`,
an ordinary command under Npgsql's default command timeout of 30 s (none is configured). Its timeout is transient to
the retrying execution strategy that wraps lock and migration (`DatabaseMigrator.cs:69`,
`ServiceCollectionExtensions.cs:66-68`), so the wait ends after the retry policy runs out with
`RetryLimitExceededException`, which `MigrateAsync` (`DatabaseMigrator.cs:72-83`) does not catch, so the migrator's
failure event (1005) is never logged.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | The lock is taken with blocking `pg_advisory_lock` inside the strategy delegate | `DatabaseMigrator.cs:69`, `:177-181` | confirmed |
| 2 | No command timeout is configured | `ServiceCollectionExtensions.cs:58-70`; `grep -rn "CommandTimeout\|Command Timeout"` hits only `DatabaseMigratorTests.cs`; compose files and `appsettings.json` have none | confirmed |
| 3 | Npgsql's default command timeout is 30 s | `NpgsqlConnectionStringBuilder.CommandTimeout`, `[DefaultValue(NpgsqlCommand.DefaultTimeout)]`, doc "Defaults to 30 seconds" (npgsql v10.0.0) | confirmed |
| 4 | The strategy retries a timeout: `ShouldRetryOn` is true for `TimeoutException` and for `NpgsqlException` whose inner exception is a `TimeoutException`, `IOException` or `SocketException` | `NpgsqlRetryingExecutionStrategy.cs:123-125`, `NpgsqlTransientExceptionDetector.cs:17-18` (efcore.pg v10.0.3), `NpgsqlException.IsTransient` (npgsql v10.0.0:45-46) | confirmed |
| 5 | The strategy does not retry `InvalidOperationException` or `OperationCanceledException`, rethrowing them unchanged; it throws `RetryLimitExceededException` when retries run out | same `ShouldRetryOn`; `ExecutionStrategy.cs:352-362` (efcore v10.0.0) | confirmed |
| 6 | `RetryLimitExceededException` derives from `System.Exception` | `RetryLimitExceededException.cs:16` (efcore v10.0.0) | confirmed |
| 7 | The total wait is about 6–7 minutes with defaults | follows from 3–5 and the back-off; not load-bearing, the fix removes the dependency | confirmed |
| 8 | The database wait loop covers only the first connection | `DatabaseMigrator.cs:117-154` | confirmed |
| 9 | EF Core's own migration lock is `LOCK TABLE "__EFMigrationsHistory" IN ACCESS EXCLUSIVE MODE`, uncontended once the application lock is held | `NpgsqlHistoryRepository.cs:41-61` (efcore.pg v10.0.3) | confirmed |
| 10 | `Task.Delay(TimeSpan, TimeProvider, CancellationToken)` creates its timer through `TimeProvider.CreateTimer`, and `WaitRecordingTimeProvider` (`Helper/`) can drive it; a zero delay creates no timer | existing use in `ScheduledBackgroundServiceTests`; `WaitRecordingTimeProvider.cs` | confirmed |
| 11 | `SqlQuery<bool>` of `SELECT pg_try_advisory_lock({key}) AS "Value"` materialized with `ToListAsync` runs on the explicitly opened connection and returns the lock result | to verify: Reviewer, step 8, manual check (AC7) against a scratch PostgreSQL 16 | unverified |
| 12 | Hosting doc, issue: "Open question 4" of `hosting.md` | `hosting.md` has no open questions; the topic is the *Known gaps* entry for #287 | refuted |
| 13 | `docs/ARCHITECTURE.md` *Configuration model* must change | §4 names no database key; only the startup list in §3 describes the lock | refuted |
| 14 | `IHostApplicationLifetime.ApplicationStopping`, passed to `MigrateAsync`, fires before `app.RunAsync()` | to verify: Dev, step 6, reads the `ConsoleLifetime` source (signal handlers registered in `WaitForStartAsync`?) before writing the area sentence on shutdown; `Program.cs` calls `InitializeDockerUpdateGuardAsync` before `RunAsync` | unverified |

Related observation (not in scope, not load-bearing): a failed `pg_advisory_unlock` on a connection that then returns
to Npgsql's pool may keep the lock until the pooled connection's reset runs; the release handling stays as the issue
demands.

## Acceptance criteria

- AC1: While another session holds the lock, `WaitForMigrationLockAsync` keeps polling past the 30 s command
  timeout: with `MigrationRetryDelaySeconds` `5`, a try-lock returning `false` 10 times and then `true` completes
  without exception after 11 attempts, every delay is 5 s, event 1007 is logged 10 times with attempts 1–10 and
  elapsed seconds 0, 5, …, 45, and neither 1008 nor 1005 is logged.
- AC2: With `MigrationLockTimeoutSeconds` `10` and `MigrationRetryDelaySeconds` `5`, a try-lock that never succeeds is
  called 3 times (at 0, 5 and 10 s of fake time), then event 1008 is logged once and `InvalidOperationException` is
  thrown with exactly `The migration advisory lock was not acquired within 'DockerUpdateGuard:Database:MigrationLockTimeoutSeconds' (10 seconds); another instance may still be applying migrations`.
- AC3: Cancelling the token during a wait ends `WaitForMigrationLockAsync` with `OperationCanceledException` and no
  further attempt or event 1008; `MigrateAsync` rethrows an `OperationCanceledException` from the execution strategy
  without logging event 1005.
- AC4: `MigrateAsync` logs event 1005 with the exception and rethrows the same instance when the execution strategy
  throws `RetryLimitExceededException`.
- AC5: `MigrationLockTimeoutSeconds` defaults to `900`, which validates; `0` and `-1` fail with
  `'DockerUpdateGuard:Database:MigrationLockTimeoutSeconds' must be greater than zero`.
- AC6: Every wait goes through the injected `TimeProvider`; no test of this change reads the real clock.
- AC7: Against a real PostgreSQL the production path waits past the command timeout and then migrates.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `DatabaseMigratorLockHeldLongerThanCommandTimeoutWaitsAndAcquiresAsync` | `src/Tests/DockerUpdateGuard.Tests/DatabaseMigratorTests.cs` |
| AC2 | `DatabaseMigratorLockNotAcquiredBeforeDeadlineFailsWithLoggedErrorAsync` | `src/Tests/DockerUpdateGuard.Tests/DatabaseMigratorTests.cs` |
| AC3 | `DatabaseMigratorLockWaitCancelledStopsWaitingAsync`, `DatabaseMigratorMigrateCancelledDoesNotLogFailureAsync` | `src/Tests/DockerUpdateGuard.Tests/DatabaseMigratorTests.cs` |
| AC4 | `DatabaseMigratorRetryLimitExceededIsLoggedAsync` | `src/Tests/DockerUpdateGuard.Tests/DatabaseMigratorTests.cs` |
| AC5 | `DockerUpdateGuardOptionsValidatorMigrationLockTimeoutSecondsNotPositiveFails` (`DataRow` 0, -1), `DockerUpdateGuardOptionsValidatorMigrationLockTimeoutSecondsDefaultIsValid` | `src/Tests/DockerUpdateGuard.Tests/DockerUpdateGuardOptionsValidatorTests.cs` |
| AC6 | the AC1–AC3 tests (`WaitRecordingTimeProvider` with `GetUtcNow` configured by the test, advanced by each elapsed wait) | `src/Tests/DockerUpdateGuard.Tests/DatabaseMigratorTests.cs` |
| AC7 | manual: Reviewer, in its step-8 round, on the stamped head built with *Build*, against a scratch PostgreSQL 16 (`/usr/lib/postgresql/16/bin`, `initdb`/`pg_ctl` as a non-root user, a scratch data directory) and a scratch console harness outside the repository calling `DatabaseMigrator.MigrateAsync` with `Command Timeout=5`: (a) `psql` holds `pg_advisory_lock(5410320540103205)` for 20 s — the harness logs 1007 past 5 s, then migrates after release; (b) the lock is never released with `MigrationLockTimeoutSeconds` `10` — 1008 and 1005, startup fails; (c) no holder — migrates at once, no 1007; after (a) and (c) `pg_locks` shows no advisory lock of the harness. Result and output excerpt in the Reviewer's report. | — |

The AC3 and AC4 `MigrateAsync` tests use SQLite in memory with an `IExecutionStrategy` double registered through
`UseSqlite(connection, o => o.ExecutionStrategy(...))` that throws a given exception; `TestLogger<T>` records the
events. A new hand-written double goes into `Helper/`, and the Tester reports its *Test doubles* line for
`.squad/project.md` (the orchestrator writes it).

## Approach

1. `DatabaseOptions`: new `MigrationLockTimeoutSeconds`, default `900`. Validator: same rule and wording as the other
   four keys, after `MigrationRetryDelaySeconds`.
2. `MigrateAsync` takes a `TimeProvider`. Before `executionStrategy.ExecuteAsync` it reads
   `waitStartedAt = timeProvider.GetUtcNow()` once and passes it into the delegate, so a transient retry neither
   restarts the deadline nor the elapsed time. The catch blocks become one
   `catch (Exception exception) when (exception is not OperationCanceledException)` that logs 1005 and rethrows;
   `WaitForDatabaseAsync` stays outside it, so its failures are not logged twice.
3. `WaitForDatabaseAsync` reads its clock and delays through the same `TimeProvider` (`docs/UNIT_TESTS.md`, *Time in
   tests*: a changed service moves to it); its behavior is unchanged.
4. `RunMigrationAsync` (gets `options`, `waitStartedAt`, `timeProvider`): when `useAdvisoryLock`, it calls
   `WaitForMigrationLockAsync` with a delegate to a private `TryAcquireMigrationLockAsync(dbContext, token)`, which runs
   `SqlQuery<bool>($"SELECT pg_try_advisory_lock({MigrationAdvisoryLockKey}) AS \"Value\"")`, materialized with
   `ToListAsync` (no composition) and returns its single value. Connection opening, `MigrateAsync`, release and close
   stay as they are; `pg_advisory_lock` disappears. The lock key, the release handling and the non-PostgreSQL path are
   unchanged.
5. `WaitForMigrationLockAsync`: `deadline = waitStartedAt + MigrationLockTimeoutSeconds`; loop: attempt++, if the
   try-lock returns `true` return; if `GetUtcNow() >= deadline` log 1008 and throw the AC2 exception; else log 1007
   and `await Task.Delay(MigrationRetryDelaySeconds, timeProvider, token)`. Exceptions of the try-lock propagate
   (a transient one is retried by the strategy). No cap of the last delay: the failing attempt is the first one at or
   after the deadline.
6. Events in `HostLoggingExtensions` (1007 and 1008 are free; the startup range is 1000–1009):
   - 1007 `ApplicationDatabaseMigrationLockWaiting`, Information: `DockerUpdateGuard migration advisory lock is held by another instance; attempt {Attempt} after {ElapsedSeconds} seconds, retrying in {DelaySeconds} seconds`
   - 1008 `ApplicationDatabaseMigrationLockTimedOut`, Error: `DockerUpdateGuard did not acquire the migration advisory lock within {TimeoutSeconds} seconds after {Attempts} attempts; raise 'DockerUpdateGuard:Database:MigrationLockTimeoutSeconds' if another instance needs longer to migrate`
   Neither carries a connection string, host or exception text.
7. `ApplicationInitializationExtensions` resolves `TimeProvider` (registered as `TimeProvider.System`,
   `ServiceCollectionExtensions.cs:104`) and passes it.

## Options considered

Weighed in [decision 0032](../../docs/decisions/0032-the-migration-lock-is-awaited-by-polling-against-its-own-deadline.md):
a per-statement command timeout or `lock_timeout` on the blocking call (lost: no progress log, server-side
cancellation, still transient to the strategy, untestable without PostgreSQL), a context-wide command timeout (lost:
lifts the bound for every runtime command) and dropping the application lock for EF Core's table lock (lost: same
command-timeout bound, the guarantee would rest on a provider internal). For the dedicated failure: a new exception
type logged only through 1008 (lost: a type for one throw site; `InvalidOperationException` is not retried and goes
through the existing 1005 path, so 1008 adds the key-naming cause and 1005 the failure, as for every migration
failure).

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Configuration/DatabaseOptions.cs` | new property |
| DockerUpdateGuard | `Configuration/DockerUpdateGuardOptionsValidator.cs` | new rule |
| DockerUpdateGuard | `DatabaseMigrator.cs` | Approach 2–5 |
| DockerUpdateGuard | `HostLoggingExtensions.cs` | events 1007, 1008 |
| DockerUpdateGuard | `ApplicationInitializationExtensions.cs` | passes `TimeProvider` |
| DockerUpdateGuard | `appsettings.json` | `"MigrationLockTimeoutSeconds": 900` |

## Signatures (for the Dev's skeleton)

- `public int MigrationLockTimeoutSeconds { get; set; } = 900;` (`DatabaseOptions`)
- `public static async Task MigrateAsync(DockerUpdateGuardDbContext dbContext, DatabaseOptions options, ILogger logger, TimeProvider timeProvider, CancellationToken cancellationToken)` replaces the four-parameter overload
- `internal static async Task WaitForMigrationLockAsync(Func<CancellationToken, Task<bool>> tryAcquireLock, DatabaseOptions options, DateTimeOffset waitStartedAt, ILogger logger, TimeProvider timeProvider, CancellationToken cancellationToken)`
- `public static partial void ApplicationDatabaseMigrationLockWaiting(this ILogger logger, int attempt, double elapsedSeconds, double delaySeconds);`
- `public static partial void ApplicationDatabaseMigrationLockTimedOut(this ILogger logger, int timeoutSeconds, int attempts);`

## Test files

- `src/Tests/DockerUpdateGuard.Tests/DatabaseMigratorTests.cs`
- `src/Tests/DockerUpdateGuard.Tests/DockerUpdateGuardOptionsValidatorTests.cs`

Existing call sites of the changed `MigrateAsync`: `DatabaseMigratorTests.cs` (three calls, lines 47, 87, 120) — the
Dev adds `TimeProvider.System` in step 4 (the old overload goes away), mechanically; their inputs reach no new path
(non-relational, SQLite without the lock, unreachable host before the lock), so they keep their assertions.
`DockerUpdateGuardOptionsValidatorInvalidDatabaseOptionsReturnsFailures` stays unchanged (the new key keeps its valid
default there).

Files no unit test can cover: none. Lines: `TryAcquireMigrationLockAsync` and the lock branch of `RunMigrationAsync`
run only on PostgreSQL (#28), covered by AC7.

## Areas

`docs/areas/hosting.md` (Dev, after the code): *Validation* rules list gains the key; *Startup sequence* step 3
describes polling, the delay, the deadline and its key, the log per wait, the deadline not restarting on a retry,
and shutdown ending the wait (wording per fact 14); *Limits and errors* gains "Migration lock not acquired within
`MigrationLockTimeoutSeconds`: logged with the key; startup fails"; *Known gaps* loses the #287 entry and gains one
that migration statements on the migrating instance are still bounded by the command timeout, linking the follow-up
issue below (number from the orchestrator); *Related decisions* links 0032.

## Documentation updates

- `README.md` configuration reference: row `DockerUpdateGuard:Database:MigrationLockTimeoutSeconds` | `900` | No |
  how long startup waits for another instance's migration lock (the table's format; the environment-variable form is
  stated once at line 83) — Dev.
- `src/DockerUpdateGuard/appsettings.json` — Dev.
- `docs/ARCHITECTURE.md` §3, startup item 1: the lock is polled until `MigrationLockTimeoutSeconds`, linking decision
  0032 — Dev. §4 needs no change (fact 13).
- `docs/areas/hosting.md` as *Areas* — Dev.
- No changelog exists; `INSTALL.md` and compose files unchanged (operators need not set the key).

## Architecture check

Migrations stay serialized by the same advisory lock key (guarantee in `.squad/project.md`); lock and migration stay on
one explicitly opened connection and one retriable unit. Layering unchanged.

## Security considerations

- [x] No limits on parsed input.
- [x] Exceptions reaching a message: none user-visible (startup failure, before requests are served); log texts are
  the fixed templates above and the AC2 exception text, which name a key and a configured number only.

## Decision records

- `docs/decisions/0032-the-migration-lock-is-awaited-by-polling-against-its-own-deadline.md` (Proposed)

## Out of scope / follow-ups

- Follow-up issue (orchestrator, before step 6): title "[BUG] Migration statements are bounded by the database
  command timeout"; body: on the migrating instance each migration statement runs under Npgsql's 30 s default command
  timeout, so a long index build fails and is retried by the execution strategy; a migration-specific command timeout
  (`Database.SetCommandTimeout` around `MigrateAsync`) would lift it. Found in #287.