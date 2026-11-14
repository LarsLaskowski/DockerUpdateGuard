# 0029: Registry calls answer every error but the caller's cancellation as *failed*

- **Status:** Proposed
- **Date:** 2026-11-14
- **Area:** Registries
- **Source:** Issue #272
- **Supersedes:** —

## Context

The Docker Hub and OCI registry clients promise outcomes instead of errors for expected failures
(`.squad/project.md`, *Guarantees*; [Integration failures](../areas/integration-failures.md#outcomes)). Since
[0019](0019-remote-json-with-undecodable-strings-is-an-invalid-response.md) their calls run through one private wrapper
per client that catches only `JsonException`. Everything else still escaped: a transport error or a timeout left after
the retries, a JSON value of an unexpected type (`InvalidOperationException` from a `JsonElement` accessor), a Docker
Hub access token whose payload is not base64url (`FormatException`). The Docker Hub image configuration lookup caught
every error but `OperationCanceledException`, so an `HttpClient` timeout — a `TaskCanceledException` while the
caller's token is not cancelled — escaped as if the scan had been cancelled. An escaped error fails the scan of the
whole image or container in the orchestrators' generic handlers, which can only say `unexpected error` or `timed out`
without the seconds, and in an OCI tag listing it aborts the listing at the first tag whose lookup fails. The Product
Manager decided in #272: transport errors, timeouts and malformed JSON become *failed* with a message; cancellation
stays cancellation. The issue leaves open whether to catch every remaining error.

## Options considered

1. **The wrapper rethrows `OperationCanceledException` only while the caller's token is cancelled and answers every
   other exception as *failed*, with the fixed reason of the shared mapping of
   [0009](0009-integration-results-carry-fixed-failure-reasons.md) and the client's `HttpClient` timeout** — one rule
   for all nine calls; a timeout is told apart from a cancellation by the caller's token, as Docker Scout already does;
   an error the mapping does not know reads `unexpected error` and is logged with its exception. Chosen.
2. **Catch only the listed types (`HttpRequestException`, `IOException`, `OperationCanceledException`,
   `JsonException`, `InvalidOperationException`, `FormatException`)** — keeps a programming error an exception, but
   the orchestrators turn it into the same `unexpected error` one level up, failing the whole image instead of one
   call; an OCI listing still aborts on one tag; and errors from remote data outside the list (an
   `ArgumentException` for an empty token payload segment) still escape.
3. **Report `InvalidOperationException` as `invalid response`** — the runtime raises it for misuse as well,
   `ObjectDisposedException` of a disposed client included; 0019 rejected the same mapping (its option 2).
4. **Read every JSON value tolerantly (check the kind at each accessor, skip list items of the wrong type)** — about
   25 sites (0019, option 3), one missed accessor throws again, and a body of another shape would read as an empty
   success instead of a failure.
5. **Put the exception's message into the result, as the issue allows for transport and JSON errors** — breaks the
   guarantee of fixed reasons (0009).
6. **Stop an OCI tag listing at the first tag whose lookup times out or fails to connect** — bounds how long a listing
   against a registry that answers `tags/list` but not its manifests can take, but treats these failures unlike a
   non-success status, which already leaves the tag listed by name only, as the issue asks for all of them.
7. **Report a token payload that is not base64url as `unexpected error`, or map `FormatException` to `invalid
   response` in the wrapper** — the first hides a malformed answer behind a generic reason; the second would also
   relabel a `UriFormatException`, which is a `FormatException`. The token cache raises it as a `JsonException`
   instead, the one place it can arise.

## Decision

Option 1: each client's wrapper answers every exception except a cancellation by the caller as *failed* with
`<failure message of the call>: <fixed reason>` and logs one warning with the exception; the Docker Hub image
configuration lookup uses the same wrapper. A Docker Hub access token payload that is not base64url is an invalid
response, and token expiry fields of the wrong type are read as absent. The rules are stated in
[Registries](../areas/registries.md#limits-and-errors).

## Consequences

- No error but the caller's cancellation and the argument guards before the wrapper leaves the nine calls; a
  programming error inside one is visible only in the log entry of the failure (3213 Docker Hub, 2100 OCI).
- A JSON value of an unexpected type where a client enumerates (a list item, the root of a body) reads
  `unexpected error`, not `invalid response`; a mistyped field a client looks up by name stays an unknown value.
- An OCI tag whose own lookup times out is listed by name only, so a listing against a registry that answers its tag
  pages but not its manifests takes up to one timeout per inspected tag instead of failing at the first.
- The Docker Hub image configuration lookup logs its failures as event 3213 instead of 3212, and reports a timeout
  with its seconds.
