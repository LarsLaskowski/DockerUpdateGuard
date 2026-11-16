# Plan: An image inspect error during container discovery fails the whole discovery

Source: Issue #497
Tier: standard — a behavior change in error handling (a failed inspect no longer fails discovery, and every failed inspect is logged) that creates, changes or removes no security control.
Security areas touched: Docker Engine access (`Docker/DockerInstanceClient.cs`); Logging and telemetry of external data (`Docker/DockerInstanceClientLogging.cs`: a new warning that carries the local image ID from the Engine's container list and, for a thrown error, the exception)

## Problem / root cause

`DockerInstanceClient.DiscoverContainersAsync` (`src/DockerUpdateGuard/Docker/DockerInstanceClient.cs:1125`) calls
`PopulateRepositoryDigestsAsync` (`:107-137`), which inspects each distinct local image through
`GetRepositoryDigestsAsync` (`:147-163`) and `ReadImageInspectAsync` (`:896-939`). `ReadImageInspectAsync` turns only a
404, another non-success answer and its own timeout into a result; it catches nothing else (`:931-938`).
`GetRepositoryDigestsAsync` has no handler either, so a transport error (`HttpRequestException`, `IOException`), a
body that does not parse or decode (`JsonException`, from `RemoteJsonDocument.ParseAsync`) or a value of an unexpected
kind (`InvalidOperationException` from `JsonElement.GetString` in `ReadStringArray` `:881-882` and in
`ParseImageInspect` `:821-835`, or from `TryGetProperty` on a root that is not an object `:816`) reaches the catch-all of
`DiscoverContainersAsync` (`:1145-1150`), which answers *failed* for the whole instance and drops every container
already listed. That breaks the promise of `docs/areas/docker-instances.md` (*Container discovery*): "A failed
inspect leaves the digest empty and does not fail discovery."

Related defect: a 404, a non-success answer or a timeout of the same inspect does leave the digest empty, but
`GetRepositoryDigestsAsync` (`:157-160`) drops the failure silently, while *Limits and errors* of the same document
says "every failure is logged with its error". The fix logs both kinds (the issue's "the failure is logged").

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | `ReadImageInspectAsync` handles only 404, non-success and timeout; other exceptions escape | `DockerInstanceClient.cs:907-918`, catch clauses `:931-938` (only `OperationCanceledException`) | confirmed |
| 2 | `GetRepositoryDigestsAsync` and `PopulateRepositoryDigestsAsync` have no exception handler | `DockerInstanceClient.cs:107-163` | confirmed |
| 3 | An escaping exception fails the whole discovery with the instance's catch-all | `DockerInstanceClient.cs:1125-1128` inside the `try` of `:1089`; catch-all `:1145-1150` logs 3104 and answers *failed* | confirmed |
| 4 | A malformed or undecodable inspect body throws `JsonException` | `Infrastructure/RemoteJsonDocument.cs` `ParseAsync`: `ExternalJson.ParseAsync`, an undecodable string rethrown as `JsonException`; decision 0045 | confirmed |
| 5 | `"RepoDigests": [1]` throws `InvalidOperationException` | `ReadStringArray` `:881-882` calls `item.GetString()` on every item; `JsonElement.GetString` throws `InvalidOperationException` for a kind other than String or Null (.NET API documentation) | confirmed |
| 6 | An inspect body whose root is not an object (`[]`) throws `InvalidOperationException` | `ParseImageInspect` `:816` calls `TryGetProperty` on the root; `JsonElement.TryGetProperty` throws `InvalidOperationException` when the kind is not Object (.NET API documentation) | confirmed |
| 7 | A caller cancellation during the inspect propagates today | `ReadImageInspectAsync` `:931-934` and `DiscoverContainersAsync` `:1135-1138` rethrow when the caller's token is cancelled | confirmed |
| 8 | The area document promises that a failed inspect does not fail discovery | `docs/areas/docker-instances.md:102-103` | confirmed |
| 9 | A non-success, 404 or timed-out inspect during discovery is not logged today | `GetRepositoryDigestsAsync` `:157-160` returns `[]` without a log call | confirmed |
| 10 | `InspectImageAsync` (the other caller of `ReadImageInspectAsync`) already catches every exception, logs 3108 and answers *failed* | `DockerInstanceClient.cs:1255-1273` | confirmed |
| 11 | `FailureReason.FromException` maps `HttpRequestException`/`IOException` → `connection failed`, `JsonException` → `invalid response`, other → `unexpected error` | `Infrastructure/FailureReason.cs` | confirmed |
| 12 | Event id 3112 is free | `grep "EventId = 3112" src` finds nothing; `DockerInstanceClientLogging.cs` ends at 3111 | confirmed |
| 13 | Existing discovery tests filter log entries by event id, so a new 3112 entry (unstubbed inspect URLs answer 404 in `SequenceHttpMessageHandler`) breaks none | `DockerInstanceClientTests.cs` assertions at `:96`, `:214`, `:277`, `:316-319`, `:1019`, `:1088`; `Helper/SequenceHttpMessageHandler.cs:118` | confirmed |

## Acceptance criteria

- AC1: When the image inspect of one local image throws during discovery — a transport error, a body that is not
  valid JSON, a body with a string that does not decode (`\ud800`), `"RepoDigests": [1]`, or a root that is not an
  object (`[]`) — `DiscoverContainersAsync` answers *succeeded* with every listed container; the containers of that
  image have no `ImageDigest`, and a container of another local image whose inspect succeeds, inspected after the
  failing one, still gets its digest.
- AC2: Each such failure is logged once per discovery as warning 3112, carrying the exception, with the message
  `Docker image <image ID> of <instance name> could not be inspected; its containers are discovered without a digest: <reason>`,
  where the reason is `connection failed` for the transport error, `invalid response` for the invalid JSON and the
  undecodable string, and `unexpected error` for `[1]` and the non-object root; no warning 3104 is logged.
- AC3: A 404, a non-success answer (500) or a timed-out inspect during discovery leaves discovery *succeeded* and the
  digest of that image's containers empty, and is logged once as warning 3112 without an exception, the reason being
  the inspect's own result message: `Docker image '<image ID>' was not found`,
  `Docker image inspect for '<image ID>' returned 500: <excerpt>`, `Docker image inspect for '<image ID>' timed out after <n> seconds`.
- AC4: A cancellation of the caller's token while an inspect is pending still propagates out of
  `DiscoverContainersAsync` as `OperationCanceledException`, and no warning 3112 is logged.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `DockerInstanceClientDiscoverContainersAsyncWhenImageInspectThrowsSucceedsWithoutThatImagesDigestAsync` (DataRow per failure kind: Transport, InvalidJson, UndecodableString, NumberInRepoDigests, RootNotObject) | `src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientTests.cs` |
| AC2 | `DockerInstanceClientDiscoverContainersAsyncWhenImageInspectThrowsLogsSkippedImageAsync` (same DataRows with the expected reason) | `src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientTests.cs` |
| AC3 | `DockerInstanceClientDiscoverContainersAsyncWhenImageInspectFailsLogsSkippedImageAsync` (DataRow NotFound, ServerError, Timeout) | `src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientTests.cs` |
| AC4 | `DockerInstanceClientDiscoverContainersAsyncWhenCancelledDuringImageInspectThrowsAsync` | `src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientTests.cs` |

Cases for the Tester: the container list holds two containers on two local images, the failing image listed first;
the per-request answers come from `DelegateHttpMessageHandler` (a thrown `HttpRequestException` for Transport; a
pending `Task.Delay` with `RequestTimeoutSeconds = 1` for Timeout, as the existing discovery timeout test does). The
UndecodableString row asserts that its input still contains the backslash of `\ud800` (`.squad/team.md`, *Escapes in
files*; a raw string literal as in `LoneSurrogateEscape`). AC4 cancels the test's own `CancellationTokenSource` from the
handler of the inspect request. AC4 passes on today's code (Verified fact 7): it guards the new handler and is
*deferred* to step 6 if it cannot fail in step 5 (`.squad/routing.md`, step 5).

## Approach

In `DockerInstanceClient.cs` only, plus one log method:

1. `PopulateRepositoryDigestsAsync` and `GetRepositoryDigestsAsync` become private **instance** methods (they need
   `_logger`), moved from the `Static methods` region into `Methods`. Each takes `DockerInstanceOptions
   instanceOptions` instead of `int requestTimeoutSeconds` (four parameters each); the call at `:1125` passes
   `instanceOptions`.
2. `GetRepositoryDigestsAsync` wraps the `ReadImageInspectAsync` call in its own `try`:
   - `catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)` → `throw;` (AC4);
   - `catch (Exception exception)` → log 3112 with the exception and
     `FailureReason.FromException(exception, TimeSpan.FromSeconds(instanceOptions.RequestTimeoutSeconds), cancellationToken)`, return `[]`;
   - a result that is not *succeeded* (or has no data) → log 3112 without exception, reason
     `inspectResult.Message ?? inspectResult.Status.ToString()`, return `[]`.
   The handler is per image, so the loop of `PopulateRepositoryDigestsAsync` goes on with the next image and the
   containers of the failed image keep a `null` digest, as the existing `ResolveRepositoryDigest([])` path does today.
3. `ReadImageInspectAsync` and `InspectImageAsync` stay unchanged.

New log output, fixed here: one `[LoggerMessage]` in `DockerInstanceClientLogging.cs`, `EventId = 3112`,
`Level = LogLevel.Warning`, `Message = "Docker image {ImageId} of {DockerInstanceName} could not be inspected; its containers are discovered without a digest: {Reason}"`,
placed after 3111. The image ID is logged as the container list gave it (control characters replaced by
`TryGetString`, `:623`). No other output changes: the discovery result and its messages are those of a successful
discovery.

## Options considered

1. **A handler per image in `GetRepositoryDigestsAsync`, logging every failed inspect (chosen)** — only the failing
   image loses its digest, as the area document promises and the issue expects; `InspectImageAsync` keeps its own
   messages and event 3108.
2. **Catch every exception inside `ReadImageInspectAsync` and answer *failed*** — it would also fix discovery, but it
   changes `InspectImageAsync` for the base-image callers: its thrown errors would no longer be logged as 3108 with
   the exception and its message frame `Docker image inspect failed for '<instance>': <reason>` would change.
3. **One `try` around the whole `PopulateRepositoryDigestsAsync` call** — discovery would succeed, but the first failing
   image would leave every later image without its digest, against the issue's "only that image's containers".
4. **Log only the thrown errors (the issue's scope) and keep 404, non-success and timeout silent** — leaves the
   related defect (Verified fact 9) against *Limits and errors*; logging them costs one call on the same path, as
   decision 0026 did for stats answers.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Docker/DockerInstanceClient.cs` | per-image handler, two private methods become instance methods |
| DockerUpdateGuard | `Docker/DockerInstanceClientLogging.cs` | new warning 3112 |
| DockerUpdateGuard.Tests | `DockerInstanceClientTests.cs` | four new tests |

## Signatures (for the Dev's skeleton)

In `internal static partial class DockerInstanceClientLogging` (region `Methods`, after 3111):

`public static partial void DockerInstanceImageDigestSkipped(this ILogger logger, Exception? exception, string dockerInstanceName, string imageId, string reason);`

Five parameters (S107), no keyword names (CA1716), no nested type. A source-generated partial needs no skeleton body.
The two changed methods are private (described in *Approach*).

## Test files

- `src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientTests.cs` (existing class, new methods).

Existing test code that calls a changed signature: none (the changed methods are private; `DiscoverContainersAsync`
keeps its signature).

Files of the change no unit test can cover: none.

## Areas

- `docs/areas/docker-instances.md`: *Container discovery* and *Timeouts* state which inspect failures leave the
  digest empty and how they are logged (see below).
- `docs/areas/telemetry.md`: the list of what logs may carry gains the local image ID.

## Documentation updates

All by the Dev, against the code as built:

- `docs/areas/docker-instances.md`, *Container discovery* (`:99-103`): replace "A failed inspect leaves the digest empty
  and does not fail discovery." by the failure kinds (non-success answer, not found, timeout, transport error, a body
  that is not valid JSON, does not decode or holds a value of an unexpected kind), that only that image's containers
  lose the digest and the other images are still inspected, and the warning 3112 with instance, image ID and reason
  (the inspect's failure message for an answer or a timeout; `connection failed`, `invalid response` or `unexpected
  error`, with the error, otherwise).
- `docs/areas/docker-instances.md`, *Timeouts* (`:83-84`): the list of requests whose timeout does not fail the
  operation ("a stats call that exceeds it only leaves its container out of the round") gains the image inspect of
  discovery, which only leaves that image's digest empty.
- `docs/areas/docker-instances.md`, *Limits and errors* (`:203`): "every failure is logged with its error" — now true
  for discovery's inspects; no edit, checked by the Dev.
- `docs/areas/telemetry.md`, *Credentials and external data* (`:111-113`): "container IDs and names, repository names
  … taken from remote answers" gains "local image IDs".
- Sweep result: `docs/ARCHITECTURE.md` (client table `:237`) and `README.md` hold no statement about inspect failures;
  `docs/areas/integration-failures.md` and the other areas list no Docker event ids; no changelog exists. No further
  hits.

## Architecture check

No guarantee of `.squad/project.md` changes. Integration calls still answer with an outcome and fixed reasons
(decision 0009): the discovery result carries no inspect error text, and the logged reasons are the fixed ones of
`FailureReason` or the result message `ReadImageInspectAsync` already composes. Layering and composition root
untouched.

## Security considerations

- [x] No limits: no byte or length limit on parsed input is added or changed.
- [x] Exceptions that can reach a user-visible message: none. The failure no longer reaches the discovery result at all;
  it reaches only the log. The logged exception is a `JsonException` with the fixed message of decision 0045, a
  framework `InvalidOperationException` naming JSON kinds only, or an `HttpRequestException`/`IOException` whose message
  can name the Engine host — allowed on the error entries of failed integration calls (`docs/areas/telemetry.md`,
  *Credentials and external data*), as for warning 3111.
- The image ID in the log line comes from the Engine's container list with control characters replaced, uncut; the
  same ID is already part of the result messages of `ReadImageInspectAsync`. The Engine itself is the root-equivalent
  trusted endpoint (*Docker Engine access*).
- The caller's cancellation is rethrown before the catch-all (AC4), so shutdown is not logged as a failed inspect.

## Decision records

- none: the change makes the code keep a behavior the area document already promises; per-image handling and logging
  of every failure follow decision 0026's pattern for stats requests.

## Out of scope / follow-ups

- `ReadImageInspectAsync`'s own messages and `InspectImageAsync` are unchanged (option 2).
