#!/usr/bin/env python3
"""One .NET major per repository: every project's target framework, the SDK of `global.json` and every .NET image of
a Dockerfile name the same major version (`.squad/stack.md`, *Toolchain*).

Several majors may be installed side by side on a machine or in CI; a solution is built for one. A new project that
`dotnet new` creates for the SDK of `global.json` gets that SDK's major, and Dependabot leaves the next major alone,
so a jump shows here, in the diff that makes it: a `<TargetFramework>` or `<TargetFrameworks>` of another major in a
`*.csproj`, `*.props` or `*.targets` file, a `global.json` on another SDK major, or a `FROM mcr.microsoft.com/dotnet/…`
image of another major (named in the `FROM` or in the default of the build argument it uses), or a `net<N>.0` moniker
in a script or workflow (`bin/Release/net10.0-windows/` in a `*.sh`, `*.ps1` or `.github/workflows/*.yml` file), which
breaks only once CI runs it (Censio#158; a version tag such as `v10.0.0` is no moniker). The major of
`global.json` is the repository's; without one, the target frameworks must agree
among themselves. A value built from an MSBuild property or a build argument (`$(…)`, `${…}`) is not read, and
`netstandard`, `netcoreapp` and .NET Framework monikers name no major. Moving to the next major is one change of all of
them together.

The *Analyzer gate* (`analyzer-check.py`) runs this check, also with `--lint-only`; it can run on its own:
    python3 .squad/tools/dotnet_major.py

Exit code 0 when every place names the same major, 1 otherwise.
"""
import json
import os
import re
import subprocess
import sys

TARGET = re.compile(r"<TargetFrameworks?>([^<]*)</TargetFrameworks?>", re.I)
MONIKER = re.compile(r"^net(\d+)\.\d+(?:-[\w.]+)?$", re.I)
IMAGE = re.compile(r"^\s*FROM\s+(?:--\S+\s+)*(?P<ref>(?:mcr\.microsoft\.com/)?dotnet/[\w./-]+:(?P<major>\d+)\.\S*)", re.I)
IMAGE_REF = re.compile(r"^(?:mcr\.microsoft\.com/)?dotnet/[\w./-]+:(?P<major>\d+)\.", re.I)
DOTNET_IMAGE = re.compile(r"^(?:mcr\.microsoft\.com/)?dotnet/[\w./-]+$", re.I)
ARG = re.compile(r"^\s*ARG\s+(?P<name>\w+)=(?P<value>\S+)", re.I)
PROJECT_FILES = (".csproj", ".props", ".targets")
SCRIPT_FILES = (".sh", ".bash", ".ps1")
WORKFLOW_DIR = ".github/workflows/"
# A target-framework moniker in running text: `net10.0`, `net10.0-windows`; not part of a longer word or version.
# A comment of a shell or PowerShell script or a workflow: from a `#` at the start of a line or after a blank.
SCRIPT_COMMENT = re.compile(r"(?:^|\s)#.*$")
SCRIPT_MONIKER = re.compile(r"(?<![\w.-])net(\d+)\.\d+(?:-[a-z][\w.]*)?(?![\w.])", re.I)
GLOBAL_JSON = "global.json"
LINE_COMMENT = re.compile(r"^\s*//.*$", re.M)


def tracked_files():
    """The repository's files, tracked and untracked but not ignored."""
    done = subprocess.run(["git", "ls-files", "--cached", "--others", "--exclude-standard"], capture_output=True,
                          text=True, check=False)
    return sorted({line.strip() for line in done.stdout.splitlines() if line.strip() and os.path.isfile(line.strip())})


def is_dockerfile(path):
    name = os.path.basename(path)
    return name == "Dockerfile" or name.endswith(".Dockerfile") or (
        name.startswith("Dockerfile.") and not name.endswith((".md", ".txt", ".html")))


def read(path):
    with open(path, encoding="utf-8-sig", errors="replace") as handle:
        return handle.read()


def target_majors(path, text):
    """[(path, line, major, what)] of the literal .NET target frameworks of a project or props file."""
    found = []
    for match in TARGET.finditer(text):
        line = text.count("\n", 0, match.start()) + 1
        for moniker in (part.strip() for part in match.group(1).split(";")):
            parsed = MONIKER.match(moniker)
            if parsed and int(parsed.group(1)) >= 5:
                found.append((path, line, int(parsed.group(1)), moniker))
    return found


