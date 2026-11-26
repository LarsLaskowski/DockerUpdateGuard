# 0063: Other registries take a tag's publication time from its image configuration

- **Status:** Proposed
- **Date:** 2026-11-26
- **Area:** Registries
- **Source:** Issue #277
- **Supersedes:** —

## Context

The OCI client read a tag's publication time only from a top-level `created` field of the manifest. Image manifests
of the OCI image specification have no such field (a real `ghcr.io` platform manifest has none), so tags of every
registry other than Docker Hub had no publication time: the published-since bound of tag listings had no effect, the
UI and `ImageVersion.PublishedAtUtc` showed no date, and the update rules that need both timestamps never applied.
Every image's configuration blob carries `created`, the build time; reproducible builds set it to the Unix epoch on
purpose (a real `gcr.io/distroless` image does).

## Options considered

1. **Fall back to the configuration blob's `created` when the manifest has none** — known for nearly every image, at
   the cost of one more request per tag lookup. Chosen (the Product Manager's decision on #277).
2. **Read the manifest annotation `org.opencontainers.image.created`** — no extra request, but optional and rarely
   set (absent from the sampled manifests), so most tags would still have no time.
3. **Always read the configuration blob** — one rule for every tag, but an extra request even where the manifest
   already answers.

## Decision

A tag lookup on another registry takes the manifest's top-level `created`, otherwise the `created` of the
configuration blob of the (selected platform) manifest; a value at or before `1970-01-01T00:00:00Z`, a missing or
unreadable one, or a blob that cannot be read leaves the time unknown, and the lookup still succeeds with its digest.
The rules and the cost are stated in [Registries](../areas/registries.md#what-can-be-read).

## Consequences

- For other registries the publication time is the image's build time, for Docker Hub the push time
  (`last_pushed`); a tag pushed long after its build looks older than on Docker Hub.
- A tag lookup sends one more request whenever the manifest has no usable `created` (nearly always), so a bounded
  listing sends up to its maximum tag count (250 by default) more requests.
- Published-since bounds, the newer-than-current rules and the age conditions of the major-upgrade policy
  ([Update detection](../areas/update-detection.md)) now apply to tags of other registries too, so a major upgrade
  deferred there for lack of timestamps can now be recommended.
- The time is best effort: a failing blob request never fails the tag lookup, so an unknown time does not tell
  whether the image had none or the blob could not be read.