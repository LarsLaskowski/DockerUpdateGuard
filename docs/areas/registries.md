# Registries

## Scope

Read access to container registries: Docker Hub (its web API and its registry) and every other registry that speaks the
OCI Distribution API. Covers which client serves which registry, what can be read (tags, manifests, image
configuration and labels), how DockerUpdateGuard authenticates, the Docker Hub account image discovery, the Docker Hub
request budget and parallelism, and the request timeout. The image catalog, base-image resolution and update detection
rely on it for registry data; operators rely on it to configure credentials and limits. How a failure is reported and
which requests are retried is the same for every integration and stated in
[Integration failures](integration-failures.md).

## Behavior

### Failure model, remote text and retry

<a id="failure-model-of-integrations"></a><a id="remote-text-fields"></a><a id="retry-of-transient-http-failures"></a>
The registry clients answer with the outcomes, fixed failure reasons and bounded excerpts of
[Integration failures](integration-failures.md), clean the text fields they read as stated there, and their requests
go through its retry of transient HTTP failures.

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

- Every segment of a registry request path is percent-encoded. URL normalization removes `.` segments and resolves
  `..` segments even when they are written as `%2E`, so encoding cannot keep them; a path whose repository, tag, digest or
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
  optional port`, sending no request and logging nothing. A digest named by a manifest answers *not found* with the same message (OCI configuration, OCI manifest-list entry),
  gives *failed* "Image configuration blob could not be read for '<reference>'" (Docker Hub configuration blob) or
  *not found* "Config blob digest could not be resolved for '<reference>'" (Docker Hub manifest-list entry). Base-image resolution reads configurations through these lookups; its outcome is described in [Base images](base-images.md#resolving-the-chain).
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
- **Image configuration** — environment variables, labels (keys compared case-insensitively; of labels whose names
  are equal case-insensitively, or repeat, the one that comes last in the configuration is kept, with its name and
  value), creation time, operating system and architecture, read from the image's configuration blob. For a
  multi-platform index one platform manifest
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
- OCI `Link` headers may contain multiple entries or parameters. Entries are split on commas only when the comma is
  outside an angle-bracketed URI target and a quoted parameter. An entry with an unterminated or otherwise malformed
  target is skipped; when no entry has a well-formed target with the `next` relation, the listing ends as *succeeded*
  with the tags read so far and no request is sent. The `next` relation is matched as an exact, case-insensitive
  relation token of the first `rel` parameter of an entry (later ones are ignored), whether `rel` is quoted
  (`rel="next last"`) or unquoted (`rel=next`), so relation-like text in another parameter cannot select a page.
- A rejected link makes the listing *failed*, and the entries read before are discarded. The message is
  `<listing> stopped: the next-page link pointed to a different origin (<origin>)`, for example
  `Docker Hub tag listing for 'acme/api' stopped: the next-page link pointed to a different origin (https://attacker.example)`;
  the same origin is logged as a warning. The origin text is the scheme, host (in punycode) and, unless it is the
  default, the port; user info, path and query of the link are never written. A link that cannot be parsed is
  described as `unparseable link`. The origin text is cut to 300 characters followed by `…` and cleaned like an excerpt ([Integration failures](integration-failures.md#excerpts-of-remote-text)).
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
- Docker Hub rate limiting (HTTP 429) is handled by the retry of
  [Integration failures](integration-failures.md#retry-of-transient-http-failures).

### Request timeout

- `DockerUpdateGuard:DockerHub:RequestTimeoutSeconds` (default 30, range 1–300) is the total time one request may
  take including all retries and waits ([Integration failures](integration-failures.md#retry-of-transient-http-failures)),
  for Docker Hub, other registries and the release feeds.

### Configuration reload

- Read live: `DockerHub:UserName`, `DockerHub:Pat`, `DockerHub:Registry` (for account discovery)
  and `DockerHub:MaxParallelRequests` (for new client instances).
- Fixed at startup, a change needs a restart: `DockerHub:ApiBaseUrl` and `DockerHub:RequestTimeoutSeconds`. The
  origin of `ApiBaseUrl` (or of an absolute `DockerHub:Registry` that replaces it), fixed at startup like `ApiBaseUrl`, is the only one Docker Hub pagination follows ([Pagination links](#pagination-links)).
- Validation of these keys at startup (an invalid value fails startup, see [Hosting](hosting.md)): `Registry`
  non-empty, `ApiBaseUrl` an absolute `http`/`https` URL, neither `ApiBaseUrl` nor an absolute `Registry` with an `@`
  outside its user info ([Hosting](hosting.md), *Validation*), the ranges above, and a non-blank `UserName`, trimmed,
  which must be a Docker Hub user name: ASCII letters and digits, separated by `.`, `_`, `__` or any run of `-`, at
  most 255 characters. Anything else fails with
  `'DockerUpdateGuard:DockerHub:UserName' must be a Docker Hub user name: letters and digits, optionally separated by '.', '_' or '-'`
  (the failure never contains the value), at startup and on reload. The account listing, which puts the trimmed value
  into its request path, thereby never receives `.` or `..`
  ([0018](../decisions/0018-the-docker-hub-user-name-is-validated-as-a-namespace-at-startup.md)).

## Limits and errors

- *Unsupported*: an empty registry; the account listing when `DockerHub:Registry` is not a Docker Hub host.
- *Not found*: HTTP 404 for an account, repository, tag, manifest or configuration blob; an image whose manifest names
  no configuration blob; a request path with a `.` or `..` segment, or an OCI registry that is not a valid host with an
  optional port, which is not sent ([Request paths](#request-paths)).
- *Failed*: any other non-success status, a token that cannot be obtained, a PAT without user name, a multi-platform
  index without a usable entry. The message contains the status code and the bounded excerpt of the remote response body ([Integration failures](integration-failures.md#excerpts-of-remote-text)). A next-page link
  to another origin also gives *failed*, with the message of [Pagination links](#pagination-links).
- The Docker Hub current-user, account listing, repository, tag and tag-listing calls and the OCI tag, tag-listing and
  image configuration calls answer an unreadable body (malformed JSON, or a string that does not decode) as *failed*
  with `invalid response`, logging one warning with the exception.
- Some failures still surface as errors instead of outcomes: transport errors and timeouts that remain after the
  retries, a JSON value of the wrong type and a Docker Hub token payload that is not base64, during tag lookups, tag
  listings and the account listing (both clients) and during image configuration lookups on other registries. A Docker Hub configuration lookup turns them into *failed* with a fixed reason
  ([Integration failures](integration-failures.md#fixed-failure-reasons)). The scan that made the call records such an error as a failed
  scan: `Container '<name>' could not be processed: <reason>` or `Observed image scan failed: <reason>`.
- The PAT is never written to logs or messages. The user name appears in the log entry and the message of a failed
  Docker Hub login.
- Registry responses are untrusted: missing or mistyped JSON fields are treated as unknown values. An unbounded
  listing (account discovery) reads every page the registry offers. Tag names, digests, the image references of
  base-image labels and the repository names of the account listing leave the clients only when they fit their
  grammars and lengths (see *What can be read*); the names of Docker Engine containers and discovered images are cut
  to their stored length, so these values fit every column they reach ([0014](../decisions/0014-registry-tag-names-and-digests-are-validated-where-they-are-read.md)).

### Known gaps

- `MaxParallelRequests` caps each Docker Hub client instance, not the process: concurrent scans, and the client
  instances one scan uses, each get the full allowance. Tracked in
  [#265](https://github.com/LarsLaskowski/DockerUpdateGuard/issues/265).
- `DockerHub:Registry` does not decide which registries the Docker Hub client serves (that host list is fixed); it
  only gates the account listing and, when set to an absolute URL with a non-Docker-Hub host, replaces the web API
  base address. Tracked in [#268](https://github.com/LarsLaskowski/DockerUpdateGuard/issues/268).
- Transport errors, timeouts, JSON values of an unexpected type and an unreadable Docker Hub token payload escape as
  errors from several calls instead of being answered as *failed* outcomes. Tracked in [#272](https://github.com/LarsLaskowski/DockerUpdateGuard/issues/272).
- For other registries an image configuration lookup without a platform takes the first index entry, which can be an
  attestation manifest, and publication times come only from a top-level `created` field of the manifest, which most
  registries do not send, so their tags usually have no publication time. Tracked in
  [#277](https://github.com/LarsLaskowski/DockerUpdateGuard/issues/277).

## Related decisions

- [0004](../decisions/0004-pagination-follows-only-same-origin-links.md): pagination follows only same-origin links.
- [0019](../decisions/0019-remote-json-with-undecodable-strings-is-an-invalid-response.md): remote JSON with a string
  that does not decode is an invalid response.
- [0014](../decisions/0014-registry-tag-names-and-digests-are-validated-where-they-are-read.md): remote tag names,
  digests, image references and names are validated or bounded where they are read.
- [0018](../decisions/0018-the-docker-hub-user-name-is-validated-as-a-namespace-at-startup.md): the Docker Hub user
  name is validated as a namespace at startup.
- [0021](../decisions/0021-of-image-configuration-labels-with-colliding-names-the-last-one-wins.md): of image
  configuration labels with colliding names the last one wins.

## Not here

- Outcomes, fixed failure reasons, excerpts, remote text cleaning and the retry of transient HTTP failures:
  [Integration failures](integration-failures.md).
- When scans and discovery run, and the formula that turns the request budget into a schedule:
  [Scan engine](scan-engine.md).
- Parsing image references and storing what was read: [Image catalog](image-catalog.md).
- Following base-image labels and the release feeds' content: [Base images](base-images.md).
- Version lines, tag ranking and update recommendations: [Update detection](update-detection.md).
- Docker Scout and Trivy: [Vulnerabilities](vulnerabilities.md).
- Docker Engine access: [Docker instances](docker-instances.md); Portainer: [Container actions](container-actions.md).
