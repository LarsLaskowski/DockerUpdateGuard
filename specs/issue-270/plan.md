# Plan: One container's stats failure discards the whole resource-sampling round of its instance

Source: Issue #270
Tier: standard — a behavior change of resource sampling (which failures end a round) that creates, changes or removes no security control.
Security areas touched: Logging and telemetry of external data (a new log entry in `Docker/DockerInstanceClientLogging.cs` carrying a container name and ID from the Engine's answer); Failure messages and remote text (a new fixed *failed* message and the fixed reasons of `FailureReason` in that log entry; `FailureReason.cs` itself unchanged)

## Problem / root cause

`src/DockerUpdateGuard/Docker/DockerInstanceClient.cs:1098-1186` (`CollectContainerResourceUsageAsync`) runs the
container list and every per-container stats request (lines 1146-1167) inside one `try`. Only a non-success stats
answer (`continue`, lines 1150-1153) and a `JsonException` while parsing the body (caught in `ReadResourceSampleAsync`,
lines 564-576) leave a single container out. Every other failure of one stats request leaves the loop and lands in the
round's handlers: a timeout of `statsTimeoutSource` in `catch (OperationCanceledException)` (lines 1175-1178), a
transport error or a body that breaks off in `catch (Exception)` (lines 1180-1185), and so does a stats body that is
valid JSON of the wrong shape (`ParseResourceSample`, line 540, calls `TryGetProperty` on a non-object, which throws
`InvalidOperationException`). The method returns *failed*; `ResourceStatisticsCollector.CollectForInstanceAsync`
(`src/DockerUpdateGuard/Images/ResourceStatisticsCollector.cs:127-132`) then logs event 2103 and records nothing for
the instance.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | A stats timeout or transport error fails the whole round of the instance | `DockerInstanceClient.cs:1146-1185`, one `try` around list and loop | confirmed |
| 2 | The issue's line numbers (1052-1135) and the collector's `_logger.LogWarning` at line 129 | Method now at 1098-1186; the collector already logs through the source-generated `ResourceStatisticsCollectionFailed` (event 2103, `ResourceStatisticsCollector.cs:130`); no collector change needed | refuted |
| 3 | A stats body that is not valid JSON aborts the round | `ReadResourceSampleAsync` catches `JsonException` and returns null (`DockerInstanceClient.cs:564-576`); test `DockerInstanceClientCollectContainerResourceUsageAsyncWhenOneStatsBodyHasLoneSurrogateSkipsThatContainerAsync` (row `{ "read": `) passes with the other container sampled; malformed JSON already drops only that container, silently | refuted |
| 4 | Related defect: valid JSON of the wrong shape (`[]`, `1`, `cpu_stats` as array) aborts the round | `TryGetString` (line 619) and `ParseCpuPercent` (line 676) call `TryGetProperty` on elements that are not objects, which throws `InvalidOperationException`; reaches `catch (Exception)` at line 1180 (read, not run) | confirmed |
| 5 | Related defect: the CPU formula can overflow `decimal` for an extreme `online_cpus` and abort the round | `ParseCpuPercent`, `(decimal)cpuDelta / systemDelta * onlineCpuCount * 100m` (line 707); covered by the per-container handler, no own test | confirmed |
| 6 | No `ResourceStatisticsCollectorTests` exist | `src/Tests/DockerUpdateGuard.Tests/ResourceStatisticsCollectorTests.cs` exists with one test (event 2103) | refuted |
| 7 | The area document tracks this issue under *Open questions* item 4 | `docs/areas/docker-instances.md`, the entry is the second bullet of *Known gaps* | refuted |
| 8 | Only running containers are listed for sampling | Request `v1.41/containers/json` without `all=1` (line 1116); area doc *Resource statistics* | confirmed |
| 9 | Caller cancellation is told apart from the per-request timeout by `cancellationToken.IsCancellationRequested` | Pattern at lines 1171-1174; `CreateRequestTimeoutCancellationTokenSource` (line 253) links the caller token | confirmed |
| 10 | `FailureReason.FromException` yields the fixed reasons used below | `Infrastructure/FailureReason.cs:53-83`: OCE not from the caller → `timed out after <n> seconds`; `HttpRequestException`/`IOException` → `connection failed`; `JsonException` → `invalid response`; other → `unexpected error` | confirmed |
| 11 | A test double throwing `TaskCanceledException` from the handler (caller token not cancelled) reaches the client as an `OperationCanceledException`, i.e. the timeout path, without real waiting | to verify: the Tester's AC2 `Timeout` row, whose expected reason is `timed out after 15 seconds`, step 5 | unverified |
| 12 | Collection rounds of the job never overlap; a long round delays the next | `docs/areas/scan-engine.md:32-33` | confirmed |
| 13 | Docker Engine requests get no retry handler | `docs/areas/integration-failures.md`, *Retry of transient HTTP failures* | confirmed |
| 14 | No log entry carries a container ID today | `grep "{ContainerId}" src/DockerUpdateGuard` finds nothing; `telemetry.md`, *Credentials and external data*, lists container names, not IDs | confirmed |
| 15 | Event id 3111 is free | `DockerInstanceClientLogging.cs` ends at 3110; no other `3111` in `src/` or `docs/` | confirmed |

## Acceptance criteria

Instance options in the examples: name `Production`, `RequestTimeoutSeconds` 15 (default). Containers `c1`, `c2`, `c3`
named `web-1`, `web-2`, `web-3`.

- AC1: When the stats request of one container fails — it times out, throws `HttpRequestException`, answers 500, or
  returns a body that is not valid JSON or is valid JSON that is not an object (`[]`) — `CollectContainerResourceUsageAsync`
  returns *succeeded* with the samples of the other containers, in list order, and requests stats for every following
  container.
- AC2: Each container left out by AC1 is logged exactly once per call as a warning with event id 3111 and the message
  `Docker container <name> (<ID>) of <instance> was left out of resource sampling: <reason>`, the reason being
  `timed out after 15 seconds`, `connection failed`, `returned 500`, `invalid response` or `unexpected error`
  respectively; the entry carries the exception for every reason but the non-success answer.
- AC3: With three running containers whose second stats request times out, `ResourceStatisticsCollector.CollectAsync`
  records container samples for `c1` and `c3` and one instance sample with container count 2.
- AC4: When stats were requested for at least one container and none could be sampled, the call returns *failed* with
  the message `Docker container resource sampling failed for 'Production': no container stats could be read`; the
  collector then records no container and no instance sample and logs its warning 2103 with that message.
- AC5: A container list without a container a stats request is sent for (empty, or only IDs longer than 200
  characters) still returns *succeeded* with no samples, and the collector records an instance sample with count 0.
- AC6: When the caller's token is cancelled during a stats request, the call throws `OperationCanceledException`,
  sends no further stats request, and logs neither event 3111 nor 3107.
- AC7: A container list request that times out or answers non-success still returns *failed* with today's messages.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | DockerInstanceClientCollectContainerResourceUsageAsyncWhenOneStatsRequestFailsSkipsOnlyThatContainerAsync | src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientTests.cs |
| AC2 | DockerInstanceClientCollectContainerResourceUsageAsyncWhenOneStatsRequestFailsLogsSkippedContainerAsync | src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientTests.cs |
| AC3 | ResourceStatisticsCollectorCollectAsyncWhenOneStatsRequestTimesOutRecordsOtherContainersAsync | src/Tests/DockerUpdateGuard.Tests/ResourceStatisticsCollectorTests.cs |
| AC4 | DockerInstanceClientCollectContainerResourceUsageAsyncWhenEveryStatsRequestFailsReturnsFailedAsync; ResourceStatisticsCollectorCollectAsyncWhenEveryStatsRequestTimesOutRecordsNoSamplesAsync | src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientTests.cs; src/Tests/DockerUpdateGuard.Tests/ResourceStatisticsCollectorTests.cs |
| AC5 | DockerInstanceClientCollectContainerResourceUsageAsyncWhenNoStatsAreRequestedReturnsEmptySucceededAsync; ResourceStatisticsCollectorCollectAsyncWhenNoContainerIsRunningRecordsEmptyInstanceSampleAsync | src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientTests.cs; src/Tests/DockerUpdateGuard.Tests/ResourceStatisticsCollectorTests.cs |
| AC6 | DockerInstanceClientCollectContainerResourceUsageAsyncWhenCallerCancelsDuringStatsThrowsOperationCanceledAsync | src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientTests.cs |
| AC7 | DockerInstanceClientCallsWhenRequestTimesOutReturnFailedAsync; DockerInstanceClientCollectContainerResourceUsageAsyncWhenErrorBodyIsLongReturnsBoundedExcerptAsync | src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientTests.cs |

Data rows: AC1 and AC2 share the rows Timeout, Transport, NonSuccess, InvalidJson and WrongShape (AC2's with the
expected reason); AC5's client test has the rows Empty and OnlyLongIds. AC7's two tests exist (the Resources row of the
first); the Tester adds `[TestCategory("AC7")]` to them.

Test doubles: `DelegateHttpMessageHandler` routing by request URI; a timeout is a `TaskCanceledException` thrown for the
stats URI of `c2` with the caller token untouched (fact 11), a transport error an `HttpRequestException`; never a real
wait. AC6's handler cancels the test's own `CancellationTokenSource` and throws `OperationCanceledException` with its
token; it records the requested URIs so the test asserts that `c3` was not requested. The collector tests use the
in-memory database and a real `DockerInstanceClient` with the injected `HttpClient` factory (as `DockerInstanceClientTests`
does), the host info URI answering 404 (memory limit 0).

## Approach

1. Move the per-container stats call (list loop body from the `statsTimeoutSource` on) into a private instance helper
   of `DockerInstanceClient`, for example `TryReadContainerSampleAsync(httpClient, instanceOptions, containerElement,
   containerId, cancellationToken)`, returning the sample or null. Inside it:
   - non-success answer: log 3111 with reason `returned <status code>` (no exception) and return null;
   - `catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)`: rethrow;
   - `catch (Exception exception)`: log 3111 with the exception and the reason
     `FailureReason.FromException(exception, TimeSpan.FromSeconds(instanceOptions.RequestTimeoutSeconds), cancellationToken)`
     and return null. This covers the timeout, transport errors, a body that breaks off, malformed JSON and wrong-shaped
     JSON.
   - `ReadResourceSampleAsync` loses its `JsonException` catch (the helper handles it, so malformed JSON is logged
     with `invalid response`); its return type may become non-nullable — a private change, the Dev's call.
   The container name in the log is the sample's name rule (`GetPrimaryName`, cut to 200), the ID the listed
   `containerId`; both already have control characters replaced.
2. In `CollectContainerResourceUsageAsync` count the containers a stats request is sent for. After the loop: requested
   > 0 and no sample → `ExternalOperationResult.Failed(...)` with the AC4 message, logging nothing more (the collector
   logs 2103); otherwise *succeeded* with the samples. The round's handlers stay for the list request.
3. `DockerInstanceClientLogging`: add `DockerInstanceResourceSampleSkipped` (event 3111, `LogLevel.Warning`, template
   `Docker container {ContainerName} ({ContainerId}) of {DockerInstanceName} was left out of resource sampling: {Reason}`).
4. `ResourceStatisticsCollector` is not changed.

New user-visible output: the log template above and the AC4 message; both fixed by the criteria.

## Options considered

1. Per-container handler inside the client, *failed* when nothing could be sampled (chosen; record 0026).
2. Return listed and sampled counts and let the collector decide (the issue's alternative) — changes
   `IDockerInstanceClient`, its result type and every substitute for a rule the client decides alone.
3. Keep the instance sample with count 0 when every stats request failed — rejected by the Product Manager's decision in
   the issue: it reads as an idle host.
4. Sample in parallel or skip a container that timed out last round, to bound the round's length — changes the Engine
   load and adds state across rounds; the issue accepts the longer round.
5. Log only the newly dropped reasons and keep a non-success answer silent — two rules for one outcome; the issue asks
   for every dropped container to be logged, and a 404 for a container that stopped meanwhile logs once and is gone the
   next round.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Docker/DockerInstanceClient.cs` | per-container handler, requested-count rule (Approach 1-2) |
| DockerUpdateGuard | `Docker/DockerInstanceClientLogging.cs` | new log method 3111 |
| DockerUpdateGuard.Tests | `DockerInstanceClientTests.cs`, `ResourceStatisticsCollectorTests.cs` | new tests, AC7 categories |

## Signatures (for the Dev's skeleton)

In the internal static class `DockerInstanceClientLogging`:

`[LoggerMessage(EventId = 3111, Level = LogLevel.Warning, Message = "Docker container {ContainerName} ({ContainerId}) of {DockerInstanceName} was left out of resource sampling: {Reason}")] public static partial void DockerInstanceResourceSampleSkipped(this ILogger logger, Exception? exception, string dockerInstanceName, string containerName, string containerId, string reason);`

Wrapped per *Parameter Formatting*. No other API member changes; `IDockerInstanceClient` and
`ResourceStatisticsCollector` keep their signatures. Existing files the skeleton rewrites: none beyond the two
production files above.

## Test files

- src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientTests.cs
- src/Tests/DockerUpdateGuard.Tests/ResourceStatisticsCollectorTests.cs

Existing test code that calls a changed signature: none. Existing sampling tests keep at least one sampled container
(`…WhenOneStatsBodyHasLoneSurrogateSkipsThatContainerAsync`, `…WhenFieldsHaveControlCharactersReplacesThemAsync`,
`…WhenContainerIdExceedsStoredLengthSkipsContainerAsync`, `…WhenNameExceedsStoredLengthCutsItAsync`) or fail at the
list (`…CallsWhenRequestTimesOutReturnFailedAsync`, `…WhenErrorBodyIsLongReturnsBoundedExcerptAsync`, the
`AssertInvalidResponseAsync` sampling case), so none is adapted; only AC7's two get a category (Tester).

Files of the change no unit test can cover: none.

## Areas

- `docs/areas/docker-instances.md`: *Timeouts*, *Resource statistics*, *Limits and errors*, *Known gaps*, *Related
  decisions* (below).
- `docs/areas/telemetry.md`: *Credentials and external data* gains container IDs.

## Documentation updates

All by the Dev, once the code is green:

- `docs/areas/docker-instances.md`
  - *Timeouts*, second bullet: a stats call that exceeds the timeout leaves only its container out (see *Resource
    statistics*); the other requests still end *failed*.
  - *Resource statistics*, second bullet: a container is left out of the round when its stats request answers
    non-success, times out, fails at the transport level or returns a body that cannot be read or is not a stats
    object; each one is logged once per round as warning 3111 with instance, container name, ID and the fixed reason.
    New bullet: a round sends one request after the other, so with hung containers it can take up to one
    `RequestTimeoutSeconds` per container (plus the list and the host info request); rounds do not overlap
    ([Scan engine](scan-engine.md)), a long round delays the next.
  - *Resource statistics*, instance-sample bullet: the sums are over the containers sampled in that round; a round
    that requested stats for at least one container and sampled none fails (next bullet's rule).
  - *Limits and errors*, the bullet "A resource collection that fails for an instance …": the round fails when the
    list request fails or times out, or when no requested container could be sampled (message as AC4); no samples,
    one warning, other instances unaffected.
  - *Known gaps*: delete the #270 entry.
  - *Related decisions*: add record 0026.
- `docs/areas/telemetry.md`, *Credentials and external data*: "container and repository names" becomes "container IDs
  and names, repository names" (the 3111 entry carries the ID).
- `docs/decisions/0026-…`: status stays `Proposed` until step 9; no other edit unless the build deviates.
- `README.md`, `docs/ARCHITECTURE.md`: no change (sweep: ARCHITECTURE.md mentions the job only in its service table,
  line 343; no configuration key changes). No changelog file exists.

## Architecture check

Integration calls still answer with outcomes and fixed reasons (`.squad/project.md`, *Guarantees*; record 0009): the
new *failed* message is composed of fixed text and the configured name, the log reasons come from `FailureReason`, and
the only exception that leaves the method is the caller's cancellation, as today. Layering unchanged.

## Security considerations

- [x] No limits: no byte or length limit is added or changed; the logged name and ID keep their existing bounds (200
  characters) and control-character replacement.
- [x] Exceptions reaching a user-visible message: none new. The per-container exceptions reach only the log (with the
  exception, as every Engine failure log does); the AC4 result message, which can reach the logs and no page (resource
  collection writes no scan run, `scan-engine.md:70`), is fixed text plus the instance name.

## Decision records

- `docs/decisions/0026-a-failed-stats-request-drops-only-its-container-from-a-resource-round.md` (Proposed), index row
  added.

## Out of scope / follow-ups

- Bounding the round's length when many containers hang (option 4): no issue proposed now; record 0026 names it.
- #80 (the collector loads the instance's whole sample history per round): same method, independent.
