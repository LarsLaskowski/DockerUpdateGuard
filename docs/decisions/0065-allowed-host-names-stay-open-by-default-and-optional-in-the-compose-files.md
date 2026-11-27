# 0065: Allowed host names stay open by default and optional in the compose files

- **Status:** Proposed
- **Date:** 2026-11-27
- **Area:** Hosting and configuration
- **Source:** Issue #473
- **Supersedes:** —

## Context

The application has no sign-in and relies on a trusted perimeter. It sets no `AllowedHosts`, so the framework's host
filter accepts every `Host` header. A page an operator opens inside the perimeter can rebind its own host name to the
application's address: the browser then treats the application as that page's origin, so the page reads the
prerendered UI, opens the UI connection and acts through it — manual scans, tag selection and, on a Portainer-enabled
instance, start, stop and restart of containers. Under rebinding the browser sends the attacker's host name as
`Host` and as the host of `Origin` alike; only a list of the host names the operator really uses tells the two
apart, and the application cannot know that list. IP addresses are not exempt from such a list: every address used to
open the UI must be named in it.

## Options considered

1. **Document `AllowedHosts` only, compose files unchanged** — nothing changes for anyone, but compose operators would
   have to add the environment entry themselves.
2. **Require it in the compose files** (`${ALLOWED_HOSTS:?…}`) — new compose installations would choose their names
   before they start, but every operator would have to list every name and internal IP address used to open the UI,
   and a compose file downloaded again would not start until `.env` has the variable: a breaking change. An upgrade
   by `pull` and `up -d` with the existing compose file would keep working.
3. **Optional in the compose files through a value-less entry** (`AllowedHosts:`, filled from `AllowedHosts` in
   `.env`), the `.env.example` line commented out with an example, the key documented prominently — without a `.env`
   value the container gets no such variable, so nothing changes, also for a deployment that sets the key in a mounted
   `appsettings.json`; a deployment stays open until its operator sets the key.
4. **Optional through interpolation** (`${ALLOWED_HOSTS:-*}`, or `${AllowedHosts:-}`) — reads more familiarly, but
   sets `*` or an empty value whenever the variable is unset, and an environment variable wins over `appsettings.json`:
   a compose deployment protected by its mounted file would be opened silently by its next compose file.
5. **A default other than `*` in the application** (such as `localhost`) — every deployment reached by another name
   answers every request with 400 after the upgrade; no default name fits the deployments.
6. **Fail startup while `AllowedHosts` is unset or `*`** — makes the choice explicit, but stops every existing
   deployment at its upgrade.
7. **Check the `Origin` of the UI connection** — compared with `Host` it stops nothing, because rebinding makes both
   name the attacker's host; compared with a configured list it needs the same list as `AllowedHosts` and covers only
   the UI connection, while the host filter already rejects that connection's upgrade request. A foreign page
   connecting cross-origin with a permitted `Host` cannot read the prerendered, data-protected component descriptors
   a circuit needs.
8. **Warn at startup while `AllowedHosts` is unset** — reaches every deployment form, but is new production code
   beyond the issue; proposed as a follow-up issue.

## Decision

The application keeps the framework's default (`AllowedHosts` unset, every host name accepted); the documented compose
files pass `AllowedHosts` through from `.env` only when it is set there, and the documentation recommends setting it in
every deployment. No `Origin` check is added. Optional in the compose files is the Product Manager's decision on #473;
the value-less entry is the form that keeps it true for deployments protected by a mounted file. The rules are in [Hosting and configuration](../areas/hosting.md#security-posture).

## Consequences

- No deployment changes behavior on upgrade, also with a compose file downloaded again over a mounted
  `appsettings.json` that sets `AllowedHosts`.
- The `.env` variable carries the key's own, case-sensitive name, and an empty value is not harmless: it overrides
  the file and accepts every name, so the documented line is commented out, never left empty.
- A deployment that does not set the key stays open to DNS rebinding; protection is the operator's step.
- A deployment that sets it answers 400 to every name and IP address it does not list.
- Behind a reverse proxy the list names the `Host` the proxy forwards, since forwarded headers are not supported
  (#283).
- Making the key required (option 2, 5 or 6) is a breaking change for existing deployments and needs the Product
  Manager.