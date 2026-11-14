# Spec: Count Docker Scout's Docker Hub login against DockerHub:MaxParallelRequests

Source: Issue #462
Status: Draft

## Problem

`DockerUpdateGuard:DockerHub:MaxParallelRequests` is meant to be one limit for every Docker Hub request of the process
([0030](../../docs/decisions/0030-docker-hub-request-parallelism-is-limited-process-wide-and-follows-reloads.md)).
The Docker Scout provider logs in to Docker Hub (`Vulnerabilities:DockerScoutLoginUrl`, default
`https://hub.docker.com/v2/users/login`) with the same Docker Hub credentials, but through its own `HttpClient`
(`DockerScoutVulnerabilityProvider.AuthenticateAsync`, confirmed in the code), so while scans and pages already use all
slots, a Scout login adds one more request to Docker Hub.
An operator who lowered the setting to stay below Docker Hub's rate limit gets more parallel Docker Hub traffic
than configured.

## Scenarios

- S1: Given Docker Scout is the vulnerability provider with Docker Hub credentials and no cached Scout token, when a
  lookup logs in, then the login counts as one Docker Hub request in flight, together with the requests of the Docker
  Hub client (web API, its login, token service, registry), from sending until the response headers arrive.
- S2: Given all `MaxParallelRequests` slots are taken, when a Scout lookup needs to log in, then the login waits for a
  free slot in arrival order with the other waiting Docker Hub requests and is sent once it gets one; the lookup then
  ends as it does today.
- S3: Given a Scout lookup with a valid cached token, when it reads vulnerabilities from `DockerScoutBaseUrl`, then
  those requests take no slot and do not wait for one.
- S4: Given Docker Scout rejects the cached token with HTTP 401, when the provider logs in again for its one retry,
  then that login takes a slot like any other (S1, S2).

## Errors and edge cases

- E1: A login inside a `Retry-After` window is not sent (*failed*, `Docker Hub authentication token request for
  '<user>' not sent: …`, unchanged) and takes no slot.
- E2: A login that fails (non-success status, HTTP 429, timeout, network error, unreadable answer) frees its slot; the
  lookup's result and message are unchanged.
- E3: A lookup cancelled while its login waits for a slot sends no login, leaves the queue and takes no slot; the
  cancellation propagates as it does today.
- E4: Other Scout lookups wait for a login in progress as today (one login at a time), now including its wait for a
  slot; each honors its own cancellation.

## Non-functional requirements

- N1: No new configuration. `DockerHub:MaxParallelRequests` (default 4, range 1–32) keeps its range and its reload
  behavior (the next admission), now counting the Scout login, whatever host `DockerScoutLoginUrl` names.
- N2: The wait for a slot is not part of `Vulnerabilities:RequestTimeoutSeconds`: like every Docker Hub request other
  than the Docker Hub client's own login, it waits as long as its caller lets it.
- N3: Docker Scout vulnerability requests and requests to other registries stay uncapped.
- N4: Security: the credentials sent, the login URL, the messages and the logs are unchanged; the change touches the
  security area *Registry and Docker Scout calls* (`.squad/project.md`) without changing a security control.

## Acceptance criteria

- AC1: With `MaxParallelRequests` = n, the requests of the Docker Hub client and the Scout logins together never have
  more than n in flight; a Scout login waits for a free slot before it is sent, also on the refresh after a 401, and
  holds it until its response headers arrive (S1, S2, S4, E4, N1).
- AC2: Every Scout login that took a slot frees it, whatever its outcome (success, failure status, HTTP 429, timeout,
  exception), so a following Docker Hub request is admitted (E2).
- AC3: A lookup that sends no login (valid cached token, `Retry-After` window) takes no slot, and the requests to
  `DockerScoutBaseUrl` take none, even with every slot taken (S3, E1, N3).
- AC4: A lookup cancelled while its login waits for a slot sends no login and leaves no slot taken, and a Scout login
  admitted after waiting longer than `Vulnerabilities:RequestTimeoutSeconds` is still sent and its answer used (E3,
  N2).

## Out of scope

- Counting the Docker Scout API requests (the issue asks for the login only; Scout is a different service with its own
  limits).
- Moving the Scout login onto the Docker Hub client or its token cache, or sharing one token between them.
- A time limit on the Scout login like the Docker Hub client's of
  [0023](../../docs/decisions/0023-the-docker-hub-web-api-token-is-cached-process-wide-per-credentials.md).
- N4 needs no criterion: it states what does not change.

## Open questions

- Should the Scout login take a slot at all? Answered: yes. The Product Manager decided in #265 that the setting is
  one limit for the process's Docker Hub requests (0030, *Context*); the Scout login is a Docker Hub request with the
  Docker Hub credentials, and 0030 lists it under *Consequences* as not counted only because the provider bypassed
  the client, not by decision. The issue limits the change to the login.
- Does a login to a non-Docker-Hub `DockerScoutLoginUrl` count? Answered: yes (N1), as the Docker Hub client's
  requests count whatever `DockerHub:ApiBaseUrl` names.
- Is the slot wait bounded by `Vulnerabilities:RequestTimeoutSeconds`? Answered: no (N2), the rule 0030 sets for
  every Docker Hub request; the Docker Hub client's login is the exception only because of 0023's process-wide login
  lock, which the Scout login does not take.

## Area documents

- [Registries](../../docs/areas/registries.md), *Docker Hub request budget and parallelism*: the cap also counts the
  Docker Scout provider's login; its vulnerability requests stay uncapped. *Request timeout*: the Scout login's slot
  wait is outside `Vulnerabilities:RequestTimeoutSeconds`.
- [Vulnerabilities](../../docs/areas/vulnerabilities.md), *Docker Scout*: the login takes a Docker Hub request slot
  (`DockerHub:MaxParallelRequests`) and may wait for one; cached-token lookups and Scout API requests do not.
