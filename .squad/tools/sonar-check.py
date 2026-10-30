#!/usr/bin/env python3
"""Sonar gate: the CI code analysis (SonarQube Cloud) read as a check, so its findings are a gate result
instead of a surprise after the pull request, and a rule the local analyzers do not know is no squad lesson.

Two modes:

- `branch -- <scan command>` (step 7, before the pull request): needs a SonarQube credential and a
  committed work branch (the scanner uploads the working tree; an uncommitted scratch file would be published
  with a public project) — only the coverage reports of *Test with coverage* may lie around untracked. Runs
  the scan command (*Sonar gate* in `.squad/stack.md`: the stack's scanner, analysing the work branch as a
  branch of the project), waits for SonarQube to process the report, and lists every open issue and security
  hotspot on a line the branch added or changed since the merge base with BASE_REF. Without a credential, or
  when the scan fails (a network policy that blocks the scanner's download hosts, for example), the result is NOT
  CHECKED, and the pull request mode runs on the draft pull request.
- `pr <number>` (step 11, on the draft pull request): reads the analysis of that pull request (with
  `SONAR_TOKEN` when it is set, which a private project needs) and checks that it belongs to the current head
  (`HEAD`, or `--head <sha>`); an analysis of an earlier head, or one without a quality gate yet, is PENDING,
  never a pass. Then lists the quality gate status, the open issues and the security hotspots to review.

The credential is a network secret of the cloud environment for the SonarQube hosts (the agent proxy attaches
it to every request to them, so the session never sees the token; `api/users/current` tells whether one is
attached), or else `SONAR_TOKEN` in the environment, which every process of the session can read.

The project comes from `SONAR_PROJECT_KEY` in `.squad/tools/squad_settings.py`, else `sonar.projectKey` in
`sonar-project.properties`, else the `/k:` argument of the scanner in a workflow under `.github/workflows/`; the
server from `SONAR_HOST` (default https://sonarcloud.io); the scanner's report file from `SONAR_REPORT_TASK`
(default `.scannerwork/report-task.txt`).

Two reads have a second endpoint for a credential that may browse the project but not use the first one (a
403 from the proxy-authenticated session): the pull request's commit comes from `project_analyses/search` when
`project_pull_requests/list` is refused, and the scan's processing status from `ce/activity` when `ce/task` is
refused.

A read that SonarQube answers with an HTTP error is NOT CHECKED with the request (path and parameters, never the
token) and the response body, so a rejected parameter can be told from a missing project or the proxy.

Every finding is printed as `rule path:line message` and fixed like an analyzer diagnostic. A rule that no local
analyzer reports is noted in the *Analyzer gate* section of `.squad/stack.md` (project knowledge, in the same
change), not filed as a squad lesson.

Usage, from anywhere in the repository:
    python3 .squad/tools/sonar-check.py branch -- <scan command and arguments>
    python3 .squad/tools/sonar-check.py pr <number> [--head <sha>]

Exit code 0 clean, 1 findings or a failed quality gate, 2 NOT CHECKED (no credential, no project key, a dirty tree,
the scan or SonarQube failed), 3 PENDING (the analysis of this head is not there yet: wait and run again),
4 wrong usage (a mistyped command line never reads as a skipped gate).
"""
import argparse
import base64
import fnmatch
import glob
import json
import os
import re
import shutil
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from analyzer_common import BASE_REF, changed_line_map, git, settings  # noqa: E402  (shared helpers next to this script)

CLEAN, FINDINGS, NOT_CHECKED, PENDING, USAGE = 0, 1, 2, 3, 4
HOST = getattr(settings, "SONAR_HOST", "https://sonarcloud.io").rstrip("/")
REPORT_TASK = getattr(settings, "SONAR_REPORT_TASK", ".scannerwork/report-task.txt")
ERROR_BODY_CHARS = 500
WAIT_SECONDS = 600
POLL_SECONDS = 5
PAGE_SIZE = 500
FILES_PER_QUERY = 50
KEY_ARGUMENT = re.compile(r'/k:"?([\w.:-]+)"?')


class NotChecked(Exception):
    """The gate could not run; the message says why."""


class ApiError(OSError):
    """SonarQube answered a request with an HTTP error; the message names the request and the response."""

    def __init__(self, message, status=None):
        super().__init__(message)
        self.status = status


class Usage(argparse.ArgumentParser):
    def error(self, message):
        self.print_usage(sys.stderr)
        self.exit(USAGE, f"{self.prog}: error: {message}\n")


