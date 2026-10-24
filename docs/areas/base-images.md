# Base images

## Scope

Which images an observed image is built on, and what follows from that: the observed-image scan that resolves the
base-image chain from image labels, the base-image relationship graph it stores, the shared base images view that shows
which observed images and running containers depend on the same base, and the derived runtime detection that recognizes
a .NET or nginx runtime inside an image and compares it with the upstream release feeds. Users rely on it to see where
a base-image update or vulnerability reaches; vulnerability refreshes and the dashboard rely on the stored graph.

## Behavior

### Observed-image scan

- An observed-image scan works on the observed image's current image version ([Image catalog](image-catalog.md)) in
  three steps, each reading registry data as described in [Registries](registries.md):
  1. **Tag refresh** — a tag lookup for the current version. On success its publication time and tag metadata are
     stored on the version. Otherwise the scan becomes *failed* (outcome *failed*) or *partial* (any other outcome).
  2. **Base-image resolution** — described below.
  3. **Derived runtime check** — described below.
- The scan ends *succeeded* when no step reported a problem, *partial* or *failed* as the steps decide (*failed* is
  never lowered to *partial*). An unexpected error in any step makes the scan *failed*. The messages of all steps are
  stored on the scan run, each distinct message once. When scans run, overlap rules and scan-run bookkeeping:
  [Scan engine](scan-engine.md).

### Resolving the chain

- Base images are resolved only for observed images. Running containers are not resolved themselves; a container
  joins an observed image's chain only when it runs the exact image version — tag and digest — of that observed image.
  A container on another digest of the same tag has no base images.
- The source is the image configuration's labels: `org.opencontainers.image.base.name` names the base image (parsed as
  an image reference, see [Image catalog](image-catalog.md)); `org.opencontainers.image.base.digest` supplies its
  digest (lowercased) when the name carries none. The same rule applies to Docker Hub and to every other registry.
- The chain is followed by reading each base image's own configuration and labels. Depth 1 is the direct base; the
  chain ends at depth 5, at an image without the label, or at a base whose configuration cannot be read (the chain
  found so far is kept).
- An image without the label has an empty chain; that is a successful resolution.
- When the observed image's own configuration cannot be read, resolution is unavailable: the stored chain is left
  unchanged and the scan becomes *failed* (outcome *failed*) or *partial* (any other outcome).
- Only base-image relationships are produced. The relationship type for build stages exists in storage but nothing
  records it.

### Storing the chain

- A successful resolution replaces the stored base-image relationships of the current version: the previous ones are
  removed and the new chain (possibly empty) is recorded. Relationships belong to the image version, so those of an
  earlier current version remain when the observed image moves to a new version.
- For each base image a tag lookup refreshes the exact base. Its stored tag is:
  - the label's tag when it is a specific version tag (semantic or year-prefixed, see
    [Update detection](update-detection.md)) or when no digest is known;
  - otherwise (a floating tag such as `latest` with a digest) the specific version tag of the same repository that
    points at the same digest, searched among at most 150 tags — restricted to the floating tag's version line unless
    it is `latest`, and to tags published since the floating tag itself; when none is found, the label's tag.
- The base image's catalog entry is the stored tag plus the digest. When the tag lookup succeeded, its publication
  time and tag metadata are stored on that entry; when it did not, the relationship is still recorded and the scan
  becomes *partial* (unless already *failed*) with the lookup's message.
