# 0054: A base image is shared from two users among enabled observed images and their containers

- **Status:** Proposed
- **Date:** 2026-11-21
- **Area:** Base images
- **Source:** Issue #286
- **Supersedes:** —

## Context

The shared base images view, its dashboard tile and its navigation entry are meant to show where a base-image update
or vulnerability reaches several tracked things. They listed every base recorded for the current version of any
observed image: a base used by one image, a base counted for a disabled observed image, and — on the page — images and
containers taken from relationships of image versions an observed image has since moved away from (relationships
belong to the image version and stay when the observed image moves on). The Product Manager decided in the issue that
"shared" requires at least two users and that disabled images do not count.

Users are of two kinds. Observed images are known to the data layer; which containers are current (the latest runtime
scan of each Docker instance) is decided in the host's view service, which three callers use: the page, the dashboard
and the navigation.

## Options considered

1. **Count observed images only, at least two** — one query in the data layer decides it; but one image running in
   several containers is a base whose update reaches several things, and the Product Manager's definition counts it.
2. **Count enabled observed images plus the current containers that run their current version, at least two** — the
   Product Manager's definition. Chosen.
3. **List every base and mark the shared ones** — keeps the single-user entries the issue calls noise.

Where the rule is applied:

4. **In the data query** — it would need the current-container selection, which lives in the host; a second copy of it
   in the data layer could drift from the one every other page uses.
5. **The data query filters enabled images and returns, per entry, the current image versions of its consuming
   images; the host counts their current containers and applies the threshold in one helper the page, the dashboard
   and the navigation share** — chosen.
6. **The page keeps loading the base-image relationships, restricted to consuming child versions** (the issue's
   suggestion) — it re-applies a source-reference filter that differs from the query's grouping for a base without
   digest and without source reference, so the listed images could again disagree with the count; the query result
   already names the consumers of each entry.

## Decision

A base entry is shown as shared when its enabled observed images (by their current version) plus the current runtime
containers running one of those versions number at least two. The data query reports the consumers per entry without
a threshold; one host helper applies it for the page, the dashboard tile and the navigation entry. The behavior is
stated in [Base images](../areas/base-images.md#shared-base-images).

## Consequences

- The page, the tile and the navigation cannot disagree about which bases are shared.
- The navigation check now also reads the current container snapshots, once per session start.
- The base image chain of a detail page and the inventory gauge of deduplicated base images still cover every
  recorded base, shared or not.
- Bases recorded under different spellings of one Docker Hub repository count as one entry only through the
  canonical references of issue #275.
