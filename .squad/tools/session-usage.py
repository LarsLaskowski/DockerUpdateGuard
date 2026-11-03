#!/usr/bin/env python3
"""Print the tokens this Claude Code session used, read from Claude Code's own session transcript, for the "Squad run
metrics" of the wrap-up (`.squad/routing.md`, step 12).

`/usage` shows the same numbers on screen only; this script reads them where Claude Code writes them:

- the session's transcript `<projects>/<folder>/<session id>.jsonl`, where `<projects>` is `projects/` under
  `CLAUDE_CONFIG_DIR` (default `~/.claude`) and the session id is `CLAUDE_CODE_SESSION_ID` (or `--session`);
- every subagent's transcript `<session id>/subagents/agent-<id>.jsonl` next to it, with its agent type from
  `agent-<id>.meta.json` — a subagent's tokens are in its own transcript only, never in the session's;
- per model, the input, output, cache-read and cache-write tokens of every API response (a response written
  once per content block is counted once, by its message id).

Calls Claude Code makes on its own (permission checks of auto mode, titles, compaction) are in no transcript, so
the tables are a lower bound: against `/usage`, output and cache-write tokens matched within a few percent, input
and cache-read tokens were 15-20 % lower. No cost is printed: Claude Code's own cost snapshot is written only
when the session pauses and mostly predates the run, and a cost computed here would be an estimate; the costs
are calculated from the token tables where they are needed.

Only these numbers, model ids and agent types are printed; no text of the transcripts, no path, account or other
personal data. The format of these files is Claude Code's internal one and may change with an update: a record
this script cannot read is skipped, never guessed.

Usage, from anywhere:
    python3 .squad/tools/session-usage.py [--session <session id>]

Prints Markdown tables (per model, per subagent type). Exit code 0 when a transcript was read,
2 when none was found (not a Claude Code session, or the id is unknown): it then prints "not provided", which the
wrap-up records as it stands.
"""
import argparse
import collections
import glob
import json
import os
import re
import sys

TOKEN_FIELDS = (
    ("input_tokens", "Input"),
    ("output_tokens", "Output"),
    ("cache_read_input_tokens", "Cache read"),
    ("cache_creation_input_tokens", "Cache write"),
)
NOT_PROVIDED = "not provided (no Claude Code session transcript found)"
NAME = re.compile(r"[\w.-]{1,64}")
TOTAL = "**Total**"
JSONL = ".jsonl"


def projects_dir():
    base = os.environ.get("CLAUDE_CONFIG_DIR") or os.path.join(os.path.expanduser("~"), ".claude")
    return os.path.join(base, "projects")


def find_transcript(root, session):
    """The session's transcript under any project folder, or None; the id is matched, never used as a path."""
    if not session or not NAME.fullmatch(session) or not os.path.isdir(root):
        return None
    for folder in sorted(os.listdir(root)):
        path = os.path.join(root, folder, session + JSONL)
        if os.path.isfile(path):
            return path
    return None


def records(path):
    """The JSON objects of a JSONL file; unreadable lines are skipped."""
    try:
        with open(path, encoding="utf-8", errors="replace") as handle:
            lines = handle.readlines()
    except OSError:
        return
    for line in lines:
        try:
            record = json.loads(line)
        except ValueError:
            continue
        if isinstance(record, dict):
            yield record


def safe_name(value, fallback):
    return value if isinstance(value, str) and NAME.fullmatch(value) else fallback


def response_usage(record):
    """(message id, model, usage) of an API response record, or None."""
    message = record.get("message")
    if record.get("type") != "assistant" or not isinstance(message, dict):
        return None
    usage = message.get("usage")
    if not isinstance(usage, dict):
        return None
    return message.get("id"), safe_name(message.get("model"), "unknown"), usage


def add_usage(counter, usage):
    for field, _ in TOKEN_FIELDS:
        value = usage.get(field)
        if isinstance(value, int) and value > 0:
            counter[field] += value


def read_transcript(path, per_model, seen):
    """Add the transcript's responses to `per_model`; return its own per-model usage."""
    own = collections.defaultdict(collections.Counter)
    for record in records(path):
        found = response_usage(record)
        if not found:
            continue
        message_id, model, usage = found
        if message_id and message_id in seen:
            continue
        seen.add(message_id)
        add_usage(per_model[model], usage)
        add_usage(own[model], usage)
    return own


def agent_type(transcript):
    meta = transcript[: -len(JSONL)] + ".meta.json"
    try:
        with open(meta, encoding="utf-8") as handle:
            data = json.load(handle)
    except (OSError, ValueError):
        return "unknown"
    return safe_name(data.get("agentType") if isinstance(data, dict) else None, "unknown")


def read_session(path):
    """(per model, per agent type) of the session and all its subagents."""
    per_model = collections.defaultdict(collections.Counter)
    seen = set()
    read_transcript(path, per_model, seen)
    agents = {}
    pattern = os.path.join(path[: -len(JSONL)], "subagents", "agent-*.jsonl")
    for transcript in sorted(glob.glob(pattern)):
        own = read_transcript(transcript, per_model, seen)
        entry = agents.setdefault(agent_type(transcript), {"launches": 0, "models": set(), "tokens": collections.Counter()})
        entry["launches"] += 1
        for model, counter in own.items():
            entry["models"].add(model)
            entry["tokens"].update(counter)
    return per_model, agents


def number(value):
    return f"{value:,}"


def token_cells(counter):
    return " | ".join(number(counter[field]) for field, _ in TOKEN_FIELDS)


def model_table(per_model):
    heads = " | ".join(label for _, label in TOKEN_FIELDS)
    lines = [f"| Model | {heads} |", "| --- |" + " ---: |" * len(TOKEN_FIELDS)]
    total = collections.Counter()
    for model in sorted(per_model):
        lines.append(f"| {model} | {token_cells(per_model[model])} |")
        total.update(per_model[model])
    lines.append(f"| {TOTAL} | {token_cells(total)} |")
    return lines


def agent_table(agents):
    heads = " | ".join(label for _, label in TOKEN_FIELDS)
    lines = [f"| Subagent | Launches | Models | {heads} |", "| --- | ---: | --- |" + " ---: |" * len(TOKEN_FIELDS)]
    total = collections.Counter()
    launches = 0
    for name in sorted(agents):
        entry = agents[name]
        models = ", ".join(sorted(entry["models"])) or "–"
        lines.append(f"| {name} | {entry['launches']} | {models} | {token_cells(entry['tokens'])} |")
        total.update(entry["tokens"])
        launches += entry["launches"]
    lines.append(f"| {TOTAL} | {launches} | | {token_cells(total)} |")
    return lines


def report(per_model, agents):
    lines = ["Tokens per model (the session and all its subagents; Claude Code's own background calls are not in "
             "the transcripts, so this is a lower bound):", ""]
    lines += model_table(per_model)
    lines += ["", "Subagents:", ""]
    lines += agent_table(agents) if agents else ["No subagent was launched in this session."]
    return "\n".join(lines)


def main(argv=None):
    parser = argparse.ArgumentParser(description="Print this Claude Code session's tokens per model and per subagent type.")
    parser.add_argument("--session", help="session id (default: CLAUDE_CODE_SESSION_ID)")
    args = parser.parse_args(argv)
    path = find_transcript(projects_dir(), args.session or os.environ.get("CLAUDE_CODE_SESSION_ID"))
    if not path:
        print(NOT_PROVIDED)
        return 2
    print(report(*read_session(path)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
