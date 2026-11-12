# 0023: The Docker Hub web API token is cached process-wide, per credentials

- **Status:** Proposed
- **Date:** 2026-11-12
- **Area:** Registries
- **Source:** Issue #25
- **Supersedes:** —

## Context

The Docker Hub client logs in to the web API (`v2/users/login`) with the configured user name and PAT and reuses the
returned token until one minute before its expiry. The client is a transient typed `HttpClient` resolved once per DI
scope, and the token lived in the instance, so every scan, account discovery run and Blazor circuit logged in again,
spending logins against Docker Hub's limits (F-009, and #6, closed with the note that moving the cache into a
singleton here also makes the instance count irrelevant). The Docker Scout provider, a singleton, already keeps its
Docker Hub token for the whole process. `DockerHub:UserName` and `DockerHub:Pat` are read live on reload, so a
process-wide token must never answer for credentials it was not issued for.

## Options considered

1. **A singleton token cache that the client instances share, keyed by the login URL, the trimmed user name and a
   SHA-256 fingerprint of the PAT, with a time limit on each login (chosen)** — one login per token lifetime for the
   whole process; a reload of either credential makes the next request log in with the new one; a token is never sent
   to another login origin; the PAT itself is not copied into the cache; a test clock drives the expiry and the limit.
   The cost is a new singleton the client depends on, and one login lock for the whole process, which the time limit
   keeps a stalled login (one whose body never completes, which `HttpClient.Timeout` does not bound) from holding.
2. **Register `DockerHubClient` itself as a singleton** — a typed `HttpClient` captured in a singleton never gets a
   new handler from the factory (DNS changes go unnoticed), and it would also make the per-instance
   `MaxParallelRequests` throttle process-wide, which is #265's decision with its own reload semantics.
3. **Static fields in the client** — global state no test can reset safely, as [0022](0022-a-logins-retry-after-window-is-remembered-process-wide.md)
   found for the Retry-After windows.
4. **Share the Docker Scout provider's token** — that login goes to the configurable `DockerScoutLoginUrl`, so the two
   tokens are not interchangeable in general, and it couples two integrations for a saving of one login.
5. **Also drop the cached token when a web API request carrying it is answered 401** — would bound the life of a token
   Docker Hub refuses early, but a 401 for a resource the account cannot read would trigger a login per request, more
   logins than today; the client has no such handling today either.
6. **Keep the cache per scope (no change)** — leaves the extra logins the issue reports.

## Decision

Option 1: one process-wide `DockerHubAccessTokenCache` holds the Docker Hub web API token for every client instance and
scope, reuses it only for the login URL and credentials it was obtained with, and lets one login run at a time, for at
most `RequestTimeoutSeconds`. The behavior is
described in [Registries](../areas/registries.md#authentication).

## Consequences

- Scans, account discovery and pages share one token: Docker Hub sees one login per token lifetime instead of one per
  scope.
- A token Docker Hub refuses before its expiry (a revoked PAT, say) is sent by every scope until one minute before
  that expiry; a changed PAT in the configuration or a restart clears it. Revisit with option 5 when such refusals are
  seen in practice.
- While one login is in flight, every authenticated Docker Hub web API request of the process waits for it. The login
  is cancelled once `RequestTimeoutSeconds` have passed since it started under the lock — its wait for a request slot,
  the headers and the body included — so that is the longest wait; each waiter also honors its own cancellation, and a
  cancelled login stores nothing.
- The token lives in memory as long as it is valid rather than as long as a scope, which is no longer than the Docker
  Scout provider already keeps its token.