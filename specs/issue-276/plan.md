# Plan: Portainer AllowInsecureHttp logs a runtime warning while plaintext HTTP is in use

Source: Issue #276
Status: Draft
Tier: standard — adds a runtime log warning to `PortainerClient`, a behavior change that creates, changes or removes no security control (no guard, credential handling or transport changes; validation stays as is).
Security areas touched: TLS and transport relaxations (`AllowInsecureHttp`); Container actions (`PortainerClient`); Secrets in configuration and Logging and telemetry of external data (a new log message next to the Portainer credentials, which must not carry them)

## Problem / root cause

`AllowInsecureHttp` is read only by validation: `DockerUpdateGuardOptionsValidator.ValidatePortainerOptions`
(`src/DockerUpdateGuard/Configuration/DockerUpdateGuardOptionsValidator.cs:337-340`) rejects an `http` Portainer
`BaseUrl` without it. `PortainerClient.CreateAuthenticatedClientAsync`
(`src/DockerUpdateGuard/Portainer/PortainerClient.cs:135-189`) sets `BaseAddress` to the `http` URL (line 141) and
sends the `X-API-Key` header (line 146) or posts the credentials to `/api/auth` (line 156) without any log entry.
`.squad/project.md` (*TLS and transport relaxations*) requires both relaxations to keep their runtime warnings; only
`SkipCertificateValidation` has one (`src/DockerUpdateGuard/Docker/DockerInstanceClient.cs:287-292`).

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | Validation accepts an `http` Portainer `BaseUrl` only with `AllowInsecureHttp: true`; no other code reads the flag | `DockerUpdateGuardOptionsValidator.cs:337-340`; `grep AllowInsecureHttp src` finds only it, `PortainerOptions.cs:43` and validator tests | confirmed |
| 2 | `CreateAuthenticatedClientAsync` sends the token or credentials over `BaseUrl` without a log entry | `PortainerClient.cs:135-189` (issue cites 134-187) | confirmed |
| 3 | `SkipCertificateValidation` warns once per instance name per process via `_certificateValidationWarningInstances` (static, `StringComparer.Ordinal`) and EventId 3106 | `DockerInstanceClient.cs:34`, `:287-292`; `DockerInstanceClientLogging.cs:90-93` | confirmed |
| 4 | `PortainerClient` is a singleton, so an instance field lasts for the process lifetime | `ServiceCollectionExtensions.cs:104` | confirmed |
| 5 | The area document says no runtime warning is logged | `docs/areas/container-actions.md:33`, and a *Known gaps* entry at `:101-102` (the issue's line 32 and *Open questions* item 4 are wrong locations) | confirmed |
| 6 | README `AllowInsecureHttp` row lacks the runtime-warning note | `README.md:191` (issue says 187); `SkipCertificateValidation` row with the note at `README.md:171`; the issue's line 187 is wrong | confirmed |
| 7 | Issue scenario "credentials have been sent unencrypted ever since" | No production code calls `IPortainerClient` (`grep` finds only its registration, the class and `PortainerClientTests`); area document `container-actions.md:80-81` and #280. Nothing is sent today; the warning appears once #280 adds a caller | refuted |
| 8 | Portainer EventIds 3300 and 3303-3310 are in use; 3301, 3302 never existed | `PortainerClientLogging.cs`; `git log -S "EventId = 3301"` / `3302` return nothing | confirmed |
| 9 | `Uri.Scheme` is lower case for any accepted spelling (`HTTP://`, `Http://`), so `Scheme == Uri.UriSchemeHttp` matches every `http` URL validation accepts | .NET documentation of `Uri.Scheme` ("returned in lowercase"); the validator relies on the same comparison | confirmed |
| 10 | `SequenceHttpMessageHandler` answers 404 for an unregistered URL, and `TestLogger<T>` records EventId, level and formatted message | `Helper/SequenceHttpMessageHandler.cs:113-116`; `Data/TestLogger{TCategoryName}.cs` | confirmed |
| 11 | `DockerInstanceOptions.Name` is non-null (`string.Empty` default, `[Required]`), so `TryAdd` cannot throw on a null key | `DockerInstanceOptions.cs:15-16` | confirmed |
| 12 | `CreateAuthenticatedClientAsync` has no test call site | `grep CreateAuthenticatedClientAsync src` finds only `PortainerClient.cs:135, 224, 306` | confirmed |
| 13 | `docs/areas/telemetry.md:143` already points to Container actions for the `AllowInsecureHttp` runtime warning; `docs/ARCHITECTURE.md:399-401` defers to the area documents | read; the telemetry pointer is stale today and true after this change | confirmed |

## Acceptance criteria

- [ ] AC1: For one `PortainerClient`, two calls for the same Docker instance whose Portainer `BaseUrl` is `http`
  (a capability check, then an action) log exactly one entry with EventId 3311 at level `Warning` whose message
  contains the instance name.
- [ ] AC2: Two Docker instances with `http` Portainer URLs, one written `http://`, the other `HTTP://`, log one
  EventId 3311 warning each, each naming its own instance.
- [ ] AC3: A Portainer `BaseUrl` with `https` and `AllowInsecureHttp: true` logs no EventId 3311 entry.
- [ ] AC4: The EventId 3311 message contains none of the API token, the username, the password or the JWT returned
  by the login (known values, one instance using `ApiToken`, one using `Username`/`Password` with a successful login).
- [ ] AC5: The warning is logged before the first Portainer request, regardless of its outcome: with an `http`
  URL and a login answering 401, the EventId 3311 entry is present and precedes the EventId 3303 entry
  (`PortainerAuthFailed`).

Unchanged and covered by existing tests: an `http` URL without `AllowInsecureHttp` still fails startup
(`DockerUpdateGuardOptionsValidatorPortainerHttpWithoutAllowInsecureHttpReturnsFailure`).

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `PortainerClientWithInsecureHttpLogsWarningOncePerInstanceAsync` | `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs` |
| AC2 | `PortainerClientWithInsecureHttpForTwoInstancesLogsOneWarningPerInstanceAsync` | `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs` |
| AC3 | `PortainerClientExecuteActionAsyncWithHttpsAndAllowInsecureHttpDoesNotLogInsecureWarningAsync` | `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs` |
| AC4 | `PortainerClientWithInsecureHttpWarningDoesNotContainSecretsAsync` | `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs` |
| AC5 | `PortainerClientGetCapabilityAsyncWithInsecureHttpAndFailedLoginLogsWarningBeforeAuthFailureAsync` | `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs` |

The AC3 test passes on today's code (no warning exists); it is a regression pin for the scheme check, not a
failing-first test.

## Approach

1. `PortainerClientLogging`: add `PortainerInsecureHttpInUse`, EventId 3311 (next above the highest in use; 3301
   and 3302 stay unused), `LogLevel.Warning`, message exactly:
   `Portainer for Docker instance {DockerInstanceName} uses plaintext HTTP (AllowInsecureHttp); the API token, credentials and session token are transmitted unencrypted`.
   Only the instance name is a placeholder: no base URL, so no user info can reach the message.
2. `PortainerClient`: add the field `_insecureHttpWarningInstances`, a
   `ConcurrentDictionary<string, byte>` with `StringComparer.Ordinal` (instance field, not static: the client is a
   singleton, and tests stay isolated).
3. `CreateAuthenticatedClientAsync` takes the `DockerInstanceOptions` instead of `PortainerOptions` (it needs the
   instance name) and reads `instanceOptions.Portainer` for everything it read before. Right after
   `client.BaseAddress = new Uri(...)` and before any header or request, when
   `client.BaseAddress.Scheme == Uri.UriSchemeHttp` and `_insecureHttpWarningInstances.TryAdd(instanceOptions.Name, 0)`
   returns `true`, log `PortainerInsecureHttpInUse(instanceOptions.Name)`. The check reads the parsed URL, not the
   flag. Both call sites (`GetCapabilityAsync`, `ExecuteActionAsync`) pass `instanceOptions`.
4. Consequence of the placement: no warning when Portainer is disabled, has no `BaseUrl`, or an action is rejected
   by the allow-list — no request is made then.

Tests: build each instance with the existing `CreateInstanceOptions` and override `BaseUrl` (for example
`http://portainer.example.test`); responses need not be registered where the outcome does not matter (404 by
default). Filter log entries by `EventId.Id == 3311`. Each test uses a fresh `PortainerClient`, so instance names
need no GUID suffix.

## Options considered

- **Where:** first request per instance (chosen) vs. a startup warning vs. both vs. every request — reasons in
  [record 0006](../../docs/decisions/0006-insecure-portainer-http-warns-at-first-request.md).
- **Dedup state:** instance field (chosen) vs. a static field like `DockerInstanceClient`'s — a static field leaks
  state between tests (that test needs a GUID instance name) and gains nothing for a singleton.
- **Trigger:** parsed scheme (chosen) vs. `AllowInsecureHttp == true` — the flag alone would warn for an `https`
  URL, which AC3 forbids.
- **Placement:** inside `CreateAuthenticatedClientAsync` (chosen, one place before every request) vs. at the start
  of both public methods — two copies, and the action allow-list rejection would warn without a request.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `src/DockerUpdateGuard/Portainer/PortainerClient.cs` | new field, changed private method signature, warning |
| DockerUpdateGuard | `src/DockerUpdateGuard/Portainer/PortainerClientLogging.cs` | new `LoggerMessage` |
| DockerUpdateGuard.Tests | `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs` | five tests |

## Signatures (for the Dev's skeleton)

In `PortainerClientLogging` (region `Methods`, after EventId 3310):

```csharp
[LoggerMessage(EventId = 3311,
               Level = LogLevel.Warning,
               Message = "Portainer for Docker instance {DockerInstanceName} uses plaintext HTTP (AllowInsecureHttp); the API token, credentials and session token are transmitted unencrypted")]
public static partial void PortainerInsecureHttpInUse(this ILogger logger, string dockerInstanceName);
```

In `PortainerClient`:

```csharp
// region Fields
private readonly ConcurrentDictionary<string, byte> _insecureHttpWarningInstances = new(StringComparer.Ordinal);

// region Methods (changed: was PortainerOptions options)
private async Task<HttpClient> CreateAuthenticatedClientAsync(DockerInstanceOptions instanceOptions, CancellationToken cancellationToken)
```

`PortainerClient.cs` needs `using System.Collections.Concurrent;`. The skeleton rewrites `PortainerClient.cs`
(signature and both call sites) and `PortainerClientLogging.cs`; the logging method is complete as written.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs`

Existing test code that calls a changed signature: none (the method is private, fact 12).

Files no unit test can cover: none.

## Areas

- [Container actions](../../docs/areas/container-actions.md): *Transport security* states the warning (once per
  instance name per process lifetime, at the first request for an instance whose `BaseUrl` is `http`, naming the
  instance only); the *Known gaps* entry for #276 is removed; *Related decisions* links record 0006.

## Documentation updates

Sweep: `grep -rn "AllowInsecureHttp\|runtime warning" docs README.md` — hits in `container-actions.md:15,31,33,86,101`,
`telemetry.md:143`, `ARCHITECTURE.md:399`, `README.md:190-191`.

| File | Edit | Owner |
| ---- | ---- | ----- |
| `docs/areas/container-actions.md` | line 33: replace "It is a validation switch: no runtime warning is logged while it is active." with the warning behavior (Areas above); delete the *Known gaps* bullet at lines 101-102; *Related decisions* "None yet." → link to record 0006 | Dev |
| `README.md` | line 191 `AllowInsecureHttp` row: append "a warning is logged at runtime, once per Docker instance, while it is in use" | Dev |
| `docs/areas/telemetry.md:143`, `docs/ARCHITECTURE.md:399-401`, `.squad/project.md:16` | none: true after this change | — |

## Architecture check

No guarantee of `.squad/project.md` is weakened; the change restores *TLS and transport relaxations* ("keep their
runtime warnings"). Secrets stay out of logs (the message has the instance name only). `PortainerClient` keeps
returning results instead of throwing; the new code path cannot throw (fact 11). No layering or lifetime change.

## Security considerations

- [x] No limits: no parsed input.
- [x] Exceptions reaching a user-visible message: none new; `TryAdd` and the log call do not throw for a non-null name.

## Decision records

- `docs/decisions/0006-insecure-portainer-http-warns-at-first-request.md` (Proposed)

## Out of scope / follow-ups

- A startup warning without Portainer traffic (record 0006, option 2).
- Wiring a caller of `PortainerClient` (#280); trace spans for Portainer calls (#83).

## Challenge notes

1. (minor) When the Dev rewrites `docs/areas/container-actions.md` *Transport security* line 33, the new text must say that the warning only appears once something calls `PortainerClient`, which nothing does today (#280). It should also link to the existing statement at `container-actions.md:80-81`. Otherwise the area document reads as if operators with `AllowInsecureHttp: true` will already see the warning, while record 0006 (*Consequences*, first bullet) says they won't. Why minor: it is a wording instruction the Dev can follow without changing the plan.
