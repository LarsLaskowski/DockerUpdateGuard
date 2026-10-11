"""Helpers shared by the profiles' analyzer gates (`analyzer-check*.py`): git access with a fixed base, and the
lint pass over changed files no stack analyzer covers — shell scripts (shellcheck), GitHub workflows
(actionlint) and Dockerfiles (hadolint), plus two SonarQube Cloud shell rules shellcheck does not report
(`shelldre:S7679`, `shelldre:S7688`). The SessionStart hook installs the three linters
(`.claude/hooks/install-linters.sh`); a missing one is reported as NOT RUN, never as a pass.

Written by adopt-template; it takes no arguments, so nothing user-supplied reaches the shell, git or the
filesystem."""
import os
import re
import shutil
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import squad_settings as settings  # noqa: E402  (per-repository settings next to this script)

BASE_REF = getattr(settings, "BASE_REF", "origin/main")
WORKFLOW_DIR = ".github/workflows/"
# A positional parameter, with the quotes it may carry: `$1`, `"$1"`, `${1}`, `"${1}"`. positional_words()
# keeps the ones used as a word of their own — not inside a longer string (`"::error::$1"`) and not the
# right-hand side of an assignment (`local path=$1`), which is the fix.
PARAMETER = re.compile(r'"?\$(?:[1-9]|\{[1-9]\d*\})"?')
NOT_BEFORE_WORD = "=$"
WORD_END = " \t;|&)<>"
# `[ … ]` as a command word: at the start of a command, followed by a space, and not the `[[` keyword.
SINGLE_BRACKET = re.compile(r"(?<![^\s;(!&|])\[\s")
# `name() {`, `function name {` and `function name() {`; a bare `name {` is not bash, so matching it costs nothing.
FUNCTION_HEAD = re.compile(r"^\s*(?:function\s+)?[\w.-]+\s*(?:\(\)\s*)?\{")
RULES = {"S7679": "assign this positional parameter to a local variable (shelldre:S7679)",
         "S7688": "use [[ … ]] instead of [ … ] (shelldre:S7688)"}


def git(*args):
    return subprocess.run(["git", *args], capture_output=True, text=True, check=True).stdout


def merge_base():
    """The merge base of BASE_REF and HEAD; a missing base ref is a clear error, not a traceback."""
    result = subprocess.run(["git", "merge-base", BASE_REF, "HEAD"], capture_output=True, text=True, check=False)
    if result.returncode != 0:
        sys.exit(f"Cannot find the merge base with {BASE_REF}: fetch it (git fetch origin <branch>) or set BASE_REF "
                 "in .squad/tools/squad_settings.py to the base branch of this repository")
    return result.stdout.strip()


def changed_files():
    """Files changed since the merge base, working tree and untracked files included, deleted ones left out."""
    names = git("diff", "--name-only", "--diff-filter=d", merge_base()).splitlines()
    names += git("ls-files", "--others", "--exclude-standard").splitlines()
    return sorted({n.strip() for n in names if n.strip() and os.path.isfile(n.strip())})


def is_shell(path):
    return path.endswith(".sh")


def is_workflow(path):
    return path.startswith(WORKFLOW_DIR) and path.endswith((".yml", ".yaml"))


def is_dockerfile(path):
    name = os.path.basename(path)
    if name == "Dockerfile" or name.endswith(".Dockerfile"):
        return True
    return name.startswith("Dockerfile.") and not name.endswith((".md", ".txt", ".html"))


LINTERS = [
    ("shellcheck", "changed shell scripts", is_shell, ["shellcheck", "--"]),
    ("actionlint", "changed workflows", is_workflow, ["actionlint", "-no-color"]),
    ("hadolint", "changed Dockerfiles", is_dockerfile, ["hadolint", "--no-color"]),
]


def run_linter(tool, label, command, files):
    """One linter over its files when it is installed; says so when files are skipped. True when nothing failed."""
    if not files:
        return True
    if shutil.which(tool) is None:
        print(f"{tool}: NOT RUN (not installed) - {len(files)} {label} not analysed locally "
              "(the SessionStart hook installs it; see .squad/stack.md, Analyzer gate)\n")
        return True
    result = subprocess.run([*command, *files], capture_output=True, text=True, check=False)
    output = (result.stdout + result.stderr).strip()
    if output:
        print(output[-6000:])
    print(f"{tool} ({label}): {'PASS' if result.returncode == 0 else 'FAIL'}\n")
    return result.returncode == 0


def strip_comment(line):
    """The line without a trailing `# comment` (a `#` that starts a word), quotes not considered."""
    match = re.search(r"(^|\s)#", line)
    return line[:match.start()] if match else line


def positional_words(line):
    """The positional parameters in a line that stand as a word of their own (see PARAMETER)."""
    for match in PARAMETER.finditer(line):
        token = match.group()
        before = line[match.start() - 1] if match.start() else ""
        after = line[match.end()] if match.end() < len(line) else ""
        if token.startswith('"') != token.endswith('"'):
            continue  # one quote belongs to a longer string
        if before and (before in NOT_BEFORE_WORD or before.isalnum() or before == "_"):
            continue
        if after and after not in WORD_END:
            continue
        yield token


def sonar_shell_findings(path, text):
    """`path(line): rule` for every use of S7679 and S7688 in a shell script, found without external tools."""
    findings = []
    depth = 0
    for number, raw in enumerate(text.splitlines(), 1):
        line = strip_comment(raw)
        if SINGLE_BRACKET.search(line):
            findings.append(f"{path}({number}): {RULES['S7688']}")
        in_function = depth > 0 or FUNCTION_HEAD.match(line) is not None
        if in_function and any(positional_words(line)):
            findings.append(f"{path}({number}): {RULES['S7679']}")
        depth = max(0, depth + line.count("{") - line.count("}"))
    return findings


def sonar_shell_check(files):
    """The two SonarQube shell rules over changed shell scripts. True when there is no finding."""
    if not files:
        return True
    findings = []
    for path in files:
        with open(path, encoding="utf-8", errors="replace") as handle:
            findings += sonar_shell_findings(path, handle.read())
    for finding in findings:
        print(finding)
    print(f"SonarQube shell rules S7679/S7688 (changed shell scripts): {'PASS' if not findings else 'FAIL'}\n")
    return not findings


def lint_check():
    """shellcheck, actionlint, hadolint and the two Sonar shell rules over the changed files. True when nothing
    failed (a linter that is not installed is reported, not counted as a failure)."""
    files = changed_files()
    ok = True
    for tool, label, matches, command in LINTERS:
        ok = run_linter(tool, label, command, [f for f in files if matches(f)]) and ok
    return sonar_shell_check([f for f in files if is_shell(f)]) and ok
