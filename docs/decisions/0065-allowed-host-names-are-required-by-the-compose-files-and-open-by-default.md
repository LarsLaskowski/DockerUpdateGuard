# 0065: Allowed host names are required by the compose files and open by default

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
apart, and the application cannot know that list.

## Options considered

1. **Document `AllowedHosts` only, every default stays open** — no deployment changes, but the compose walkthrough,
   the way most new installations come up, keeps leaving them exposed.
2. **Document `AllowedHosts`, require it in the compose files, keep the application's default open** — new compose
   installations name their host names before they start (the walkthrough's `.env` names `localhost`); existing
   deployments keep working, also an upgrade by `pull` and `up -d` with the existing compose file, while a compose
   file downloaded again needs the variable in `.env`; a deployment configured another way stays open until its operator
   sets the key.
3. **A default other than `*` in the application** (such as `localhost`) — every deployment reached by another name
   answers every request with 400 after the upgrade; no default name fits the deployments.
4. **Fail startup while `AllowedHosts` is unset or `*`** — makes the choice explicit, but stops every existing
   deployment at its upgrade: a breaking change for a setting most of them have never needed.
5. **Check the `Origin` of the UI connection** — compared with `Host` it stops nothing, because rebinding makes both
   name the attacker's host; compared with a configured list it needs the same list as `AllowedHosts` and covers only
   the UI connection, while the host filter already rejects that connection's upgrade request. A foreign page
   connecting cross-origin with a permitted `Host` cannot read the prerendered, data-protected component descriptors
   a circuit needs.
6. **Warn at startup while `AllowedHosts` is unset** — reaches deployments that do not use the compose files, but is
   new production code beyond the issue; proposed as a follow-up issue.

## Decision

The application keeps the framework's default (`AllowedHosts` unset, every host name accepted); the documented compose
files set `AllowedHosts` from a required `.env` variable, and the security posture tells every operator to set it. No
`Origin` check is added. The rules are in [Hosting and configuration](../areas/hosting.md#security-posture).

## Consequences

- A compose installation reached through a name it does not list answers 400 until the operator adds the name; a
  compose file downloaded again without the new `.env` variable does not start, with a message naming it.
- Deployments started with `docker run` or a mounted `appsettings.json` stay open until their operator sets the key.
- Behind a reverse proxy the list names the `Host` the proxy forwards, since forwarded headers are not supported
  (#283).
- Revisiting the default (option 3 or 4) is a breaking change for existing deployments and needs the Product Manager.