def project_key():
    """The SonarQube project key, or None when no source names one."""
    key = getattr(settings, "SONAR_PROJECT_KEY", "")
    if key:
        return key
    if os.path.isfile("sonar-project.properties"):
        with open("sonar-project.properties", encoding="utf-8") as handle:
            for line in handle:
                name, _, value = line.partition("=")
                if name.strip() == "sonar.projectKey" and value.strip() and "{{" not in value:
                    return value.strip()
    for workflow in sorted(glob.glob(".github/workflows/*.y*ml")):
        with open(workflow, encoding="utf-8") as handle:
            match = KEY_ARGUMENT.search(handle.read())
        if match and "{{" not in match.group(1):
            return match.group(1)
    return None


def api(path, params, token=None):
    """GET a SonarQube Web API endpoint and return the decoded JSON."""
    if not HOST.startswith("https://"):
        raise NotChecked(f"SONAR_HOST must be an https:// URL, not {HOST!r}")
    url = f"{HOST}/api/{path}?{urllib.parse.urlencode(params)}"
    # Only an https:// URL reaches urllib (checked above), so no file:// or custom scheme can be opened.
    request = urllib.request.Request(url)  # noqa: S310  # nosec B310
    if token:  # never forwarded to another host on a redirect
        request.add_unredirected_header("Authorization", "Basic " + base64.b64encode(f"{token}:".encode()).decode())
    try:
        with urllib.request.urlopen(request, timeout=60) as response:  # noqa: S310  # nosec B310
            return json.load(response)
    except urllib.error.HTTPError as error:
        # The URL carries the parameters but never the token (it travels in a header), so it may be printed.
        body = error.read().decode("utf-8", "replace").strip()[:ERROR_BODY_CHARS]
        raise ApiError(f"HTTP {error.code} for GET {url}: {body or 'empty response body'}", error.code) from error


def proxy_credential():
    """True when SonarQube accepts requests that carry no token of their own: a network secret the agent proxy
    attaches to the requests for the SonarQube hosts."""
    try:
        return api("users/current", {}).get("isLoggedIn") is True  # `authentication/validate` is true anonymously too
    except (OSError, ValueError):  # URLError is an OSError
        return False


def paged(path, params, field, token=None):
    """Every item of a paged list endpoint."""
    items, page = [], 1
    while True:
        data = api(path, {**params, "p": page, "ps": PAGE_SIZE}, token)
        items += data.get(field, [])
        total = data.get("paging", {}).get("total", data.get("total", 0))
        if page * PAGE_SIZE >= total:
            return items
        page += 1


def file_of(component, key):
    return component[len(key) + 1:] if component.startswith(key + ":") else component


def finding_line(item, key, kind):
    """`rule path:line message` for an issue or a hotspot."""
    path = file_of(item.get("component", ""), key)
    line = item.get("line") or item.get("textRange", {}).get("startLine") or "-"
    rule = item.get("rule") or item.get("ruleKey") or kind
    return f"{rule} {path}:{line} {item.get('message', '')}".rstrip()


def on_changed_line(item, key, lines):
    """True when the finding sits on a changed line, or is a file-level finding of a changed file."""
    path = file_of(item.get("component", ""), key)
    if path not in lines:
        return False
    line = item.get("line") or item.get("textRange", {}).get("startLine")
    return line is None or line in lines[path]


def report(findings, gate=None):
    """Print the result and return the exit code."""
    for finding in findings:
        print(finding)
    failed_gate = gate not in (None, "OK")
    if failed_gate:
        print(f"Quality gate: {gate}")
    if findings or failed_gate:
        print(f"FAIL: {len(findings)} finding(s). Fix them like analyzer diagnostics; a rule no local analyzer "
              "reports is noted in the *Analyzer gate* section of .squad/stack.md.")
        return FINDINGS
    print("PASS: no new SonarQube issue or hotspot" + (" and the quality gate passed" if gate else ""))
    return CLEAN


def analysed_commit(key, number, token=None):
    """The commit SonarQube analysed last for the pull request, or "" when there is no analysis."""
    try:
        analyses = api("project_pull_requests/list", {"project": key}, token).get("pullRequests", [])
    except ApiError as error:
        if error.status != 403:
            raise
        found = api("project_analyses/search", {"project": key, "pullRequest": number, "ps": 1}, token)
        return next((a.get("revision", "") for a in found.get("analyses", [])), "")
    analysis = next((a for a in analyses if a.get("key") == str(number)), None)
    return (analysis or {}).get("commit", {}).get("sha", "")


