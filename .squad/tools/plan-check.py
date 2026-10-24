#!/usr/bin/env python3
"""Plan gate: the structure of a squad work folder checked by a script instead of remembered by a reviewer.

Run by the orchestrator after every Lead launch that writes or revises the work folder (step 2), before any
other member reads it; a finding goes back to the Lead (mode `revise`, the findings only), without a challenge
round (*Loop limits* in `.squad/routing.md`).

Checks, in the work folder `specs/<folder>/`:

- **Feature** (`spec.md` exists): every section of the spec template is filled — *Problem*, *Scenarios*,
  *Errors and edge cases*, *Non-functional requirements*, *Acceptance criteria*, *Out of scope*, *Open
  questions*, *Area documents* ("none" counts as filled, the template's guidance comments do not); there is at
  least one scenario `S<n>` and one criterion written `- [ ] AC<n>: …`, and a line that names a criterion in
  another form is reported; every scenario and edge case `E<n>` is named by a criterion or listed under *Out of
  scope*; every open question is answered (`Answered:`) or the section says "none"; `plan.md` defines no
  criterion of its own; every criterion of the spec has a row in the plan's *Tests per criterion* and appears in
  the *Criteria* column of `tasks.md` (every table of it), and neither names a criterion the spec lacks; every
  task row names its *Group* (the unit steps 4–6 run as one launch per member), and a group's rows are
  consecutive; when the plan's *Signatures* lists any API, every group has a *Skeleton* row (its *Task* cell
  starts with "Skeleton") or a row saying "no new API", a group the Lead owns alone excepted, and every name in
  *Signatures* (the identifier of each backticked declaration) appears in a skeleton row of some group.
- **Bug fix** (no `spec.md`): `plan.md` defines its acceptance criteria, and each has a row in *Tests per
  criterion* (tier `trivial`, which writes no tests first, needs none).
- **Both:** the *Tier* line names exactly one tier, and the *Security areas touched* line says "none" or lists
  them (it decides the Security plan review); *Verified facts* has at least
  one row, and every row has evidence and a status (`confirmed`, `refuted`, or `unverified` with the measurement
  that settles it, starting with `to verify:`); no template placeholder is left; no Markdown file of the work
  folder contains a control character (`scope-check.py`'s check, here before any member reads the folder). A
  plan longer than `PLAN_MAX_WORDS` (`squad_settings.py`, default 4000) is reported as a warning, one longer than
  `PLAN_HARD_WORDS` (default twice that) as a finding: a long plan costs every member that reads it, so the
  Lead moves settled material (answered challenge entries, facts verified in earlier rounds) into the decision
  record or the area document.

Ids may be written as ranges (`AC1–AC3`, `S2-S4`); backticks and emphasis around a cell are ignored. A criterion
without a test, in a plan whose *Verification without tests* section names it, counts as covered.

With `--spec` only `spec.md` is checked: after the Lead's mode `spec`, before the spec challenge.

Usage, from the repository root:
    python3 .squad/tools/plan-check.py <work folder name, e.g. feature-12-export or issue-34> [--spec]

Exit code 0 when the work folder passes, 1 otherwise.
"""
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import squad_settings as settings  # noqa: E402  (per-repository settings next to this script)
from analyzer_common import control_characters  # noqa: E402  (shared helpers next to this script)

PLAN_MAX_WORDS = getattr(settings, "PLAN_MAX_WORDS", 4000)
PLAN_HARD_WORDS = getattr(settings, "PLAN_HARD_WORDS", 2 * PLAN_MAX_WORDS)
TIERS = ("trivial", "standard", "security")
STATUSES = ("confirmed", "refuted", "unverified")
SPEC_SECTIONS = ["Problem", "Scenarios", "Errors and edge cases", "Non-functional requirements",
                 "Acceptance criteria", "Out of scope", "Open questions", "Area documents"]
