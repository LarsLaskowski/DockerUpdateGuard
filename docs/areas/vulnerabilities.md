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
| `TrivyBaseUrl` | none | Absolute `http`/`https` URI; required when `Enabled` is `true` and `Provider` is `Trivy` |
| `TrivyExecutablePath` | `trivy` | Executable to start; resolved through `PATH`; an empty value means `trivy` |
| `DockerScoutLoginUrl` | `https://hub.docker.com/v2/users/login` | Absolute `http`/`https` URI, always validated |
| `DockerScoutBaseUrl` | `https://api.scout.docker.com` | Absolute `http`/`https` URI, always validated |
| `RequestTimeoutSeconds` | `120` | `1`–`300` |
| `MaxParallelScans` | `1` | `1`–`8` |

- A configuration that breaks one of these rules fails startup ([Hosting](hosting.md)).
- The provider is chosen anew at the start of every refresh from the current configuration, so a reloaded
  configuration takes effect with the next refresh. When `Enabled` is `false`, a refresh does nothing: no provider is
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
  saved one image version after the other.

### Assessment per image version

Every scanned image version gets an assessment status, the provider it came from, a message, the check time and the
scan run:

| Status | When | Message |
| --- | --- | --- |
| Not scanned | No assessment stored yet | — |
| No findings | Provider succeeded with no advisory | "The vulnerability provider reported no findings" |
| Findings detected | Provider succeeded with advisories | "The vulnerability provider reported N finding(s)", N after deduplication |
| Not configured | Provider not configured (missing Trivy URL or Docker Hub credentials) | Provider message |
| Unsupported | Provider cannot scan this image (Docker Scout and a registry other than `docker.io`) | Provider message |
| Unavailable | Image or tag not found by the provider | Provider message |
| Failed | Any other provider failure | Provider message |

- A failed assessment leaves the image version's findings untouched.
- A failed assessment adds its message to the scan run. The run ends *failed* once any provider call returned
  *failed* (or the run itself failed), otherwise *partial* when any assessment was not successful, otherwise
  *succeeded*.

### Findings

- A finding is identified within an image version by advisory id and affected package, compared case-insensitively.
  When a provider reports the same pair more than once, the entry with the highest severity, then the highest CVSS
  score, is kept.
- For each reported advisory: a matching active finding is updated; a matching inactive finding (the most recently
  resolved one) is reactivated with a new detection time and the current scan run, and updated; otherwise a new
  finding is created. Updating overwrites title, severity, source, installed and fixed version, fix status, package
  class and type, CVSS score, summary and reference URL.
- A finding that was active before a successful scan of its image version and is not reported again is deactivated
  with a resolution time and the resolving scan run.
- At the end of a full refresh, every active finding of an image version that is neither live nor scanned in this run
  is deactivated the same way. A manual rescan never does this.
- At most one active finding exists per image version, advisory id and affected package.
- Severity is one of *not set*, *low*, *medium*, *high*, *critical*. The source is the configured provider (*Trivy* or
  *Docker Scout*). Fix status is one of *not set*, *fixed*, *affected*, *will not fix*, *fix deferred*, *end of life*.

### Trivy

- Trivy runs as a local process in client mode against the Trivy server at `TrivyBaseUrl`; the image is analysed by
  the process, only the vulnerability database lookup goes to the server. No shell is involved; each argument is
  passed separately:
  `image --format json --quiet --scanners vuln --pkg-types os,library --timeout <RequestTimeoutSeconds>s <target>`.
- The server address is passed as `TRIVY_SERVER` (`TrivyBaseUrl` without trailing `/`, user info unchanged) in the
  process environment, added to the environment inherited from the application (an inherited `TRIVY_SERVER` is
  replaced), so it does not stand on the command line, where every user who can see the process could read the user
  info.
- The target is `<repository>:<tag>` for `docker.io` images and `<registry>/<repository>:<tag>` otherwise, followed by
  `@<digest>` when the digest is known.
- The process gets `RequestTimeoutSeconds` plus 10 seconds. When that passes, the process and its children are killed
  and the result is *failed*, "Trivy scan timed out after N seconds".
- A non-zero exit code whose error output contains `MANIFEST_UNKNOWN`, `manifest unknown`, or both `manifest` and
  `not found` (any case) means the image is gone from the registry: *unavailable*, "The image is no longer available
  in the registry. The tag was likely re-published with a newer digest, leaving this version orphaned." Any other
  non-zero exit code is *failed*, "Trivy exited with code X: <last line of the error output cut to 500 characters followed by `…`, or 'no error output'>".
  When `TrivyBaseUrl` contains an `@` (user info, or an `@` anywhere else in it), a non-empty error output is
  replaced by "error output withheld because TrivyBaseUrl may carry credentials" in the message and the log, because
  Trivy may repeat parts of the URL's user info there (measured with Trivy v0.58.1: the password masked but the user
  name kept, a user name alone in full, the raw URL for an invalid escape).
- The JSON report on standard output is mapped per vulnerability of every result block: advisory id from
  `VulnerabilityID`; title from `Title`, else the advisory id; severity from `Severity` (`LOW`, `MEDIUM`, `HIGH`,
  `CRITICAL`, any case, everything else *not set*); affected package, installed and fixed version from `PkgName`,
  `InstalledVersion`, `FixedVersion`; fix status from `Status` (`fixed`, `affected`, `will_not_fix`, `fix_deferred`,
  `end_of_life`, any case, everything else *not set*); package class and type from the result block's `Class` and
  `Type`; summary from `Description`; reference URL from `PrimaryURL`, else the first of `References`.
