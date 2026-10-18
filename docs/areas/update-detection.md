# Update detection

## Scope

Which update DockerUpdateGuard recommends for a running container image, given the tags its registry publishes: how
tags are parsed and compared, how floating alias tags are resolved to a concrete version, how conservative a major
upgrade is, which assessment status a runtime container gets, and how update findings with their ranked tag candidates
and the manual tag selection per container are kept. Users rely on it to decide what to update; the UI and the
inventory metrics rely on the stored assessments and findings.

## Behavior

### Tag formats

- A **semantic version tag** has the form `[v|V]<major>.<minor>.<patch>[-<suffix>]` with decimal components, e.g.
  `1.27.3`, `v2.0.0-alpine`, `8.0.11-bookworm-slim`. A component too large for a 32-bit integer makes the tag
  non-semantic.
- A **version line tag** has the form `[v|V]<major>.<minor>[-<suffix>]`, e.g. `8.0` or `1.27-alpine`. It is treated as
  an alias of the newest patch of that line, never as a version itself.
- A **year-prefixed tag** has the form `<four digits>-<suffix>`, e.g. `2022-CU12-ubuntu-22.04`.
- Every other tag (`latest`, `stable`, `alpine`, ...) is an **alias tag**.
- Tag names are compared case-insensitively throughout. When the registry reports the same tag name twice, only the
  most recently published entry counts.

### Variant families and ordering

- The suffix of a semantic version tag is split at `-` into segments. A segment whose leading letters are one of
  `alpha`, `beta`, `rc`, `pre`, `preview`, `dev`, `snapshot`, `nightly`, `canary`, `milestone`, `ea` (any case) is a
  **pre-release segment**; all others are **variant segments**.
- The **variant family** of a semantic tag is its variant segments, each reduced to its leading letters (or kept
  whole when it has none), joined by `-`: `1.2.3-alpine3.19` and `1.2.4-alpine3.20` are both family `alpine`;
  `1.2.3-rc1` and `1.2.3` are both the plain family.
- Two semantic tags are comparable only within the same variant family. A tag of another family is never a candidate,
  so an Alpine tag is never recommended for a Debian one.
- Comparable tags order by major, minor, patch; then by their variant segments in natural order (digit runs compared
  numerically, text case-insensitively); then a release ranks above any pre-release of the same version; then
  pre-release segments in natural order (`rc2` below `rc10`).
- Year-prefixed tags are comparable only within the same year and the same year-variant family. The year-variant
  family is the suffix after its leading servicing segments (segments that start with letters and contain a digit,
  such as `CU12`), lower-cased; it is empty when that rest contains no letter. Within a family, tags order by the natural order of the
  suffix.

### Alias resolution

- An alias or version line tag is resolved to the semantic version tag that carries the same digest. The digest is the
  running digest, or, when that is unknown, the registry digest of the alias tag. Without any digest nothing is
  resolved.
