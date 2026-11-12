#!/usr/bin/env python3
"""One build structure and one rule set for every .NET repository of the template (`.squad/stack.md`, *Toolchain* and
*Analyzer gate*). The template does not own the build files; this checks them:

- **structure**: `TargetFramework`/`TargetFrameworks` is set in `Directory.Build.props` and in no project file (its
  value is the project's; a project may name a platform of the same major, `net11.0-windows`); `Directory.Packages.props`
  turns on `ManagePackageVersionsCentrally` and no project file gives a `PackageReference` a version;
  `Directory.Build.props` sets REQUIRED with these values, references the analyzers of ANALYZERS and names
  `.squad/dotnet-rules.globalconfig` in `GlobalAnalyzerConfigFiles`, and no project file sets one of them again; the
  tool manifest is `.config/dotnet-tools.json` and its `reihitsu.cli` is the version of `Reihitsu.Analyzer`;
  Git checks C# files out with CRLF;
- **rules**: no rule set (`*.ruleset`, `CodeAnalysisRuleSet`), no `.globalconfig`, no `NoWarn` or
  `WarningsNotAsErrors` that names a rule, no assembly-wide `SuppressMessage` (one without a `Target`); the blocks of
  `dotnet-rules.py` are current; and outside the blocks, a section of every C# file sets no rule or code-style option,
  and no other setting of the baseline with another value. A pending entry for a rule the baseline switches off (S1125)
  is refused.

What it does not check: the .NET major and the SDK (`dotnet_major.py` keeps them one per repository), any package
version, `Deterministic`.

The *Analyzer gate* (`analyzer-check.py`) runs this check, also with `--lint-only`; it can run on its own:
    python3 .squad/tools/dotnet_config.py

Exit code 0 when everything holds, 1 otherwise.
"""
import importlib.util
import json
import os
import re
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

HERE = os.path.dirname(os.path.abspath(__file__))
BUILD_PROPS = "Directory.Build.props"
PACKAGES_PROPS = "Directory.Packages.props"
TOOL_MANIFEST = os.path.join(".config", "dotnet-tools.json")
EDITORCONFIG = ".editorconfig"
PROJECT = ".csproj"
BUILD_FILES = (PROJECT, ".props", ".targets")
REQUIRED = {"TreatWarningsAsErrors": "true", "EnforceCodeStyleInBuild": "true", "AnalysisLevel": "latest-recommended",
            "Nullable": "enable", "ImplicitUsings": "enable", "GenerateDocumentationFile": "true"}
ANALYZERS = ("Reihitsu.Analyzer", "SonarAnalyzer.CSharp")
GLOBAL_CONFIG = re.compile(r'<GlobalAnalyzerConfigFiles\b[^>]*\bInclude="[^"]*\.squad[/\\]dotnet-rules\.globalconfig"', re.I)
REIHITSU_TOOL = "reihitsu.cli"
TARGET = re.compile(r"<(TargetFrameworks?)\b[^>]*>([^<]*)</TargetFrameworks?>", re.I)
MONIKER = re.compile(r"net(\d+)\.\d+(?:-[\w.]+)?", re.I)
PACKAGE_REFERENCE = re.compile(r"<PackageReference\b[^>]*?(?:/>|>.*?</PackageReference>)", re.I | re.S)
PACKAGE_VERSION = re.compile(r"<PackageVersion\b[^>]*>", re.I)
SUPPRESSIONS = re.compile(r"<(NoWarn|WarningsNotAsErrors)\b[^>]*>([^<]*)</(?:NoWarn|WarningsNotAsErrors)>", re.I)
# Warnings of the build itself (NuGet, MSBuild, the SDK) are no analyzer rules.
NOT_RULES = ("NU", "MSB", "NETSDK")
RULE = re.compile(r"[A-Z]+\d+")
ASSEMBLY_SUPPRESSION = re.compile(r"\[(?:assembly|module):\s*(?:System\.Diagnostics\.CodeAnalysis\.)?SuppressMessage"
                                  r"(?:Attribute)?\((?P<args>[^\]]*)\]")
TARGET_ARGUMENT = re.compile(r"\bTarget\s*=")
OPTION_PREFIXES = ("csharp_", "dotnet_")


def tracked_files():
    """The repository's files, tracked and untracked but not ignored."""
    done = subprocess.run(["git", "ls-files", "--cached", "--others", "--exclude-standard"], capture_output=True,
                          text=True, check=False)
    return sorted({line.strip() for line in done.stdout.splitlines() if line.strip() and os.path.isfile(line.strip())})


def read(path):
    try:
        with open(path, encoding="utf-8-sig") as handle:
            return handle.read()
    except (OSError, UnicodeDecodeError):
        return ""


def property_value(text, name):
    """The value of an MSBuild property (`<Nullable>enable</Nullable>`), or None."""
    found = re.search(rf"<{name}\b[^>]*>([^<]*)</{name}>", text, re.I)
    return found.group(1).strip() if found else None


def attribute(tag, name):
    found = re.search(rf'\b{name}="([^"]*)"', tag, re.I)
    return found.group(1) if found else None


