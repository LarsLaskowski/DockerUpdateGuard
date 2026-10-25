# 0010: A Portainer environment is used without EndpointId only when it is unambiguous

- **Status:** Proposed
- **Date:** 2026-10-25
- **Area:** Container actions
- **Source:** Issue #279
- **Supersedes:** —

## Context

`EndpointId` of a Docker instance's Portainer section is optional. Without it, `PortainerClient` took the first entry
of Portainer's environment list on every call. A Portainer server often manages several environments (its own host,
agents, edge devices, Kubernetes clusters), so the first one can be a different host than the Docker instance
describes: the capability check reported "connected" and a container action was looked up, and for a container of the
same name executed, on the wrong host. Startup validation cannot know how many environments a Portainer server has,
and the Docker instance's `BaseUrl` (a socket, a TCP address) usually differs from the URL Portainer stores for the
environment (an agent address), so the right environment cannot be derived from configuration alone. The Product
Manager decided on the issue that no environment is picked silently.

## Options considered

1. **Use the only environment the list contains; refuse when it contains more than one, naming the IDs and names to
   choose from** — keeps the zero-configuration case of a single-environment Portainer, never guesses. Chosen.
2. **Match the environment by name or URL against the Docker instance** — the URLs rarely agree (socket vs. agent URL)
   and names are free text; a wrong match executes on the wrong host again, now with a false sense of precision.
3. **Require `EndpointId` whenever Portainer is enabled (startup validation)** — safe, but breaks every working
   single-environment configuration on upgrade for no gain.
4. **Resolve once and persist the resolved ID in `PortainerEndpoint.ExternalEndpointId`** — fewer requests, but the
   stored value then no longer reflects configuration, and an environment added later would not be noticed.
5. **Keep the first environment and only warn** — the action still runs on the wrong host.
6. **Ignore a present but invalid total-count header** — the header check exists to notice a list that is not the full
   list; an unparseable, negative or repeated header is such an unexpected answer, and ignoring it would let a single
   listed environment be used again. An invalid header therefore counts as an unknown total and refuses; an absent
   header (a proxy, an older Portainer) is still ignored.
7. **Name the full configuration key with the instance name in the message** — configuration addresses Docker
   instances by list position, not by name, so a copied key would add a stray list entry; the client does not know
   the position. The message names the key relative to the named instance instead.

## Decision

Without a configured `EndpointId`, the capability check and every action use Portainer's environment only when the
environment list (including a valid total-count header) shows exactly one; with more than one, or with an invalid
total-count header and at least one listed environment, they fail before any container lookup with a message naming the key to set relative to the Docker
instance and the environments to choose from, and a failed list request reports its HTTP status. The rules — message texts, the listing limit, how names are cleaned — are in
[Container actions](../areas/container-actions.md#configuration-and-mapping).

## Consequences

- A Portainer server that manages more than one environment, of any type (Kubernetes and Azure environments count
  too), needs `EndpointId` before actions work; the capability message tells the operator which value to set.
- Portainer lists only the environments the authenticating user may see, so a restricted API user that sees exactly
  one environment still works without `EndpointId`.
- A single listed environment is trusted, not checked against the Docker instance; `EndpointId` is the only way to
  bind an instance to a specific host.
- The environment list is requested on every call without `EndpointId`, as before; a configured `EndpointId` skips it.
- Environment names are Portainer data shown in a message: they are cleaned of control and format characters and
  shortened, and the list is capped, so a large or hostile list cannot flood the UI; the message is never logged and
  is rendered encoded wherever it is shown.
