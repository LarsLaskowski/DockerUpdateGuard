# Plan: A non-idempotent request gives up on a 429 whose Retry-After exceeds the maximum wait

Source: Issue #316
Status: Revised (1)
Tier: standard — changes the retry behavior of `TransientHttpRetryHandler` for non-idempotent requests (one more case in which a `POST` is not retried) without creating, changing or removing a control that stops an attacker.
Security areas touched: Registry and Docker Hub calls (`TransientHttpRetryHandler`); Logging and telemetry of external data (new debug entry 3503 with the masked URL and the server's Retry-After value); Container actions (behavior only: the Portainer login and action `POST`s behind the handler, `PortainerClient` itself unchanged)

## Problem / root cause

`TransientHttpRetryHandler.SendAsync` lets a non-idempotent request through to the retry path on a 429
(`src/DockerUpdateGuard/Infrastructure/TransientHttpRetryHandler.cs:280`, `IsNotProcessedStatusCode` at 114–117),
then takes the wait from `GetRetryDelay` (287), which clamps a positive `Retry-After` delta (224–227) or date (229–236)
to `MaxBackoffMilliseconds` = 30000 (26) through `ClampToMaximum` (162–167). A server that answers a login `POST` with
429 and `Retry-After: 3600` therefore gets the same login again after 30 seconds, inside the window it asked for, up
to `RetryCount` times (default 2) — as long as the client's `RequestTimeoutSeconds` leaves room for the wait (fact 3). The decision that kept the 429 retry for non-idempotent methods,
[0008](../../docs/decisions/0008-non-idempotent-requests-retry-only-when-not-processed.md), did not weigh the clamp.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | Issue: the handler retries every method, `POST` logins included, on 429 | `TransientHttpRetryHandler.cs:273–285`: a non-idempotent request returns early only when `IsNotProcessedStatusCode` is false, i.e. for every transient status but 429; existing test `TransientHttpRetryHandlerRetriesPostOnTooManyRequestsAsync` | confirmed |
| 2 | Issue: the `Retry-After` wait is clamped to the maximum backoff (`ClampToMaximum`) | `TransientHttpRetryHandler.cs:224–236`, `162–167`; existing test `TransientHttpRetryHandlerClampsRetryAfterToMaximumBackoffAsync` expects 30000 ms for a 1 h delta | confirmed |
| 3 | Issue: a server asking for a longer wait gets the login again early — only when the client's `RequestTimeoutSeconds` exceeds the 30 s wait plus the first attempt; with the defaults (Portainer 15 s, Docker Hub 30 s) the `HttpClient.Timeout` cancels the wait and the caller gets a timeout instead of the 429 | Follows from 1 and 2 with `RetryCount` ≥ 1 (default 2, `docs/areas/registries.md:159`); `PortainerOptions.cs:48`, `DockerHubOptions.cs:33` (validator allows up to 300 s) | confirmed |
| 4 | Issue: the early resend can extend a rate limit or lockout | to verify: server-side behavior of Docker Hub and Portainer, not measurable in the session (needs their real limiter); owner none, no step depends on it — the fix honors the server's window whatever the server does with an early resend | unverified |
| 5 | The non-idempotent requests behind the handler are the Docker Hub login, the Portainer login and the Portainer action, all `POST` | `DockerHubClient.cs:1031`, `PortainerClient.cs:210`, `PortainerClient.cs:395`; Docker Scout's login (`DockerScoutVulnerabilityProvider.cs:226`) has no handler (`docs/areas/vulnerabilities.md:169`) | confirmed |
| 6 | The callers report a returned 429 as a failure with its status: Docker Hub login → *failed* with the status and body, Portainer login → `PortainerAuthenticationException` "failed with HTTP 429", Portainer action → "Action '…' failed: HTTP 429" | `DockerHubClient.cs:1039–1048`, `PortainerClient.cs:228–232`, `PortainerClient.cs:410–416` | confirmed |
| 7 | The forms of `Retry-After` the handler reads are `RetryConditionHeaderValue.Delta` (delta-seconds) and `.Date` (HTTP-date); an invalid header leaves `Headers.RetryAfter` null, so the exponential backoff applies | `TransientHttpRetryHandler.cs:220–238`; RFC 9110 §10.2.3 defines exactly these two forms | confirmed |
| 8 | A delta-seconds value beyond `int.MaxValue` (about 68 years), and a negative, signed or fractional delta, does not parse in .NET, so the handler sees no `Retry-After` and retries with the exponential backoff; `2147483647` parses as a delta, a far-future date does not overflow, a duplicated header gives the delta | Security's measurement on .NET SDK 10.0.402 in step 3 | confirmed |
| 9 | `0008` is unreleased and is edited in place | `git tag --contains eba66b9` (the commit that added it) prints nothing; latest remote tag `v1.3.7` (`git ls-remote --tags origin`) | confirmed |
| 10 | The Portainer client's `HttpClient.Timeout` (default 15 s) and the Docker Hub/registry clients' (default 30 s) bound a request including its retry waits, so a wait longer than the remaining time ends in a cancellation, not a resend | `PortainerClient.cs:196`, `ServiceCollectionExtensions.cs:76–96`; `docs/areas/registries.md:176–178`, `docs/areas/container-actions.md:78` | confirmed |
| 11 | The callers log in again on their own after a failed login: the Docker Hub client does not cache a failed token request or its `Retry-After`, so every later authenticated request of the same client instance posts `v2/users/login` again; the Portainer client keeps no JWT and posts `/api/auth` per capability check and per action | `DockerHubClient.cs:864` (`SendGetAsync` → `ApplyAuthenticationAsync`), `DockerHubClient.cs:953–1002` (`GetAccessTokenAsync` returns the failure uncached); `PortainerClient.cs:181–232` (`CreateAuthenticatedClientAsync`), called at 278 and 360 | confirmed |

## Acceptance criteria

- [ ] AC1: A non-idempotent request (`POST`, `PATCH`) answered with 429 and a `Retry-After` delta longer than 30
  seconds is sent once: the handler returns that 429 response to the caller and waits not at all.
- [ ] AC2: The same holds for a `Retry-After` date more than 30 seconds in the future.
- [ ] AC3: A non-idempotent request answered with 429 and a `Retry-After` of at most 30 seconds, or without
  `Retry-After`, is still retried after that wait or the exponential backoff (30 seconds exactly is retried).
- [ ] AC4: An idempotent request answered with 429 and a `Retry-After` longer than 30 seconds is still retried after
  the clamped 30-second wait (unchanged).
- [ ] AC5: When AC1 or AC2 ends a request that still had retries left, the handler logs one Debug entry with event id
  3503 and the text `Transient HTTP response 429 for POST https://***@registry.example.test/path not retried: Retry-After of 3600000 ms exceeds the maximum wait of 30000 ms for a method that is not idempotent`
  (for a `POST` to `https://user:secret@registry.example.test/path?token=x` with `Retry-After: 3600`), and no 3500
  warning; with the retries used up (`RetryCount` 0) no 3503 entry is logged. The decision path is the branch in
  `SendAsync` that runs only after `attempt >= retryCount` returned false.

## Tests per criterion

All in `src/Tests/DockerUpdateGuard.Tests/TransientHttpRetryHandlerTests.cs`, through the existing
`SendThroughRetryHandlerAsync`, `CreatePostRequest` and `RetryProbeHttpMessageHandler` (its `RetryAfter` property).

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `TransientHttpRetryHandlerDoesNotRetryNonIdempotentRequestWhenRetryAfterExceedsMaximumAsync` — data rows (`POST`, 31), (`POST`, 3600), (`PATCH`, 3600): outcomes `[429, 200]`, `RetryCount` 2; asserts status 429, `AttemptCount` 1, no captured delay | `src/Tests/DockerUpdateGuard.Tests/TransientHttpRetryHandlerTests.cs` |
| AC2 | `TransientHttpRetryHandlerDoesNotRetryPostWhenRetryAfterDateExceedsMaximumAsync` — `Retry-After` date `UtcNow + 1 h`, outcomes `[429, 200]`; asserts status 429, `AttemptCount` 1, no captured delay | `src/Tests/DockerUpdateGuard.Tests/TransientHttpRetryHandlerTests.cs` |
| AC3 | new data row `(30, 30000)` on the existing `TransientHttpRetryHandlerRetriesPostOnTooManyRequestsAsync` (rows 7 s and none stay) — passes on today's code, so it is deferred to step 6 (it pins the boundary against `>=`) | `src/Tests/DockerUpdateGuard.Tests/TransientHttpRetryHandlerTests.cs` |
| AC4 | `TransientHttpRetryHandlerClampsRetryAfterOfTooManyRequestsForGetAsync` — `GET`, outcomes `[429, 200]`, `Retry-After` 1 h; asserts status 200, `AttemptCount` 2, delays `[30000 ms]` — passes on today's code, deferred to step 6 (the existing `…ClampsRetryAfterToMaximumBackoffAsync` covers 503 only) | `src/Tests/DockerUpdateGuard.Tests/TransientHttpRetryHandlerTests.cs` |
| AC5 | `TransientHttpRetryHandlerLogsDebugWhenPostIsNotRetriedOnLongRetryAfterAsync` (exact text, level Debug, no `secret`, no `token=x`, no 3500 entry); `TransientHttpRetryHandlerWithZeroRetryCountDoesNotLogLongRetryAfterAsync` (no 3503 entry, status 429, one attempt) | `src/Tests/DockerUpdateGuard.Tests/TransientHttpRetryHandlerTests.cs` |

## Approach

- Split the `Retry-After` reading out of `GetRetryDelay` into a static `GetRetryAfter(HttpResponseMessage?)` that
  returns the positive, unclamped wait the server asked for (delta, or date minus `DateTimeOffset.UtcNow`), or
  null. `GetRetryDelay` takes that value instead of the response and clamps it as today; with null it computes the
  exponential backoff with jitter as today. The date is read against the clock once per response, so the check and
  the wait use the same value.
- In `SendAsync`, after the existing non-idempotent check (line 280–285) and before disposing the response: read
  `GetRetryAfter(response)` once; when the request is not idempotent and the value exceeds
  `MaxBackoffMilliseconds`, log entry 3503 and return the response undisposed. Otherwise pass the value to
  `GetRetryDelay`. The exception path passes null. Idempotent requests keep the clamp.
- Only a 429 reaches the new branch (the only status a non-idempotent request may be retried on); the log message
  still takes the status as a placeholder so it stays true if that set grows.
- Log entry 3503, `LogLevel.Debug` like 3502, because the caller already reports the 429 as a failure (fact 6):
  `Transient HTTP response {StatusCode} for {Method} {RequestTarget} not retried: Retry-After of {RetryAfterMilliseconds} ms exceeds the maximum wait of {MaximumMilliseconds} ms for a method that is not idempotent`.
  `RetryAfterMilliseconds` is a `long` (a date far in the future overflows `int` milliseconds), computed as
  `(long)retryAfter.TotalMilliseconds`; the request target is the existing `GetRequestTarget` value (user info
  masked, no query). Guarded by `_logger.IsEnabled(LogLevel.Debug)` like `LogNotRetried`.
- No new user-visible output beyond that log line; no configuration.

## Options considered

1. **Give up on a non-idempotent 429 whose `Retry-After` exceeds 30 s (chosen)** — the retry handler does not resend
   the request inside the server's window (the callers' own next login is a follow-up, *Challenge* 1), the caller gets the server's own 429, and a short `Retry-After` is still honored
   exactly, which keeps the retry 0008 deliberately kept.