# --- structure ----------------------------------------------------------------------------------------------------

def target_framework_findings(projects, props):
    """`TargetFramework` in Directory.Build.props, in no project file but as a platform of the same major."""
    central = TARGET.search(props)
    findings = [] if central else [f"{BUILD_PROPS}: no <TargetFramework>; it is set there once for every project"]
    major = MONIKER.match(central.group(2).split(";")[0].strip()) if central else None
    for path, text in projects:
        for found in TARGET.finditer(text):
            values = [value.strip() for value in found.group(2).split(";") if value.strip()]
            platform = all("-" in value and MONIKER.fullmatch(value) for value in values) and major \
                and all(MONIKER.fullmatch(value).group(1) == major.group(1) for value in values)
            if not platform:
                findings.append(f"{path}: <{found.group(1)}>{found.group(2)}</{found.group(1)}> belongs in {BUILD_PROPS} "
                                "(a project file may only name a platform of the same major, net11.0-windows)")
    return findings


def required_findings(projects, props):
    """The required switches and analyzers in Directory.Build.props, and none of them again in a project file."""
    findings = [f"{BUILD_PROPS}: <{name}> must be {value} (found {property_value(props, name) or 'nothing'})"
                for name, value in REQUIRED.items() if (property_value(props, name) or "").lower() != value.lower()]
    referenced = {attribute(tag, "Include") for tag in PACKAGE_REFERENCE.findall(props)}
    findings += [f"{BUILD_PROPS}: no <PackageReference Include=\"{name}\"> for every project" for name in ANALYZERS
                 if name not in referenced]
    if GLOBAL_CONFIG.search(props) is None:
        findings.append(f"{BUILD_PROPS}: no <GlobalAnalyzerConfigFiles Include=\"$(MSBuildThisFileDirectory)"
                        ".squad/dotnet-rules.globalconfig\" /> (the rules switched off, also in generated code)")
    for path, text in projects:
        findings += [f"{path}: <{name}> is set once in {BUILD_PROPS}" for name in REQUIRED
                     if property_value(text, name) is not None]
    return findings


def package_findings(projects, packages):
    """Central package management: on in Directory.Packages.props, and no version in a project file."""
    findings = [] if (property_value(packages, "ManagePackageVersionsCentrally") or "").lower() == "true" else [
        f"{PACKAGES_PROPS}: <ManagePackageVersionsCentrally> must be true; every version is written there"]
    for path, text in projects:
        for reference in PACKAGE_REFERENCE.findall(text):
            if attribute(reference, "Version") or attribute(reference, "VersionOverride") \
                    or re.search(r"<(?:Version|VersionOverride)>", reference, re.I):
                findings.append(f"{path}: {attribute(reference, 'Include')} has a version of its own; it belongs in "
                                f"{PACKAGES_PROPS}")
    return findings


def tool_findings(files, packages):
    """The tool manifest under .config/, with reihitsu.cli at the version of Reihitsu.Analyzer."""
    if "dotnet-tools.json" in files:
        return [f"dotnet-tools.json: move it to {TOOL_MANIFEST}"]
    try:
        tools = json.loads(read(TOOL_MANIFEST) or "{}").get("tools", {})
    except (ValueError, AttributeError):
        return [f"{TOOL_MANIFEST}: no valid tool manifest"]
    cli = (tools.get(REIHITSU_TOOL) or {}).get("version")
    analyzer = next((attribute(tag, "Version") for tag in PACKAGE_VERSION.findall(packages)
                     if (attribute(tag, "Include") or "").lower() == "reihitsu.analyzer"), None)
    if cli != analyzer:
        return [f"{TOOL_MANIFEST}: {REIHITSU_TOOL} {cli} and Reihitsu.Analyzer {analyzer} in {PACKAGES_PROPS} are one "
                "version, so the formatter and the analyzer agree"]
    return []


def line_ending_findings():
    """Git checks C# files out with CRLF."""
    done = subprocess.run(["git", "check-attr", "eol", "--", "src/Probe.cs"], capture_output=True, text=True, check=False)
    if done.stdout.strip().endswith(": crlf"):
        return []
    return [".gitattributes: C# files are checked out with CRLF (*.cs text eol=crlf), the line ending of every .NET "
            "repository"]


# --- rules --------------------------------------------------------------------------------------------------------

def build_file_findings(path, text):
    """A rule set or a rule switched off in a project or build file."""
    found = [] if property_value(text, "CodeAnalysisRuleSet") is None else [
        f"{path}: <CodeAnalysisRuleSet>; the rules are the dotnet-rules block's"]
    for match in SUPPRESSIONS.finditer(text):
        rules = [part for part in re.split(r"[;,\s]+", match.group(2))
                 if RULE.fullmatch(part.upper()) and not part.upper().startswith(NOT_RULES)]
        if rules:
            found.append(f"{path}: <{match.group(1)}> switches off {', '.join(rules)}; the rules are the dotnet-rules "
                         "block's (a rule the code does not meet yet: DOTNET_RULES_PENDING)")
    return found


