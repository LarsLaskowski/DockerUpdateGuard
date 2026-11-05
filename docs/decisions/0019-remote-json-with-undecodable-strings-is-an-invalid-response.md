# 0019: Remote JSON with a string that does not decode is an invalid response

- **Status:** Accepted
- **Date:** 2026-11-05
- **Area:** Registries
- **Source:** Issue #358
- **Supersedes:** 0009 — amends it: a JSON body with a string that does not decode is an `invalid response`, and the Docker Hub and OCI calls that had no handler answer a body they cannot parse as *failed*.

## Context

`System.Text.Json` parses a JSON string that does not decode as Unicode text — a lone surrogate escape such as
`\ud800`, or bytes that are not UTF-8 — without complaint and throws `InvalidOperationException` only when the string
is read (`GetString()`, a property's `Name`). The Docker Hub, OCI and Docker Engine clients parse every body as a
`JsonDocument` and read its strings afterwards, so such a body either escaped the five Docker Hub and three OCI calls
that have no handler, as a malformed body already did, or was reported as `unexpected error` by the calls that map
errors through the fixed reasons of [0009](0009-integration-results-carry-fixed-failure-reasons.md), which know only
`JsonException` as an invalid response. The control-character replacement of #353 never saw the value. RFC 7493
(I-JSON) §2.1 excludes such strings from interoperable JSON; the deserializing clients (Trivy, Docker Scout, Portainer)
already receive a `JsonException` for them. Issue #272 asks for every error escaping the Docker Hub and OCI calls —
transport errors, timeouts and malformed JSON, including the login response and the token payload — to become
*failed*, through one private wrapper per client with one warning per mapped failure.

## Options considered

1. **Decode every string value and property name once where a remote body is parsed, raise `JsonException` when one
   does not decode, and route the registry calls without a handler through the per-client wrapper #272 describes,
   catching only `JsonException` for now** — one place decides, the existing reason `invalid response` follows in
   every client, and #272 widens the same wrapper and warning instead of replacing them. Chosen.
2. **Map `InvalidOperationException` to `invalid response` in the shared mapping** — the runtime throws it for
   unrelated misuse as well (0009, option 4), and it does nothing for the calls without a handler.
3. **Turn `InvalidOperationException` into `JsonException` around each read** — would also cover values of the wrong
   type, but several read blocks make further HTTP calls whose errors it would relabel; about 25 sites, and values of
   the wrong type are #272's.
4. **Read tolerantly (treat such a string as absent, or decode it with U+FFFD)** — keeps the rest of the body, but
   needs an own JSON string decoder or a replacement for every accessor, including label names; one missed accessor
   throws again, for input no conforming sender produces.
5. **Catch every exception in those calls now** — pre-empts #272, which carries the Product Manager's decision on
   timeouts and their messages.
6. **Only the parse check, leaving the handlers to #272** — the error would still escape the client.
7. **A `try`/`catch (JsonException)` in each of those calls with a JSON-only log event** — #272 would have to tear it
   out or log a second way beside it.
8. **Let one container's unreadable stats body fail the resource sampling of the instance** — hides every other
   container's sample, while a non-success stats answer for the same container is already skipped.

## Decision

Every remote JSON body the Docker Hub, OCI and Docker Engine clients parse is refused as a `JsonException` when any
of its string values or property names does not decode, so it is reported with the fixed reason `invalid response`;
the Docker Hub and OCI calls that had no handler answer it, and a malformed body, as *failed* with that reason through
one wrapper per client. One container's unreadable stats body leaves only that container out of a resource sampling.
The rule is stated in [Registries](../areas/registries.md#failure-model-of-integrations).

## Consequences

- One undecodable string anywhere refuses the whole body, also in a field the client never reads; a tag listing or an
  account listing fails as a whole when one of its pages is affected, while an OCI tag whose own manifest is affected
  is listed by name only, and a container whose own stats body is affected is left out of that resource sampling
  round (intended: the other containers' samples stay).
- This settles #272's malformed-JSON part for the Docker Hub and OCI calls (bodies, the login response, the token
  payload's JSON); the wrapper and the warning event of each client are the ones #272 widens.
- Each string of a parsed body is decoded once more than before; the bodies are already held whole as a
  `JsonDocument`, so no limit changes.
- Transport errors, timeouts, values of an unexpected type and a Docker Hub token payload that is not base64 still
  escape the Docker Hub and OCI calls until #272.
- The release feeds keep reporting such a body as `unexpected error`: they are not covered, and their text reaches no
  message.
