#!/usr/bin/env python3
"""Dotnet rules: the analyzer rules every .NET repository of the template shares, written into `.editorconfig`.

The template keeps one rule baseline for its .NET repositories, the stricter setting of the repositories winning:
`.squad/dotnet-rules.editorconfig`, template-managed. This script writes it into two marked blocks of `.editorconfig`:

    [*.cs]
    # squad:dotnet-rules begin — …
    csharp_style_namespace_declarations = file_scoped:error
    dotnet_diagnostic.RH3001.severity = error
    # squad:dotnet-rules end

    # squad:sonar-rules begin — …       (sonar-rules.py)
    # squad:sonar-rules end

    # squad:dotnet-rules-paths begin — …
    [**/*.Tests/**.cs]
    dotnet_diagnostic.S107.severity = none
    # squad:dotnet-rules-paths end

- the first block holds the baseline's settings for every C# file, right before the sonar-rules block (so
  `sonar-rules.py` leaves the rules it sets to it, and it comes after the file's own settings) or at the end of the
  first `[*.cs]` section; the second holds the baseline's sections of paths at the end of the file, where they win
  over both blocks;
- a rule in `DOTNET_RULES_PENDING` of `.squad/tools/squad_settings.py` (`{"S3776": "#123"}`: a rule id, or a
  code-style option, to the issue that brings the code up to it) is written as `suggestion` instead, with the issue
  above it: the *Analyzer gate* still reports it in every changed file, while code no change touches keeps building.
  An entry without an issue is refused;
- a block below the header of a section that does not apply to every C# file is refused, since its settings would
  reach those files only; so is a paths block before the other blocks.

The project's own sections of paths (generated code, migrations) go after the blocks; a setting for every C# file
outside them is the baseline's to make, not the project's.

`adopt-template` runs it on adoption and on every refresh; `--check` reports blocks that no longer match the baseline
or the pending list.

Usage, from the repository root:
    python3 .squad/tools/dotnet-rules.py [--check]

Exit code 0 when the blocks were written or are current, 1 when `--check` finds them missing or out of date, or the
file's layout, the pending list or the baseline is refused, 4 wrong usage.
"""
import argparse
import importlib.util
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from analyzer_common import settings  # noqa: E402  (shared helpers next to this script)

CURRENT, REFUSED, USAGE = 0, 1, 4
EDITORCONFIG = ".editorconfig"
BASELINE = os.path.join(os.path.dirname(HERE), "dotnet-rules.editorconfig")
BEGIN = "# squad:dotnet-rules begin"
END = "# squad:dotnet-rules end"
PATHS_BEGIN = "# squad:dotnet-rules-paths begin"
PATHS_END = "# squad:dotnet-rules-paths end"
SONAR_BEGIN = "# squad:sonar-rules begin"
SONAR_END = "# squad:sonar-rules end"
NOTE = (" — written by .squad/tools/dotnet-rules.py from .squad/dotnet-rules.editorconfig, the template's rule baseline; "
        "never edited by hand (.squad/stack.md, Analyzer gate)")
PENDING = getattr(settings, "DOTNET_RULES_PENDING", {})
PENDING_SEVERITY = "suggestion"
SEVERITIES = ("none", "silent", "suggestion", "warning", "error")
RULE = re.compile(r"[A-Z]+\d+")
OPTION = re.compile(r"[a-z][a-z0-9_.]*")
LOWER_RULE = re.compile(r"[a-z]+\d+")
ISSUE = re.compile(r"(?:[\w.-]+/[\w.-]+)?#\d+")


class Usage(argparse.ArgumentParser):
    def error(self, message):
        self.print_usage(sys.stderr)
        self.exit(USAGE, f"{self.prog}: error: {message}\n")


