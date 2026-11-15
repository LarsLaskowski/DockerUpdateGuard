# 0039: A registry request and the read of its response body share one deadline

- **Status:** Accepted
- **Date:** 2026-11-15
- **Area:** Registries
- **Source:** Issue #346
- **Supersedes:** —

## Context

The Docker Hub and OCI registry clients send with `HttpCompletionOption.ResponseHeadersRead` so that an error body is
read only up to its bounded excerpt and the Docker Hub request slot is released at the headers
([0030](0030-docker-hub-request-parallelism-is-limited-process-wide-and-follows-reloads.md)). `HttpClient.Timeout`
ends at the headers in that mode, so a remote side that trickles a body keeps the read, and the scan waiting for it,
alive until the caller's own token fires; [Registries](../areas/registries.md#request-timeout) already promised that
`RequestTimeoutSeconds` is the total time of one request. The clients read bodies at about fifteen places (JSON
documents, excerpts, token answers).

## Options considered

1. **A delegating handler on the two clients' `HttpClient`s, outside the retry handler, that arms one deadline of
   `RequestTimeoutSeconds` on the injected `TimeProvider` when a request is sent and links it into every read of the
   response body** — one place bounds every current and future read site, no client constructor changes, the reason
   stays `timed out after <n> seconds`. Chosen.
2. **A per-request bound at each call site, as the Portainer client does
   ([0037](0037-stop-and-restart-requests-wait-up-to-300-seconds-beyond-the-request-timeout.md))** — explicit, but a
   `TimeProvider` in both client constructors (about 70 test call sites), about fifteen read sites restructured, and a
   new read site that forgets the token is unbounded again.
3. **Read with `ResponseContentRead`, so `HttpClient.Timeout` covers the buffering** — every body, an error body
   included, is buffered whole (up to 2 GB by default) instead of the excerpt's few KiB, and the Docker Hub slot is
   held during the body read.
4. **One bound per client call in the clients' error wrapper** — a listing of up to 1,000 pages and a tag listing
   that inspects every tag would have to fit into one request timeout.
5. **A handler that disposes the response when the deadline passes** — bounds the read too, but the reader sees an
   `ObjectDisposedException` or `IOException`, reported as `unexpected error` or `connection failed` instead of a
   timeout.
6. **A second timeout for the body that starts at the headers** — doubles the worst case of one request and breaks
   the documented meaning of `RequestTimeoutSeconds`.

## Decision

Option 1: the Docker Hub and OCI registry `HttpClient`s send through a deadline handler, outside the retry handler,
whose deadline of `RequestTimeoutSeconds` starts when the request is sent and also ends a pending read of the
response body; the behavior is stated in [Registries](../areas/registries.md#request-timeout).

## Consequences

- A request whose body is still arriving when `RequestTimeoutSeconds` has passed since it was sent ends *failed* with
  `timed out after <n> seconds`, also when the body arrives steadily but slowly; an operator with a slow link raises
  `RequestTimeoutSeconds`.
- An OCI tag whose own lookup times out in its body is listed by name only, as one that times out at its headers
  ([0029](0029-registry-calls-answer-every-error-but-the-callers-cancellation-as-failed.md)).
- The response a client receives carries a replacement content whose stream reads only asynchronously; a synchronous
  read fails instead of waiting without a bound.
- The duration of a whole listing stays unbounded; only each of its requests is.
- The release feeds, Docker Scout and Docker Engine buffer their bodies under their own timeouts and Portainer bounds
  each request itself, so they do not get the handler.
