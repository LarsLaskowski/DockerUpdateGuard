#!/usr/bin/env python3
"""Scope gate for a product change: what a squad or `create-pr` change may touch, checked against the diff
instead of remembered by a reviewer.

Checks, over every file changed since the merge base with BASE_REF (working tree and untracked files
included; BASE_REF from `.squad/tools/squad_settings.py`, default origin/main):

- no squad or instruction file is changed: `.squad/` except `stack.md` and `project.md`, `.claude/`,
  `AGENTS.md`, `CLAUDE.md`, `.github/copilot-instructions.md` (`.squad/routing.md`, *Scope of a product PR*; squad
  lessons become issues);
- a marked file (`docs/CONTRIBUTING.md`, `docs/ARCHITECTURE.md`, the bug report and pull request templates,
  the decision and area index) is changed only inside its `<!-- project:… -->` blocks;
- no changed Markdown file contains a control character (Unicode categories Cc, Cf, Zl, Zp other than tab
  and line break);
- with `--no-specs` (after squad step 10, and in every review after the PR is open): no working record
  under `specs/` is left in the diff, only `specs/README.md` and `specs/_template/` may change;
- with `--tier docs`: every changed file is product documentation as the `docs` tier defines it
  (`README.md`, `SECURITY.md`, `docs/` except `docs/decisions/` and `docs/areas/`, `.squad/stack.md`,
  `.squad/project.md`, the bug report and pull request templates) or a working record under `specs/`;
- with `--tier patch`: the files outside `specs/` change by at most PATCH_MAX_LINES lines in all (added plus
  removed, an untracked file with every line; default 30, `squad_settings.py`), and no binary file changes.

When BASE_REF is not an ancestor of HEAD (a stale local copy in a fresh session, or a base that moved on), the
script prints a warning above the result: the file list may then include changes that are not this branch's, so
fetch the base (`git fetch origin main`) and merge it first. The warning does not change the exit code.

The script takes only these two options, so nothing user-supplied reaches git or the filesystem.

Usage, from the repository root:
    python3 .squad/tools/scope-check.py [--tier docs|patch|trivial|standard|security] [--no-specs]

Exit code 0 when the diff is in scope, 1 otherwise.
"""
import argparse
import os
import re
import subprocess
import sys
import unicodedata

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from analyzer_common import BASE_REF, git, merge_base, settings  # noqa: E402  (shared helpers next to this script)

SQUAD_FILES_ALLOWED = {".squad/stack.md", ".squad/project.md"}
INSTRUCTION_FILES = {"AGENTS.md", "CLAUDE.md", ".github/copilot-instructions.md"}
MARKED = ["docs/CONTRIBUTING.md", "docs/ARCHITECTURE.md", ".github/ISSUE_TEMPLATE/bug_report.md",
          ".github/pull_request_template.md", "docs/decisions/README.md", "docs/areas/README.md"]
DOCS_TIER = ["README.md", "SECURITY.md", ".squad/stack.md", ".squad/project.md",
             ".github/ISSUE_TEMPLATE/bug_report.md", ".github/pull_request_template.md"]
DOCS_TIER_DIRS = ["docs/"]
DOCS_TIER_EXCLUDED = ["docs/decisions/", "docs/areas/"]
PROJECT_BLOCK = re.compile(r"<!-- project:begin ([\w-]+) -->\n.*?<!-- project:end \1 -->\n?", re.S)
CONTROL_CATEGORIES = {"Cc", "Cf", "Zl", "Zp"}
KEEP = {"\t", "\n", "\r"}
PATCH_MAX_LINES = getattr(settings, "PATCH_MAX_LINES", 30)


def changed_files(base):
    names = git("diff", "--name-only", base).splitlines()
    names += git("ls-files", "--others", "--exclude-standard").splitlines()
    return sorted({n.strip() for n in names if n.strip()})


def is_squad_file(path):
    if path in SQUAD_FILES_ALLOWED:
        return False
    return path.startswith((".squad/", ".claude/")) or path in INSTRUCTION_FILES


def is_working_record(path):
    return path.startswith("specs/") and path != "specs/README.md" and not path.startswith("specs/_template/")


