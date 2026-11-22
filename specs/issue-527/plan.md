# Plan: Image catalog get-or-create takes every failed save for a lost race

Source: Issue #527
Tier: standard — changes which save failures the catalog's get-or-create methods absorb (error behavior), and creates, changes or removes no security control.
Security areas touched: none

## Problem / root cause

`src/DockerUpdateGuard.Data/Repositories/ImageCatalogRepository.cs`: `GetOrCreateRegistryRepositoryAsync` (catch at
line 152) and `GetOrCreateImageVersionAsync` (catch at line 214) catch every `DbUpdateException`, detach the new entity
and re-read with `SingleAsync`. A failure that is not the insert race leaves nothing to re-read, so `SingleAsync` throws
`InvalidOperationException` ("Sequence contains no elements") and the original exception is lost. Related defect found on
the way: `SaveChangesAsync` saves every pending change of the caller's context, so a unique violation of *another*
pending entity is also taken for the race and ends the same way; the fix covers it (AC5).

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | Both methods catch every `DbUpdateException`, detach and re-read with `SingleAsync`; nothing is logged or rethrown | `ImageCatalogRepository.cs:152-161`, `:214-225` | confirmed |
| 2 | A length overflow reaches the catch as `DbUpdateException` | EFCore.PG `NpgsqlModificationCommandBatch.cs` at tag `v11.0.0`, lines 190-204, wraps every non-`DbUpdateException`, non-cancellation exception of a batch in `DbUpdateException` with the provider exception as `InnerException`; PostgreSQL reports overflow as SQLSTATE `22001`; #343 shows the registration case | confirmed |
| 3 | Issue: "a lost connection" reaches the catch | The host uses `EnableRetryOnFailure` (`src/DockerUpdateGuard/ServiceCollectionExtensions.cs:67`): a transient failure is retried and ends as EF Core's `RetryLimitExceededException`, which the catch does not see; a non-transient connection failure inside the batch is a `DbUpdateException`. Only partly true, and irrelevant to the fix, which rethrows anything but `23505` | refuted |
| 4 | A unique violation arrives as `DbUpdateException` whose `InnerException` is `Npgsql.PostgresException` with `SqlState` `23505` | Fact 2's wrapping; `Npgsql.PostgresErrorCodes.UniqueViolation` = `23505` and `StringDataRightTruncation` = `22001` exist in Npgsql 11.0.0 (`~/.nuget/packages/npgsql/11.0.0`, strings of `Npgsql.dll`); the tests' substituted failure rests on this fact | confirmed |
| 5 | Tests can build that failure: `PostgresException(string messageText, string severity, string invariantSeverity, string sqlState)` is public | `Npgsql.xml` 11.0.0 line 8108 | confirmed |
| 6 | SQLite cannot produce the failure: `SqliteException` does not set `SqlState`, and the data tests run on SQLite | `SqliteException.cs` (dotnet/efcore main) has no `SqlState`; `SqliteTestDatabase.cs` | confirmed |
| 7 | No caller runs get-or-create inside an explicit transaction (a PostgreSQL error would abort it and fail the re-read) | no `BeginTransaction` outside tests and migrations (`grep`) | confirmed |
| 8 | No caller filters on `InvalidOperationException` or `DbUpdateException` from these methods; the registration page maps both to `unexpected error` | `grep` of `src/DockerUpdateGuard`; `ObservedImages.razor.cs:200-209`, `FailureReason.FromException` | confirmed |
| 9 | Npgsql is reachable from the data project without a new reference | `DockerUpdateGuard.Data.csproj` references `Npgsql.EntityFrameworkCore.PostgreSQL`, which depends on `Npgsql` | confirmed |

## Acceptance criteria

- AC1: When the save of a new image version fails with a `DbUpdateException` whose inner exception is not a
  `PostgresException` with SQLSTATE `23505`, `GetOrCreateImageVersionAsync` throws that same `DbUpdateException`
  instance, and the context no longer tracks the new image version.
- AC2: AC1 for `GetOrCreateRegistryRepositoryAsync` and the new registry repository.
- AC3: When the save of a new image version fails with a `23505` unique violation and the same registry repository,
  tag and digest exist (written by a competing writer), `GetOrCreateImageVersionAsync` returns that entry, with
  `RegistryRepository` populated.
- AC4: AC3 for `GetOrCreateRegistryRepositoryAsync` and a competing registry repository.
- AC5: When the save fails with a `23505` unique violation but the re-read finds no matching entry, each method throws
  the original `DbUpdateException` instance, not `InvalidOperationException`.

## Tests per criterion

All in `src/Tests/DockerUpdateGuard.Data.Tests/ImageCatalogRepositoryTests.cs`, on SQLite with the substituted save
failure of *Test files*.

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `ImageCatalogRepositoryGetOrCreateImageVersionAsyncNonUniqueSaveFailureRethrowsOriginalExceptionAsync` (SQLSTATE `22001`; `Assert.AreSame` on the exception; no tracked `ImageVersion` entry) | `ImageCatalogRepositoryTests.cs` |
| AC2 | `ImageCatalogRepositoryGetOrCreateRegistryRepositoryAsyncNonUniqueSaveFailureRethrowsOriginalExceptionAsync` | `ImageCatalogRepositoryTests.cs` |
| AC3 | `ImageCatalogRepositoryGetOrCreateImageVersionAsyncLostInsertRaceReturnsCompetingVersionAsync` — passes on today's code, so deferred to step 6 (regression guard of the new filter) | `ImageCatalogRepositoryTests.cs` |
| AC4 | `ImageCatalogRepositoryGetOrCreateRegistryRepositoryAsyncLostInsertRaceReturnsCompetingRepositoryAsync` — deferred to step 6 like AC3 | `ImageCatalogRepositoryTests.cs` |
| AC5 | `ImageCatalogRepositoryGetOrCreateImageVersionAsyncUniqueViolationWithoutEntryRethrowsOriginalExceptionAsync`, `ImageCatalogRepositoryGetOrCreateRegistryRepositoryAsyncUniqueViolationWithoutEntryRethrowsOriginalExceptionAsync` | `ImageCatalogRepositoryTests.cs` |

