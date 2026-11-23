# Plan: Gauge dockerupdateguard.runtime_containers counts only the currently known containers

Source: Issue #294
Tier: standard — changes the value a metric reports and moves the "currently known containers" rule of the view service and the live-image inventory into one shared data-layer query, without creating, changing or removing any security control.
Security areas touched: none

## Problem / root cause

`ApplicationTelemetry.RefreshInventoryMetricsAsync` (`src/DockerUpdateGuard/ApplicationTelemetry.cs:142-149`) sets
`_runtimeContainers` to the number of distinct `(DockerInstanceId, ContainerId)` pairs over **all** stored
`ContainerSnapshots`. Snapshots are append-only history that stays until cleanup removes it, so every container that was
removed or recreated (new container ID) keeps counting. The "currently known containers" rule — per instance the
snapshots of its most recent `RuntimeContainer` scan run that recorded snapshots, else the latest snapshot per
container — exists twice, in `ApplicationViewService.GetLatestContainerSnapshotsAsync`
(`src/DockerUpdateGuard/UI/ApplicationViewService.cs:662-718`) and in `LiveImageInventoryQueryService.GetRunningImageVersionIdsAsync`
(`src/DockerUpdateGuard.Data/Queries/LiveImageInventoryQueryService.cs:41-81`), and the gauge uses neither.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | The gauge counts distinct `(DockerInstanceId, ContainerId)` over all snapshots | `ApplicationTelemetry.cs:142-149` | confirmed |
| 2 | The gauge is registered as an observable gauge reading a field, so observing it never queries the database | `ApplicationTelemetry.cs:94` | confirmed |
| 3 | Refresh points: startup, observed-image scan, runtime-container scan, vulnerability refresh, cleanup | `ApplicationInitializationExtensions.cs:70`, `ImageScanOrchestrator.cs:663`, `RuntimeContainerScanOrchestrator.cs:809`, `VulnerabilityEnrichmentService.cs:584`, `ScanCleanupBackgroundService.cs:342` | confirmed |
| 4 | The UI rule: latest runtime scan run (by `ScanRun.StartedAtUtc`) among those with snapshots of the instance; instances without any runtime-scan snapshot take the latest snapshot per container (server-side `NOT EXISTS` on a later `RecordedAtUtc`) | `ApplicationViewService.cs:662-718` (the issue's `:1083` is stale) | confirmed |
| 5 | The live-image rule is the same, computed client-side over all snapshot rows (`ScanRunStartedAtUtc ?? RecordedAtUtc` is always `StartedAtUtc` for a runtime run, since the type is only non-null with a scan run) | `LiveImageInventoryQueryService.cs:43-78` | confirmed |
| 6 | The two existing copies differ only in how they break ties (equal start times, equal recording times): both pick an arbitrary one | reading both | confirmed |
| 7 | The dashboard count is `latestSnapshots.Count` of `GetLatestContainerSnapshotsAsync`; the Runtime Containers page lists the same snapshots | `ApplicationViewService.cs:1913-1919`, `:410-436` | confirmed |
| 8 | No consumer of `GetLatestContainerSnapshotsAsync` depends on its order: every list is re-sorted, the others count, group or build dictionaries by snapshot id | `:315-322`, `:428-436`, `:454-457`, `:505`, `:1165`, `:1445-1456`, `:1869`, `:1995` | confirmed |
| 9 | Discovery lists stopped containers too (`containers/json?all=1`), so a stopped container that still exists stays current | `Docker/DockerInstanceClient.cs:1180` (the issue's `:996` is stale) | confirmed |
| 10 | Cleanup deletes snapshots older than the cutoff only without active update finding and without container action run | `Images/ScanCleanupBackgroundService.cs:315-319` (the issue's `:231-236` is stale) | confirmed |
| 11 | Deleting a scan run sets `ContainerSnapshot.ScanRunId` to null, so retained old snapshots lose their run | `ContainerSnapshotConfiguration.cs:76-79` | confirmed |
| 12 | `ApplicationTelemetry` is a singleton that gets the caller's `DbContext` per refresh; it cannot take a scoped query service | `ServiceCollectionExtensions.cs:134`, signature at `ApplicationTelemetry.cs:135` | confirmed |
| 13 | `ApplicationViewService` is constructed in 38 test call sites and `LiveImageInventoryQueryService` in 26; a changed constructor would touch all of them | `grep "new ApplicationViewService\|new LiveImageInventoryQueryService" src/Tests` | confirmed |
| 14 | `DockerUpdateGuard.Data.Tests` runs on SQLite, whose EF Core provider does not translate `<`/`>` comparisons or `ORDER BY` on `DateTimeOffset`; the existing `LiveImageInventoryQueryServiceTests` therefore need a shared query that compares and orders times client-side | to verify: the Tester's `CurrentContainerSnapshotQueryTests` and the unchanged `LiveImageInventoryQueryServiceTests` pass on SQLite in step 6 (basis: the EF Core SQLite provider's documented limitations; `SqliteTestDatabase.cs:49` configures no converter) | unverified |
| 15 | SQLite translates `SELECT DISTINCT` over a `DateTimeOffset` column and `Contains` over a `List<Guid?>` | to verify: the same tests in step 6 (Tester); if it does not, the Dev takes the distinct on the client over the same projection | unverified |
| 16 | `InternalsVisibleTo("DockerUpdateGuard.Tests")` exists for the host project | `src/DockerUpdateGuard/AssemblyInfo.cs:3` | confirmed |
| 17 | A `MeterListener` cannot single out the instance under test: every `ApplicationTelemetry` ever constructed registers its own gauge of the same name on the static meter (`DockerUpdateGuardTelemetry.Meter`) and stays reachable through its callback | `ApplicationTelemetry.cs:93-98`, dozens of `new ApplicationTelemetry()` in the tests | confirmed |
| 18 | Decision record 0054 (released in v1.3.10) states in its context that the current-container selection lives in the host's view service; its decision (the sharing threshold) is unaffected | `git tag --contains` on its commit | confirmed |

## Acceptance criteria

- AC1: For one Docker instance with snapshots of an older runtime scan run (containers A, B, C) and a newer runtime scan
  run (containers A, D), the `dockerupdateguard.runtime_containers` value after `RefreshInventoryMetricsAsync` is 2.
- AC2: For two instances — instance 1 as in AC1, instance 2 with only snapshots that belong to no runtime scan run
  (container X twice at different times, container Y once) — the value is 4, and the shared query returns exactly the
  keys (1, A), (1, D), (2, X with its newest snapshot), (2, Y).
- AC3: With no snapshots the value is 0.
- AC4: For the AC2 data, the gauge value equals `GetDashboardSummaryAsync().RuntimeContainerCount` and the number of
  entries of `GetRuntimeContainersAsync()`.
- AC5: The live-image inventory keeps its behavior: `LiveImageInventoryQueryServiceTests` pass unchanged.
- AC6: `docs/areas/telemetry.md` states the new gauge definition and no longer lists the #294 known gap.
- AC7: The *Coverage gate* passes.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `ApplicationTelemetryRefreshInventoryMetricsCountsOnlyLatestRuntimeScanContainersAsync` | `src/Tests/DockerUpdateGuard.Tests/ApplicationTelemetryTests.cs` |
| AC2 | `ApplicationTelemetryRefreshInventoryMetricsInstanceWithoutRuntimeScanUsesLatestSnapshotPerContainerAsync`; `CurrentContainerSnapshotQueryMixedInstancesReturnsLatestRuntimeScanAndLatestSnapshotPerContainerAsync` | `ApplicationTelemetryTests.cs`; `src/Tests/DockerUpdateGuard.Data.Tests/CurrentContainerSnapshotQueryTests.cs` |
| AC3 | `ApplicationTelemetryRefreshInventoryMetricsNoSnapshotsReportsZeroAsync` (passes on today's code: deferred to step 6) | `ApplicationTelemetryTests.cs` |
| AC4 | `ApplicationTelemetryRefreshInventoryMetricsMatchesDashboardRuntimeContainerCountAsync` | `ApplicationTelemetryTests.cs` |
| AC5 | existing `LiveImageInventoryQueryServiceTests` (all three), unchanged | `src/Tests/DockerUpdateGuard.Data.Tests/LiveImageInventoryQueryServiceTests.cs` |
| AC6 | check: the Reviewer reads `docs/areas/telemetry.md` in step 8 | — |
| AC7 | CI: *Coverage gate* in step 7's stamp | — |

The telemetry tests read the value through `ApplicationTelemetry.RuntimeContainerCount` (fact 17), run on EF Core
InMemory with a fresh database name per test, and each assert has a message. Runtime scan runs get distinct
`StartedAtUtc` values from a fixed base time (no real clock). The AC2 query test asserts on the returned keys and the
snapshot id of X's newest snapshot.

## Approach

Early draft PR: no

1. **Shared query** — new static class `DockerUpdateGuard.Data.Queries.CurrentContainerSnapshotQuery` with
   `GetCurrentContainerSnapshotsAsync`. It projects keys only (no `Include`, no tracking) in three reads:
   1. distinct `(DockerInstanceId, ScanRunId, ScanRun.StartedAtUtc)` of snapshots whose scan run has type
      `RuntimeContainer` (the UI's first query today);
   2. on the client, per instance the run with the latest `StartedAtUtc`, ties broken by the higher `ScanRunId`; then the
      `(Id, DockerInstanceId, ContainerId, ImageVersionId, ScanRunId)` of snapshots in those runs, keeping a row only
      when its run is the target of its own instance;
   3. for instances not in step 2, `(Id, DockerInstanceId, ContainerId, ImageVersionId, RecordedAtUtc)` of all their
      snapshots, and on the client per `(DockerInstanceId, ContainerId)` the one with the latest `RecordedAtUtc`, ties
      broken by the higher `Id`.
   Times are compared and ordered only on the client (fact 14). Steps 2 and 3 are two private static methods, so each
   stays within the cognitive-complexity rule. The result rows are `CurrentContainerSnapshotData`. The fallback read
   loads every snapshot of instances without a runtime-scan snapshot; such snapshots exist only where a run was
   deleted (fact 11) or no runtime scan produced them, so this is no larger than what `LiveImageInventoryQueryService`
   loads today (all snapshots).
2. **Gauge** — `RefreshInventoryMetricsAsync` keeps its signature and call sites; `_runtimeContainers` becomes the
   `Count` of the shared query's result over the passed `dbContext`. The gauge callback reads the new internal property
   `RuntimeContainerCount`, which returns `_runtimeContainers`, so the tests read exactly what the gauge reports. The
   other five gauges, the log event 1010 and the refresh points stay as they are.
3. **View service** — `GetLatestContainerSnapshotsAsync` calls the shared query with `_dbContext`, then loads the full
   snapshots with today's three `Include`s where `Id` is in the returned ids, `AsNoTracking`, ordered by
   `RecordedAtUtc` descending (order not load-bearing, fact 8). It stays private and inside the existing
   `ExecuteSerializedAsync` gate of its callers, so the serialized use of the context is unchanged.
4. **Live inventory** — `GetRunningImageVersionIdsAsync` is replaced by the distinct `ImageVersionId`s of the shared
   query's result over `_dbContext`; the constructor and the interface are unchanged.

No new user-visible output. No configuration key, no migration, no DI registration.

## Options considered

1. **A method on `ILiveImageInventoryQueryService`** (the issue's first suggestion) — that service answers "which image
   versions are live" and is resolved from DI; the singleton telemetry (fact 12) and the view service would each need
   it bound to their own context, and the view service's constructor would change in 38 test call sites (fact 13).
2. **A new `IRuntimeContainerInventoryQueryService` registered in DI** — same constructor churn, and the telemetry
   could still not inject a scoped service; the interface would have no substitute anywhere (all three consumers are
   tested against real databases).
3. **Change `RefreshInventoryMetricsAsync` to take the query service** — five production call sites and their tests
   change for nothing a static query over the passed context does not give.
4. **One composable server-side `IQueryable<ContainerSnapshot>`** (correlated `NOT EXISTS` for "no later runtime run" and
   "no later snapshot") — counts both rows of a tie, compares `DateTimeOffset` in SQL, which the SQLite tests cannot
   translate (fact 14), and runs correlated subqueries over the whole history table on every refresh.
5. **Count server-side in the telemetry only, leave the two copies** — fixes the number but keeps three definitions
   that can drift apart, which is how this bug arose.

Chosen: a static key-only query in the data layer over the caller's context (decision record 0057).

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard.Data | `Queries/CurrentContainerSnapshotQuery.cs` | new: the shared rule |
| DockerUpdateGuard.Data | `Queries/CurrentContainerSnapshotData.cs` | new: result row |
| DockerUpdateGuard.Data | `Queries/LiveImageInventoryQueryService.cs` | uses the shared query; private `GetRunningImageVersionIdsAsync` removed |
| DockerUpdateGuard | `ApplicationTelemetry.cs` | gauge value from the shared query; internal `RuntimeContainerCount` |
| DockerUpdateGuard | `UI/ApplicationViewService.cs` | `GetLatestContainerSnapshotsAsync` uses the shared query |

## Signatures (for the Dev's skeleton)

- `public static class CurrentContainerSnapshotQuery` (namespace `DockerUpdateGuard.Data.Queries`), region `Static methods`:
  `public static async Task<IReadOnlyList<CurrentContainerSnapshotData>> GetCurrentContainerSnapshotsAsync(DockerUpdateGuardDbContext dbContext, CancellationToken cancellationToken)`
  — throws `ArgumentNullException` for a null `dbContext`.
- `public class CurrentContainerSnapshotData` (same namespace), region `Properties`:
  `public Guid SnapshotId { get; set; }`, `public Guid DockerInstanceId { get; set; }`,
  `public string ContainerId { get; set; } = string.Empty;`, `public Guid ImageVersionId { get; set; }`.
- `ApplicationTelemetry`, new region `Properties`: `internal long RuntimeContainerCount => _runtimeContainers;`
  — real in the skeleton (it only reads the field); the gauge callback switches to it in the skeleton too.

All names checked against *Writing code* in `.squad/stack.md`: no nested type, at most two parameters, no keyword of
another .NET language, no `Flags`/`Enum` suffix. The private `GetRunningImageVersionIdsAsync` of
`LiveImageInventoryQueryService` is removed in step 6; `GetLatestContainerSnapshotsAsync` stays private with its
signature. The skeleton holds nothing for real besides `RuntimeContainerCount`.

## Test files

- New: `src/Tests/DockerUpdateGuard.Tests/ApplicationTelemetryTests.cs`,
  `src/Tests/DockerUpdateGuard.Data.Tests/CurrentContainerSnapshotQueryTests.cs` (Tester).
- Existing test code that calls a changed signature: none — no public or internal signature changes.
- Kept unchanged: `ApplicationViewServiceTests`, `ApplicationViewServiceVulnerabilityOverviewTests`,
  `LiveImageInventoryQueryServiceTests`, `ScanCleanupBackgroundServiceTests`, `VulnerabilityEnrichmentServiceTests` —
  their data has no tie of start or recording times, the only input the shared rule decides differently (fact 6).
- Files no unit test can cover: none.

## Areas

- `docs/areas/telemetry.md` (-30 words): the `runtime_containers` row; the #294 *Known gaps* entry removed; 0057 under
  *Related decisions*.
- `docs/areas/docker-instances.md` (+60 words): the *current* containers bullet of *Container snapshots* also states
  the rule for an instance without a runtime scan (the latest snapshot of each container) and that this one set is
  what the pages, the dashboard, the live-image inventory and the `runtime_containers` gauge use; 0057 under *Related
  decisions*.

## Documentation updates

All by the Dev, once the code is green:

- `docs/areas/telemetry.md`, *Metrics*: `dockerupdateguard.runtime_containers` — number of currently known runtime
  containers, the set the dashboard and the Runtime Containers page show, linking
  [Docker instances](docker-instances.md#container-snapshots); stopped containers that still exist count,
  removed ones do not. *Known gaps*: delete the #294 bullet. *Related decisions*: add 0057.
- `docs/areas/docker-instances.md`, *Container snapshots* and *Related decisions*: as under *Areas*.
- `docs/ARCHITECTURE.md` §5.2: the `Queries\` bullet names `CurrentContainerSnapshotQuery` — the shared definition of
  the currently known containers, read over the caller's context by the view service, the live-image inventory and
  the inventory gauge — and links [0057](decisions/0057-currently-known-containers-are-resolved-by-one-query-in-the-data-layer.md).
- Sweep, no change needed: `docs/areas/image-catalog.md:91`, `vulnerabilities.md:42`, `scan-engine.md:180`,
  `base-images.md:70`, `web-ui.md:43,45` already state the same rule; `README.md`, `INSTALL.md` do not name the gauge;
  no changelog exists; the PR template asks for nothing more.

## Architecture check

The data layer still references neither host nor telemetry; the host calls a data-layer query, as it does with the two
query services (§5.2, updated). The guarantees that observing a gauge never queries the database and that gauges
refresh only at the five points stay as they are (facts 2, 3). No guarantee of `.squad/project.md` is weakened. The
security area *Database and migrations* covers queries built from user input; this query takes none.

## Security considerations

- [x] No limits: the change parses no input.
- [x] None: no new exception reaches a user. A failing query raises what the callers already handle (startup
  initialization, the scan runs' and cleanup's existing error paths, the view service's page errors).

## Decision records

- `docs/decisions/0057-currently-known-containers-are-resolved-by-one-query-in-the-data-layer.md` (Proposed)

## Out of scope / follow-ups

- A Docker instance whose latest runtime scan recorded no snapshot keeps its previous set as current
  (`docker-instances.md`, by design); the gauge follows the pages there.
- `ContainerActionService` decides "in the latest runtime scan" for one container with its own query
  (`Portainer/ContainerActionService.cs:193-200`); it answers a different question (one container's eligibility) and
  agrees with the rule except on ties — not changed here.
- Query cost of #80/#81 stays as it is; the live-image inventory now loads fewer rows than before.
