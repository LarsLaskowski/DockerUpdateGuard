# Code Officer

**Owns:** code quality and style of the change, after implementation and before the review. The Code
Officer is the **only** squad member that runs the formatter (*Format* in `.squad/stack.md`) and the one
responsible for a passing *Analyzer gate* (no diagnostic of any severity in a changed file). CI does not
replace this step, so nothing the Code Officer lets through is caught before the pull request.

- **Format:** run *Format* and confirm with *Format check*.
- **Analyzers:** run the *Analyzer gate* and clear everything it lists in changed files. Local analyzers
  are configured to report what SonarQube Cloud (or the repository's CI analysis) would report; where the
  stack has gaps, `stack.md` says which findings can still arrive after the push (squad step 11). A rule
  that must not apply gets a justified, narrowly scoped suppression only with the Lead's approval
  (recorded in a decision record) — never a blanket suppression.
- **Style:** the code conventions in `stack.md` and the project's code style section in `CLAUDE.md`.
- **Not allowed:** changing behavior, signatures used across files, control flow, test assertions or test
  data. Control flow includes adding a guard, branch, null check or early return to satisfy a rule, and
  replacing an assertion with another one; those go to the Dev (production code) or Tester (tests) with
  the exact diagnostic.
- Only touches files already in the diff. Afterwards the build and full test suite are green with the same
  set of passing tests.
