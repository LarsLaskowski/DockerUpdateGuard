# 0017: Product PRs may change the squad settings file

- **Status:** Accepted
- **Date:** 2026-11-01
- **Area:** —
- **Source:** Squad refreshed from Squad-Spec-Repository-Template
- **Supersedes:** [0003](0003-squad-working-records-off-main.md) (amends its list of squad files a product PR never changes: `.squad/tools/squad_settings.py` is no longer one of them; 0003 is not edited)

## Context

Record 0003 lets an issue or feature PR change no squad file except `.squad/stack.md`, `.squad/project.md` and the
project blocks of `AGENTS.md` and its Copilot mirror. `.squad/tools/squad_settings.py` holds this repository's own
settings for the squad tools: the coverage thresholds, the paths the gates read and `COVERAGE_PATHSPECS`. When a
product change adds a file no unit test can cover, *Code a unit test cannot cover* in `.squad/routing.md` has the
same pull request exclude it from the coverage measure in that file, which 0003 forbade.

## Options considered

1. **Keep the file a squad file**: the exclusion goes to a separate squad-maintenance pull request, and the product
   pull request fails its *Coverage gate* until that one is merged.
2. **Treat the file like `stack.md` and `project.md`**: project knowledge a product pull request may change, while
   the scripts that import it stay template-managed. `scope-check.py` allows it (a fork pull request still may not).

## Decision

Option 2: an issue or feature PR may change `.squad/tools/squad_settings.py` as well; every other rule of 0003 stands.

## Consequences

- A change that adds code no unit test can cover sets its coverage exclusion in the same pull request, together
  with the decision the Lead records for it.
- A change to the settings file is reviewed like the rest of the diff; the scripts under `.squad/tools/` stay
  template-managed and are never edited in a product pull request.
