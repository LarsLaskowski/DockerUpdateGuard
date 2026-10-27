# 0009: Integration results and page actions carry fixed failure reasons, never exception messages

- **Status:** Accepted
- **Date:** 2026-10-25
- **Area:** Registries
- **Source:** Issue #315, issue #318, issue #323, issue #324, issue #347, issue #353
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

Bounding the length left the content as the remote side sent it (#347): a line break, a carriage return or an escape
sequence in a body, a reason phrase or Trivy's error line went into plain-text log output, where it can start what
reads as a new log entry or rewrite the terminal line, and a bidirectional override or a zero-width character went into
the messages the pages show, where it reorders or hides the text around it.

The same characters arrive in the text fields of remote answers (#353), which never passed that helper: container
labels, environment variables and history entries a Docker image's author writes, Docker Hub repository descriptions,
the base-image label of an image configuration, and the package names, versions, titles and summaries a vulnerability
scanner reports for an image. They are logged (container names and image references, repository names, base-image
references), stored and shown on the pages.

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
16. **For control characters in remote text: replace each one with a space in the shared helper that already bounds
    every such text, after the cut** — one place covers the body excerpts, the reason phrase, a rejected link's origin
    and Trivy's line, and every log entry, result, stored message and page that carries them; the length never grows,
    so the bounds of #324 hold unchanged. Chosen.
17. **Remove the characters instead** — joins the words on both sides of a line break and leaves no trace that
    something was there.
18. **Write them as visible escapes (`\u000A`)** — keeps the information, but one character becomes six, so the
    text has to be bounded again after the escaping, and a pretty-printed JSON body becomes hard to read.
19. **Clean the text where it is written (a log processor, the console formatter, the pages)** — a log processor
    sees the formatted message only in some providers, the stored messages and scan-run errors need a cut of their own
    each, and every new sink has to remember it; the sources are few and already pass one helper.
20. **Replace only line breaks** — leaves escape sequences, bidirectional overrides and zero-width characters, which
    distort a terminal or a page without breaking a line.
21. **Replace before the cut** — a removed character outside the Basic Multilingual Plane shortens the text by one,
    so a body one character over the limit could lose its `…` and look complete.
22. **For text fields of remote answers: the same replacement, without a cut, where each client reads a field it
    passes on to its callers** — every log entry, stored value and page built from the field is covered, whoever reads
    it later, as [0014](0014-registry-tag-names-and-digests-are-validated-where-they-are-read.md) validates tag names
    where they are read; the length stays as received, so the bounds of #348 and #355 are unaffected. Chosen.
23. **Clean in the data types that carry the fields (their setters)** — catches every constructor, but hides the rule
    in plain data classes and also rewrites values a test or the application sets itself.
24. **Clean only the fields whose source can carry such characters** (labels, environment, history, descriptions,
    advisory texts), trusting the grammars Docker applies to container names, image references and IDs — the clients
    do not check those grammars, and an endpoint that is not a genuine Docker Engine is not held to them; the
    replacement costs nothing on a clean value.
25. **Clean every string a client reads, the bearer tokens and next-page links it consumes itself included** —
    changes the input of the pagination guard ([0004](0004-pagination-follows-only-same-origin-links.md)) and of a
    credential, neither of which leaves the client as data.
26. **Validate the fields and drop a value with such characters** — only names and references have a grammar; labels,
    descriptions and advisory texts are free text, and validating lengths and grammars is #355.
27. **Keep line breaks in multi-line advisory summaries** — a second rule for one field, which the pages show only as a
    tooltip; a line break there is harmless, but the other characters are not.
28. **Clean label names as well as values** — the labels are looked up by fixed names only, and two names that differ
    only in such a character would collide.

## Decision

Every exception an integration client or a scan orchestrator turns into a failure message is reduced to one of a fixed
set of reasons by its type; only an exception the application raises with a message it composed itself, marked by a
dedicated exception type, keeps its text. The exception itself is logged with the failure. The reasons are listed in
[Registries](../areas/registries.md#failure-model-of-integrations); the Portainer specifics in
[Container actions](../areas/container-actions.md#limits-and-errors). A failed user action on a page shows the action
and the same fixed reason and logs the exception ([Web UI](../areas/web-ui.md#limits-and-errors)); the registration
service raises the parser's rejection of a reference without a repository name as the marker type. A response body,
a reason phrase, the origin of a rejected next-page link and Trivy's error line reach a failure message only cut to a
bounded length by one shared helper ([Registries](../areas/registries.md#failure-model-of-integrations)), which also
replaces every control character in them with a space after the cut. Every other text field a client takes from a
remote answer and passes on gets the same replacement, without a cut, where the client reads it; exempt are the values
a client consumes itself (tokens, next-page links) and those already held to a grammar without such characters (tag
names and digests). The fields are listed in [Registries](../areas/registries.md#remote-text-fields).

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
  `#` or `?` in the user info, `https://admin#secret@host` connects to `admin`). Configuration validation rejects such URLs
  since [0012](0012-configured-urls-are-rejected-when-an-at-sign-is-not-their-user-info-separator.md), which also
  stops requests to that wrong host. Proxy URLs taken from the environment
  appear in these messages without their user info.
- Response bodies the remote side sent and the error output of Trivy are not exception messages; they stay in results.
  A body is cut to its first characters, so a message no longer shows the end of a long answer; the operator who needs
  it reproduces the request. The excerpt is decoded as UTF-8, or as the encoding a byte-order mark names, whatever charset the response declares.
- The message length is bounded for every client; the bytes read are bounded only where the response is streamed (the
  registry clients). Docker Engine and release-feed responses are still buffered whole by `HttpClient` before the
  excerpt is taken.
- A remote text loses its line structure and its formatting characters: a multi-line body reads as one line, and an
  error text whose meaning depends on a tab or a zero-width joiner is shown with a space there. The same holds for the
  remote text fields: a multi-line advisory summary reads as one line in its tooltip, and an environment variable or
  label value keeps its length but not its formatting characters.
- Values stored before #353 keep their characters until they are written again: container snapshots at the next
  runtime scan, findings at the next vulnerability refresh, discovered images' descriptions at the next account
  discovery; catalog entries and base-image relationships created from such a value stay as they are. A value that
  differs from another only in such a character now equals it, so the two collapse into one catalog entry or finding.
- A cleaned image reference or ID no longer names what the remote side named, so a follow-up request with it (an image
  inspect) answers *not found*; a genuine Docker Engine does not report such values.
- The replacement runs before any trimming, so a reference whose replaced characters sit only at its edges now
  resolves to the reference without them (`nginx:1.25` followed by an escape character becomes `nginx:1.25`).
- Only Trivy's title falls back when its cleaned value is blank. A `PrimaryURL` made only of control characters is kept
  as blanks instead of falling back to `References`: the pages show no link for a blank URL, nothing is exposed, and
  before #353 such a value did not fall back either.
- Portainer environment names keep their own rule from
  [0010](0010-portainer-environment-is-used-only-when-unambiguous.md) (`?` instead of a space, cut to 64 UTF-16 units).
- After a failed registry token exchange the excerpt is taken of the token failure message, which names the token request URI (OCI) or the Docker Hub user name before the token endpoint's own body excerpt, so that body may be cut away.
- A page no longer tells a user why an action failed beyond the fixed reason; a registration with a value longer than
  the stored limits, or a tag selection for a container whose snapshots were just cleaned up, reads "unexpected
  error", and the operator reads the cause in the log entry of the failure.
