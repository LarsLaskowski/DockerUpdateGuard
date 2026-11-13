#!/usr/bin/env python3
"""Analyzer gate: every Roslyn diagnostic in a changed file, at any severity, as SonarQube Cloud sees it.

SonarQube Cloud imports *all* Roslyn diagnostics from the build's SARIF error log - including info-level
ones such as the MSTest analyzer rules (MSTEST####), which never appear as build warnings. A console
build therefore hides issues that the quality gate later reports. This script runs a full, non-incremental
Release build with an SARIF error log per project and reports every non-suppressed diagnostic (RH####,
S####, MSTEST####, CA####, ...) located in a file changed since the merge base with origin/main
(working tree and untracked files included); a diagnostic without a location (an assembly-level rule such as
MSTEST0001) counts for its project, reported with the project folder, when a file of that project changed. Every
run deletes the SARIF logs (`obj/roslyn.sarif`) before its build: one read by hand is current only right after a
run of this script. Before it, `dotnet_major.py` checks that every project, `global.json`
and every .NET image name the same .NET major, and `dotnet_config.py` the build structure and the template's rules
(the dotnet-rules blocks current, no rule switched off outside them), also with `--lint-only`.

The build command (solution from `.squad/tools/squad_settings.py`), the base (origin/main) and the log
location are fixed on purpose: the script accepts one flag, `--lint-only` (the lint pass over changed shell
scripts, workflows and Dockerfiles without the build, for a `patch` that touches no C# — `.squad/routing.md`,
*Gates of a patch*), and no value, so nothing user-supplied reaches the shell, git or the filesystem.

Usage, from the repository root (after *Restore* from `.squad/stack.md`):
    python3 .squad/tools/analyzer-check.py [--lint-only]

Runs are serialized with a lock file (obj/analyzer-check.lock), because two concurrent full builds of the
solution break each other. The check fails if the build fails or if any project produced no SARIF log.

Exit code 0 when no changed file has a diagnostic, 1 otherwise.
"""
import fcntl
import glob
import json
import os
import subprocess
import sys
from urllib.parse import unquote, urlparse

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import squad_settings as settings  # noqa: E402  (per-repository settings next to this script)
from analyzer_common import git, merge_base, lint_check, lint_only_requested  # noqa: E402  (shared helpers next to this script)
import dotnet_major  # noqa: E402  (one .NET major per repository; next to this script)
import dotnet_config  # noqa: E402  (one build structure and the template's rules; next to this script)

SARIF_NAME = os.path.join("obj", "roslyn.sarif")
LOCK_FILE = os.path.join("obj", "analyzer-check.lock")
BUILD = ["dotnet", "build", settings.SOLUTION, "-c", "Release", "--no-restore", "--no-incremental",
         "-p:ErrorLog=" + SARIF_NAME + "%2Cversion=2.1"]


def changed_files():
    base = merge_base()
    names = git("diff", "--name-only", base).splitlines()
    names += git("ls-files", "--others", "--exclude-standard").splitlines()
    return {name.strip() for name in names if name.strip()}


def to_repo_path(uri, root):
    parsed = urlparse(uri)
    path = unquote(parsed.path) if parsed.scheme == "file" else unquote(uri)
    return os.path.relpath(os.path.realpath(path), root).replace(os.sep, "/")


def diagnostics(root):
    """(path, line, rule, level, message) of every diagnostic of the SARIF logs. A diagnostic without a location (an
    assembly-level rule such as MSTEST0001) belongs to its project: its path is the project folder with a trailing
    `/`, line 0 (Vandox#430)."""
    for log in glob.glob(os.path.join("**", SARIF_NAME), recursive=True):
        with open(log, encoding="utf-8-sig") as handle:
            data = json.load(handle)
        project = os.path.dirname(os.path.dirname(log)).replace(os.sep, "/")
        for run in data.get("runs", []):
            for result in run.get("results", []):
                if result.get("suppressions"):
                    continue
                yield located(result, project, root)


def located(result, project, root):
    locations = result.get("locations") or []
    physical = locations[0].get("physicalLocation", {}) if locations else {}
    uri = physical.get("artifactLocation", {}).get("uri", "")
    folder = f"{project}/" if project else "./"
    path = to_repo_path(uri, root) if uri else folder
    line = physical.get("region", {}).get("startLine", 0)
    return path, line, result.get("ruleId", "?"), result.get("level", "warning"), \
        result.get("message", {}).get("text", "")


def in_changed_file(path, changed):
    """A diagnostic of a changed file, or one of a project (path ending in `/`) with a changed file in it."""
    if path.endswith("/"):
        prefix = "" if path == "./" else path
        return any(name.startswith(prefix) for name in changed)
    return path in changed


def project_dirs():
    return sorted(os.path.dirname(project) for project in glob.glob(os.path.join("**", "*.csproj"), recursive=True)
                  if os.sep + "bin" + os.sep not in project and os.sep + "obj" + os.sep not in project)


def main():
    os.chdir(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
    if lint_only_requested():
        ok = dotnet_major.check()
        ok = dotnet_config.check() and ok
        ok = lint_check() and ok
        print("PASS" if ok else "FAIL")
        return 0 if ok else 1
    os.makedirs("obj", exist_ok=True)
    with open(LOCK_FILE, "w", encoding="utf-8") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        return run_check()


def run_check():
    root = os.path.realpath(os.getcwd())
    for stale in glob.glob(os.path.join("**", SARIF_NAME), recursive=True):
        os.remove(stale)
    build = subprocess.run(BUILD, capture_output=True, text=True, check=False)
    if build.returncode != 0:
        print(build.stdout[-4000:])
        print("Build failed.")
        return 1
    missing = [d for d in project_dirs() if not os.path.isfile(os.path.join(d, SARIF_NAME))]
    if missing:
        print("No SARIF log produced for: " + ", ".join(missing))
        print("FAIL")
        return 1

    changed = changed_files()
    found = sorted(set(diagnostics(root)))
    in_changed = [d for d in found if in_changed_file(d[0], changed)]
    elsewhere = len(found) - len(in_changed)

    for path, line, rule, level, message in in_changed:
        print(f"{path}({line}): {level} {rule}: {message}")
    print(f"\nDiagnostics in changed files: {len(in_changed)}")
    print(f"Diagnostics in unchanged files (not gating): {elsewhere}")
    ok = dotnet_major.check()
    ok = dotnet_config.check() and ok
    ok = lint_check() and not in_changed and ok
    print("PASS" if ok else "FAIL")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
