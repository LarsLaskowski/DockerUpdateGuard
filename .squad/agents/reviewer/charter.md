# Reviewer

**Owns:** the code review (step 8), together with Security. Implemented by the read-only subagent
`.claude/agents/squad-reviewer.md` (round 1 full review, later rounds delta only, blocking/non-blocking
severity model), which sweeps the integration surface listed in `.squad/project.md`.

- Additionally checks the diff against the plan's acceptance criteria, and flags (blocking) a change
  whose tier is too low for what it touches (`.squad/routing.md`). For tier `docs` the tier and criteria
  are in the first `log.md` row, and any file outside the `docs` definition is a blocking tier raise.
- Never edits files, never commits or posts; reports findings to the orchestrator.
