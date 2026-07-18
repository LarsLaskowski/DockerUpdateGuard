---
name: review-pr
description: Reviews a GitHub pull request by number and reports findings and actionable recommendations, without changing any code. Use this whenever the user wants a pull request examined, e.g. "review PR 42", "check #42", or passes a bare PR number for review. Posting a review comment is optional and only happens on explicit request.
---

Use this skill when the user gives you a GitHub pull request number (e.g. "review PR 42", "#42", or just "42") and wants it reviewed.

Repository: `LarsLaskowski/DockerUpdateGuard`.

This skill is **read-only**. Its job is to understand the PR and give the user findings and actionable recommendations. It must **not** modify any code, commit, push, change the PR, or check out the branch in a way that alters the working tree beyond what is needed to inspect the diff. The output is a review, not a fix.

Write all output in **English**: your summary to the user, your recommendations, and — only if the user asks for it — any review comment posted to GitHub. This holds regardless of the language the user wrote in.

## Workflow

### 1. Load the pull request

- Confirm the PR number from the user's request. If none was given, stop and ask for one.
- Fetch metadata with `mcp__github__pull_request_read` (`method: get`, `owner: LarsLaskowski`, `repo: DockerUpdateGuard`, `pullNumber: <number>`).
- Fetch the diff with `method: get_diff`, and the changed files with `method: get_files` if you need per-file granularity.
- Check CI status with `method: get_check_runs` (or `get_status`) so the review reflects whether builds/tests currently pass.
- If the PR is already merged or closed, say so and ask whether the user still wants a review before continuing.

### 2. Understand the intent

- Read the PR title and body to understand what the change is supposed to do.
- If the PR references an issue (e.g. `Closes #N`), read that issue with `mcp__github__issue_read` (`method: get`) so you can judge whether the change actually solves the stated problem.

### 3. Review the diff

Evaluate the change against what matters for this project. Focus on:

- **Correctness** — bugs, edge cases, error handling, null handling, concurrency issues. Pay attention to Docker/registry API interaction, async/await usage (missing `.ConfigureAwait(false)` in service/data-access code), and EF Core query/navigation assumptions.
- **Convention adherence** (`AGENTS.md` and `.github/instructions/csharp.instructions.md`) — naming, region layout, file-scoped namespaces, XML documentation on public/internal/private members, no `this.`, no primary constructors, `== false` instead of `!`, layering (`.Data` for persistence, `.Telemetry` for observability, main host for web/DI wiring).
- **Scope and size** — unrelated changes bundled in, accidental file inclusions, debug leftovers.
- **Tests** — whether tests were added or updated under `src\Tests`, whether they follow the `{Class}{Scenario}{ExpectedResult}` naming and MSTest `Assert`/`CollectionAssert` conventions, and whether assertion messages are present.
- **Clarity** — naming, dead code, needless complexity, missing or misleading comments/docs.

Do not run builds that modify files unnecessarily; reading the diff and the surrounding code is usually enough. You may read any file in the repo for context.

### 4. Report findings

Present the review to the user in this structure:

```
## PR #<number> — <title>

**Verdict:** <Approve / Approve with comments / Request changes / Needs discussion>

### Summary
<1–3 sentences on what the PR does and whether it achieves its goal.>

### Findings
- **[Blocking|Suggestion|Nit] <file:line>** — <what and why, with a recommended action.>
- ...

### Recommendations
<Concrete next steps the author should take.>
```

- Classify each finding as **Blocking** (must fix before merge), **Suggestion** (worth doing), or **Nit** (minor/optional).
- Reference exact `file:line` locations so findings are easy to act on.
- If you find nothing wrong, say so plainly rather than inventing issues.

### 5. Optional: post a review comment

- Only post anything to GitHub if the user explicitly asks for it. By default, just report back in the chat.
- If asked, use `mcp__github__pull_request_review_write`:
  - `method: create` with `event: COMMENT` for a neutral English review comment, or `event: APPROVE` / `event: REQUEST_CHANGES` only when the user explicitly chooses that action.
  - For line-specific comments, create a pending review (`method: create` without `event`), add comments with `mcp__github__add_comment_to_pending_review`, then submit with `method: submit_pending`.
- Do not add any attribution, "Generated with" footer, or other note referencing an AI/assistant.

## Rules

- Never modify code, commit, push, or change the PR contents — this skill only reviews.
- Prefer non-interactive commands only.
- Do not post any comment or review to GitHub unless the user explicitly requests it.
- Base your verdict on evidence from the diff and code; if something is uncertain, say so instead of guessing.
