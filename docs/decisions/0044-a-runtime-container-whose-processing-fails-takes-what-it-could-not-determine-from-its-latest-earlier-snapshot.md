# 0044: A runtime container whose processing fails takes what it could not determine from its latest earlier snapshot

- **Status:** Proposed
- **Date:** 2026-11-15
- **Area:** Docker instances
- **Source:** Issues #491, #492
- **Supersedes:** —

## Context

[0043](0043-runtime-findings-kept-through-a-failed-container-evaluation-move-to-its-new-snapshot.md) moves the active
findings of a container whose processing fails to its new snapshot, which the pages read. Two gaps remained. A container
whose processing fails before its snapshot is recorded (the image catalog call throws) gets no snapshot in the run, and
since the current containers of an instance are the snapshots of its latest run, it drops out of the pages with its
findings until the next scan (#491). And a snapshot whose evaluation did not complete gets no resolved or available
update version, so the container list works both out from the kept findings' tag candidates, which seldom hold the
running version: an alias such as `latest` is shown as the version (#492). A snapshot needs an image version, and the
one the failed catalog call would have returned is unknown; a container keeps its image for its whole life, so its
earlier snapshots name the image it still runs.

## Options considered

1. **Take the missing values from the container's latest earlier snapshot (chosen)** — a failed snapshot is recorded
   with that snapshot's image version when the catalog call failed, and every new snapshot starts with its resolved
   and available update version when both name the same image version, until a completed evaluation replaces them.
   One rule for both gaps, in the scan that owns the snapshots; the pages stay unchanged.
2. **Let the pages fall back to a container's older snapshot when the latest run has none for it, or none of the two
   versions** — the pages cannot tell a container whose processing failed from one that was removed, and every
   current-container read (container list and detail, image pages, container actions) would need the fallback.
3. **Allow a snapshot without an image version** — an entity model change with a migration, and every page that reads
   a snapshot's image would have to handle a missing one.
4. **Carry the two versions only in the failure branches** — the error handler cannot tell whether the evaluation had
   already stored versions of its own before the error, so it would overwrite them or need a flag across methods.

## Decision

Option 1: a container whose processing fails before its image version is known is recorded with the image version of
its latest earlier snapshot, and a new snapshot keeps that snapshot's resolved and available update version, for the same image version and provided the running digest is known, unless an evaluation of the registry's answer replaces them. The rules are in
[Docker instances](../areas/docker-instances.md#container-snapshots) and
[Update detection](../areas/update-detection.md#alias-resolution).

## Consequences

- A container whose processing fails stays among the current containers, *failed*, with its active findings and the
  versions shown before the failure; container actions stay available for it, from the runtime status this scan
  discovered.
- A container seen for the first time still records no snapshot when its catalog call fails: there is nothing earlier
  to take the image version from.
- Every runtime scan reads each container's latest earlier snapshot once more, before the catalog call.
- *No tag data* and *unsupported* store no versions, as before: they are evaluations, and their findings are
  deactivated.