# 0053: Tracked state moves to the canonical Docker Hub catalog entries at startup

- **Status:** Proposed
- **Date:** 2026-11-21
- **Area:** Image catalog
- **Source:** Issue #275
- **Supersedes:** —

## Context

The image reference parser kept the Docker Hub host aliases `index.docker.io` and `registry-1.docker.io` as registries
of their own and added the `library/` namespace only to a reference of one segment, so `nginx`, `docker.io/nginx` and
`index.docker.io/library/nginx` became three catalog entries. The catalog compares registry and repository exactly and
has one write path ([Image catalog](../areas/image-catalog.md#the-catalog)), so the fix normalizes every Docker Hub
spelling in the parser to Docker's own canonical form (`docker.io`, `library/` for a repository without a namespace;
`registry-1.docker.io`, the host the registry requests go to, is treated as an alias as well).

Installations that already ran such references hold duplicate entries. Most rows re-parse by themselves: containers on
every runtime scan, base images on the next observed-image scan. Two rows never do: the current version of a manually
registered observed image (it never moves on its own) and a container's manual tag selection, which applies only while
it points at the registry repository of the container's latest snapshot, and so would silently stop applying once the
next scan stores the container under the canonical entry. The catalog's unique indexes (image versions, relationships,
active vulnerability findings) collide when two entries are merged.

## Options considered

1. **Move only the tracked state, at startup, in C#** — the current version of each observed image and each tag
   selection whose registry is a Docker Hub host move to the canonical entry, got or created through the catalog's
   write path with the parser's rules; history stays where it is and ages out. Idempotent, no schema change, testable
   on SQLite. Chosen.
2. **Merge the duplicates completely** — re-point snapshots, relationships, update and vulnerability findings, tag
   candidates and selections, then delete the old entries. Collides with several unique indexes, rewrites history, and
   breaks the rule that catalog entries are never deleted.
3. **Leave existing data alone** — new scans write canonical entries, but manual observed images stay on the old
   entry for good and tag selections stop applying without notice.
4. **A data migration in SQL (`Update8`)** — runs once under the migration lock, but repeats the parser's rules in SQL,
   cannot get-or-create through the catalog, and is not testable with the test databases.
5. **Normalize where entries are compared instead of in the parser** — every reader (catalog lookups, own-image keys,
   shared base images, Docker Scout, Trivy) would need the same rule, and the catalog's exact comparison, which writers
   rely on, would no longer hold.

## Decision

The parser returns the canonical Docker Hub form for every Docker Hub spelling, and the startup sequence moves the
current version of every observed image and every tag selection still on a non-canonical Docker Hub entry to the
canonical one; nothing else is rewritten and no entry is deleted. The rules are in
[Image catalog](../areas/image-catalog.md#image-references) and [Hosting](../areas/hosting.md#startup-sequence).

## Consequences

- The step runs on every start and does nothing once all tracked state is canonical; it touches only rows whose
  registry is a Docker Hub host, so other registries are never rewritten.
- A moved observed image has no base images until its next scan, because base-image relationships belong to the image
  version; its update findings and scan runs stay with it.
- Old entries keep their snapshots and findings; they leave the live image versions with the next scans, and the
  vulnerability cleanup and retention remove what depends on that.
- A tracked row whose canonical repository would exceed the stored length is left as it is.
- A database failure in the step fails startup like the other startup steps.
