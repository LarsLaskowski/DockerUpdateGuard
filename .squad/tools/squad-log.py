#!/usr/bin/env python3
"""Append one row to a squad working record's `log.md`, the way the pipeline wants it, and summarize what
each role cost over the run.

The orchestrator is the only writer of `log.md`; it records what the members report. This script keeps the
mechanics out of the orchestrator's hands:

- the row is `| <date> <time> | <step> | <member> | <result> |` (`2026-10-11 13:05`, UTC), appended after the
  last line, which gets its line ending first if it lacks one; rows of older records that carry only the date
  are still read;
- the line ending follows the file (CRLF when the file uses it), so rows never end up with mixed endings;
- a `|` in a cell is written as `\\|`, a line break in the result as `<br>` (a Markdown table cell has one line);
- a control character (Unicode categories Cc, Cf, Zl, Zp other than tab and line break) is written as its
  escape (`U+202E`), never as the character itself; a tab becomes a space;
- for a subagent launch, `--agent squad-<role>` (model and effort read from `.claude/agents/squad-<role>.md`,
  so nothing is typed from memory) or `--launch MODEL/EFFORT`, with `--tokens N --tool-uses N --seconds N` from
  the usage block of the launch's task notification, appends a trailer `(opus/high · 37,445 tokens · 6 tool
  uses · 57 s)` the summary can read back;
- the first row replaces the template's heading `# Log: <issue #number | feature name>` with the work folder's
  (`# Log: issue #3`, `# Log: feature incidents-view`), so the posted record carries no placeholder;
- one row names one member: a member cell that lists two (`squad-reviewer / squad-security`, a comma, `and`)
  is refused, because the trailer of a parallel launch belongs to one row each, or `--amend-last` cannot tell
  them apart;
- a continued launch (`SendMessage` to the agent of an earlier round; `.squad/routing.md`, *Continuing a
  member*) reports the agent's tokens since its first launch, while its tool uses and its duration cover the
  round alone: with `--continued` the script subtracts the tokens of that member's last fresh launch and of
  the continuations logged after it, keeps tool uses and seconds as reported and marks the trailer
  `· continued`; a reported token figure below the earlier total can only be the round alone and is kept as it
  is. When the continued agent is not the member's last fresh launch (one member ran several task groups,
  each in a fresh launch), `--continued-from "<step>"` names the step cell of the fresh launch whose agent was
  continued: only that launch and its own continuations are subtracted, and the trailer records the step
  (`· continued from <step>`), so a later continuation of either agent is computed against the right rows.
  `--continued` needs an earlier row of that member with usage, `--continued-from` a fresh launch of that step;
- the usage block often arrives only after the member's hand-back: `--amend-last MEMBER` with the same
  options adds the trailer to the last row of that member afterwards, so the row can be written as soon as
  the report is in. A row gets its trailer once; a second amend is refused. When a later row of that member
  exists already (two members that ran in parallel, and the first one's notification came last),
  `--step "<step>"` selects the last row of the member whose step cell is that text, so an earlier launch row
  is still amended with the tool, never by hand;
- a row whose trailer is wrong (a launch logged with made-up numbers before its notification arrived) is
  corrected with `--replace-last MEMBER` and the same options (`--step` selects an earlier row the same way):
  the trailer of that row is replaced, so the launch is still counted once;
- a launch whose notification never arrived is marked with `--amend-last MEMBER --unreported --agent
  squad-<role>` (no numbers): the trailer `(opus/medium · usage not reported)` lets the summary count the
  launch, name it as unreported and exit 0, so the table is posted as the tool prints it instead of being
  summed by hand or left out. `--replace-last` with the real numbers turns it into a full trailer later;
- rows of the Orchestrator, the Product Manager and the User (an answer, a measurement the user ran) are
  not launches: write them with that member cell and no metric option, the summary does not ask for usage;
  a metric option, `--amend-last`, `--replace-last` or `--unreported` for such a member is refused, since no
  task notification exists for it;
- the metrics are what a launch really cost: fewer than 1,000 tokens (every launch loads its agent file and
  instructions first, so `--tokens 0` or `--tokens 1` can only be a placeholder) and negative numbers are
  refused, so a placeholder cannot be logged by accident. A row for a launch whose notification has not
  arrived yet is written without any metric option and completed with `--amend-last` — the only way to add
  the numbers later.

Usage, from anywhere inside the repository:
    python3 .squad/tools/squad-log.py <work folder> <step> <member> <result> [--agent squad-lead
        --tokens 37445 --tool-uses 6 --seconds 57]
    python3 .squad/tools/squad-log.py <work folder> --amend-last Lead --agent squad-lead --tokens 37445
        --tool-uses 6 --seconds 57
    python3 .squad/tools/squad-log.py <work folder> --replace-last Lead --agent squad-lead --tokens 37445
        --tool-uses 6 --seconds 57
    python3 .squad/tools/squad-log.py <work folder> --amend-last Security --step "3 Plan security" --agent
        squad-security --tokens 45751 --tool-uses 7 --seconds 108
    python3 .squad/tools/squad-log.py <work folder> --amend-last "Devil's Advocate" --unreported --agent
        squad-devils-advocate
    python3 .squad/tools/squad-log.py <work folder> "8 Review delta" Reviewer "APPROVE" --agent squad-reviewer
        --tokens 139230 --tool-uses 4 --seconds 61 --continued
    python3 .squad/tools/squad-log.py <work folder> "7 Fix" Dev "fixed" --agent squad-dev --tokens 412000
        --tool-uses 9 --seconds 317 --continued-from "6 Implement (rows 9-14)"
    python3 .squad/tools/squad-log.py <work folder> --summary

`<work folder>` is the folder's name under `specs/` (`issue-12`, or `specs/issue-12`); it is matched against the
folders that exist there, never used as a path, so no argument reaches the file system; `--agent` is matched
against the files under `.claude/agents/` the same way. `<result>` may contain line breaks.
`--summary` prints a Markdown table per member (launches, with the continuations among them, tokens, tool
uses, seconds) with a total row, for the "Squad working record" comment and the wrap-up report, followed by an
**Orchestrator** table once two rows carry a time of day: the wall time from the first to the last such row,
the part of it no launch covers (an upper bound for the orchestrator's own work and the waiting between
launches; launches that ran in parallel count once, each placed by its row's time and its seconds) and the
number of gate runs (Orchestrator rows of step 7). Every row of a member other than the Orchestrator, the
Product Manager and the User is a launch: the summary ends with the launch rows that carry no usage yet, so the
orchestrator amends them before posting and an incomplete table is never posted as complete; launches marked
`--unreported` are counted and named below the table instead. Exit code 0 when the row was written, amended or
the summary is complete, 1 when the folder has no `log.md`, the agent file is unknown, no row can be amended or
replaced, or `--continued` finds no earlier usage of the member (`--continued-from`: no fresh launch of that
step), 3 when the summary lists launch rows without usage.
"""
import argparse
import datetime
import os
import re
import subprocess
import sys
import unicodedata

