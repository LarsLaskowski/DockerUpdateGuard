# Plan: Wait for the migration advisory lock against its own deadline

Source: Issue #287
Tier: standard — the lock wait and the migrator's failure logging change behavior; the advisory lock is a concurrency control, not a security control, so nothing is created, changed or removed that decides what an attacker may reach.
Security areas touched: Database and migrations (the lock around migrations, `DatabaseMigrator.cs`); Logging and telemetry of external data (new events in `HostLoggingExtensions.cs`, carrying no connection details)

## Problem / root cause

`DatabaseMigrator.cs:177-181` takes the lock with blocking `pg_advisory_lock` under Npgsql's default 30 s command
timeout; the retrying strategy (`:69`, `ServiceCollectionExtensions.cs:66-68`) retries the timeout until it throws
`RetryLimitExceededException`, which `MigrateAsync` (`:72-83`) does not catch, so event 1005 is never logged.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | Blocking `pg_advisory_lock` inside the strategy delegate; no command timeout configured anywhere | `DatabaseMigrator.cs:69`, `:177-181`; `ServiceCollectionExtensions.cs:58-70`; grep of `src`, compose files, `appsettings.json` | confirmed |
| 2 | Npgsql's default command timeout is 30 s | `NpgsqlConnectionStringBuilder.CommandTimeout` (npgsql v10.0.0) | confirmed |
| 3 | The strategy retries `TimeoutException` and transient `NpgsqlException`, rethrows `InvalidOperationException` and `OperationCanceledException` unchanged, and throws `RetryLimitExceededException` (base `Exception`) when retries run out; with defaults the wait ends after about 6–7 minutes | `NpgsqlTransientExceptionDetector.cs:17-18` (efcore.pg v10.0.3); `ExecutionStrategy.cs:352-362`, `RetryLimitExceededException.cs:16` (efcore v10.0.0) | confirmed |
| 4 | The database wait loop covers only the first connection | `DatabaseMigrator.cs:117-154` | confirmed |
| 5 | EF Core's own migration lock is a `LOCK TABLE`, uncontended once the application lock is held | `NpgsqlHistoryRepository.cs:41-61` (efcore.pg v10.0.3) | confirmed |
| 6 | `WaitRecordingTimeProvider` (`Helper/`) can drive `Task.Delay(TimeSpan, TimeProvider, CancellationToken)`; a zero delay creates no timer | `WaitRecordingTimeProvider.cs`, `ScheduledBackgroundServiceTests` | confirmed |
| 7 | (formerly fact 11) `SqlQuery<bool>` of `pg_try_advisory_lock` with `ToListAsync` runs on the opened connection, sends the key as an `Int64` parameter and competes correctly with a blocking holder | Security's measurement on PostgreSQL 16.15 (*Security notes*) | confirmed |
| 8 | "Open question 4" of `hosting.md`; `ARCHITECTURE.md` §4 must change | `hosting.md` has only a *Known gaps* entry; §4 names no database key | refuted |
| 9 | (formerly fact 14) `ApplicationStopping`, passed to `MigrateAsync`, fires before `app.RunAsync()` | `Program.cs` awaits initialization before `RunAsync`; Lead's scratch Web app on ASP.NET Core 10.0.15, waiting on the token before `RunAsync`: SIGTERM and SIGINT (also as PID 1) end the process at once (143, 130), the token never fires. The issue's "shutdown ends the wait promptly" holds by process exit (`hosting.md` step 3, decision 0032) | refuted |

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
| AC7 | manual: Reviewer, in its step-8 round, on the stamped head: scratch PostgreSQL 16 and a scratch harness calling `MigrateAsync` with `Command Timeout=5`, as Security measured: (a) lock held 20 s by `psql` — 1007 past 5 s, then migrates; (b) never released, `MigrationLockTimeoutSeconds` `10` — 1008 and 1005; (c) no holder — no 1007; no advisory lock left in `pg_locks`. | — |

The AC3 and AC4 `MigrateAsync` tests use SQLite in memory with an `IExecutionStrategy` double (via
`o.ExecutionStrategy(...)`) that throws a given exception; a new double goes into `Helper/` and the Tester reports its
*Test doubles* line for `.squad/project.md`.

## Approach

1. `DatabaseOptions`: new `MigrationLockTimeoutSeconds`, default `900`. Validator: same rule and wording as the other
   four keys, after `MigrationRetryDelaySeconds`.
