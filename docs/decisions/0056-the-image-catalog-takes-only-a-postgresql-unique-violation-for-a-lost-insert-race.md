# 0056: The image catalog takes only a PostgreSQL unique violation for a lost insert race

- **Status:** Proposed
- **Date:** 2026-11-22
- **Area:** Image catalog
- **Source:** Issue #527
- **Supersedes:** —

## Context

`ImageCatalogRepository`'s get-or-create methods insert a missing registry repository or image version and, when two
writers insert the same entry at once, re-read the entry the other one created. They took every `DbUpdateException` for
that race. A save that failed for another reason (a value longer than its column, as #343 showed for the registration
form, or a collision of another pending change of the caller's context, whose save the catalog's own save also carries) re-read
nothing and surfaced as `InvalidOperationException` ("Sequence contains no elements"), losing the original error. The application runs only
against PostgreSQL; EF Core wraps the provider's `PostgresException` as the `DbUpdateException`'s inner exception. The
data tests run on SQLite, whose `SqliteException` carries no SQLSTATE.

## Options considered

1. **Take the race only for an inner `PostgresException` with SQLSTATE `23505` (unique violation) and rethrow the
   original exception when the re-read finds no entry (chosen)** — the error the issue names is passed on unchanged,
   and a unique violation of another pending change is rethrown when the re-read finds no entry (the SQLSTATE alone
   cannot tell which entity collided). Couples the repository to Npgsql, which
   the data project references already; the race path is tested with a substituted save failure, not a real SQLite
   collision.
2. **Keep catching every `DbUpdateException` and rethrow it when the re-read finds nothing** — provider-neutral, but a
   non-collision failure still runs a query (against a lost connection a second failure that can hide the first), and a
   coincidentally existing row would hide a real error.
3. **Recognize unique violations of PostgreSQL and SQLite** — real collisions in the SQLite tests, but the production
   data project would reference the SQLite provider for its tests' sake.
4. **The `EntityFramework.Exceptions` package (`UniqueConstraintException`)** — provider-neutral, but a new dependency
   with its own interceptor in the composition root for one check.
5. **Match `DbException.SqlState` without the Npgsql type** — the same outcome as option 1, but `23505` is a PostgreSQL
   code, not a portable one, so naming the provider's type and `PostgresErrorCodes.UniqueViolation` states the coupling
   honestly.

## Decision

Option 1. A save failure of a get-or-create method is taken for a lost race only when its inner exception is a
PostgreSQL unique violation and the re-read finds the entry; every other failure reaches the caller as the original
exception. The behavior is described in [Image catalog](../areas/image-catalog.md#the-catalog).

## Consequences

- Callers see the real cause of a failed catalog insert (the registration page logs it; its message stays
  `unexpected error`).
- The new entry is detached on every failure, as before, so a long-lived context (the registration page's circuit)
  does not save it again with its next change.
- Moving to another database provider needs a new recognizer for its unique violation.
