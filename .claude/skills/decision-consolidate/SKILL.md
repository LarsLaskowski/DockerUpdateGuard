---
name: decision-consolidate
description: Use when the user wants to tidy up the decision records in docs/decisions/ - merge Superseded chains and records on the same topic into one record, delete the obsolete ones, lift behavior that sits in records into the area documents in docs/areas/, fix links and the index, and open a pull request. Only touches records that no release tag contains.
---

# Consolidate decision records

Unreleased decision records are edited in place (`docs/decisions/README.md`), so a `Superseded by` chain or
several records on one topic that never shipped can be folded into one. Released records are never touched.

All output you create — branch name, commit message, PR title and body — is written in **English**.

## Steps

1. **Branch.** Work on a new branch off `main`, never on `main`.
2. **Find the released set.** `git tag --list 'v*'`; for each record, the commit that added it is
   `git log --diff-filter=A --format=%H -- docs/decisions/<file>` and it is released if
   `git tag --contains <commit> --list 'v*'` is not empty. Run `python3 .squad/tools/decision-check.py` first
   and note what it already reports. Released records are left exactly as they are.
3. **Group the unreleased records** by topic: every `Superseded` chain (the newest record is the survivor), and
   records that describe the same decision area (same guarantee, same component, a follow-up that only extends
   an earlier one). Also list records that no longer apply to the code. Show the user the proposed groups
   (record numbers, survivor, what is deleted) and wait for approval before editing.
4. **Lift behavior into the area documents.** For each group, find what the *Decision* section states as
   behavior (values, formats, limits, error texts, exit codes) rather than as a choice. Write it, language-neutral
   and normative, into the area document of the group (`docs/areas/<slug>.md` from `_template.md`, listed in
   `docs/areas/README.md`; a new area is the user's decision, name and scope go into the index) and shorten the record
   to the choice in one to three sentences with a link to the area document. Give every record an `Area:`. Drop
   class, package and function names as a contract; keep them only as a pointer to where it is implemented.
5. **Merge each group** into its survivor, in place: Context, Options considered, Decision and Consequences
   describe the current state; the rejected options of the folded records stay under *Options considered*
   (that is where the "why not" belongs). Keep the survivor's number and set *Status* to `Accepted`
   (`Proposed` stays). Drop *Supersedes* unless it names a released record.
6. **Delete** the folded and obsolete records. Numbers are never reused; gaps are fine.
7. **Fix every link** to a deleted record: the index in `docs/decisions/README.md`, `docs/ARCHITECTURE.md`,
   the guarantees in `.squad/project.md`, other records, code comments, issue and PR templates. Search with
   `grep -rn "decisions/NNNN\|decision NNNN"` for each deleted number and point the link at the survivor.
8. **Verify.** `python3 .squad/tools/decision-check.py` and `python3 .squad/tools/config-check.py` pass, and
   no link to a deleted record is left.
9. **Pull request.** Open it only when the user asks (use `create-pr`). Title `[Docs] Consolidate decision
   records`; the description lists the groups (survivor ← folded records) and the count before and after.