CRITERION = re.compile(r"^\s*- \[[ xX]\] (AC\d+):", re.M)
CASE = re.compile(r"^\s*- (?:\*\*)?([SE]\d+)(?:\*\*)?:", re.M)
# A range repeats the prefix (`AC1\u2013AC3`, `S2 .. S4`) or joins the numbers without spaces (`AC1-3`), so
# that "E1 \u2013 10 s at most" stays one id and a number.
RANGE = re.compile(r"\b(AC|S|E)(\d+)(?:\s*(?:[-\u2013\u2014]|\.\.)\s*\1(\d+)|[-\u2013](\d+))\b")
ID = re.compile(r"\b(AC|S|E)(\d+)\b")
ELLIPSIS = r"(?:\.\.\.|\u2026)"
PLACEHOLDERS = [re.compile(p) for p in (r"<title>", r"Issue #<number>", r"<one-sentence justification>",
                                         r"NNNN-title", rf"\bAC\d+: {ELLIPSIS}\s*$",
                                         rf"^\s*- [SE]\d+: {ELLIPSIS}\s*$", rf"^\|.*\|\s*{ELLIPSIS}\s*\|")]
EMPTY_CELLS = {"", "-", "\u2013", "\u2014", "...", "\u2026", "none", "n/a", "tbd", "todo"}
COMMENT = re.compile(r"<!--.*?-->", re.S)
EMPHASIS = "`*_"
SPEC = "spec.md"
NONE = re.compile(r"^\s*(?:[-*]\s+)?none\.?\s*$", re.I)
SKELETON_ROW = re.compile(r"^skeleton\b", re.I)
NO_NEW_API = re.compile(r"\bno new API\b", re.I)
SPAN = re.compile(r"`([^`\n]+)`")
DECLARATION_KEYWORDS = {"func", "def", "fn", "public", "private", "protected", "internal", "static", "const",
                        "readonly", "final", "async", "export", "function", "type", "class", "struct", "record",
                        "interface", "enum", "var", "let", "val", "abstract", "override"}
TOP_ITEM = re.compile(r"^(?:[-*]|\d+[.)])\s+")


def read(path, errors):
    if not os.path.isfile(path):
        return None
    try:
        with open(path, encoding="utf-8-sig") as handle:
            return handle.read()
    except UnicodeDecodeError:
        errors.append(f"{os.path.basename(path)}: not UTF-8")
        return ""


def resolve_folder(root, folder):
    """The work folder under `specs/`, found in the directory listing so the argument never becomes a path."""
    wanted = folder.strip().replace("\\", "/").rstrip("/")
    wanted = wanted[len("specs/"):] if wanted.startswith("specs/") else wanted
    specs = os.path.join(root, "specs")
    if not os.path.isdir(specs):
        return None
    for name in sorted(os.listdir(specs)):
        if name == wanted and name not in ("_template", ".", "..") and os.path.isdir(os.path.join(specs, name)):
            return os.path.join(specs, name)
    return None


def ids(text, kind=None):
    """Every id the text names (`AC1`, `S2`, `E3`), ranges expanded; only those of `kind` when given."""
    found = set()
    for prefix, low, spaced, joined in RANGE.findall(text):
        high = spaced or joined
        found.update(f"{prefix}{n}" for n in range(int(low), int(high) + 1) if int(high) - int(low) < 100)
    found.update(f"{prefix}{number}" for prefix, number in ID.findall(text))
    return {i for i in found if kind is None or re.fullmatch(rf"{kind}\d+", i)}


def sections(text):
    """{heading: body} for every `## ` section, without the template's guidance comments."""
    result, current = {}, None
    for line in COMMENT.sub("", text).splitlines():
        if line.startswith("## "):
            current = line[3:].strip()
            result[current] = []
        elif current is not None:
            result[current].append(line)
    return {name: "\n".join(lines).strip() for name, lines in result.items()}


def cell(value):
    return value.strip().strip(EMPHASIS).strip()


def split_row(line):
    return [cell(c) for c in re.split(r"(?<!\\)\|", line.strip().strip("|"))]


def tables(body):
    """Every Markdown table in the text, as (header cells, data rows)."""
    found, header, rows = [], None, []
    for line in (body or "").splitlines():
        if not line.strip().startswith("|"):
            if header is not None:
                found.append((header, rows))
            header, rows = None, []
            continue
        cells = split_row(line)
        if all(set(c) <= set("-: ") for c in cells):
            continue
        if header is None:
            header = cells
        else:
            rows.append(cells)
    if header is not None:
        found.append((header, rows))
    return found


def column(header, *names):
    """The index of the first header cell that is one of the names (case-insensitive), or None."""
    for index, name in enumerate(header):
        if name.lower() in names:
            return index
    return None


def empty(value):
    return cell(value).lower() in EMPTY_CELLS


