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