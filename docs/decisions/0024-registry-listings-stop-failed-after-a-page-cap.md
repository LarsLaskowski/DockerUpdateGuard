# 0024: Registry listings stop with *failed* at a repeated link or after a page cap

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

Real listings are long. Measured on 2026-11-13 (plan challenge of #301): the anonymous `tags/list` walk of
`ghcr.io/renovatebot/renovate` with `n=100` followed 270 pages (26,988 tags), and Docker Hub reports a `count` of
46,986 tags (470 pages of 100) for `renovate/renovate`. A scan with a pinned version tag reads an OCI listing by name
until 250 tags qualify, and a Docker Hub listing without the current tag's publication time, or account discovery,
reads every page, so a cap near these sizes would fail such images on every scan.

## Options considered

1. **Stop *failed* when a next link names a page the listing already requested, and after a fixed page cap well above
   the largest measured listing (1,000 pages of 100)** — a repeating link ends after one extra page, an endless
   sequence of distinct links after the cap; no measured repository reaches it. Chosen.
2. **The cap alone, at 100 pages** — the first proposal; breaks the measured repositories above.
3. **The cap alone, at 1,000 pages** — bounds everything, but a link that repeats costs a thousand requests on every
   scan before it fails, against the Docker Hub rate limit.
4. **The cap with the entries read so far as *succeeded*, as Docker Scout does** — a truncated account listing makes
   discovery disable the images beyond the cut, an OCI listing (ordered by name) loses an arbitrary part of its tags,
   and a truncated tag list hides updates silently — the reason 0004 rejected partial results.
5. **Repeated-link detection alone** — does not stop an endless sequence of distinct links (`page=2`, `page=3`, …).
6. **A time budget per listing** — bounds the duration, not the requests; a fast server still gets an unbounded
   number of them within it.
7. **A configurable cap** — a new key with validation and documentation without a known operator need.
8. **A cap counted in entries** — a server can send empty pages; pages are what bound the requests.

## Decision

Each of the three listings ends *failed*, with a fixed message and a warning and without the entries read, when a
same-origin next link names a page the listing already requested, or when the last of a fixed number of pages still
links to a next page; no further request is sent. The cap, the messages and their order after the origin check are in
[Registries](../areas/registries.md#pagination-links).

## Consequences

- A registry can no longer keep a scan or a discovery run reading: every listing sends a bounded number of requests.
- A listing bounded in requests is not bounded in duration: a slow server can hold one for up to the page cap times
  the request timeout. Nor is it bounded in memory beyond the entries and the request URIs of the pages read.
- A listing that needs more than 1,000 pages is *failed*: the container's update assessment reports it, discovery
  skips the repository and keeps its observed image as it was.
- A server that varies a link only in a parameter it ignores is not caught as repeating; the cap still ends it.
- Docker Scout keeps its own cap of 50 pages with a partial success; aligning it is a separate decision.
- Revisit when a real repository needs more than the cap, by raising it or making it configurable (option 7).
