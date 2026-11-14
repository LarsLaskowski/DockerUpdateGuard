# 0036: One container action at a time per container, guarded in the process

- **Status:** Proposed
- **Date:** 2026-11-14
- **Area:** Container actions
- **Source:** Issue #280
- **Supersedes:** —

## Context

Two browser tabs, or two people, can confirm actions on the same container at once. Disabled buttons only protect one
page. The guard must also not block a container forever when a run's final status could not be written.

## Options considered

1. **A process-wide set of containers with an action in progress, held by a singleton action service** — atomic,
   needs no schema change, and released in a `finally` whatever the database does. Chosen.
2. **A non-terminal action run in the database as the lock (checked before the insert)** — check and insert race
   without a lock of their own, and a run whose terminal write failed blocks the container until the next startup.
3. **A partial unique index on non-terminal runs per container** — atomic across instances, but needs a migration and
   a container ID column, and shares the stale-row problem of option 2.

## Decision

The action service keeps the containers with an action in progress in a process-wide set; a confirmation for a
container in the set is recorded as *failed* without contacting Portainer. Behavior: [Container
actions](../areas/container-actions.md#action-runs).

## Consequences

- The guard holds for one application process; the scan engine's single-instance assumption applies to it as well.
- The action service is a singleton that creates its own scope for each database step, which also lets an action
  outlive the browser connection that started it.