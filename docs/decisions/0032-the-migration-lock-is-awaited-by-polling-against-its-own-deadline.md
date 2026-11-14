# 0032: The migration lock is awaited by polling against its own deadline

- **Status:** Accepted
- **Date:** 2026-11-14
- **Area:** Hosting and configuration
- **Source:** Issue #287
- **Supersedes:** —

## Context

Instances starting against one database serialize their migrations with a session-scoped PostgreSQL advisory lock
([Hosting and configuration](../areas/hosting.md#startup-sequence); the guarantee in `.squad/project.md` that
migrations are serialized at startup). The lock was taken with the blocking `pg_advisory_lock`, an ordinary command
under Npgsql's default command timeout of 30 seconds. The timeout is transient to the Npgsql retrying execution
strategy, which re-runs lock and migration together, so the wait was bounded by the retry policy (about six to seven
minutes with the defaults), and a migration running longer on one instance failed the startup of the others. The
`RetryLimitExceededException` that ended the wait was not logged by the migrator, which caught only `DbException` and
`InvalidOperationException`. The unit tests cannot run PostgreSQL (#28).

## Options considered

1. **Poll `pg_try_advisory_lock` every `MigrationRetryDelaySeconds` until a deadline of its own,
   `MigrationLockTimeoutSeconds`, computed once before the execution strategy runs (chosen)** — no statement ever
   blocks on the server, so the command timeout no longer bounds the wait; each wait is logged; a cancelled token ends
   the wait during its delay; the polling and deadline logic is unit-testable through a seam that takes the try-lock
   call and a `TimeProvider`. Costs one query per delay and a new key.
2. **Keep `pg_advisory_lock` and give that one statement a longer or infinite command timeout, or a server-side
   `lock_timeout`** — one statement changed, but the wait stays a server-side block: no progress can be logged, the
   deadline and cancellation depend on the server's cancellation, the timeout exception is still transient to the
   strategy (a deadline would be multiplied by the retries unless mapped to a non-transient error), and none of it
   can be tested without PostgreSQL.
3. **Raise the command timeout of the whole context** — also lifts the bound for every other command at runtime, the
   scans' queries included, to cover one startup statement.
4. **Rely on EF Core's own migration lock** (`LOCK TABLE "__EFMigrationsHistory"` in the Npgsql history repository)
   **and drop the application lock** — the table lock is a blocking statement under the same command timeout, so the
   defect stays, and the serialization the guarantee describes would rest on a provider internal.

## Decision

The application polls `pg_try_advisory_lock` with the unchanged key on the connection that runs the migrations, waits
`MigrationRetryDelaySeconds` between attempts, and fails startup with a logged, dedicated error naming
`DockerUpdateGuard:Database:MigrationLockTimeoutSeconds` (default `900`) when an attempt at or after the deadline
fails. Every exception but a cancellation that fails the migration is logged through the migrator's failure event.

## Consequences

- An instance now waits for a long migration on another instance up to `MigrationLockTimeoutSeconds` instead of
  failing after the retry policy runs out; operators with migrations longer than 15 minutes raise the key.
- A transient fault during the wait re-runs lock and migration on a new connection but does not restart the
  deadline.
- The wait runs before the host starts, when no shutdown signal handling is registered yet, so `ApplicationStopping`
  never cancels it: a termination signal ends the process at once, and a lock it held ends with its session.
- The statements of the migration itself are still bounded by the command timeout on the migrating instance; that is
  a separate gap with its own issue.
- The production try-lock query is not covered by unit tests; it is checked once against a real PostgreSQL by hand.