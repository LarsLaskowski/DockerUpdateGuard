# Plan: TransientHttpRetryHandler retries non-idempotent POSTs, so a Portainer action can run twice

Source: Issue #273
Status: Draft
Tier: standard — the retry decision of `TransientHttpRetryHandler` changes behavior in two security areas, but the retry rule is no guard against an attacker (no authentication, secret handling, allow-list or exposure changes), so it creates, changes or removes no security control.
Security areas touched: Registries and Docker Hub calls (`TransientHttpRetryHandler`, the Docker Hub login through it); Container actions (`PortainerClient` login and action requests through it)

## Problem / root cause

`src/DockerUpdateGuard/Infrastructure/TransientHttpRetryHandler.cs` decides retries without looking at the method:

- line 204: `if (IsTransientStatusCode(response.StatusCode) == false || attempt >= retryCount)` — every method is
  retried on 408, 429 and every 5xx (`IsTransientStatusCode`, lines 86–91);
- lines 222–224: the catch filter retries any `HttpRequestException` or `TimeoutException` (`IsTransientException`,
  lines 98–101), including errors raised after the request was written;
- lines 189–192: every request body is buffered so it can be resent.

So the `POST`s behind the handler — Portainer login (`PortainerClient.cs:170`), Portainer container action
(`PortainerClient.cs:352`) and Docker Hub login (`DockerHubClient.cs:1031`) — are resent after a 5xx/408 or a broken
connection, although the server may already have acted: one `restart` can run up to `RetryCount + 1` times.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | The handler retries every method on 408, 429, every 5xx, any `HttpRequestException` and `TimeoutException`, and buffers every body | `TransientHttpRetryHandler.cs:86-101`, `:189-192`, `:204`, `:222-224` (the issue's line numbers 181–237/186–189/201/219 are a few lines off; substance confirmed) | confirmed |
| 2 | The handler is attached to the Docker Hub, OCI, .NET and nginx release-feed and Portainer clients | `ServiceCollectionExtensions.cs:72-101` (`AddHttpMessageHandler<TransientHttpRetryHandler>()` five times) | confirmed |
| 3 | The non-idempotent requests behind the handler are exactly three `POST`s: Portainer login, Portainer action, Docker Hub login; no `PATCH`, `PUT` or `DELETE` exists | `grep HttpMethod\.\|PostAsync\|PostAsJson` over `src/DockerUpdateGuard`: `PortainerClient.cs:170` (`PostAsJsonAsync("/api/auth")`), `:352` (`PostAsync(actionUrl, content: null)`), `DockerHubClient.cs:1031` (`HttpMethod.Post, "v2/users/login"`); all others `HttpMethod.Get` (the issue's lines 154/336/969 are off) | confirmed |
| 4 | The Docker Scout login does not use the handler | `DockerScoutVulnerabilityProvider.cs:421` uses `_httpClientFactory.CreateClient()` (default client, no handler); login at `:226` | confirmed |
| 5 | No client behind the handler sets an HTTP version or a primary handler, so all run HTTP/1.1 on the factory's default `SocketsHttpHandler` | `grep DefaultRequestVersion\|HttpVersion\|ConfigurePrimaryHttpMessageHandler` in `src/DockerUpdateGuard`: no hit (the only `SocketsHttpHandler`s are the Docker Engine client's, which has no retry handler) | confirmed |
| 6 | `SocketsHttpHandler` sets `ConnectionError`, `NameResolutionError`, `SecureConnectionError`, `ProxyTunnelError` only for failures before any request byte reaches the origin; an accepted-then-closed or reset connection gives `Unknown`, a server that read the request and closed gives `ResponseEnded`, a bad status line `InvalidResponse` | Lead's scratch measurement (.NET SDK 10.0.402, POST through `SocketsHttpHandler` against a `TcpListener`): refused port → `ConnectionError`; `nonexistent.invalid` → `NameResolutionError`; `https` to a plain server → `SecureConnectionError`; proxy answers `CONNECT` with 502 → `ProxyTunnelError`; tunnel established then closed → `SecureConnectionError`; accept then close/reset before reading, after reading headers, after reading the body with reset → `Unknown`; body read then close → `ResponseEnded`; partial response → `ResponseEnded`; garbage status line → `InvalidResponse` | confirmed |
| 7 | The test double `RetryProbeHttpMessageHandler` throws `new HttpRequestException(message)` for a `null` outcome, whose `HttpRequestError` is `Unknown`; it cannot throw another error or `TimeoutException`, nor record bodies | `src/Tests/DockerUpdateGuard.Tests/Helper/RetryProbeHttpMessageHandler.cs:62-65` | confirmed |
| 8 | Only one existing test sends a non-GET through the handler: `TransientHttpRetryHandlerBuffersAndRetriesRequestWithContentAsync` (POST `/api/auth`, 503 then 200) — it fails under the new rule; every other test uses GET | `TransientHttpRetryHandlerTests.cs:407-424` and `:70`; no other test file uses the handler except a registration check in `ServiceCollectionExtensionsTests.cs:57` | confirmed |
| 9 | `PortainerClientTests` builds its `HttpClient` over `SequenceHttpMessageHandler` without the retry handler; that handler answers 404 when a URL's queue is empty and records every request | `PortainerClientTests.cs:704-748`, `:1201-1219`; `Helper/SequenceHttpMessageHandler.cs` `SendAsync` | confirmed |
| 10 | `TestLogger` records every level, Debug included | `TestLogger.cs` `IsEnabled` returns `true` | confirmed |
| 11 | The behavior is documented as "every method is retried" and as a known gap in both area documents | `docs/areas/registries.md:164` and *Known gaps* entry 2 (`:203-205`); `docs/areas/container-actions.md:72-73` and *Known gaps* entry 2 (`:103-105`) (the issue's line numbers and "*Open questions* item 3" are off) | confirmed |
| 12 | `docs/ARCHITECTURE.md` describes the retry policy | `ARCHITECTURE.md:241-245` only says where the handler is attached and links Registries for the policy; nothing there becomes untrue | refuted |
| 13 | Today no component calls `PortainerClient.ExecuteActionAsync`, so the double execution is latent until #280 | `docs/areas/container-actions.md:85-86`; `grep ExecuteActionAsync src/DockerUpdateGuard` outside `Portainer/` finds no caller | confirmed |
| 14 | `README.md` describes the retry policy in the `RetryCount` row | `README.md:156` | confirmed |

## Acceptance criteria

- [ ] AC1: A `POST` that answers 500, 502, 503, 504 or 408 is sent exactly once and that response is returned (status unchanged), with `RetryCount` 2; no retry warning (EventId 3500) is logged.
- [ ] AC2: A `POST` whose send throws `HttpRequestException` with `HttpRequestError` `ConnectionError`, `NameResolutionError`, `SecureConnectionError` or `ProxyTunnelError` is retried up to `RetryCount` times: with `RetryCount` 2 and two such failures then 200 it is sent 3 times and returns 200; with `RetryCount` 1 and two failures the second exception is raised after 2 attempts.
- [ ] AC3: A `POST` whose send throws `HttpRequestException` with `HttpRequestError` `ResponseEnded`, `InvalidResponse`, `HttpProtocolError` or `Unknown` (also one built without an error), or a `TimeoutException`, is sent exactly once and that exception reaches the caller.
- [ ] AC4: A `POST` answered 429 is retried as before: with `Retry-After: 7` (delta) the wait is 7 s, without it the exponential backoff (500 ms for the first retry, no jitter); 429 then 200 returns 200 after 2 attempts.
- [ ] AC5: `PATCH` and an unknown method (`new HttpMethod("LOCK")`) behave like `POST` (sent once on 503 and on `ResponseEnded`, retried on `ConnectionError`); `GET`, `HEAD`, `OPTIONS`, `TRACE`, `PUT` and `DELETE` are retried on 503 and on `ResponseEnded` as before; all existing tests except the one rewritten in AC6 pass unchanged.
- [ ] AC6: A `POST` with a body retried after a `ConnectionError` is resent with the identical body.
- [ ] AC7: With `RetryCount` 0 the request body is not buffered by the handler; with `RetryCount` above 0 a body is buffered as today.
- [ ] AC8: When a non-idempotent request gets a transient outcome that an idempotent request would have retried (attempts left, not cancelled) but the rule forbids it, one Debug entry, EventId 3502, is logged: `Transient HTTP outcome {Outcome} for {Method} {RequestTarget} not retried: the method is not idempotent`, with `{Outcome}` the status code number (`503`) or the `HttpRequestError` name (`ResponseEnded`) or `Timeout`, and `{RequestTarget}` from `GetRequestTarget` (no user info, no query). No entry when the retries are already used up or `RetryCount` is 0.
- [ ] AC9: A Portainer action whose action request answers 503 (with a 200 queued behind it) is sent exactly once through the real `TransientHttpRetryHandler` and reported failed with a message containing `HTTP 503`.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `TransientHttpRetryHandlerDoesNotRetryPostOnServerErrorAsync` (`[DataRow]` 500, 502, 503, 504, 408) | `src/Tests/DockerUpdateGuard.Tests/TransientHttpRetryHandlerTests.cs` |
| AC2 | `TransientHttpRetryHandlerRetriesPostOnConnectionErrorAsync` (`[DataRow]` the four errors); `TransientHttpRetryHandlerRethrowsPostConnectionErrorWhenRetriesExhaustedAsync` | same |
| AC3 | `TransientHttpRetryHandlerDoesNotRetryPostOnResponseEndedAsync` (`[DataRow]` `ResponseEnded`, `InvalidResponse`, `HttpProtocolError`, `Unknown`); `TransientHttpRetryHandlerDoesNotRetryPostOnPlainHttpRequestExceptionAsync`; `TransientHttpRetryHandlerDoesNotRetryPostOnTimeoutAsync` | same |
| AC4 | `TransientHttpRetryHandlerRetriesPostOnTooManyRequestsAsync` (`[DataRow]` with and without `Retry-After`) | same |
| AC5 | `TransientHttpRetryHandlerTreatsNonIdempotentMethodLikePostAsync` (`[DataRow]` `PATCH`, `LOCK`); `TransientHttpRetryHandlerRetriesIdempotentMethodOnServerErrorAndResponseEndedAsync` (`[DataRow]` the six methods; 503, then `ResponseEnded`, then 200 → 3 attempts) | same |
| AC6 | `TransientHttpRetryHandlerBuffersAndRetriesRequestWithContentAsync` (rewritten: POST with body, `ConnectionError` then 200, both recorded bodies equal the sent body) | same |
| AC7 | `TransientHttpRetryHandlerWithZeroRetryCountDoesNotBufferContentAsync`; `TransientHttpRetryHandlerWithRetryCountBuffersContentAsync` | same |
| AC8 | `TransientHttpRetryHandlerLogsDebugWhenPostIsNotRetriedOnServerErrorAsync` (POST to `https://user:secret@…/path?token=x`, 503: one 3502 entry at Debug containing `POST`, `503`, the masked target, not `secret`/`token=x`; no 3500 entry); `TransientHttpRetryHandlerLogsDebugWhenPostIsNotRetriedOnResponseEndedAsync` (outcome `ResponseEnded`, no 3501 entry); `TransientHttpRetryHandlerWithZeroRetryCountDoesNotLogNotRetriedAsync` | same |
| AC9 | `PortainerClientExecuteActionAsyncWhenActionRequestAnswersServiceUnavailableExecutesOnceAndFailsAsync` | `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs` |

All of AC1–AC9 fail on the current code (AC1, AC3, AC5-non-idempotent, AC9 by extra attempts; AC2 and AC6 only once the probe can throw a typed error — AC2 passes today for the retry count, so it may be *deferred* to step 6 if it cannot fail; AC7 and AC8 by buffering and the missing entry).

## Approach

In `TransientHttpRetryHandler.SendAsync`:

1. Read `retryCount` first; buffer `request.Content` only when it is not null **and** `retryCount > 0` (with 0 no retry
   is possible). Compute `var isIdempotent = IsIdempotentMethod(request.Method);` once.
2. Response path: after `if (IsTransientStatusCode(...) == false || attempt >= retryCount) return response;` add
   `if (isIdempotent == false && IsNotProcessedStatusCode(response.StatusCode) == false)` → log
   `NonIdempotentRequestNotRetried(method, requestTarget, ((int)statusCode).ToString(CultureInfo.InvariantCulture))`
   and return the response undisposed.
3. Exception path: keep the catch filter as is (`IsTransientException && !cancelled && attempt < retryCount`); at the
   top of the catch body, `if (isIdempotent == false && IsRaisedBeforeSending(exception) == false)` → log
   `NonIdempotentRequestNotRetried(method, requestTarget, GetExceptionOutcome(exception))` and `throw;` (stack kept).
   Logging stays out of the exception filter.
4. `IsIdempotentMethod`: `GET`, `HEAD`, `OPTIONS`, `TRACE`, `PUT`, `DELETE` by `HttpMethod` equality; any other
   method (`POST`, `PATCH`, `CONNECT`, custom) is non-idempotent. `IsNotProcessedStatusCode`: 429 only.
   `IsRaisedBeforeSending`: `exception is HttpRequestException { HttpRequestError: NameResolutionError or
   ConnectionError or SecureConnectionError or ProxyTunnelError }` — only the top-level exception, never an inner one.
   `GetExceptionOutcome`: the `HttpRequestError` name for an `HttpRequestException`, `Timeout` for a
   `TimeoutException`.
5. Backoff, jitter, `Retry-After`, `RetryCount`, the warnings 3500/3501 (wording unchanged) and the total timeouts stay
   as they are. The debug entry carries no exception and no exception message.

Test helpers (Tester's, `Helper/`): give `RetryProbeHttpMessageHandler` a settable failure factory (default: today's
`new HttpRequestException("Simulated transient transport failure")`, so existing tests are unchanged) and a list of the
request bodies it received; add a counting `HttpContent` (`CountingHttpContent`, counts `SerializeToStreamAsync`
calls) for AC7. The Portainer test builds `new HttpClient(new TransientHttpRetryHandler(options, logger, no-op delay,
_ => 0) { InnerHandler = sequenceHandler })` with `RetryCount` 2.

No new user-visible output besides the debug log line fixed in AC8.

## Options considered

1. **Method-based rule inside the handler** (chosen) — one place, covers future `POST`s; see record 0008.
2. **Per-request opt-out through `HttpRequestOptions`** — lost: a new `POST` that forgets it is unsafe by default.
3. **Remove the handler from the Portainer client** — lost: drops Portainer's safe `GET` retries, leaves the Docker Hub
   login unprotected.
4. **Also retry `HttpRequestError.Unknown`** — lost: the measurement (fact 6) gives `Unknown` both for a connection
   closed before the request was read and for one reset after the whole body was read.
5. **Keep buffering unconditional** (the issue's "buffer only when a retry is possible" read as moot because a `POST`
   can be retried on a connection error) — lost to buffering only when `RetryCount > 0`, the one case in which no retry
   of any method is possible; cheap and keeps the issue's intent.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| `DockerUpdateGuard` | `Infrastructure/TransientHttpRetryHandler.cs` | method rule, conditional buffering, debug log call |
| `DockerUpdateGuard` | `Infrastructure/TransientHttpRetryHandlerLogging.cs` | new Debug message 3502 |
| `DockerUpdateGuard.Tests` | `TransientHttpRetryHandlerTests.cs`, `PortainerClientTests.cs`, `Helper/RetryProbeHttpMessageHandler.cs`, `Helper/CountingHttpContent.cs` (new) | tests and helpers (Tester) |

## Signatures (for the Dev's skeleton)

All private or internal; no test calls them, so step 4 has no skeleton to build (the tests compile against the
unchanged constructor and `SendAsync`):

- `private static bool IsIdempotentMethod(HttpMethod method)` (region *Static methods*)
- `private static bool IsNotProcessedStatusCode(HttpStatusCode statusCode)`
- `private static bool IsRaisedBeforeSending(Exception exception)`
- `private static string GetExceptionOutcome(Exception exception)`
- `TransientHttpRetryHandlerLogging`: `[LoggerMessage(EventId = 3502, Level = LogLevel.Debug, Message = "Transient HTTP outcome {Outcome} for {Method} {RequestTarget} not retried: the method is not idempotent")] public static partial void NonIdempotentRequestNotRetried(this ILogger logger, string method, string requestTarget, string outcome);`

## Test files

- `src/Tests/DockerUpdateGuard.Tests/TransientHttpRetryHandlerTests.cs`
- `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs`
- helpers: `src/Tests/DockerUpdateGuard.Tests/Helper/RetryProbeHttpMessageHandler.cs` (extended),
  `src/Tests/DockerUpdateGuard.Tests/Helper/CountingHttpContent.cs` (new)

Existing test code that calls a changed signature: none (no signature changes; the probe's constructor stays). The
existing test `TransientHttpRetryHandlerBuffersAndRetriesRequestWithContentAsync` is rewritten by the Tester (AC6).

Files no unit test can cover: none.

## Areas

- `docs/areas/registries.md`, *Retry of transient HTTP failures*: the method rule replaces "Every method is retried"
  (line 164); the logging bullet gains the Debug entry 3502; the *Known gaps* entry for #273 goes; *Related decisions*
  links 0008.
- `docs/areas/container-actions.md`, *Actions*: the login and the action request are `POST`s and are retried only when
  not processed (connection not established, or 429), so a requested action reaches Portainer's processing at most
  once and a 5xx/408/timeout/broken connection ends it *failed* after one attempt; the *Known gaps* entry for #273 goes.

## Documentation updates

Sweep: `grep -rn -i "retr" docs README.md INSTALL.md SECURITY.md`. Hits that the change extends or makes untrue:

| File | Edit | Owner |
| ---- | ---- | ----- |
| `docs/areas/registries.md:157-167, 203-205, 216-218` | as in *Areas* | Dev |
| `docs/areas/container-actions.md:72-73, 103-105` | as in *Areas* | Dev |
| `README.md:156` (`RetryCount` row) | append: non-idempotent requests (logins, Portainer actions) are retried only after a connection error before sending and on 429 | Dev |
| `docs/decisions/0008-…md`, index row | written (Proposed) | Lead |

Checked and unchanged: `docs/ARCHITECTURE.md:94, 148-150, 241-245, 338` (pointers only, fact 12);
`docs/areas/telemetry.md:107` (retry log lines without user info and query — the new entry keeps it);
`docs/areas/scan-engine.md:110`, `docs/areas/hosting.md:194`, `docs/areas/base-images.md:140`,
`docs/areas/update-detection.md:189`, `docs/areas/vulnerabilities.md:169, 191`, `docs/areas/docker-instances.md:53`
(pointers); `docs/CONTRIBUTING.md:123` (milestone rationale, stays true).

## Architecture check

No guarantee of `.squad/project.md` is weakened. "Integration clients never throw for expected failures" holds:
`PortainerClient.ExecuteActionAsync` catches every exception into a *failed* result, and the Docker Hub login turns a
non-success status into *failed*; an exception the handler now raises earlier (without retry) is of the same kinds it
raised after exhaustion before. Layering unchanged (handler in the host). `ARCHITECTURE.md` stays correct.

## Security considerations

- [x] No limits on parsed input.
- [x] Exceptions reaching a user-visible message: the handler adds none; it now lets `HttpRequestException` /
  `TimeoutException` through after one attempt where it did after the last retry. `PortainerClient` already reports
  `Action '<name>' failed: {ex.Message}` (`PortainerClient.cs:380`; the capability check `Portainer connection error: {ex.Message}`, `:287`), e.g. `Error while copying content to a stream.`
  — runtime text, unchanged here (see *Out of scope*).
- The debug entry logs method, masked target and an outcome token only — never the exception message, body or query.

## Decision records

- `docs/decisions/0008-non-idempotent-requests-retry-only-when-not-processed.md` (Proposed)

## Out of scope / follow-ups

- Related defect, not fixed here: `PortainerClient.ExecuteActionAsync` and the capability check put the raw
  `ex.Message` of a runtime exception into the user-visible result. Proposed follow-up issue: title "[BUG]
  PortainerClient puts raw runtime exception messages into action and capability results"; body: "The catch blocks of
  `PortainerClient` (`ExecuteActionAsync`, capability check) return `ex.Message` verbatim, e.g. `Error while copying
  content to a stream.` or an `HttpRequestException` text that may name hosts. Map transport errors to a fixed
  reason (HTTP status, `connection failed`, `timed out`) before #280 shows these messages in the UI."
- Idempotency keys or a per-request opt-in to retry a `POST` on 5xx (record 0008, *Consequences*).
