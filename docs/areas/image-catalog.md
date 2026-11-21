# Image catalog

## Scope

What "an image" is in DockerUpdateGuard: how image references are parsed and normalized, the deduplicated catalog of
registry repositories and image versions (repository, tag and digest) that every other area points at, how the catalog
stays free of duplicates under concurrent writers, and observed images — the images a user registers or the Docker Hub
account discovery adds to be tracked, with their source and enabled state. Every scanning area and the UI rely on it to
refer to the same image the same way.

## Behavior

### Image references

- A reference has the form `[registry/]repository[:tag][@digest]`; surrounding whitespace is ignored.
- **Digest:** everything after the first `@`, trimmed and lowercased.
- **Tag:** the text after the last `:` that follows the last `/`, trimmed, with its case kept. A missing or empty tag
  is `latest`, also for a reference that names only a digest.
- **Registry:** the first path segment, lowercased, when the reference has at least two segments and that segment
  contains a `.` or a `:` or equals `localhost` (case-insensitive). Otherwise the registry is `docker.io`. The three
  Docker Hub hosts `docker.io`, `index.docker.io` and `registry-1.docker.io` (case-insensitive) all become `docker.io`;
  a host with a port (`docker.io:443`) and the web hosts (`hub.docker.com`) stay registries of their own.
- **Repository:** the remaining segments joined with `/`, lowercased; empty segments are ignored. A `docker.io`
  repository without a `/` gets the `library/` namespace, whatever spelling named the registry (`nginx`,
  `docker.io/nginx` and `index.docker.io/nginx` → `docker.io/library/nginx:latest`); no other registry gets it
  (`localhost/app` stays `localhost` / `app`).
- **Mirror-wrapped references:** after the registry mapping above, when the registry is `docker.io` and the first
  repository segment is `mcr.microsoft.com`, `ghcr.io`, `quay.io` or a host-like name containing `harbor`, that
  segment becomes the registry (`docker.io/ghcr.io/owner/app:1` and `index.docker.io/ghcr.io/owner/app:1` →
  `ghcr.io/owner/app:1`). A single-segment repository such as `docker.io/ghcr.io` is no wrapper: it is
  `docker.io` / `library/ghcr.io`.
- No other validation happens when parsing: characters and lengths are not checked. A reference without a repository
  name is rejected.
