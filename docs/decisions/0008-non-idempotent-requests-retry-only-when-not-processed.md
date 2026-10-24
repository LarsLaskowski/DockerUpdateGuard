# 0008: Non-idempotent requests are retried only when they cannot have been processed

- **Status:** Accepted
- **Date:** 2026-10-24
- **Area:** Registries
- **Source:** Issue #273
- **Supersedes:** —

## Context

`TransientHttpRetryHandler` is attached to the Docker Hub, OCI registry, release-feed and Portainer `HttpClient`s and
retried every method on 408, 429, every 5xx and every transport error or timeout. Three requests behind it are not
idempotent: the Portainer container action, the Portainer login and the Docker Hub login, all `POST`. A 5xx, a 408 or a
broken connection does not tell whether the server already acted, so one requested `restart` could run up to
`RetryCount + 1` times (for example behind a reverse proxy that answers 504 while Portainer carries on), and a login
could be repeated into an account lockout or rate limit. The Portainer action feature (#280) depends on an action
being executed at most once.

A scratch measurement on .NET 10's `SocketsHttpHandler` (HTTP/1.1, the version every client behind the handler uses)
showed which `HttpRequestError` a failure carries: a refused connection gives `ConnectionError`, an unknown host
`NameResolutionError`, a failed TLS handshake `SecureConnectionError`; a connection the server accepts and then closes
or resets gives `Unknown` (before or after it read the request), a server that read the request and closed gives
`ResponseEnded`, a garbage status line `InvalidResponse`.

## Options considered

1. **A method-based rule inside `TransientHttpRetryHandler`** — idempotent methods keep the full retry; every other
   method is retried only on a transport error raised while establishing the connection and on 429. One place covers
   every current and future `POST`; the cost is that a non-idempotent request no longer survives a transient 5xx.
2. **A per-request opt-out through `HttpRequestOptions`, set by the callers** — the most precise, but a new `POST`
   that forgets the option is retried again, and the default stays unsafe.
3. **Removing the handler from the Portainer client** — loses the safe retries of Portainer's `GET`s and leaves the
   Docker Hub login unprotected.
4. **Treating `HttpRequestError.Unknown` as "not sent" as well** — would retry the accept-then-close case, but the
   measurement shows the same error for a request the server has already read, so it cannot tell the cases apart.
5. **Buffering a request body only when a retry is possible (`RetryCount` above 0)** — with `RetryCount` 0 the
   Portainer login's `JsonContent` would be sent chunked instead of with `Content-Length`, which some reverse proxies
   reject; every body stays buffered.
6. **Letting the handler give every body-less non-idempotent request an empty body** — would cover future callers, but
   rewrites their requests for the single body-less `POST` there is; the Portainer action sends an empty body itself.

## Decision

Option 1: `TransientHttpRetryHandler` retries `GET`, `HEAD`, `OPTIONS`, `TRACE`, `PUT` and `DELETE` as before, and
any other method only after an `HttpRequestException` whose `HttpRequestError` is `NameResolutionError`,
`ConnectionError`, `SecureConnectionError` or `ProxyTunnelError`, or after a 429. The resulting behavior is described in
[Registries](../areas/registries.md), *Retry of transient HTTP failures*.

## Consequences

- A Portainer action or login that meets a 5xx, a 408, a timeout or a broken connection fails after one attempt with
  that status or error; the operator retries it deliberately. A Docker Hub login that meets a transient 5xx fails that
  token request instead of being retried, so the scan step that needed the token fails until its next run.
- No exception list: the Docker Hub and Portainer logins, which are probably safe to repeat, follow the same rule on
  purpose, because a repeated failing login is what trips lockout and rate limits.
- The rule assumes the request has a body: below the handler, `SocketsHttpHandler` resends a body-less request on its
  own when a reused pooled connection closes after the server read it (measured: a `content: null` `POST` reached the
  server twice, one with an empty `ByteArrayContent` once). The Portainer action is therefore sent with an empty body,
  and a new non-idempotent request must carry one too.
- A connect timeout or `HttpClient.Timeout` surfaces as `TaskCanceledException`, which the handler retries for no
  method, before and after this decision.
- The rule rests on `SocketsHttpHandler` setting the connection-phase errors only before any request byte is written.
  A primary handler other than `SocketsHttpHandler`, or HTTP/2 or HTTP/3 on these clients, would need the measurement
  again.
- Revisit when a caller needs a non-idempotent request that is safe to repeat and must survive a 5xx (an idempotency
  key, or option 2 as an opt-in on top of this rule).
