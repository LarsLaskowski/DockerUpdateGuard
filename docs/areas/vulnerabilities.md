# Vulnerabilities

## Scope

How DockerUpdateGuard enriches image versions with known vulnerabilities: the choice of provider (Trivy, Docker Scout
or none), how each provider is called and its answer mapped, which image versions a refresh covers, the vulnerability
assessment per image version, the lifecycle of vulnerability findings, the manual rescan and the configuration hints.
Users rely on it to see which running or observed images carry which CVEs; operators rely on it to configure a
provider; the UI and the inventory metrics rely on the stored assessments and findings.

## Behavior

### Configuration

All keys live under `DockerUpdateGuard:Vulnerabilities`:

| Key | Default | Rule |
| --- | --- | --- |
| `Enabled` | `false` | Switches vulnerability refresh on |
| `Provider` | `None` | `None`, `DockerScout` or `Trivy`; must not be `None` when `Enabled` is `true` |
| `TrivyBaseUrl` | none | Absolute `http`/`https` URI without an `@` outside its user info ([Hosting](hosting.md)); required when `Enabled` is `true` and `Provider` is `Trivy` |
| `TrivyExecutablePath` | `trivy` | Executable to start; resolved through `PATH`; an empty value means `trivy` |
| `DockerScoutLoginUrl` | `https://hub.docker.com/v2/users/login` | Absolute `http`/`https` URI without an `@` outside its user info, always validated |
| `DockerScoutBaseUrl` | `https://api.scout.docker.com` | Absolute `http`/`https` URI without an `@` outside its user info, always validated |
| `RequestTimeoutSeconds` | `120` | `1`–`300` |
| `MaxParallelScans` | `1` | `1`–`8` |

- A configuration that breaks one of these rules fails startup ([Hosting](hosting.md)).
- The provider is chosen anew at the start of every refresh from the current configuration, so a reloaded
  configuration takes effect with the next refresh. The provider chosen at the start of a refresh is used, and named
  as the source of every finding and assessment it stores, for the whole refresh, also when the configuration is
  reloaded during it; each Trivy call reads the current `TrivyBaseUrl`, so a valid reload during a refresh to a
  configuration without it (Docker Scout, or `Enabled` `false`) makes the remaining Trivy calls *not configured*.
  When `Enabled` is `false`, a refresh does nothing: no provider is
  called, no scan run is recorded and stored assessments and findings stay as they are.
- Docker Scout uses the Docker Hub credentials `DockerUpdateGuard:DockerHub:UserName` and
  `DockerUpdateGuard:DockerHub:Pat`; they are not required by validation.

### What a refresh covers

- A full refresh scans every **live** image version: the current image version of every observed image, the image
  versions of the most recent runtime container scan of each Docker instance (for an instance without such a scan,
  the latest snapshot of each container), and the recorded base images of all of these
  ([Base images](base-images.md)).
- A full refresh runs on the schedule `DockerUpdateGuard:Scanning:VulnerabilityRefreshIntervalMinutes` (default `180`,
  range `1`–`10080`, see [Scan engine](scan-engine.md)) and can be started manually from the scan history page.
- A **manual rescan** of a single image version can be started from the observed image, own image and runtime
  container lists. It follows the same rules for that one image version. An image version that does not exist is
  ignored without a scan run.
- Every refresh is recorded as one scan run of type *vulnerability* with trigger *scheduled* or *manual*, also when it
  covers no image version.
