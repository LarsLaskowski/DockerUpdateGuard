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

## Challenge notes

Devil's Advocate, mode plan, issue #270 (specs/issue-270/plan.md, docs/decisions/0026-a-failed-stats-request-drops-only-its-container-from-a-resource-round.md)

The plan's facts are right. I checked these myself:
- The single `try` around the list and the stats loop is at `DockerInstanceClient.cs:1112-1186`.
- `ReadResourceSampleAsync` catches `JsonException` (lines 564-576), and `RemoteJsonDocument.ParseAsync:35-39` turns a lone surrogate into `JsonException`.
- A wrong-shaped body throws: `TryGetString` (line 619) and `ParseResourceSample` (line 540) call `TryGetProperty` on a non-object.
- The `FailureReason.FromException` mapping is as stated (`FailureReason.cs:53-83`).
- 3111 is the next free event id; the last one is 3110.
- The client has no other loop that sends a request per item. The list loops at lines 988 and 1059 send no requests, so the fix has no sibling path to cover.
- Every existing sampling test either keeps one container sampled or fails at the list: lines 80, 128-155, 1080, 1202 and 1709 (the handler fails every request).

Fact 11 also holds, by reading rather than running. When the handler throws `TaskCanceledException` and the caller's token is not cancelled, `HttpClient` either passes it on or wraps it in a `TaskCanceledException` with an inner `TimeoutException`. Both are `OperationCanceledException`, so `FromException` returns `timed out after 15 seconds`.

## Objections

