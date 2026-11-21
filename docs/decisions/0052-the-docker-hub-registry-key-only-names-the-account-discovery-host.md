# 0052: The Docker Hub registry key only names the host of account discovery

- **Status:** Proposed
- **Date:** 2026-11-21
- **Area:** Registries
- **Source:** Issue #268
- **Supersedes:** 0012 (amends it: `DockerHub:Registry` is no longer an absolute URI key, so its `@` rule no longer applies to that key)

## Context

`DockerUpdateGuard:DockerHub:Registry` did two unrelated things. It gated the Docker Hub account listing and the
current-user lookup, which answered *unsupported* when it was not a Docker Hub host, and an absolute URL with another
host replaced the Docker Hub web API base address `DockerHub:ApiBaseUrl` at startup, so the login with the PAT and
its bearer token went to that host without a warning. Validation required only a non-empty value, and an `@` rule
for an absolute value ([0012](0012-configured-urls-are-rejected-when-an-at-sign-is-not-their-user-info-separator.md)).
Which registries the Docker Hub client serves never depended on the key: a fixed host list, matched against the
image reference. The Product Manager decided in the issue that the key only identifies the Docker Hub host of
account discovery and that `ApiBaseUrl` alone sets the web API address.

## Options considered

1. **Accept every host of the client's Docker Hub list (six, `docker.com` and `www.docker.com` included)** — one
   list for the client and the validator, and no configuration that names one of the client's hosts today breaks.
   Two of the names are no registries. Chosen.
2. **Accept only the four registry hosts the issue's example message names** — closer to "registry host", but a
   second list beside the client's, and a configuration with `docker.com` would stop starting for no gain: the
   value reaches no request URL any more.
3. **Accept an absolute Docker Hub URL and normalize it to its host** — keeps `https://hub.docker.com` working, but
   the key would stay a URL in documentation and validation, which the decision rules out.
4. **Keep the host list in `DockerHubClient` and expose a second public check there** — no new type, but the
   validator would depend on the client class, and `SupportsRegistry` already accepts absolute URLs, which the key
   must not.

## Decision

The web API base address is `ApiBaseUrl` whatever `Registry` says, and `Registry`, trimmed once where the options are
built (as [0049](0049-docker-instance-names-are-trimmed-once-where-the-options-are-built.md) does for instance names),
must be one of the Docker Hub hosts of the client's list, compared case-insensitively, or startup and a reload fail.
The host list lives in one static helper that the client and the validator share; the rules are in
[Registries](../areas/registries.md#configuration-reload).

## Consequences

- An operator who pointed the web API somewhere else through an absolute `Registry` gets a startup failure naming the
  key and the allowed hosts, and moves the URL to `ApiBaseUrl`. The release notes say so.
- The account listing's *unsupported* answer for a non-Docker-Hub `Registry` stays as a defensive check; with
  validated options it is no longer reachable.
- Discovered repositories stay stored under `docker.io`, and the Docker Hub client serves the same registries as
  before.
