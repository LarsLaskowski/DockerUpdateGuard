# Plan: PortainerClient results carry fixed failure reasons instead of raw exception messages

Source: Issue #315
Status: Draft
Tier: security (raised from standard in review round 1: `GetFailureReason` is an output-sanitizing allow-list for what an unauthenticated UI viewer sees; area added: Secrets in configuration) — originally standard: the result messages of the Portainer client change (behavior, error output), but no security control is created, changed or removed: URL masking, credential handling and the allow-list stay as they are.
Security areas touched: Container actions (`PortainerClient` capability check and action results); Secrets in configuration (exception text in Portainer result messages)

## Problem / root cause

Both catch-all blocks of `src/DockerUpdateGuard/Portainer/PortainerClient.cs` put the caught exception's message
into the result as it stands:

- `GetCapabilityAsync`, line 279-289: `Message = $"Portainer connection error: {ex.Message}"` (line 287);
- `ExecuteActionAsync`, line 376-385: `Message = $"Action '{actionRequest.ActionName}' failed: {ex.Message}"` (line 383).

`catch (Exception ex)` receives runtime exceptions from `HttpClient` (`HttpRequestException`, the
`TaskCanceledException` of `HttpClient.Timeout`, cancellation by the caller), `System.Text.Json`'s `JsonException`
from `ReadFromJsonAsync` (lines 90, 123, 174) and the client's own `InvalidOperationException`s for a failed login
(lines 184, 191). Only the last are composed by the application (masked base URL, HTTP status); the others carry
runtime text that names hosts and ports.

Related defects found on the way (not fixed here, follow-up issue proposed below): the same pattern puts
`exception.Message` into `ExternalOperationResult` messages that already reach scan-run errors and the pages in
`DockerInstanceClient.cs` (lines 1041, 1127, 1164, 1201, 1256), `OciRegistryClient.cs:1347`,
`DockerHubClient.cs:1420` and `:1477`, `DotNetReleaseMetadataService.cs:208`, `NginxReleaseMetadataService.cs:165`,
`TrivyVulnerabilityProvider.cs:355` and `DockerScoutVulnerabilityProvider.cs:469`. Also, both Portainer catch blocks
swallow cancellation by the caller and report it as a failure with "A task was canceled." (fixed here: fixed reason
"cancelled").

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | The catch blocks of `ExecuteActionAsync` and the capability check return `ex.Message` verbatim (issue) | `PortainerClient.cs:287` and `:383` | confirmed |
| 2 | A response that breaks off yields `Error while copying content to a stream.` (issue) | Scratch probe (.NET 10, scratchpad, not in the repository): a server that sends a `Content-Length: 100` header and closes early gives `HttpRequestException`, `HttpRequestError.ResponseEnded`, message `Error while copying content to a stream.` | confirmed |
| 3 | An `HttpRequestException` text may name hosts (issue) | Same probe: refused connection gives `HttpRequestException`, `HttpRequestError.ConnectionError`, message `Connection refused (127.0.0.1:1)` | confirmed |
| 4 | `HttpClient.Timeout` surfaces as `TaskCanceledException` with an inner `TimeoutException` while the caller's token is not cancelled | Same probe: `Timeout = 1 s` against a silent listener gives `TaskCanceledException`, message `The request was canceled due to the configured HttpClient.Timeout of 1 seconds elapsing.`, inner `TimeoutException` | confirmed |
| 5 | A pre-cancelled caller token surfaces as `TaskCanceledException` from `GetAsync` | Same probe: `A task was canceled.`; today `PortainerClient` turns it into a failed result with that text | confirmed |
| 6 | Malformed JSON in a Portainer answer surfaces as `JsonException` from `ReadFromJsonAsync` | Same probe: `<html>nope</html>` with `application/json` gives `System.Text.Json.JsonException`, `'<' is an invalid start of a value. Path: $ ...` | confirmed |
| 7 | Nothing shows these messages today; #280 is going to (issue) | `IPortainerClient` is registered (`ServiceCollectionExtensions.cs:104`) and has no caller outside tests (grep); #280 is open and plans to show capability and action messages and store them in action runs | confirmed |
| 8 | Non-success HTTP answers already produce a fixed "HTTP <code>" reason (issue: "HTTP status") | `PortainerClient.cs:249`, `:373`; login `:191` | confirmed |
| 9 | The login failure messages are composed by the client and carry only the masked base URL and the HTTP status | `PortainerClient.cs:168`, `:184`, `:191`; record 0005; tests `PortainerClientGetCapabilityAsyncWithUserInfoAndFailedLoginMasksUserInfoAsync`, `...MissingJwtMasksUserInfoAsync` | confirmed |
| 10 | `RequestTimeoutSeconds` defaults to 15 | `PortainerOptions.cs:48` | confirmed |
| 11 | `RetryProbeHttpMessageHandler` can throw an arbitrary exception on the first request (`[null]` outcome and `FailureFactory`) | `src/Tests/DockerUpdateGuard.Tests/Helper/RetryProbeHttpMessageHandler.cs:25-55`, `:86` | confirmed |
| 12 | With `ApiToken` and `EndpointId` set, the first request of an action is the container list `GET`, of a capability check the status `GET` | `PortainerClient.cs:158-163`, `:239`, `:322-338` | confirmed |

