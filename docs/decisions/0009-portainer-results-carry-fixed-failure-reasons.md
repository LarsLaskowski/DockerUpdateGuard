# 0009: Portainer results carry fixed failure reasons, never exception messages

- **Status:** Accepted
- **Date:** 2026-10-24
- **Area:** Container actions
- **Source:** Issue #315
- **Supersedes:** —

## Context

The Portainer client caught every exception of a capability check or a container action and put its message into the
result as it stood. Those messages come from the runtime, not from the application: a refused connection reads
`Connection refused (<host>:<port>)`, a broken response `Error while copying content to a stream.`, a timeout names
the client setting it elapsed, malformed JSON quotes the parser's position. They name internal hosts and ports, mean
nothing to an operator reading a result, and change with the runtime version. Issue #280 is going to show these
results in the UI and store them in action runs, and the application has no login (`.squad/project.md`,
*Guarantees*), so whatever a result carries is visible to anyone who reaches the UI. The one exception message the
application composes itself — the authentication failure, with the base URL masked
([0005](0005-urls-are-output-with-user-info-masked.md)) and the HTTP status — is safe and useful.

## Options considered

1. **Map each exception to a fixed reason chosen by its type, and mark the composed authentication failure with a
   dedicated exception type whose message is passed on** — the result text is fully determined by the application;
   the runtime's details stay in the error log entry, which keeps the exception. Chosen.
2. **Sanitize the exception message (strip hosts, URLs, ports)** — a deny-list over free text the runtime may
   reword at any release; one missed form leaks again.
3. **Pass on the messages of selected exception types only** — still runtime text, still naming hosts for the most
   common failure (connection refused).
4. **Report authentication failures without throwing (a client-or-reason result from the login step)** — avoids the
   new exception type, but reworks the login flow and the tests that check the logged exception, beyond what the bug
   needs.
5. **Rethrow cancellation by the caller instead of reporting it** — matches the Docker Engine client, but breaks the
   documented promise that Portainer actions never throw; whether a cancelled action becomes a *cancelled* action
   run is #280's decision, and a fixed reason leaves it open.

## Decision

Every exception caught by a Portainer capability check or action is turned into one of a fixed set of reasons by its
type; only the authentication failure the client composes itself, raised as its own exception type, keeps its text.
The exception itself is still logged with the failure. The reasons are listed in
[Container actions](../areas/container-actions.md#limits-and-errors).

## Consequences

- A result no longer tells *why* a connection failed (refused, name resolution, TLS); the operator reads that in the
  log entry of the failure, which carries the exception.
- A new failure mode the mapping does not know reads "unexpected error" until it gets its own reason.
- Caller cancellation is reported as a result with the reason "cancelled", not thrown; #280 decides what an action
  run records for it.
- The other integration clients (Docker Engine, registries, release feeds, vulnerability providers) still put
  exception messages into their results; this record covers Portainer only.
