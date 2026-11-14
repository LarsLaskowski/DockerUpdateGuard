# Plan: Migration statements run under a command timeout of their own

Source: Issue #470
Tier: standard — a behavior change (a configurable, finite command timeout for the migration statements, with a new configuration key) that creates, changes or removes no security control.
Security areas touched: Database and migrations (`DatabaseMigrator.cs`)

## Problem / root cause

`src/DockerUpdateGuard/ServiceCollectionExtensions.cs:62-69` configures Npgsql without a command timeout, so EF Core
leaves every command at the driver's default of 30 seconds. `DatabaseMigrator.RunMigrationAsync`
(`src/DockerUpdateGuard/DatabaseMigrator.cs:239`) calls `dbContext.Database.MigrateAsync` without changing it, so
each migration statement is cancelled after 30 seconds. Npgsql reports that as a transient error, the retrying
execution strategy around lock and migration (`DatabaseMigrator.cs:73`) re-runs them, the statement times out again,
and once the retries are spent startup fails. The gap is documented in `docs/areas/hosting.md:218-220`.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | Npgsql's default command timeout is 30 s; 0 means infinite | Npgsql v10.0.3 `NpgsqlConnectionStringBuilder.cs:950-967` (doc "Defaults to 30 seconds", description "Set to zero for infinity"); package version from `Directory.Packages.props:20` and the NuGet cache | confirmed |
| 2 | The application sets no command timeout (EF Core `RelationalOptions.CommandTimeout` stays null) | `ServiceCollectionExtensions.cs:62-69`: only `MigrationsAssembly` and `EnableRetryOnFailure` | confirmed |
| 3 | Every command EF Core builds, the migration commands included, takes the connection's `CommandTimeout` when it is not null | EF Core v10.0.15 `RelationalCommand.cs:842-845`; `Database.SetCommandTimeout` sets `RelationalConnection.CommandTimeout` (`RelationalConnection.cs:271-282`) | confirmed |
| 4 | A command timeout surfaces as `NpgsqlException` with an inner `TimeoutException`, which is transient, so the strategy retries it (issue claim "fails and is retried") | Npgsql v10.0.3 `Internal/NpgsqlConnector.cs:1790-1799`, `NpgsqlException.cs:45-46`; efcore.pg v10.0.3 `NpgsqlRetryingExecutionStrategy.cs:123-125` and `NpgsqlTransientExceptionDetector` | confirmed |
| 5 | The issue claim "each migration statement runs under the 30 s default on the migrating instance" | rows 1-3 and `DatabaseMigrator.cs:239` | confirmed |
| 6 | A timeout set on the context lasts until the context is reset or disposed | `RelationalConnection.cs:1041` (`ResetState` restores the default) | confirmed |
| 7 | The migration's context is the scoped context startup then uses for the instance synchronization and the inventory gauges | `ApplicationInitializationExtensions.cs:32-51`; `AddDbContext` (scoped, not pooled) in `src/DockerUpdateGuard.Data/ServiceCollectionExtensions.cs:27` | confirmed |
| 8 | EF Core's pending-model-changes check runs before the migrator's first command and is skipped when the warning is ignored, so a SQLite test can reach the migrator's commands | EF Core v10.0.15 `Migrator.cs:225-231` (`ValidateMigrations` before `ExistsAsync`), `Migrator.cs:378-381` (`WarningBehavior.Ignore`) | confirmed |
| 9 | No shutdown handling is active during the migration, so an unbounded statement could stall startup with no graceful end | `docs/areas/hosting.md` *Startup sequence* step 3; decision 0032, *Consequences* | confirmed |
| 10 | Decision 0032 is unreleased (edited in place) | its adding commit 99a019c is no ancestor of `v1.3.8` (`git ls-remote --tags origin`, `git merge-base --is-ancestor`) | confirmed |

## Acceptance criteria

- AC1: On a relational provider, every command EF Core executes inside `DatabaseMigrator`'s call of
  `Database.MigrateAsync` has the command timeout `DatabaseOptions.MigrationCommandTimeoutSeconds`.
- AC2: When `DatabaseMigrator.MigrateAsync` has returned or thrown, the context's command timeout
  (`Database.GetCommandTimeout()`) is the value it had before the call — null when none was set, an explicitly set
  value otherwise.
