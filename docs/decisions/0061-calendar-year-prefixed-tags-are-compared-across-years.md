# 0061: Calendar year-prefixed tags are compared across years

- **Status:** Proposed
- **Date:** 2026-11-25
- **Area:** Update detection
- **Source:** Issue #82
- **Supersedes:** —

## Context

Year-prefixed tags (`<four digits>-<suffix>`) were comparable only within the same year, so no tag of a later year
was ever a candidate. For product-year tags such as `2019-CU32-ubuntu-20.04` the year names a product line (SQL
Server 2019 against 2022), and a move to another year is a major upgrade that should not be recommended
automatically. For date-stamped tags such as `2024-12-15` the year is part of a release date, and the same rule
answered every release after a year change with *up to date* ("No newer year-based version was found"), the
issue's signal downgrade (the issue expected *needs review*; the code returned *up to date*). The runtime container
page applied the same year rule when it filtered the available tag candidates.

## Options considered

1. **Keep the same-year rule** — no behavior change; date-stamped tags stay wrongly *up to date* after every year
   change.
2. **Any later year of the same year-variant family is a candidate** (the issue's suggestion) — fixes date-stamped
   tags, but recommends product-line jumps such as 2019 to 2022 automatically, unlike the major-upgrade policy for
   semantic tags.
3. **Only calendar tags cross years** — a year-prefixed tag whose suffix contains no letter compares across years
   with another such tag; every other year-prefixed tag keeps the same-year rule.
4. **Same-year rule, later years named in the details** — safe, but a routine date-stamped release is still never
   an *update available*.

## Decision

Option 3, chosen by the Product Manager: two year-prefixed tags of different years are comparable only when neither
suffix contains a letter; the update evaluation and the runtime container page's candidate filter apply this one rule
through a shared helper. The rules are in [Update detection](../areas/update-detection.md#variant-families-and-ordering).

## Consequences

- Date-stamped tags such as `2024-12-15` get *update available* for `2025-01-05`; product-year tags are unchanged.
- A date-stamped tag with a lettered variant (`2024-12-15-alpine`) counts as a product-year tag and still stays
  within its year; widening the rule to such variants would revisit this decision.
- A tag whose suffix has no letter but is no date (`2019-1`) is treated as a calendar tag as well.