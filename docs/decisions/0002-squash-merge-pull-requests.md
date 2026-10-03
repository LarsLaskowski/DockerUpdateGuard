# 0002: Squash-merge pull requests

- **Status:** Accepted
- **Date:** 2026-10-03
- **Source:** Squad adopted from Squad-Spec-Repository-Template
- **Supersedes:** —

## Context

The squad commits and pushes after every pipeline step to secure its work, and the internal process (plan
revisions, review rounds) must not appear in the history of `main`. Rewriting a pushed branch to tidy it
up would need a force-push, which requires explicit approval.

## Options considered

1. **Rebase merges, commit only at curated milestones** — linear, granular history on `main`; work
   between milestones is not secured, and review fixes have to be folded into earlier commits.
2. **Squash and merge** — one commit per PR on `main`, built from the PR title and description; branch
   commits can be frequent and unpolished; per-commit granularity inside a PR is lost on `main`.

## Decision

Option 2. Pull requests are merged with *Squash and merge*. The PR title (`[area] Description`) becomes
the commit subject on `main`, the description its body. Branches are kept current by merging `main` into
them instead of rebasing.

## Consequences

- The PR title and description are the permanent record of a change and are written for that purpose.
- Branch commits may name pipeline steps; they never reach `main`.
- The repository settings allow only *Squash and merge* (or at least default to it).
