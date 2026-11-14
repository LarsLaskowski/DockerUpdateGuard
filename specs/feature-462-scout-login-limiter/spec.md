# Spec: Count Docker Scout's Docker Hub login against DockerHub:MaxParallelRequests

Source: Issue #462
Status: Approved by Lead

## Problem

`DockerUpdateGuard:DockerHub:MaxParallelRequests` is meant to be one limit for every Docker Hub request of the process
([0030](../../docs/decisions/0030-docker-hub-request-parallelism-is-limited-process-wide-and-follows-reloads.md)).
The Docker Scout provider logs in to Docker Hub (`Vulnerabilities:DockerScoutLoginUrl`, default
`https://hub.docker.com/v2/users/login`) with the same Docker Hub credentials, but through its own `HttpClient`
(confirmed in the code), so an operator who lowered the setting to stay below Docker Hub's rate limit gets more
parallel Docker Hub requests than configured.

## Scenarios

- S1: Given Docker Scout is the vulnerability provider with Docker Hub credentials and no cached Scout token, when a
  lookup logs in, then the login counts as one Docker Hub request in flight, together with the requests of the Docker
  Hub client (web API, its login, token service, registry), for the whole login call as it is sent today: from
  sending until its answer, body included, has been read, within the unchanged `Vulnerabilities:RequestTimeoutSeconds`.
- S2: Given all `MaxParallelRequests` slots are taken, when a Scout lookup needs to log in, then the login waits for a
  free slot in arrival order with the other waiting Docker Hub requests and is sent once it gets one; the lookup then
  ends as it does today.
- S3: Given a Scout lookup with a valid cached token and no login in progress (else E4), when it reads vulnerabilities from `DockerScoutBaseUrl`, then
  those requests take no slot and do not wait for one.
- S4: Given Docker Scout rejects the cached token with HTTP 401, when the provider logs in again for its one retry,
  then that login takes a slot like any other (S1, S2).

## Errors and edge cases

- E1: A login inside a `Retry-After` window is not sent (*failed*, `Docker Hub authentication token request for
  '<user>' not sent: …`, unchanged) and takes no slot. The window is checked once, before the wait for a slot, as the
  Docker Hub client's login does: a login whose window opens while it waits is still sent once admitted.
- E2: A login that fails (non-success status, HTTP 429, timeout, network error, unreadable answer) frees its slot; the
  lookup's result and message are unchanged.
- E3: A lookup cancelled while its login waits for a slot sends no login, leaves the queue and takes no slot; the
  cancellation propagates as it does today.
- E4: Other Scout lookups wait for a login in progress as today, now including its slot wait, each cancellable.

## Non-functional requirements

- N1: No new configuration. `DockerHub:MaxParallelRequests` (default 4, range 1–32) keeps its range and its reload
  behavior (the next admission), now counting the Scout login, whatever host `DockerScoutLoginUrl` names.
- N2: The wait for a slot is not part of `Vulnerabilities:RequestTimeoutSeconds`: like every Docker Hub request other
  than the Docker Hub client's own login, it waits as long as its caller lets it.
- N3: Docker Scout vulnerability requests and requests to other registries stay uncapped.
- N4: Security: credentials, login URL, messages and logs are unchanged; the security area *Registry and Docker Scout
  calls* is touched, no control changed.

## Acceptance criteria

- AC1: With `MaxParallelRequests` = n, the requests of the Docker Hub client and the Scout logins together never have
  more than n in flight; a Scout login waits for a free slot before it is sent, also on the refresh after a 401, and
  holds it until the login call, its buffered body read included, has ended (S1, S2, S4, E4, N1).
- AC2: Every Scout login that took a slot frees it, whatever its outcome (success, failure status, HTTP 429, timeout,
  exception), so a following Docker Hub request is admitted (E2).
- AC3: With no login in progress, a lookup that sends no login (valid cached token, `Retry-After` window) takes no
  slot, and the requests to `DockerScoutBaseUrl` take none, even with every slot taken (S3, E1, N3).
- AC4: A lookup cancelled while its login waits for a slot sends no login and leaves no slot taken; a login reaches
  the HTTP handler only after a slot is granted and is then answered normally, however long it waited (E3, N2).

## Out of scope

- Counting the Docker Scout API requests (the issue asks for the login only).
- Moving the Scout login onto the Docker Hub client or its token cache, or sharing one token between them.
- A time limit on the Scout login like the Docker Hub client's of 0023.
- N4: it states what does not change.

## Open questions

- Should the Scout login take a slot? Answered: yes. The Product Manager made the setting one limit for the process's
  Docker Hub requests (0030, *Context*); 0030 lists the Scout login as uncounted only as a consequence, not a decision.
- Does a login to another `DockerScoutLoginUrl` host count? Answered: yes (N1), as with `DockerHub:ApiBaseUrl`.
- Is the slot wait bounded by `Vulnerabilities:RequestTimeoutSeconds`? Answered: no (N2), 0030's rule; the Docker Hub
  client's login is the exception only because of 0023's login lock, which the Scout login does not take.

## Area documents

- [Registries](../../docs/areas/registries.md), *Docker Hub request budget and parallelism*: the cap counts the Scout
  login for its whole call, body read included; "the Docker Scout provider's requests are not capped" narrows to its
  vulnerability requests. *Request timeout*: "except for the login" names the Docker Hub client's login.
- [Vulnerabilities](../../docs/areas/vulnerabilities.md), *Docker Scout*: the login takes a slot and may wait for one,
  after the `Retry-After` check; Scout API requests take none.
- Record [0030](../../docs/decisions/0030-docker-hub-request-parallelism-is-limited-process-wide-and-follows-reloads.md)
  (unreleased, edited in place): *Decision* includes the Scout login; its *Consequences* exclusion goes.

## Challenge

1. Slot span against the buffered login call (major) — accepted. The slot covers the login call as sent today
   (headers and buffered body, `Vulnerabilities:RequestTimeoutSeconds` unchanged over both); S1, AC1 and the
   Registries line now say so. Switching to headers-only would drop the timeout over the body, which the issue does
   not ask for.
2. S3/AC3 against E4 — accepted. Both now hold only while no login is in progress and point to E4.
3. `Retry-After` window opening during the wait — accepted. E1 states the window is checked once, before the wait, as
   the Docker Hub client's login does (DockerHubClient.cs: the check precedes `SendThrottledAsync`); no re-check.
4. AC4 untestable with a real clock — accepted. Rephrased as an ordering: the login reaches the handler only after a
   slot is granted and is answered normally however long it waited.
5. Record 0030 and the Registries wording — accepted. 0030 is listed under *Area documents* with the parts that
   change, and the two Registries sentences are named.
