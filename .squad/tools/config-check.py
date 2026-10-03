#!/usr/bin/env python3
"""Config gate for the squad: agent and skill definitions must load, mirrors must match, and the
per-repository squad files must exist and be filled in.

Claude Code silently drops an agent or skill whose YAML front matter does not parse (for example an
unquoted description containing ": "), so a broken file only shows up when a squad run tries to launch
it. This script checks, without arguments:

- every `.claude/agents/*.md` and every `SKILL.md` under `.claude/skills/`, `.agents/skills/` and
  `.github/skills/` has front matter that parses as YAML, with a non-empty `name` and `description`;
- an agent's `name` equals its file name, a skill's `name` equals its folder name;
- the three skill folders contain the same skills with identical content;
- `CLAUDE.md`, `AGENTS.md` and `.github/copilot-instructions.md` are identical from their first `## `
  heading on (only the title and introduction may differ);
- `.squad/template.json` names the template repository (where lessons about template-managed files are
  filed);
- `.squad/stack.md`, `.squad/project.md` and `.squad/tools/squad_settings.py` exist, and no file the
  template seeded or rebuilt still contains a template placeholder (`{{TODO: …}}` — a marker that
  ordinary Go templates, `docker --format` strings or GitHub Actions expressions never contain).

Usage, from anywhere inside the repository:
    python3 .squad/tools/config-check.py

Exit code 0 when everything is valid, 1 otherwise. Requires PyYAML (`pip install pyyaml`).
"""
import glob
import json
import os
import re
import sys

CLAUDE_DIR = ".claude"
GITHUB_DIR = ".github"
SQUAD_DIR = ".squad"
AGENTS_DIR = os.path.join(CLAUDE_DIR, "agents")
SKILL_ROOTS = [os.path.join(CLAUDE_DIR, "skills"), os.path.join(".agents", "skills"), os.path.join(GITHUB_DIR, "skills")]
INSTRUCTION_FILES = ["CLAUDE.md", "AGENTS.md", os.path.join(GITHUB_DIR, "copilot-instructions.md")]
REQUIRED_FILES = [os.path.join(SQUAD_DIR, "stack.md"), os.path.join(SQUAD_DIR, "project.md"),
                  os.path.join(SQUAD_DIR, "tools", "squad_settings.py")]
# No quantifier overlaps another one, so matching stays linear (no backtracking).
PLACEHOLDER = re.compile(r"\{\{TODO:([^{}]*)\}\}")
PLACEHOLDER_GLOBS = INSTRUCTION_FILES + REQUIRED_FILES + [
    "SECURITY.md", "sonar-project.properties",
    os.path.join(SQUAD_DIR, "**", "*.md"), os.path.join("docs", "**", "*.md"),
    os.path.join(GITHUB_DIR, "**", "*.md"), os.path.join(GITHUB_DIR, "**", "*.yml"),
]

try:
    import yaml
except ImportError:
    sys.exit("PyYAML is required: pip install pyyaml")


def read_text(path):
    with open(path, encoding="utf-8-sig") as handle:
        return handle.read().replace("\r\n", "\n")


def front_matter(path):
    text = read_text(path)
    if not text.startswith("---\n"):
        raise ValueError("no front matter")
    end = text.find("\n---", 4)
    if end < 0:
        raise ValueError("unterminated front matter")
    data = yaml.safe_load(text[4:end])
    if not isinstance(data, dict):
        raise ValueError("front matter is not a mapping")
    return data


def check(path, expected_name, errors):
    try:
        data = front_matter(path)
    except (ValueError, yaml.YAMLError) as error:
        errors.append(f"{path}: {str(error).splitlines()[0]}")
        return
    for key in ("name", "description"):
        if not str(data.get(key) or "").strip():
            errors.append(f"{path}: missing '{key}'")
    if data.get("name") and data["name"] != expected_name:
        errors.append(f"{path}: name '{data['name']}' does not match '{expected_name}'")


def check_skills(errors):
    skills = {}
    for root in SKILL_ROOTS:
        found = {}
        for path in sorted(glob.glob(os.path.join(root, "*", "SKILL.md"))):
            name = os.path.basename(os.path.dirname(path))
            check(path, name, errors)
            with open(path, "rb") as handle:
                found[name] = handle.read().replace(b"\r\n", b"\n")
        skills[root] = found
    reference = skills[SKILL_ROOTS[0]]
    for root in SKILL_ROOTS[1:]:
        other = skills[root]
        for name in sorted(set(reference) | set(other)):
            if name not in reference or name not in other:
                errors.append(f"skill '{name}' exists in only one of {SKILL_ROOTS[0]} and {root}")
            elif reference[name] != other[name]:
                errors.append(f"skill '{name}' differs between {SKILL_ROOTS[0]} and {root}")
    return reference


def body(path):
    text = read_text(path)
    start = text.find("\n## ")
    return text[start:] if start >= 0 else ""


def check_instructions(errors):
    missing = [path for path in INSTRUCTION_FILES if not os.path.isfile(path)]
    for path in missing:
        errors.append(f"{path} is missing")
    present = [path for path in INSTRUCTION_FILES if path not in missing]
    if len(present) < 2:
        return
    reference = body(present[0])
    for path in present[1:]:
        if body(path) != reference:
            errors.append(f"{path} differs from {present[0]} after the first '## ' heading")


def check_template_record(errors):
    path = os.path.join(SQUAD_DIR, "template.json")
    try:
        with open(path, encoding="utf-8") as handle:
            record = json.load(handle)
    except (OSError, ValueError) as error:
        errors.append(f"{path}: {error} (written by adopt-template)")
        return
    if not isinstance(record, dict) or not str(record.get("repository") or "").strip():
        errors.append(f"{path}: no 'repository' - refresh the squad with adopt-template")


def check_project_files(errors):
    for path in REQUIRED_FILES:
        if not os.path.isfile(path):
            errors.append(f"{path} is missing (seeded by adopt-template)")
    paths = sorted({p for pattern in PLACEHOLDER_GLOBS for p in glob.glob(pattern, recursive=True)
                    if os.path.isfile(p)})
    for path in paths:
        text = read_text(path)
        for match in PLACEHOLDER.finditer(text):
            line = text.count("\n", 0, match.start()) + 1
            first = " ".join(match.group(1).split())[:60]
            errors.append(f"{path}:{line}: template placeholder '{{{{TODO: {first}}}}}' not filled in")


def main():
    # Resolve paths from the repository root, whatever the current directory is.
    os.chdir(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
    errors = []
    agents = sorted(glob.glob(os.path.join(AGENTS_DIR, "*.md")))
    for path in agents:
        check(path, os.path.splitext(os.path.basename(path))[0], errors)
    skills = check_skills(errors)
    check_instructions(errors)
    check_template_record(errors)
    check_project_files(errors)

    if not agents or not skills:
        errors.append("no agents or skills found - has the squad been adopted in this repository?")
    for error in errors:
        print(error)
    print(f"\nChecked {len(agents)} agents and {len(skills)} skills: {'PASS' if not errors else 'FAIL'}")
    return 0 if not errors else 1


if __name__ == "__main__":
    sys.exit(main())
