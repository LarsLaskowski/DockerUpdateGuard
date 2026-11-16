# 0047: The Docker Scout listing stops *failed* at a repeated link or after the shared page cap

- **Status:** Accepted
- **Date:** 2026-11-16
- **Area:** Vulnerabilities
- **Source:** Issue #436
- **Supersedes:** — (amends [0024](0024-registry-listings-stop-failed-after-a-page-cap.md): Docker Scout no longer
  keeps its own cap of 50 pages with a partial success, as that record's consequences left it)

## Context

The Docker Scout vulnerability listing follows same-origin `next` links
([0004](0004-pagination-follows-only-same-origin-links.md)) and, after 50 pages that still link onward, returns the
advisories read so far as *succeeded* with a warning. The vulnerability refresh treats a succeeded list as complete:
every active finding of the image version that the list does not contain is resolved. A cut list therefore resolves
findings that still exist — the reason 0004 rejected partial results, and the reason
[0024](0024-registry-listings-stop-failed-after-a-page-cap.md) made the Docker Hub and OCI listings end *failed*
instead, leaving Docker Scout to a separate decision. A *failed* assessment leaves the stored findings untouched.

Docker Scout's page size is not documented and its real `next` link was never observed (no credentials in the
session, 0004), so how many advisories 50 pages hold is unknown. A cap that a heavily affected image could reach
would turn its assessment from *succeeded* into *failed* on every refresh.

## Options considered

1. **The rule of 0024: *failed* at a next link that repeats a page already requested, and after
   `PaginationLinkGuard.MaxListingPages` (1,000) pages, with the shared messages** — one rule for every listing; a
   repeating link ends after one extra page, an endless sequence of distinct links after the cap, which no real
   advisory list can reach whatever the page size. Chosen.
2. **Keep the cap of 50 pages and only end *failed* there** — the smallest change, but with an unknown page size an
   image with many advisories could fail on every refresh where it succeeds today, and a repeating link still costs
   50 requests.
3. **Keep the partial success (no change)** — a cut list resolves findings that still exist and shows the image as
   assessed: the defect of the issue.
4. **Return the partial list as *partial*, keeping the findings it lacks** — `ExternalOperationResult` has no
   partial status with data, and the refresh would need a second path that adds findings without resolving any; the
   new findings of a cut list are not worth that against a listing no real image reaches.

## Decision

The Docker Scout vulnerability listing applies the repeated-link check and the page cap of the registry listings
(0024) after its origin check, and ends *failed* with their messages and without the advisories read; the 50-page cap
with its partial success is removed. The behavior is in
[Vulnerabilities](../areas/vulnerabilities.md#docker-scout) and [Registries](../areas/registries.md#pagination-links).

## Consequences

- A cut advisory list can no longer resolve findings: a listing that does not end keeps the stored findings and
  shows the assessment *failed*.
- An endless sequence of distinct links costs up to 1,000 Docker Scout requests per fetch instead of 50 (twice after a
  401 that restarts the fetch). These requests take no Docker Hub request slot.
- As in 0024, the cap bounds requests, not duration or memory: a slow Docker Scout origin can hold one fetch for up
  to 1,000 times `RequestTimeoutSeconds` (twice after a 401), and the advisories of every page read are kept until
  the listing ends.
- Residual risk: should Docker Scout's real last page carry a `next` link to itself, every listing would now end
  *failed* (event 3407) instead of reading 50 copies of that page; the link form is still unobserved.
- Revisit together with 0024 when a real listing needs more pages than the cap.