- A reference read from a remote side — the base-image labels of an image configuration and the image of a Docker
  Engine container — is accepted only when its registry (host and optional port, at most 200 characters), repository
  (`/`-separated path components of `[a-z0-9]` runs joined by `.`, `_`, `__` or one or more `-`, at most 255
  characters; no `.` or `..` component) and
  tag (at most 128 characters) fit the reference grammar of Docker; otherwise it is treated as absent. A digest
  outside the digest grammar becomes none and the reference is kept. A reference entered by the user keeps the plain
  parsing above; registry requests refuse its `.` and `..` segments and, for the OCI client, a registry that is not a host with an optional port ([Registries](registries.md#request-paths)). The Trivy scan passes it after `--`, so a reference that begins with `-` is never read as a Trivy option ([Vulnerabilities](vulnerabilities.md#trivy)). See [0014](../decisions/0014-registry-tag-names-and-digests-are-validated-where-they-are-read.md) and [0015](../decisions/0015-the-trivy-scan-target-follows-an-end-of-options-marker.md).
- Every Docker Hub spelling therefore parses to one canonical form, `docker.io` with a repository that has a namespace
  (`library/` for official images), and so to one catalog entry. A single-segment repository of 248 to 255
  characters becomes longer than 255 with `library/` and is then rejected as a remote reference.
- The canonical text form is `registry/repository:tag`, followed by `@digest` when the digest is known. Parsing the
  canonical form gives back the same parts.

### The catalog

- A **registry repository** is a unique pair of registry (at most 200 characters) and repository (at most 300).
- An **image version** is a unique combination of registry repository, tag (at most 200 characters) and digest (at most
  255). The digest is optional; an image version without one is a different entry from the same tag with a digest.
- Registry, repository and tag must be non-blank. The catalog trims the digest and drops any `name@` prefix in front of
  it, but changes no case: parts are compared exactly, so writers pass references normalized as above.
- Writers use get-or-create: an existing entry is returned unchanged; a new one stores the given publication time and
  metadata (at most 4000 characters). Refreshing the publication time or metadata of an existing entry is done by the
  component that owns the refresh ([Base images](base-images.md) for observed images and their bases, the runtime
  container scan for container images, see [Update detection](update-detection.md)).
- **Concurrency:** when two writers create the same entry at once, the one whose insert collides with the uniqueness
  rule re-reads and returns the entry the other created. No duplicates arise; no writer sees an error for the race.
- Every image version records which kind of component registered it last: observed image, runtime container or
  base-image resolution. The value is informational and is overwritten by the next registering component.
- Catalog entries are shared: observed images, container snapshots, base-image relationships, update findings and
  vulnerability findings all point at the same entries. Catalog entries are never deleted.

### Moving tracked state to canonical entries

- Entries written by earlier versions can hold a non-canonical spelling. At startup, after the migrations and before
  any background job, `ImageCatalogCanonicalizer` moves two kinds of rows that never re-parse their reference on their
  own: the current image version of every observed image, and every container tag selection. Only rows the rules above
  can change are read: registry `index.docker.io` or `registry-1.docker.io`, and `docker.io` with a repository
  without a `/`. The canonical pair is the parse of `registry/repository`, so a move can end on another registry
  (`index.docker.io` / `ghcr.io/owner/app` moves to `ghcr.io` / `owner/app`).
- An observed image gets the canonical image version for the same tag and digest through get-or-create; a version
  the step creates takes the old one's publication time and metadata. A tag selection gets the canonical registry
  repository. Nothing else of the row changes (name, enabled flag, update time).
- A row whose canonical registry or repository fails the reference rules (registry at most 200 characters,
  repository at most 255, host and path grammar) stays unchanged. Rows that are already canonical, and rows of other
  registries, are not touched, so a second run moves nothing.
- No catalog entry and no observed image is deleted. Two observed images can end on one version (two manual ones, or a
  manual one on the version of an enabled discovered image); both stay, and the discovered image is unchanged.
- Until the next scans, a moved observed image has no base images, and on a newly created version no vulnerability
  assessment. A moved tag selection applies from the container's next runtime scan. Snapshots, findings and scan runs
  of the old entries stay on them and age out.
- The number of moved rows is logged (event `1009`) when it is above zero ([Hosting](hosting.md#startup-sequence)). A
  database error in the step fails startup like the other startup steps.

### Live image versions

- The live image versions are: the current version of every observed image (enabled or not); the image versions of
  the containers in the latest runtime scan of each Docker instance (for an instance without a runtime scan, the
  latest snapshot of each of its containers); and the base images recorded for any of these versions.
- The live set decides which images get vulnerability refreshes and which findings the cleanup keeps
  ([Vulnerabilities](vulnerabilities.md), [Scan engine](scan-engine.md)).

### Observed images

- An observed image is a tracked target: a name (at most 200 characters, not required to be unique; the name of a
  discovered image is its repository cut to 200 characters with a trailing `…`), an optional
  description, its current image version, an enabled flag, its source and its creation and update times.
- Sources: *manual* (registered on `/observed-images`) and *discovery* (added by the Docker Hub account discovery and
  shown as "My images" on `/my-images`; see [Registries](registries.md)). Discovered images are the user's own images.
- **Manual registration** takes a name and an image reference (both required) and an optional description, then:
  1. parses the reference and gets or creates its image version exactly as written — tag and, if given, digest — with
     no registry call;
  2. validates the trimmed name first (not blank, at most 200 characters), before anything reaches the catalog;
  3. looks for the observed images whose current version is that image version, oldest first (by creation time, then
     id). If one of them is an enabled discovered image, the registration is rejected and nothing is changed. Otherwise
     the oldest manual one is updated (name and description replaced, enabled again, version unchanged, update time set
     from the injected clock), or a new manual image is created (name trimmed, description trimmed or none, enabled).
     Disabled discovered images and images of other sources are ignored, and a discovered image is never written;
  4. starts a manual scan of that image ([Base images](base-images.md)) and opens its detail page.
- The current version of a manual image never moves on its own: scans refresh its metadata but keep its tag and
  digest. A discovered image's current version is moved by each discovery run to the repository's most recently
  pushed tag.
- **Enabled state:** only discovery disables an observed image (when its repository disappears from the account) and
  it enables again every image it lists. Manual registration of an existing reference enables it again. There is no way to
  disable or delete an observed image from the UI. Disabled images are skipped by the scheduled observed-image scan
  ([Scan engine](scan-engine.md)) and keep their history.
- **Own images and running containers:** a running container is linked to an own image when its image's registry and
  repository equal (case-insensitive) those of a discovered image's current version, regardless of tag and digest;
  when several match, the one first by name wins. Manually registered images are not linked this way.

## Limits and errors

- A blank name or reference is rejected by the registration form. A reference without a repository name makes the
  registration fail and the form shows `Registration failed: The image reference does not contain a repository name`;
  a reference part longer than the stored limits makes it fail with `Registration failed: unexpected error`. In both cases the
  observed image is not saved (an image version created before the failure remains in the catalog).
- A name that is blank after trimming fails with `Registration failed: The name must not be empty`; a trimmed name
  longer than 200 characters with `Registration failed: The name must not be longer than 200 characters`. The limit
  counts UTF-16 code units of the trimmed .NET string, never more than the column's character count; a name of 200
  characters with surrounding whitespace is accepted. Nothing is saved and no catalog entry is created.
- A reference that is the current version of an enabled discovered image fails with `Registration failed: This image
  is already tracked as own image '<name>'.` (the name with control characters replaced); the form links to the own
  image. A reference that differs from an own image's version in digest or tag registers as a manual image.
- Two registrations under one name with different references create two manual images. Earlier duplicates stay
  (deletable once [#282](https://github.com/LarsLaskowski/DockerUpdateGuard/issues/282) lands), and simultaneous
  registrations of one reference can create two manual images.
- The catalog rejects a blank registry, repository or tag.
- Nothing in this area handles credentials.

### Known gaps

- Observed images cannot be disabled or deleted by the user, and discovery enables every listed repository again on
  each run. Tracked in [#282](https://github.com/LarsLaskowski/DockerUpdateGuard/issues/282).
- The recorded kind of the component that last registered an image version is last-writer-wins and carries no
  meaning, and a further kind (registry metadata) is defined but never written.
  Tracked in [#293](https://github.com/LarsLaskowski/DockerUpdateGuard/issues/293).

## Related decisions

- [0048](../decisions/0048-manual-registration-identifies-an-observed-image-by-its-image-version.md): manual registration
  identifies an observed image by its image version.
- [0053](../decisions/0053-tracked-state-moves-to-the-canonical-catalog-entries-at-startup.md): tracked state moves to
  the canonical catalog entries at startup.

## Not here

- Reading tags and metadata from registries and the account discovery itself: [Registries](registries.md).
- Base-image chains and derived runtimes: [Base images](base-images.md).
- Comparing tags and choosing updates: [Update detection](update-detection.md).
- Container snapshots: [Docker instances](docker-instances.md).
- Vulnerability findings: [Vulnerabilities](vulnerabilities.md).
- Scan runs, schedules and retention: [Scan engine](scan-engine.md).
- What the pages show: [Web UI](web-ui.md).