def check_pr(key, number, head, token=None):
    """The analysis of the pull request, matched to the head."""
    analysed = analysed_commit(key, number, token)
    if len(head) < 7 or not analysed or len(head) < 7 or not analysed.startswith(head):  # a full SHA, or an abbreviation of it
        print(f"PENDING: SonarQube has no analysis of pull request {number} at {head[:12]} yet"
              + (f" (latest: {analysed[:12]})" if analysed else "") + ". PENDING says that a newer analysis than "
              "the one shown is still to come, not that the head has a problem: wait and run again. Still PENDING "
              "after every CI check of this head finished: the analysis cannot be read, and the PR stays a draft.")
        return PENDING
    params = {"projectKey": key, "pullRequest": number}
    gate = api("qualitygates/project_status", params, token)["projectStatus"]["status"]
    if gate == "NONE":
        print(f"PENDING: the analysis of {head[:12]} has no quality gate status yet; wait and run again.")
        return PENDING
    issues = paged("issues/search", {"componentKeys": key, "pullRequest": number, "resolved": "false"}, "issues",
                   token)
    hotspots = paged("hotspots/search", {**params, "status": "TO_REVIEW"}, "hotspots", token)
    return report([finding_line(i, key, "issue") for i in issues]
                  + [finding_line(h, key, "hotspot") for h in hotspots], gate)


def ce_task_id():
    if os.path.isfile(REPORT_TASK):
        with open(REPORT_TASK, encoding="utf-8") as handle:
            for line in handle:
                name, _, value = line.strip().partition("=")
                if name == "ceTaskId":
                    return value
    return None


def ce_task(task, token):
    """The compute engine task; `ce/activity` answers for a credential that `ce/task` refuses."""
    try:
        return api("ce/task", {"id": task}, token)["task"]
    except ApiError as error:
        if error.status != 403:
            raise
        key = project_key()
        for candidate in api("ce/activity", {"component": key, "ps": 20}, token).get("tasks", []):
            if candidate.get("id") == task:
                return candidate
        return {"id": task, "status": "PENDING"}


def wait_for_task(task, token):
    """Wait until SonarQube has processed the uploaded report; return the task (its status says how it ended)."""
    deadline = time.monotonic() + WAIT_SECONDS
    while True:
        result = ce_task(task, token)
        if result["status"] not in ("PENDING", "IN_PROGRESS"):
            return result
        if time.monotonic() >= deadline:
            return {**result, "status": "TIMEOUT"}
        time.sleep(POLL_SECONDS)


def scan(command):
    """Run the scan command; return the CE task id, or None when the scan failed or could not start."""
    output_dir = REPORT_TASK.split("/", 1)[0]
    left_behind = not os.path.exists(output_dir)
    try:
        result = subprocess.run(command, check=False)  # the command line of *Sonar gate* in .squad/stack.md
        return ce_task_id() if result.returncode == 0 else None
    except OSError as error:
        print(f"The scan command could not start: {error}")
        return None
    finally:
        if left_behind:  # leave no untracked scanner output behind
            shutil.rmtree(output_dir, ignore_errors=True)


def coverage_reports():
    """The globs of the coverage reports *Test with coverage* writes: untracked output the gate expects."""
    pairs = getattr(settings, "COVERAGE_REPORTS", None) or [
        (getattr(settings, "COVERAGE_FORMAT", ""), getattr(settings, "COVERAGE_REPORT_GLOB", ""))]
    return [pattern for _, pattern in pairs if pattern]


def unexpected_files():
    """Changed tracked files, and untracked files other than the coverage reports, which the scanner would upload."""
    changed = git("diff", "--name-only", "HEAD").splitlines()
    reports = coverage_reports()
    untracked = [path for path in git("ls-files", "--others", "--exclude-standard").splitlines()
                 if not any(fnmatch.fnmatch(path, pattern) or path.startswith(pattern.split("/", 1)[0] + "/")
                            for pattern in reports)]
    return [path for path in changed + untracked if path.strip()]


def branch_preconditions():
    """The current branch, when branch mode may scan it."""
    branch = git("branch", "--show-current").strip()
    base = BASE_REF.split("/", 1)[1] if BASE_REF.startswith("origin/") else BASE_REF
    if not branch or branch == base:
        raise NotChecked("branch mode scans a work branch only (not a detached HEAD or the base branch, whose "
                         "analysis it would replace)")
    unexpected = unexpected_files()
    if unexpected:
        raise NotChecked("uncommitted or untracked files the scanner would upload — commit the change first (no "
                         "push needed) and remove scratch files: " + ", ".join(unexpected[:10]))
    return branch


