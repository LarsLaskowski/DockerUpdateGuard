---
name: rereview-pr
description: Re-reviews a GitHub pull request after review feedback was addressed, focusing only on what changed since the previous review, without changing any code. Use this whenever the user wants a follow-up look at a PR after fixes were pushed, e.g. "rereview PR 42", "re-review #42 after the fixes", "check if the review comments on 42 were addressed".
---

Use this skill when the user gives you a GitHub pull request number and wants it re-reviewed after earlier review feedback (from this skill's `review-pr` sibling, a human reviewer, or GitHub review comments) was supposedly addressed.

Repository: `LarsLaskowski/DockerUpdateGuard`.

This skill is **read-only**, exactly like `review-pr`. Its job is to check whether the previous review's findings were actually resolved and whether the newest commits introduced anything new worth flagging. It must **not** modify any code, commit, push, change the PR, or check out the branch in a way that alters the working tree beyond what is needed to inspect the diff.

Write all output in **English**: your summary to the user, your recommendations, and — only if the user asks for it — any review comment posted to GitHub. This holds regardless of the language the user wrote in.

## Scope

Branch protection on this repository blocks a PR from merging on its own — a review is always required first. That is just background context for why re-reviews happen; it is not a finding to restate in your output.

Keep the review itself narrow. Only evaluate:

1. Whether each finding from the previous review was actually resolved by the new commits.
2. The code changes made since the previous review (not the whole PR from scratch, unless nothing was reviewed before).
3. Whether the PR description still matches what the diff now does.
4. The SonarQube Cloud check status — at most.

Anything else about the PR (other CI checks such as build/test/CodeQL runs, labels, assignees, unrelated discussion) is out of scope and should not be reported on.

## Workflow

### 1. Find the previous review

- Confirm the PR number from the user's request. If none was given, stop and ask for one.
- Fetch metadata with `mcp__github__pull_request_read` (`method: get`, `owner: LarsLaskowski`, `repo: DockerUpdateGuard`, `pullNumber: <number>`).
- Find what was reviewed before:
  - Look at prior review submissions and review comments with `mcp__github__pull_request_read` (`method: get_reviews` and `method: get_review_comments`).
  - If the user pasted or referenced a previous `review-pr` report in the conversation, use that as the list of findings instead of (or in addition to) GitHub review comments.
- If you cannot find any previous review or findings at all, say so and ask the user whether to proceed as a full `review-pr`-style review instead.

### 2. Isolate what changed since that review

- Identify the commit (or timestamp) the previous review was based on — the latest commit reviewed, or the time of the last review submission.
- Fetch the current diff with `method: get_diff` and the changed files with `method: get_files`.
- Where possible, scope your reading to the commits/files touched **since** the previous review point, rather than re-reading the entire PR diff from scratch. Use the full diff only for context when a finding can't be judged from the incremental change alone.
- Fetch check runs with `method: get_check_runs` and read only the SonarQube Cloud check's conclusion. Ignore every other check.

### 3. Check each previous finding

For every finding from the previous review:

- Mark it **Resolved**, **Not resolved**, or **Partially resolved**, with a one-line reason pointing at the relevant `file:line`.
- If a finding was a **Blocking** item and is not resolved, it stays blocking.
- If the fix introduces a new problem (regression, incomplete fix, new edge case), report that as a new finding.

### 4. Check the description and new code

- Compare the current PR description to the current diff; if it no longer matches (e.g. the fix changed scope but the description wasn't updated), raise it as a finding.
- Review any newly added or changed code (since the previous review) against the same criteria `review-pr` uses: correctness (bugs, edge cases, null handling, concurrency, async/`.ConfigureAwait(false)`, EF Core navigation assumptions), convention adherence (`AGENTS.md` and `.github/instructions/csharp.instructions.md`), scope/size, tests (`src\Tests`, `{Class}{Scenario}{ExpectedResult}` naming, MSTest assertions with messages), and clarity.

### 5. Report findings

Present the re-review to the user in this structure:

```
## Re-review of PR #<number> — <title>

**Verdict:** <Approve / Approve with comments / Request changes / Needs discussion>

### Summary
<1–3 sentences on whether the previous feedback was addressed and the PR is now in better shape.>

**Description match:** <Does the PR description accurately reflect the current diff? Yes/No and why.>
**SonarQube Cloud:** <Pass / Fail / Warnings / Not run — no other checks.>

### Previous findings
- **[Resolved|Not resolved|Partially resolved] <file:line>** — <what changed, or why it's still open.>
- ...

### New findings
- **[Blocking|Suggestion|Nit] <file:line>** — <what and why, with a recommended action.>
- ...

### Recommendations
<Concrete next steps the author should take.>
```

- Reference exact `file:line` locations so findings are easy to act on.
- If everything was resolved and nothing new came up, say so plainly rather than inventing issues.

### 6. Optional: post a review comment

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
- Stay inside the scope defined above: previous-finding resolution, the changes since the last review, the description-vs-diff match, and the SonarQube Cloud check. Do not comment on other checks, labels, or metadata.
