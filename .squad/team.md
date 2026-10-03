# Team

Squad for this repository, used for both GitHub issues (`squad-issue` skill) and new features
(`squad-spec` skill). The layout follows [bradygaster/squad](https://github.com/bradygaster/squad)
(`team.md`, `routing.md`, `decisions.md`, `agents/{name}/charter.md` + `history.md`, all changed only in
squad-maintenance PRs); in Claude Code the roles run as subagents under `.claude/agents/`, driven by the
invoking session (the orchestrator). An agent without subagent support runs each role inline by following
the same agent file.

The squad files are stack-neutral and come from the Squad-Spec-Repository-Template repository. Everything
specific to this repository lives in two files the members read first:

- [`stack.md`](stack.md) — toolchain, layout and the exact commands: format, build, test, coverage, the
  analyzer gate, how to write code and tests that pass it, and how to build a compile-only skeleton.
- [`project.md`](project.md) — what this project guarantees: the security areas that decide the
  `security` tier, deliberate guarantees, the integration surface the Reviewer sweeps, and the test doubles.

## Members

| Role             | Charter                                       | Claude subagent         | Model  | Writes                                                  |
| ---------------- | --------------------------------------------- | ----------------------- | ------ | ------------------------------------------------------- |
| Lead             | [charter](agents/lead/charter.md)             | `squad-lead`            | Opus   | plans, decisions                                        |
| Devil's Advocate | [charter](agents/devils-advocate/charter.md)  | `squad-devils-advocate` | Opus   | nothing (read-only)                                     |
| Security         | [charter](agents/security/charter.md)         | `squad-security`        | Opus   | nothing (read-only)                                     |
| Tester           | [charter](agents/tester/charter.md)           | `squad-tester`          | Sonnet | test code                                               |
| Dev              | [charter](agents/dev/charter.md)              | `squad-dev`             | Sonnet | production code                                         |
| Code Officer     | [charter](agents/code-officer/charter.md)     | `squad-code-officer`    | Sonnet | production and test code (format, analyzer and style fixes only) |
| Reviewer         | [charter](agents/reviewer/charter.md)         | `squad-reviewer`        | Opus   | nothing (read-only)                                     |
| Product Manager  | —                                             | the human user          | —      | answers escalations                                     |

Where production and test code live is defined in `stack.md` (*Layout*).

The **Lead** decides everything inside the squad, including approving plans and approving the pull
request. The **Product Manager** is only involved when the Lead escalates: an unclear requirement, a
product decision that cannot be derived from the issue or the existing documentation, or a deadlock the
Lead cannot resolve.

## Shared rules (apply to every member)

`CLAUDE.md`, `docs/ARCHITECTURE.md`, `docs/CONTRIBUTING.md`, `docs/UNIT_TESTS.md`, `.squad/stack.md` and
`.squad/project.md` are binding: unit tests for all new code with at least 80 % line coverage on
new/changed production code, the code conventions from `stack.md` while writing, English for everything
that ends up in the repository or on GitHub. Inside the squad, only the Code Officer runs the formatter
(*Format* in `stack.md`) and owns a clean analyzer gate. Subagents never run Git write operations, except
creating and removing a scratch `git worktree` for experiments (`.squad/routing.md`, *Concurrency*); the
orchestrator commits and pushes to the work branch at any time (see `CLAUDE.md`, golden rules) and opens
the pull request only after the Lead's approval.
