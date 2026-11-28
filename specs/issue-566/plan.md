# Plan: Guard the Docker Scout token expiry conversion

Source: Issue #566
Tier: standard — a behavior change (an untrusted token payload no longer fails the lookup) that creates, changes or removes no security control; the check only keeps malformed input from breaking the program.
Security areas touched: Secrets in configuration — the lifetime of the cached Docker Hub token the PAT login returns (`Vulnerabilities/DockerScoutVulnerabilityProvider.cs`)

## Problem / root cause

`DockerScoutVulnerabilityProvider.ResolveTokenExpiryUtc` (`src/DockerUpdateGuard/Vulnerabilities/DockerScoutVulnerabilityProvider.cs:294-300`)
reads the payload's `exp` with `TryGetInt64` without checking `ValueKind`, and converts it with
`DateTimeOffset.FromUnixTimeSeconds` without a range check. The `catch` (line 303) takes only `FormatException` and
`JsonException`, so a string `exp` (`InvalidOperationException`) or an out-of-range one (`ArgumentOutOfRangeException`)
leaves the method. The same holds for a payload whose root is not an object (`TryGetProperty` throws
`InvalidOperationException`), a related defect the issue does not name.

The exception passes through `GetOrCreateTokenAsync` (line 402, after `_cachedToken` was already set at line 401) and
`FetchWithTokenRefreshAsync` to the catch-all of `GetVulnerabilitiesAsync` (line 681): the lookup is *failed*,
"Docker Scout scan failed: …", logged as error 3414. Every lookup logs in again on the first token (the expiry stays
`default`); after a refresh the token is kept until the previous token's expiry.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | `exp` is read with `TryGetInt64` without a `ValueKind` check and converted with `FromUnixTimeSeconds` without a range check | `DockerScoutVulnerabilityProvider.cs:296-299` | confirmed |
| 2 | `JsonElement.TryGetInt64` throws `InvalidOperationException` when `ValueKind` is not `Number`; `TryGetProperty` throws it when the element is not an object | .NET API documentation of `JsonElement.TryGetInt64` and `TryGetProperty` (*Exceptions*) | confirmed |
| 3 | `FromUnixTimeSeconds` throws `ArgumentOutOfRangeException` outside -62135596800 … 253402300799 | .NET API documentation; the same bounds as `MinimumExpirationUnixTimeSeconds`/`MaximumExpirationUnixTimeSeconds`, `DockerHubAccessTokenCache.cs:31,36` | confirmed |
| 4 | The catch takes only `FormatException` and `JsonException` | `DockerScoutVulnerabilityProvider.cs:303` | confirmed |
| 5 | An escaping exception fails the lookup through the catch-all, error 3414 | `DockerScoutVulnerabilityProvider.cs:681-686`; `DockerScoutVulnerabilityProviderLogging.cs` (EventId 3414) | confirmed |
| 6 | `DockerHubAccessTokenCache` guards root kind, `ValueKind == Number` and the range | `DockerHubAccessTokenCache.cs:228-233` | confirmed |
| 7 | `ExternalTimestamp.TryFromUnixSeconds` exists on `main`, never throws and returns null for `0`, a negative value and a value past `DateTimeOffset.MaxValue` | `src/DockerUpdateGuard/Infrastructure/ExternalTimestamp.cs:32-41`; `ExternalTimestampTests.cs:102-130` | confirmed |
| 8 | Today an unreadable expiry (no `exp`, a fraction, a number beyond `long`, a payload that is not Base64 or JSON) gives `now + 5 minutes`, logged as 3418 only when an exception was caught | `DockerScoutVulnerabilityProvider.cs:41,284,303-308`; `DockerScoutTokenExpiryTests.cs:48-121` | confirmed |
| 9 | Today an `exp` of `0` or a negative in-range value gives an expiry in the past, so the token is never reused | `DockerScoutVulnerabilityProvider.cs:299,380-385` | confirmed |
| 10 | The provider reads the real clock (`DateTimeOffset.UtcNow`, lines 284, 308, 382); `TimeProvider` is registered as a singleton and the provider is resolved by `AddSingleton<DockerScoutVulnerabilityProvider>()` | `ServiceCollectionExtensions.cs:130,141` | confirmed |
| 11 | The constructor has six parameters; a seventh stays within S107 (more than seven is refused) | `DockerScoutVulnerabilityProvider.cs:101-106`; `.squad/stack.md`, *Writing code* | confirmed |
| 12 | The constructor is called in five places in the tests | `DockerScoutTokenExpiryTests.cs:184`; `DockerScoutVulnerabilityProviderTests.cs:668,2524,2589`; `VulnerabilityProviderResolverTests.cs:86` | confirmed |

## Acceptance criteria

