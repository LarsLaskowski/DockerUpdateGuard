# 0034: Container actions are start, stop and restart of a container

- **Status:** Proposed
- **Date:** 2026-11-14
- **Area:** Container actions
- **Source:** Issue #280
- **Supersedes:** —

## Context

The Portainer client could execute six container actions named by free strings (`start`, `stop`, `restart`, `kill`,
`pause`, `unpause`), while the action-run record stores the action types *restart container*, *redeploy stack* and
*update service*. Nothing called the client. Issue #280 makes the actions reachable from the runtime container detail
page of an application without sign-in, so every executable action is one anyone on the network can start. Redeploying
a Portainer stack re-sends the stack file and its environment, and the update target of a redeploy depends on the
pinned tag of #291; no real Portainer answer for stacks or services is available to build against.

## Options considered

1. **Start, stop and restart of a container, the action type enum as the only contract** — covers bringing a
   container back, stopping a misbehaving one and restarting after a configuration change; every request is a single
   Docker Engine call through Portainer's proxy. Chosen.
2. **Also redeploy stacks and update Swarm services now** — the actions an update needs, but a wrong stack request
   rewrites the stack, the target tag depends on #291, and nothing could be tested against a real Portainer. The
   Product Manager deferred them to a follow-up issue.
3. **Keep `kill`, `pause` and `unpause`** — not needed to manage updates; `kill` skips the container's graceful stop.
4. **Keep free action strings in the client and map them in the caller** — two lists to keep in step, and the stored
   type would not say what was sent.

## Decision

The executable actions are start, stop and restart of a container, requested as `ContainerActionType` values
(`StartContainer` and `StopContainer` appended after the existing numbers); every other type is rejected before
Portainer is contacted. The behavior is in [Container actions](../areas/container-actions.md#actions).

## Consequences

- `RedeployStack` and `UpdateService` stay stored types without an executable action until the follow-up of #291.
- Stored numbers of existing types are unchanged, so no migration is needed.
- A container is addressed by the container ID of its snapshot; a container recreated since the last scan needs a
  rescan before it can be acted on.