2. **Wait the full `Retry-After` for non-idempotent requests (no clamp)** — the `HttpClient.Timeout` of every client
   behind the handler (Portainer 15 s, Docker Hub 30 s by default, fact 10) cancels such a wait, so the caller would
   get a timeout instead of the 429; an hours-long value would also hold a scan or UI operation. Lost.
3. **Give up for every method** — would change the documented Docker Hub rate-limit handling of `GET`s
   (`docs/areas/registries.md:149`) beyond the issue; an early `GET` runs no effect twice and does not count toward a
   login lockout. Lost; no follow-up.
4. **Never retry a non-idempotent 429** — also safe, but drops the retry of a short, honored `Retry-After` that
   0008 kept on purpose (a 429 is the one status that proves the request was not processed). Lost.
5. **Reuse log entry 3502** — its text names only the method as the reason; a separate id tells the operator that
   the server's window, not the status alone, stopped the retry. Lost.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Infrastructure/TransientHttpRetryHandler.cs` | new `GetRetryAfter`, `LogRetryAfterExceedsMaximum`; `GetRetryDelay` parameter changed; new branch in `SendAsync` |
| DockerUpdateGuard | `Infrastructure/TransientHttpRetryHandlerLogging.cs` | new event 3503 |
| DockerUpdateGuard.Tests | `TransientHttpRetryHandlerTests.cs` | tests of *Tests per criterion* |

