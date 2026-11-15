# 0042: A runtime container keeps its derived runtime finding while its runtime check cannot be completed

- **Status:** Accepted
- **Date:** 2026-11-15
- **Area:** Base images
- **Source:** Issue #385
- **Supersedes:** —

## Context

A runtime scan deactivated every active finding of a container whose update evaluation completed, the derived base
runtime update included, and created the derived finding again only when the local image inspect, the image history,
the runtime detection and the .NET or nginx release feed all succeeded. A transient Docker Engine or feed failure
therefore resolved a still valid runtime alert until the next good scan. Observed images do not flicker this way since
F-025 (#129): their derived findings stay when the image configuration or the feed cannot be read.

The pages read a container's findings through its latest snapshot, and every runtime scan records a new snapshot, so a
finding that only stays active on the previous snapshot is not shown any more.

## Options considered

1. **Keep the previous derived finding and attach it to the container's new snapshot whenever an input of the runtime
   check cannot be read (chosen)** — the alert stays visible through a transient failure, as for observed images; the
   finding keeps its identity and first detection time.
2. **Keep the deactivation and document the asymmetry** — no code change, but the alert keeps flickering on every
   transient Docker Engine or feed failure, the defect F-025 removed for observed images.
3. **Keep the previous finding active on its old snapshot** — the database matches observed images, but no page
   shows it, so the user sees the same flicker.
4. **Copy the previous finding onto the new snapshot and deactivate the old one** — visible too, but every failed
   check resolves a finding that is still valid and adds a new one with a new detection time.
5. **Read active findings per container instead of per latest snapshot in the pages** — would also show findings kept
   after a failed tag lookup, but changes four view queries and what the pages show for a container's history;
   beyond this issue, split into a follow-up issue.

## Decision

Option 1: a completed update evaluation no longer deactivates the container's derived base runtime findings; the
runtime check does. When the image inspect, the image history or the release feed cannot be read, the previous derived
findings stay active and move to the new snapshot; when all inputs were read, they are deactivated and a new one is
created if the runtime is still outdated. The rules are in [Base images](../areas/base-images.md#release-feeds-and-runtime-findings).

## Consequences

- A runtime alert survives a transient failure of the Docker Engine or a release feed and disappears only after a
  check that read all its inputs.
- A kept finding still states the versions of the check that created it; a channel missing from the feed keeps it
  too, as for observed images.
- A history the Docker Engine cannot return keeps the finding even when the environment alone would detect a runtime,
  because a stronger history signal could name another one.
- The previous snapshot no longer holds an active finding and falls under the normal snapshot retention.