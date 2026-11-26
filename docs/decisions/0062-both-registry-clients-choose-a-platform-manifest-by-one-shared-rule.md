# 0062: Both registry clients choose a platform manifest by one shared rule

- **Status:** Proposed
- **Date:** 2026-11-26
- **Area:** Registries
- **Source:** Issue #277
- **Supersedes:** —

## Context

An image index (multi-platform image) lists one entry per platform image, and images built by BuildKit with
provenance or SBOM attestations add entries whose platform is `unknown/unknown` (a real `ghcr.io` index lists them
after each image). The Docker Hub client skipped `unknown` entries and preferred `linux`; the OCI client took an exact
platform match, then an architecture match, and otherwise the first entry with a digest. The image configuration
lookup passes no platform (`IRegistryMetadataClient.GetImageConfigurationAsync` has none), so for other registries it
always read the first entry: an attestation (whose configuration is the empty blob, so no labels, no environment and
no platform) or a Windows image, and base-image resolution followed the wrong labels.

## Options considered

1. **One shared selection helper used by both clients** (`Images/Helper/ManifestPlatformSelector`) — the rule cannot
   drift again; the OCI client keeps its preference for a requested platform. Chosen (the Product Manager's decision
   on #277).
2. **Fix the OCI client's fallback in place, keep two implementations** — same behavior today, but the two copies
   drifted once already.
3. **Add a platform parameter to `GetImageConfigurationAsync`** — would let callers name the platform, but changes
   the interface and every caller; out of scope of #277 and not needed once the default prefers `linux`.

## Decision

`ManifestPlatformSelector.SelectManifestDigest` is the one rule both clients apply to an image index: it never selects
an entry without a digest or with an `unknown` operating system or architecture, prefers an exact match and then an
architecture match of a requested platform, and otherwise takes the first `linux` entry, then the first remaining
one. The rule is stated in [Registries](../areas/registries.md#what-can-be-read).

## Consequences

- Configuration lookups on other registries read the Linux image instead of an attestation or a Windows image, so
  labels, environment, platform and base-image resolution come from the right manifest.
- Docker Hub behaves as before. For other registries an entry's operating system and architecture are compared as
  sent (case-insensitively) and no longer trimmed; only the requested platform is trimmed. Whitespace-padded platform
  values are not seen in practice.
- An entry or `platform` that is not a JSON object still makes the lookup *failed* with `unexpected error`, as before
  in both clients ([0029](0029-registry-calls-answer-every-error-but-the-callers-cancellation-as-failed.md)).
- A caller that needs a Windows image's configuration cannot ask for it until the interface gets a platform.