2. `MigrateAsync` takes a `TimeProvider` and reads `waitStartedAt` once before `executionStrategy.ExecuteAsync`, so a
   retry restarts neither deadline nor elapsed time. The catch blocks become one
   `catch (Exception exception) when (exception is not OperationCanceledException)` logging 1005 and rethrowing;
   `WaitForDatabaseAsync` stays outside it.
3. `WaitForDatabaseAsync` moves to the same `TimeProvider` (`docs/UNIT_TESTS.md`, *Time in tests*), unchanged in
   behavior.
4. `RunMigrationAsync`: when `useAdvisoryLock`, it calls `WaitForMigrationLockAsync` with a delegate to a private
   `TryAcquireMigrationLockAsync(dbContext, token)` running
   `SqlQuery<bool>($"SELECT pg_try_advisory_lock({MigrationAdvisoryLockKey}) AS \"Value\"")` with `ToListAsync` (fact 7).
   Key, connection handling, release and the non-PostgreSQL path are unchanged.
5. `WaitForMigrationLockAsync`: `deadline = waitStartedAt + MigrationLockTimeoutSeconds`; loop: attempt++, if the
   try-lock returns `true` return; if `GetUtcNow() >= deadline` log 1008 and throw the AC2 exception; else log 1007
   and `await Task.Delay(MigrationRetryDelaySeconds, timeProvider, token)`. Try-lock exceptions propagate to the
   strategy. The last delay is not capped.
6. Events in `HostLoggingExtensions`:
   - 1007 `ApplicationDatabaseMigrationLockWaiting`, Information: `DockerUpdateGuard migration advisory lock is held by another instance; attempt {Attempt} after {ElapsedSeconds} seconds, retrying in {DelaySeconds} seconds`
   - 1008 `ApplicationDatabaseMigrationLockTimedOut`, Error: `DockerUpdateGuard did not acquire the migration advisory lock within {TimeoutSeconds} seconds after {Attempts} attempts; raise 'DockerUpdateGuard:Database:MigrationLockTimeoutSeconds' if another instance needs longer to migrate`
7. `ApplicationInitializationExtensions` resolves and passes `TimeProvider` (`ServiceCollectionExtensions.cs:104`).

## Options considered

In [decision 0032](../../docs/decisions/0032-the-migration-lock-is-awaited-by-polling-against-its-own-deadline.md):
a per-statement command timeout or `lock_timeout`, a context-wide command timeout, and EF Core's table lock alone.
A dedicated exception type logged only through 1008 lost to `InvalidOperationException`, which the strategy does not
retry and which reaches 1005 like every migration failure.

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

Existing call sites of `MigrateAsync`: `DatabaseMigratorTests.cs:47, 87, 120` — the Dev adds `TimeProvider.System`
in step 4, mechanically; the Tester then gives the unreachable-database test a substituted clock (*Challenge notes*,
objection 1). `DockerUpdateGuardOptionsValidatorInvalidDatabaseOptionsReturnsFailures` stays unchanged.