def check_placeholders(name, text, errors):
    for line in COMMENT.sub("", text).splitlines():
        if any(pattern.search(line) for pattern in PLACEHOLDERS):
            errors.append(f"{name}: template placeholder left: {line.strip()}")


def check_open_questions(questions, errors):
    """Every top-level question is answered somewhere in its item (continuation lines and sub-items included)."""
    if NONE.match(questions or ""):
        return
    if questions and not any(TOP_ITEM.match(line) for line in questions.splitlines()):
        if "Answered:" not in questions:
            errors.append("spec.md: *Open questions* holds a question without `Answered:` (or write \"none\")")
        return
    items, current = [], None
    for line in questions.splitlines():
        if TOP_ITEM.match(line):
            current = [line]
            items.append(current)
        elif current is not None and line.strip():
            current.append(line)
    for item in items:
        if not NONE.match(item[0]) and "Answered:" not in "\n".join(item):
            errors.append(f"spec.md: open question without `Answered:`: {item[0].strip()}")


def check_spec(spec, errors):
    """The spec's sections; returns its criterion ids."""
    parts = sections(spec)
    for section in SPEC_SECTIONS:
        if not parts.get(section):
            errors.append(f"spec.md: section *{section}* is missing or empty (write \"none\" when nothing applies)")
    block = parts.get("Acceptance criteria", "")
    criteria = CRITERION.findall(block)
    if not criteria:
        errors.append("spec.md: no acceptance criterion written `- [ ] AC1: …` under *Acceptance criteria*")
    if len(criteria) != len(set(criteria)):
        errors.append("spec.md: an acceptance criterion id is used twice")
    for line in block.splitlines():
        if re.match(r"^[-*\d]", line) and ids(line, "AC") and not CRITERION.match(line):
            errors.append(f"spec.md: criterion not written `- [ ] AC<n>: …`: {line.strip()}")
    cases = CASE.findall(parts.get("Scenarios", "")) + CASE.findall(parts.get("Errors and edge cases", ""))
    if not any(case.startswith("S") for case in cases):
        errors.append("spec.md: no scenario written `- S1: …` under *Scenarios*")
    covered = ids(block) | ids(parts.get("Out of scope", ""))
    for case in cases:
        if case not in covered:
            errors.append(f"spec.md: {case} is named by no acceptance criterion and not listed under *Out of scope*")
    check_open_questions(parts.get("Open questions", ""), errors)
    return set(criteria)


def check_tests(plan_parts, criteria, tier, errors):
    named = set()
    for header, rows in tables(plan_parts.get("Tests per criterion")):
        test = column(header, "test(s)", "tests", "test")
        test = 1 if test is None else test
        for row in rows:
            named |= ids(row[0], "AC")
            if len(row) <= test or (empty(row[test]) and tier != "trivial"):
                errors.append(f"plan.md: *Tests per criterion* row without a test name: | {' | '.join(row)} |")
    if tier == "trivial":
        return
    without_tests = ids(plan_parts.get("Verification without tests", ""), "AC")
    for criterion in sorted(criteria - named - without_tests):
        errors.append(f"plan.md: {criterion} has no row in *Tests per criterion* (nor in *Verification without tests*)")
    for criterion in sorted(named - criteria):
        errors.append(f"plan.md: *Tests per criterion* names {criterion}, which no acceptance criterion defines")


def check_facts(plan_parts, errors):
    rows = [row for _, table_rows in tables(plan_parts.get("Verified facts")) for row in table_rows]
    if not rows:
        errors.append("plan.md: *Verified facts* is missing or has no row (claim, evidence, status)")
    for row in rows:
        if len(row) < 3:
            errors.append(f"plan.md: *Verified facts* row needs claim, evidence and status: | {' | '.join(row)} |")
            continue
        claim, evidence, status = row[-3], row[-2], row[-1].lower()
        if empty(evidence):
            errors.append(f"plan.md: claim without evidence in *Verified facts*: {claim}")
        if status not in STATUSES:
            errors.append(f"plan.md: *Verified facts* status must be one of {', '.join(STATUSES)}: {claim}")
        elif status == "unverified" and not evidence.lower().startswith("to verify:"):
            errors.append(f"plan.md: unverified claim without `to verify:` and its measurement: {claim}")