def rule_set_findings(files, texts):
    """No rule set, no global config, no rule switched off in a build file, no assembly-wide suppression."""
    findings = [f"{path}: a rule set; the rules are the dotnet-rules block's" for path in files
                if path.endswith(".ruleset") or os.path.basename(path) == ".globalconfig"]
    for path, text in texts:
        if path.endswith(BUILD_FILES):
            findings += build_file_findings(path, text)
        elif path.endswith(".cs"):
            findings += [f"{path}: an assembly-wide SuppressMessage; suppress one member or type with a Target, or put "
                         "the rule on DOTNET_RULES_PENDING" for found in ASSEMBLY_SUPPRESSION.finditer(text)
                         if not TARGET_ARGUMENT.search(found.group("args"))]
    return findings


def dotnet_rules():
    """`dotnet-rules.py` next to this script as a module."""
    spec = importlib.util.spec_from_file_location("dotnet_rules", os.path.join(HERE, "dotnet-rules.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def baseline_values(rules):
    """{key: value} of the baseline's settings for every C# file."""
    general, _, _ = rules.wanted(rules.read_file(rules.BASELINE), {})
    values = {}
    for line in general:
        key, equals, value = line.partition("=")
        if equals and not line.lstrip().startswith(("#", ";")):
            values[key.strip().lower()] = value.strip()
    return values


def outside_blocks(rules, lines):
    """(line number, section, key, value) of every setting outside the three blocks."""
    blocks = [rules.find(lines, begin, end) for begin, end in ((rules.BEGIN, rules.END),
                                                               (rules.SONAR_BEGIN, rules.SONAR_END),
                                                               (rules.PATHS_BEGIN, rules.PATHS_END))]
    inside = {i for block in blocks if block for i in range(block[0], block[1] + 1)}
    section = None
    for number, line in enumerate(lines):
        header = rules.SECTIONS.SECTION.match(line)
        if header:
            section = line.strip()
        elif number not in inside and "=" in line and not line.lstrip().startswith(("#", ";")):
            key, _, value = line.partition("=")
            yield number + 1, section, key.strip(), value.strip()


def editorconfig_findings(rules):
    """A section of every C# file sets no rule or code-style option outside the blocks, and after the dotnet-rules
    block, which it would override, no other setting of the baseline with another value (one before it, such as
    `[*] end_of_line = lf` for the other files, the block overrides for C# files)."""
    baseline = baseline_values(rules)
    lines = read(EDITORCONFIG).splitlines()
    block = rules.find(lines, rules.BEGIN, rules.END)
    after = block[1] + 1 if block else 0
    findings = []
    for number, section, key, value in outside_blocks(rules, lines):
        if section is None or not rules.SECTIONS.every_cs_file(section):
            continue
        if key.lower().startswith(OPTION_PREFIXES):
            findings.append(f"{EDITORCONFIG}:{number}: {section} sets {key} outside the dotnet-rules block; the "
                            "baseline's block holds the rules (a section of a path after the blocks may)")
        elif number > after and key.lower() in baseline and value.lower() != baseline[key.lower()].lower():
            findings.append(f"{EDITORCONFIG}:{number}: {section} sets {key} = {value}, the baseline "
                            f"{baseline[key.lower()]}")
    return findings


def blocks_findings(rules):
    """The dotnet-rules blocks are current and the pending list is valid, as `dotnet-rules.py --check` sees them."""
    text, baseline = rules.read_file(rules.EDITORCONFIG), rules.read_file(rules.BASELINE)
    refused = rules.refusals(text, baseline, rules.PENDING)
    if refused:
        return refused
    general, paths, _ = rules.wanted(baseline, rules.PENDING)
    if rules.render(text, general, paths) == text:
        return []
    return [f"{EDITORCONFIG}: the dotnet-rules blocks are missing or out of date; run python3 .squad/tools/dotnet-rules.py"]


def findings(files=None):
    files = tracked_files() if files is None else files
    texts = [(path, read(path)) for path in files if path.endswith((*BUILD_FILES, ".cs"))]
    # every project file and every build file below the root's Directory.Build.props, which may set them once
    projects = [(path, text) for path, text in texts if path.endswith(BUILD_FILES) and path != BUILD_PROPS]
    props, packages = read(BUILD_PROPS), read(PACKAGES_PROPS)
    rules = dotnet_rules()
    found = target_framework_findings(projects, props) + required_findings(projects, props) \
        + package_findings(projects, packages) + tool_findings(files, packages) + line_ending_findings() \
        + rule_set_findings(files, texts) + blocks_findings(rules) + editorconfig_findings(rules)
    return found


def check(files=None):
    """Print every finding; True when there is none."""
    found = findings(files)
    for finding in found:
        print(f".NET config: {finding}")
    if not found:
        print(".NET config: one build structure and the template's rules")
    return not found


def main():
    os.chdir(os.path.join(HERE, "..", ".."))
    ok = check()
    print("PASS" if ok else "FAIL")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