- A relationship records the child (the observed image's current version), the base, the depth, the source reference
  (the image whose label named this base: the observed image for depth 1, the previous base for deeper levels) and the
  scan run that recorded it (cleared when that run is deleted).
- A relationship is unique per child, base, depth and type; depth is greater than zero and child and base differ.

### Shared base images

- `/base-images` lists every base image recorded for the current version of any observed image (enabled or not), at
  every depth. An entry used by a single observed image is listed too.
- Entries are grouped case-insensitively: a base with a digest by `registry/repository@digest` (tags that resolve to
  the same digest merge); a base without a digest by `registry/repository:tag` together with its source reference.
- Each entry shows the number of distinct observed images using it, the references of the images that name it, the
  running containers whose latest snapshot is exactly one of those image versions, and a vulnerability summary of the
  base ([Vulnerabilities](vulnerabilities.md)).
- Entries are ordered by number of running containers, then number of observed images (both descending), then by
  reference.

### Derived runtime detection

- Recognizes the .NET or nginx runtime an image is built on, also without base-image labels. One runtime per image.
- Inputs: for observed images the environment variables of the image configuration from the registry; for running
  containers the environment of the local image and its build history (each entry's command and comment) from the
  Docker Engine ([Docker instances](docker-instances.md)). Registry images have no build history, so the
  history-based signals apply to running containers only.
- Signals, strongest first; on equal strength environment variables win over history, and earlier history entries
  over later ones:

  | Strength | Signal |
  | -------- | ------ |
  | 0 | Environment variable `DOTNET_VERSION` |
  | 1 | Environment variable `NGINX_VERSION`; `DOTNET_VERSION` in history |
  | 2 | Environment variable `ASPNET_VERSION`; `NGINX_VERSION` in history |
  | 3 | `ASPNET_VERSION` in history |
  | 4 | A history reference to `mcr.microsoft.com/dotnet/aspnet`, `runtime`, `runtime-deps` or `sdk` with tag `x.y.z` |

- Variable names are matched case-insensitively; in history the name must be followed by `=`, `:` or whitespace and a
  version. The version is the first `major.minor.patch` in the value (anything after it is ignored); a value without
  three numeric parts is no signal. The channel is `major.minor`.

### Release feeds and runtime findings

- **.NET:** `releases-index.json` under `DockerUpdateGuard:ReleaseMetadata:DotNetBaseUrl` (default
  `https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/`). Per channel (`channel-version`) the latest
  runtime (`latest-runtime`) and whether that release is a security release (`security`).
- **nginx:** the `download/` page under `DockerUpdateGuard:ReleaseMetadata:NginxBaseUrl` (default
  `https://nginx.org/`). Every `nginx-x.y.z.tar.gz` link counts; a channel's latest is its highest version.
- Both URLs are fixed at startup and must be absolute `http`/`https` URLs ([Hosting](hosting.md)). A feed is loaded at
  most once per scan and not kept between scans. A channel missing from the feed gives no finding.
- A runtime is outdated when the feed's latest version of the same channel is higher than the detected one. Only
  patch updates within the channel are reported; a newer channel is never suggested.
- An outdated runtime yields an update finding of type *derived base runtime update*:
  - on an observed image: summary "Own image uses an outdated .NET base runtime" (discovered image) or "Observed image
    uses an outdated .NET base runtime" (manual image), likewise for NGINX. The details name the detected and the
    latest version, add for .NET that the latest release includes security fixes when it does, and advise to rebuild
    and republish (own image) or that the upstream publisher must rebuild (manual image);
  - on a running container: the same check, made only when the container's update evaluation completed
    ([Update detection](update-detection.md)); the summary says "Own image …" when the container runs a discovered
    image's repository, otherwise "A linked image uses an outdated … base runtime".
- On an observed image the previous findings are replaced: with no runtime detected they are removed; with a runtime
  detected and the feed available they are removed and a new one is added if still outdated; when the configuration
  or the feed cannot be read they stay as they are.

## Limits and errors

- Chain depth is capped at 5; base-tag resolution searches at most 150 tags.
- Registry outcomes other than *succeeded* never abort the scan; they set *partial* or *failed* and add the registry's
  message (status code and response body, see [Registries](registries.md)).
- Feed failures (non-success status, missing `releases-index` array, unreadable content) only suppress the runtime
  finding; they do not change the scan status. Unreadable content is logged as a warning.

### Known gaps

- The relationship type for build stages is defined in storage but never produced. Tracked in
  [#293](https://github.com/LarsLaskowski/DockerUpdateGuard/issues/293).
- The shared base images view also lists bases used by a single observed image and counts disabled observed images.
  Tracked in [#286](https://github.com/LarsLaskowski/DockerUpdateGuard/issues/286).

## Related decisions

None yet.

## Not here

- Reading configurations, tags and labels from registries, retry and timeouts: [Registries](registries.md).
- Parsing references and the catalog entries: [Image catalog](image-catalog.md).
- Tag formats, version lines and update recommendations: [Update detection](update-detection.md).
- Vulnerabilities of base images: [Vulnerabilities](vulnerabilities.md).
- Schedules, overlap rules and scan-run retention: [Scan engine](scan-engine.md).
- Page layout of `/base-images` and the detail pages: [Web UI](web-ui.md).
