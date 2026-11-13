# 0024: Registry listings stop with *failed* after a page cap

- **Status:** Proposed
- **Date:** 2026-11-13
- **Area:** Registries
- **Source:** Issue #301
- **Supersedes:** —

## Context

The Docker Hub repository listing (account discovery), the Docker Hub tag listing and the OCI tag listing follow
same-origin next-page links ([0004](0004-pagination-follows-only-same-origin-links.md)) with no limit on the number of
pages. A registry, mirror or proxy that keeps answering with a next link — the same one again, or an endless sequence —
keeps a scan or a discovery run reading until it is cancelled. The tag bounds of a scan do not stop it: the maximum
count counts only kept (Docker Hub) or inspected (OCI) tags, so pages whose tags fail the filters, or are empty, never
reach it. Docker Scout already stops after 50 pages and returns what it read as a success. The callers treat a listing
that does not succeed as "no answer": discovery keeps the state of the account or the repository, a runtime scan marks
the container's update assessment failed, the base-image resolution keeps the image's own tag.

## Options considered

1. **A fixed page cap per listing; a listing that would read past it ends *failed*, without the entries read** — the
   same outcome as a rejected next-page link, so every caller already handles it, and no truncated list is ever
   taken as complete. A repository with more entries than the cap reaches, whose listing has no earlier stop, fails
   where it used to succeed slowly. Chosen.
2. **The cap with the entries read so far as *succeeded*, as Docker Scout does** — keeps such huge listings working,
   but a truncated account listing makes discovery disable the images beyond the cap, an OCI listing (lexically
   ordered) loses an arbitrary part of its tags, a repeated link fills the result with copies of one page, and a
   truncated tag list hides updates silently — the reason 0004 rejected partial results for a rejected link.
3. **Stop only when a next link repeats** — ends the loop on one page, not an endless sequence of distinct links
   (`page=2`, `page=3`, …).
4. **A time budget per listing** — bounds the duration, not the requests; a fast server still gets an unbounded
   number of them within it, and the count of pages needs no clock.
5. **A configurable cap** — a new key with validation and documentation without a known operator need; the constant
   can become one when a real repository needs it.

## Decision

Each of the three listings reads at most a fixed number of pages; when the last allowed page still links to a next
page on the listing's origin, the listing ends *failed* with a fixed message and a warning, and sends no further request.
The cap, the message and the order against the origin check are in
[Registries](../areas/registries.md#pagination-links).

## Consequences

- A registry can no longer keep a scan or a discovery run reading: every listing sends a bounded number of requests.
- A listing that needs more pages than the cap (a repository with more tags than the cap holds whose scan has no
  earlier stop, or account discovery, which reads every tag of each own repository) is *failed*: the container's update
  assessment reports it, discovery skips the repository and keeps its observed image as it was.
- Docker Scout keeps its own cap of 50 pages with a partial success; aligning it is a separate decision.
- Revisit when a real repository is found that legitimately needs more pages, by raising the cap or making it
  configurable (option 5).