## Signatures (for the Dev's skeleton)

In `TransientHttpRetryHandler` (region *Static methods*, *Methods*):

- `private static TimeSpan? GetRetryAfter(HttpResponseMessage? response)` — new
- `private TimeSpan GetRetryDelay(int attempt, TimeSpan? retryAfter)` — changed (was `HttpResponseMessage? response`)
- `private void LogRetryAfterExceedsMaximum(HttpRequestMessage request, string requestTarget, int statusCode, TimeSpan retryAfter)` — new, Debug-guarded

In `TransientHttpRetryHandlerLogging` (region *Methods*):

- `[LoggerMessage(EventId = 3503, Level = LogLevel.Debug, Message = "…")] public static partial void RetryAfterExceedsMaximumNotRetried(this ILogger logger, string method, string requestTarget, int statusCode, long retryAfterMilliseconds, int maximumMilliseconds)` — new, message text as in *Approach*

All members are private or internal; no public API changes, no skeleton files to rewrite beyond these two.

Deviation accepted in step 9: the Dev added `private bool IsNotRetried(HttpRequestMessage request, string requestTarget, HttpResponseMessage response, out TimeSpan? retryAfter)`,
which holds both non-idempotent give-up checks (3502 and 3503) so `SendAsync` stays below S3776; behavior, log
entries and the single `Retry-After` read the criteria rest on are unchanged.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/TransientHttpRetryHandlerTests.cs`

Existing test call sites of a changed signature: none (`GetRetryDelay` is private). The existing
`TransientHttpRetryHandlerClampsRetryAfterToMaximumBackoffAsync` (GET, 503) and the rows of
`TransientHttpRetryHandlerRetriesPostOnTooManyRequestsAsync` stay unchanged and must stay green.

Files no unit test can cover: none.

## Areas

- [Registries](../../docs/areas/registries.md), *Retry of transient HTTP failures*: the 429 rule for non-idempotent
  methods and the new debug entry.
- [Container actions](../../docs/areas/container-actions.md), *Actions*: the Portainer login and action on a 429.

## Documentation updates

All owned by the **Dev** (with the code); the record by the **Lead** (done in step 2).

- `docs/areas/registries.md` line 161: "when positive, capped at 30 seconds;" → "when positive, capped at 30
  seconds (a non-idempotent request with a longer `Retry-After` is not retried, see below);".
- `docs/areas/registries.md` lines 165–166: "so it is retried only on a 429 and after an `HttpRequestException`" →
  "so it is retried only on a 429 whose `Retry-After` asks for at most 30 seconds or is missing, and after an
  `HttpRequestException`"; and after "ends the request after one attempt: the response is returned or the error
  raised." add "A 429 whose `Retry-After` asks for more than 30 seconds is returned after one attempt as well: the
  retry does not resend the request inside the window the server named. The callers may still log in again with their
  next request (see *Known gaps*)."
- `docs/areas/registries.md`, *Known gaps*: new bullet "After a failed login the Docker Hub client logs in again with
  its next authenticated request, and the Portainer client logs in for every capability check and action; neither
  remembers a 429's `Retry-After`. Tracked in [#<n>](https://github.com/LarsLaskowski/DockerUpdateGuard/issues/<n>)."
  The orchestrator opens the follow-up issue (*Out of scope / follow-ups*) before step 6 and gives the Dev its number.
- `docs/areas/registries.md` lines 174–175: append "A 429 that is not retried because its `Retry-After` exceeds 30
  seconds is logged as a debug entry (3503) with the status, the method, the masked URL, the requested wait and the
  30-second maximum."
- `docs/areas/container-actions.md` lines 73–77: "and on 429." → "and on a 429 whose `Retry-After` asks for at most
  30 seconds."; and "a 5xx, 408, timeout or broken connection ends it *failed* after one attempt" → "a 5xx, 408,
  timeout, broken connection or a 429 with a longer `Retry-After` ends it *failed* after one attempt". The
  sentence "A requested action therefore reaches Portainer's processing at most once" stays (a 429 is not processed);
  no statement there may claim the login is not repeated by the next action.
- `README.md` line 156 (`RetryCount` row): "and on 429" → "and on a 429 whose `Retry-After` asks for at most 30
  seconds".
- `docs/decisions/0008-…md`: extended in place by the Lead (Options 7, Decision, Consequences, Source).

Sweep (`grep -n -i "retry\|429\|Retry-After"` over `docs/areas`, `docs/ARCHITECTURE.md`, `README.md`, `INSTALL.md`,
`docs/*.md`): the hits above are the only statements of the 429 rule; `docs/ARCHITECTURE.md:241–245` only points to
Registries and stays; `docs/areas/telemetry.md:107` (retry log lines carry the masked URL) stays true; the event-id
list exists only in `registries.md:175`.

## Architecture check

No guarantee of `.squad/project.md` is touched: the integration clients still never throw for the 429 (they get the
response and map it to *failed* as today, fact 6); no layering, composition-root or wiring change.
`docs/ARCHITECTURE.md` needs no update.

## Security considerations

- [ ] No limits on parsed input: the `Retry-After` value is parsed by the runtime header parser; the new comparison
  uses its `TimeSpan`, and the logged value is a `long`, so no overflow for a far date.
- [ ] Exceptions reaching a user-visible message: none new — the 429 is returned, not thrown; the Portainer login's
  existing `PortainerAuthenticationException` text "failed with HTTP 429 for '<masked base URL>'" is unchanged.
- [ ] The new log entry carries the method, the masked URL without query (as 3500–3502), a status number and two
  durations; no header value, body or credential.

## Challenge

1. **Major — the "never resent inside the window" wording is false, because the callers log in again on their own.**
   Accepted; claims checked in the code (fact 11). Decision: narrow the wording, and split the caller path into a
   follow-up issue rather than bringing it into scope. Reason: a caller-level login back-off is a different design —
   Docker Hub client instances live per scan and per UI operation (`docs/areas/registries.md:145–148`), so honoring a
   `Retry-After` across them needs shared state with its own lifetime, and the Portainer client would need a login
   cache it does not have; neither belongs in the retry handler's fix. Changed: the area-document, `container-actions.md`
   and `README.md` texts in *Documentation updates* now say the retry handler does not resend the request; a *Known
   gaps* bullet in `registries.md` names the caller path with the follow-up issue; record 0008 (option 7, Decision,
   Consequences) says the same; the follow-up is listed under *Out of scope / follow-ups*.
2. **Minor — fact 3 and 0008's Context hold only when `RequestTimeoutSeconds` exceeds 30 s.** Accepted: fact 3,
   *Problem / root cause* and 0008's Context are qualified; under the defaults the visible change is the caller getting
   the 429 at once instead of a timeout. Tests unchanged.
3. **Minor — a Portainer `POST` with 429 and `Retry-After` of 16–30 s is still retried and ends in the 15 s timeout.**
   Accepted: one sentence in 0008's Consequences and in *Out of scope / follow-ups*; it is not a regression.

## Security review

Security approved (tier `standard` stays) and confirmed fact 8 by measurement. The one note that left a choice — a
`Retry-After` that is present but unparseable (beyond `Int32` seconds, negative, signed, fractional, garbage) still lets
a non-idempotent 429 be retried after the normal backoff:

- **Option taken: no change and no follow-up issue.** An unparseable value names no window the handler could honor,
  so the request is treated like a 429 without `Retry-After`, which 0008 retries on purpose; RFC 9110 lets a recipient
  ignore an invalid field value. Reading `Headers.NonValidated` to refuse the retry would treat a server's malformed
  header as a long window by guess, and the only realistic long form (a delta above about 68 years) is not a case
  worth a guard. Recorded under *Out of scope / follow-ups* and in 0008's Consequences.

## Decision records

- `docs/decisions/0008-non-idempotent-requests-retry-only-when-not-processed.md` (Accepted, unreleased) — extended in
  place: Source adds #316, option 7 added, the Decision names the `Retry-After` limit, a consequence added. Index row
  unchanged (title and status stay).

## Out of scope / follow-ups

- Idempotent requests keep the clamped wait (option 3).
- A `Retry-After` that is present but unparseable (fact 8: beyond `Int32` seconds, negative, signed, fractional) is
  treated as absent by the runtime and retried with the exponential backoff, for every method (*Security review*).
- A Portainer `POST` answered with 429 and a `Retry-After` of 16–30 s is still retried by the handler and, with the
  default `RequestTimeoutSeconds` of 15, ends in the timeout rather than the 429 — the behavior of today, not a
  regression of this change.
- Follow-up issue (*Challenge* 1): "[Docker] Do not log in again before a login's Retry-After has passed" — body:
  "After a failed login the Docker Hub client sends `v2/users/login` again with every later authenticated request of
  the same client instance (`GetAccessTokenAsync` caches no failure and no `Retry-After`), and the Portainer client
  posts `/api/auth` for every capability check and action (`CreateAuthenticatedClientAsync`, no JWT cache). A server
  that answered a login with 429 and a `Retry-After` therefore still gets logins inside its window, from the callers
  rather than from the retry handler (#316). Remember a login's 429 with its `Retry-After` and fail fast until it has
  passed, in a place that outlives a single client instance. Found while planning #316."

- Related defect found on the way: `GetRetryDelay` reads `DateTimeOffset.UtcNow` directly
  (`TransientHttpRetryHandler.cs:231`) instead of an injected `TimeProvider`, against `docs/UNIT_TESTS.md`; the
  date tests rely on margins (AC2 uses a one-hour margin). Proposed follow-up issue: "[Docker] Read the Retry-After
  date against an injected TimeProvider in TransientHttpRetryHandler" — body: "`GetRetryDelay` computes the wait for
  a `Retry-After` date from `DateTimeOffset.UtcNow`, so its tests depend on the real clock with tolerance margins.
  Inject `TimeProvider` (default `TimeProvider.System`) through the internal constructor and use it there. Found
  while planning #316."
