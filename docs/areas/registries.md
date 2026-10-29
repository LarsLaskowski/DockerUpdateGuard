# Registries

## Scope

Read access to container registries: Docker Hub (its web API and its registry) and every other registry that speaks the
OCI Distribution API. Covers which client serves which registry, what can be read (tags, manifests, image
configuration and labels), how DockerUpdateGuard authenticates, the Docker Hub account image discovery, the Docker Hub
request budget and parallelism, the retry of transient HTTP failures shared by the outbound HTTP integrations, and the
failure model every integration uses. The image catalog, base-image resolution and update detection rely on it for
registry data; operators rely on it to configure credentials and limits; the other integration areas link here for the
retry policy and the failure model.

## Behavior

### Failure model of integrations

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
- A *failed* outcome from a registry names the HTTP status code and includes an excerpt of the response body the remote side sent:
  its first 500 characters (decoded as UTF-8 unless a byte-order mark names another encoding), followed by `…` when the body was longer; an OCI reason phrase is cut to
  100 characters the same way. In every such excerpt (response body, reason phrase, rejected origin, Trivy's error
  line and Docker Scout's unknown severity value) each control character is replaced by one space after the cut: the characters of the Unicode categories
  Cc, Cf, Zl and Zp, that is line breaks, tabs, escape characters, bidirectional marks and zero-width characters
  including the joiners U+200C and U+200D (so an emoji sequence or a joined word in an excerpt can come out split by
  spaces), and a code point outside the Basic Multilingual Plane is replaced by a single space. A lone surrogate becomes U+FFFD.
  The cut is decided on the text as received, before the replacement, so `…` follows exactly when the received text was
  longer than the limit. An excerpt therefore never spans lines in a log or reorders a page; the text fields of remote
  answers get the same replacement without a cut ([Remote text fields](#remote-text-fields)). This subsection and that
  one together state the rule. The message length is bounded everywhere, but the read is bounded (about 8 KiB) only for
  the streamed OCI and Docker Hub responses; Docker Engine and feed responses are buffered by `HttpClient` first. After a failed OCI or Docker Hub token exchange the excerpt is of the token failure message, which names the token request URI (OCI) or the Docker Hub user name before the token endpoint's own body excerpt, so that body may be cut away.
  The exception is a listing stopped by a rejected next-page link: it has neither a status code nor a body and names the
  rejected origin instead ([Pagination links](#pagination-links)).
- An outcome caused by an error rather than by an answer carries a fixed reason, never the error's own text:
  `cancelled`, `timed out after <n> seconds` (or `timed out` when the call has no timeout of its own), `connection
  failed` (transport failures and responses that break off), `invalid response` (malformed JSON) or `unexpected
  error`. A message the application composed itself from fixed text, configuration names and masked URLs is passed on:
  it is raised as a `ReportableException`, a type whose message must never interpolate an error's message, a response
  body or an unmasked URL (a reviewer treats such a use as blocking). The error is logged with the failure
  ([record 0009](../decisions/0009-integration-results-carry-fixed-failure-reasons.md)).

### Remote text fields

The text fields a client reads from a remote answer and passes on to its callers get the same replacement as an excerpt
(the characters of the Unicode categories Cc, Cf, Zl and Zp become one space each, a lone surrogate becomes U+FFFD),
where the client reads the field and, apart from the names that [Docker instances](docker-instances.md) cuts to their
stored length, without a cut: the length stays as received, a field without such a character is unchanged, and the replacement runs on the decoded string, so a JSON escape cannot bypass it. Logs, stored values and
pages therefore never carry them from these fields:

- **Docker Engine** ([Docker instances](docker-instances.md)): of a listed container the ID, name, image reference,
  local image ID and the compose project, stack name and swarm service name labels, and the ID and name of a resource
  sample; of an image inspect the ID, repo tags, repo digests, environment variables, root file-system layers,
  operating system and architecture; of a history entry the created-by command, comment and tags. An item of a list
  that consists only of such characters is dropped like a blank one.
- **Registries** (OCI and Docker Hub clients): of an image configuration the environment variables, the label values
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
[Bounded tag listings](#bounded-tag-listings) and [What can be read](#what-can-be-read)), bearer tokens and next-page
links (consumed by the client, and the guard of [Pagination links](#pagination-links) keeps its input),
manifest-list platforms (used only to select a digest), Portainer text (environment names have their own rule,
[Container actions](container-actions.md)), release-feed text (never reaches a log, a message or a stored value), and
values stored before the replacement existed, which stay until they are written again
([record 0009](../decisions/0009-integration-results-carry-fixed-failure-reasons.md)).

### Choice of client per registry

- The registry of an image reference (see [Image catalog](image-catalog.md)) decides which client serves it. The Docker
  Hub client serves the hosts `docker.io`, `index.docker.io`, `registry-1.docker.io`, `hub.docker.com`, `docker.com`
  and `www.docker.com`, compared case-insensitively; a registry given as an absolute URL is matched by its host.
- Every other non-empty registry is served by the generic OCI client. Only an empty registry is *unsupported*.
- The Docker Hub client uses three endpoints: the web API at `DockerUpdateGuard:DockerHub:ApiBaseUrl` (default
  `https://hub.docker.com/`), the token service `https://auth.docker.io/token` and the registry
  `https://registry-1.docker.io`. A repository without a namespace is addressed as `library/<name>`.
- The OCI client addresses `https://<registry>/v2/<repository>/…`, with the registry as a host and an optional port;
  registry names are compared lowercased and keep their port. A registry that is not a host with an optional port is
  refused ([Request paths](#request-paths)).
- Outbound requests to registries and release feeds carry the user agent `DockerUpdateGuard/1.0`.

### Request paths

- Every segment of a registry request path is percent-encoded. `System.Uri` removes `.` segments and resolves `..`
  segments even when they are written as `%2E`, so encoding cannot keep them; a path whose repository, tag, digest or
  reference has a segment that is exactly `.` or `..` is therefore never sent. A segment that contains dots but is not
  exactly `.` or `..` (`my.app`, `...`) is requested as it is, and percent text in a segment is itself encoded (`%2E%2E`
  is sent as `%252E%252E`).
- The registry is checked before the path: it must be a DNS host name, an IPv4 address or a bracketed IPv6 address with
  an optional port, at most 200 characters in all (the reference grammar of Docker, ASCII only), and `https://<registry>/`
  must parse with that host. A registry with a `?`, `#`, `\`, `%`, a space or a second `:` outside brackets, a scheme or user info
  (`https://ghcr.io`, `user:pw`), an empty label, a port that is not a number or above 65535, or the port 0 is never
  addressed. Any port from 1 to 65535 on the named host is reachable, over HTTPS and without stored credentials. The
  Docker Hub client matches an absolute URL by its host and is not affected.
- The path rule applies to the repository (including the Docker Hub namespace), to the tag or digest of the reference, and
  to the digests a manifest names (the selected manifest-list entry and the configuration blob). Nothing is logged and
  no request is sent, not even a token request.
- The outcome is that of a 404 for the lookup: the OCI manifest, tag listing and configuration lookups, the Docker Hub
  web API lookups (repository, tag, tag listing) and the Docker Hub configuration lookup with a repository, tag or
  reference of that kind answer *not found* with the message `Registry request path would contain a '.' or '..'
  segment`; the OCI lookups with an invalid registry answer *not found* with `Registry is not a valid host with an
  optional port`, sending no request and logging nothing. A base-image resolution ends its chain without base images and succeeds. A digest named by a manifest ends
  the chain (base images), answers *not found* with the same message (OCI configuration, OCI manifest-list entry),
  gives *failed* "Image configuration blob could not be read for '<reference>'" (Docker Hub configuration blob) or
  *not found* "Config blob digest could not be resolved for '<reference>'" (Docker Hub manifest-list entry).
- The vulnerability lookup of Docker Scout follows the same rule and escapes each segment of its path
  ([Vulnerabilities](vulnerabilities.md#docker-scout)).
- Not covered: the account name of the account listing and the Docker Engine requests.

### What can be read

- **Tag lookup** — digest and publication time of one tag.
  - Docker Hub: the web API's tag record (the digest Docker Hub reports and its `last_pushed` time).
  - Other registries: a manifest request for the tag. The digest is the `Docker-Content-Digest` of the first response
    (for a multi-platform index, the index digest). The publication time is known only when the manifest carries a
    top-level `created` timestamp; otherwise it is unknown.
  - Both clients accept only a requested tag that matches the OCI tag grammar (1 to 128 characters of
    `[A-Za-z0-9_][A-Za-z0-9_.-]*`, ASCII only); for any other tag the lookup is answered *not found*, "Requested tag
    is not a valid registry tag", without a request. Docker Hub reports the requested tag, not the `name` of the
    record. A digest is accepted only when it matches the digest grammar of the OCI image specification
    (`algorithm:encoded`, ASCII only) and has at most 255 characters; any other digest is unknown (no digest).
- **Image configuration** — environment variables, labels (keys compared case-insensitively), creation time, operating
  system and architecture, read from the image's configuration blob. For a multi-platform index one platform manifest
  is chosen:
  - Docker Hub: the first `linux` entry, skipping entries whose operating system or architecture is `unknown`
    (attestations); otherwise the first remaining entry.
  - Other registries: the entry matching the caller's operating system and architecture, then the first entry matching
    the architecture, then the first entry with a digest. A caller that passes no platform gets the first entry.
- **Tag listing** — the tags of a repository with digest and publication time.
  - Docker Hub: web API pages of 100 tags, in the order Docker Hub returns them (most recently pushed first), following
    the `next` link of each page ([Pagination links](#pagination-links)).
  - Other registries: `tags/list` pages of 100 names following the `Link: rel="next"` header ([Pagination links](#pagination-links)); every tag is then
    inspected with a tag lookup. A tag whose lookup does not succeed is still listed, by name only.
  - A name outside the tag grammar is skipped without a log entry: a Docker Hub record with such a `name` is left out,
    and a name of an other registry's `tags/list` is neither listed nor inspected. A digest outside its grammar leaves
    the tag listed without a digest. A skipped name does not count toward the maximum count.

### Bounded tag listings

- A caller may bound a listing with the current tag, the current digest, a minimum version tag, a version-line tag, a
  published-since time and a maximum count (default 250 when bounds are given). Without bounds every tag is read.
- The current tag is always kept. With a version-line tag only tags of that version line are kept (version lines and
  version comparison: [Update detection](update-detection.md)). A tag published before the published-since time, or
  lower than the minimum version, is dropped unless its digest equals the current digest; a tag that cannot be
  compared with the minimum version is kept.
- Docker Hub: the listing stops once the maximum count of kept tags is reached, and at the first tag older than the
  published-since time — except when the current tag is `latest` with a known digest and no specific version tag
  with that digest has been seen yet.
- Other registries: the maximum count limits the number of inspected tags; a tag that fails the name checks (current
  tag, version line, minimum version) is not inspected.

### Pagination links

- A listing follows the next-page link of a response (`next` on Docker Hub, `Link: rel="next"` elsewhere) only when
  it stays on the origin of the listing's first request: the same scheme, host (compared case-insensitively) and
  port. For Docker Hub that origin is the one of `DockerHub:ApiBaseUrl` (or of an absolute `DockerHub:Registry`
  that replaces it); for other registries it is the origin of the first `tags/list` request. A mirror or proxy in
  front of a registry must rewrite its next-page links to its own origin, otherwise the listing fails.
- A link is resolved against the URL of the request that returned it, so a root-relative (`/v2/<repository>/tags/list?...`)
  or path-relative link stays on the same origin and is followed. A link to another host or port, to `http` when the
  listing uses `https`, or with another scheme is not followed and no request is sent to it, so the Docker Hub bearer
  token never leaves the configured origin. A link that cannot be parsed is treated the same way.
- A rejected link makes the listing *failed*, and the entries read before are discarded. The message is
  `<listing> stopped: the next-page link pointed to a different origin (<origin>)`, for example
  `Docker Hub tag listing for 'acme/api' stopped: the next-page link pointed to a different origin (https://attacker.example)`;
  the same origin is logged as a warning. The origin text is the scheme, host (in punycode) and, unless it is the
  default, the port; user info, path and query of the link are never written. A link that cannot be parsed is
  described as `unparseable link`. The origin text is cut to 300 characters followed by `…` and cleaned like an excerpt ([Failure model](#failure-model-of-integrations)).
- The guard applies only where a listing would follow the link: a listing that stops on its own condition (a Docker
  Hub bound, the maximum tag count) does not look at it, and an empty link ends the listing as before.
- Not covered: the token service a `WWW-Authenticate` challenge names, and HTTP redirects.
- Decision: [0004](../decisions/0004-pagination-follows-only-same-origin-links.md).

### Authentication

- **Docker Hub web API.** Anonymous unless `DockerUpdateGuard:DockerHub:Pat` is set. With `Pat` and `UserName` (both
  trimmed) the client logs in and sends the returned token as a bearer token. The token is kept in memory and renewed
  one minute before its expiry (its `exp` claim, or five minutes after issue when it has none). `Pat` without
  `UserName` makes every web API request *failed*, naming the missing key. `UserName` without `Pat` is anonymous.
- **Docker Hub registry.** A pull token is requested from the token service for every lookup, with the configured user
  name and PAT as basic credentials when both are set, otherwise anonymously.
- **Other registries.** Access is anonymous only. On a 401 with a `Bearer` challenge a token is requested anonymously
  from the challenge's realm, with its `service` and `scope` (default `repository:<repository>:pull`). Tokens are
  cached per registry host and repository until `issued_at` + `expires_in` (60 seconds when absent) minus 5 seconds; a
  401 with a cached token drops it and authenticates again once. Any other challenge, or a failed token request, makes
  the call *failed* — private repositories outside Docker Hub cannot be read.

### Docker Hub account discovery

- Discovers the repositories of the configured Docker Hub account as own images. It runs only when both `UserName`
  and `Pat` are set; otherwise each run is skipped with a log entry. It runs every
  `DockerUpdateGuard:Scanning:DockerHubAccountDiscoveryIntervalMinutes` ([Scan engine](scan-engine.md)).
- The account listing requires `DockerUpdateGuard:DockerHub:Registry` to name a Docker Hub host; otherwise it answers
  *unsupported* and the run ends without changes.
- The account is the namespace equal to `UserName`. All its repositories are listed (pages of 100, following the `next` link as described in
  [Pagination links](#pagination-links)), including private ones the token can see. Repository names are lowercased; duplicates collapse to the most recently updated one.
- For each repository the full tag list is read and one tag is tracked: the most recently pushed; on equal push time
  `latest` first, then alphabetical; tags without a push time come last.
- Each repository becomes an observed image with source *discovery*, matched by registry and repository
  (case-insensitive). A repository whose `namespace/name` is outside the repository grammar
  ([Image catalog](image-catalog.md#image-references)) is skipped like one with a blank name. A new one is created
  enabled; an existing one gets its name set to the repository (cut to 200 characters), its
  description set to the Docker Hub description, its current version set to the tracked tag, and is enabled again.
  The catalog entry is created as described in [Image catalog](image-catalog.md), with the tag's digest and push time.
- Discovered observed images whose repository is no longer listed are disabled, never deleted.
- A repository whose tag listing does not succeed or has no tags is skipped; its observed image keeps its state. When
  the account listing does not succeed, nothing changes.
- All changes of a run are saved together at its end. Discovery does not scan; discovered images are scanned by the
  observed-image scan ([Scan engine](scan-engine.md)).

### Docker Hub request budget and parallelism

- `Scanning:DockerHubRequestLimitWindowHours` (default 6, range 1–168), `Scanning:DockerHubRequestLimitPerWindow`
  (default 200, range 1–100000) and `Scanning:DockerHubReservedManualRequestsPerWindow` (default 40, range 0–100000,
  smaller than the per-window limit) describe the Docker Hub request budget. The budget is a scheduling hint: it
  assumes one Docker Hub request per observed image and scan and only stretches the schedule of the observed-image
  scan ([Scan engine](scan-engine.md)). It does not count requests; a scan may issue several requests per image
  (token, manifests, configuration blobs, tag lookups and listings), manual scans are not limited, and no request is
  refused for budget reasons.
- `DockerUpdateGuard:DockerHub:MaxParallelRequests` (default 4, range 1–32) caps the requests one Docker Hub client
  instance has in flight at once, covering web API, login, token and registry requests. Every scan and every UI
  operation works with its own client instances, so the cap is not global. The value is read when an instance is
  created, so a configuration reload applies to later operations. Requests to other registries are not capped.
- Docker Hub rate limiting (HTTP 429) is handled by the retry below.

### Retry of transient HTTP failures

- Applies to Docker Hub (web API, login, token service, registry), other registries and their token services, the
  .NET and nginx release feeds ([Base images](base-images.md)) and Portainer
  ([Container actions](container-actions.md)). It does not apply to Docker Engine connections
  ([Docker instances](docker-instances.md)) or to Docker Scout ([Vulnerabilities](vulnerabilities.md)).
- Transient are the responses 408, 429 and every 5xx, and transport errors and timeouts raised while sending. Other
  statuses (including 401, 403 and 404) and cancellation are not retried.
- A request is retried up to `DockerUpdateGuard:Scanning:RetryCount` times (default 2, range 0–10; 0 disables
  retries). The value is read for every request, so a configuration reload applies at once.
- The wait before a retry is the response's `Retry-After` (delta or date) when positive, capped at 30 seconds (a non-idempotent request with a longer `Retry-After` is not retried, see below);
  otherwise 500 ms × 2^n for the n-th retry (n starting at 0), capped at 30 seconds, plus a random jitter of up to 20 %
  of that value, the total again capped at 30 seconds.
- `GET`, `HEAD`, `OPTIONS`, `TRACE`, `PUT` and `DELETE` are retried as described above. Every other method (`POST`,
  `PATCH`, any custom method) is not idempotent: a repeat could run its effect twice, so it is retried only on a 429 whose `Retry-After` asks for at most 30 seconds or is missing,
  and after an `HttpRequestException` whose error shows that no request byte was sent (`NameResolutionError`,
  `ConnectionError`, `SecureConnectionError`, `ProxyTunnelError`). A 5xx, a 408, a timeout or a connection that broke
  after the connection was established ends the request after one attempt: the response is returned or the error
  raised. A 429 whose `Retry-After` asks for more than 30 seconds is returned after one attempt as well: the retry does
  not resend the request inside the window the server named. The callers may still log in again with their next
  request (see *Known gaps*). The requests concerned are the Docker Hub login and the Portainer login and actions. A non-idempotent request always carries a body, an empty one if it has nothing to send: the runtime resends a body-less request on its own when a reused connection closes after the server read it ([0008](../decisions/0008-non-idempotent-requests-retry-only-when-not-processed.md)).
- A request body is buffered before the first attempt, so a retry resends the identical body and a body of unknown
  length is sent with a `Content-Length`.
- When the retries are used up the last response is returned to the caller, or the last error is raised.
- Each retry is logged as a warning with the method, the URL without its user info and query, the status or error, the attempt
  number and the wait. A transient outcome that is not retried because the method is not idempotent is logged as a
  debug entry (3502) with the method, the masked URL and the status or error name. A 429 that is not retried because its `Retry-After` exceeds 30 seconds
  is logged as a debug entry (3503) with the status, the method, the masked URL, the requested wait and the 30-second
  maximum.
- `DockerUpdateGuard:DockerHub:RequestTimeoutSeconds` (default 30, range 1–300) is the total time one request may
  take including all retries and waits, for Docker Hub, other registries and the release feeds. A request that runs
  out of time is not retried.

### Configuration reload

- Read live: `DockerHub:UserName`, `DockerHub:Pat`, `DockerHub:Registry` (for account discovery),
  `DockerHub:MaxParallelRequests` (for new client instances) and `Scanning:RetryCount`.
- Fixed at startup, a change needs a restart: `DockerHub:ApiBaseUrl` and `DockerHub:RequestTimeoutSeconds`. The
  origin of `ApiBaseUrl` (or of an absolute `DockerHub:Registry` that replaces it), fixed at startup like `ApiBaseUrl`, is the only one Docker Hub pagination follows ([Pagination links](#pagination-links)).
- Validation of these keys at startup (an invalid value fails startup, see [Hosting](hosting.md)): `Registry`
  non-empty, `ApiBaseUrl` an absolute `http`/`https` URL, neither `ApiBaseUrl` nor an absolute `Registry` with an `@`
  outside its user info ([Hosting](hosting.md), *Validation*), and the ranges above.

## Limits and errors

- *Unsupported*: an empty registry; the account listing when `DockerHub:Registry` is not a Docker Hub host.
- *Not found*: HTTP 404 for an account, repository, tag, manifest or configuration blob; an image whose manifest names
  no configuration blob; a request path with a `.` or `..` segment, or an OCI registry that is not a valid host with an
  optional port, which is not sent ([Request paths](#request-paths)).
- *Failed*: any other non-success status, a token that cannot be obtained, a PAT without user name, a multi-platform
  index without a usable entry. The message contains the status code and the bounded excerpt of the remote response body ([failure model](#failure-model-of-integrations)). A next-page link
  to another origin also gives *failed*, with the message of [Pagination links](#pagination-links).
- Some failures still surface as errors instead of outcomes: transport errors and timeouts that remain after the
  retries, and malformed JSON, during tag lookups, tag listings and the account listing (both clients) and during
  image configuration lookups on other registries. A Docker Hub configuration lookup turns them into *failed* with a fixed reason
  ([failure model](#failure-model-of-integrations)). The scan that made the call records such an error as a failed
  scan: `Container '<name>' could not be processed: <reason>` or `Observed image scan failed: <reason>`.
- The PAT is never written to logs or messages. The user name appears in the log entry and the message of a failed
  Docker Hub login.
- Registry responses are untrusted: missing or mistyped JSON fields are treated as unknown values. An unbounded
  listing (account discovery) reads every page the registry offers. Tag names, digests, the image references of
  base-image labels and the repository names of the account listing leave the clients only when they fit their
  grammars and lengths (see *What can be read*); the names of Docker Engine containers and discovered images are cut
  to their stored length, so these values fit every column they reach ([0014](../decisions/0014-registry-tag-names-and-digests-are-validated-where-they-are-read.md)).

### Known gaps

- After a failed login the Docker Hub client logs in again with its next authenticated request, and the Portainer
  client logs in for every capability check and action; neither remembers a 429's `Retry-After`. Tracked in
  [#320](https://github.com/LarsLaskowski/DockerUpdateGuard/issues/320).
- `MaxParallelRequests` caps each Docker Hub client instance, not the process: concurrent scans, and the client
  instances one scan uses, each get the full allowance. Tracked in
  [#265](https://github.com/LarsLaskowski/DockerUpdateGuard/issues/265).
- `DockerHub:Registry` does not decide which registries the Docker Hub client serves (that host list is fixed); it
  only gates the account listing and, when set to an absolute URL with a non-Docker-Hub host, replaces the web API
  base address. Tracked in [#268](https://github.com/LarsLaskowski/DockerUpdateGuard/issues/268).
- Transport errors, timeouts and malformed JSON escape as errors from several calls instead of being answered as
  *failed* outcomes. Tracked in [#272](https://github.com/LarsLaskowski/DockerUpdateGuard/issues/272).
- For other registries an image configuration lookup without a platform takes the first index entry, which can be an
  attestation manifest, and publication times come only from a top-level `created` field of the manifest, which most
  registries do not send, so their tags usually have no publication time. Tracked in
  [#277](https://github.com/LarsLaskowski/DockerUpdateGuard/issues/277).
- The configured `DockerHub:UserName` reaches the path of the account listing without a check, so `.` or `..` would
  address another path of the Docker Hub web API. Tracked in
  [#363](https://github.com/LarsLaskowski/DockerUpdateGuard/issues/363).

## Related decisions

- [0004](../decisions/0004-pagination-follows-only-same-origin-links.md): pagination follows only same-origin links.
- [0008](../decisions/0008-non-idempotent-requests-retry-only-when-not-processed.md): non-idempotent requests are
  retried only when they cannot have been processed.
- [0009](../decisions/0009-integration-results-carry-fixed-failure-reasons.md): integration results carry fixed
  failure reasons, never an error's own text.
- [0014](../decisions/0014-registry-tag-names-and-digests-are-validated-where-they-are-read.md): remote tag names,
  digests, image references and names are validated or bounded where they are read.

## Not here

- When scans and discovery run, and the formula that turns the request budget into a schedule:
  [Scan engine](scan-engine.md).
- Parsing image references and storing what was read: [Image catalog](image-catalog.md).
- Following base-image labels and the release feeds' content: [Base images](base-images.md).
- Version lines, tag ranking and update recommendations: [Update detection](update-detection.md).
- Docker Scout and Trivy: [Vulnerabilities](vulnerabilities.md).
- Docker Engine access: [Docker instances](docker-instances.md); Portainer: [Container actions](container-actions.md).
