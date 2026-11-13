# Integration failures

## Scope

How every outbound integration (Docker Hub and the other registries, Docker Engine, Portainer, the vulnerability
providers and the release feeds) reports a failure: the outcomes a call answers with, the fixed reasons that replace
an error's own text, the bounded and cleaned excerpts of remote text, the cleaning of the text fields read from remote
answers, and the retry of transient HTTP failures shared by the HTTP integrations. The scans, the pages and the
operator's logs rely on it; each integration area states only what it adds.

## Behavior

### Outcomes

- An integration call answers with an outcome instead of raising an error for an expected failure. The outcomes are:
  - *succeeded* — the call returned data;
  - *not configured* — the feature lacks the configuration it needs;
  - *unsupported* — the integration cannot serve this target (for example a registry no client handles);
  - *not found* — the target does not exist (HTTP 404 for an account, repository, tag, manifest or blob);
  - *failed* — the call was made and did not succeed (any other non-success status, a missing token, a response
    without the expected content);
  - *unknown* — the outcome could not be classified.
- Every outcome other than *succeeded* carries a human-readable message. Callers decide on the outcome, not on errors;
  how a scan turns outcomes into scan-run statuses is described with each scan
  ([Base images](base-images.md), [Update detection](update-detection.md), [Scan engine](scan-engine.md)).

### Fixed failure reasons

- An outcome caused by an error rather than by an answer carries a fixed reason, never the error's own text:
  `cancelled`, `timed out after <n> seconds` (or `timed out` when the call has no timeout of its own), `connection
  failed` (transport failures and responses that break off), `invalid response` or `unexpected error`. The error
  itself is logged with the failure.
- `invalid response` covers malformed JSON and a JSON string that does not decode as Unicode text, such as a lone
  surrogate escape like `\ud800` or bytes that are not UTF-8. The Docker Hub, OCI and Docker Engine clients refuse the
  whole body for one in any string value or property name; the Portainer client and the vulnerability providers for one
  in a field they read; the release feeds report it as `unexpected error`.
