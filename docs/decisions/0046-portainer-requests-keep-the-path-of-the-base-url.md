# 0046: Portainer requests keep the path of the base URL

- **Status:** Proposed
- **Date:** 2026-11-16
- **Area:** Container actions
- **Source:** Issue #412
- **Supersedes:** —

## Context

The Portainer client sets the configured `BaseUrl` as the base address of its HTTP client and sends root-relative
paths (`/api/auth`, `/api/system/status`, `/api/endpoints/…`). A root-relative reference replaces the whole path of the
base address, so a Portainer published by a reverse proxy under a path prefix (`https://host/portainer/`) is called at
`https://host/api/…`: the request reaches the proxy's root, another application or nothing, and the login posts the
Portainer password to that address. Validation accepts such a `BaseUrl` without a word, and the login Retry-After
window is keyed on the same prefix-less login URL ([0022](0022-a-logins-retry-after-window-is-remembered-process-wide.md)).

## Options considered

1. **Resolve every request below the path of `BaseUrl`** — a prefix-published Portainer works, and the credentials go
   only to the path the operator configured. Cons: a `BaseUrl` that today works only because its path is ignored (one
   ending in `/api`, say) now gets a doubled `api` segment and fails until it is corrected.
2. **Reject a `BaseUrl` with a path at validation** — keeps the client unchanged and makes the mistake visible at
   startup. Cons: Portainer behind a path prefix, a common reverse-proxy layout, cannot be used at all, and a `BaseUrl`
   pasted with a trailing path from the browser stops the application.
3. **Keep the behavior and document that `BaseUrl` must not have a path** — no code change. Cons: the silent
   misdirection of the login, the one defect the issue names, stays.

## Decision

Every Portainer request is resolved relative to `BaseUrl` with its path taken as a directory: a missing trailing slash
is added, and query and fragment of `BaseUrl` are not sent. The resulting addresses are stated in
[Container actions](../areas/container-actions.md#transport-security).

## Consequences

- A Portainer behind a path prefix works with `BaseUrl` set to the prefix, with or without a trailing slash.
- The login Retry-After window follows the prefixed login URL, so two Portainers behind one host under different
  prefixes no longer share a window.
- An operator whose `BaseUrl` carries a path Portainer does not serve (such as `/api`) sees the capability check fail
  with an HTTP status after the upgrade; the README tells to configure the address Portainer's UI is reached at.
