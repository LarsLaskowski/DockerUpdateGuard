# 0035: Action runs are an audit history written before Portainer is contacted

- **Status:** Proposed
- **Date:** 2026-11-14
- **Area:** Container actions
- **Source:** Issue #280
- **Supersedes:** —

## Context

The application has no sign-in (a guarantee in `.squad/project.md`), so a container action cannot be attributed to a
person. An action can outlive the page that started it, and a Stop or Restart of the application's own container ends
the process in the middle of it. The record must still say that an action was asked for, and what became of it.

## Options considered

1. **Write the run as *requested* before Portainer is contacted, finalize it after, reconcile runs left open at
   startup; `RequestedBy` is the fixed value `UI`; no retention** — every confirmed request leaves a row, even when the
   process dies. Chosen.
2. **Write the run only after the action** — a process stop during the action leaves no trace of a request that may
   have changed a container.
3. **Reconcile open runs when they are read** — the page would have to tell an interrupted run from one still
   running in this process; the startup step decides it once, as local database work.
4. **Record the client IP address or a reverse-proxy user header as `RequestedBy`** — an IP address is personal data
   and behind a proxy names the proxy; a header can be forged by anyone who reaches the application directly.
5. **Clean up action runs with the scan retention** — they are the only record of changes made to running containers
   and grow by one row per confirmed click.

## Decision

A confirmed action writes its run before Portainer is contacted, with `RequestedBy` `UI`; startup turns runs left
*requested* or *running* into *failed* with an interruption message, and action runs are never cleaned up. The status
flow and messages are in [Container actions](../areas/container-actions.md#action-runs).

## Consequences

- An interrupted Stop or Restart of the application's own container is recorded as failed although Docker may have
  completed it; the message says it was interrupted.
- Each run keeps its container snapshot from retention ([Scan engine](../areas/scan-engine.md)).
- The outcome message of every terminal run is stored in the run's `ErrorMessage` column, so no schema change is needed.
- Several application instances on one database would reconcile each other's open runs; the single-instance assumption
  of the scan engine applies.