#!/usr/bin/env python3
"""Coverage gate for the squad: line coverage of new/changed production lines (like SonarQube's
"coverage on new code") and overall line coverage, merged from all coverage reports
(one per test project) of the latest test run; *Test with coverage* clears the results directory first.

Supported report formats (set COVERAGE_FORMAT in `.squad/tools/squad_settings.py`):
- `cobertura` — e.g. coverlet's `coverage.cobertura.xml` (.NET), or any Cobertura XML
- `lcov`      — `lcov.info` (Node: c8, Node's test runner, Jest, Vitest, ...)
- `go`        — a `go test -coverprofile` file

The base (origin/main), the report location and the production-code paths are fixed in
`squad_settings.py`, not taken from the command line, so nothing user-supplied reaches git or the
filesystem.

Usage, from the repository root, after *Test with coverage* from `.squad/stack.md`:
    python3 .squad/tools/coverage-check.py [--threshold 80]

Exit code 0 when both values reach the threshold, 1 otherwise.
"""
import argparse
import glob
import os
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import squad_settings as settings  # noqa: E402  (per-repository settings next to this script)

BASE_REF = "origin/main"


def repo_path(path):
    """Normalize a report path to a repository-relative path with forward slashes."""
    return os.path.relpath(os.path.abspath(path)).replace(os.sep, "/")


def changed_lines():
    """Return {repo-relative path: set(line numbers)} of lines added or changed since the merge base with
    origin/main (working tree included) in the production paths from squad_settings."""
    merge_base = subprocess.run(
        ["git", "merge-base", BASE_REF, "HEAD"], capture_output=True, text=True, check=True).stdout.strip()
    pathspecs = list(settings.COVERAGE_PATHSPECS) + [f":(exclude){p}" for p in settings.COVERAGE_EXCLUDES]
    diff = subprocess.run(
        ["git", "diff", "-U0", merge_base, "--", *pathspecs],
        capture_output=True, text=True, check=True).stdout
    result, current = {}, None
    for line in diff.splitlines():
        if line.startswith("+++ "):
            path = line[4:]
            current = path[2:] if path.startswith("b/") else None
            if current:
                result.setdefault(current, set())
        elif line.startswith("@@") and current:
            match = re.search(r"\+(\d+)(?:,(\d+))?", line)
            start, count = int(match.group(1)), int(match.group(2) or "1")
            result[current].update(range(start, start + count))
    return result


def run_reports():
    """Every report the glob matches: one per test project of the latest run. *Test with coverage* clears the
    results directory first, so no report of an earlier run can be among them."""
    reports = sorted(glob.glob(settings.COVERAGE_REPORT_GLOB, recursive=True))
    if not reports:
        sys.exit(f"No coverage report matches {settings.COVERAGE_REPORT_GLOB} - run *Test with coverage* first")
    return reports


def add_hit(hits, path, number, count):
    lines = hits.setdefault(path, {})
    lines[number] = max(lines.get(number, 0), count)


def load_cobertura(report):
    # Local report from the repo's own test run; the tool stays stdlib-only, so no defusedxml.
    root = ET.parse(report).getroot()  # noqa: S314  # nosec B314
    sources = [s.text.rstrip("/\\") for s in root.iter("source") if s.text]
    hits = {}
    for cls in root.iter("class"):
        filename = cls.get("filename")
        for src in sources:
            candidate = os.path.join(src, filename)
            if os.path.exists(candidate):
                filename = candidate
                break
        path = repo_path(filename)
        for ln in cls.iter("line"):
            add_hit(hits, path, int(ln.get("number")), int(ln.get("hits")))
    return hits


def load_lcov(report):
    hits, path = {}, None
    with open(report, encoding="utf-8") as handle:
        for raw in handle:
            line = raw.strip()
            if line.startswith("SF:"):
                path = repo_path(line[3:])
            elif line.startswith("DA:") and path:
                number, count = line[3:].split(",")[:2]
                add_hit(hits, path, int(number), int(float(count)))
            elif line == "end_of_record":
                path = None
    return hits


def go_module():
    with open("go.mod", encoding="utf-8") as handle:
        for line in handle:
            if line.startswith("module "):
                return line.split()[1].strip()
    sys.exit("go.mod has no module line")


def load_go(report):
    module, hits = go_module(), {}
    block = re.compile(r"^(.+):(\d+)\.\d+,(\d+)\.\d+ (\d+) (\d+)$")
    with open(report, encoding="utf-8") as handle:
        for raw in handle:
            match = block.match(raw.strip())
            if not match:
                continue
            name, start, end, statements, count = match.groups()
            if int(statements) == 0:
                continue
            path = name[len(module) + 1:] if name.startswith(module + "/") else name
            for number in range(int(start), int(end) + 1):
                add_hit(hits, path, number, int(count))
    return hits


LOADERS = {"cobertura": load_cobertura, "lcov": load_lcov, "go": load_go}


def merged_hits(loader):
    """Merge every report matching COVERAGE_REPORT_GLOB into one map of hits per file and line."""
    hits = {}
    reports = run_reports()
    print(f"Coverage reports merged: {len(reports)}")
    for report in reports:
        for path, lines in loader(report).items():
            for number, count in lines.items():
                add_hit(hits, path, number, count)
    return hits


def overall_coverage(hits):
    """Return (percent, hit lines, coverable lines) over the tracked production files."""
    tracked = set(subprocess.run(
        ["git", "ls-files", "--", *settings.COVERAGE_PATHSPECS,
         *[f":(exclude){p}" for p in settings.COVERAGE_EXCLUDES]],
        capture_output=True, text=True, check=True).stdout.splitlines())
    production = {path: lines for path, lines in hits.items() if path in tracked}
    total = sum(len(lines) for lines in production.values())
    total_hit = sum(1 for lines in production.values() for count in lines.values() if count > 0)
    return (total_hit / total * 100 if total else 100.0), total_hit, total


def new_code_coverage(hits):
    """Print the changed production files and returns (percent, hit lines, coverable lines)."""
    covered = coverable = 0
    print("Changed production files (coverable changed lines):")
    for path, lines in sorted(changed_lines().items()):
        file_hits = hits.get(path, {})
        relevant = [n for n in lines if n in file_hits]
        hit = sum(1 for n in relevant if file_hits[n] > 0)
        covered, coverable = covered + hit, coverable + len(relevant)
        missed = sorted(n for n in relevant if file_hits[n] == 0)
        rate = f"{hit / len(relevant) * 100:5.1f}%" if relevant else "  n/a "
        print(f"  {rate}  {hit}/{len(relevant)}  {path}" + (f"  uncovered: {missed}" if missed else ""))
    return (covered / coverable * 100 if coverable else 100.0), covered, coverable


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--threshold", type=float, default=80.0)
    args = parser.parse_args()

    os.chdir(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
    loader = LOADERS.get(settings.COVERAGE_FORMAT)
    if loader is None:
        sys.exit(f"Unknown COVERAGE_FORMAT '{settings.COVERAGE_FORMAT}' in squad_settings.py")
    hits = merged_hits(loader)
    overall, total_hit, total = overall_coverage(hits)
    new_code, covered, coverable = new_code_coverage(hits)

    print(f"\nNew/changed code: {new_code:.1f}% ({covered}/{coverable} lines)")
    print(f"Overall:          {overall:.1f}% ({total_hit}/{total} lines)")
    ok = new_code >= args.threshold and overall >= args.threshold
    print(f"Threshold {args.threshold:.0f}%: {'PASS' if ok else 'FAIL'}")
    return 0 if ok else 1

if __name__ == "__main__":
    sys.exit(main())
