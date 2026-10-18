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
  contains a `.` or a `:` or equals `localhost` (case-insensitive). Otherwise the registry is `docker.io`.
- **Repository:** the remaining segments joined with `/`, lowercased; empty segments are ignored. A reference with a
  single segment gets the `library/` namespace (`nginx` → `docker.io/library/nginx:latest`).
- **Mirror-wrapped references:** when the registry is `docker.io` and the first repository segment is
  `mcr.microsoft.com`, `ghcr.io`, `quay.io` or a host-like name containing `harbor`, that segment becomes the registry
  (`docker.io/ghcr.io/owner/app:1` → `ghcr.io/owner/app:1`).
- No other validation happens when parsing: characters and lengths are not checked. A reference without a repository
  name is rejected.
- Docker Hub spellings are not unified: `docker.io/nginx` has the repository `nginx`, not `library/nginx`, and
  `index.docker.io/…` is a different registry from `docker.io/…`. Registry access still treats them alike
  ([Registries](registries.md)), but they are separate catalog entries.
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

### Live image versions

- The live image versions are: the current version of every observed image (enabled or not); the image versions of
  the containers in the latest runtime scan of each Docker instance (for an instance without a runtime scan, the
  latest snapshot of each of its containers); and the base images recorded for any of these versions.
- The live set decides which images get vulnerability refreshes and which findings the cleanup keeps
  ([Vulnerabilities](vulnerabilities.md), [Scan engine](scan-engine.md)).

### Observed images

- An observed image is a tracked target: a name (at most 200 characters, not required to be unique), an optional
  description, its current image version, an enabled flag, its source and its creation and update times.
- Sources: *manual* (registered on `/observed-images`) and *discovery* (added by the Docker Hub account discovery and
  shown as "My images" on `/my-images`; see [Registries](registries.md)). Discovered images are the user's own images.
- **Manual registration** takes a name and an image reference (both required) and an optional description, then:
  1. parses the reference and gets or creates its image version exactly as written — tag and, if given, digest — with
     no registry call;
  2. looks for an observed image whose stored name equals the entered name exactly (case-sensitive, untrimmed);
  3. creates a new observed image (name trimmed, description trimmed or none, source *manual*, enabled), or updates
     the found one: description replaced, current version replaced, enabled again; name and source stay;
  4. starts a manual scan of that image ([Base images](base-images.md)) and opens its detail page.
- The current version of a manual image never moves on its own: scans refresh its metadata but keep its tag and
  digest. A discovered image's current version is moved by each discovery run to the repository's most recently
  pushed tag.
- **Enabled state:** only discovery disables an observed image (when its repository disappears from the account) and
  it enables again every image it lists. Manual registration of an existing name enables it again. There is no way to
  disable or delete an observed image from the UI. Disabled images are skipped by the scheduled observed-image scan
  ([Scan engine](scan-engine.md)) and keep their history.
- **Own images and running containers:** a running container is linked to an own image when its image's registry and
  repository equal (case-insensitive) those of a discovered image's current version, regardless of tag and digest;
  when several match, the one first by name wins. Manually registered images are not linked this way.

## Limits and errors

- A blank name or reference is rejected by the registration form. A reference without a repository name, or a value
  longer than the stored limits, makes the registration fail; the form shows the error message and the observed image
  is not saved (an image version created before the failure remains in the catalog).
- A name entered with leading or trailing whitespace does not match the stored, trimmed name and creates a further
  observed image.
- The catalog rejects a blank registry, repository or tag.
- Nothing in this area handles credentials.

## Related decisions

None yet.

## Not here

- Reading tags and metadata from registries and the account discovery itself: [Registries](registries.md).
- Base-image chains and derived runtimes: [Base images](base-images.md).
- Comparing tags and choosing updates: [Update detection](update-detection.md).
- Container snapshots: [Docker instances](docker-instances.md).
- Vulnerability findings: [Vulnerabilities](vulnerabilities.md).
- Scan runs, schedules and retention: [Scan engine](scan-engine.md).
- What the pages show: [Web UI](web-ui.md).

## Open questions

1. Docker Hub references are not normalized to one form (`docker.io/nginx` vs `nginx`, `index.docker.io` vs
   `docker.io`), so one image can have several catalog entries and not be recognized as shared. Should the parser
   unify them?
2. Manual registration matches an existing observed image by the untrimmed, case-sensitive name and names are not
   unique, so near-identical names create separate entries, and registering a manual image under a discovered image's
   name updates the discovered one (which the next discovery run overwrites). Is name the intended identity?
3. Observed images can neither be disabled nor deleted by the user, and discovery re-enables every listed repository on
   each run. Is a user-controlled enabled state planned?
4. The recorded "registered by" kind of an image version is last-writer-wins and a further kind (registry metadata) is
   defined but never written. Is the field meant to carry meaning?
