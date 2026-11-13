# Plan: One container's stats failure discards the whole resource-sampling round of its instance

Source: Issue #270
Tier: standard — a behavior change of resource sampling (which failures end a round, how the instance network rate is computed) that creates, changes or removes no security control.
Security areas touched: Logging and telemetry of external data (a new log entry in `Docker/DockerInstanceClientLogging.cs` carrying a container name and ID from the Engine's answer); Failure messages and remote text (a new fixed *failed* message and the fixed reasons of `FailureReason` in that log entry; `FailureReason.cs` itself unchanged)

## Problem / root cause

`DockerInstanceClient.cs:1098-1186` (`CollectContainerResourceUsageAsync`) runs the list and every stats request
(lines 1146-1167) in one `try`. Only a non-success answer (lines 1150-1153) and a `JsonException` (caught in
`ReadResourceSampleAsync`, 564-576) drop a single container; a stats timeout (1175-1178), a transport error, a body that
breaks off or valid JSON of the wrong shape (1180-1185) fail the round, and `ResourceStatisticsCollector.cs:127-132`
records nothing for the instance. With partial rounds recorded, the instance network rate (a delta of summed totals
against the previous instance sample, collector lines 186-206) would spike when a dropped container returns, so that
rule changes too.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | A stats timeout or transport error fails the whole round of the instance | `DockerInstanceClient.cs:1146-1185`, one `try` around list and loop | confirmed |
| 2 | The issue's line numbers (1052-1135) and the collector's `_logger.LogWarning` at line 129 | Method at 1098-1186; the collector logs the source-generated event 2103 (line 130) | refuted |
| 3 | A stats body that is not valid JSON aborts the round | `ReadResourceSampleAsync` returns null on `JsonException` (564-576); an existing test (row `{ "read": `) passes | refuted |
| 4 | Related defect: valid JSON of the wrong shape (`[]`, `1`, `cpu_stats` as array) aborts the round | `TryGetString` (line 619) and `ParseCpuPercent` (line 676) call `TryGetProperty` on elements that are not objects, which throws `InvalidOperationException`; reaches `catch (Exception)` at line 1180 (read, not run) | confirmed |
| 5 | Related defect: the CPU formula can overflow `decimal` for an extreme `online_cpus` and abort the round | `ParseCpuPercent`, `(decimal)cpuDelta / systemDelta * onlineCpuCount * 100m` (line 707); covered by the per-container handler, no own test | confirmed |
| 6 | No `ResourceStatisticsCollectorTests` exist | The file exists with one test | refuted |
| 7 | The area document tracks this issue under *Open questions* item 4 | It is the second *Known gaps* bullet | refuted |
| 8 | Only running containers are listed for sampling | Request `v1.41/containers/json` without `all=1` (line 1116); area doc *Resource statistics* | confirmed |
| 9 | Caller cancellation is told apart from the per-request timeout by `cancellationToken.IsCancellationRequested` | Pattern at lines 1171-1174; `CreateRequestTimeoutCancellationTokenSource` (line 253) links the caller token | confirmed |
| 10 | `FailureReason.FromException` yields the fixed reasons used below | `Infrastructure/FailureReason.cs:53-83`: OCE not from the caller → `timed out after <n> seconds`; `HttpRequestException`/`IOException` → `connection failed`; `JsonException` → `invalid response`; other → `unexpected error` | confirmed |
| 11 | A test double throwing `TaskCanceledException` from the handler (caller token not cancelled) reaches the client as an `OperationCanceledException`, the same path as a real timeout | Security's measurement (`dotnet run`, SDK 10.0.404): both the double and a real `CancelAfter` timeout arrive as `TaskCanceledException` with the caller token not cancelled | confirmed |
| 12 | Collection rounds of the job never overlap; a long round delays the next | `docs/areas/scan-engine.md:32-33` | confirmed |
| 13 | Docker Engine requests get no retry handler | `docs/areas/integration-failures.md`, *Retry of transient HTTP failures* | confirmed |
| 14 | No log entry carries a container ID today | `grep "{ContainerId}" src/DockerUpdateGuard` finds nothing; `telemetry.md`, *Credentials and external data*, lists container names, not IDs | confirmed |
| 15 | Event id 3111 is free | `DockerInstanceClientLogging.cs` ends at 3110; no other `3111` in `src/` or `docs/` | confirmed |
| 16 | The instance network rate is the delta of summed totals against the previous instance sample; the per-container rate is computed against that container's own previous sample | `ResourceStatisticsCollector.cs:164-171` (container), `186-206` (instance), `CalculateBytesPerSecond` lines 81-104 | confirmed |
| 17 | Related defect: a `Names` element that is not a string (`"Names": [1]`) makes `GetPrimaryName` throw `InvalidOperationException` from `JsonElement.GetString()`, failing discovery and the sampling round | `DockerInstanceClient.cs:595-610` (line 601); callers `ParseContainer` (line 521) and `ParseResourceSample` (line 547) | confirmed |

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
- AC8: The instance receive and transmit rates of a round are the sums of the per-container rates of that round. Three
  rounds one minute apart (12:00, 12:01, 12:02), `rx_bytes` and `tx_bytes` equal per container: round 1 samples
  `c1`-`c3` at 1000 each; round 2 drops `c2` (timeout), `c1` and `c3` at 1600; round 3 samples `c1` and `c3` at 2200 and
  `c2` at 1600. The instance rates are 0, 20 and 25 bytes per second (round 3: 10 + 5 + 10).
- AC9: A container whose `Names` holds no string element (`"Names": [1]`) is named by its ID, in discovery and in
  resource sampling, and fails neither.

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
| AC8 | ResourceStatisticsCollectorCollectAsyncWhenContainerIsDroppedAndReturnsSumsContainerRatesAsync | src/Tests/DockerUpdateGuard.Tests/ResourceStatisticsCollectorTests.cs |
| AC9 | DockerInstanceClientDiscoverContainersAsyncWhenNamesHoldNoStringUsesContainerIdAsync; DockerInstanceClientCollectContainerResourceUsageAsyncWhenNamesHoldNoStringUsesContainerIdAsync | src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientTests.cs |

Data rows: AC1 and AC2 share the rows Timeout, Transport, NonSuccess, InvalidJson and WrongShape (AC2's with the
expected reason); AC5's client test has the rows Empty and OnlyLongIds. AC7's two tests exist (the Resources row of the
first); the Tester adds `[TestCategory("AC7")]` to them.

Test doubles: `DelegateHttpMessageHandler` by request URI; a timeout is a `TaskCanceledException` for `c2`'s stats URI
with the caller token untouched (fact 11), never `TimeoutHttpMessageHandler` (a real timer). AC6's handler cancels the
test's own token source, throws `OperationCanceledException` and records URIs (`c3` never requested). Collector tests: the
in-memory database and a real `DockerInstanceClient` with the injected `HttpClient` factory, host info answering 404.
AC8 runs `CollectAsync` three times on one database, stats bodies carrying `read` and `networks.eth0` totals; the old
rule would give 46.6667 in round 3.

## Approach

1. Move the per-container stats call (list loop body from the `statsTimeoutSource` on) into a private instance helper
   of `DockerInstanceClient`, for example `TryReadContainerSampleAsync(httpClient, instanceOptions, containerElement,
   containerId, cancellationToken)`, returning the sample or null. Inside it:
   - non-success answer: log 3111 with reason `returned <status code>` (no exception) and return null;
   - `catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)`: rethrow;
   - `catch (Exception exception)`: log 3111 with the exception and the reason
     `FailureReason.FromException(exception, TimeSpan.FromSeconds(instanceOptions.RequestTimeoutSeconds), cancellationToken)`
     and return null. This covers the timeout, transport errors, a body that breaks off, malformed JSON, wrong-shaped
     JSON and the `decimal` overflow.
   - `ReadResourceSampleAsync` loses its `JsonException` catch (the helper handles it, so malformed JSON is logged
     with `invalid response`); its return type may become non-nullable — a private change, the Dev's call.
   The container name in the log is the sample's name rule (`GetPrimaryName`, cut to 200), computed once before the
   `try` so the handler itself cannot throw; the ID is the listed `containerId`. Both already have control characters
   replaced.
2. `GetPrimaryName` considers only `Names` elements of kind `String` (others are skipped), falling back to the ID as
   today (AC9). This fixes discovery and sampling alike.
3. In `CollectContainerResourceUsageAsync` count the containers a stats request is sent for. After the loop: requested
   > 0 and no sample → `ExternalOperationResult.Failed(...)` with the AC4 message, logging nothing more (the collector
   logs 2103); otherwise *succeeded* with the samples. The round's handlers stay for the list request.
4. `DockerInstanceClientLogging`: add `DockerInstanceResourceSampleSkipped` (event 3111, `LogLevel.Warning`, template
   `Docker container {ContainerName} ({ContainerId}) of {DockerInstanceName} was left out of resource sampling: {Reason}`).
5. `ResourceStatisticsCollector.CollectForInstanceAsync`: the instance `NetworkRxBytesPerSecond` and
   `NetworkTxBytesPerSecond` become the sums of the round's container rates (AC8). The instance totals stay the sums of
   the container totals; the query of the latest instance sample, used only for the rates, is removed. No signature
   changes.

New user-visible output: the log template above and the AC4 message; both fixed by the criteria.

## Options considered

Record 0026 gives each option's reasoning.

1. Per-container handler inside the client, *failed* when nothing could be sampled (chosen).
2. Listed and sampled counts returned for the collector to decide (the issue's alternative) — changes the interface.
3. An instance sample with count 0 when every stats request failed — rejected by the Product Manager.
4. Parallel sampling, or skipping last round's hung container — changes Engine load, adds state.
5. A non-success answer kept silent — two rules for one outcome; the issue asks to log every dropped container.
6. Instance rate as the delta over containers sampled in both rounds — needs the previous set, drops a returning
   container's traffic.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Docker/DockerInstanceClient.cs` | per-container handler, requested-count rule, `GetPrimaryName` (Approach 1-3) |
| DockerUpdateGuard | `Docker/DockerInstanceClientLogging.cs` | new log method 3111 |
| DockerUpdateGuard | `Images/ResourceStatisticsCollector.cs` | instance rate as sum of container rates (Approach 5) |
| DockerUpdateGuard.Tests | `DockerInstanceClientTests.cs`, `ResourceStatisticsCollectorTests.cs` | new tests, AC7 categories |

## Signatures (for the Dev's skeleton)

In the internal static class `DockerInstanceClientLogging`:

`[LoggerMessage(EventId = 3111, Level = LogLevel.Warning, Message = "Docker container {ContainerName} ({ContainerId}) of {DockerInstanceName} was left out of resource sampling: {Reason}")] public static partial void DockerInstanceResourceSampleSkipped(this ILogger logger, Exception? exception, string dockerInstanceName, string containerName, string containerId, string reason);`

Wrapped per *Parameter Formatting*. No other API member changes; `IDockerInstanceClient` and
`ResourceStatisticsCollector` keep their signatures (the collector's change is inside a private method). Existing files
the skeleton rewrites: none beyond the production files above.

## Test files

- src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientTests.cs
- src/Tests/DockerUpdateGuard.Tests/ResourceStatisticsCollectorTests.cs

Existing test code that calls a changed signature: none. Every existing sampling test keeps one sampled container or
fails at the list, and none asserts a collector-computed instance rate, so none is adapted; AC7's two get a category
(Tester).

Files of the change no unit test can cover: none.

## Areas

- `docs/areas/docker-instances.md`: *Container discovery*, *Timeouts*, *Resource statistics*, *Limits and errors*,
  *Known gaps*, *Related decisions* (below).
- `docs/areas/telemetry.md`: *Credentials and external data* gains container IDs.

## Documentation updates

All by the Dev, once the code is green:

- `docs/areas/docker-instances.md`
  - *Container discovery*, name rule: the first listed name that is a string, without its leading `/`, or the ID.
  - *Timeouts*, second bullet: a stats call that exceeds the timeout leaves only its container out (see *Resource
    statistics*); the other requests still end *failed*.
  - *Resource statistics*, second bullet: a container is left out of the round when its stats request answers
    non-success, times out, fails at the transport level, or returns a body that is not valid JSON or not a JSON
    object; each one is logged once per round as warning 3111 with instance, container name, ID and the reason —
    `returned <status code>`, `timed out after <n> seconds`, `connection failed`, `invalid response` for a body that is
    not valid JSON (or has a string that does not decode), `unexpected error` for a body that is not a JSON object.
    New bullet: a round sends one request after the other, so with hung containers it can take up to one
    `RequestTimeoutSeconds` per container (plus the list and the host info request); rounds do not overlap
    ([Scan engine](scan-engine.md)), a long round delays the next.
  - *Resource statistics*, instance-sample bullet: count, CPU, memory and network totals are sums over the containers
    sampled in that round; the receive and transmit rates are the sums of those containers' rates (no longer computed
    against the previous instance sample); a round that requested stats for at least one container and sampled none
    fails (next bullet's rule).
  - *Limits and errors*, the bullet "A resource collection that fails for an instance …": the round fails when the
    list request fails or times out, or when no requested container could be sampled (message as AC4); no samples,
    one warning, other instances unaffected.
  - *Known gaps*: delete the #270 entry.
  - *Related decisions*: add record 0026.
- `docs/areas/telemetry.md`, *Credentials and external data*: "container and repository names" becomes "container IDs
  and names, repository names" (the 3111 entry carries the ID).
- `README.md`, `docs/ARCHITECTURE.md` (service table only, line 343): no change. No changelog file exists.

## Architecture check

Outcomes with fixed reasons (*Guarantees*, record 0009) hold: the new message is fixed text plus the configured name,
log reasons come from `FailureReason`, only the caller's cancellation leaves the method. Layering unchanged.

## Security considerations

- [x] No limits: no byte or length limit is added or changed; the logged name and ID keep their existing bounds (200
  characters) and control-character replacement.
- [x] Exceptions reaching a user-visible message: none new. The per-container exceptions reach only the log (with the
  exception, as every Engine failure log does); the AC4 result message, which can reach the logs and no page (resource
  collection writes no scan run, `scan-engine.md:70`), is fixed text plus the instance name.

## Decision records

- `docs/decisions/0026-a-failed-stats-request-drops-only-its-container-from-a-resource-round.md` (Proposed), index row
  added; covers the instance rate rule as well.

## Challenge

1. Major — the instance network rate spikes when a dropped container returns. Accepted: the instance rates become the
   sums of the round's container rates (Approach 5, AC8 with its three-round collector test, option 6 for the
   alternative). The *Affected projects* table, the area-document instance-sample bullet and record 0026 (decision and
   consequences) are updated; the statement that the collector does not change is gone.
2. Minor — the area document must match the logged reasons. Accepted: the *Resource statistics* bullet in
   *Documentation updates* now names `invalid response` for a body that is not valid JSON and `unexpected error` for one
   that is not a JSON object.
3. Minor — fact 11 settled. Accepted: fact 11 is confirmed by Security's measurement, and *Tests per criterion* forbids
   `TimeoutHttpMessageHandler` for AC1-AC4 and AC8.

## Security review

Verdict APPROVED, no `CHANGES_REQUIRED` finding. Open choice: a `Names` element that is not a string makes
`GetPrimaryName` throw, also inside the new handler. Decided: fixed in this change (Approach 1-2, AC9, fact 17) —
`GetPrimaryName` skips non-string elements, and the helper computes the logged name before its `try`. It is the same
code the Dev rewrites, a one-condition change, and it closes the last case in which one container fails the whole
round; the fix reaches discovery too, which the area document's name rule records.

## Out of scope / follow-ups

- Bounding the round's length when many containers hang (option 4): no issue proposed now; record 0026 names it.
- #80 (the collector loads the instance's whole sample history per round): same method, independent.