def check_groups(header, rows, errors):
    """Every task row names its group, and a group's rows are consecutive: steps 4–6 run one group at a time, as
    one launch per member, in table order."""
    index = column(header, "group")
    if index is None:
        errors.append("tasks.md: a task table without a *Group* column (steps 4–6 run per task group)")
        return
    seen, previous = set(), None
    for row in rows:
        group = row[index] if len(row) > index else ""
        if empty(group):
            errors.append(f"tasks.md: task without a group: | {' | '.join(row)} |")
            continue
        if group != previous and group in seen:
            errors.append(f"tasks.md: the rows of group {group} are not consecutive")
        seen.add(group)
        previous = group


def signatures_section(plan):
    parts = sections(plan)
    return next((body for name, body in parts.items() if name.startswith("Signatures")), "")


def signature_names(body):
    """The identifier of every backticked declaration in the plan's *Signatures* section (`Options.Kernel` and
    `func (o *Options) Kernel() string` give `Kernel`); a "none" section gives none."""
    names = []
    for span in SPAN.findall(COMMENT.sub("", body or "")):
        call = re.search(r"([A-Za-z_]\w*)\s*\(", span)
        words = [w for w in re.findall(r"[A-Za-z_][\w.]*", span) if w not in DECLARATION_KEYWORDS]
        name = words[0].split(".")[-1] if words else ""
        if call:
            name = call.group(1)
        if name and name not in names:
            names.append(name)
    return names


def group_states(header, rows):
    """{group: (skeleton or "no new API" stated, owned by the Lead alone)} and the text of the skeleton rows."""
    group, task, owner = column(header, "group"), column(header, "task"), column(header, "owner")
    states, skeleton = {}, []
    for row in rows:
        if group is None or task is None or len(row) <= max(group, task):
            continue
        text = " ".join(row)
        is_skeleton = bool(SKELETON_ROW.match(row[task]))
        stated, lead_only = states.get(row[group], (False, True))
        lead = owner is not None and len(row) > owner and row[owner].lower() == "lead"
        states[row[group]] = (stated or is_skeleton or bool(NO_NEW_API.search(text)), lead_only and lead)
        if is_skeleton:
            skeleton.append(text)
    return states, "\n".join(skeleton)


def check_skeleton(header, rows, signatures, errors):
    """With API in the plan's *Signatures*, every group has a skeleton row or says "no new API" (a group of the
    Lead alone writes documentation), and every signature name appears in a skeleton row. Returns the text of the
    skeleton rows."""
    states, skeleton = group_states(header, rows)
    for key, (stated, lead_only) in states.items():
        if signatures and not (stated or lead_only or empty(key)):
            errors.append(f"tasks.md: group {key} has no *Skeleton* row and does not say \"no new API\" "
                          "(the plan's *Signatures* lists API)")
    return skeleton


def check_tasks(tasks, criteria, errors, signatures=()):
    named, found_column, skeleton = set(), False, ""
    for header, rows in tables(tasks):
        index = column(header, "criteria", "criterion", "tests (ac)", "ac")
        if index is None:
            continue
        found_column = True
        named |= {i for row in rows if len(row) > index for i in ids(row[index], "AC")}
        check_groups(header, rows, errors)
        skeleton += check_skeleton(header, rows, signatures, errors)
    for name in signatures:
        if not re.search(rf"(?<!\w){re.escape(name)}(?!\w)", skeleton):
            errors.append(f"tasks.md: the plan's signature `{name}` is named by no skeleton row of any group")
    if not found_column:
        errors.append("tasks.md: no table with a *Criteria* column")
    for criterion in sorted(criteria - named):
        errors.append(f"tasks.md: {criterion} is covered by no task")
    for criterion in sorted(named - criteria):
        errors.append(f"tasks.md: names {criterion}, which spec.md does not define")


def check_security_areas(plan, errors):
    """The line that decides the Security plan review: "none", or the areas — never empty or left as the template."""
    areas = re.search(r"^Security areas touched:(.*)", plan, re.M)
    value = cell(areas.group(1)) if areas else ""
    tier = re.search(r"^Tier:\s*`?(\w+)", plan, re.M)
    if NONE.match(value):
        if tier and tier.group(1) == "security":
            errors.append("plan.md: tier `security` changes a security control, so *Security areas touched* "
                          "cannot be \"none\": name the area (add it to .squad/project.md if the list lacks it)")
        return
    if (not areas or empty(value) or re.match(r"(?:none|no|nothing)\b", value, re.I) or "<areas>" in value):
        errors.append("plan.md: write the line `Security areas touched: none` (exactly) or `Security areas "
                      "touched: <the areas>` (it decides the Security plan review)")


