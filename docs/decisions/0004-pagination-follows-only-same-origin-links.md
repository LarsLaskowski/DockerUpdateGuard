# 0004: Pagination follows only same-origin links

- **Status:** Accepted
- **Date:** 2026-10-24
- **Area:** Registries
- **Source:** Issue #264
- **Supersedes:** —

## Context

The Docker Hub web API (`next` in the JSON body), Docker Scout (`next` in the JSON body) and the OCI tag listing
(`Link: <…>; rel="next"`) tell the client where the next page is. The clients followed that link to any scheme, host
and port, and Docker Hub and Docker Scout attach the Docker Hub bearer token, derived from the configured PAT, to every
page request. A compromised upstream, a misconfigured proxy or anyone who can tamper with a response could collect that
token or steer the application into requests against arbitrary, including internal, hosts. Integration clients must
answer expected failures with `ExternalOperationResult<T>` and never throw (`docs/ARCHITECTURE.md`, *Integration
clients*), and credentials must never leave for a host the operator did not configure.

## Options considered

1. **Follow only links on the origin of the listing, otherwise stop with *failed*** — one rule for the three clients,
   no request ever reaches a foreign host; a mirror or proxy in `DockerHub:ApiBaseUrl` or `DockerScoutBaseUrl` that
   does not rewrite the links to its own origin makes the listings fail.
2. **Follow cross-origin links without the `Authorization` header** — the token stays home, but the application still
   requests whatever host a response names (server-side request forgery), and the tags such a host returns would be
   treated as the registry's.
3. **Stop at a foreign link and return the pages read so far as *succeeded*** — keeps listings working, but a
   truncated tag list hides updates silently, and a truncated Docker Scout result would resolve findings that still
   exist.
4. **Rebuild the next page from its query (`page`, `last`) on the configured base address** — never leaves the
   configured host, but depends on each API's paging parameters, which Docker Scout does not document, and ignores a
   legitimate path the server names.
5. **Block foreign hosts in an HTTP message handler** — would cover every request, but the OCI token service
   legitimately lives on another host (the `realm` of a `WWW-Authenticate` challenge) and Docker Scout's client has no
   handler pipeline; the defect is application-level link following, not the transport.

## Decision

Option 1, decided by the Product Manager on the issue: a next-page link is followed only when, resolved against the
URI of the current request, it has the scheme, host and port of the listing's origin; any other link, a scheme change
and an unparseable link stop the listing with *failed* and send no request. The rule lives in one shared helper
(`Infrastructure/PaginationLinkGuard`) used by all three clients; the behavior is specified in
[Registries](../areas/registries.md#pagination-links) and [Vulnerabilities](../areas/vulnerabilities.md#docker-scout).

## Consequences

- No request, and so no Docker Hub bearer token, reaches a host other than the configured one through pagination.
- Root-relative OCI `Link` targets (the form ghcr.io sends) are now resolved against the current request; before, on
  Linux they were parsed as `file:` URIs and the listing failed with an error after its first page.
- An operator who points `DockerHub:ApiBaseUrl` or `Vulnerabilities:DockerScoutBaseUrl` at a mirror or proxy that
  passes the upstream's absolute links through gets *failed* listings; the proxy has to rewrite the links. Accepted:
  credentials never cross hosts.
- Residual risk: the form of Docker Scout's real `next` link could not be observed (no credentials in the session).
  Should Docker Scout ever link to another host, its listings fail closed instead of following.
- Out of scope and unchanged: the token service named by a `WWW-Authenticate` realm and HTTP redirects.