Files no unit test can cover: none. Lines: `TryAcquireMigrationLockAsync` and the lock branch of `RunMigrationAsync`
run only on PostgreSQL (#28), covered by AC7.

## Areas

`docs/areas/hosting.md` (Dev, after the code): *Validation* rules list gains the key; *Startup sequence* step 3
describes polling, delay, deadline and key, the log per wait, the deadline surviving a retry, and shutdown
(wording per fact 9); *Limits and errors* gains "Migration lock not acquired within
`MigrationLockTimeoutSeconds`: logged with the key; startup fails"; *Known gaps* loses the #287 entry and gains one
that migration statements on the migrating instance are still bounded by the command timeout, linking the follow-up
issue below (number from the orchestrator); *Related decisions* links 0032.

## Documentation updates

- `README.md` configuration reference: row for the key, default `900`, in the table's format — Dev.
- `src/DockerUpdateGuard/appsettings.json` — Dev.
- `docs/ARCHITECTURE.md` §3, startup item 1: the lock is polled until `MigrationLockTimeoutSeconds`, linking decision
  0032 — Dev.
- `docs/areas/hosting.md` as *Areas* — Dev.
- No changelog exists; `INSTALL.md` and compose files unchanged (operators need not set the key).

## Architecture check

Migrations stay serialized by the same advisory lock key (guarantee in `.squad/project.md`); lock and migration stay on
one explicitly opened connection and one retriable unit. Layering unchanged.

## Security considerations

- [x] No limits on parsed input.
- [x] Exceptions reaching a message: none user-visible (startup failure, before requests are served); 1007,
  1008 and the AC2 exception text carry only a key name and numbers; 1005 now logs every non-cancellation
  exception with its message (a RetryLimitExceededException's inner Npgsql error can name the database host,
  as the telemetry area allows for failed database calls), never the connection string.

## Decision records

- `docs/decisions/0032-the-migration-lock-is-awaited-by-polling-against-its-own-deadline.md` (Proposed)

## Out of scope / follow-ups

- Follow-up issue (orchestrator, before step 6): "[BUG] Migration statements are bounded by the database command
  timeout" — each statement of a migration runs under the 30 s default, so a long index build fails; a
  migration-specific `Database.SetCommandTimeout` would lift it. Found in #287.

## Challenge notes

Devil's Advocate, mode plan, issue #287 (`/home/user/DockerUpdateGuard/specs/issue-287/plan.md`)

I checked the plan's load-bearing claims against the code and found no major problem.

- **Root cause confirmed.** The lock is taken with a blocking call at `src/DockerUpdateGuard/DatabaseMigrator.cs:177-181`, inside the strategy delegate at `:69`. The method only catches `DbException` and `InvalidOperationException` (`:72-83`), so a `RetryLimitExceededException` skips event 1005. `pg_advisory_lock` is used nowhere else; the only other advisory-lock SQL is the unlock at `:211`. There is no sibling code path to fix.
- **Event ids are free.** 1007 and 1008 are unused; the ids in use are 1000–1006 and 1010.
- **The time fake can drive the waits.** `WaitRecordingTimeProvider` (`src/Tests/DockerUpdateGuard.Tests/Helper/WaitRecordingTimeProvider.cs:60-67`) records each delay's due time and lets the test end the wait. It does not move `GetUtcNow` forward, but the plan already says the test configures that.
- **The AC1 and AC2 counts hold.** I traced Approach step 5:
  - AC2 (10 s deadline, 5 s delay): the lock is tried at 0, 5 and 10 s. At 10 s the check `now >= deadline` is true, so event 1008 is logged once and the exception is thrown.
  - AC1: event 1007 is logged with elapsed seconds 0 to 45, and the 11th attempt (at 50 s) gets the lock.
- **The two refuted claims are refuted correctly.** `docs/areas/hosting.md` has no open questions; the topic is the *Known gaps* entry at lines 210-213. `docs/ARCHITECTURE.md` §4 (lines 126-156) names no database keys, so it needs no change. The README states the environment-variable form once, at line 83, which supports adding a plain table row.
- **The options validator test is unaffected.** It keeps passing because the new key's default is valid, as the plan says.

Objections:

1. **minor** — The plan moves `WaitForDatabaseAsync` to the injected `TimeProvider` (Approach 3) but keeps the existing test `DatabaseMigratorUnreachableDatabaseThrowsAfterTimeoutAsync` on `TimeProvider.System` (`src/Tests/DockerUpdateGuard.Tests/DatabaseMigratorTests.cs:105-132`, call at :121). That test sets `MigrationStartupTimeoutSeconds = 1` and `MigrationRetryDelaySeconds = 0` (:111-112) and passes only because a real second goes by. That breaks `docs/UNIT_TESTS.md:283-288` ("Tests never depend on the real clock") for a test this change edits anyway. Why minor: the Tester can fix it without changing the plan. **Instruction for the Tester:** pass that test a substituted `TimeProvider` whose `GetUtcNow` returns a time past the deadline from the second read on. Do not pass a bare substitute: its `GetUtcNow` returns a fixed value, and a zero delay creates no timer, so the loop never reaches the deadline and the test hangs.

The plan otherwise holds up. The two claims it leaves unverified are already assigned:
- Fact 11 (`SqlQuery<bool>` runs on the opened connection) is checked by the Reviewer in the manual AC7 run.
- Fact 14 (whether `ApplicationStopping` fires before `RunAsync()`) is checked by the Dev before writing the shutdown sentence in `docs/areas/hosting.md`.

## Security notes

## Security plan review: issue #287 (mode `plan`)

I reviewed `/home/user/DockerUpdateGuard/specs/issue-287/plan.md` as of commit `aa0e35f`. The later commit `b12f13b` only adds the Devil's Advocate challenge notes, and I did not review those. I checked the plan against `src/DockerUpdateGuard/DatabaseMigrator.cs`, `HostLoggingExtensions.cs`, `.squad/project.md` (*Security areas*, *Guarantees*), `docs/areas/hosting.md` (*Startup sequence*) and `docs/areas/telemetry.md` (*Credentials and external data*).

### Tier and areas
- **Tier `standard` is correct.** The migration advisory lock only coordinates instances. It does not decide what an attacker may reach, so it is not a security control under the `security` row of *Tiers*. No control is created, changed or removed.
- **The listed areas are complete.** They are *Database and migrations* and *Logging and telemetry of external data*. The plan adds no dependency and changes no CI, Docker or build configuration. The `appsettings.json` key is plain configuration.

### Measurement (fact 11, which the migration-serialization guarantee rests on)
I ran this once on a scratch PostgreSQL 16.15 (`initdb` and `pg_ctl` as user `postgres`, TCP port 55432). The harness was a scratch console app outside the repository: `Microsoft.EntityFrameworkCore.Relational` 10.0.15 and `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3, `UseNpgsql(...; Command Timeout=5, o => o.EnableRetryOnFailure())`. Inside `strategy.ExecuteAsync` it does `OpenConnectionAsync`, then polls `SqlQuery<bool>($"SELECT pg_try_advisory_lock({Key}) AS \"Value\"").ToListAsync()` every 2 s, then reads `pg_locks` and unlocks in `finally`.

The holder ran `psql -h 127.0.0.1 -p 55432 -U postgres -c "SELECT pg_advisory_lock(5410320540103205), pg_backend_pid(); SELECT pg_sleep(9);"` in the background, so it used today's blocking call. Then `dotnet bin/Release/net10.0/harness.dll`. Output excerpt:

```
Executed DbCommand (11ms) [Parameters=[p0='?' (DbType = Int64)], CommandType='Text', CommandTimeout='5']
SELECT pg_try_advisory_lock(@p0) AS "Value"
MEASURE backend pid=1711
MEASURE attempt=1 t=0.0s got=False pid=1711
...
MEASURE attempt=4 t=6.0s got=False pid=1711
MEASURE attempt=5 t=8.1s got=True pid=1711
MEASURE lock holders: 1711
SELECT pg_advisory_unlock(@p0)
MEASURE done
holder: pg_backend_pid 1696
afterwards: SELECT count(*) FROM pg_locks WHERE locktype='advisory'  ->  0
```

What this shows:
- The try-lock runs on the explicitly opened connection: the backend pid is the same for every attempt and is the lock holder in `pg_locks`.
- The key is sent as an `Int64` parameter, so no SQL is built from text.
- Polling continues past the 5 s command timeout.
- `pg_try_advisory_lock` competes correctly with an instance still on the blocking `pg_advisory_lock`, so a mixed-version rollout stays serialized.
- After release and close, no advisory lock is left.

I also checked the deadline and cancellation path, where `finally` unlocks a lock this session never got. `psql ... -c "SELECT pg_advisory_unlock(5410320540103205)"` returns `f` with `WARNING: you don't own a lock of type ExclusiveLock` and raises no exception. So event 1006 is not logged and the AC2 exception is not hidden.

The Reviewer's AC7 manual check is still worth running on the built head. With the measurement above, fact 11 no longer blocks this plan.

### Other checks
- **Logging.** Events 1007 and 1008 carry only `attempt`, `elapsedSeconds`, `delaySeconds`, `timeoutSeconds` and `attempts`. None of these is on the sensitive-name list (`.squad/stack.md`, *Logger arguments*). The AC2 exception text names only a key and a configured number. No connection string, host or credential appears.
- **SQL.** The only new statement takes a compile-time constant through a `FormattableString` and is parameterized, as measured above. No user input reaches a query.
- **Bounds.** `MigrationLockTimeoutSeconds` must be greater than zero. Even `int.MaxValue` seconds added to the start time stays within the `DateTimeOffset` range. A database user holding the lock now fails startup after the configured deadline instead of after about 6–7 minutes, which is the intended, logged failure.

### Findings
1. **[NON-BLOCKING]** plan.md, *Security considerations*, second bullet. It says the log texts are "the fixed templates above and the AC2 exception text". Approach 2, however, widens 1005 from `DbException`/`InvalidOperationException` to every exception except cancellation, and logs it with its message. That includes `RetryLimitExceededException`, whose inner `NpgsqlException` can name the database host. `docs/areas/telemetry.md` allows exception messages naming a database host on the error entries of failed integration calls, so this is allowed. The bullet just does not say so.
   - Smallest fix, which I recommend making now, verbatim. Replace the bullet with:
     `- [x] Exceptions reaching a message: none user-visible (startup failure, before requests are served); 1007,
       1008 and the AC2 exception text carry only a key name and numbers; 1005 now logs every non-cancellation
       exception with its message (a RetryLimitExceededException's inner Npgsql error can name the database host,
       as the telemetry area allows for failed database calls), never the connection string.`