def check_plan(plan, spec, errors):
    """plan.md against the tier, the facts and the criteria; returns the criteria of the work folder."""
    parts = sections(plan)
    tier = re.search(r"^Tier:\s*`?(\w+)`?([^\n]*)", plan, re.M)
    name = tier.group(1) if tier else None
    if name not in TIERS or tier.group(2).lstrip().startswith("|"):
        errors.append(f"plan.md: the *Tier* line must name exactly one of {', '.join(TIERS)}")
    check_security_areas(plan, errors)
    own = set(CRITERION.findall(COMMENT.sub("", plan)))
    if spec is None:
        criteria = own
        if not criteria:
            errors.append("plan.md: no acceptance criterion (`- [ ] AC1: …`) under *Acceptance criteria*")
    else:
        criteria = check_spec(spec, errors)
        for criterion in sorted(own):
            errors.append(f"plan.md: defines {criterion}; a feature's criteria live in spec.md only")
    check_tests(parts, criteria, name, errors)
    check_facts(parts, errors)
    return criteria


def check_spec_only(folder):
    """Findings for `spec.md` alone (step 2, before the plan is written)."""
    errors = []
    spec = read(os.path.join(folder, SPEC), errors)
    if spec is None:
        return ["spec.md is missing"], []
    check_spec(spec, errors)
    check_placeholders(SPEC, spec, errors)
    check_control_characters(folder, errors)
    return errors, []


def check_control_characters(folder, errors):
    """No Markdown file of the work folder carries a control character: `scope-check.py` would reject it only in
    the docs step, after every member has read the plan."""
    for name in sorted(os.listdir(folder)):
        path = os.path.join(folder, name)
        if name.endswith(".md") and os.path.isfile(path):
            found = control_characters(read(path, []) or "")
            if found:
                errors.append(f"{name}: control characters {', '.join(found)} (write them as text escapes)")


def check(folder):
    """All findings for the work folder, and the warnings."""
    errors, warnings = [], []
    check_control_characters(folder, errors)
    plan = read(os.path.join(folder, "plan.md"), errors)
    spec = read(os.path.join(folder, SPEC), errors)
    if plan is None:
        return ["plan.md is missing (tiers docs and patch write none: there is nothing to check)"], warnings
    criteria = check_plan(plan, spec, errors)
    for name, text in (("plan.md", plan), (SPEC, spec)):
        if text is not None:
            check_placeholders(name, text, errors)
    if spec is not None:
        tasks = read(os.path.join(folder, "tasks.md"), errors)
        if tasks is None:
            errors.append("tasks.md is missing")
        else:
            check_tasks(tasks, criteria, errors, signature_names(signatures_section(plan)))
            check_placeholders("tasks.md", tasks, errors)
    words = len(plan.split())
    if words > PLAN_HARD_WORDS:
        errors.append(f"plan.md has {words} words (PLAN_HARD_WORDS {PLAN_HARD_WORDS}): move settled material "
                      "(answered challenge entries, facts verified in earlier rounds) into the decision record "
                      "or the area document")
    elif words > PLAN_MAX_WORDS:
        warnings.append(f"plan.md has {words} words (PLAN_MAX_WORDS {PLAN_MAX_WORDS}): every member reads it — "
                        "cut what the code, the spec or a decision record already says")
    return errors, warnings


def main(argv=None):
    argv = sys.argv[1:] if argv is None else argv
    spec_only = "--spec" in argv
    names = [arg for arg in argv if arg != "--spec"]
    if len(names) != 1 or any(arg.startswith("-") for arg in names):
        print("Usage: python3 .squad/tools/plan-check.py <work folder> [--spec]")
        return 1
    folder = resolve_folder(os.getcwd(), names[0])
    if folder is None:
        print(f"No work folder {names[0]!r} under specs/")
        return 1
    errors, warnings = check_spec_only(folder) if spec_only else check(folder)
    for line in warnings:
        print(f"WARNING: {line}")
    for line in errors:
        print(line)
    print("PASS" if not errors else f"FAIL: {len(errors)} finding(s) — back to the Lead (mode `revise`)")
    return 0 if not errors else 1


if __name__ == "__main__":
    sys.exit(main())
