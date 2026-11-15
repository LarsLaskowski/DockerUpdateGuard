# 0043: Runtime findings kept through a failed container evaluation move to its new snapshot

- **Status:** Accepted
- **Date:** 2026-11-15
- **Area:** Update detection
- **Source:** Issue #489
- **Supersedes:** —

## Context

A runtime scan records a new snapshot for every discovered container before it reads the container's registry tags.
When the tag lookup fails, or processing the container raises an error after the snapshot was recorded, the
container's previous update findings stay active, as they should through a transient failure, but they stay on the
previous snapshot. The pages read a container's findings through its latest snapshot (container list and detail, the
runtime alerts of the observed and own images), so the alerts disappear until the next good scan: the flicker F-025
(#129) removed for observed images. The derived runtime findings of [0042](0042-a-runtime-container-keeps-its-derived-runtime-finding-while-its-runtime-check-cannot-be-completed.md)
are affected too, because their runtime check runs only after a completed update evaluation; 0042 split the choice
made here into this issue (its option 5).

## Options considered

1. **Move every finding of the container that is still active after the failure to the new snapshot (chosen)** — the
   same mechanism as 0042, in the scan that owns the findings; the pages stay unchanged, the finding keeps its
   identity, detection time and tag candidates.
2. **Let the pages read a container's active findings per container instead of per latest snapshot** — would also
   show the findings, but changes four view reads, leaves two mechanisms for one rule beside 0042, and shows the
   latest snapshot next to findings that do not belong to it.
3. **Record no new snapshot when the tag lookup fails** — the findings would stay on the snapshot the pages read, but
   the current containers of an instance are the snapshots of its latest run, so the container would drop out of the
   pages, together with its *failed* status and its current runtime status.
4. **Copy the findings onto the new snapshot and deactivate the old ones** — rejected for the reasons of 0042,
   option 4: every failure would resolve still valid findings and create new ones with a new detection time.

## Decision

Option 1: when a container's tag lookup fails, or its processing raises an error after its new snapshot was
recorded, every finding of the container that is still active at that point stays active and moves to the new
snapshot. The rules are in [Update detection](../areas/update-detection.md#update-findings-and-tag-candidates).

## Consequences

- An update alert, its tag candidates and the derived runtime alert stay visible through a transient registry
  failure or a processing error, and disappear only after a scan that evaluated the container.
- A finding the failed processing had already deactivated stays inactive on its old snapshot, and one it had
  already created stays on the new snapshot; only what is still active moves.
- Every runtime scan reads the container's active findings once more, before its registry calls.
- A container whose processing fails before its image version is known, and the resolved and available version of a
  snapshot whose evaluation did not complete, are decided in
  [0044](0044-a-runtime-container-whose-processing-fails-takes-what-it-could-not-determine-from-its-latest-earlier-snapshot.md).
- The previous snapshot no longer holds an active finding and falls under the normal snapshot retention.