CONTROL_CATEGORIES = {"Cc", "Cf", "Zl", "Zp"}
KEEP = {"\t", "\n", "\r"}
TRAILER = re.compile(r"\((?P<launch>[\w.-]+/[\w-]+) · (?P<tokens>[\d,]+) tokens · (?P<tools>\d+) tool uses · "
                     r"(?P<seconds>\d+) s(?P<continued> · continued(?: from (?P<origin>.+))?)?\)\s*$")
UNREPORTED = "usage not reported"
UNREPORTED_TRAILER = re.compile(rf"\((?P<launch>[\w.-]+/[\w-]+) · {UNREPORTED}\)\s*$")
ANY_TRAILER = re.compile(rf"\((?P<launch>[\w.-]+/[\w-]+) · (?:{UNREPORTED}|[\d,]+ tokens · \d+ tool uses · \d+ s"
                         r"(?: · continued(?: from .+)?)?)\)\s*$")
TWO_MEMBERS = re.compile(r"[,/+&]|\band\b")
ORCHESTRATOR = "orchestrator"
NOT_LAUNCHES = {ORCHESTRATOR, "product manager", "user"}
STAMP = r"\d{4}-\d{2}-\d{2}(?: \d{2}:\d{2})?"
ROW = re.compile(rf"^\| (?P<date>{STAMP}) \| (?P<step>(?:[^|\\]|\\\|)*) \| (?P<member>(?:[^|\\]|\\\|)*) \| "
                 r"(?P<result>.*) \|\s*$")