## Acceptance criteria

The texts below are the exact `Message` values the tests compare (`<action>` is the requested action name, `restart`
in the tests; `<n>` the instance's `Portainer:RequestTimeoutSeconds`).

- [ ] AC1: When an action fails with an `HttpRequestException` (any `HttpRequestError`), the result is failed with
  the message `Action '<action>' failed: connection failed`; the exception's message (host, port, runtime text) does
  not appear in it, and the failure is still logged (EventId 3310) with the exception.
- [ ] AC2: When a capability check fails with an `HttpRequestException`, the result is configured, without actions,
  with the message `Portainer connection error: connection failed`; the exception's message does not appear in it,
  and the failure is still logged (EventId 3306) with the exception.
- [ ] AC3: When a request times out (an `OperationCanceledException` or `TimeoutException` while the caller's token is
  not cancelled), the action message is `Action '<action>' failed: timed out after <n> seconds` and the capability
  message `Portainer connection error: timed out after <n> seconds`.
- [ ] AC4: When the caller's token is cancelled, neither method throws; the action message is
  `Action '<action>' failed: cancelled` and the capability message `Portainer connection error: cancelled`.
- [ ] AC5: When a Portainer answer is not valid JSON (`JsonException`), the action message is
  `Action '<action>' failed: invalid response` (the parser's text does not appear).
- [ ] AC6: Any other exception yields `Action '<action>' failed: unexpected error` /
  `Portainer connection error: unexpected error`; the exception's message does not appear.
- [ ] AC7: A failed login (non-success answer, or success without JWT) keeps its composed message unchanged in both
  results: `Portainer authentication failed with HTTP <code> for '<masked base URL>'` and
  `Portainer authentication succeeded but returned no JWT token for '<masked base URL>'`.

## Tests per criterion

All in `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs`. Exceptions are thrown by
`RetryProbeHttpMessageHandler` (outcomes `[null]`, `FailureFactory`), with exception types and messages as the
probe measured them (*Verified facts* 2-6); the options use `ApiToken` and `EndpointId = "7"`. Each test asserts the
exact `Message` with `Assert.AreEqual` and, where an exception message was injected, `Assert.DoesNotContain` of a
host marker (`portainer.internal.test`) in it.

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `PortainerClientExecuteActionAsyncWhenTransportFailsReportsConnectionFailedAsync` (`[DataRow]`: `ConnectionError` with `Connection refused (portainer.internal.test:9443)`; `ResponseEnded` with `Error while copying content to a stream. portainer.internal.test`); also asserts the 3310 entry carries the exception | `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs` |
| AC2 | `PortainerClientGetCapabilityAsyncWhenTransportFailsReportsConnectionFailedAsync` (`ConnectionError` with `Connection refused (portainer.internal.test:9443)`); also asserts the 3306 entry carries the exception | `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs` |
| AC3 | `PortainerClientExecuteActionAsyncWhenRequestTimesOutReportsTimedOutAsync`, `PortainerClientGetCapabilityAsyncWhenRequestTimesOutReportsTimedOutAsync` (`new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 15 seconds elapsing.", new TimeoutException())`, default timeout 15) | `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs` |
| AC4 | `PortainerClientExecuteActionAsyncWhenCallerCancelsReportsCancelledAsync`, `PortainerClientGetCapabilityAsyncWhenCallerCancelsReportsCancelledAsync` (a `CancellationTokenSource` linked to `TestContext.CancellationToken`, cancelled before the call; `RetryProbeHttpMessageHandler`, outcome `[null]`, `FailureFactory` throwing `TaskCanceledException` with that token) | `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs` |
| AC5 | `PortainerClientExecuteActionAsyncWhenResponseIsNotJsonReportsInvalidResponseAsync` (`SequenceHttpMessageHandler.AddJsonResponse(ContainersUrl, "<html>nope</html>")`) | `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs` |
| AC6 | `PortainerClientExecuteActionAsyncWhenUnexpectedExceptionReportsUnexpectedErrorAsync`, `PortainerClientGetCapabilityAsyncWhenUnexpectedExceptionReportsUnexpectedErrorAsync` (`new InvalidOperationException("Raw runtime text for portainer.internal.test")` — the type the old login path used, so a mapping by `InvalidOperationException` fails the test) | `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs` |
| AC7 | existing, unchanged, must stay green: `PortainerClientExecuteActionAsyncWhenLoginFailsFailsAndLogsAuthFailedAsync`, `PortainerClientExecuteActionAsyncWhenLoginReturnsNoJwtFailsAndLogsAuthFailedAsync`, `PortainerClientGetCapabilityAsyncWithUserInfoAndFailedLoginMasksUserInfoAsync`, `PortainerClientGetCapabilityAsyncWithUserInfoAndMissingJwtMasksUserInfoAsync`, `PortainerClientGetCapabilityAsyncWhenLoginFailsReturnsUnsupportedResultAndLogsExceptionAsync` — a pin of existing behavior, not written in step 5 | `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs` |

Expected to fail on today's code: AC1, AC2, AC3, AC4, AC5, AC6 (today the message ends in the exception's text).

## Approach

1. New exception type `PortainerAuthenticationException` (file of its own, *Signatures*) for the two login failures:
   `CreateAuthenticatedClientAsync` throws it instead of `InvalidOperationException` at lines 184 and 191, with the
   message texts unchanged.
2. New private static `GetFailureReason` in `PortainerClient` that maps the caught exception, first match wins:
   1. `PortainerAuthenticationException` → its `Message`;
   2. `OperationCanceledException` while `cancellationToken.IsCancellationRequested` → `cancelled`;
   3. `OperationCanceledException` or `TimeoutException` → `timed out after <n> seconds`
      (`n` = `instanceOptions.Portainer.RequestTimeoutSeconds`, invariant culture);
   4. `HttpRequestException` → `connection failed`;
   5. `JsonException` → `invalid response`;
   6. anything else → `unexpected error`.
3. Both catch blocks keep their log call with the exception (3306, 3310) unchanged and build the message from
   `GetFailureReason` with the existing prefixes `Portainer connection error: ` and `Action '<action>' failed: `.
   Every message composed inside the `try` (HTTP status, not found, no endpoint) stays as it is.

No new user-visible layout: the result is a single-line message as today.

## Options considered

1. **Fixed reasons by exception type, auth failure marked by its own exception type** — chosen
   (record 0009).
2. **Sanitize `ex.Message` (strip hosts, URLs, ports)** — lost: a deny-list over runtime text that can be reworded
   with any .NET release; one missed form leaks again.
3. **Pass the message of selected exception types** — lost: the most common failure (connection refused) names the
   host.
4. **Auth failure as a client-or-reason return value instead of an exception** — lost: reworks the login flow and the
   tests that assert the logged exception (3306) of a failed login, beyond the bug.
5. **Keep `InvalidOperationException` and pass its message** — lost: the runtime throws `InvalidOperationException`
   too (for example from `HttpClient` on an invalid request), so its text would leak again.
6. **Rethrow caller cancellation** — lost: breaks the documented "actions never throw to the caller"; #280 decides
   how a cancelled action is recorded.
7. **A reason per `HttpRequestError` (name resolution, TLS, response ended)** — lost: the issue asks for one fixed
   "connection failed"; the distinction is in the logged exception.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `src/DockerUpdateGuard/Portainer/PortainerAuthenticationException.cs` | new: exception for the client's composed login failures |
| DockerUpdateGuard | `src/DockerUpdateGuard/Portainer/PortainerClient.cs` | throw the new exception at the two login failures; new `GetFailureReason`; both catch blocks use it |
| DockerUpdateGuard.Tests | `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs` | new tests of *Tests per criterion* |

## Signatures (for the Dev's skeleton)

In `src/DockerUpdateGuard/Portainer/PortainerAuthenticationException.cs`, namespace `DockerUpdateGuard.Portainer`,
`#region Constructors`, the three standard constructors (Sonar S4027 / CA1032):

```csharp
public sealed class PortainerAuthenticationException : Exception
{
    public PortainerAuthenticationException()
    public PortainerAuthenticationException(string message)
    public PortainerAuthenticationException(string message, Exception innerException)
}
```

In `PortainerClient`, region `Static methods` (private static):

```csharp
private static string GetFailureReason(Exception exception,
                                       int requestTimeoutSeconds,
                                       CancellationToken cancellationToken)
```

`PortainerClient.cs` is not final: the skeleton adds `GetFailureReason`; the two throw sites and the catch blocks are
the Dev's step-6 edits.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs` (existing).

Existing test code that calls a changed signature: none (no existing signature changes; the existing login tests
assert the message and the logged exception's message, not its type).

Files of the change no unit test can cover: none. The parameterless and the inner-exception constructor of
`PortainerAuthenticationException` are not used by production code; if the *Coverage gate* reports them, the
Tester's coverage pass adds `src/Tests/DockerUpdateGuard.Tests/PortainerAuthenticationExceptionTests.cs`.

## Areas

- `docs/areas/container-actions.md` — the capability check and the action results name a fixed reason instead of
  the error text; *Limits and errors* lists the reasons.

## Documentation updates

All owned by the **Dev**, in step 6. Sweep: `grep -rn -i "error text\|exception message\|connection error"` over
`docs/areas`, `docs/ARCHITECTURE.md` and `README.md`; the hits outside Portainer (`docker-instances.md:172`,
`vulnerabilities.md:164`, `README.md:156`) describe other clients or retries and stay.

- `docs/areas/container-actions.md`, *Capability check*: replace "any error (including authentication failure) →
  configured, no actions, message with the error text." with "any error → configured, no actions, message
  `Portainer connection error: ` with the reason (see *Limits and errors*)."
- `docs/areas/container-actions.md`, *Actions*: replace "HTTP status code of a non-success answer, or the error
  text." with "HTTP status code of a non-success answer, or the reason of an error (see *Limits and errors*)."
- `docs/areas/container-actions.md`, *Limits and errors*: add a bullet: an error during a capability check or an
  action is reported with one fixed reason, never with the error's own text — the authentication error as described
  in *Authentication*; `cancelled` when the caller cancelled; `timed out after <n> seconds` (`RequestTimeoutSeconds`);
  `connection failed` for any transport failure; `invalid response` for an answer that is not the expected JSON;
  `unexpected error` otherwise. The error itself is logged with the failure (EventIds 3306, 3310).
- `docs/areas/container-actions.md`, *Related decisions*: add the link to record 0009.
- `docs/ARCHITECTURE.md`: no change (the client table line 229 and *Security posture* make no statement about
  result messages).

## Architecture check

- *Integration clients never throw for expected failures*: preserved; caller cancellation is reported, not thrown,
  as today. Results stay `PortainerActionResult` / `PortainerCapabilityData` (moving them to
  `ExternalOperationResult<T>` is #280's scope).
- Record 0005 (masked URLs): preserved; the only text passed on is the client's composed login message with the
  masked base URL.
- Layering unchanged; no new dependency, configuration or entity.

## Security considerations

- [x] No limits: no parsed input gains or loses a limit.
- [x] Every exception that can reach a user-visible failure reason: `PortainerAuthenticationException` → its composed
  message (masked base URL, HTTP status); `OperationCanceledException` (caller) → `cancelled`;
  `OperationCanceledException` (timeout) / `TimeoutException` → `timed out after <n> seconds`;
  `HttpRequestException` → `connection failed`; `JsonException` → `invalid response`; every other exception →
  `unexpected error`. No raw runtime exception message reaches a result.
- Logs are unchanged: 3306 and 3310 keep the exception (with the runtime text naming the host), as today; the logged
  exception can name the would-be host parsed from a malformed user info (`admin#secret@` -> `admin:443`) or the proxy
  URL. The change closes the result-message leak of such a `#`/`?` user-info fragment and of the proxy URL.

## Decision records

- `docs/decisions/0009-portainer-results-carry-fixed-failure-reasons.md` (Proposed), index row added.

## Out of scope / follow-ups

Follow-up issue for the orchestrator to create:

- **Title:** `[BUG] Integration clients put raw runtime exception messages into ExternalOperationResult messages`
- **Body:** Like #315 for Portainer, the catch-all blocks of other integration clients put `exception.Message`
  verbatim into `ExternalOperationResult.Failed(...)`, which reaches scan-run errors and the pages: runtime texts such
  as `Connection refused (<host>:<port>)` or JSON parser positions. Sites: `DockerInstanceClient.cs` lines 1041, 1127,
  1164, 1201, 1256; `OciRegistryClient.cs:1347`; `DockerHubClient.cs:1420`, `:1477`;
  `DotNetReleaseMetadataService.cs:208`; `NginxReleaseMetadataService.cs:165`; `TrivyVulnerabilityProvider.cs:355`;
  `DockerScoutVulnerabilityProvider.cs:469`; and the orchestrators that add `exception.Message` to status messages
  (`RuntimeContainerScanOrchestrator.cs:517`, `:673`, `:681`, `VulnerabilityEnrichmentService.cs:489`,
  `ImageScanOrchestrator.cs:763`). Map transport errors to fixed reasons as record 0009 does for Portainer, keeping
  the exception in the log. Also, the Portainer log entries 3306 and 3310 keep the exception, whose message can name
  the would-be host parsed from a malformed user info (`admin#secret@` -> `admin:443`) or the proxy URL. Found while
  fixing #315.

## Challenge notes

Devil's Advocate, round 1 (0 major, 1 minor), verbatim:

1. `minor` (the Tester can fix this in the test setup without changing the plan): the AC4 capability test as specified cannot pass, either before or after the fix.
   - Why: the plan uses `SequenceHttpMessageHandler` with a caller token cancelled before the call and nothing injected. That handler ignores the token: it returns a canned response or 404 (`src/Tests/DockerUpdateGuard.Tests/Helper/SequenceHttpMessageHandler.cs:95-120`).
   - Probe: `HttpClient.GetAsync` with an already-cancelled token, through a handler that ignores the token, returns `200 OK` and does not throw. Verified fact 5 holds only for the real `SocketsHttpHandler`.
   - Effect: with `ApiToken` and `EndpointId = "7"` set, the capability path never reads a body (`PortainerClient.cs:239-276`). The result would be "Portainer connected, endpoint 7" or "HTTP 404", never `cancelled`. The action-side test may only throw by chance, from `ReadFromJsonAsync` at `:123`.
   - Instruction for the Tester: write both AC4 tests with `RetryProbeHttpMessageHandler`, outcome `[null]`, and `FailureFactory = () => new TaskCanceledException("A task was canceled.", null, cts.Token)`. Cancel `cts` before the call and pass `cts.Token` to the method, so the cancellation happens in a request on purpose rather than depending on the handler.

## Security notes

Security, plan review round 1 (APPROVED), non-blocking findings verbatim:

1. Non-blocking: one statement in the plan is wrong. *Security considerations*, last bullet, says the 3306/3310 log entries "never contained credentials, since `HttpClient` exception messages carry host and port only". The probe shows otherwise. For a `BaseUrl` such as `https://admin#secret@host` the parser takes the user name as the host, and the logged exception says `(admin:443)`. With a proxy configured, the message names the proxy URL. This change leaves the logs as they are, so it is not a defect it introduces. But record 0005 says secrets must never be logged, and this plan should not contain a false security claim. Fix: reword the bullet to "logs unchanged; the logged exception can name the would-be host parsed from a malformed user info (`admin#secret@` -> `admin:443`) or the proxy URL". Add that log path to the proposed follow-up issue, which today only lists result messages.
2. Non-blocking: *Security considerations* should also say that the change closes the result-message leak of a `#`/`?` user-info fragment and of the proxy URL. That way the Reviewer and the PR see the security effect, not only "hosts and ports".
