# 0048: Manual registration identifies an observed image by its image version

- **Status:** Proposed
- **Date:** 2026-11-17
- **Area:** Image catalog
- **Source:** Issue #284
- **Supersedes:** —

## Context

Manual registration on `/observed-images` decided between creating and updating an observed image by comparing the
entered name — untrimmed and case-sensitive — with the stored names, which are trimmed and not unique. Near-identical
names created separate images, two images with one name made every later registration of that name fail with
`unexpected error`, the same image registered under two names was tracked twice, and a name equal to a discovered
(own) image's name took that image over: its current version moved to the manual reference, and the next discovery run
disabled it and created a second own image for the repository. Names are display labels; discovered images are owned
by the account discovery ([Registries](../areas/registries.md)), which rewrites them on each run.

## Options considered

1. **Identify a manual image by its catalog image version; never write a discovered image, and reject a reference
   that is the current version of an enabled discovered image; enforce it in the service** — one image per reference,
   names free, no migration; a rule in code instead of the database, so two registrations of one reference at the
   same instant can still both create. Chosen.
2. **Keep the name as identity, normalized (trimmed, case-insensitive) and limited to manual images** — fixes the
   crash and the takeover, but one reference under two names is still tracked twice, and a rename creates a new image.
3. **A unique index on the current image version of manual images** — the database would hold the rule, but needs a
   migration that fails on the duplicates earlier versions created, and discovered images move their current version
   on every run, so a filtered index has to follow a column another writer changes.
4. **Create a manual image beside a discovered one of the same reference** — never rejects, but tracks one image
   twice under two sources, which the rule against duplicates is meant to end.

## Decision

Manual registration matches an existing manual observed image by the image version its normalized reference resolves
to (the oldest when several exist) and updates it, creates one otherwise, and rejects a reference that is the current
version of an enabled discovered image without saving anything. The rules are in
[Image catalog](../areas/image-catalog.md#observed-images).

## Consequences

- The name is a label: duplicates are allowed and a re-registration renames the image.
- Duplicates created by earlier versions stay until the user deletes them (#282); registration no longer fails on
  them.
- Without a database constraint, two simultaneous registrations of one reference can create two manual images; the
  form allows one registration at a time per page.
- A reference that differs from a discovered image's current version only in its digest (or its tag) is a different
  image version and registers as a manual image; Docker Hub spellings that the parser does not unify (#275) do too.
- Revisiting this means a migration with a unique rule once observed images have one identity across sources.