MIN_TOKENS = 1000
FRONT_MATTER_KEY = re.compile(r"^(model|effort):\s*(\S+)\s*$", re.M)


def escape_controls(text):
    """Every control character other than tab and line break as `U+XXXX`."""
    return "".join(f"U+{ord(ch):04X}" if unicodedata.category(ch) in CONTROL_CATEGORIES and ch not in KEEP else ch
                   for ch in text)


def cell(text):
    text = escape_controls(text).replace("\t", " ")
    text = re.sub(r"\r\n|\r|\n", "<br>", text.strip())
    return text.replace("|", "\\|")


def trailer(launch, tokens, tool_uses, seconds, continued=False, origin=None):
    """The launch trailer, already escaped as part of a cell; with `tokens` None, the mark of a launch whose usage
    was never reported. A continuation of a fresh launch other than the member's last names that launch's step."""
    launch = cell(launch)
    if tokens is None:
        return f"({launch} · {UNREPORTED})"
    mark = ""
    if continued:
        mark = f" · continued from {cell(origin)}" if origin else " · continued"
    return f"({launch} · {tokens:,} tokens · {tool_uses} tool uses · {seconds} s{mark})"


def metrics(match):
    """(tokens, tool uses, seconds) of a trailer match."""
    return (int(match.group("tokens").replace(",", "")), int(match.group("tools")), int(match.group("seconds")))


def previous_tokens(path, member, skip_last=False, step=None, origin=None):
    """The tokens the continued agent had used before the round being logged: its fresh launch plus the
    continuations logged for it. Without `origin` that agent is the member's last fresh launch, with `origin` the
    member's last fresh launch whose step cell is that text; a continuation row belongs to the fresh launch its
    trailer names (`· continued from <step>`), or else to the member's last fresh launch before it. None when the
    member has no row with usage (with `origin`: no fresh launch of that step). With `skip_last` the member's last
    trailer is left out (it is the one being replaced); with `step`, only the rows before the member's last row of
    that step count (that row is the one amended or replaced)."""
    with open(path, encoding="utf-8-sig") as handle:
        rows = parse_rows(handle.read())
    wanted = cell(member)
    if step is not None:
        rows = rows_before(rows, wanted, cell(step))
    matches = [(row_step, TRAILER.search(result)) for _, row_step, row_member, result in rows if row_member == wanted]
    matches = [(row_step, match) for row_step, match in matches if match]
    if skip_last and step is None:
        matches = matches[:-1]
    chains, fresh, current = {}, {}, None
    for index, (row_step, match) in enumerate(matches):
        tokens = metrics(match)[0]
        if not match.group("continued"):
            current, fresh[row_step] = index, index
            chains[index] = tokens
            continue
        own = match.group("origin")
        chain = fresh.get(own) if own else current
        chains[chain] = chains.get(chain, 0) + tokens
    if origin is not None:
        target = fresh.get(cell(origin))
        return None if target is None else chains[target]
    return chains.get(current)


def rows_before(rows, member, step):
    """The rows before the last row of `member` whose step cell is `step` (both already escaped as cells); all
    rows when there is no such row."""
    for index in range(len(rows) - 1, -1, -1):
        if rows[index][2] == member and rows[index][1] == step:
            return rows[:index]
    return rows


def continued_launch(path, member, launch, skip_last=False, step=None, origin=None):
    """The round's own share of a continued launch: the reported tokens minus the agent's earlier tokens, tool
    uses and seconds as reported (they cover the round alone). A reported token figure below the earlier total
    was already the round alone and is kept. None when there is nothing to continue from (`previous_tokens`)."""
    earlier = previous_tokens(path, member, skip_last, step, origin)
    if earlier is None:
        return None
    model, tokens, tool_uses, seconds = launch[:4]
    return (model, tokens - earlier if tokens >= earlier else tokens, tool_uses, seconds, True, origin)


