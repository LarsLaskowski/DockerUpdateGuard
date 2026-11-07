# Plan: Read the Retry-After date against an injected TimeProvider in TransientHttpRetryHandler

Source: Issue #321
Tier: standard — production code changes (a constructor parameter and the clock a private method reads) without a change in behavior or a security control, and its tests are rewritten against a substituted clock.
Security areas touched: Failure messages and remote text (`Infrastructure/TransientHttpRetryHandler.cs` is listed there; the change does not touch what a message, log entry or result carries)

## Problem / root cause

`TransientHttpRetryHandler.GetRetryAfter` (`src/DockerUpdateGuard/Infrastructure/TransientHttpRetryHandler.cs:190`)
computes the wait for a `Retry-After` date as `date - DateTimeOffset.UtcNow`, so the handler has no seam for the
clock and its tests build the header from the real clock and assert a tolerance window
(`TransientHttpRetryHandlerTests.cs:343`, `:349-354`). `docs/UNIT_TESTS.md` (*Time in tests*) requires an
injected `TimeProvider` once a service's time-dependent logic is changed.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | The issue: `GetRetryDelay` computes the wait from `DateTimeOffset.UtcNow` at line 231 | The clock is read in `GetRetryAfter` at line 190; line 231 is the XML doc of `LogNotRetried`; `GetRetryDelay` (line 309) only clamps the value `GetRetryAfter` returned; the substance (the wait for a date reads the real clock) holds | refuted |
| 2 | The tests depend on the real clock with tolerance margins | `TransientHttpRetryHandlerTests.cs:343` (date `UtcNow + 30 s`, asserted between 27 and 30 s, lines 349-354), `:369` (`UtcNow - 30 s`), `:674` (`UtcNow + 1 h`) | confirmed |
| 3 | The handler has an internal constructor for test seams that the public one delegates to | `TransientHttpRetryHandler.cs:51-76` (`delayAsync`, `nextJitterMilliseconds`) | confirmed |
| 4 | The clock decides two paths: the retry wait (`SendAsync`, line 363) and whether a non-idempotent 429 is refused for a wait over 30 s (`IsNotRetried`, line 291) | `TransientHttpRetryHandler.cs:291`, `:363`, `:293` | confirmed |
| 5 | No `TimeProvider` is registered in DI and no production type uses one yet; DI builds the handler through its public constructor | `grep -rn TimeProvider src` (no production hit); `ServiceCollectionExtensions.cs:72` (`AddTransient<TransientHttpRetryHandler>()`) | confirmed |
| 6 | Existing callers of the internal constructor | `TransientHttpRetryHandlerTests.cs:69` (helper `SendThroughRetryHandlerAsync`), `PortainerClientTests.cs:836` | confirmed |
| 7 | `TimeProvider.GetUtcNow` is virtual and NSubstitute is referenced by the host tests | `docs/UNIT_TESTS.md:284`; `DockerUpdateGuard.Tests.csproj:46` | confirmed |

## Acceptance criteria

- AC1: For a GET answered 503 with a `Retry-After` date 7 seconds after the injected clock's `GetUtcNow`, the
  handler waits exactly 7 seconds before the retry.
- AC2: For a `Retry-After` date equal to or before the injected clock's now, the handler waits the exponential
  backoff (500 ms for the first retry, no jitter), as without a header.
- AC3: A POST answered 429 with a `Retry-After` date 1 hour after the injected clock's now is returned without a
  retry; one with a date 10 seconds after it is retried after exactly 10 seconds.
- AC4: Neither the handler nor its tests read `DateTimeOffset.UtcNow`; the public constructor passes
  `TimeProvider.System`.

## Tests per criterion

All tests use a `Substitute.For<TimeProvider>()` whose `GetUtcNow()` returns a fixed whole-second instant in the
future, 2100-01-01T00:00:00Z: against the real clock every date relative to it lies decades ahead, so AC1, AC2 and
the retried case of AC3 fail on the current code (it clamps the wait to 30 s, or refuses the POST).

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `TransientHttpRetryHandlerHonorsRetryAfterDateHeaderAsync` (rewritten: exact 7 s, no tolerance) | `src/Tests/DockerUpdateGuard.Tests/TransientHttpRetryHandlerTests.cs` |
| AC2 | `TransientHttpRetryHandlerWithPastRetryAfterDateUsesExponentialBackoffAsync` (rewritten: now - 30 s), `TransientHttpRetryHandlerWithRetryAfterDateAtNowUsesExponentialBackoffAsync` (new) | `src/Tests/DockerUpdateGuard.Tests/TransientHttpRetryHandlerTests.cs` |
| AC3 | `TransientHttpRetryHandlerDoesNotRetryPostWhenRetryAfterDateExceedsMaximumAsync` (rewritten: now + 1 h; passes on the current code, a pin), `TransientHttpRetryHandlerRetriesPostAfterShortRetryAfterDateAsync` (new: 2 attempts, captured delays exactly [10 s]) | `src/Tests/DockerUpdateGuard.Tests/TransientHttpRetryHandlerTests.cs` |
| AC4 | check: `grep -n "UtcNow" src/DockerUpdateGuard/Infrastructure/TransientHttpRetryHandler.cs src/Tests/DockerUpdateGuard.Tests/TransientHttpRetryHandlerTests.cs` has no hit, and the public constructor passes `TimeProvider.System` (Reviewer, step 8) | — |