- A message the application composed itself from fixed text, configuration names and masked URLs
  ([Telemetry](telemetry.md#credentials-and-external-data)) is passed on as it is; it never contains an error's own
  message, a response body or an unmasked URL.

### Excerpts of remote text

- A *failed* outcome from a registry names the HTTP status code and includes an excerpt of the response body: its first
  500 characters (decoded as UTF-8 unless a byte-order mark names another encoding), followed by `…` when the body was
  longer. An OCI reason phrase is cut to 100 characters the same way.
- In every excerpt (response body, reason phrase, rejected origin of a next-page link, Trivy's error line and Docker
  Scout's unknown severity value) each control character is replaced by one space after the cut: the characters of the
  Unicode categories Cc, Cf, Zl and Zp, that is line breaks, tabs, escape characters, bidirectional marks and
  zero-width characters including the joiners U+200C and U+200D (so an emoji sequence or a joined word can come out
  split by spaces). A code point outside the Basic Multilingual Plane is replaced by a single space, and a lone
  surrogate becomes U+FFFD. An excerpt therefore never spans lines in a log or reorders a page.
- The cut is decided on the text as received, before the replacement, so `…` follows exactly when the received text
  was longer than the limit.
- The message length is bounded everywhere; the read itself is bounded (about 8 KiB) only for the streamed OCI and
  Docker Hub responses, while Docker Engine and feed responses are read whole first.
- After a failed OCI or Docker Hub token exchange the excerpt is of the token failure message, which names the token
  request URI (OCI) or the Docker Hub user name before the token endpoint's own body excerpt, so that body may be cut
  away.
- A listing stopped by a rejected next-page link has neither a status code nor a body and names the rejected origin
  instead; a listing stopped by a repeated link or the page cap has neither a status code, a body nor an origin
  ([Registries](registries.md#pagination-links)).

### Remote text fields

The text fields a client reads from a remote answer and passes on to its callers get the replacement of an excerpt
(the characters of the Unicode categories Cc, Cf, Zl and Zp become one space each; a lone surrogate becomes U+FFFD only
in text that is not a JSON string, because a JSON answer with an undecodable string is an invalid response instead).
The replacement happens where the client reads the field and, apart from the names that
[Docker instances](docker-instances.md) cuts to their stored length and the vulnerability values that the refresh
cuts where it stores them ([Vulnerabilities](vulnerabilities.md#limits-and-errors)), without a cut: the length stays as received, a
field without such a character is unchanged, and the replacement runs on the decoded string, so a JSON escape cannot
bypass it. Logs, stored values and pages therefore never carry these characters from these fields:

- **Docker Engine** ([Docker instances](docker-instances.md)): of a listed container the ID, name, image reference,
  local image ID and the compose project, stack name and swarm service name labels, and the ID and name of a resource
  sample; of an image inspect the ID, repo tags, repo digests, environment variables, root file-system layers,
  operating system and architecture; of a history entry the created-by command, comment and tags. An item of a list
  that consists only of such characters is dropped like a blank one.
- **Registries** ([Registries](registries.md)): of an image configuration the environment variables, the label values
  (the label names stay as received, they are only looked up by fixed names), operating system and architecture; the
  values of `org.opencontainers.image.base.name` and `org.opencontainers.image.base.digest`, before they are parsed
  ([Base images](base-images.md)); of Docker Hub the repository name built from namespace and name, the description of
  a listed or a single repository, and the current user's name, in the result and in its log entry.
- **Vulnerability providers** ([Vulnerabilities](vulnerabilities.md)): of Trivy the advisory id, title, package,
  installed and fixed version, package class and type, summary and reference URL; of Docker Scout the advisory id,
  title, summary and reference URL.

The replacement happens before any trimming, so a reference whose replaced characters sit only at its edges resolves to
the reference without them (`nginx:1.25` followed by an escape character reads as `nginx:1.25`).

Not cleaned: tag names and digests (held to a grammar without such characters,
[Registries](registries.md#what-can-be-read)), bearer tokens and next-page links (consumed by the client; the guard of
[Pagination links](registries.md#pagination-links) keeps its input), manifest-list platforms (used only to select a
digest), Portainer text (environment names have their own rule, [Container actions](container-actions.md)),
release-feed text (never reaches a log, a message or a stored value), and values stored before the replacement existed,
which stay until they are written again.

### Retry of transient HTTP failures

- Applies to Docker Hub (web API, login, token service, registry), other registries and their token services, the
  .NET and nginx release feeds ([Base images](base-images.md)) and Portainer
  ([Container actions](container-actions.md)). It does not apply to Docker Engine connections
  ([Docker instances](docker-instances.md)) or to Docker Scout ([Vulnerabilities](vulnerabilities.md)).
- Transient are the responses 408, 429 and every 5xx, and transport errors and timeouts raised while sending. Other
  statuses (including 401, 403 and 404) and cancellation are not retried.
- A request is retried up to `DockerUpdateGuard:Scanning:RetryCount` times (default 2, range 0–10; 0 disables
  retries). The value is read for every request, so a configuration reload applies at once.
- The wait before a retry is the response's `Retry-After` (delta or date) when positive, capped at 30 seconds;
  otherwise 500 ms × 2^n for the n-th retry (n starting at 0), capped at 30 seconds, plus a random jitter of up to 20 %
  of that value, the total again capped at 30 seconds.
- `GET`, `HEAD`, `OPTIONS`, `TRACE`, `PUT` and `DELETE` are retried as described above. Every other method (`POST`,
  `PATCH`, any custom method) is not idempotent: a repeat could run its effect twice, so it is retried only on a 429
  whose `Retry-After` is missing or asks for at most 30 seconds, and after a send error that shows no request byte was
  sent (the host name did not resolve, the connection could not be established, the TLS handshake or the proxy tunnel
  failed). A 5xx, a 408, a timeout or a connection that broke after it was established ends the request after one
  attempt: the response is returned or the error raised. A 429 whose `Retry-After` asks for more than 30 seconds is
  returned after one attempt as well, so the request is never resent inside the window the server named. The requests
  concerned are the Docker Hub login and the Portainer login and actions.
- A non-idempotent request always carries a body, an empty one if it has nothing to send, so the HTTP stack never
  resends it on its own when a reused connection closes after the server read it.
- A request body is buffered before the first attempt, so a retry resends the identical body and a body of unknown
  length is sent with a `Content-Length`.
- When the retries are used up the last response is returned to the caller, or the last error is raised.
- A request's timeout, set by its integration ([Registries](registries.md#request-timeout),
  [Container actions](container-actions.md)), covers all its retries and waits; a request that runs out of time is not
  retried.
- Each retry is logged as a warning with the method, the URL without its user info and query, the status or error, the
  attempt number and the wait. A transient outcome that is not retried because the method is not idempotent is logged
  as a debug entry (3502) with the method, the masked URL and the status or error name. A 429 that is not retried
  because its `Retry-After` exceeds 30 seconds is logged as a debug entry (3503) with the status, the method, the masked
  URL, the requested wait and the 30-second maximum.

### Logins inside a Retry-After window

- Covers the three logins that send a secret: the Docker Hub web API login (`v2/users/login`), the Portainer login
  (`/api/auth`, only for an instance with user name and password) and the Docker Scout login
  (`DockerScoutLoginUrl`). Logins to the Docker Hub registry token service are not covered; they are idempotent and keep
  the wait of [Retry of transient HTTP failures](#retry-of-transient-http-failures).
- A window is kept per login URL (scheme, host, port and path; user info, query and fragment are ignored) and user name
  (compared ordinally; the Docker Hub client and the Docker Scout provider use the trimmed name, Portainer the name as
  configured). The Docker Hub client and the Docker Scout provider therefore share a window when `DockerScoutLoginUrl`
  is the Docker Hub login URL (the default) and the user name is the same. A Docker Scout login URL that is not an
  absolute URI is not tracked.
- A login response of 429 with a positive `Retry-After` (seconds or date) opens a window that ends at the current time
  plus that wait; the wait is not capped. A later window never shortens a longer one. A 429 without a usable
  `Retry-After` and any other failure (401, 503, …) open no window and leave an existing one as it is. A successful
  login removes the window; a window that has passed is removed when it is next read.
- Inside a window no login and no request that needs the login is sent. The operation ends as a failure naming the end
  of the window as UTC `yyyy-MM-ddTHH:mm:ssZ`:
  - Docker Hub web API and Docker Scout: *failed* with `Docker Hub authentication token request for '<user>' not sent:
    Docker Hub answered HTTP 429 and asked to wait until <until>`.
  - Portainer: the authentication error `Portainer authentication for '<masked BaseUrl>' not sent: Portainer answered
    HTTP 429 and asked to wait until <until>` ([Container actions](container-actions.md#authentication)).
- Once the clock reaches the end of the window the next login is sent as usual.
- The windows live in memory in one process-wide `LoginRetryAfterRegistry` shared by all clients and scopes; a restart
  forgets them. Only the final response the client receives is recorded: a 429 the retry handler waits out and
  retries opens no window.
  Decision [0022](../decisions/0022-a-logins-retry-after-window-is-remembered-process-wide.md).

## Limits and errors

- No outcome message, scan run or page carries an error's own text; the error is in the log entry of the failure.
- Some registry calls still let errors escape instead of answering an outcome; which ones is listed in
  [Registries](registries.md#limits-and-errors).

## Related decisions

- [0008](../decisions/0008-non-idempotent-requests-retry-only-when-not-processed.md): non-idempotent requests are
  retried only when they cannot have been processed.
- [0009](../decisions/0009-integration-results-carry-fixed-failure-reasons.md): integration results carry fixed
  failure reasons, never an error's own text.
- [0022](../decisions/0022-a-logins-retry-after-window-is-remembered-process-wide.md): a login's Retry-After window is
  remembered process-wide.
- [0019](../decisions/0019-remote-json-with-undecodable-strings-is-an-invalid-response.md): remote JSON with a string
  that does not decode is an invalid response.

## Not here

- What each integration reads and when it answers *not found* or *failed*: [Registries](registries.md),
  [Docker instances](docker-instances.md), [Container actions](container-actions.md),
  [Vulnerabilities](vulnerabilities.md), [Base images](base-images.md).
- How URLs and credentials are masked in logs and messages: [Telemetry](telemetry.md#credentials-and-external-data).
- How the pages report a failed user action: [Web UI](web-ui.md#limits-and-errors).