def resolve_log(root, folder):
    """The `log.md` of the named work folder under `specs/`, or None. The name is compared with the folders
    that exist there and the path is built from the directory listing, so the argument never becomes a path."""
    wanted = folder.strip().replace("\\", "/").rstrip("/")
    wanted = wanted[len("specs/"):] if wanted.startswith("specs/") else wanted
    specs = os.path.join(root, "specs")
    if not os.path.isdir(specs):
        return None
    for name in sorted(os.listdir(specs)):
        if name == wanted and name not in (".", "..") and os.path.isdir(os.path.join(specs, name)):
            path = os.path.join(specs, name, "log.md")
            return path if os.path.isfile(path) else None
    return None


def agent_file(root, agent):
    """`.claude/agents/<agent>.md` when it exists; found through the directory listing, so the argument never
    becomes a path."""
    wanted = agent.strip().removesuffix(".md") + ".md"
    agents = os.path.join(root, ".claude", "agents")
    if not os.path.isdir(agents):
        return None
    names = [n for n in os.listdir(agents) if n == wanted and os.path.isfile(os.path.join(agents, n))]
    return os.path.join(agents, names[0]) if names else None


def agent_launch(root, agent):
    """`model/effort` from the front matter of `.claude/agents/<agent>.md`, or None when there is no such file or
    it names no model or effort."""
    path = agent_file(root, agent)
    if path is None:
        return None
    with open(path, encoding="utf-8-sig") as handle:
        text = handle.read().replace("\r\n", "\n")
    end = text.find("\n---", 4) if text.startswith("---\n") else -1
    found = dict(FRONT_MATTER_KEY.findall(text[4:end])) if end > 0 else {}
    return f"{found['model']}/{found['effort']}" if "model" in found and "effort" in found else None


def amend_last(path, member, launch, replace=False, step=None):
    """Add the launch trailer to the last row of `member` (with `step`, to its last row of that step); the amended
    row, or None when no such row exists or it already carries a trailer. With `replace`, that trailer is
    replaced instead and a row without one is left alone (None)."""
    with open(path, "rb") as handle:
        data = handle.read()
    eol = b"\r\n" if b"\r\n" in data else b"\n"
    lines = data.decode("utf-8-sig").split(eol.decode())
    wanted, wanted_step = cell(member), None if step is None else cell(step)
    for index in range(len(lines) - 1, -1, -1):
        match = ROW.match(lines[index])
        if not match or match.group("member") != wanted or (wanted_step is not None and match.group("step") != wanted_step):
            continue
        old = ANY_TRAILER.search(match.group("result"))
        if bool(old) != replace:
            return None
        result = match.group("result")[:old.start()] if old else match.group("result")
        lines[index] = f"| {match.group('date')} | {match.group('step')} | {match.group('member')} | " \
                       f"{result.rstrip()} {trailer(*launch)} |"
        with open(path, "wb") as handle:
            handle.write(eol.decode().join(lines).encode())
        return lines[index]
    return None


PLACEHOLDER_HEADING = b"# Log: <issue #number | feature name>"


def log_heading(path):
    """`issue #3` for `specs/issue-3/log.md`, `feature x` for `specs/feature-x/log.md`, else the folder name."""
    name = os.path.basename(os.path.dirname(os.path.abspath(path)))
    kind, _, rest = name.partition("-")
    if kind == "issue" and rest.isdigit():
        return f"issue #{rest}"
    return f"{kind} {rest}" if rest else name


def append_row(path, step, member, result, today=None, launch=None):
    with open(path, "rb") as handle:
        data = handle.read()
    if data.startswith(PLACEHOLDER_HEADING):
        data = f"# Log: {log_heading(path)}".encode() + data[len(PLACEHOLDER_HEADING):]
    eol = b"\r\n" if b"\r\n" in data else b"\n"
    date = today or datetime.datetime.now(datetime.UTC).strftime("%Y-%m-%d %H:%M")
    text = cell(result) if launch is None else f"{cell(result)} {trailer(*launch)}"
    row = f"| {date} | {cell(step)} | {cell(member)} | {text} |".encode()
    if data and not data.endswith((b"\n", b"\r\n")):
        data += eol
    with open(path, "wb") as handle:
        handle.write(data + row + eol)
    return row.decode()