- AC1: A token whose `exp` is a JSON string (`"1700000000"`), or whose payload root is not an object (`[]`, `"text"`,
  `null`), resolves to the fallback expiry, exactly the clock's now plus five minutes, without an exception; a lookup
  with such a token succeeds, logs no error 3414, and a second lookup within five minutes reuses the token (one login).
- AC2: A token whose `exp` is a number `TryFromUnixSeconds` rejects (`0`, `-1`, `253402300800`, `9223372036854775807`)
  resolves to the fallback expiry, without an exception.
- AC3: A token with an in-range `exp` resolves to that instant minus one minute, also at the upper bound
  `253402300799` (no change, pinned on the injected clock path).
- AC4: The provider reads the time only from the injected `TimeProvider`: the fallback expiry and the cache check
  follow a substituted clock (the fallback test of AC1 on a fixed instant; the reuse in AC1 decided by that clock).

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `DockerScoutTokenExpiryNonNumericExpClaimFallsBackToDefaultLifetime`, `DockerScoutTokenExpiryNonObjectPayloadFallsBackToDefaultLifetime` (data rows `[]`, `"text"`, `null`), `DockerScoutVulnerabilityProviderGetVulnerabilitiesAsyncWithNonNumericExpSucceedsAndReusesTokenAsync` | `DockerScoutTokenExpiryTests.cs`, `DockerScoutVulnerabilityProviderTests.cs` |
| AC2 | `DockerScoutTokenExpiryOutOfRangeExpClaimFallsBackToDefaultLifetime` (data rows `0`, `-1`, `253402300800`, `9223372036854775807`) | `DockerScoutTokenExpiryTests.cs` |
| AC3 | `DockerScoutTokenExpiryInRangeExpClaimResolvesExpiryWithSafetyMargin` (data rows a mid value and `253402300799`) | `DockerScoutTokenExpiryTests.cs` |
| AC4 | `DockerScoutVulnerabilityProviderGetVulnerabilitiesAsyncCachedTokenExpiresOnInjectedClockAsync` (two lookups, the substituted clock advanced past the fallback expiry between them: two logins) | `DockerScoutVulnerabilityProviderTests.cs` |

The AC1, AC2 and AC4 tests fail on the unfixed head (exception, or the real clock); AC3 is a pin of the
upper-bound path and may pass on the skeleton — the Tester defers it to step 6 if so. The AC1/AC2 reflection tests
assert `Assert.AreEqual(fixedNow + 5 minutes, resolved)` on a clock substituted with NSubstitute (`GetUtcNow`),
never against `DateTimeOffset.UtcNow`.

## Approach

In `ResolveTokenExpiryUtc` the condition becomes the guarded chain of `DockerHubAccessTokenCache`, ending in the
shared conversion:

```csharp
if (payloadDocument.RootElement.ValueKind == JsonValueKind.Object
    && payloadDocument.RootElement.TryGetProperty("exp", out var expiryElement)
    && expiryElement.ValueKind == JsonValueKind.Number
    && expiryElement.TryGetInt64(out var expirySeconds)
    && ExternalTimestamp.TryFromUnixSeconds(expirySeconds) is { } expiryUtc)
{
    return expiryUtc - _tokenExpirySafetyMargin;
}
```

The `catch` stays as it is (`FormatException`, `JsonException`); it is not widened. A value the chain rejects falls
through to the fallback as a missing `exp` does today, without a 3418 entry (only a caught exception logs it, as now).
`expiryUtc - 1 minute` cannot underflow: `TryFromUnixSeconds` returns at least 1970-01-01T00:00:01Z.

Consequence, deliberate: an `exp` of `0` or a negative value (fact 9) now gives the five-minute fallback instead of an
already expired token, the rule `TryFromUnixSeconds` applies to every Unix-seconds field (`docs/areas/docker-instances.md`,
history `Created`). Should Docker Scout refuse such a token, the existing one-time refresh after a 401 still applies.

Clock: the provider moves to the injected `TimeProvider` (`docs/UNIT_TESTS.md`, *Time in tests*: an existing service
moves when it is changed). A new field `private readonly TimeProvider _timeProvider;` (region *Fields*), assigned in
the constructor; the three `DateTimeOffset.UtcNow` reads (lines 284, 308, 382) become `_timeProvider.GetUtcNow()`.
DI needs no change (fact 10).

No new user-visible output.

Early draft PR: no — every criterion is a unit test

## Options considered

- **Widen the catch** to `InvalidOperationException` and `ArgumentOutOfRangeException` — lost: the issue asks for the
  guard; a broad catch also hides unrelated defects and reaches 3418 with a misleading "could not be read" for a bug.
- **Keep `DateTimeOffset.UtcNow` and test the fallback against the real clock** as the existing tests of the class do —
  lost: *Time in tests* forbids real-clock tests for changed time-dependent logic, and only the injected clock lets the
  tests assert the exact fallback instant.