- For a version line tag only semantic tags of the same major, minor and variant family qualify.
- When several tags qualify, the order of preference is: the variant family of a given reference tag (used when
  resolving a recommended alias against the container's current version), then plain tags without a variant, then the
  most recently published, then the highest version.
- The resolved tag is stored on each container snapshot as the container's *resolved version*. When it cannot be
  resolved in a scan, the resolved version of the most recent earlier snapshot of the same container and the same
  image version is carried over, provided the running digest is known.
- When an update is available, the snapshot also stores the *available update version*: the recommended tag itself
  when it is a semantic or year-prefixed tag, otherwise the semantic tag the recommended alias resolves to.

### Input of an evaluation

- A runtime container is evaluated against at most 250 repository tags requested from its registry for the image's
  operating system and architecture ([Registries](registries.md)).
- Before evaluation, tags are dropped that were published before the current tag or that order below the current
  version (or, for `latest`, below the version `latest` resolves to), unless they carry the running digest. The
  current tag itself is always kept, with the metadata of the direct lookup of the current tag when that succeeded.

### Evaluation rules

An evaluation returns one of *up to date*, *update available*, *needs review* or *unknown*, a summary, optional
details, an optional recommended tag and digest, and at most **50** candidates. The first rule that applies wins:

1. **No tags** — *unknown*, "No registry tag information is available".
2. **`latest` unchanged** — the current tag is `latest` and the running digest equals the registry digest of `latest`:
   *up to date*.
3. **Semantic current tag** — candidates are the comparable tags that order above the current tag, are not
   pre-releases (unless the current tag is one), do not carry the running digest and were published after the
   current tag (when both timestamps are known). They are ordered highest first, ties broken by newest publication,
   and filtered by the major-upgrade policy below. If candidates remain, the first is recommended (*update
   available*). Otherwise, if the current tag was republished with a different digest, that is the update; otherwise
   *up to date*.
4. **Year-prefixed current tag** — candidates are the tags of the same year and year-variant family that order above
   the current tag, with the same digest and publication conditions; highest first. No major-upgrade policy applies,
   and no other year is ever recommended. Without candidates, a republished current tag is the update; otherwise *up
   to date*.
5. **Republished alias** — the registry digest of the current tag differs from the running digest: *update available*
   recommending the current tag with its new digest. Its candidates are the tags carrying that new digest: the
   current tag first, then tags of the same version line, then semantic tags, then newest and highest.
6. **Version line alias** — the running digest matches a semantic tag of the line: that tag is the current version and
   rule 3 is applied to it. The resolved current version is added to the candidates. Without a successor the result is
   *up to date*, "The running digest matches version tag '…'".
7. **Anything else** — every other tag that has a digest and was published after the current tag (when both
   timestamps are known; newest first, at most 50) is a candidate for *needs review*, "Alternative tags are available and require manual review". With no
   such tag the result is *up to date*.

### Major-upgrade policy

- When a successor exists in the current major line, the highest of them is recommended. Higher majors are kept as
  further candidates but are never recommended.
- When the only successors are in higher majors, only the **next** major (the lowest one above the current) can be
  recommended, and only when all of the following hold:
  - the next major has at least `DockerUpdateGuard:Scanning:MajorUpgradeMinimumReleaseCount` distinct candidate
    versions (default `2`, range `0`–`100`, `0` disables this condition);
  - and, unless `DockerUpdateGuard:Scanning:MajorUpgradeMinimumAgeDays` is `0` (default `30`, range `0`–`3650`):
    - the earliest known release of the next major (same variant family, with a publication timestamp) is exactly
      `<major>.0.0`, so a truncated tag history defers the upgrade;
    - that first release was published at least the configured number of days ago;
    - the most recent release of the current major line is known, at least that many days old, and not later than the
      first release of the next major (the current line is dormant, not maintained next to the new one).
- A recommended major upgrade carries the details "No newer version is available in the `<major>.x` version line".
- A deferred major upgrade yields *up to date*, "No newer version was found in the `<major>.x` version line", with the
  details "Tag '…' introduces a new major version and is not recommended yet". A republished current tag (rule 3)
  takes precedence over this result.
- The age conditions are measured against the wall clock at evaluation time; a deferred upgrade becomes a
  recommendation in a later scan without any other change.

### Runtime container assessment

Each container snapshot ([Docker instances](docker-instances.md)) carries an update assessment status and a message
(the summary, followed by the details when present):

| Status | When |
| --- | --- |
| Not evaluated | Initial value of a new snapshot |
| Up to date | Evaluation result *up to date* |
| Update available | Evaluation result *update available* |
| Manual review required | Evaluation result *needs review* |
| No tag data | Evaluation result *unknown*, or the registry reports the repository as not found |
| Unsupported | No registry client supports the image's registry |
| Failed | The tag lookup failed for any other reason, or processing the container raised an error; the message is the failure message |

A container that ends *Failed*, *Unsupported* or with *No tag data* because of the registry answer makes its runtime
scan run *partial*, with the message added to the run ([Scan engine](scan-engine.md)).

### Update findings and tag candidates

- For *update available* a finding of type *runtime image update* is created, for *needs review* one of type *tag
  recommendation*. Each finding references the scan run, the container snapshot, the image version it is about and,
  when a tag is recommended, the catalog image version of the recommended tag and digest
  ([Image catalog](image-catalog.md)).
- The candidates of the evaluation are stored as tag candidates with tag, digest, publication time, the evaluation
  summary as reason, a rank equal to the candidate's 1-based position in the evaluation and a *recommended* flag on the
  candidate whose tag equals the recommended tag. Candidates without a digest are not stored, so ranks may have gaps.
  A finding holds each tag/digest pair at most once.
- Whenever a container was evaluated (including *no tag data* and *unsupported*), all active findings of the same
  container on the same Docker instance are first deactivated: they become inactive, get a resolution timestamp and
  lose their tag candidates. When the tag lookup failed, the previous findings stay active.
- After a runtime scan of an instance, the active findings of containers that were no longer discovered are
  deactivated the same way.
- Findings of type *base image update* exist in the data model but are not produced. Findings of type *derived base
  runtime update* are owned by [Base images](base-images.md).

### Manual tag selection

- A user can select one tag, with an optional digest, per runtime container (Docker instance and container id), and
  clear it again. A new selection replaces the previous one; tag and digest are stored trimmed, an empty digest as
  none, together with the registry repository of the container's latest snapshot and the selection time.
- Clearing a container without a selection does nothing.
- The selection is a record of the user's choice only: it does not change the evaluation, the findings or any
  container action. It is shown while the container still runs an image of the same repository it was made for.
- A selection is removed together with its Docker instance or registry repository.

## Limits and errors

- At most 250 tags per runtime container evaluation and at most 50 candidates per finding.
- Stored lengths: tag candidate tag 200 characters, digest 255, reason 500; finding summary 500, details 2000;
  resolved and available update version 200; assessment message 2000.
- Selecting a tag for a container that has no snapshot fails with "Runtime container '<instance id>/<container id>'
  was not found"; nothing is stored.
- Evaluation itself performs no I/O and has no failure state of its own; registry failures reach it only as missing
  tags and are reported through the assessment status above, with the registry's failure message.

## Related decisions

None yet.

## Not here

- Fetching tags, digests and publication times, registry support and retries: [Registries](registries.md).
- Parsing and normalizing image references, the image version catalog: [Image catalog](image-catalog.md).
- Base-image resolution, derived .NET/nginx runtime findings and observed-image findings: [Base images](base-images.md).
- When runtime scans run, scan runs, retention and cleanup of findings and candidates: [Scan engine](scan-engine.md).
- Container discovery and snapshots: [Docker instances](docker-instances.md).
- Applying an update through Portainer: [Container actions](container-actions.md).
- How findings, candidates and the selection are displayed: [Web UI](web-ui.md).

## Open questions

1. The manual tag selection is stored and displayed but has no effect on update evaluation or on container actions.
   Is it meant to stay informational, or should it pin the recommendation or feed the Portainer update action?
2. Tag candidates without a digest are dropped when a finding is stored, which can leave a *runtime image update*
   finding with fewer candidates than the evaluation produced (or none) and ranks with gaps. Is that intended?
3. Findings of type *base image update* are defined but never created. Is the type reserved for future use or
   obsolete?
