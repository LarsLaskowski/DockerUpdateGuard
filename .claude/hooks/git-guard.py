#!/usr/bin/env python3
"""PreToolUse hook for the squad subagents: no Git write operation and no GitHub write from a member.

Every `.claude/agents/squad-*.md` declares this hook in its front matter, so it runs only while a squad
member is active - the orchestrator (the session that runs the skill) commits, pushes and posts as before.
It reads the hook input JSON from stdin and, for a `Bash` call, checks every `git` and `gh` invocation in
the command (also behind `&&`, `;`, `|`, `$(…)` and `git -C <dir>`):

- allowed: read-only Git (`status`, `diff`, `log`, `show`, `ls-files`, `grep`, `rev-parse`, `merge-base`,
  `fetch`, `tag --list` / `--contains`, `branch --list` / `--show-current`, `ls-remote`, `clone`, …),
  `git add` (the coverage gate only counts tracked files) and `git worktree add` / `remove` / `prune` /
  `list` (scratch worktrees, `.squad/routing.md`, *Concurrency*), and read-only `gh` (`gh api` without a
  method or field, `gh pr view`, …);
- denied: `commit`, `push`, `stash`, `reset`, `checkout`, `switch`, `restore`, `rebase`, `merge`,
  `cherry-pick`, `revert`, `rm`, `mv`, `clean`, `pull`, `am`, `apply`, `tag <name>`, `branch -d/-D/-m/…`,
  `notes`, `update-ref`, `reflog`, `gc`, `filter-branch`, `config` (except `--get*`, `--list`), and `gh`
  writes (`gh pr create|comment|review|merge|close|edit|ready`, `gh issue create|comment|close|edit`,
  `gh api` with `-X`/`--method` other than GET, `-f`, `-F`, `--field`, `--raw-field`, `--input`).

A denied call answers `{"hookSpecificOutput": {"permissionDecision": "deny", …}}` with the reason, so the
member reports the need to the orchestrator instead. Anything else, including input the hook cannot
parse, is left to the normal permission flow (exit 0, no decision).
"""
import json
import re
import shlex
import sys

GIT_DENIED = {
    "commit", "push", "stash", "reset", "checkout", "switch", "restore", "rebase", "merge", "cherry-pick",
    "revert", "rm", "mv", "clean", "pull", "am", "apply", "notes", "update-ref", "reflog", "gc",
    "filter-branch", "replace", "submodule", "bisect",
}
GIT_OPTIONS_WITH_VALUE = {"-C", "-c", "--git-dir", "--work-tree", "--namespace", "--exec-path"}
WORKTREE_ALLOWED = {"add", "remove", "prune", "list", "lock", "unlock"}
GH_WRITE_SUBCOMMANDS = {
    "pr": {"create", "comment", "review", "merge", "close", "edit", "ready", "reopen", "lock", "unlock"},
    "issue": {"create", "comment", "close", "edit", "reopen", "delete", "transfer", "pin", "unpin", "lock", "unlock"},
    "release": {"create", "delete", "edit", "upload"},
    "repo": {"create", "delete", "edit", "fork", "rename", "archive", "unarchive", "sync"},
    "label": {"create", "delete", "edit", "clone"},
}
GH_API_WRITE_FLAGS = {"-f", "-F", "--field", "--raw-field", "--input"}
SEGMENT_SPLIT = re.compile(r"&&|\|\||[;|()`\n]|\$\(")
TAG_READ = {"-l", "--list", "--contains", "--points-at", "--merged", "--no-merged"}
BRANCH_WRITE = {"-d", "-D", "-m", "-M", "-c", "-C", "-f", "--delete", "--move", "--copy", "--force",
                "--set-upstream-to", "-u", "--unset-upstream", "--edit-description"}
BRANCH_READ = {"-l", "--list", "-a", "-r", "--all", "--remotes", "--show-current", "--contains", "--merged",
               "--no-merged", "-v", "-vv", "--verbose"}
CONFIG_READ = {"-l", "--list", "--show-origin", "--show-scope"}


