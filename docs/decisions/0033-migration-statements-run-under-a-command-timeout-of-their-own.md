# 0033: Migration statements run under a command timeout of their own

- **Status:** Accepted
- **Date:** 2026-11-14
- **Area:** Hosting and configuration
- **Source:** Issue #470
- **Supersedes:** —

## Context

Startup applies the EF Core migrations on one connection under the migration advisory lock
([Hosting and configuration](../areas/hosting.md#startup-sequence),
[0032](0032-the-migration-lock-is-awaited-by-polling-against-its-own-deadline.md)). The application sets no command
timeout, so every migration statement runs under Npgsql's default of 30 seconds. A statement that takes longer, such
as an index build on a large table, is cancelled; Npgsql reports the timeout as a transient error, the retrying
execution strategy re-runs lock and migration, and the statement times out again until the retries run out and
startup fails. The database context of the migration is the scoped context that startup goes on to use for the
instance synchronization and the inventory gauges, and a command timeout set on it lasts until the context is
disposed. The wait for the lock runs before the host's shutdown handling is active. The unit tests cannot run
PostgreSQL (#28).

## Options considered

1. **A key of its own, `MigrationCommandTimeoutSeconds`, set on the context only while EF Core migrates and
   restored afterwards (chosen)** — the bound is lifted for the migration statements alone and stays configurable
   and finite; the queries after the migration keep the database command timeout. Costs one more key.
2. **No timeout (zero, which Npgsql reads as infinite) while migrating** — no key, but a statement that blocks, for
   example behind a lock another session holds, stalls startup with no end, and no shutdown signal handling is active
   to end it gracefully.
3. **Reuse `MigrationLockTimeoutSeconds`** — no new key, but one value would mean both how long other instances wait
   for a whole migration and how long one statement may run; raising one raises the other.
4. **Raise the timeout for the whole context** (`Command Timeout` in the connection string, or the provider's
   `CommandTimeout` option) — possible today without code, but it lifts the bound for every command at runtime, the
   scans' queries included, to cover the migration (the reason 0032 rejected the same for the lock).

## Decision

While `DatabaseMigrator` runs EF Core's migration, the database context's command timeout is
`DockerUpdateGuard:Database:MigrationCommandTimeoutSeconds`; afterwards, also after a failure, it is set back to the
value it had before. The key's default and validation are in the area document.

## Consequences

- A migration statement may run up to the configured time; operators with longer statements raise the key.
- A statement that still exceeds it remains a transient fault to the execution strategy, so lock and migration are
  re-run up to `MaxConnectionRetryCount` times before startup fails: that statement alone can hold startup for `MaxConnectionRetryCount` + 1 times the timeout.
- Instances waiting for the lock still give up after `MigrationLockTimeoutSeconds`; a migration that runs longer
  needs both keys raised.
- The advisory lock queries keep the database command timeout; they never block.
- The mechanism is unit-tested on SQLite through a command interceptor; a long statement on PostgreSQL is not.