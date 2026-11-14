# 0031: Docker Scout assesses a digest only while its tag still points to it

- **Status:** Accepted
- **Date:** 2026-11-14
- **Area:** Vulnerabilities
- **Source:** Issue #267
- **Supersedes:** —

## Context

The Docker Scout provider reads advisories from `/v1/repositories/<namespace>/<repository>/tags/<tag>/vulnerabilities`,
an endpoint Docker does not document, and ignores the digest of the image version it is asked about. After a tag was
republished, an image version still running the old digest was assessed with the advisories of the new image: findings
were created or resolved for an image that does not run that content. No digest variant of the endpoint is documented
either, and the session that planned the fix has no Docker Hub account to probe one. The digest a stored image version
carries is usually the index digest Docker Hub's tag record reports (runtime containers pulled by tag, observed images,
account discovery), but it can be the digest of one platform image (a reference pinned to it, possibly a base-image
label). The provider is a singleton; the registry metadata service that reads a tag record is scoped.

## Options considered

1. **Query a digest endpoint of Docker Scout** — would describe exactly the stored digest, but no such endpoint is
   documented and none could be confirmed; building on a guessed path fails at run time in a way no unit test shows.
2. **Before the tag lookup, compare the image version's digest with the tag's current digests from Docker Hub's tag
   record, in the provider, through the registry metadata service resolved from a scope of its own (chosen)** — every
   Docker Scout rule (credentials, `docker.io` only, path check, digest check) stays in one place and in one order, so
   an unsupported registry or missing credentials never cost a registry request and never turn into another outcome;
   the tag lookup reuses the Docker Hub client with its token cache, retry and request limiter. The cost is a scope per
   lookup and a provider that depends on the registry layer.
3. **The same comparison in the vulnerability enrichment service, before it calls the provider** — keeps the provider
   free of registry dependencies, as the issue suggested, but the service would have to repeat Docker Scout's
   preconditions to avoid a registry request for an image Docker Scout rejects anyway, or else turn *unsupported* into
   *failed* or *unavailable* when that request fails or differs; it also needs a provider capability flag and a new
   dependency of a service with many test call sites.
4. **The comparison after a successful Docker Scout answer** — needs no precondition knowledge, but reads and then
   discards the advisories of the wrong image, and the issue asks that they are not read.
5. **Compare with the top-level (index) digest only** — no parser change, but an image version pinned to a platform
   image of the current tag would be shown *unavailable* although the tag still points to it.

## Decision

Option 2: when the image reference has a digest, the Docker Scout provider asks the registry metadata service for the
tag first and reads Docker Scout only when the digest equals the tag's digest or one of its platform image digests;
otherwise the assessment is *unavailable*, and a failed tag lookup is *failed*. The rules, messages and the extra Docker
Hub request are in [Vulnerabilities](../areas/vulnerabilities.md#docker-scout).

## Consequences

- An image version is never assessed with the advisories of a different image release; one whose tag moved on stays
  *unavailable* with its findings untouched, as Trivy does for a digest that is gone.
- Each Docker Scout lookup of an image version with a digest costs one Docker Hub web API request, which takes a
  Docker Hub request slot ([0030](0030-docker-hub-request-parallelism-is-limited-process-wide-and-follows-reloads.md))
  and counts against Docker Hub's rate limit; its time limit is `DockerHub:RequestTimeoutSeconds`, not the Docker
  Scout timeout.
- A platform digest of the current tag counts as a match, although Docker Scout's tag answer may describe another
  platform of that release; this imprecision already holds for the index digest.
- A tag republished between the check and the Docker Scout request is not detected; the window is one lookup long.
- The tag record's platform digests are read only for this check and never stored, so the stored tag metadata keeps
  its size.
- Revisit with option 1 once Docker documents a digest lookup for Docker Scout.