def parse_rows(text):
    """(stamp, step, member, result) of every `| date [time] | step | member | result |` row; the title may contain
    escaped pipes."""
    rows = []
    for line in text.splitlines():
        cells = [c.strip() for c in re.split(r"(?<!\\)\|", line.strip())]
        if len(cells) >= 6 and not cells[0] and not cells[-1] and re.fullmatch(STAMP, cells[1]):
            rows.append((cells[1], cells[2], cells[3], "|".join(cells[4:-1])))
    return rows


def orchestrator(rows):
    """Markdown table: wall time from the first to the last row with a time of day, the part of it the launch
    trailers do not cover, and the gate runs (Orchestrator rows of step 7); None with fewer than two timed rows."""
    times = [datetime.datetime.strptime(stamp, "%Y-%m-%d %H:%M") for stamp, _, _, _ in rows if " " in stamp]
    if len(times) < 2:
        return None
    first, last = min(times), max(times)
    wall = int((last - first).total_seconds())
    covered = covered_seconds(rows, first, last)
    gates = sum(1 for _, step, member, _ in rows if member == "Orchestrator" and re.match(r"7\b", step))
    return "\n".join(["| Member | Wall time (s) | Not covered by launches (s) | Gate runs |",
                      "| ------ | ------------- | --------------------------- | --------- |",
                      f"| Orchestrator | {wall:,} | {max(wall - covered, 0):,} | {gates} |"])


def covered_seconds(rows, first, last):
    """Seconds between `first` and `last` during which at least one launch ran: each timed row with a trailer
    ends at its time of day and started its seconds earlier; the intervals are merged, so parallel launches
    count once."""
    intervals = []
    for stamp, _, _, result in rows:
        match = TRAILER.search(result)
        if " " not in stamp or not match:
            continue
        end = datetime.datetime.strptime(stamp, "%Y-%m-%d %H:%M")
        start = end - datetime.timedelta(seconds=metrics(match)[2])
        intervals.append((max(start, first), min(end, last)))
    covered, reach = 0, first
    for start, end in sorted(intervals):
        start = max(start, reach)
        if end > start:
            covered += int((end - start).total_seconds())
            reach = end
    return covered


def is_not_launch(member):
    """True for the Orchestrator, the Product Manager and the User, whose rows are never launches."""
    return member.strip().lower() in NOT_LAUNCHES


def missing_usage(rows):
    """(stamp, step, member) of every launch row — a member other than the Orchestrator, the Product Manager and
    the User — without a trailer; a launch marked as unreported has one."""
    return [(stamp, step, member) for stamp, step, member, result in rows
            if not is_not_launch(member) and not ANY_TRAILER.search(result)]


def unreported_usage(rows):
    """(step, member) of every launch row marked `usage not reported`."""
    return [(step, member) for _, step, member, result in rows if UNREPORTED_TRAILER.search(result)]


def summary(path):
    """Markdown table: launches, tokens, tool uses and seconds per member (and model/effort), with a total; then
    the orchestrator's table when the rows carry a time of day."""
    with open(path, encoding="utf-8-sig") as handle:
        rows = parse_rows(handle.read())
    per_member = {}
    for _, _, member, result in rows:
        match = TRAILER.search(result) or UNREPORTED_TRAILER.search(result)
        if not match:
            continue
        entry = per_member.setdefault((member, match.group("launch")), [0, 0, 0, 0, 0])
        entry[0] += 1
        if "tokens" not in match.groupdict():
            continue
        entry[4] += 1 if match.group("continued") else 0
        for index, value in enumerate(metrics(match), start=1):
            entry[index] += value
    lines = ["| Member | Model/effort | Launches | Tokens | Tool uses | Seconds |",
             "| ------ | ------------ | -------- | ------ | --------- | ------- |"]
    total = [0, 0, 0, 0, 0]
    for (member, launch), entry in sorted(per_member.items()):
        lines.append(f"| {member} | {launch} | {launches(entry[0], entry[4])} | {entry[1]:,} | {entry[2]} | "
                     f"{entry[3]} |")
        total = [a + b for a, b in zip(total, entry, strict=True)]
    lines.append(f"| **Total** | | {launches(total[0], total[4])} | {total[1]:,} | {total[2]} | {total[3]} |")
    own = orchestrator(rows)
    text = "\n".join(lines) + (f"\n\n{own}" if own else "")
    unreported = unreported_usage(rows)
    if unreported:
        names = ", ".join(f"`{step} · {member}`" for step, member in unreported)
        text += (f"\n\n**Without reported usage:** {len(unreported)} launch(es) whose task notification never "
                 f"arrived — counted in the launches, not in the totals: {names}")
    missing = missing_usage(rows)
    if missing:
        names = ", ".join(f"`{step} · {member}`" for _, step, member in missing)
        text += (f"\n\n**Incomplete:** {len(missing)} launch row(s) without usage — add it with `--amend-last` "
                 f"from the launch's task notification before posting this table: {names}")
    return text


