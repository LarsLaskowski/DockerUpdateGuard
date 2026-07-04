---
name: publish-pr
description: Creates a branch, commits the current changes, pushes the branch, opens a pull request, and switches back to main. Use this when asked to publish local changes as a pull request.
---

Use this skill when the user wants the current local changes published to GitHub as a pull request.

Repository: `LarsLaskowski/DockerUpdateGuard`.

All user-facing output you create — branch name, commit message, PR title and body — must be written in **English**, regardless of the language the user wrote in. Never mention Claude, Anthropic, or any other AI/assistant tooling in the PR title or body, and do not add any `Co-Authored-By` trailer, "Generated with" footer, session link, or other note attributing the work to an AI (see the "Pull requests" section in `CLAUDE.md`).

Follow this workflow:

1. Inspect the repository state first with non-interactive Git commands:
   - confirm the current branch
   - review `git status --short --branch`
   - confirm the `origin` remote exists
2. If there are no relevant local changes to publish, stop and say so plainly.
3. Validate the changes before publishing:
   - Run `reihitsu-format ./` if any source files changed.
   - Run `dotnet build DockerUpdateGuard.slnx -c Release --no-restore` (restore first if needed).
   - Run the tests for any affected project(s) under `src\Tests`.
   - If validation fails, fix the cause before continuing — do not publish broken code. If you cannot make it pass, stop and report clearly.
4. Choose or confirm a branch name based on the change. If the user already provided one, use it. Otherwise derive a short kebab-case branch name (e.g. `fix/…`, `feat/…`, `chore/…`) from the work.
5. Create and switch to the branch from the current base branch.
6. Stage only the files relevant to this change. Do not include unrelated changes.
7. Create a non-interactive Git commit following `CLAUDE.md`: one-line summary under 80 characters, no trailing period, not first person, and a body of 3–5 sentences depending on the number of changes.
8. Push the branch to `origin` and set upstream tracking (`git push -u origin <branch>`).
9. Create a pull request with `mcp__github__create_pull_request`:
   - `owner: LarsLaskowski`, `repo: DockerUpdateGuard`
   - `base`: `main`, unless the user explicitly requests a different base
   - `head`: the branch created in step 5
   - `title`: concise English summary of the change
   - `body`: short English summary of what changed. If a PR template exists in the repository, structure the body to match it.
   - Do not add any attribution, "Generated with" footer, session link, or other note referencing an AI/assistant in the PR title or body.
10. After the pull request is created, switch back to the `main` branch.
11. Report the branch name and pull request URL clearly.

Additional rules:

- Prefer non-interactive commands only.
- Do not amend existing commits unless the user explicitly asks.
- Do not include unrelated modified files in the commit.
- If push or PR creation fails, stop and report the failure clearly instead of continuing as if it succeeded.
- If switching back to `main` would discard or conflict with uncommitted work, stop and explain the blocker.
