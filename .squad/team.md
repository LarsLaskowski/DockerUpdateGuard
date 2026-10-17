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
launch through the launch's own `model` and `effort` parameters, but no step does so by default: the plan of
a feature ran on `high` until Vandox#19 (1,188 s and 307 k tokens for the plan, and a `CHANGES_REQUIRED`
from Security all the same), while the plan of Vandox#20 on `medium` went through challenge and security
review with the same rigor; a run on `xhigh` costs about half the member time of a feature run (Vandox#17)
and is not used. Every run
records launches, tokens, tool uses and seconds per role in its working record (`squad-log.py --summary`);
a model or effort change for a role is made on that evidence, in the template, and rolled out from there.

Where production and test code live is defined in `stack.md` (*Layout*).

The **Lead** decides everything inside the squad, including approving plans; the pull request is approved
by the orchestrator's checklist (`.squad/routing.md`, step 9) and the Lead is launched only for what is still
open. The **Product Manager** is only involved when the Lead escalates: an unclear requirement, a
product decision that cannot be derived from the issue or the existing documentation, or a deadlock the
Lead cannot resolve.

## Shared rules (apply to every member)

`AGENTS.md`, `docs/ARCHITECTURE.md`, `docs/CONTRIBUTING.md`, `docs/UNIT_TESTS.md`, `.squad/stack.md` and
`.squad/project.md` are binding: unit tests for all new code until the *Coverage gate* passes, the code
conventions from `stack.md` while writing, English for everything
that ends up in the repository or on GitHub. Inside the squad, the member that changed files — Dev,
Tester, or the orchestrator for its own edits — runs *Format* from `stack.md` before handing back and keeps
its result to those files: every other file the formatter touched is reported, not edited, and the
orchestrator restores it. *Format* is a deterministic command, not a judgment; an older `stack.md` that
labels it "Code Officer only" is superseded by this rule. The Code Officer owns a clean analyzer gate and is
launched only for analyzer diagnostics or style findings that need reading. Subagents never run Git write operations or post
to GitHub — the `PreToolUse` hook `.claude/hooks/git-guard.py`, declared in every `squad-*.md`, denies them
(`git add` and a scratch `git worktree` for experiments stay allowed, `.squad/routing.md`, *Concurrency*);
the orchestrator commits and pushes to the work branch at any time (see `AGENTS.md`, golden rules) and
opens the pull request only after the Lead's approval. Because the orchestrator may commit an interim state
while a member works (a stop hook demands it), a clean `git status` at hand-back is no sign of lost work: a
member reports its changes against the head SHA it was given (`git diff <sha>`).

Session-management tools (`create_session`, `send_message`, `archive_session` and the other remote-session
tools) belong to the orchestrator alone: a member never calls them, also not to "hand over" work — it
reports the need in its result instead.

**Continued members.** For a fix or delta round the orchestrator may continue a member's earlier launch of
the same run instead of starting a fresh one (`.squad/routing.md`, *Pipeline*). A continued member keeps
its role and its limits: the Dev still does not edit tests, the Reviewer and Security still review only the
commit SHA they are given, and nothing it remembers from an earlier round replaces reading the delta. Its
task notification reports the agent's tokens since the first launch (tool uses and duration for the round
alone); the orchestrator logs the round with `--continued` (or `--continued-from "<step>"` for an agent that
is not the member's last fresh launch), so the record carries the round's share.

**Test doubles that model a real component.** A test double that copies the behavior of a library, framework
or server (a response stream where a flush starts the response, a client that retries, a parser that stops at
a limit) is only as good as that copy, and every gate and test passes on a wrong one. A claim about such
behavior that a double encodes is checked once against the real component — a scratch measurement in a
scratch `git worktree`, or the component's source — before the tests rely on it, and the hand-back names the
measurement. Security's plan review asks for that measurement when the plan relies on such behavior.

**Escapes in files.** The Write and Edit tools turn a single-backslash escape such as `\u0041` into the
character it names, so a plan table or a test fixture whose cases are about escapes silently loses them. Write
each such escape with a doubled backslash in the tool input, or generate the file with a script; either way
check the bytes on disk (`grep -n '\\u' <file>`) and confirm every escape is there exactly as the case needs
before reporting the file done — again after *Format*, since a formatter may do the same to string literals
(*Known pitfalls* in `.squad/stack.md`). A test whose case is about an escape also asserts that its input still
contains the backslash, so a lost escape fails the run instead of testing the wrong input.
