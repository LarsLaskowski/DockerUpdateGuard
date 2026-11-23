# 0057: Currently known containers are resolved by one query in the data layer

- **Status:** Accepted
- **Date:** 2026-11-23
- **Area:** Docker instances
- **Source:** Issue #294
- **Supersedes:** —

## Context

Which containers are current — per Docker instance the snapshots of its most recent runtime scan run that recorded
snapshots, else the latest snapshot of each container — was decided twice: in the host's view service for the pages and
the dashboard, and in the live-image inventory that cleanup and the vulnerability refresh use. The inventory gauge
`dockerupdateguard.runtime_containers` used neither and counted every container that still had a snapshot, so it
drifted far above what the pages showed once containers were recreated. [0054](0054-a-base-image-is-shared-from-two-users-among-enabled-observed-images-and-their-containers.md)
had kept the selection in the host for the same reason: a second copy could drift.

The three consumers hold their database context differently: the telemetry is a singleton that receives a context per
refresh, the view service serializes its scoped context behind a gate, and the live-image inventory is a scoped data
service. The data-layer tests run on SQLite, which does not compare or order `DateTimeOffset` values in SQL.

## Options considered

1. **A method on the live-image inventory service** — mixes "which containers" into "which image versions", and the
   singleton telemetry and the view service would each need the service bound to their own context.
2. **A new query service in dependency injection** — the singleton telemetry cannot take a scoped service, and the
   view service's constructor would change in every test that builds it, for an interface nothing substitutes.
3. **One composable server-side query with correlated subqueries** — counts both rows of a tie, compares times in SQL,
   which the SQLite tests cannot translate, and scans the whole history table per row on every refresh.
4. **Fix only the gauge's count** — leaves three definitions that can drift apart.
5. **One static key-only query in the data layer, run over the caller's context** — chosen.

## Decision

`CurrentContainerSnapshotQuery` in the data layer is the only definition of the currently known containers; the view
service, the live-image inventory and the inventory gauge all read it over their own context. It projects keys only and
compares times on the client, breaking ties deterministically. The rule is stated in
[Docker instances](../areas/docker-instances.md#container-snapshots).

## Consequences

- The gauge, the dashboard, the Runtime Containers page and the live-image set cannot disagree for the same data.
- The view service loads full snapshots in a second read by the ids the query returns.
- The live-image inventory no longer loads every snapshot row; the fallback for instances without a runtime-scan
  snapshot still loads all of those instances' snapshots, which only retained snapshots of deleted runs produce.
- A consumer that needs the set injected or substituted would turn the static query into a service.