- The CVSS score is the NVD v3 score, else the first v3 score of any source, else the NVD v2 score, else the first v2
  score of any source, else none.

### Docker Scout

- Docker Scout scans only `docker.io` images, by tag; the digest is not used. A repository without a namespace is
  looked up under `library`.
- The provider logs in by posting the Docker Hub user name and token to `DockerScoutLoginUrl` and caches the returned
  token for the whole process until one minute before the expiry stated in the token, or for five minutes when the
  expiry cannot be read. Only one login runs at a time. A failed login gives *failed*, "Docker Hub authentication
  failed".
- Vulnerabilities are read from `<DockerScoutBaseUrl>/v1/repositories/<namespace>/<repository>/tags/<tag>/vulnerabilities`,
  following the `next` link of each page, for at most **50 pages**. When the cap is reached, the advisories read so
  far are returned as a successful result.
- The `next` link is followed only on the origin of `DockerScoutBaseUrl` (same scheme, host and port); a root-relative
  link is resolved against the current request. A link to another origin is not requested, so the bearer token never
  leaves that origin: the provider answers *failed*, "Docker Scout vulnerability listing for '<reference>' stopped: the
  next-page link pointed to a different origin (<origin>)", with only scheme, host and non-default port of the link (cut to 300 characters followed by `…`, as in [Registries](registries.md#pagination-links)) in
  the message and the log; the stored findings stay untouched. A proxy in front of Docker Scout must rewrite next-page
  links to its own origin ([Registries](registries.md#pagination-links)).
- HTTP 401 discards the cached token, logs in once more and reads again from the first page; a second 401 gives
  *failed*, "Docker Scout rejected the authentication token". HTTP 404 gives *unavailable*, "Image <reference> not
  found in Docker Scout". Any other unsuccessful status gives *failed*, "Docker Scout request failed with status X".
- Each HTTP request is limited to `RequestTimeoutSeconds`.
- Mapping: advisory id and title from `id`, severity from `severity` (as for Trivy), summary from `description`,
  reference URL from the first of `links`. Docker Scout supplies no package, versions, fix status or CVSS score.

### Configuration hints

The dashboard shows at most one hint about the vulnerability configuration:

- *Scanning disabled* when `Enabled` is `false`.
- *Trivy base URL missing* when `Provider` is `Trivy` and the most recent completed vulnerability scan run (by start
  time) ended *partial* or *failed* with a message containing "TrivyBaseUrl is not configured".
- *Docker Hub credentials missing* when `Provider` is `DockerScout` and that run ended *partial* or *failed* with a
  message containing "Docker Hub credentials are required for Docker Scout".
- No hint otherwise.

## Limits and errors

- Providers never raise errors for expected failures; they report *not configured*, *unsupported*, *not found* or
  *failed* with a message ([Registries](registries.md)). An unexpected error inside a provider call becomes *failed*
  with `Trivy scan failed: <reason>` or `Docker Scout scan failed: <reason>`, where `<reason>` is a fixed reason
  ([Registries](registries.md#failure-model-of-integrations)); a Trivy process that cannot be started reads `Trivy
  scan failed: the Trivy executable could not be started`. Cancellation is not converted.
- An unexpected error during a refresh ends the scan run *failed* with `Vulnerability refresh failed: <reason>`; work
  saved before stays saved. If saving the final run state fails, it is retried once.
- Stored lengths: advisory id 100 characters, title 300, affected package 200, installed and fixed version 100 each,
  package class and type 50 each, CVSS score 5 digits with 2 decimals, assessment message 2000; summary and reference
  URL are unlimited.
- Messages are built from the image reference and the provider's error output (Trivy's excerpt) or a fixed reason; no credentials or tokens are put into
  them.

### Known gaps

- Docker Scout requests do not go through the transient-failure retry the other outbound HTTP calls use, and Docker
  Scout scans the tag, not the digest, so an image version whose tag was republished is assessed with the advisories
  of the newer image. Tracked in [#267](https://github.com/LarsLaskowski/DockerUpdateGuard/issues/267).
- Provider values longer than the stored lengths (for example a Trivy title over 300 characters) are not shortened;
  saving them fails and ends the whole refresh *failed*, leaving the remaining image versions unscanned. Tracked in
  [#271](https://github.com/LarsLaskowski/DockerUpdateGuard/issues/271).
- Some values are kept although nothing produces them: validation makes the *Trivy base URL missing* hint and the
  Trivy *not configured* result unreachable (unless an invalid reloaded configuration is still served), and the
  finding sources *registry advisory* and *manual* exist in the data model but are never produced. Tracked in
  [#293](https://github.com/LarsLaskowski/DockerUpdateGuard/issues/293).

## Related decisions

- [0004](../decisions/0004-pagination-follows-only-same-origin-links.md): pagination follows only same-origin links.
- [0005](../decisions/0005-urls-are-output-with-user-info-masked.md): URLs are output with their user info masked;
  Trivy's error output is withheld for a `TrivyBaseUrl` with an `@`; the Trivy server address is passed in the
  environment.

## Not here

- Refresh schedule, scan run statuses, stuck-run repair and cleanup of resolved findings: [Scan engine](scan-engine.md).
- Which base images an image version has: [Base images](base-images.md).
- Docker Hub credentials, registry access and the retry of transient HTTP failures: [Registries](registries.md).
- Validation at startup, the bundled Trivy CLI in the container image: [Hosting](hosting.md).
- How assessments, findings, severities and the rescan control are displayed: [Web UI](web-ui.md).
- Inventory gauges for active findings: [Telemetry](telemetry.md).
