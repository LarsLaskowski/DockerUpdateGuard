---
name: fix-issue
description: Takes a GitHub issue number, fixes the issue in the codebase, creates a branch, opens a pull request that closes the issue, and switches back to main. Use this whenever the user wants an issue resolved end-to-end, e.g. "fix issue 42", "work on #42", or passes a bare issue number to be handled.
---

Use this skill when the user gives you a GitHub issue number (e.g. "fix issue 42", "#42", or just "42") and wants it resolved end-to-end: understand the issue, implement the fix, and publish it as a pull request.

Repository: `LarsLaskowski/DockerUpdateGuard`.

All user-facing output you create — branch name, commit message, PR title and body, and any code comments or XML docs — must be written in **English**, regardless of the language the user wrote in. Never mention Codex, Anthropic, or any AI/assistant tooling in the commit message or PR, and do not add any `Co-Authored-By` trailer, "Generated with" footer, session link, or other note attributing the work to an AI (see the "Pull requests" section in `AGENTS.md`).

## Workflow

### 1. Read and understand the issue

- Confirm the issue number from the user's request. If no number was given, stop and ask for one.
- Fetch the issue with the GitHub MCP tool: `mcp__github__issue_read` (`method: get`, `owner: LarsLaskowski`, `repo: DockerUpdateGuard`, `issue_number: <number>`), and `method: get_comments` for the discussion.
- Read the title, body, and comments to understand what is actually being asked. If the issue is already `closed`, stop and report that instead of starting work.
- If the issue is ambiguous, underspecified, or could be solved several materially different ways, ask the user a focused clarifying question before writing code. Do not guess on decisions that are expensive to reverse.

### 2. Prepare a clean starting point

- Verify the working tree is clean with `git status --short --branch`. If there are unrelated uncommitted changes, stop and report them — do not bundle them into this fix.
- Make sure you start from an up-to-date base branch (`main` unless the user says otherwise): switch to it and `git pull` so the branch and PR are based on current code.
- Confirm the `origin` remote exists.

### 3. Create the branch

- Derive the branch type from the issue labels and content: use `fix/` for bugs, `feat/` for new functionality, `chore/`/`docs/`/`build/` where appropriate.
- Name the branch `<type>/<number>-<short-kebab-slug>`, e.g. `fix/42-registry-token-cache`. If the user supplied a branch name, use theirs.
- Create and switch to the branch from the base branch.

### 4. Implement the fix

- Solve the issue following the conventions in `AGENTS.md` and `.github/instructions/csharp.instructions.md` (binding C# style: naming, regions, XML docs, null handling, no `this.`, no primary constructors).
- Keep changes small and targeted; reuse existing helpers before adding abstractions.
- Respect the project layering: web startup and DI wiring stay in `src\DockerUpdateGuard`, persistence in `src\DockerUpdateGuard.Data`, observability in `src\DockerUpdateGuard.Telemetry`.
- Add or update tests under `src\Tests` (MSTest, `{Class}{Scenario}{ExpectedResult}` naming, assertion messages required) for the behavior you change.
- Read the surrounding code and match its style, naming, and comment density.

### 5. Validate

- Run `reihitsu-format ./` after making source changes.
- Run `dotnet build DockerUpdateGuard.slnx -c Release --no-restore` (run `dotnet restore DockerUpdateGuard.slnx` first if needed).
- Run the relevant tests, e.g. `dotnet test src\Tests\DockerUpdateGuard.Tests\DockerUpdateGuard.Tests.csproj -c Release --no-build` (and/or the `.Data.Tests` project) for the layer you touched.
- If validation fails, fix the cause before continuing — do not push broken code. If you cannot make it pass, stop and report clearly.

### 6. Commit

- Stage only the files relevant to this fix. Do not include unrelated changes.
- Write a commit message following `AGENTS.md`: one-line summary under 80 characters, no trailing period, not first person, and a body of 3–5 sentences depending on the number of changes. Reference the issue number.
- Do not add any `Co-Authored-By` trailer or any other note attributing the work to an AI/assistant.

### 7. Push and open the pull request

- Push the branch to `origin` with upstream tracking (`git push -u origin <branch>`).
- Open the pull request with `mcp__github__create_pull_request`:
  - `owner: LarsLaskowski`, `repo: DockerUpdateGuard`
  - `base`: `main`, unless the user requested a different base
  - `head`: the branch created in step 3
  - `title`: concise English summary of the fix
  - `body`: a short English summary of the problem and the fix, and a line `Closes #<number>` so the issue auto-closes on merge. If a PR template exists in the repository, structure the body to match it.
  - Do not add any attribution, "Generated with" footer, session link, or other note referencing an AI/assistant in the PR title or body.

### 8. Finish

- After the PR is created, switch back to the base branch (`main`).
- Report the issue number, branch name, and pull request URL clearly.

## Rules

- Prefer non-interactive commands only.
- If push or PR creation fails, stop and report the failure clearly — do not continue as if it succeeded.
- Do not amend existing commits unless the user explicitly asks.
- If switching back to `main` would discard or conflict with uncommitted work, stop and explain the blocker.
- Never close the issue manually; let `Closes #<number>` in the PR body do it on merge.
