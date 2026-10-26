# 0012: Configured URLs are rejected when an `@` is not their user-info separator

- **Status:** Proposed
- **Date:** 2026-10-26
- **Area:** Hosting and configuration
- **Source:** Issue #325
- **Supersedes:** —

## Context

Configuration validation checked a configured URL only for being absolute and for its scheme. The platform's URI
parser ends the authority at the first `/`, `?` or `#`, so `https://admin#secret@portainer.example:9443` is a valid
URI with host `admin`, port 443, no user info and the fragment `#secret@portainer.example:9443` (measured with .NET
10.0.403). The Portainer client connected there and sent the Portainer API key in its `X-API-Key` header to `admin`,
and log entries named `admin:443` (issue #325, found while fixing #318; record
[0009](0009-integration-results-carry-fixed-failure-reasons.md) left it as a follow-up). Every other configured URL
the application connects to is read the same way: the Docker Engine `BaseUrl` (any of its schemes), the Docker Hub
API base address or an absolute `DockerHub:Registry` (which carry the Docker Hub PAT), the release feeds, the Docker
Scout URLs (credentials), `TrivyBaseUrl` (Trivy's Go parser reads the host the same way, record
[0005](0005-urls-are-output-with-user-info-masked.md)) and `Telemetry:OtlpEndpoint`. The operator's intent is
evident from the text — the host is what follows the `@` — but the parser has no way to know it.

## Options considered

1. **Reject a value whose `@` is not the one separator of a non-empty parsed user info** — a value with an `@` must
   contain exactly one, and the parser must report user info for it. Covers every character that ends the authority
   early without listing them, accepts exactly the values whose user info record 0005 masks from its parts, and
   fails closed on the rest (an `@` in a path, query or fragment, an empty user info). An operator fixes the value by
   percent-encoding the user info.
2. **Reject only when the parsed user info is empty and the text contains `@`** — stops the wrong host as well, but
   accepts a second `@` after a correct user info (`https://u:p@host#x@y`), which record 0005 then masks fail-closed
   and Trivy's parser may read differently; the rule would no longer match the issue's "an `@` the user info does not
   account for".
3. **Reject `#`, `?` and `/` before the last `@`** — reimplements part of the parser and misses what it does not list
   (a `\` is rejected by the parser today, other forms may not be).
4. **Repair the value (treat everything up to the last `@` as user info and percent-encode it)** — guesses the
   operator's intent, makes the application read a URL differently from every other tool (Trivy receives the
   configured value), and hides the configuration error.
5. **Reject any URL with user info** (record 0005, option 3) — breaks configurations that work today; a product
   decision beyond this bug.
6. **Warn at runtime only** — the credential still travels to the wrong host on the first request.
7. **Check only the Portainer and Docker instance `BaseUrl`** — the issue's example, but the same parser reading sends
   the Docker Hub PAT, the Docker Scout credentials and telemetry to the wrong host.

## Decision

Option 1, for every configured URL validated at startup (and, for the `DockerUpdateGuard` section, on reload): the
URL keys of the `DockerUpdateGuard` section, `DockerHub:Registry` when it is an absolute URI, and `Telemetry:OtlpEndpoint`. A violation fails
validation with a fixed message that names the key and how to encode the user info, never the value. The rule and its message
are in [Hosting and configuration](../areas/hosting.md#validation) and, for the OTLP endpoint,
[Telemetry](../areas/telemetry.md#limits-and-errors).

## Consequences

- A configuration that connected to the wrong host now fails startup (or its reload) instead; the fix is to
  percent-encode `@`, `#`, `?` and `/` in the user name and password (`%40`, `%23`, `%3F`, `%2F`).
- A base URL with an `@` in its path, query or fragment, or with an empty user info (`https://@host`), is rejected
  although it would reach the intended host; accepted, it fails closed and such base URLs are not expected. An `@` in
  a path is written as `%40`.
- A URL with correctly parsed user info is still accepted, also when it reads unexpectedly to a human
  (`https://portainer.example@other.example` names host `other.example`): the parser and the text agree there.
- After this rule every accepted value with an `@` is one that record 0005 masks by rebuilding it from its parts, or
  (without `scheme://`) masks fail-closed; Trivy's error output stays withheld for a `TrivyBaseUrl` with user info.
- The rule is implemented twice, in the `DockerUpdateGuard` validator and in the telemetry validator, because the
  telemetry project does not reference the host.