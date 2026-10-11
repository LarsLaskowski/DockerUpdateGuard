#!/usr/bin/env python3
"""Config gate for the squad: agent and skill definitions must load, and the per-repository squad files
must exist and be filled in.

Claude Code silently drops an agent or skill whose YAML front matter does not parse (for example an
unquoted description containing ": "), so a broken file only shows up when a squad run tries to launch
it. This script checks, without arguments:

- every `.claude/agents/*.md` and every `SKILL.md` under `.claude/skills/` has front matter that parses as
  YAML, with a non-empty `name` and `description`;
- an agent's `name` equals its file name, a skill's `name` equals its folder name;
- every squad agent (`squad-*.md`) declares the `PreToolUse` hook `.claude/hooks/git-guard.py` for `Bash`, and
  that hook exists: it keeps Git and GitHub write operations out of the members' hands; it also names its
  `model` as an alias (`haiku`, `sonnet`, `opus`) and an explicit `effort`;
- `CLAUDE.md` exists;
- `.squad/template.json` names the template repository (where lessons about template-managed files are
  filed) and, for a repository with several stack profiles, lists them in `profiles` (the first one is
  `profile`); then every profile has its `analyzer-check-<profile>.py` and `session-start-<profile>.sh`
  next to the dispatchers, and `squad_settings.py` lists at least one `COVERAGE_REPORTS` entry per profile;
- `.squad/tools/decision-check.py` passes (decision records consistent, released records frozen);
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
import subprocess
import sys

CLAUDE_DIR = ".claude"
GITHUB_DIR = ".github"
SQUAD_DIR = ".squad"
AGENTS_DIR = os.path.join(CLAUDE_DIR, "agents")
GIT_GUARD = os.path.join(CLAUDE_DIR, "hooks", "git-guard.py")
MODELS = ("haiku", "sonnet", "opus")
EFFORTS = ("low", "medium", "high", "xhigh", "max")
SKILLS_DIR = os.path.join(CLAUDE_DIR, "skills")
INSTRUCTION_FILES = ["CLAUDE.md"]
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


def check(path, expected_name, errors, agent=False):
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
    if agent and expected_name.startswith("squad-"):
        check_squad_agent(path, data, errors)


def check_squad_agent(path, data, errors):
    """A squad member declares the git-guard hook, an alias as model and an explicit effort."""
    if not declares_git_guard(data):
        errors.append(f"{path}: no PreToolUse hook for Bash running {GIT_GUARD} (squad members never run Git writes)")
    if str(data.get("model") or "") not in MODELS:
        errors.append(f"{path}: 'model' must be one of {', '.join(MODELS)} (an alias, so a model change rolls out "
                      "with the template)")
    if str(data.get("effort") or "") not in EFFORTS:
        errors.append(f"{path}: 'effort' must be set to one of {', '.join(EFFORTS)}")


def declares_git_guard(data):
    """True when the front matter's hooks.PreToolUse has a Bash matcher with a command that runs git-guard.py."""
    groups = (data.get("hooks") or {}).get("PreToolUse") if isinstance(data.get("hooks"), dict) else None
    for group in groups or []:
        if not isinstance(group, dict) or "Bash" not in str(group.get("matcher", "")):
            continue
        for hook in group.get("hooks") or []:
            if isinstance(hook, dict) and os.path.basename(GIT_GUARD) in str(hook.get("command", "")):
                return True
    return False


def check_skills(errors):
    skills = []
    for path in sorted(glob.glob(os.path.join(SKILLS_DIR, "*", "SKILL.md"))):
        name = os.path.basename(os.path.dirname(path))
        check(path, name, errors)
        skills.append(name)
    return skills


def check_instructions(errors):
    for path in INSTRUCTION_FILES:
        if not os.path.isfile(path):
            errors.append(f"{path} is missing")


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
        return
    check_profiles(record, errors)


def check_profiles(record, errors):
    path = os.path.join(SQUAD_DIR, "template.json")
    profiles = record.get("profiles")
    if profiles is None:
        if record.get("additionalProfiles"):
            errors.append(f"{path}: 'additionalProfiles' is not read - refresh the squad with adopt-template, "
                          "which records 'profiles'")
        return
    if (not isinstance(profiles, list) or not profiles or len(set(profiles)) != len(profiles)
            or not all(isinstance(p, str) and re.fullmatch(r"[\w-]+", p) for p in profiles)):
        errors.append(f"{path}: 'profiles' must be a list of distinct profile names")
        return
    if record.get("profile") != profiles[0]:
        errors.append(f"{path}: 'profile' must be the first entry of 'profiles' ({profiles[0]})")
    if len(profiles) == 1:
        return
    for rel in [f for p in profiles for f in (os.path.join(SQUAD_DIR, "tools", f"analyzer-check-{p}.py"),
                                              os.path.join(CLAUDE_DIR, "hooks", f"session-start-{p}.sh"))] + \
            [os.path.join(SQUAD_DIR, "tools", "analyzer-check.py"), os.path.join(CLAUDE_DIR, "hooks", "session-start.sh")]:
        if not os.path.isfile(rel):
            errors.append(f"{rel} is missing (written by adopt-template for the profiles {', '.join(profiles)})")
    try:
        sys.path.insert(0, os.path.join(SQUAD_DIR, "tools"))
        sys.dont_write_bytecode = True
        import squad_settings
        reports = getattr(squad_settings, "COVERAGE_REPORTS", None)
    except Exception as error:  # noqa: BLE001  (any failure to load the settings is the finding)
        errors.append(f"{SQUAD_DIR}/tools/squad_settings.py: {error}")
        return
    if not isinstance(reports, (list, tuple)) or len(reports) < len(profiles):
        errors.append(f"{SQUAD_DIR}/tools/squad_settings.py: COVERAGE_REPORTS needs a (format, glob) entry for "
                      f"each of the {len(profiles)} profiles")


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


ERROR_PREFIX = "ERROR: "


def check_decisions(errors):
    script = os.path.join(SQUAD_DIR, "tools", "decision-check.py")
    if not os.path.isfile(script):
        errors.append(f"{script} is missing (written by adopt-template)")
        return
    result = subprocess.run([sys.executable, script], capture_output=True, text=True, encoding="utf-8")
    findings = [line[len(ERROR_PREFIX):] for line in result.stdout.splitlines() if line.startswith(ERROR_PREFIX)]
    errors.extend(findings)
    for line in result.stdout.splitlines():
        if line.startswith("WARNING: "):
            print(line)
    if result.returncode != 0 and not findings:
        errors.append(f"{script} failed: {result.stderr.strip() or 'no output'}")


def main():
    # Resolve paths from the repository root, whatever the current directory is.
    os.chdir(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
    errors = []
    agents = sorted(glob.glob(os.path.join(AGENTS_DIR, "*.md")))
    for path in agents:
        check(path, os.path.splitext(os.path.basename(path))[0], errors, agent=True)
    if agents and not os.path.isfile(GIT_GUARD):
        errors.append(f"{GIT_GUARD} is missing (written by adopt-template)")
    skills = check_skills(errors)
    check_instructions(errors)
    check_template_record(errors)
    check_project_files(errors)
    check_decisions(errors)

    if not agents or not skills:
        errors.append("no agents or skills found - has the squad been adopted in this repository?")
    for error in errors:
        print(error)
    print(f"\nChecked {len(agents)} agents and {len(skills)} skills: {'PASS' if not errors else 'FAIL'}")
    return 0 if not errors else 1


if __name__ == "__main__":
    sys.exit(main())