def sonar_rules():
    """`sonar-rules.py` next to this script as a module: its reading of EditorConfig sections."""
    spec = importlib.util.spec_from_file_location("sonar_rules", os.path.join(HERE, "sonar-rules.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


SECTIONS = sonar_rules()


def read_file(path):
    if not os.path.isfile(path):
        return ""
    with open(path, encoding="utf-8", newline="") as handle:
        return handle.read()


# --- the baseline and the pending list ----------------------------------------------------------------------------

def trimmed(lines):
    """The lines without blank lines at either end."""
    while lines and not lines[0].strip():
        lines = lines[1:]
    while lines and not lines[-1].strip():
        lines = lines[:-1]
    return lines


def baseline_sections(text):
    """[(glob, lines)] of the baseline, comments kept; the comment at the top of the file is left out."""
    sections = []
    for line in text.splitlines():
        found = SECTIONS.SECTION.match(line)
        if found:
            sections.append((found.group("glob").strip(), []))
        elif sections:
            sections[-1][1].append(line)
    return [(glob, trimmed(lines)) for glob, lines in sections]


def pending_errors(pending):
    """Why the pending list is refused: an entry that is no rule id or option, or names no issue."""
    if not isinstance(pending, dict):
        return ["DOTNET_RULES_PENDING must be a dict of rule id (or code-style option) to issue, such as {\"S3776\": \"#123\"}"]
    return [f"DOTNET_RULES_PENDING: {name!r} -> {issue!r} needs a rule id (S3776) or option and an issue (#123)"
            for name, issue in pending.items()
            if not (isinstance(name, str) and (RULE.fullmatch(name) or OPTION.fullmatch(name)))
            or (isinstance(name, str) and LOWER_RULE.fullmatch(name))
            or not (isinstance(issue, str) and ISSUE.fullmatch(issue.strip()))]


def setting_key(name):
    """The `.editorconfig` key a pending entry stands for."""
    return f"dotnet_diagnostic.{name}.severity" if RULE.fullmatch(name) else name


def lowered(value):
    """A value with its severity at most PENDING_SEVERITY: `error` -> `suggestion`, `true:error` -> `true:suggestion`."""
    head, colon, severity = value.rpartition(":")
    word = (severity if colon else value).strip().lower()
    if word not in SEVERITIES or SEVERITIES.index(word) <= SEVERITIES.index(PENDING_SEVERITY):
        return value
    return f"{head}:{PENDING_SEVERITY}" if colon else PENDING_SEVERITY


def with_pending(lines, pending):
    """The block's lines with every pending setting lowered and its issue above it, and the pending entries the
    baseline does not set (a rule of the SonarQube profile is sonar-rules.py's)."""
    keys = {setting_key(name).lower(): name for name in pending}
    out, used = [], set()
    for line in lines:
        key, equals, value = line.partition("=")
        name = keys.get(key.strip().lower()) if equals and not line.lstrip().startswith(("#", ";")) else None
        if name:
            used.add(name)
            out += [f"# pending: {pending[name].strip()} (DOTNET_RULES_PENDING)", f"{key.strip()} = {lowered(value.strip())}"]
        else:
            out.append(line)
    return out, sorted(set(pending) - used)


# --- the blocks in .editorconfig ----------------------------------------------------------------------------------

def find(lines, begin, end):
    """(index of the begin marker, index of the end marker) of a block, or None."""
    start = next((i for i, line in enumerate(lines) if line.startswith(begin)), None)
    stop = next((i for i, line in enumerate(lines) if line.startswith(end)), None)
    return None if start is None or stop is None or stop < start else (start, stop)


def section_above(lines, index):
    """The section header nearest above a line, or None."""
    return next((line for line in reversed(lines[:index]) if SECTIONS.SECTION.match(line)), None)


def layout_errors(lines):
    """Why the file's layout is refused: a block under a section that does not apply to every C# file alone, or the
    paths block before the others."""
    errors = []
    block, paths, sonar = find(lines, BEGIN, END), find(lines, PATHS_BEGIN, PATHS_END), find(lines, SONAR_BEGIN, SONAR_END)
    if block and not SECTIONS.cs_section(section_above(lines, block[0]) or ""):
        errors.append(f"the dotnet-rules block stands under {section_above(lines, block[0])}, not a section of every C# "
                      "file: move it into the first [*.cs] section")
    for other, name in ((block, "dotnet-rules"), (sonar, "sonar-rules")):
        if paths and other and paths[0] < other[1]:
            errors.append(f"the dotnet-rules-paths block comes before the {name} block: move it to the end of the file")
    return errors


def block_position(lines):
    """Where a new dotnet-rules block goes: right before the sonar-rules block when that stands in a section of every
    C# file, so the block comes after the file's own settings for every C# file and wins over them; else at the end
    of the first [*.cs] section; None without one."""
    sonar = find(lines, SONAR_BEGIN, SONAR_END)
    if sonar and SECTIONS.cs_section(section_above(lines, sonar[0]) or ""):
        return sonar[0]
    return SECTIONS.insert_at(lines)


def put_block(lines, markers, body, position):
    """`lines` with the block between `markers` replaced by `body`, or `body` inserted at `position(lines)` (the end
    of the file when that is None) after a blank line (none right below a section header), and one after it when more
    follows."""
    found = find(lines, *markers)
    if found:
        return lines[:found[0]] + body + lines[found[1] + 1:]
    at = position(lines)
    at = len(lines) if at is None else at
    separate = at and lines[at - 1].strip() and not SECTIONS.SECTION.match(lines[at - 1])
    head = lines[:at] + ([""] if separate else [])
    tail = lines[at:]
    return head + body + ([""] if tail and tail[0].strip() else []) + tail


def render(text, general, paths):
    """The file's text with both blocks."""
    lines = text.splitlines()
    if find(lines, BEGIN, END) is None and SECTIONS.insert_at(lines) is None:
        lines = lines + ([""] if lines and lines[-1].strip() else []) + ["[*.cs]"]
    lines = put_block(lines, (BEGIN, END), [BEGIN + NOTE, *general, END], block_position)
    path_lines = [line for glob, body in paths for line in ["", f"[{glob}]", *body]]
    lines = put_block(lines, (PATHS_BEGIN, PATHS_END), [PATHS_BEGIN + NOTE, *path_lines, PATHS_END], lambda _: None)
    newline = "\r\n" if "\r\n" in text else "\n"
    final = text.endswith(("\n", "\r")) or not text
    return newline.join(lines) + (newline if final else "")


def wanted(baseline, pending):
    """(the general block's lines, [(glob, lines)] of the paths block, pending entries the baseline does not set)."""
    sections = baseline_sections(baseline)
    general = next((lines for glob, lines in sections if glob == "*.cs"), [])
    general, unused = with_pending(general, pending)
    paths = []
    for glob, lines in sections:
        if glob != "*.cs":
            lowered_lines, rest = with_pending(lines, pending)
            paths.append((glob, lowered_lines))
            unused = sorted(set(unused) & set(rest))
    return general, paths, unused


def switched_off(baseline, pending):
    """A pending entry for a rule the baseline switches off makes no sense: S1125 stays off."""
    general = next((lines for glob, lines in baseline_sections(baseline) if glob == "*.cs"), [])
    values = {key.strip().lower(): value.strip().lower()
              for key, equals, value in (line.partition("=") for line in general)
              if equals and not line_is_comment(key)}
    return [f"DOTNET_RULES_PENDING: {name} is off in the baseline ({values[setting_key(name).lower()]}); remove the entry"
            for name in pending if isinstance(name, str)
            and values.get(setting_key(name).lower()) in ("none", "silent")]


def line_is_comment(text):
    return text.lstrip().startswith(("#", ";"))


def refusals(text, baseline, pending):
    """Every reason to write nothing."""
    errors = pending_errors(pending)
    errors += switched_off(baseline, pending) if not errors else []
    if not baseline_sections(baseline):
        errors.append(f"{os.path.relpath(BASELINE)} is missing or holds no section: refresh the template")
    return errors + layout_errors(text.splitlines())


def main(argv=None):
    parser = Usage(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--check", action="store_true", help="report missing or outdated blocks, write nothing")
    options = parser.parse_args(argv)
    text, baseline = read_file(EDITORCONFIG), read_file(BASELINE)
    errors = refusals(text, baseline, PENDING)
    for error in errors:
        print(f"REFUSED: {error}")
    if errors:
        return REFUSED
    general, paths, unused = wanted(baseline, PENDING)
    for name in sorted(PENDING):
        print(f"Pending: {name} at {PENDING_SEVERITY} until {PENDING[name].strip()}"
              + (" (not in the baseline; sonar-rules.py lowers it if its profile has it)" if name in unused else ""))
    updated = render(text, general, paths)
    if updated == text:
        print(f"{EDITORCONFIG}: the dotnet-rules blocks are current")
        return CURRENT
    if options.check:
        print(f"{EDITORCONFIG}: the dotnet-rules blocks are missing or out of date — run python3 .squad/tools/dotnet-rules.py")
        return REFUSED
    with open(EDITORCONFIG, "w", encoding="utf-8", newline="") as handle:
        handle.write(updated)
    print(f"{EDITORCONFIG}: the dotnet-rules blocks written")
    return CURRENT


if __name__ == "__main__":
    sys.exit(main())
