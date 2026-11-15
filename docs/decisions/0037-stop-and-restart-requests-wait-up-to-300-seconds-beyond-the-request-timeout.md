# 0037: Stop and restart requests wait up to 300 seconds beyond the request timeout

- **Status:** Accepted
- **Date:** 2026-11-14
- **Area:** Container actions
- **Source:** Issue #280
- **Supersedes:** —

## Context

Docker answers a stop or restart only after the container has stopped, which takes up to its stop timeout (10 seconds
by default, longer with a compose `stop_grace_period`). Every Portainer request was bounded by
`RequestTimeoutSeconds` (default 15), so a container with a longer grace period would be reported as failed although
Docker completed the action, and the operator might repeat it.

## Options considered

1. **Bound the stop and restart request by 300 seconds plus `RequestTimeoutSeconds`; every other request keeps
   `RequestTimeoutSeconds`** — honors any grace period up to 300 seconds, no new configuration and no new reader.
   Chosen.
2. **Read the container's stop timeout (container inspect) and bound the request by it** — a tighter bound, but a new
   reader of Docker's container inspect without a real sample to build it from.
3. **Pass Docker's `t` parameter** — bounds the wait, but overrides the operator's grace period and kills the
   container early.
4. **Document that `RequestTimeoutSeconds` must exceed the longest grace period** — every other Portainer request would
   then wait as long before failing.
5. **A new configuration key for the action timeout** — one more setting for a value that hardly needs tuning.

## Decision

The stop and restart request is bounded by 300 seconds plus `RequestTimeoutSeconds`, without a `t` parameter; the
login, environment requests, container lookup and start keep `RequestTimeoutSeconds` each
([Container actions](../areas/container-actions.md#actions)).

## Consequences

- A Docker daemon that hangs keeps a Stop or Restart waiting for up to 300 seconds plus `RequestTimeoutSeconds`; the
  page shows progress meanwhile.
- A grace period above 300 seconds ends as failed with the hint that the action may have been applied.
- The Portainer client creates its timeouts through the injected `TimeProvider`, per request instead of
  `HttpClient.Timeout`.