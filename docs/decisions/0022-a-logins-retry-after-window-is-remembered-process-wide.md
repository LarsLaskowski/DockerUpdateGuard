# 0022: A login's Retry-After window is remembered process-wide

- **Status:** Accepted
- **Date:** 2026-11-10
- **Area:** Integration failures
- **Source:** Issue #320
- **Supersedes:** —

## Context

[0008](0008-non-idempotent-requests-retry-only-when-not-processed.md) keeps the retry handler from resending a login
inside the window a 429's `Retry-After` names, and left the callers' own next login to a follow-up. The Docker Hub
client logs in again with its next authenticated request after a failed token request, and a new client instance is
created for every scope (a scan, a page operation), so even an instance that remembered the failure would not stop
the next scan. The Portainer client logs in for every capability check and action. The Docker Scout provider posts the same Docker Hub
credentials to `DockerScoutLoginUrl`, by default the Docker Hub client's own login URL, again for every image after a
failed login and without a retry handler in front of it. A server that rate-limits logins
therefore keeps receiving them inside its window, which is what trips lockouts and longer limits.

## Options considered

1. **One process-wide registry of login windows, keyed by login URL and user name, consulted by every client before
   it logs in (chosen)** — outlives every client instance, one implementation for all logins, the Docker Hub and Docker
   Scout logins to the same URL share one window, and a test clock can
   drive it. The cost is a new singleton the three clients depend on.
2. **Refuse the send in the retry handler** — the handler is created per client and would need the same shared state;
   it would also stop requests other than the login, and it cannot answer with the client's own failure message, only
   with a fabricated response or an error.
3. **State in each client (a static field in the Docker Hub client, an instance field in the singleton Portainer
   client)** — two implementations of one rule, and a static field is global state no test can reset safely.
4. **Cache the Portainer JWT instead** — fewer logins while a token is valid, but a failed login still repeats inside
   the window, and keeping a credential in memory longer is a change to credential handling the issue does not ask for.
5. **Cap the remembered window** — would send a login inside a window the server named; the server can refuse every
   login anyway, and a restart clears the registry.
6. **Persist the window across restarts** — a schema change for a state that a restart, an operator action on a
   single instance, may reasonably clear.
7. **Also remember a 429 without `Retry-After`, or a 503 with one** — the first names no window, so its length would
   be a guess; the second speaks of availability, and the retry handler already covers it.

## Decision

Option 1: the Docker Hub login, the Docker Scout login and the Portainer login record a 429 with a positive `Retry-After` in one singleton
registry, and none of them sends that login again for the same login URL and user name until the window has passed; they
answer at once with a failure that names the end of the window. The behavior is described in
[Integration failures](../areas/integration-failures.md).

## Consequences

- A login that a server rate-limited for an hour is not sent again for that hour, from any scan, page or Docker
  instance that logs in to the same URL with the same user name; every operation that needs it fails fast meanwhile.
- A 429 whose wait the retry handler or the client's timeout swallowed (a short `Retry-After` retried, or a wait cut
  off by `RequestTimeoutSeconds`) reaches the client as a later 429 or as a timeout; only a 429 the client receives is
  remembered.
- The Docker Scout login keys its window by the trimmed user name, as the Docker Hub client does, so the two share it
  although Scout sends the user name untrimmed.
- The window lives in memory: a restart clears it, and a configuration change of the user name or the URL starts a new
  login target.
- Logins already in flight when the first 429 arrives are not recalled.
- A new integration that logs in with credentials has to consult the registry as well; revisit together with a JWT
  cache for Portainer.