def findings_on_branch(key, branch, token):
    """Open issues and hotspots of the analysed branch on the lines the branch changed."""
    lines = changed_line_map(merge_base_or_raise())
    paths = sorted(lines)
    found = []
    for start in range(0, len(paths), FILES_PER_QUERY):
        chunk = paths[start:start + FILES_PER_QUERY]
        components = ",".join(f"{key}:{path}" for path in chunk)
        issues = paged("issues/search", {"componentKeys": components, "branch": branch, "resolved": "false"},
                       "issues", token)
        found += [finding_line(i, key, "issue") for i in issues if on_changed_line(i, key, lines)]
        hotspots = paged("hotspots/search", {"projectKey": key, "branch": branch, "files": ",".join(chunk),
                                             "status": "TO_REVIEW"}, "hotspots", token)
        found += [finding_line(h, key, "hotspot") for h in hotspots if on_changed_line(h, key, lines)]
    return found


def merge_base_or_raise():
    result = subprocess.run(["git", "merge-base", BASE_REF, "HEAD"], capture_output=True, text=True, check=False)
    if result.returncode != 0:
        raise NotChecked(f"no merge base with {BASE_REF}: fetch it first (git fetch origin <branch>)")
    return result.stdout.strip()


def check_branch(key, command, token):
    if not command:
        print("Usage: branch mode needs the scan command after `--` (*Sonar gate* in .squad/stack.md)")
        return USAGE
    branch = branch_preconditions()
    task = scan(command)
    if not task:
        raise NotChecked("the scan failed or wrote no report (output above); check the draft pull request with "
                         "`sonar-check.py pr <number>` in step 11 instead")
    result = wait_for_task(task, token)
    if result["status"] != "SUCCESS":
        raise NotChecked(f"SonarQube processed the report with status {result['status']}")
    return report(findings_on_branch(key, result.get("branch") or branch, token))


def parse(argv):
    parser = Usage(prog="sonar-check.py", description="SonarQube Cloud findings as a gate result.")
    modes = parser.add_subparsers(dest="mode", required=True, parser_class=Usage)
    branch = modes.add_parser("branch", help="scan the work branch (needs a SonarQube credential)")
    branch.add_argument("command", nargs=argparse.REMAINDER, help="-- followed by the *Sonar gate* scan command")
    pr = modes.add_parser("pr", help="check the analysis of a pull request")
    pr.add_argument("number", type=int)
    pr.add_argument("--head", help="commit SHA the analysis must belong to (default: HEAD)")
    return parser.parse_args(argv)


def run(args):
    key = project_key()
    if not key:
        raise NotChecked("no SonarQube project key (SONAR_PROJECT_KEY in .squad/tools/squad_settings.py, "
                         "sonar.projectKey in sonar-project.properties or /k: in a workflow)")
    token = os.environ.get("SONAR_TOKEN", "")
    if args.mode == "pr":
        head = args.head or git("rev-parse", "HEAD").strip()
        ahead = subprocess.run(["git", "rev-list", "--count", "@{u}..HEAD"], capture_output=True, text=True,
                               check=False).stdout.strip()
        if not args.head and ahead not in ("", "0"):
            print("Note: HEAD has commits that are not pushed; the pull request cannot have been analysed at it.")
        return check_pr(key, args.number, head, token or None)
    if not token and not proxy_credential():
        raise NotChecked("no SonarQube credential: neither a network secret for the SonarQube hosts nor "
                         "SONAR_TOKEN in this environment; check the draft pull request with "
                         "`sonar-check.py pr <number>` in step 11 instead")
    command = args.command[1:] if args.command[:1] == ["--"] else args.command
    return check_branch(key, command, token or None)


def main(argv=None):
    args = parse(argv)
    try:
        os.chdir(git("rev-parse", "--show-toplevel").strip())
        return run(args)
    except NotChecked as reason:
        print(f"NOT CHECKED: {reason}.")
    except (OSError, subprocess.CalledProcessError, KeyError, ValueError) as error:  # URLError is an OSError
        print(f"NOT CHECKED: SonarQube or git could not be read ({error}).")
    return NOT_CHECKED


if __name__ == "__main__":
    sys.exit(main())