## Approach

- Add a private readonly field `_timeProvider` and a last parameter `TimeProvider timeProvider` to the internal
  constructor; the public constructor passes `TimeProvider.System`. Its XML doc names it as the clock the
  `Retry-After` date is measured against.
- `GetRetryAfter` becomes a private instance method (moved from *Static methods* to *Methods*, signature otherwise
  unchanged) and computes `date - _timeProvider.GetUtcNow()`. Both callers (lines 291, 363) stay as they are.
- Test helper `SendThroughRetryHandlerAsync` gets an optional `TimeProvider? timeProvider = null` parameter; when
  null it uses the substitute at the fixed instant above, so no test of the class reads the real clock (Tester).
- No user-visible output changes.

## Options considered

One option: the issue prescribes the seam, and the class already takes its other test seams (delay, jitter)
through the internal constructor that the public, DI-built constructor feeds with the production values; no
`TimeProvider` is registered in DI (fact 5), so resolving it there would add a registration for one consumer.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Infrastructure/TransientHttpRetryHandler.cs` | constructor parameter, field, `GetRetryAfter` reads the injected clock |
| DockerUpdateGuard.Tests | `TransientHttpRetryHandlerTests.cs` | helper parameter, three tests rewritten, two added |
| DockerUpdateGuard.Tests | `PortainerClientTests.cs` | call site passes the new argument |

## Signatures (for the Dev's skeleton)

- `internal TransientHttpRetryHandler(IOptionsMonitor<DockerUpdateGuardOptions> options, ILogger<TransientHttpRetryHandler> logger, Func<TimeSpan, CancellationToken, Task> delayAsync, Func<int, int> nextJitterMilliseconds, TimeProvider timeProvider)`
  — replaces the four-parameter internal constructor; the public constructor is unchanged in signature.

The skeleton stores the argument; `GetRetryAfter` keeps reading `DateTimeOffset.UtcNow` until step 6.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/TransientHttpRetryHandlerTests.cs`

Existing call sites of the changed constructor, adapted by the **Dev** in step 4 (the old signature goes away),
mechanically by appending `TimeProvider.System`: `TransientHttpRetryHandlerTests.cs:69` (the Tester then gives the
helper its clock parameter in step 5) and `PortainerClientTests.cs:836` (no `Retry-After` there, so the clock is
never read).

Files no unit test can cover: none.

## Areas

none — no change in behavior; `docs/areas/integration-failures.md:106` (`Retry-After` delta or date, capped at
30 seconds) stays true.

## Documentation updates

none. Sweep: `docs/ARCHITECTURE.md` and `docs/areas/` hold no list of the services that use `TimeProvider`;
`docs/UNIT_TESTS.md` (*Time in tests*) already describes this move; the PR template asks for no changelog.

## Architecture check

No guarantee of `.squad/project.md` is touched: the retry decisions, the 30-second cap and the 429 rule for
non-idempotent requests (`docs/decisions/0008-non-idempotent-requests-retry-only-when-not-processed.md`) are
unchanged; the composition root is unchanged (DI still uses the public constructor).

## Security considerations

- [x] no limits: the 30-second cap is unchanged and is applied to the same computed wait.
- [x] none: no exception text reaches a message; the clock read cannot throw.

## Decision records

- none: no decision beyond the obvious fix.

## Out of scope / follow-ups

- Other services that still read `DateTimeOffset.UtcNow` move when they are changed (`docs/UNIT_TESTS.md`).

## Security notes

- AC4 check (non-blocking, Security): the pattern `grep -n "UtcNow"` also matches `_timeProvider.GetUtcNow()` and the substitute setup. Use `grep -nE "DateTime(Offset)?\.(UtcNow|Now)"` instead; the criterion is unchanged.