def script_majors(path, text):
    """[(path, line, major, what)] of the target-framework monikers a script or workflow names (an output path); a
    comment is left out (`# SDK 11 builds a net10.0 project` in the managed session-start hook)."""
    found = []
    for number, line in enumerate(text.splitlines(), 1):
        for match in SCRIPT_MONIKER.finditer(SCRIPT_COMMENT.sub("", line)):
            if int(match.group(1)) >= 5:
                found.append((path, number, int(match.group(1)), match.group(0)))
    return found


def is_script(path):
    """A shell or PowerShell script, or a GitHub Actions workflow."""
    return path.endswith(SCRIPT_FILES) or (path.startswith(WORKFLOW_DIR) and path.endswith((".yml", ".yaml")))


def image_majors(path, text):
    """[(path, line, major, what)] of the .NET images a Dockerfile builds from: a `FROM` that names one, the default
    of an `ARG` that holds a whole image reference (`ARG BASE_SDK="mcr.microsoft.com/dotnet/sdk:10.0-alpine"`), and
    the default of a `<NAME>_TAG` argument beside a `<NAME>_IMAGE` that names a .NET image without a tag."""
    found, args = [], {}
    for number, line in enumerate(text.splitlines(), 1):
        match = IMAGE.match(line)
        if match:
            found.append((path, number, int(match.group("major")), match.group("ref")))
        arg = ARG.match(line)
        if arg:
            args[arg.group("name")] = (number, arg.group("value").strip("\"'"))
    for name, (number, value) in args.items():
        whole = IMAGE_REF.match(value)
        if whole:
            found.append((path, number, int(whole.group("major")), value))
            continue
        image = args.get(name[:-len("_TAG")] + "_IMAGE") if name.endswith("_TAG") else None
        tag = re.match(r"(\d+)\.", value)
        if image and tag and DOTNET_IMAGE.match(image[1]):
            found.append((path, number, int(tag.group(1)), f"{image[1]}:{value}"))
    return found


def sdk_major(text):
    """The major of `global.json`'s `sdk.version`, or None (no SDK pinned, or a file JSON cannot read)."""
    try:
        version = json.loads(LINE_COMMENT.sub("", text)).get("sdk", {}).get("version", "")
    except (ValueError, AttributeError):
        return None
    match = re.match(r"(\d+)\.", str(version))
    return int(match.group(1)) if match else None


def places(files):
    """(the SDK major of global.json or None, [(path, line, major, what)] of every target framework and image)."""
    sdk = sdk_major(read(GLOBAL_JSON)) if GLOBAL_JSON in files else None
    found = []
    for path in files:
        if path.endswith(PROJECT_FILES) and "/bin/" not in f"/{path}" and "/obj/" not in f"/{path}":
            found += target_majors(path, read(path))
        elif is_dockerfile(path):
            found += image_majors(path, read(path))
        elif is_script(path):
            found += script_majors(path, read(path))
    return sdk, found


def expected_major(sdk, found):
    """(the repository's major or None, what it comes from): `global.json`'s SDK, else the one major of the target
    frameworks, else the one major of the images."""
    if sdk is not None:
        return sdk, f"global.json (SDK {sdk}) names"
    for kind, test in (("the target frameworks name", lambda what: not IMAGE_REF.match(what)),
                       ("the images name", lambda what: bool(IMAGE_REF.match(what)))):
        majors = {major for _, _, major, what in found if test(what)}
        if majors:
            return (majors.pop(), kind) if len(majors) == 1 else (None, kind)
    return None, ""


def check(files=None):
    """Print every place whose major differs from the repository's; True when there is none."""
    sdk, found = places(tracked_files() if files is None else files)
    if not found and sdk is None:
        return True
    expected, source = expected_major(sdk, found)
    if expected is None:
        majors = ", ".join(str(major) for major in sorted({major for _, _, major, _ in found}))
        print(f".NET major: {source} {majors} and no global.json fixes one — every project and image stays on one "
              "major")
        for path, line, major, what in found:
            print(f"  {path}:{line}: {what} (.NET {major})")
        return False
    wrong = [place for place in found if place[2] != expected]
    for path, line, major, what in wrong:
        print(f"{path}:{line}: {what} is .NET {major}, but {source} .NET {expected}: every project and image stays on "
              "one major; the next major is one change of all of them")
    if not wrong:
        print(f".NET major: {expected} everywhere ({len(found)} target framework(s), image(s) and script path(s); {source} it)")
    return not wrong


def main():
    os.chdir(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
    ok = check()
    print("PASS" if ok else "FAIL")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
