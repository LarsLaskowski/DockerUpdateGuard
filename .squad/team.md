# Team

Squad for this repository, used for both GitHub issues (`squad-issue` skill) and new features
(`squad-spec` skill). The idea follows [bradygaster/squad](https://github.com/bradygaster/squad): this
file names the members, `routing.md` holds the pipeline and its limits, and each role's charter is the
Claude Code subagent file under `.claude/agents/` (all changed only in squad-maintenance PRs). The roles run
as subagents, driven by the invoking session (the orchestrator).

The squad files are stack-neutral and come from the Squad-Spec-Repository-Template repository. Everything
specific to this repository lives in two files the members read first:

- [`stack.md`](stack.md) — toolchain, layout and the exact commands: format, build, test, coverage, the
  analyzer gate, how to write code and tests that pass it, and how to build a compile-only skeleton.
- [`project.md`](project.md) — what this project guarantees: the security areas that decide the
  `security` tier, deliberate guarantees, the integration surface the Reviewer sweeps, and the test doubles.

## Members

| Role             | Subagent (charter)                                                    | Model, effort  | Writes                                                           |
| ---------------- | --------------------------------------------------------------------- | -------------- | ---------------------------------------------------------------- |
| Lead             | [`squad-lead`](../.claude/agents/squad-lead.md)                       | Opus, medium   | plans, decisions, area documents                                 |
| Devil's Advocate | [`squad-devils-advocate`](../.claude/agents/squad-devils-advocate.md) | Sonnet, medium | nothing (read-only)                                              |
| Security         | [`squad-security`](../.claude/agents/squad-security.md)               | Opus, medium   | nothing (read-only)                                              |
| Tester           | [`squad-tester`](../.claude/agents/squad-tester.md)                   | Sonnet, medium | test code                                                        |
| Dev              | [`squad-dev`](../.claude/agents/squad-dev.md)                         | Sonnet, medium | production code                                                  |
| Code Officer     | [`squad-code-officer`](../.claude/agents/squad-code-officer.md)       | Haiku, medium  | production and test code (format, analyzer and style fixes only) |
| Reviewer         | [`squad-reviewer`](../.claude/agents/squad-reviewer.md)               | Opus, medium   | nothing (read-only)                                              |
| Product Manager  | —                                                                     | —              | answers escalations (the human user)                             |

The model aliases resolve to the current generation of each line; the effort is set per role in the agent
file so a session's effort setting does not change every role at once. The orchestrator may raise a single
launch (the plan of a feature on `high`) through the launch's own `model` and `effort` parameters; a run on
`xhigh` costs about half the member time of a feature run (Vandox#17) and is not used. Every run
records launches, tokens, tool uses and seconds per role in its working record (`squad-log.py --summary`);
a model or effort change for a role is made on that evidence, in the template, and rolled out from there.

Where production and test code live is defined in `stack.md` (*Layout*).

The **Lead** decides everything inside the squad, including approving plans; the pull request is approved
by the orchestrator's checklist (`.squad/routing.md`, step 9) and the Lead is launched only for what is still
open. The **Product Manager** is only involved when the Lead escalates: an unclear requirement, a
product decision that cannot be derived from the issue or the existing documentation, or a deadlock the
Lead cannot resolve.

## Shared rules (apply to every member)

`CLAUDE.md`, `docs/ARCHITECTURE.md`, `docs/CONTRIBUTING.md`, `docs/UNIT_TESTS.md`, `.squad/stack.md` and
`.squad/project.md` are binding: unit tests for all new code until the *Coverage gate* passes, the code
conventions from `stack.md` while writing, English for everything
that ends up in the repository or on GitHub. Inside the squad, only the Code Officer runs the formatter
(*Format* in `stack.md`) and owns a clean analyzer gate. Subagents never run Git write operations or post
to GitHub — the `PreToolUse` hook `.claude/hooks/git-guard.py`, declared in every `squad-*.md`, denies them
(`git add` and a scratch `git worktree` for experiments stay allowed, `.squad/routing.md`, *Concurrency*);
the orchestrator commits and pushes to the work branch at any time (see `CLAUDE.md`, golden rules) and
opens the pull request only after the Lead's approval.