def git_subcommand(tokens):
    """(subcommand, arguments) of a git invocation, skipping global options such as `-C <dir>`."""
    i = 0
    while i < len(tokens):
        token = tokens[i]
        if token in GIT_OPTIONS_WITH_VALUE:
            i += 2
        elif token.startswith("-"):
            i += 1
        else:
            return token, tokens[i + 1:]
    return None, []


def tag_verdict(args):
    """`git tag <name>` creates a tag; the listing and query forms are reads."""
    if args and not any(a in TAG_READ or a.startswith("--list=") for a in args):
        return "git tag <name>"
    return None


def branch_verdict(args):
    if any(a in BRANCH_WRITE for a in args):
        return "git branch (delete/move/copy/upstream)"
    if args and not any(a in BRANCH_READ or a.startswith("--") for a in args):
        return "git branch <name>"
    return None


def worktree_verdict(args):
    if args and args[0] in WORKTREE_ALLOWED:
        return None
    return "git worktree " + (args[0] if args else "")


def config_verdict(args):
    if any(a.startswith("--get") or a in CONFIG_READ for a in args):
        return None
    return "git config (write)"


GIT_CHECKS = {"tag": tag_verdict, "branch": branch_verdict, "worktree": worktree_verdict, "config": config_verdict}


def git_verdict(tokens):
    sub, args = git_subcommand(tokens)
    if sub is None:
        return None
    if sub in GIT_DENIED:
        return f"git {sub}"
    check = GIT_CHECKS.get(sub)
    return check(args) if check else None


def api_method(args, i):
    """The HTTP method `gh api` is given at position i (`-X POST`, `--method POST`, `--method=POST`), or None."""
    option = args[i]
    if option in ("-X", "--method"):
        return args[i + 1] if i + 1 < len(args) else None
    if option.startswith("--method="):
        return option.split("=", 1)[1]
    return None


def gh_api_verdict(args):
    for i, option in enumerate(args):
        if option in GH_API_WRITE_FLAGS or option.startswith(("--field=", "--raw-field=", "--input=")):
            return "gh api with a field or input (a write)"
        method = api_method(args, i)
        if method and method.upper() != "GET":
            return f"gh api {option} {method}"
    return None


def gh_verdict(tokens):
    if len(tokens) < 2:
        return None
    group, rest = tokens[0], tokens[1:]
    if group == "api":
        return gh_api_verdict(rest)
    writes = GH_WRITE_SUBCOMMANDS.get(group)
    return f"gh {group} {rest[0]}" if writes and rest[0] in writes else None


def basename(token):
    return token.rsplit("/", 1)[-1]


def check_command(command):
    """The first denied invocation in the command as text, or None."""
    for segment in SEGMENT_SPLIT.split(command):
        try:
            tokens = shlex.split(segment)
        except ValueError:
            tokens = segment.split()
        for i, token in enumerate(tokens):
            name = basename(token)
            if name == "git":
                found = git_verdict(tokens[i + 1:])
            elif name == "gh":
                found = gh_verdict(tokens[i + 1:])
            else:
                continue
            if found:
                return found
            break
    return None


def deny_reason(data):
    """The reason to deny the Bash call described by the hook input, or None when it may run."""
    if not isinstance(data, dict) or data.get("tool_name") != "Bash":
        return None
    command = (data.get("tool_input") or {}).get("command")
    found = check_command(command) if isinstance(command, str) else None
    if found is None:
        return None
    agent = data.get("agent_type") or "a squad member"
    return (f"{found} is a Git or GitHub write operation, which {agent} never runs: subagents report, the "
            "orchestrator commits, pushes and posts (.squad/team.md, Shared rules). Scratch worktrees "
            "(git worktree add/remove) and git add stay allowed.")


def main():
    """Exit code 0 either way: a denial is the JSON decision on stdout, not an error."""
    try:
        data = json.load(sys.stdin)
    except ValueError:
        return
    reason = deny_reason(data)
    if reason is not None:
        print(json.dumps({"hookSpecificOutput": {"hookEventName": "PreToolUse", "permissionDecision": "deny",
                                                 "permissionDecisionReason": reason}}))


if __name__ == "__main__":
    main()
