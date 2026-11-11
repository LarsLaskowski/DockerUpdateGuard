#!/usr/bin/env python3
"""Sonar rules: the C# rules of the project's SonarQube Cloud quality profile, switched on for the local analyzer gate.

SonarAnalyzer.CSharp runs the rules of SonarQube Cloud's built-in profile *Sonar way core* by default. A project whose
quality profile activates more (*Sonar way comprehensive*: S3776, S1192, S107, …) or switches core rules off would
otherwise learn of the difference only from the pull request's analysis. This script reads the project's C# profile and
*Sonar way core* from SonarQube Cloud and writes the difference into the marked block of `.editorconfig`:

    # squad:sonar-rules begin — …
    dotnet_diagnostic.S3776.severity = suggestion
    dotnet_diagnostic.S1309.severity = none
    # squad:sonar-rules end

- a rule the project's profile activates beyond *Sonar way core* gets `SONAR_RULES_SEVERITY` from
  `.squad/tools/squad_settings.py` (default `suggestion`: the *Analyzer gate* reports every severity in a changed file,
  while code the change leaves alone keeps building; `warning` or `error` make the build itself report them);
- a rule of *Sonar way core* the project's profile does not activate gets `none`;
- a rule the file already sets outside the block is the project's own choice: it stays out of the block and is
  listed.

The block replaces the one between the markers. A file without them gets the block at the end of its first `[*.cs]`
section, or a `[*.cs]` section at its end. Only `csharpsquid` rules are written; a rule's parameters (S107's
maximum, S3776's threshold) stay the analyzer's defaults. The
project and the server come from `sonar-check.py` (`SONAR_PROJECT_KEY`, `sonar-project.properties`, the scanner's
`/k:`; `SONAR_HOST`), with the same credential (a network secret of the environment, or `SONAR_TOKEN`).

`adopt-template` runs it on adoption and on every refresh; `--check` reports a block that no longer matches the
profiles (SonarSource or the project changed them).

Usage, from the repository root:
    python3 .squad/tools/sonar-rules.py [--check]

Exit code 0 when the block was written or is current, 1 when `--check` finds it missing or out of date, 2 NOT CHECKED
(no project key, SonarQube could not be read, no *Sonar way core* profile), 4 wrong usage.
"""
import argparse
import importlib.util
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from analyzer_common import settings  # noqa: E402  (shared helpers next to this script)

CURRENT, OUTDATED, NOT_CHECKED, USAGE = 0, 1, 2, 4
EDITORCONFIG = ".editorconfig"
BEGIN = "# squad:sonar-rules begin"
END = "# squad:sonar-rules end"
HEADER = (f"{BEGIN} — written by .squad/tools/sonar-rules.py from the project's SonarQube Cloud C# profile against "
          "\"Sonar way core\"; never edited by hand (.squad/stack.md, Analyzer gate)")
CORE = "Sonar way core"
REPOSITORY = "csharpsquid"
SEVERITY = getattr(settings, "SONAR_RULES_SEVERITY", "suggestion")
SEVERITIES = ("suggestion", "warning", "error")
SECTION = re.compile(r"^\s*\[(?P<glob>[^\]]*)\]\s*$")
OWN_RULE = re.compile(r"^\s*dotnet_diagnostic\.(?P<rule>S\d+)\.severity\s*=", re.I)


class NotChecked(Exception):
    """The profiles could not be read; the message says why."""


class Usage(argparse.ArgumentParser):
    def error(self, message):
        self.print_usage(sys.stderr)
        self.exit(USAGE, f"{self.prog}: error: {message}\n")


