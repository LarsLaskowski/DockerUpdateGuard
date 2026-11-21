# 0055: Entity enum members nothing produces are removed, and their numbers are never reused

- **Status:** Proposed
- **Date:** 2026-11-21
- **Area:** —
- **Source:** Issue #293
- **Supersedes:** —

## Context

The entities store their enums as integer columns without a value list or check constraint
([`ARCHITECTURE.md`](../ARCHITECTURE.md), *Entity model*). Four of them had members no code ever writes: an image
version source (registry metadata), a relationship type (build stage), an update finding type (base image update) and
two vulnerability sources (registry advisory, manual). They showed up as UI labels, test fixtures and area-document
statements about behavior that cannot happen. Because no writer produced them, a database should hold none of those
numbers; one could still exist after a manual edit.

## Options considered

1. **Remove the members, keep every other member's number, and never give a removed number to a new member; no
   migration** — the model and the schema stay as they are (the columns are plain integers), and a row with a removed
   number still loads as an undefined enum value, which the pages show with their fallback label. Chosen.
2. **Also add a data migration that rewrites removed numbers to `NotSet`** — cleans rows that should not exist, at the
   price of a migration whose only effect is on rows no code wrote; a removed number would still be retired.
3. **Renumber the remaining members contiguously** — tidier enums, but every stored row would change meaning without
   a migration that rewrites all of them.
4. **Keep the members and mark them `[Obsolete]`** — no stored value can become undefined, but the UI labels, the test
   fixtures and the area documents keep describing features that do not exist.

## Decision

Enum members of entities that nothing produces are deleted without a migration; the remaining members keep their
numbers, and a deleted member's number stays retired, as each enum's documentation and the stored-number tests in
`MappingTests` state.

## Consequences

- A row with a retired number loads and is shown with the fallback label of its page (the number for a finding type,
  "Not set" for a vulnerability source); the behavior is in the area documents of the enums
  ([Update detection](../areas/update-detection.md), [Vulnerabilities](../areas/vulnerabilities.md)).
- A future member gets the next unused number, never a retired one; the stored-number tests fail on a renumbering.
- Revisiting this means a migration that rewrites stored values, which option 2 describes.