- AC3: `DockerUpdateGuard:Database:MigrationCommandTimeoutSeconds` defaults to `900`, which validates; a value of zero
  or less fails validation with exactly
  `'DockerUpdateGuard:Database:MigrationCommandTimeoutSeconds' must be greater than zero`.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `DatabaseMigratorMigrationCommandsUseMigrationCommandTimeoutAsync` | `src/Tests/DockerUpdateGuard.Tests/DatabaseMigratorTests.cs` |
| AC2 | `DatabaseMigratorMigrationRestoresPreviousCommandTimeoutAsync` (two cases: none set, an explicit value) | `src/Tests/DockerUpdateGuard.Tests/DatabaseMigratorTests.cs` |
| AC3 | `DockerUpdateGuardOptionsValidatorMigrationCommandTimeoutSecondsNotPositiveFails` (0, -1), `DockerUpdateGuardOptionsValidatorMigrationCommandTimeoutSecondsDefaultIsValid` | `src/Tests/DockerUpdateGuard.Tests/DockerUpdateGuardOptionsValidatorTests.cs` |

Seam for AC1 and AC2 (as in the existing SQLite tests of `DatabaseMigratorTests`): an open in-memory
`SqliteConnection`, the context options with `ConfigureWarnings` ignoring `RelationalEventId.PendingModelChangesWarning`
(fact 8) and a new test double `CommandTimeoutRecordingInterceptor` (a `DbCommandInterceptor`) that records the
`CommandTimeout` of every command it sees and fails the first one with an exception of its own, so the outcome does
not depend on the Npgsql migrations' SQL on SQLite. AC1 uses a distinctive value (not 30, SQLite's default) and asserts
at least one recorded command and every recorded timeout equal to it; the run fails with the interceptor's exception
instance. AC2 asserts the same recorded timeout first (so it fails before the fix) and then `GetCommandTimeout()`
after the throw. The advisory lock is not taken on SQLite, so every recorded command is the migrator's.

## Approach

In `DatabaseMigrator.RunMigrationAsync`, directly around the `MigrateAsync` call (after the lock wait, inside the
existing `try`): read `dbContext.Database.GetCommandTimeout()`, call
`dbContext.Database.SetCommandTimeout(options.MigrationCommandTimeoutSeconds)`, run `MigrateAsync`, and in a nested
`finally` set the read value back with `SetCommandTimeout(int?)`. The advisory lock queries and the unlock keep the
database command timeout (they never block). A retry of the execution strategy re-runs the whole unit, so it sets and
restores again. No new log message, no user-visible output.

