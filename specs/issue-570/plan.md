# Plan: Read Docker Engine stats and host memory numbers only from the JSON kinds that hold them

Source: Issue #570
Tier: standard — changes what a resource round records for a stats body with an unreadable value (kept with that value as 0 instead of left out) and what host info answers; no security control is created or changed.
Security areas touched: Docker Engine access (input handling of the Engine's stats and info JSON in `Docker/DockerInstanceClient.cs`)

## Problem / root cause

`System.Text.Json.JsonElement.TryGetProperty` throws `InvalidOperationException` on an element that is not an object,
and `TryGetInt64`/`TryGetInt32` on one that is not a number. `DockerInstanceClient` calls them unguarded on Engine JSON:

- the private `TryGetInt64(JsonElement, params string[])` (`DockerInstanceClient.cs:714–727`), used for CPU
  (`:611–614`), memory (`:647`, `:659`) and host memory (`:1416`), walks the path without an object check and reads the
  value without a number check;
- `ParseNetworkTotal` (`:681–682`) calls `TryGetProperty` on each `networks` entry and `TryGetInt64` on its value
  unchecked;
- related, found while planning: `ParseCpuPercent` reads `online_cpus` with `TryGetProperty` + `TryGetInt32` unchecked
  (`:623–624`), and `GetPerCpuCount` calls `TryGetProperty` on `cpu_stats` and `cpu_usage` unchecked (`:698–699`).

The issue's claim that nothing catches the exception is refuted: `TryReadContainerSampleAsync` catches it (`:1103`), so
the container is **left out of the round** (warning 3111, reason `unexpected error`), and with every container left
out the round fails (`:1151–1153`); `GetHostMemoryTotalAsync` catches it (`:1427`) and answers *failed*
(`Docker host info request failed for '<name>': unexpected error`, warning 3110). No crash, but one unreadable counter
discards all readable values of its container.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | The helper and its call sites are where the issue says | `DockerInstanceClient.cs:611–614, 647, 659, 714–727, 1416`; network read at `:681–682` (issue: 680–681) | confirmed |
| 2 | `TryGetProperty` throws `InvalidOperationException` when `ValueKind` is not `Object`; `TryGetInt64`/`TryGetInt32` throw it when `ValueKind` is not `Number` and return `false` for a number that does not fit | .NET API reference of `JsonElement` (Exceptions sections of these members) | confirmed |
| 3 | "Nothing in the file catches `InvalidOperationException`" | `catch (Exception)` at `:1103` (container left out, 3111) and `:1427` (host info *failed*, 3110) | refuted |
| 4 | A stats body that is not a JSON object is left out with `unexpected error`, documented and tested | `ParseResourceSample` reads `read` first through `TryGetString` (`:475`, `:548–553`: only `Undefined` is guarded, so `[]` throws); `docs/areas/docker-instances.md:162–166`; test `…WhenOneStatsRequestFailsLogsSkippedContainerAsync` row `WrongShape` (body `[]`) | confirmed |
| 5 | The collector treats a *failed* and a 0 host memory alike | `ResourceStatisticsCollector.cs:167–169` | confirmed |
| 6 | After a lowered network total the next rate spikes (computed against the previous stored total) | `ResourceStatisticsCollector.cs:129–136`, `docs/areas/docker-instances.md:178–180` | confirmed |
| 7 | No test reads CPU percent, memory usage or memory limit of a sample today | `grep CpuPercent\|MemoryUsageBytes\|MemoryLimitBytes` in `DockerInstanceClientTests.cs`: no hit | confirmed |
| 8 | The Engine API documents the stats body as in `engine-stats-sample.json` (this folder) | `ContainerStats` 200 example of `api/swagger.yaml` at moby `v20.10.24` (API v1.41, the version the client calls), converted to JSON unchanged; values: CPU delta 121359, system delta 729814450000000, online CPUs 4, `percpu_usage` 4 entries, memory usage 6537216, limit 67108864, rx 5338 + 4641, tx 648 + 690 | confirmed |
| 9 | With the sample as documented the CPU percent rounds to 0 | 121359 ÷ 729814450000000 × 4 × 100 ≈ 6.7e-8 → `Math.Round(…, 4)` = 0 | confirmed |

The sample is the vendor's published example, no live capture: the change does not alter how a valid number is read,
only adds the kinds a real Engine never sends (it sends unsigned integers), so the cases are mutations of the sample.
It holds no private values.

## Acceptance criteria

The *base sample* is `engine-stats-sample.json` with `precpu_stats.system_cpu_usage` set to `739306588786410`
(system delta 1213590, so the CPU percent is 121359 ÷ 1213590 × 4 × 100 = **40**); read from it: memory usage 6537216,
memory limit 67108864, network rx 9979, tx 1338. "Sampled" means the container's sample is in the succeeded result and
no warning 3111 is logged.

- AC1: A base sample in which one read value is replaced by a non-number JSON value is sampled; that value counts as 0
  and the others are read: `cpu_stats.cpu_usage.total_usage` = `"100215355"` → CPU 0; `cpu_stats.system_cpu_usage` =
  `true` → CPU 0; `memory_stats.usage` = `"6537216"` → usage 0, limit 67108864; `memory_stats.limit` = `null` → limit 0,
  usage 6537216; `networks.eth0.rx_bytes` = `"5338"` → rx 4641, tx 1338; `networks.eth5.tx_bytes` = `{}` → tx 648,
  rx 9979.
- AC2: A base sample in which a member on a read path is replaced by a non-object JSON value is sampled; the values
  under it count as 0 and the others are read: `memory_stats` = `[]` → usage 0, limit 0, CPU 40; `networks.eth0` = `5`
  → rx 4641, tx 690; `cpu_stats` = `"x"` → CPU 0, memory read; `cpu_stats.cpu_usage` = `1` → CPU 0.
- AC3: A base sample whose `cpu_stats.online_cpus` is not a number (`"2"`) is sampled and its CPU count falls back to
  the `percpu_usage` length: CPU 40 (not 20).
- AC4: `GetHostMemoryTotalAsync` on an info answer whose `MemTotal` is not a number (`"8589934592"`, `null`) or whose
  root is not a JSON object (`[]`, `"x"`) answers *succeeded* with 0 and logs no warning 3110.

Unchanged, by an existing test: a stats body that is not a JSON object is still left out with `unexpected error`
(fact 4).

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `DockerInstanceClientStatsValuesTests.DockerInstanceClientCollectContainerResourceUsageAsyncWhenStatsValueIsNoNumberCountsItAsZeroAsync` (one `DataRow` per AC1 case: path, replacement JSON, expected CPU, usage, limit, rx, tx) | `src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientStatsValuesTests.cs` |
| AC2 | `DockerInstanceClientStatsValuesTests.DockerInstanceClientCollectContainerResourceUsageAsyncWhenStatsMemberIsNoObjectCountsItsValuesAsZeroAsync` (one `DataRow` per AC2 case) | same file |
| AC3 | `DockerInstanceClientStatsValuesTests.DockerInstanceClientCollectContainerResourceUsageAsyncWhenOnlineCpusIsNoNumberUsesPerCpuCountAsync` | same file |
| AC4 | `DockerInstanceClientStatsValuesTests.DockerInstanceClientGetHostMemoryTotalAsyncWhenMemTotalIsUnreadableReturnsZeroAsync` (one `DataRow` per AC4 body) | same file |

Every test fails on today's code: the container is left out (empty data, warning 3111) or host info is *failed*.

## Approach

All changes are private to `DockerInstanceClient` (region *Static methods*/*Methods* as the members stand today):

1. **One shared path reader**, a new private static `FindNestedProperty(JsonElement element, params string[]
   propertyNames)` returning `JsonElement?`: for each name it returns `null` unless the current element's `ValueKind`
   is `Object` and `TryGetProperty` finds the name; it returns the element at the end of the path. No `out` parameter.
2. **`TryGetInt64(JsonElement, params string[])`** keeps its signature and returns the value only when
   `FindNestedProperty` finds it, its `ValueKind` is `Number` and `TryGetInt64` succeeds; otherwise 0.
3. **`ParseNetworkTotal`**: the loop adds `TryGetInt64(networkProperty.Value, propertyName)` per entry (the `networks`
   object check stays).
4. **`ParseCpuPercent`**: `online_cpus` is read through `FindNestedProperty(cpuStats, "online_cpus")` with a `Number`
   check before `TryGetInt32`; the `cpu_stats`/`precpu_stats` lookups on the stats root stay as they are.
5. **`GetPerCpuCount`**: `FindNestedProperty(cpuStats, "cpu_usage", "percpu_usage")` replaces the two unchecked
   lookups; the `Array` check stays.

The stats root keeps today's handling: its first read (`TryGetString(statsElement, "read")`) still throws for a
non-object body, which leaves the container out as documented (fact 4, decision 0026). A number that is not a 64-bit
(online CPUs: 32-bit) integer stays 0 as today. No log entry is added for an unreadable value (decision 0068).

No new user-visible output. Mutating the sample in the tests: parse the constant with `System.Text.Json.Nodes.JsonNode`,
replace the node at the case's path with the case's JSON, serialize; one private helper in the test class.

Early draft PR: no

## Options considered

The options of [decision 0068](../../docs/decisions/0068-an-unreadable-docker-engine-stats-or-host-memory-number-counts-as-missing.md):
keep leaving the container out by an explicit check (loses all readable values of the container), leave out only for
unreadable but keep for missing values (two outcomes for the same information), and answer host info *failed* for a
non-object root (changes only a log entry, the collector takes both as 0). Implementation alternative: object checks
added inline at each call site instead of one path reader — rejected, the same guard would stand in four places and the
quality gate's duplication condition counts it.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Docker/DockerInstanceClient.cs` | new private `FindNestedProperty`; `TryGetInt64`, `ParseNetworkTotal`, `ParseCpuPercent`, `GetPerCpuCount` read through it |
| DockerUpdateGuard.Tests | `DockerInstanceClientStatsValuesTests.cs` | new test class |

## Signatures (for the Dev's skeleton)

None: the change adds no API; the new member is private and described in *Approach*. No skeleton.

## Test files

`src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientStatsValuesTests.cs` (new, class
`DockerInstanceClientStatsValuesTests`, its own `TestContext`, `CreateInstanceOptions` and HTTP client helpers as
*Layout* in `docs/UNIT_TESTS.md` asks for a split class). It holds the content of `specs/issue-570/engine-stats-sample.json`
verbatim as a raw string constant (the folder is removed before the PR) and serves list and stats answers with
`DelegateHttpMessageHandler` and `ResourceSamplingEngineHandlerFactory.CreateJsonResponse` (`Helper/`), as the existing
sampling tests do.

Existing test code that calls a changed signature: none. Existing tests kept unchanged: the `WrongShape` (`[]`) and
`InvalidJson` rows of the sampling tests (root not an object or not JSON: handling unchanged), and
`…GetHostMemoryTotalAsyncWhenEngineAnswersReturnsMemTotalAsync` (a number `MemTotal`: unchanged).

Files of the change no unit test can cover: none.

## Areas

`docs/areas/docker-instances.md` (3131 words, +60 words), *Resource statistics*: under *Per container sample*, a
sentence that a value that is missing, not a JSON number fitting a 64-bit integer, or under a member that is not a JSON
object counts as 0 and does not leave the container out; the CPU count fallback also applies when the online CPU count
is not a number; host memory is 0 "when that request fails or its answer holds no readable total"; *Related decisions*
gets 0068.

## Documentation updates

- `docs/areas/docker-instances.md`: as *Areas* (Dev, after the code is green).
- `docs/ARCHITECTURE.md`: none — the sweep for "stats", "MemTotal" and the client's enumerations found no statement the
  change alters. `README.md`: none (no configuration). No changelog in this repository.

## Architecture check

The guarantee "integration calls answer expected failures with an outcome and a fixed reason" is kept: no message or
reason changes, host info answers *succeeded* with 0 where it answered *failed*. Layering and the client's interface
are untouched.

## Security considerations

- [x] No limits: the change adds no byte or length limit.
- [x] Exceptions reaching a user-visible message: fewer — the `InvalidOperationException` of these reads no longer
  occurs; the remaining ones (non-object stats body) still reach only the fixed reason `unexpected error`.

## Decision records

- `docs/decisions/0068-an-unreadable-docker-engine-stats-or-host-memory-number-counts-as-missing.md` (Proposed;
  amends the released 0026)

## Out of scope / follow-ups

- The stats body that is not a JSON object (left out, decision 0026).
- Proposed follow-up issue, title "[Docker] A container list or image history item that is not a JSON object fails
  the whole Engine call", body: "`ParseContainers` (`DockerInstanceClient.cs:953–960`), `ReadContainerSamplesAsync`
  (`:1131–1133`) and the history loop (`:926`) call `TryGetString` on each array item, which guards only
  `JsonValueKind.Undefined` (`:548`); an item that is a string or number throws `InvalidOperationException`, and the
  catch of the call turns the whole discovery, resource round or history into *failed* with `unexpected error`.
  Expected: decide whether such an item is skipped like an item without an ID, and cover it with tests. Found while
  planning #570."