def launches(count, continued):
    return f"{count} ({continued} continued)" if continued else str(count)


def build_parser():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("folder")
    parser.add_argument("step", nargs="?")
    parser.add_argument("member", nargs="?")
    parser.add_argument("result", nargs="?")
    parser.add_argument("--summary", action="store_true", help="print the per-member launch table instead of a row")
    parser.add_argument("--amend-last", metavar="MEMBER", help="add the launch trailer to the last row of this member")
    parser.add_argument("--replace-last", metavar="MEMBER", help="replace the launch trailer of the last row of "
                                                                 "this member (to correct wrong metrics)")
    parser.add_argument("--step", dest="at_step", metavar="STEP", help="with --amend-last or --replace-last: the "
                        "member's last row of this step instead of its last row (for a parallel launch whose "
                        "notification arrived after the next row was written)")
    parser.add_argument("--unreported", action="store_true",
                        help="with --amend-last: the launch's task notification never arrived; the trailer marks "
                             "it as unreported (with --agent or --launch, without numbers)")
    parser.add_argument("--agent", metavar="squad-ROLE", help="the launched agent; model and effort come from its file")
    parser.add_argument("--launch", metavar="MODEL/EFFORT", help="the launched subagent's model and effort (when "
                                                               "the launch overrode the agent file)")
    parser.add_argument("--tokens", type=int, help="subagent_tokens from the usage block of the task notification")
    parser.add_argument("--tool-uses", type=int, help="tool_uses from the usage block of the task notification")
    parser.add_argument("--seconds", type=int, help="duration of the launch in seconds (duration_ms / 1000)")
    parser.add_argument("--continued", action="store_true",
                        help="the launch continued the member's last fresh agent: the usage block reports the "
                             "agent's tokens since its first launch, the trailer gets this round's share")
    parser.add_argument("--continued-from", metavar="STEP",
                        help="like --continued, for the agent of the member's fresh launch whose step cell is STEP "
                             "(when a later fresh launch of the member exists)")
    return parser


def launch_metrics(parser, root, args):
    """(model/effort, tokens, tool uses, seconds) from the options, or None when no metric option is given; an
    incomplete set is a usage error, an unknown agent file ends the run with exit code 1."""
    if not (args.agent or args.launch or args.tokens is not None or args.tool_uses is not None
            or args.seconds is not None):
        return None
    model = args.launch or (agent_launch(root, args.agent) if args.agent else None)
    if args.agent and not args.launch and model is None:
        sys.exit(f"No .claude/agents/{args.agent}.md with a model and an effort")
    if args.unreported:
        if not model or args.tokens is not None or args.tool_uses is not None or args.seconds is not None \
                or args.continued or args.continued_from is not None:
            parser.error("--unreported goes with --agent (or --launch) alone: no --tokens, --tool-uses, --seconds "
                         "or --continued")
        return (model, None, None, None)
    if not model or None in (args.tokens, args.tool_uses, args.seconds):
        parser.error("--agent (or --launch), --tokens, --tool-uses and --seconds go together; a launch whose "
                     "notification has not arrived yet is logged without any metric option (no --agent either) "
                     "and completed with --amend-last")
    if args.tokens < MIN_TOKENS or args.tool_uses < 0 or args.seconds < 0:
        parser.error(f"--tokens must be at least {MIN_TOKENS:,} and --tool-uses and --seconds not negative; a launch "
                     "whose notification has not arrived yet is logged without any metric option and completed with "
                     "--amend-last")
    return (model, args.tokens, args.tool_uses, args.seconds)