- **Keep `0` and negative `exp` as an expired token** (range check of `DockerHubAccessTokenCache` instead of
  `TryFromUnixSeconds`) — lost: the issue names `TryFromUnixSeconds`, an already expired token would make every lookup
  log in again, and `0`/negative is "unknown" for every other Unix-seconds field.
- **One shared JWT expiry reader for both token caches** — lost: the two differ in margin, fallback and accepted range,
  so sharing them changes `DockerHubAccessTokenCache`'s behavior beyond this issue; the conversion they would share is
  already one helper (`ExternalTimestamp`).

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Vulnerabilities/DockerScoutVulnerabilityProvider.cs` | guarded `exp` read; `TimeProvider` injected and used for all three time reads |
| DockerUpdateGuard.Tests | `DockerScoutTokenExpiryTests.cs`, `DockerScoutVulnerabilityProviderTests.cs` | new tests; provider factories take a clock |
| DockerUpdateGuard.Tests | `VulnerabilityProviderResolverTests.cs` | call site adapted |

## Signatures (for the Dev's skeleton)

Changed constructor (seventh parameter appended, documented `<param name="timeProvider">Time provider</param>`):

`public DockerScoutVulnerabilityProvider(IHttpClientFactory httpClientFactory, IOptionsMonitor<DockerUpdateGuardOptions> optionsMonitor, ILogger<DockerScoutVulnerabilityProvider> logger, LoginRetryAfterRegistry loginRetryAfterRegistry, DockerHubRequestLimiter requestLimiter, IServiceScopeFactory serviceScopeFactory, TimeProvider timeProvider)`

The old six-parameter constructor is removed. The private `ResolveTokenExpiryUtc(string)` keeps its signature (the
reflection tests call it by name). The skeleton holds the field and its assignment for real; the time reads and the
guard are step 6.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/DockerScoutTokenExpiryTests.cs`
- `src/Tests/DockerUpdateGuard.Tests/DockerScoutVulnerabilityProviderTests.cs`

Existing call sites of the changed constructor: `DockerScoutTokenExpiryTests.cs:184`,
`DockerScoutVulnerabilityProviderTests.cs:668,2524,2589` and `VulnerabilityProviderResolverTests.cs:86` — the Dev adds
`TimeProvider.System` as the last argument in step 4 (old signature goes away), mechanically. In step 5 the Tester gives
the factory `CreateProvider` of `DockerScoutTokenExpiryTests.cs` and the one at line 2589 of
`DockerScoutVulnerabilityProviderTests.cs` an optional `TimeProvider? timeProvider = null` (default `TimeProvider.System`),
so the existing tests stay unchanged; none of them has an input a new criterion handles differently (they use
`exp`-less, in-range or non-JWT tokens). `DockerScoutTokenExpiryValidExpClaimResolvesExpiryWithSafetyMargin` is
superseded by the AC3 test and removed by the Tester.

Files no unit test can cover: none.

## Areas

- `docs/areas/vulnerabilities.md (+20 words)` — *Docker Scout*, the login bullet: "or for five minutes when the expiry
  cannot be read" names what counts as unreadable: no `exp`, one that is not a whole number, or one outside
  1 … 253402300799 seconds (`0` and negative values included).

## Documentation updates

- `docs/areas/vulnerabilities.md` — as *Areas*, Dev, against the code as built.
- Sweep: `docs/areas/integration-failures.md` (*Remote timestamps*, the list of timestamp fields) lists no token
  expiry (the Docker Hub token's `exp` is in `registries.md`), so it gets no entry; `docs/areas/registries.md:290`
  concerns registry tokens, unchanged; `docs/ARCHITECTURE.md:256` (the provider's row) names no clock for other
  clients either — unchanged. `README.md`: no setting changes. No changelog file exists in the repository.

## Architecture check

No guarantee of `.squad/project.md` or `docs/ARCHITECTURE.md` is touched: the token is still cached process-wide, one
login at a time, the PAT is not logged; the composition root is unchanged (the clock comes from the existing
registration).

## Security considerations

- [x] No limits: the payload size is not bounded here and the change adds no bound.
- [x] Exceptions reaching a user-visible message: none new — the guarded read throws nothing; `FormatException` and
  `JsonException` are caught as today. The change removes the path where `InvalidOperationException` or
  `ArgumentOutOfRangeException` reached "Docker Scout scan failed: …".

## Decision records

- none: no decision beyond the obvious fix (the guard follows `DockerHubAccessTokenCache`, the clock rule is
  `docs/UNIT_TESTS.md`'s).

## Out of scope / follow-ups

- `DockerHubAccessTokenCache` accepts a negative in-range `exp` (an expired token); unchanged here.
