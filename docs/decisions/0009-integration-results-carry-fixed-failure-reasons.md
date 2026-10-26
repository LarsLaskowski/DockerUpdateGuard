# 0009: Integration results and page actions carry fixed failure reasons, never exception messages

- **Status:** Accepted
- **Date:** 2026-10-25
- **Area:** Registries
- **Source:** Issue #315, issue #318, issue #323, issue #324
- **Supersedes:** —

## Context

The integration clients — Portainer, Docker Engine, the registries, the release feeds, Trivy and Docker Scout — and the
scan orchestrators caught every exception and put its message into the result or the scan run as it stood. Those
messages come from the runtime, not from the application: a refused connection reads
`Connection refused (<host>:<port>)`, a broken response `Error while copying content to a stream.`, a timeout names
the client setting it elapsed, malformed JSON quotes the parser's position, a process that cannot start names the
executable and the working directory. They name internal hosts and ports, mean nothing to an operator reading a
result, and change with the runtime version. Results end up in scan-run errors, container assessments and the pages,
and the application has no login (`.squad/project.md`, *Guarantees*), so whatever a result carries is visible to anyone
who reaches the UI. A few exception messages are composed by the application itself — the Portainer authentication
failure (masked base URL, HTTP status, [0005](0005-urls-are-output-with-user-info-masked.md)), the unusable client
certificate and the named-pipe URI without a pipe name of a Docker instance — and are safe and useful.

