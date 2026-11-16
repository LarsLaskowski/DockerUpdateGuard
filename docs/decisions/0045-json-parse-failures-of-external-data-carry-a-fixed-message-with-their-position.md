# 0045: JSON parse failures of external data carry a fixed message with their position

- **Status:** Proposed
- **Date:** 2026-11-16
- **Area:** Telemetry
- **Source:** Issue #393
- **Supersedes:** —

## Context

`System.Text.Json` words its parse errors with the input itself: an invalid literal is quoted together with the rest
of the buffer (the whole body for a parsed document, up to about 16 KB for the serializer), control characters
included, and the serializer adds a JSON path that can hold a decoded property name or dictionary key and keeps the
reader's exception, with the same quote, as inner exception. A remote side that answers `{"a":nul` followed by a line
break and a forged log line, 100,000 characters or an escape sequence therefore writes them into the log entry of the
failure: the integration clients, the vulnerability providers and the release feed log the exception they catch
([Telemetry](../areas/telemetry.md#credentials-and-external-data)), and so does every handler further up. Results and
scan runs are not affected: they carry the fixed reason `invalid response`
([0009](0009-integration-results-carry-fixed-failure-reasons.md)). Remote JSON is parsed in a dozen places: the Docker
Hub, OCI and Docker Engine clients through one helper ([0019](0019-remote-json-with-undecodable-strings-is-an-invalid-response.md)),
the Portainer client and Docker Scout through the HTTP content deserializer, the Docker Hub and Docker Scout token
payloads, the .NET release index and Trivy's report directly.

## Options considered

1. **Replace the parse failure where external JSON is parsed: every parse goes through one helper that rethrows a
   `JsonException` with a fixed message naming only the line and byte position, without path and inner exception**
   — one place decides for every log entry and handler, present and future, and the position is enough to find the
   fault in the answer. Chosen.
2. **Clean the exception where it is logged** — some 30 log events take an exception, several of them generic
   (background job, user action, container processing); one missed or new event leaks again.
3. **Bound and clean the parser's message with the excerpt rule of [Integration failures](../areas/integration-failures.md#excerpts-of-remote-text)
   instead of replacing it** — keeps the parser's wording, but still writes up to 500 characters of remote text into
   the log for no diagnostic gain over the position, and leaves the path and the inner exception to be treated too.
4. **Clean every exception in the logging pipeline (console formatter and OpenTelemetry log processor)** — would cover
   exception texts of every kind, but an exception's message cannot be changed after it is thrown, so the pipeline
   could only drop or reformat it, losing the stack traces of all errors; it changes the telemetry project for a
   problem that has one source.

## Decision

Option 1: a JSON parse failure of a remote answer, a token payload, the .NET release index or Trivy's report is
replaced where it is parsed by a `JsonException` with a fixed message that names only the parser's line and byte
position, with no path and no inner exception. The message is stated in
[Telemetry](../areas/telemetry.md#credentials-and-external-data).

## Consequences

- A log entry of such a failure no longer shows which token the parser expected; the position in the answer is what
  remains for diagnosis.
- A value of the wrong type in a deserialized answer is reported with the same message (no path), so the field is
  found by its position.
- The refusal of a string that does not decode ([0019](0019-remote-json-with-undecodable-strings-is-an-invalid-response.md))
  keeps its own fixed message and its decoding error, which carries no remote text.
- The helper's HTTP content read also refuses a `Content-Type` charset it cannot use with a fixed message and no inner
  exception, since the framework's refusal quotes the remote charset; it stays an unexpected error, not an invalid
  response, so no outcome changes, and this refusal too carries no remote text.
- A new parse of external JSON must use the helper; the Reviewer checks it ([`.squad/project.md`](../../.squad/project.md),
  *Integration surface*).