def share_of_round(path, member, launch, continued, replace=False, step=None, origin=None):
    """The launch as logged: with `continued`, this round's share of the reported totals (with `origin`, of the
    agent of the member's fresh launch of that step); None when there is no earlier usage to subtract (reported
    to the user, exit code 1). With `replace`, the trailer being replaced is not part of the earlier usage; with
    `step`, only the rows before the amended one are."""
    if launch is None or not continued:
        return launch
    share = continued_launch(path, member, launch, skip_last=replace, step=step, origin=origin)
    if share is None and origin is not None:
        print(f"--continued-from: no fresh launch of {member} with usage at step '{origin}'")
    elif share is None:
        print(f"--continued: no earlier row of {member} with usage to continue from")
    return share


def amend_command(parser, path, folder, member, launch, replace, continued, step=None, origin=None):
    option = "--replace-last" if replace else "--amend-last"
    if launch is None:
        parser.error(f"{option} needs --agent (or --launch), --tokens, --tool-uses and --seconds (or --unreported)")
    if replace and launch[1] is None:
        parser.error("--unreported marks a row without a trailer: use it with --amend-last")
    launch = share_of_round(path, member, launch, continued, replace, step, origin)
    if launch is None:
        return 1
    row = amend_last(path, member, launch, replace=replace, step=step)
    if row is None:
        state = "with" if replace else "without"
        where = f"of step '{step}' " if step is not None else ""
        hint = "" if replace else " (a row that already carries one, also the unreported mark, is corrected " \
                                  "with --replace-last)"
        print(f"No row of {member} {where}{state} a launch trailer in {folder}{hint}")
        return 1
    print(row)
    return 0


def row_command(parser, path, args, launch):
    if args.step is None or args.member is None or args.result is None:
        parser.error("a row needs <step> <member> <result> (or --summary, --amend-last or --replace-last)")
    if TWO_MEMBERS.search(args.member):
        parser.error(f"one row per launch: '{args.member}' names more than one member; write a row for each, "
                     "also when they ran in parallel")
    if args.continued and launch is None:
        parser.error("--continued goes with the launch's usage (--agent or --launch, --tokens, --tool-uses, "
                     "--seconds), on the row or with --amend-last")
    if args.at_step is not None or args.unreported:
        parser.error("--step and --unreported go with --amend-last or --replace-last")
    launch = share_of_round(path, args.member, launch, args.continued, origin=args.continued_from)
    if args.continued and launch is None:
        return 1
    print(append_row(path, args.step, args.member, args.result, launch=launch))
    return 0


def main():
    parser = build_parser()
    args = parser.parse_args()
    toplevel = subprocess.run(["git", "rev-parse", "--show-toplevel"], capture_output=True, text=True, check=False)
    root = toplevel.stdout.strip() if toplevel.returncode == 0 else os.getcwd()
    path = resolve_log(root, args.folder)
    if path is None:
        print(f"No log.md in {args.folder} (create it from specs/_template/log.md first)")
        return 1
    if args.summary:
        print(summary(path))
        with open(path, encoding="utf-8-sig") as handle:
            return 3 if missing_usage(parse_rows(handle.read())) else 0
    target = args.amend_last or args.replace_last or args.member
    uses_metrics = args.amend_last or args.replace_last or args.unreported or args.agent or args.launch \
        or args.tokens is not None or args.tool_uses is not None or args.seconds is not None or args.continued \
        or args.continued_from is not None
    if target is not None and is_not_launch(target) and uses_metrics:
        parser.error(f"'{target}' is no launch: its rows are written without any metric option and never "
                     "amended, and the summary asks no usage for them")
    launch = launch_metrics(parser, root, args)
    args.continued = args.continued or args.continued_from is not None
    if args.amend_last and args.replace_last:
        parser.error("--amend-last and --replace-last exclude each other")
    if args.amend_last or args.replace_last:
        return amend_command(parser, path, args.folder, args.amend_last or args.replace_last, launch,
                             replace=bool(args.replace_last), continued=args.continued, step=args.at_step,
                             origin=args.continued_from)
    return row_command(parser, path, args, launch)


if __name__ == "__main__":
    sys.exit(main())