The pages had the same defect one level up (#323): the user actions — the per-image rescan, the update check, the
vulnerability refresh, the registration of an observed image and the manual tag selection — caught every exception that
escaped a service (a database error, a failure outside the clients' own handling) and showed its message on the page,
without logging it. One message there is the application's own and useful: the image reference without a repository
name, which the reference parser raises as an `ArgumentException`.

A non-success answer is not an exception: the Docker Engine client, both registry clients and the two release-feed
readers appended the whole response body the remote side sent to the failure message (#324). Other remote text went in
just as unbounded: the OCI reason phrase, the origin of a rejected next-page link (registries and Docker Scout) and the
last line of Trivy's error output. None has a size limit, so a long one made the message longer than the 2000
characters a container's update assessment or an image's vulnerability assessment holds, and the save of the scan
failed; the same text went unbounded into logs and scan-run errors.

## Options considered

1. **One shared mapping from the exception's type to a fixed reason, and one exception type that marks a message the
   application composed itself, whose text is passed on** — the result text is fully determined by the application;
   the runtime's details stay in the log entry of the failure, which keeps the exception. Chosen.
2. **A private mapping per client (as first built for Portainer)** — eight copies of the same rules that drift apart
   as soon as one client learns a new case.
3. **Sanitize the exception message (strip hosts, URLs, ports)** — a deny-list over free text the runtime may reword
   at any release; one missed form leaks again.
4. **Pass on the messages of selected exception types only** — still runtime text, still naming hosts for the most
   common failure (connection refused). Keeping `InvalidOperationException` as the marker of composed messages fails
   the same way: the runtime throws it too.
5. **Map only in the orchestrators** — one place, but the clients' results also reach logs and other callers
   unmapped, and an orchestrator cannot know which client's timeout elapsed.
6. **Report authentication failures without throwing (a client-or-reason result from the login step)** — avoids the
   marker type, but reworks the Portainer login flow and its tests beyond what the bug needs.
7. **Rethrow cancellation by the caller instead of reporting it** — changes which calls throw; the clients keep their
   cancellation behavior, and a call that reports a caller cancellation reports it as "cancelled".
8. **Drop the exception from the error log entries as well** — the log is the operator's only way to learn why a
   connection failed; a would-be host from a malformed URL is better rejected where the URL is configured (follow-up
   below).
9. **For the pages: a generic text without a reason ("The action failed")** — hides the distinction between a
   connection failure and an unexpected error that the shared mapping gives for free.
10. **For the pages: the shared mapping alone** — the invalid image reference would read "unexpected error", the one
    page error a user can fix by changing the input.
11. **For the pages: let the reference parser raise the marker type** — changes the exception type for every caller
    of the parser (the Docker Engine client catches its `ArgumentException`); the registration service, the one
    caller whose failure reaches a page, wraps it instead.
12. **For remote text: one shared bound applied where the text enters a message — a body excerpt read from the
    response instead of the whole body, and the same cut on the reason phrase, a rejected link's origin and Trivy's
    error line** — every result, log entry and scan-run error stays short, and a streamed error response is no longer
    read to its end. Chosen.
13. **For remote text: cut the message where it is stored, alone or in addition** — logs and scan-run errors still
    carry the whole text, a body is still read in full, and each of the stores needs its own cut. A first sweep of the
    sources missed the rejected origin and Trivy's line, so the sources of each stored message are enumerated part by
    part and their sum is tested at the client instead; a cut at the store would also hide a future unbounded source
    that the log should show.
14. **For response bodies: drop the body** — the body is often the only explanation of a registry or Engine refusal
    (an error code, a rate-limit note); it is the remote side's answer, not runtime text, so a bounded part of it is
    kept.
15. **For response bodies: decode with the charset the response declares** — the remote sides here answer UTF-8 JSON
    or ASCII pages; an unknown charset name would need its own fallback for a text that is only an excerpt.

## Decision

Every exception an integration client or a scan orchestrator turns into a failure message is reduced to one of a fixed
set of reasons by its type; only an exception the application raises with a message it composed itself, marked by a
dedicated exception type, keeps its text. The exception itself is logged with the failure. The reasons are listed in
[Registries](../areas/registries.md#failure-model-of-integrations); the Portainer specifics in
[Container actions](../areas/container-actions.md#limits-and-errors). A failed user action on a page shows the action
and the same fixed reason and logs the exception ([Web UI](../areas/web-ui.md#limits-and-errors)); the registration
service raises the parser's rejection of a reference without a repository name as the marker type. A response body,
a reason phrase, the origin of a rejected next-page link and Trivy's error line reach a failure message only cut to a
bounded length by one shared helper ([Registries](../areas/registries.md#failure-model-of-integrations)).

## Consequences

- A result no longer tells *why* a connection failed (refused, name resolution, TLS); the operator reads that in the
  log entry of the failure, which carries the exception. Calls that did not log their failure before now log it.
- A new failure mode the mapping does not know reads "unexpected error" until it gets its own reason; a composed
  message is passed on only when it is raised with the marker type.
- The Portainer authentication failure still names the configured Portainer host and port (only the user info is
  masked), so anyone who reaches the UI can read that host from a failed login. This is accepted: the same masked base
  URL is already stored as the Portainer endpoint and Docker instance endpoints are shown on the Docker Instances page
  ([0005](0005-urls-are-output-with-user-info-masked.md), chosen so operators see which host is meant); the host is
  configuration, not a secret, and hiding it would make the one actionable failure unreadable.
- Caller cancellation is reported as "cancelled" where a call reported it before (Portainer, the release feeds, the
  orchestrators) and still thrown where it was thrown before; #280 decides what an action run records for it.
- Log entries keep the exception, and its runtime message can name a would-be host that a malformed URL produced (a
  `#` or `?` in the user info, `https://admin#secret@host` connects to `admin`). Rejecting such URLs at configuration
  validation is a follow-up issue; it also stops requests to that wrong host. Proxy URLs taken from the environment
  appear in these messages without their user info.
- Response bodies the remote side sent and the error output of Trivy are not exception messages; they stay in results.
  A body is cut to its first characters, so a message no longer shows the end of a long answer; the operator who needs
  it reproduces the request. The excerpt is decoded as UTF-8 whatever charset the response declares.
- The message length is bounded for every client; the bytes read are bounded only where the response is streamed (the
  registry clients). Docker Engine and release-feed responses are still buffered whole by `HttpClient` before the
  excerpt is taken.
- After a failed registry token exchange the excerpt is taken of the token failure message, which starts with the
  token request URI, so the token endpoint's own body may be cut away.
- A page no longer tells a user why an action failed beyond the fixed reason; a registration with a value longer than
  the stored limits, or a tag selection for a container whose snapshots were just cleaned up, reads "unexpected
  error", and the operator reads the cause in the log entry of the failure.