`DatabaseOptions` gets `MigrationCommandTimeoutSeconds` (default `900`, the lock wait's default);
`DockerUpdateGuardOptionsValidator.ValidateDatabaseOptions` adds the rule of AC3 after the `MigrationLockTimeoutSeconds`
rule; `appsettings.json` gets the key with `900` after `MigrationLockTimeoutSeconds`. Zero is rejected rather than read
as "infinite" (fact 9, fail closed).

## Options considered

The four of decision 0033, *Options considered*: a key of its own set around `MigrateAsync` and restored (chosen); no
timeout while migrating (lost: an unbounded stall with no shutdown handling, fact 9); reusing
`MigrationLockTimeoutSeconds` (lost: one value for two different bounds); a timeout for the whole context through
the connection string or the provider option (lost: lifts the bound for every runtime query, as 0032 rejected).

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Configuration/DatabaseOptions.cs` | new property |
| DockerUpdateGuard | `Configuration/DockerUpdateGuardOptionsValidator.cs` | new rule |
| DockerUpdateGuard | `DatabaseMigrator.cs` | timeout set and restored around `MigrateAsync` |
| DockerUpdateGuard | `appsettings.json` | new key |
| DockerUpdateGuard.Tests | `DatabaseMigratorTests.cs`, `DockerUpdateGuardOptionsValidatorTests.cs`, `Helper/CommandTimeoutRecordingInterceptor.cs` | new tests and test double |

## Signatures (for the Dev's skeleton)

- `DatabaseOptions`: `public int MigrationCommandTimeoutSeconds { get; set; } = 900;` (XML summary: the command
  timeout in seconds of each statement the startup migration runs)

No other API changes; the `DatabaseMigrator` change is private. No existing file needs a rewrite beyond this member.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/DatabaseMigratorTests.cs` (AC1, AC2)
- `src/Tests/DockerUpdateGuard.Tests/DockerUpdateGuardOptionsValidatorTests.cs` (AC3)
- `src/Tests/DockerUpdateGuard.Tests/Helper/CommandTimeoutRecordingInterceptor.cs` (new test double, Tester; its line
  for `.squad/project.md`, *Test doubles*, is reported by the Tester and written by the orchestrator)

Existing test code that calls a changed signature: none (a new property with a default). The existing SQLite tests of
`DatabaseMigratorTests` have no input the new criteria handle differently and stay unchanged.

Files no unit test can cover: none.

## Areas

`docs/areas/hosting.md` (Hosting and configuration): the migration step names the statement timeout; the validation
rule covers the new key; the limits table gets the timeout's outcome; the known gap of #470 goes.

## Documentation updates

All by the Dev, once the code is green:

- `docs/areas/hosting.md`
  - *Validation*, "Rules owned by this area" (line 38-40): add `MigrationCommandTimeoutSeconds` to the keys that must
    be greater than zero.
  - *Startup sequence* step 3: each migration statement runs under `DockerUpdateGuard:Database:MigrationCommandTimeoutSeconds`
    seconds (default `900`) instead of the database command timeout; the commands after the migration keep the
    database command timeout; a statement that exceeds it fails like a transient fault, so lock and migration are
    retried as already described, and a migration longer than `MigrationLockTimeoutSeconds` needs that key raised for
    the waiting instances.
  - *Limits and errors*: a row for a migration statement exceeding `MigrationCommandTimeoutSeconds` (retried with lock
    and migration up to `MaxConnectionRetryCount` times, then logged; startup fails).
  - *Known gaps*: remove the bullet of #470 (lines 218-220).
  - *Related decisions*: add 0033.
- `README.md` configuration table: a row after `MigrationLockTimeoutSeconds` — key, default `900`, `No`, the
  command timeout in seconds of each statement of the startup migration; raise it for long migrations.
- `docs/ARCHITECTURE.md` startup step 1 (lines 106-111): one sentence that the migration statements run under
  `MigrationCommandTimeoutSeconds`, linking decision 0033.
- `.squad/project.md`, *Test doubles*: one line for `CommandTimeoutRecordingInterceptor` (`Helper/`) — the EF Core
  command pipeline, recording command timeouts (Tester reports the text, orchestrator writes it).

Sweep: `docs/ARCHITECTURE.md`, `docs/areas/*.md`, `README.md`, `INSTALL.md`, `DOCKER.md` and `docs/docker-compose.*.yml`
searched for the `Database` keys and "command timeout"; the hits are listed above. No changelog exists; operators
need not set the key, so `INSTALL.md` and the compose files stay unchanged.

## Architecture check

The guarantee "Migrations are serialized at startup" is untouched: the lock, its key, the polling and the retry unit
stay as they are. Layering holds: the change stays in the host's migrator and options. `docs/ARCHITECTURE.md` gets the
sentence above; no guarantee changes.

## Security considerations

- [x] No limits on parsed input; the new key is an integer validated greater than zero.
- [x] Exceptions reaching a user-visible failure: none new. A migration timeout ends startup through the existing
  migration-failure log (event 1005) and the rethrown exception, as any migration failure does today; no page shows it.

## Decision records

- `docs/decisions/0033-migration-statements-run-under-a-command-timeout-of-their-own.md` (Proposed)
- `docs/decisions/0032-the-migration-lock-is-awaited-by-polling-against-its-own-deadline.md` (unreleased, edited in
  place): its consequence about the migration statements now links 0033.

## Out of scope / follow-ups

- Mapping a migration statement timeout to a non-retried failure, so that it is not re-run `MaxConnectionRetryCount`
  times: it would change the retry unit decision 0032 describes; recorded as a consequence in 0033, no issue unless
  the Product Manager asks for one.

## Challenge notes

The plan holds up: no major objections and three minor ones. I checked its root cause myself. `ServiceCollectionExtensions.cs:62-69` sets no command timeout. `DatabaseMigrator.cs:239` calls `MigrateAsync` without one. The EF Core source applies `connection.CommandTimeout` only when it is not null (`RelationalCommand.cs:842-845`), and `ResetState` restores the default (`RelationalConnection.cs:1041`). The issue asks for exactly the plan's approach (`Database.SetCommandTimeout` around `MigrateAsync`). A `NO CHANGE` result does not fit, the scope matches the issue, and the `standard` tier fits.

Minor objections (for the plan's *Challenge notes*):

1. **minor — the test double must read the timeout after EF Core sets it.** In EF Core v10.0.15 `RelationalCommand.cs:819-845`, the `CommandCreated` hook runs before `command.CommandTimeout = connection.CommandTimeout` is assigned. An interceptor that records in `CommandCreated` would always see SQLite's default and fail even on a correct build. Instruction to the Tester: `CommandTimeoutRecordingInterceptor` reads `command.CommandTimeout` in `CommandInitialized` or in the `NonQueryExecuting`/`ScalarExecuting`/`ReaderExecuting` hooks (sync and async), never in `CommandCreating`/`CommandCreated`. It also throws its own exception from that same hook. The existing `Helper/RecordingCommandInterceptor.cs` only covers reader executions, so it is not a drop-in.

2. **minor — operators who raised the timeout in their connection string get a lower one for migrations.** The connection string is passed through as given (`docs/areas/hosting.md:62-66`), so an operator may already have worked around #470 with `Command Timeout=0` or a large value. Npgsql honours both (`NpgsqlConnectionStringBuilder.cs:950-967`: 0 means infinite). With the plan's approach, `SetCommandTimeout(900)` replaces that value for every migration statement. Instruction to the Dev: in the `docs/areas/hosting.md` step 3 text and the `README.md` row, say that `MigrationCommandTimeoutSeconds` replaces any `Command Timeout` from the connection string for the migration statements. Operators who set a longer one must set this key to match.

3. **minor — state the worst case in the area document, not only in decision 0033.** A statement that times out counts as transient and re-runs the whole unit. The defaults are `MaxConnectionRetryCount` = 8 (`DatabaseOptions.cs:13`) and 900 s for the new key, so startup can stall for about 9 × 900 s (2 h 15 min) with no shutdown handling (fact 9). Instruction to the Dev: the new *Limits and errors* row in `docs/areas/hosting.md` names this bound (timeout × (`MaxConnectionRetryCount` + 1)), as decision 0033's *Consequences* already does.

Also for the Lead, as wording: AC1 says "every command EF Core executes inside `MigrateAsync`". EF Core's `Migrator.MigrateAsync` runs `_databaseCreator.ExistsAsync`/`CreateAsync` first (EF Core v10.0.15 `Migrator.cs:228-231`). From memory, not checked against efcore.pg source: Npgsql's database creator runs `CREATE DATABASE` on a separate admin connection, which the context's timeout does not reach. A more accurate AC1 would say "every command on the context's connection"; the SQLite test is unaffected.

Files read:
- /home/user/DockerUpdateGuard/specs/issue-470/plan.md
- /home/user/DockerUpdateGuard/src/DockerUpdateGuard/DatabaseMigrator.cs
- /home/user/DockerUpdateGuard/src/DockerUpdateGuard/ServiceCollectionExtensions.cs
- /home/user/DockerUpdateGuard/src/DockerUpdateGuard/Configuration/DatabaseOptions.cs
- /home/user/DockerUpdateGuard/docs/areas/hosting.md
- /home/user/DockerUpdateGuard/docs/decisions/0033-migration-statements-run-under-a-command-timeout-of-their-own.md
- /home/user/DockerUpdateGuard/src/Tests/DockerUpdateGuard.Tests/DatabaseMigratorTests.cs
- /home/user/DockerUpdateGuard/src/Tests/DockerUpdateGuard.Tests/Helper/RecordingCommandInterceptor.cs

## Security notes

Security plan review: specs/issue-470/plan.md (mode plan, round 1)

**Verdict: approved, nothing blocking.** I ran no measurements. My findings come from reading `/home/user/DockerUpdateGuard/specs/issue-470/plan.md`, `/home/user/DockerUpdateGuard/src/DockerUpdateGuard/DatabaseMigrator.cs`, the validator and `.squad/project.md` / `.squad/routing.md`.

**What I checked**
- **Tier `standard` is right.** A per-statement command timeout bounds a resource and decides nothing about what an attacker can reach. In the *Tiers* table it counts as input handling and availability, not a security control. Nothing in the plan creates, changes or removes authentication, a secret, a guard, the network exposure or build trust.
- ***Security areas touched* is complete.** "Database and migrations" is the only area involved. The plan adds no query built from user input, no dependency, and no CI, Docker or build change. `appsettings.json` only gets a non-secret integer key.
- **The migration lock is unaffected.**
  - The plan sets the timeout directly around `MigrateAsync`, after the lock wait and inside the existing `try`.
  - It restores the previous value in a nested `finally`. That runs before the outer `finally` (`DatabaseMigrator.cs:242-248`), which releases the advisory lock and closes the connection.
  - So `pg_try_advisory_lock` and `pg_advisory_unlock` keep the default timeout. The lock is still released on every path, including a statement timeout that breaks the connector: the unlock is best-effort and the session end releases the lock anyway.
  - A strategy retry re-runs the whole unit, so the value is set and restored each time.
- **The bound fails closed.** The validator rejects zero or less, so "infinite" can't be configured by accident (fact 9). AC2 checks that the larger value doesn't stay on the scoped context that startup reuses afterwards (fact 7), so later runtime queries keep the 30 s bound.
- **No new output.** There is no new log call, so the *Logger arguments* check doesn't apply. A timeout ends startup through the existing event 1005 and the rethrown exception. The Npgsql timeout exception carries no secrets, and no page shows it.
- **Library behavior the bound relies on.** Facts 3 and 4 come from source reading and the issue itself. The plan doesn't model them with a test double: AC1 drives the real EF Core command pipeline on SQLite with an interceptor, and that pins how the timeout reaches the migration commands. Npgsql's cancel-on-timeout is existing behavior and is the issue's premise. I see no unverified security claim that needs a measurement before approval.

**Findings**

1. **[NON-BLOCKING] No upper bound on `MigrationCommandTimeoutSeconds`** (plan, *Approach* and AC3: "a value of zero or less fails validation").
   - The plan's own reason for rejecting zero is that an unbounded migration statement can stall startup with no shutdown handling (fact 9). A very large value does practically the same: `int.MaxValue` seconds is about 68 years.
   - Npgsql converts the command timeout to milliseconds internally, so values above about 2,147,483 s may overflow. I did not measure this (no PostgreSQL available); it is from reading only.
   - The key is operator-trusted configuration and the existing `Migration*Seconds` keys have no upper bound either, so this is consistency, not an exploit.
   - Smallest fix: validate `is <= 0 or > 86400` with the message "must be between 1 and 86400", as the `RequestTimeoutSeconds` rules do.
   - Recommendation: leave AC3 as planned, and open a linked issue only if the Lead wants upper bounds on all `Database:*Seconds` keys together.

2. **[NON-BLOCKING] Where the test double records the timeout** (plan, *Tests per criterion*, the seam paragraph: "records the `CommandTimeout` of every command it sees").
   - From memory of EF Core's `RelationalCommand.CreateDbCommand`, the connection's `CommandTimeout` is applied to the `DbCommand` after `CommandCreated` runs. A recording made in `CommandCreated` would see SQLite's default of 30, so AC1 would fail even with the fix in place. I did not check this against the EF Core 10.0.15 source.
   - It does not weaken the security property. But the test only pins the bound if it records in the `*Executing` hooks (`ReaderExecuting[Async]`, `NonQueryExecuting[Async]`, `ScalarExecuting[Async]`) and throws from the first of them.
   - Smallest fix: one sentence in the seam paragraph naming the `*Executing` hooks.
   - Recommendation: fix now, it is one sentence.

3. **[NON-BLOCKING] Statement-timeout retries are a documented availability limit, not a defect.** A statement that keeps hitting the timeout is re-run up to `MaxConnectionRetryCount` times while this instance holds the lock. The planned *Startup sequence* text and the *Limits and errors* row already say this, and decision 0033 records it.
   - Recommendation: no change.