## Approach

In both catch blocks, in this order:

1. `catch (DbUpdateException exception)`; detach the new entity first, as today, so a long-lived context (the
   registration page's circuit, decision 0040's consequences) does not save the failed entity again.
2. If `IsUniqueViolation(exception) == false`: `throw;`.
3. Re-read with `SingleOrDefaultAsync` (same predicates, the image version still with `.Include(RegistryRepository)`);
   `null` → `throw;` (rethrow after `await` inside the catch keeps the original exception and stack).
4. Return the entry.

`IsUniqueViolation` is one private static helper shared by both methods:
`exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }` (`using Npgsql;`, in
the third-party using group after `Microsoft.EntityFrameworkCore`). It goes into the existing `Methods` region with the
other private static helpers. No logging is added: the repository has no logger, and the callers log the rethrown
exception (fact 8).

No user-visible output changes.

Early draft PR: no

## Options considered

1. Recognize only PostgreSQL's `23505`, rethrow when the re-read finds nothing (chosen).
2. Catch every `DbUpdateException` and rethrow only when the re-read finds nothing — lost: a non-collision failure still
   runs a query, and a coincidental row hides a real error; the issue asks for the unique violation only.
3. Also recognize SQLite's unique violation — lost: the production data project would reference the SQLite provider.
4. `EntityFramework.Exceptions` — lost: a new dependency and interceptor for one check.
5. `DbException.SqlState` without the Npgsql type — lost: `23505` is PostgreSQL's code; naming the type states the
   coupling.

Details in decision record 0056.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard.Data | `Repositories/ImageCatalogRepository.cs` | both catch blocks as *Approach*; new private static `IsUniqueViolation` |
| DockerUpdateGuard.Data.Tests | `ImageCatalogRepositoryTests.cs`, `Data/SqliteTestDatabase.cs`, new `Data/FailingSaveChangesInterceptor.cs` | tests and test seam (*Test files*) |

## Signatures (for the Dev's skeleton)

None: no public or internal API member changes. The only new member is the private static helper of *Approach*
(`bool IsUniqueViolation(DbUpdateException exception)`), which the Dev writes with the implementation; step 4 is
skipped.

## Test files

- `src/Tests/DockerUpdateGuard.Data.Tests/ImageCatalogRepositoryTests.cs` — the tests above (Tester).
- Seam (Tester): a test-only `SaveChangesInterceptor`, `src/Tests/DockerUpdateGuard.Data.Tests/Data/FailingSaveChangesInterceptor.cs`,
  registered through a new `SqliteTestDatabase.CreateDbContext` overload that takes interceptors (the parameterless
  call sites stay unchanged). In `SavingChangesAsync`, only when the tracker holds an *Added* entity of the configured
  type (so the image version test's preceding registry repository save passes), it optionally runs a competing insert
  through a second context of the same `SqliteTestDatabase` (AC3, AC4; no transaction of the first context is open
  yet) and then throws `new DbUpdateException(<text>, new PostgresException(<text>, "ERROR", "ERROR", <sqlState>))`;
  the test keeps the thrown instance for `Assert.AreSame`. The substituted exception rests on fact 4.

Existing test code that calls a changed signature: none.

Files of the change no unit test can cover: none.

## Areas

- `docs/areas/image-catalog.md` (+30 words): in *The catalog*, the **Concurrency** item adds that only a unique-key
  collision is taken for the race, and that every other failed save — and a collision after which the entry is not
  found — reaches the caller as the original database error, with the new entry no longer tracked.

## Documentation updates

- `docs/areas/image-catalog.md` — as *Areas* (Dev), with a link to 0056 under *Related decisions*.
- Sweep: `docs/ARCHITECTURE.md` lines 214 and 470 ("race-retry path") and 588 stay true — no change. No changelog
  exists; the PR template asks for none beyond the area document and the record.

## Architecture check

The catalog's uniqueness guarantee (`ARCHITECTURE.md` §5.2, single-instance note) is kept: a real race still returns the
other writer's entry. No guarantee of `.squad/project.md` is touched.

## Security considerations

- [x] No limits.
- [x] Exceptions reaching a user-visible message: the rethrown `DbUpdateException` replaces `InvalidOperationException`
  on the registration page and in scan runs; both map to the fixed `unexpected error` (`FailureReason.FromException`),
  so no database text reaches a page. The logged exception now carries the PostgreSQL error (SQLSTATE and message,
  no row values for `22001`); logs already carry other save failures the same way. No query built from input changes,
  so the *Database and migrations* area is not touched.

## Decision records

- `docs/decisions/0056-the-image-catalog-takes-only-a-postgresql-unique-violation-for-a-lost-insert-race.md` (Proposed)

## Out of scope / follow-ups

None.