def sonar_check():
    """`sonar-check.py` next to this script as a module: the project key, the API and its credential."""
    spec = importlib.util.spec_from_file_location("sonar_check", os.path.join(HERE, "sonar-check.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def active_rules(sonar, profile, organization, token):
    """The ids (`S3776`) of the `csharpsquid` rules the quality profile activates."""
    rules = sonar.paged("rules/search", {"qprofile": profile, "activation": "true", "organization": organization,
                                         "repositories": REPOSITORY, "f": "repo,params"}, "rules", token)
    return {rule["key"].split(":", 1)[1] for rule in rules if rule.get("key", "").startswith(REPOSITORY + ":")}


def profiles(sonar, key, token):
    """(the project's C# profile, Sonar way core) as {key, name}, and the project's organization."""
    component = sonar.api("components/show", {"component": key}, token).get("component", {})
    organization = component.get("organization")
    if not organization:
        raise NotChecked(f"SonarQube names no organization for the project {key}")
    own = sonar.api("qualityprofiles/search", {"project": key, "language": "cs", "organization": organization},
                    token).get("profiles", [])
    every = sonar.api("qualityprofiles/search", {"language": "cs", "organization": organization},
                      token).get("profiles", [])
    core = next((profile for profile in every if profile.get("name") == CORE and profile.get("isBuiltIn")), None)
    if not own or core is None:
        raise NotChecked(f"no C# profile for {key}" if not own else f"no built-in profile \"{CORE}\" in {organization}")
    return own[0], core, organization


def wanted_lines(added, removed, own):
    """The block's rule lines: the project's extra rules at SEVERITY, core rules it switched off at `none`; rules the
    file sets itself are left out."""
    def number(rule):
        return int(rule[1:])
    lines = [f"dotnet_diagnostic.{rule}.severity = {SEVERITY}" for rule in sorted(added - own, key=number)]
    lines += [f"dotnet_diagnostic.{rule}.severity = none" for rule in sorted(removed - own, key=number)]
    return lines


def split_block(text):
    """(lines before the block, the block's rule lines or None without markers, lines after it)."""
    lines = text.splitlines()
    begin = next((i for i, line in enumerate(lines) if line.startswith(BEGIN)), None)
    end = next((i for i, line in enumerate(lines) if line.startswith(END)), None)
    if begin is None or end is None or end < begin:
        return lines, None, []
    return lines[:begin], lines[begin + 1:end], lines[end + 1:]


def own_rules(lines):
    """The rules the file sets outside the block."""
    return {match.group("rule").upper() for match in map(OWN_RULE.match, lines) if match}


def insert_at(lines):
    """The index at the end of the first `[*.cs]` section (before trailing blank lines), or None without one."""
    start = next((i for i, line in enumerate(lines) if SECTION.match(line)
                  and "cs" in re.split(r"[{},.*\s]+", SECTION.match(line).group("glob"))
                  and "/" not in SECTION.match(line).group("glob")), None)
    if start is None:
        return None
    end = next((i for i in range(start + 1, len(lines)) if SECTION.match(lines[i])), len(lines))
    while end > start + 1 and not lines[end - 1].strip():
        end -= 1
    return end


def with_block(lines, body):
    """`lines` without a block, with `body` at the end of the first `[*.cs]` section, or in a new one at the end; a
    blank line before it."""
    at = insert_at(lines)
    if at is None:
        return lines + ([""] if lines and lines[-1].strip() else []) + ["[*.cs]", *body]
    return lines[:at] + ([""] if at and lines[at - 1].strip() else []) + body + lines[at:]


def render(text, rules):
    """The file's text with the block holding `rules`."""
    before, block, after = split_block(text)
    body = [HEADER, *rules, END]
    lines = before + body + after if block is not None else with_block(before, body)
    newline = "\r\n" if "\r\n" in text else "\n"
    final = text.endswith(("\n", "\r")) or not text
    return newline.join(lines) + (newline if final else "")


def read_file(path):
    if not os.path.isfile(path):
        return ""
    with open(path, encoding="utf-8", newline="") as handle:
        return handle.read()


def report(own_profile, added, removed, own):
    print(f"Profile \"{own_profile.get('name')}\" against \"{CORE}\": {len(added)} rule(s) beyond it at {SEVERITY}, "
          f"{len(removed)} switched off.")
    kept = sorted((added | removed) & own, key=lambda rule: int(rule[1:]))
    if kept:
        print(f"Set by {EDITORCONFIG} itself, left out of the block: {', '.join(kept)}")


def main(argv=None):
    parser = Usage(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--check", action="store_true", help="report a missing or outdated block, write nothing")
    options = parser.parse_args(argv)
    if SEVERITY not in SEVERITIES:
        parser.error(f"SONAR_RULES_SEVERITY must be one of {', '.join(SEVERITIES)}, not {SEVERITY!r}")
    sonar = sonar_check()
    key = sonar.project_key()
    if key is None:
        print("NOT CHECKED: no SonarQube project key (SONAR_PROJECT_KEY, sonar-project.properties or the scanner's /k:)")
        return NOT_CHECKED
    token = os.environ.get("SONAR_TOKEN") or None
    try:
        own_profile, core, organization = profiles(sonar, key, token)
        project_rules = active_rules(sonar, own_profile["key"], organization, token)
        core_rules = active_rules(sonar, core["key"], organization, token)
    except (NotChecked, sonar.ApiError, sonar.NotChecked, OSError, ValueError) as error:
        print(f"NOT CHECKED: {error}")
        return NOT_CHECKED
    text = read_file(EDITORCONFIG)
    before, _, after = split_block(text)
    own = own_rules(before + after)
    added, removed = project_rules - core_rules, core_rules - project_rules
    report(own_profile, added, removed, own)
    updated = render(text, wanted_lines(added, removed, own))
    if updated == text:
        print(f"{EDITORCONFIG}: the sonar-rules block is current")
        return CURRENT
    if options.check:
        print(f"{EDITORCONFIG}: the sonar-rules block is missing or out of date — run python3 .squad/tools/sonar-rules.py")
        return OUTDATED
    with open(EDITORCONFIG, "w", encoding="utf-8", newline="") as handle:
        handle.write(updated)
    print(f"{EDITORCONFIG}: the sonar-rules block written")
    return CURRENT


if __name__ == "__main__":
    sys.exit(main())