def is_docs_tier(path):
    if path in DOCS_TIER or is_working_record(path):
        return True
    return (path.endswith(".md") and path.startswith(tuple(DOCS_TIER_DIRS))
            and not path.startswith(tuple(DOCS_TIER_EXCLUDED)))


def outside_blocks(text):
    return PROJECT_BLOCK.sub("", text.replace("\r\n", "\n"))


def read_text(path):
    with open(path, encoding="utf-8-sig", errors="replace") as handle:
        return handle.read()


def marked_file_changed_outside_blocks(path, base):
    before = subprocess.run(["git", "show", f"{base}:{path}"], capture_output=True, text=True, encoding="utf-8",
                            errors="replace", check=False)
    if before.returncode != 0 or not os.path.isfile(path):
        return False  # added or deleted: a different kind of finding, not an edit outside the blocks
    return outside_blocks(before.stdout.lstrip("﻿")) != outside_blocks(read_text(path))


def control_characters(path):
    text = read_text(path)
    return sorted({f"U+{ord(ch):04X}" for ch in text if unicodedata.category(ch) in CONTROL_CATEGORIES
                   and ch not in KEEP})


def control_character_error(path):
    if not path.endswith(".md") or not os.path.isfile(path):
        return None
    found = control_characters(path)
    return f"{path}: control characters {', '.join(found)} (write them as text escapes)" if found else None


def file_errors(path, base, tier, no_specs):
    """Every scope finding for one changed file."""
    findings = [
        (is_squad_file(path), f"{path}: squad or instruction file changed in a product change "
                              "(file a squad issue instead, .squad/routing.md *Squad lessons*)"),
        (path in MARKED and marked_file_changed_outside_blocks(path, base),
         f"{path}: changed outside its <!-- project:… --> blocks (the rest is template-managed)"),
        (no_specs and is_working_record(path),
         f"{path}: working record still in the diff (squad step 10 removes specs/<folder>/)"),
        (tier == "docs" and not is_docs_tier(path),
         f"{path}: not product documentation - tier docs allows no such file (raise the tier)"),
    ]
    errors = [message for applies, message in findings if applies]
    control = control_character_error(path)
    return errors + ([control] if control else [])


def patch_errors(base):
    """The `patch` tier's budget: lines added plus removed outside `specs/` (an untracked file counts every line),
    and no binary file."""
    errors, lines = [], 0
    for row in git("diff", "--numstat", base).splitlines():
        added, removed, path = row.split("\t", 2)
        if is_working_record(path):
            continue
        if added == "-":
            errors.append(f"{path}: binary file changed - tier patch allows only text edits (raise the tier)")
            continue
        lines += int(added) + int(removed)
    for path in git("ls-files", "--others", "--exclude-standard").splitlines():
        if path.strip() and not is_working_record(path) and os.path.isfile(path):
            lines += len(read_text(path).splitlines())
    if lines > PATCH_MAX_LINES:
        errors.append(f"{lines} changed lines outside specs/: tier patch allows at most {PATCH_MAX_LINES} "
                      "(raise the tier)")
    return errors


def check(files, base, tier, no_specs):
    errors = [error for path in files for error in file_errors(path, base, tier, no_specs)]
    return errors + (patch_errors(base) if tier == "patch" else [])


def base_is_stale():
    """True when BASE_REF is not an ancestor of HEAD, so the diff against the merge base may show foreign files."""
    result = subprocess.run(["git", "merge-base", "--is-ancestor", BASE_REF, "HEAD"], capture_output=True, check=False)
    return result.returncode == 1


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--tier", choices=["docs", "patch", "trivial", "standard", "security"])
    parser.add_argument("--no-specs", action="store_true", help="no working record may be left under specs/")
    args = parser.parse_args()
    os.chdir(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
    base = merge_base()
    files = changed_files(base)
    errors = check(files, base, args.tier, args.no_specs)
    if base_is_stale():
        print(f"WARNING: {BASE_REF} is not an ancestor of HEAD: fetch it (git fetch origin main) and merge it, or "
              "the changed files below may include changes of the base branch")
    for error in errors:
        print(f"ERROR: {error}")
    print(f"\nChecked {len(files)} changed files: {'PASS' if not errors else 'FAIL'}")
    return 0 if not errors else 1


if __name__ == "__main__":
    sys.exit(main())