1. **major: once a dropped container comes back, the instance network rate spikes by that container's whole counter. Step 4 ("`ResourceStatisticsCollector` is not changed") leaves this in.**
   - **Evidence:**
     - `ResourceStatisticsCollector.cs:186-206` sets the instance `NetworkRx/TxBytesTotal` to the sum over the containers sampled this round.
     - It computes `NetworkRx/TxBytesPerSecond` against the latest instance sample with `CalculateBytesPerSecond` (lines 81-104: delta of the totals, 0 when the delta is not positive).
   - **Example:** `c1` (counter 10 GB) is dropped in round N and sampled again in round N+1.
     - Round N: instance total falls by 10 GB, so the rate is 0.
     - Round N+1: the delta against round N's sample contains `c1`'s whole 10 GB, so the chart shows about 10 GB divided by the interval.
   - **Why this is new:** today a failed round writes no instance sample, so round N+1 compares against N-1, which already included `c1`. The plan makes partial rounds normal for a hung container, which is exactly the "misleading host sample" the PM rejected in option 3.
   - **Record 0026:** its Consequences mention only understated totals. `docs/areas/docker-instances.md:153-154` ("rates against the previous instance sample by the same rule") would become wrong without anyone noticing.
   - **Fix:** the plan should pick one rule and pin it with an AC and a collector test (round 1 with `c1`-`c3`, round 2 with `c2` dropped, round 3 with `c2` back). Either:
     - compute the instance rate as the sum of the per-container rates of the round (each one is already computed against that container's own previous sample, lines 164-171, so gaps are handled); or
     - take the instance delta only over containers sampled in both rounds.
   - **Knock-on changes:** this changes the collector. The *Affected projects* table, step 4, record 0026's Consequences and the area document's instance-sample bullet need updating.

2. **minor: the WrongShape reason in AC2 is `unexpected error`, while the planned area text calls the same case "a body that … is not a stats object".**
   - **Evidence:** a root `[]` throws `InvalidOperationException` from `TryGetString` (line 619), which `FailureReason.FromException` maps to `UnexpectedError` (`FailureReason.cs:81`).
   - **Instruction for the Dev:** keep AC2 as written, but in the *Resource statistics* bullet of `docs/areas/docker-instances.md` say that a stats body that is not a JSON object is logged with the reason `unexpected error`, and a body that is not valid JSON with `invalid response`. An operator should be able to match the log reason to the documented case.

3. **minor: Fact 11 is listed as unverified; the Tester can treat it as settled.**
   - **Instruction for the Tester:** throw `TaskCanceledException` from the `DelegateHttpMessageHandler` for the stats URI of `c2` without cancelling the test token. Assert the reason `timed out after 15 seconds` and that the log entry carries the exception. Never wait on a real timer; `TimeoutHttpMessageHandler` waits on a real one-second timer, so do not reuse it for AC1, AC2 or AC3.

## Security notes

Security review, mode `plan`, issue #270. I reviewed `/home/user/DockerUpdateGuard/specs/issue-270/plan.md` and `/home/user/DockerUpdateGuard/docs/decisions/0026-a-failed-stats-request-drops-only-its-container-from-a-resource-round.md` at branch head d1d533e. I found no blocking findings.

**Tier and areas**
- `standard` is the right tier. A per-container `catch` and the rule that a round reading no stats fails are behavior changes. They create, change and remove no security control: no limit, no redaction, no TLS setting, no credential handling.
- The plan lists the two security areas the change touches: *Logging and telemetry of external data* and *Failure messages and remote text*. I found no third one.
  - *Docker Engine access*: the stats URI, `Uri.EscapeDataString` and the request set stay the same.
  - *TLS relaxation*: unchanged.
  - The stats body is still parsed by `RemoteJsonDocument`, as today.

**Logging of external data (new event 3111)**
- The container ID comes from `TryGetString` (`DockerInstanceClient.cs:619-632`), so its control characters are already replaced, and the loop guarantees it is 200 characters or fewer before any request is sent (`:1146-1150`).
- The container name follows `GetPrimaryName`, which replaces control characters (`:595-610`), and is then cut to 200 characters. `ReplaceControlCharacters` covers Cc, Cf, Zl and Zp plus lone surrogates (`ResponseBodyExcerpt.cs:76-165`), so the entry cannot be used to forge log lines.
- The ID and name of a resource sample are already listed under `docs/areas/integration-failures.md`, *Remote text fields*. The plan's change to `telemetry.md` ("container IDs and names") brings that document in line.
- The `returned <status>` reason reads no response body, so no remote text goes into it.
- 3111 carries the exception, as the warnings 3107–3110 already do. Every exception it can carry (stats-body `JsonException`, `InvalidOperationException` from `TryGetProperty`, `OverflowException`, transport errors) could already reach 3107 today through the round's `catch (Exception)`. That is no new kind of exposure, and `telemetry.md` already allows "the exception with its message" on failed integration calls.
- None of the new log arguments matches the sensitive-name list: instance name, container name, container ID, reason.

**Failure messages and remote text**
- The AC4 result message is fixed text plus the configured instance name.
- The 3111 reasons come from `FailureReason.FromException` (`FailureReason.cs:53-83`, all fixed text) or are a status code.
- `FailureReason.cs` is unchanged. Caller cancellation is still rethrown through the `when (cancellationToken.IsCancellationRequested)` filter, so the only exception that leaves the method is still the caller's cancellation (Guarantees, record 0009).

**Bypass forms of the new per-container guard**
The guard is a catch-all that is not under any security area, so no input form gets around it. These are all covered by the helper's `catch (Exception)`:
- malformed JSON
- a string that does not decode (re-raised as `JsonException` by `RemoteJsonDocument`)
- a non-object root
- a non-object `cpu_stats`
- `decimal` overflow
- a body that breaks off
- the timeout

**Resource bound**
The plan claims no bound. The longer round (up to one `RequestTimeoutSeconds` per hung container) is stated in record 0026 and in the planned area-doc bullet. Rounds don't overlap (`scan-engine.md`), and each stats timeout source is created and disposed per iteration, so nothing extra is held across containers.

**Measurement: fact 11, the test double for a real timeout**
The plan listed this as unverified, so I ran it. A console program in `/tmp/claude-0/-home-user-DockerUpdateGuard/bb46b5ad-cc47-5a12-b40f-9a6249d15b9d/scratchpad/m11/Program.cs` sends an `HttpClient` request with a linked, not-cancelled caller token and `CancelAfter(200 ms)` through two handlers:
- one throws `TaskCanceledException` (the planned test double);
- one waits on `Task.Delay(Infinite, ct)` (a real timeout).

Command: `dotnet run` in that folder (SDK 10.0.404). Output:
```
double-TaskCanceled: System.Threading.Tasks.TaskCanceledException isOCE=True callerCancelled=False inner=
real-timeout: System.Threading.Tasks.TaskCanceledException isOCE=True callerCancelled=False inner=TaskCanceledException
```
Both reach the client as an `OperationCanceledException` while the caller token is not cancelled. They therefore take the same path, `catch (Exception)` → `FromException` → `timed out after 15 seconds`. Fact 11 holds; the double matches the real component.

**Non-blocking finding (correctness rather than security)**
- **Defect:** if a listed container's `Names` array holds a non-string element (e.g. `"Names":[1]`), `GetPrimaryName` throws `InvalidOperationException` from `JsonElement.GetString()` (`DockerInstanceClient.cs:601`). This happens both inside `ParseResourceSample` and again when the helper's `catch` works out the name for 3111. The second throw escapes the helper, and the whole round fails with 3107, which is the same as today. There is no exposure, but it is a leftover case where one container still fails the whole round.
- **Smallest fix:** in the helper, compute the logged name once before the `try` with a non-throwing fallback to the ID, or filter `Names` items on `ValueKind == JsonValueKind.String`.
- **My recommendation:** fix it now if the Dev touches that code anyway; otherwise open a linked issue. This is the Lead's call in step 8.
