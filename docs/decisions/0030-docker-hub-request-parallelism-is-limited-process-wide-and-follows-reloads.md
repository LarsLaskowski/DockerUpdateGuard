# 0030: Docker Hub request parallelism is limited process-wide and follows reloads

- **Status:** Accepted
- **Date:** 2026-11-14
- **Area:** Registries
- **Source:** Issue #265
- **Supersedes:** —

## Context

`DockerUpdateGuard:DockerHub:MaxParallelRequests` was a `SemaphoreSlim` inside each `DockerHubClient`, a transient typed
`HttpClient` resolved once per DI scope, sized once from the options when the instance was created. Every scan,
account discovery run and Blazor circuit therefore had the full allowance of its own, so N concurrent operations could
have N × `MaxParallelRequests` Docker Hub requests in flight, raising the risk of HTTP 429, and a reload reached only
instances created after it. The Product Manager decided that the setting is one limit for the whole process, applied
live on reload, and that other registries stay unlimited. The Docker Hub login holds the process-wide login lock of
[0023](0023-the-docker-hub-web-api-token-is-cached-process-wide-per-credentials.md) while it waits for a slot, so the
limiter must not let a slot holder wait for that lock.

## Options considered

1. **A singleton limiter the client instances share, called where the client sends today, with a counter, a FIFO queue
   of waiters and the limit taken from the options monitor's change notification (chosen)** — one count for the
   process whatever the number of scopes or instances; a raised limit admits waiters at once, a lowered one lets the
   requests in flight finish; the slot covers the same span as before (the retries and `Retry-After` waits of
   `TransientHttpRetryHandler` included, the body read not), so no other behavior changes; testable without a clock or
   an `HttpClient` pipeline. The cost is a small hand-written synchronization type.
2. **A `DelegatingHandler` on the `DockerHubClient` pipeline using the same singleton gate** — outside the retry handler
   it holds the same span as option 1 with more wiring; inside it each attempt takes a slot and a `Retry-After` wait
   holds none, which changes how 429s and the limit interact without a decision asking for it.
3. **`System.Threading.RateLimiting.ConcurrencyLimiter` as a singleton** — its permit limit is fixed when it is
   created; following a reload means replacing it, and requests in flight on the old instance are then not counted
   against the new one.
4. **One singleton `SemaphoreSlim`** — it cannot shrink, so a lowered limit would need permits withheld by hand, and a
   raised one above the initial count is impossible without replacing it.
5. **Register `DockerHubClient` as a singleton** — rejected in 0023 already: a typed `HttpClient` captured in a
   singleton never gets a new handler from the factory, and the limit would still be read only once.

## Decision

Option 1: one process-wide `DockerHubRequestLimiter` bounds the requests every `DockerHubClient` instance sends to Docker
Hub (web API, login, token service, registry) and takes a reloaded `MaxParallelRequests` for the next admission; the
client owns no throttle of its own. The behavior is described in
[Registries](../areas/registries.md#docker-hub-request-budget-and-parallelism).

## Consequences

- Concurrent scans, account discovery and pages share one allowance, so Docker Hub sees at most `MaxParallelRequests`
  requests of the process at once; operations now wait for each other's slots.
- The wait for a slot lies before `HttpClient.Timeout` starts, so `RequestTimeoutSeconds` does not bound it, except for
  the login, whose time limit of 0023 includes it; a request waits for a slot as long as its caller lets it. Under a
  low limit and many concurrent operations a login is more likely to time out than before.
- A slot is held through the retries and `Retry-After` waits of a request, so a 429 storm slows every Docker Hub caller
  of the process; revisit with option 2 (inside the retry handler) if that proves too strict.
- Requests to other registries and the Docker Scout provider's own Docker Hub login are not counted.
