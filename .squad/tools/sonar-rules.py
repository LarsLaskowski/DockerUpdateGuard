#!/usr/bin/env python3
"""Sonar rules: the C# rules of the project's SonarQube Cloud quality profile, switched on for the local analyzer gate.

SonarAnalyzer.CSharp runs the rules of SonarQube Cloud's built-in profile *Sonar way core* by default. A project whose
quality profile activates more (*Sonar way comprehensive*: S3776, S1192, S107, …) or switches core rules off would
otherwise learn of the difference only from the pull request's analysis. This script reads the project's C# profile and
*Sonar way core* from SonarQube Cloud and writes the difference into the marked block of `.editorconfig`:

    # squad:sonar-rules begin — …
    dotnet_diagnostic.S3776.severity = error
    dotnet_diagnostic.S1309.severity = none
    # squad:sonar-rules end

- a rule the project's profile activates beyond *Sonar way core* gets `error`, the severity every .NET repository of
  the template uses; one in `DOTNET_RULES_PENDING` of `.squad/tools/squad_settings.py` (a rule the code does not meet
  yet, with its issue, `dotnet-rules.py`) gets `suggestion`, which the *Analyzer gate* still reports in a changed file;
- a rule of *Sonar way core* the project's profile does not activate gets `none`;
- a rule the file already sets outside the block for every C# file (`[*]`, `[*.cs]`, `[*.{cs,vb}]`), which is the
  dotnet-rules block's (`dotnet_config.py` refuses any other place), stays out of the block and is listed; one set
  only in a section of a path or of some file names (`[tests/**.cs]`, `[*.g.cs]`) still gets its line in the block, so
  the rest of the code keeps it. Such a section belongs after the block, or the block's line wins there too; the
  script warns about one before it.

The block replaces the one between the markers. A file without them gets the block at the end of its first section of
the C# files alone (`[*.cs]`), or a `[*.cs]` section at its end. Only `csharpsquid` rules are written; a rule's
parameters (S107's maximum, S3776's threshold) stay the analyzer's defaults. The project and the server come from
`sonar-check.py` (`SONAR_PROJECT_KEY`, `sonar-project.properties`, the scanner's `/k:`; `SONAR_HOST`), with the same
credential (a network secret of the environment, or `SONAR_TOKEN`).

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
SEVERITY = "error"
PENDING_SEVERITY = "suggestion"
PENDING = getattr(settings, "DOTNET_RULES_PENDING", {})
SECTION = re.compile(r"^\s*\[(?P<glob>.*)\]\s*$")
OWN_RULE = re.compile(r"^\s*dotnet_diagnostic\.(?P<rule>S\d+)\.severity\s*=", re.I)
CS_PATHS = ("Program.cs", "src/App/Widget.cs", "src/App/Form.Designer.cs", "src/App/obj/Widget.g.cs",
            "tests/App.Tests/WidgetTests.cs")
OTHER_PATHS = ("README.md", "src/App/App.csproj")


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


def severity_of(rule):
    """The severity of a rule the project's profile adds: SEVERITY, or PENDING_SEVERITY while it is pending."""
    return PENDING_SEVERITY if isinstance(PENDING, dict) and rule in PENDING else SEVERITY


def wanted_lines(added, removed, own):
    """The block's rule lines: the project's extra rules at SEVERITY (a pending one at PENDING_SEVERITY), core rules it
    switched off at `none`; rules the file sets itself are left out."""
    def number(rule):
        return int(rule[1:])
    lines = [f"dotnet_diagnostic.{rule}.severity = {severity_of(rule)}" for rule in sorted(added - own, key=number)]
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


def glob_regex(glob):
    """An EditorConfig section glob as a regular expression over a path relative to the file: `*` within one folder,
    `**` across folders, `?`, `[…]` and `{a,b}`; a glob without `/` matches the file name in any folder."""
    out, depth, i = [], 0, 0
    while i < len(glob):
        char = glob[i]
        if glob.startswith("**", i):
            out.append(".*")
            i += 1
        elif char == "*":
            out.append("[^/]*")
        elif char == "?":
            out.append("[^/]")
        elif char == "[" and "]" in glob[i:]:
            end = glob.index("]", i)
            out.append("[" + glob[i + 1:end].replace("!", "^", 1) + "]")
            i = end
        elif char == "{":
            depth += 1
            out.append("(?:")
        elif char == "}" and depth:
            depth -= 1
            out.append(")")
        elif char == "," and depth:
            out.append("|")
        else:
            out.append(re.escape(char))
        i += 1
    body = "".join(out).replace(r".*/", "(?:.*/)?")
    return re.compile(("" if "/" in glob else "(?:.*/)?") + body.lstrip("/") + "$")


