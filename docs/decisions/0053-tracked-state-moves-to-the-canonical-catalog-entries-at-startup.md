# 0053: Tracked state moves to the canonical catalog entries at startup

- **Status:** Proposed
- **Date:** 2026-11-21
- **Area:** Image catalog
- **Source:** Issue #275
- **Supersedes:** —

## Context

The image reference parser treated the Docker Hub host aliases `index.docker.io` and `registry-1.docker.io` as separate
registries. It also added the `library/` namespace only to a reference of one segment. As a result `nginx`,
`docker.io/nginx` and `index.docker.io/library/nginx` became three catalog entries, and a mirror-wrapped reference
behind an alias (`index.docker.io/ghcr.io/owner/app`) was never unwrapped. The catalog compares registry and
repository exactly and has one write path ([Image catalog](../areas/image-catalog.md#the-catalog)). The fix therefore
normalizes every Docker Hub spelling in the parser to Docker's own canonical form: `docker.io`, plus `library/` for a
repository without a namespace. `registry-1.docker.io`, the host that registry requests go to, is treated as an alias
too.

Installations that already ran such references hold entries that are no longer canonical. Containers re-parse on every
runtime scan, and base images re-parse on the next observed-image scan. Two kinds of rows never re-parse on their own:

- the current version of a manually registered observed image, which never moves on its own;
- a container's manual tag selection, which applies only while it points at the registry repository of the container's
  latest snapshot.

The catalog's unique indexes on image versions, relationships and active vulnerability findings collide when two
entries are merged.

## Options considered

1. **Move only the tracked state, at startup, in C#.** Each observed image and each tag selection whose stored
   registry and repository can change under the new rules moves to the canonical entry. The entry is got or created
   through the catalog's write path using the parser's rules. History stays where it is and ages out. Idempotent, no
   schema change, testable on SQLite. Chosen.
2. **Merge the duplicates completely.** Re-point snapshots, relationships, update and vulnerability findings, tag
   candidates and selections, then delete the old entries. This collides with several unique indexes, rewrites
   history, and breaks the rule that catalog entries are never deleted.
3. **Leave existing data alone.** New scans write canonical entries, but manual observed images stay on the old entry
   for good, and tag selections silently stop applying.
4. **A data migration in SQL (`Update8`).** It runs once under the migration lock, but it repeats the parser's rules
   in SQL, cannot get-or-create through the catalog, and cannot be tested with the test databases.
5. **Normalize where entries are compared instead of in the parser.** Every reader (catalog lookups, own-image keys,
   shared base images, Docker Scout, Trivy) would need the same rule. The catalog's exact comparison, which writers
   rely on, would no longer hold.
6. **When a moved observed image lands on a version another observed image already tracks, skip it or merge it.**
   Skipping leaves it non-canonical for good. Merging deletes an observed image, together with its scan runs and
   findings, which no other path does (#282).
7. **Carry the container version tags over across the old and the canonical image version.** This avoids a one-time
   loss, but adds a canonical-equivalence lookup to the runtime scan's carry-over, on every scan, for a transition that
   happens once.

## Decision

The parser returns the canonical form for every Docker Hub spelling. The startup sequence moves to the canonical entry
the current version of every observed image, and every tag selection, that is still on an entry the new rules change.
It skips a row whose canonical registry or repository fails the reference rules. Nothing else is rewritten, no entry
and no observed image is deleted, and an observed image that lands on a version another one already tracks is moved
anyway. The rules are in [Image catalog](../areas/image-catalog.md#image-references) and
[Hosting](../areas/hosting.md#startup-sequence).

## Consequences

- The step runs on every start and does nothing once all tracked state is canonical. It reads only rows that the new
  rules can change: the two aliases, and `docker.io` repositories without a namespace.
- The move can end on another registry: a stored `index.docker.io` / `ghcr.io/owner/app` row moves to `ghcr.io`.
- A row whose canonical registry or repository fails the reference rules (too long, outside the grammar) stays as it
  is. A single row of that kind therefore never fails startup, and the step stores no reference that the regular paths
  would reject.
- Two observed images can end up on one version: two manual ones, or a manual one on the version of an enabled
  discovered image. Both stay, like the duplicates that earlier versions created
  ([0048](0048-manual-registration-identifies-an-observed-image-by-its-image-version.md)) until #282 lets users delete
  them. A new registration of that reference follows the usual rules.
- Until the next scans, a moved observed image has no base images and, on a newly created version, no vulnerability
  assessment. A moved tag selection applies from the container's next runtime scan on. Update findings and scan runs
  stay with the observed image.
- A container whose spelling is not canonical gets a new image version on its first scan after the update. Its
  carried-over resolved and available update versions start empty and come back with the next evaluation that
  resolves them. A container whose running digest is no longer in the tag list keeps none until it runs another image.
  This one-time loss is accepted (option 7).
- Old entries keep their snapshots and findings. They leave the live image versions with the next scans, and the
  vulnerability cleanup and retention then remove what depends on them.
- The step saves through the catalog's get-or-create as it goes. A run that is interrupted is completed by the next
  start.
- A database failure in the step fails startup, like the other startup steps.