- Provider calls run in batches of `MaxParallelScans` image versions at a time; the results of a batch are applied and
  saved one image version after the other, each image version while no other refresh applies the same image version
  ([Scan engine](scan-engine.md#overlapping-scans)). A failure to save the results of one image version does not stop the
  others ([Limits and errors](#limits-and-errors)).

### Assessment per image version

Every scanned image version gets an assessment status, the provider it came from, a message, the check time and the
scan run:

| Status | When | Message |
| --- | --- | --- |
| Not scanned | No assessment stored yet | — |
| No findings | Provider succeeded with no advisory | "The vulnerability provider reported no findings" |
| Findings detected | Provider succeeded with advisories | "The vulnerability provider reported N finding(s)", N after deduplication |
| Not configured | Provider not configured (missing Docker Hub credentials, or no Trivy URL after a reload during a refresh) | Provider message |
| Unsupported | Provider cannot scan this image (Docker Scout and a registry other than `docker.io`) | Provider message |
| Unavailable | Image or tag not found by the provider or, for Docker Scout, the tag now points to another digest | Provider message |
| Failed | Any other provider failure, or the results of the image version could not be saved | Provider message, cut to 2000 characters; or `Vulnerability results for '<reference>' could not be saved: <reason>` |

- A failed assessment leaves the image version's findings untouched.
- A failed assessment adds its message to the scan run. The run ends *failed* once any provider call returned
  *failed* (or the run itself failed), otherwise *partial* when any assessment was not successful, otherwise
  *succeeded*. It also ends *failed* when the results of an image version could not be saved.

### Findings

- A finding is identified within an image version by advisory id and affected package, compared case-insensitively.
  Identity and deduplication use the values as stored, after the cut to the stored lengths ([Limits and errors](#limits-and-errors)):
  two advisories that differ only beyond the stored length of the package are one finding. When a provider reports the
  same pair more than once, the entry with the highest severity, then the highest CVSS score, is kept.
- For each reported advisory: a matching active finding is updated; a matching inactive finding (the most recently
  resolved one) is reactivated with a new detection time and the current scan run, and updated; otherwise a new
  finding is created. Updating overwrites title, severity, source, installed and fixed version, fix status, package
  class and type, CVSS score, summary and reference URL.
- A finding that was active before a successful scan of its image version and is not reported again is deactivated
  with a resolution time and the resolving scan run.
- At the end of a full refresh, every active finding of an image version that is neither live nor scanned in this run
  is deactivated the same way. A manual rescan never does this.
- At most one active finding exists per image version, advisory id and affected package.
- Severity is one of *not set*, *low*, *medium*, *high*, *critical*. The source is the provider resolved for the refresh
  (*Trivy* or *Docker Scout*). Every page that shows a vulnerability assessment shows a stored retired assessment source (3 or 4) as "Not set". Fix status is one of *not set*, *fixed*, *affected*, *will not fix*, *fix deferred*, *end of life*.

### Trivy

- Trivy runs as a local process in client mode against the Trivy server at `TrivyBaseUrl`; the image is analysed by
  the process, only the vulnerability database lookup goes to the server. No shell is involved; each argument is
  passed separately:
  `image --format json --quiet --scanners vuln --pkg-types os,library --timeout <RequestTimeoutSeconds>s -- <target>`.
  The `--` ends Trivy's options, so a target that begins with `-` (possible for a registered reference) is read as the
  image name, never as an option; when Trivy cannot pull such an image, the scan ends with a non-zero exit code as
  described below.
- The server address is passed as `TRIVY_SERVER` (`TrivyBaseUrl` without trailing `/`, user info unchanged) in the
  process environment, added to the environment inherited from the application (an inherited `TRIVY_SERVER` is
  replaced), so it does not stand on the command line, where every user who can see the process could read the user
  info.
- Besides the Trivy server, the Trivy process contacts only the Docker Engine at `/var/run/docker.sock` (Trivy reads
  the image from it first when the socket is mounted there) and the registries, including Trivy's Java DB repository
  when an image contains Java archives; the container image switches off Trivy's update check and telemetry
  ([Hosting](hosting.md#container-image)).
- The target is `<repository>:<tag>` for `docker.io` images and `<registry>/<repository>:<tag>` otherwise, followed by
  `@<digest>` when the digest is known.
- The process gets `RequestTimeoutSeconds` plus 10 seconds. When that passes, the process and its children are killed
  and the result is *failed*, "Trivy scan timed out after N seconds".
- A non-zero exit code whose error output contains `MANIFEST_UNKNOWN`, `manifest unknown`, or both `manifest` and
  `not found` (any case) means the image is gone from the registry: *unavailable*, "The image is no longer available
  in the registry. The tag was likely re-published with a newer digest, leaving this version orphaned." Any other
  non-zero exit code is *failed*, "Trivy exited with code X: <last line of the error output cut to 500 characters followed by `…`, with control characters replaced ([Integration failures](integration-failures.md#excerpts-of-remote-text)), or 'no error output'>".
  When `TrivyBaseUrl` contains an `@` (which validation allows only as the end of its user info), a non-empty error output is
  replaced by "error output withheld because TrivyBaseUrl may carry credentials" in the message and the log, because
  Trivy may repeat parts of the URL's user info there (measured with Trivy v0.58.1: the password masked but the user
  name kept, a user name alone in full, the raw URL for an invalid escape).
- The JSON report on standard output is mapped per vulnerability of every result block: advisory id from
  `VulnerabilityID`; title from `Title`, else the advisory id; severity from `Severity` (`LOW`, `MEDIUM`, `HIGH`,
  `CRITICAL`, any case, everything else *not set*); affected package, installed and fixed version from `PkgName`,
  `InstalledVersion`, `FixedVersion`; fix status from `Status` (`fixed`, `affected`, `will_not_fix`, `fix_deferred`,
  `end_of_life`, any case, everything else *not set*); package class and type from the result block's `Class` and
  `Type`; summary from `Description`; reference URL from `PrimaryURL`, else the first of `References`. Every text value
  has its control characters replaced by spaces ([Integration failures](integration-failures.md#remote-text-fields)); a title that is then
  blank falls back to the advisory id, and a class or type that is then blank is *none*.
- The CVSS score is the NVD v3 score, else the first v3 score of any source, else the NVD v2 score, else the first v2
  score of any source, else none.

### Docker Scout

- Docker Scout scans only `docker.io` images, by tag; every Docker Hub spelling counts as `docker.io` after parsing
  ([Image catalog](image-catalog.md#image-references)). A repository without a namespace is looked up under `library`.
- **Digest check.** Docker Scout looks up tags only, so an image reference with a digest is checked first, after the
  credentials, registry and path checks (an image reference without a digest skips the check, and a reference those
  checks answer is not checked). The provider reads the tag from Docker Hub's web API through the registry metadata
  service, in a service scope of its own that it disposes after the lookup, and continues only when the reference's
  digest equals the tag record's digest or one of its platform image digests (compared case-insensitively;
  [Registries](registries.md#what-can-be-read)). Otherwise, also when the tag record has no digest, the lookup
  sends neither the login nor a Docker Scout request and gives *not found*, `The tag of '<reference>' now points to a
  different digest; Docker Scout looks up tags only and cannot assess this version.`, shown as *unavailable* with the
  stored findings untouched. A tag lookup that gives *not found* gives *not found* with the registry's message; any
  other unsuccessful status gives *failed* with the registry's message, or `Tag digest lookup failed for
  '<reference>'` when it has none; an unexpected error gives *failed*, `Docker Scout scan failed: <reason>`. The check
  costs one Docker Hub web API request per lookup of a reference with a digest; it takes a Docker Hub request slot,
  and its time limit is `DockerHub:RequestTimeoutSeconds`, not `Vulnerabilities:RequestTimeoutSeconds`
  ([Registries](registries.md#request-timeout)). A tag republished between the check and the Docker Scout request is
  not detected ([0031](../decisions/0031-docker-scout-assesses-a-digest-only-while-its-tag-still-points-to-it.md)).
- The provider logs in by posting the Docker Hub user name and token to `DockerScoutLoginUrl` and caches the returned
  token for the whole process until one minute before the expiry stated in the token, or for five minutes when the
  expiry cannot be read (no `exp`, one that is not a whole number, or one outside 1 … 253402300799
  seconds, `0` and negative values included). Only one login runs at a time. A failed login gives *failed*, "Docker Hub authentication
  failed". The login is a non-idempotent request for the retry handler, so it is retried only on a 429 without a
  `Retry-After` or one of at most 30 seconds and after an error before any byte was sent, not on a 5xx
  ([Integration failures](integration-failures.md#retry-of-transient-http-failures)). After a login answered 429 with a positive `Retry-After`, the provider logs in no more before the window
  ends, and neither does it after the Docker Hub client's login to the same URL with the same trimmed user name was
  answered so: the lookup sends nothing and gives *failed* with `Docker Hub authentication token request for '<user>'
  not sent: Docker Hub answered HTTP 429 and asked to wait until <until>`, also on the refresh after a 401
  ([Integration failures](integration-failures.md#logins-inside-a-retry-after-window)).
- A login takes a Docker Hub request slot (`DockerHub:MaxParallelRequests`,
  [Registries](registries.md#docker-hub-request-budget-and-parallelism)) and waits for one after the `Retry-After`
  check. The tag lookup of the digest check takes a slot as every Docker Hub web API request does; lookups with a
  cached token and the Docker Scout API requests take none.
- Vulnerabilities are read from `<DockerScoutBaseUrl>/v1/repositories/<namespace>/<repository>/tags/<tag>/vulnerabilities`,
  following the `next` link of each page. Each segment is percent-encoded. A repository or tag
  with a `.` or `..` segment is not requested: the lookup logs nothing, does not log in, and gives *not found* with the
  message `Registry request path would contain a '.' or '..' segment`, shown as *unavailable* on the vulnerability pages
  ([Registries](registries.md#request-paths)).
- The `next` link is followed only on the origin of `DockerScoutBaseUrl` (same scheme, host and port); a root-relative
  link is resolved against the current request. A link to another origin is not requested, so the bearer token never
  leaves that origin: the provider answers *failed*, "Docker Scout vulnerability listing for '<reference>' stopped: the
  next-page link pointed to a different origin (<origin>)", with only scheme, host and non-default port of the link (cut to 300 characters followed by `…`, as in [Registries](registries.md#pagination-links)) in
  the message and the log; the stored findings stay untouched. A proxy in front of Docker Scout must rewrite next-page
  links to its own origin ([Registries](registries.md#pagination-links)).
- After the origin check, a `next` link whose request the fetch already sent (the first page, a page itself, or a link
  that differs only in its fragment) makes the listing *failed*, "Docker Scout vulnerability listing for '<reference>'
  stopped: the next-page link repeated a page already read", with warning 3407. A listing whose 1,000th page
  (`PaginationLinkGuard.MaxListingPages`) still links to a next page is *failed*, "Docker Scout vulnerability listing
  for '<reference>' stopped: the listing did not end within 1000 pages", with warning 3419. Both stops return no
  advisories, send no further request and leave the stored findings untouched; a listing of exactly 1,000 pages whose
  last page has no link succeeds. The rule is in [Registries](registries.md#pagination-links).
- HTTP 401 discards the cached token, logs in once more and reads again from the first page; a second 401 gives
  *failed*, "Docker Scout rejected the authentication token". HTTP 404 gives *unavailable*, "Image <reference> not
  found in Docker Scout". Any other unsuccessful status gives *failed*, "Docker Scout request failed with status X".
- Each login and each page request is limited to `RequestTimeoutSeconds`, its retries and waits included; the
  requests go through the transient-failure retry
  ([Integration failures](integration-failures.md#retry-of-transient-http-failures)): a page answered 408, 429 or 5xx
  is read again, a 401 is not retried by it and is handled as above.
- Mapping: advisory id and title from `id`, severity from `severity` (as for Trivy; an unknown value maps to *not set* and is logged cut to 100 characters, followed by `…` when it was longer, with its control characters replaced), summary from `description`,
  reference URL from the first of `links`; the text values have their control characters replaced by spaces
  ([Integration failures](integration-failures.md#remote-text-fields)). Docker Scout supplies no package, versions, fix status or CVSS score.

### Configuration hints

The dashboard shows at most one hint about the vulnerability configuration:

- *Scanning disabled* when `Enabled` is `false`.
- *Docker Hub credentials missing* when `Provider` is `DockerScout` and that run ended *partial* or *failed* with a
  message containing "Docker Hub credentials are required for Docker Scout".
- No hint otherwise.

## Limits and errors

- Providers never raise errors for expected failures; they report *not configured*, *unsupported*, *not found* or
  *failed* with a message ([Integration failures](integration-failures.md#outcomes)). An unexpected error inside a provider call becomes *failed*
  with `Trivy scan failed: <reason>` or `Docker Scout scan failed: <reason>`, where `<reason>` is a fixed reason
  ([Integration failures](integration-failures.md#fixed-failure-reasons)); a Trivy process that cannot be started reads `Trivy
  scan failed: the Trivy executable could not be started`. Cancellation is not converted.
- An unexpected error during a refresh ends the scan run *failed* with `Vulnerability refresh failed: <reason>`; work
  saved before stays saved, and the changes of the step that was aborted, by an error or a cancellation, are discarded
  before the run's final state is saved ([decision 0041](../decisions/0041-an-aborted-vulnerability-refresh-discards-its-unsaved-changes-before-the-runs-final-save.md)).
  If saving the final run state fails, it is retried once on a fresh state: the retry saves the run alone with the
  status and message it already has ([decision 0040](../decisions/0040-a-scan-item-whose-save-fails-is-saved-again-alone-as-failed.md)).
- Stored lengths: advisory id 100 characters, title 300, affected package 200, installed and fixed version 100 each,
  package class and type 50 each, CVSS score 5 digits with 2 decimals, assessment message 2000; summary and reference
  URL are unlimited. A longer advisory id, title, package, version, class or type, and a longer assessment message
  (also in the scan run's message), is cut where the refresh stores it to the stored length including a final `…`,
  never splitting a surrogate pair ([Integration failures](integration-failures.md#excerpts-of-remote-text)).
- When applying or saving the results of one image version throws (not a cancellation), the refresh discards that
  image version's pending changes, stores its assessment *Failed* with
  `Vulnerability results for '<reference>' could not be saved: <reason>` (a fixed reason), leaves its findings as
  they were, adds the message to the scan run, logs a warning with the image reference and continues with the remaining
  image versions; the run ends *failed*. The stale-findings pass of a full refresh still runs and keeps the findings of
  that image version. If saving the *Failed* assessment fails as well, the refresh ends as for an unexpected error and the image version keeps the assessment stored before the refresh.
  A cancellation is not turned into such a failure: the cancelled image version's results are not stored, and the run ends *failed* with `Vulnerability refresh failed: cancelled`.
- Messages are built from the image reference and the provider's error output (Trivy's excerpt, or for Docker
  Scout the failure message of Docker Hub's tag lookup with its body excerpt,
  [Integration failures](integration-failures.md#excerpts-of-remote-text)) or a fixed reason; no tokens or
  passwords are put into them.

## Related decisions

- [0004](../decisions/0004-pagination-follows-only-same-origin-links.md): pagination follows only same-origin links.
- [0024](../decisions/0024-registry-listings-stop-failed-after-a-page-cap.md): listings stop *failed* at a repeated
  link or after a page cap.
- [0047](../decisions/0047-the-docker-scout-listing-stops-failed-at-a-repeated-link-or-after-the-shared-page-cap.md): the Docker Scout listing stops *failed* at a repeated link or after the
  shared page cap.
- [0005](../decisions/0005-urls-are-output-with-user-info-masked.md): URLs are output with their user info masked;
  Trivy's error output is withheld for a `TrivyBaseUrl` with an `@`; the Trivy server address is passed in the
  environment.
- [0015](../decisions/0015-the-trivy-scan-target-follows-an-end-of-options-marker.md): the Trivy scan target follows
  an end-of-options marker (`--`).
- [0016](../decisions/0016-the-bundled-trivy-skips-its-update-check-and-telemetry.md): the bundled Trivy skips its
  update check and telemetry through the image's environment.
- [0051](../decisions/0051-the-bundled-trivy-and-the-compose-trivy-servers-move-to-v0-76-2.md): the bundled Trivy and
  the compose Trivy servers move to v0.76.2, measured again.
- [0027](../decisions/0027-over-long-vulnerability-values-are-cut-to-their-column-length-where-the-refresh-stores-them.md):
  over-long vulnerability values are cut to their column length where the refresh stores them.
- [0028](../decisions/0028-a-failed-save-of-one-image-version-does-not-stop-the-vulnerability-refresh.md): a failed
  save of one image version does not stop the vulnerability refresh.
- [0031](../decisions/0031-docker-scout-assesses-a-digest-only-while-its-tag-still-points-to-it.md): Docker Scout
  assesses a digest only while its tag still points to it.

## Not here

- Refresh schedule, scan run statuses, stuck-run repair and cleanup of resolved findings: [Scan engine](scan-engine.md).
- Which base images an image version has: [Base images](base-images.md).
- Docker Hub credentials and registry access: [Registries](registries.md); the retry of transient HTTP failures:
  [Integration failures](integration-failures.md).
- Validation at startup, the bundled Trivy CLI in the container image: [Hosting](hosting.md).
- How assessments, findings, severities and the rescan control are displayed: [Web UI](web-ui.md).
- Inventory gauges for active findings: [Telemetry](telemetry.md).