def section_matches(line, paths):
    """True when `line` is a section header whose glob matches every path of `paths`."""
    match = SECTION.match(line)
    if not match:
        return False
    try:
        pattern = glob_regex(match.group("glob").strip())
    except re.error:
        return False
    return all(pattern.match(path) for path in paths)


def every_cs_file(line):
    """True for a section header that applies to every C# file (`[*]`, `[*.cs]`, `[*.{cs,vb}]`), not to a path
    (`[tests/**.cs]`) or to some file names (`[*.g.cs]`, `[*Tests.cs]`)."""
    return section_matches(line, CS_PATHS)


def cs_section(line):
    """True for a section of every C# file that applies to no other file, where the block belongs."""
    return every_cs_file(line) and not any(section_matches(line, [path]) for path in OTHER_PATHS)


def own_rules(lines):
    """The rules the file sets outside the block for every C# file. A rule set in a section of a path or of some file
    names only (`[tests/**.cs]`, `[*.g.cs]`) is no choice for the rest of the code, so the block still carries it
    (Vandox: S107 off under `tests/**` would otherwise have left production code without S107)."""
    found, everywhere = set(), True
    for line in lines:
        if SECTION.match(line):
            everywhere = every_cs_file(line)
            continue
        match = OWN_RULE.match(line)
        if match and everywhere:
            found.add(match.group("rule").upper())
    return found


def overridden_ahead(lines):
    """{rule: (section, severity)} of the rules a narrower section of C# files sets in `lines`, the part of the file
    before the block: the block's line comes later and wins there too."""
    found, section = {}, None
    for line in lines:
        if SECTION.match(line):
            narrower = not every_cs_file(line) and any(section_matches(line, [path]) for path in CS_PATHS)
            section = line.strip() if narrower else None
            continue
        match = OWN_RULE.match(line)
        if match and section:
            severity = line.split("=", 1)[1].split("#")[0].strip().lower()
            found.setdefault(match.group("rule").upper(), (section, severity))
    return found


def insert_at(lines):
    """The index at the end of the first `[*.cs]` section (before trailing blank lines), or None without one."""
    start = next((i for i, line in enumerate(lines) if cs_section(line)), None)
    if start is None:
        return None
    end = next((i for i in range(start + 1, len(lines)) if SECTION.match(lines[i])), len(lines))
    while end > start + 1 and not lines[end - 1].strip():
        end -= 1
    return end


DOTNET_RULES_END = "# squad:dotnet-rules end"


def with_block(lines, body):
    """`lines` without a block, with `body` right after the dotnet-rules block where there is one (never inside the
    dotnet-rules-paths block after it), else at the end of the first `[*.cs]` section, or in a new one at the end; a
    blank line before it."""
    dotnet_end = next((i for i, line in enumerate(lines) if line.startswith(DOTNET_RULES_END)), None)
    at = dotnet_end + 1 if dotnet_end is not None else insert_at(lines)
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


def ahead_of_block(text):
    """The lines of the file that come before the block, where it is or where `render` puts it."""
    before, block, _ = split_block(text)
    at = None if block is not None else insert_at(before)
    return before if at is None else before[:at]


def report(own_profile, added, removed, own, ahead):
    print(f"Profile \"{own_profile.get('name')}\" against \"{CORE}\": {len(added)} rule(s) beyond it at {SEVERITY}, "
          f"{len(removed)} switched off.")
    kept = sorted((added | removed) & own, key=lambda rule: int(rule[1:]))
    if kept:
        print(f"Set by {EDITORCONFIG} itself, left out of the block: {', '.join(kept)}")
    for rule, (section, severity) in sorted(ahead.items(), key=lambda item: int(item[0][1:])):
        written = severity_of(rule) if rule in added else "none"
        if rule in (added | removed) - own and severity != written:
            print(f"WARNING: {section} sets {rule} before the block, whose line comes later and wins there too: move "
                  "that section after the block")


def main(argv=None):
    parser = Usage(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--check", action="store_true", help="report a missing or outdated block, write nothing")
    options = parser.parse_args(argv)
    if hasattr(settings, "SONAR_RULES_SEVERITY"):
        print("NOTE: SONAR_RULES_SEVERITY is no longer read (the block is at error; DOTNET_RULES_PENDING holds a rule the "
              "code does not meet yet): remove it from squad_settings.py and the sentence of stack.md that names it")
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
    report(own_profile, added, removed, own, overridden_ahead(ahead_of_block(text)))
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
