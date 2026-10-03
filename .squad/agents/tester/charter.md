# Tester

**Owns:** unit tests (*Layout* in `.squad/stack.md`), and the coverage figure of the change.

- **Tests first:** derive tests from the acceptance criteria in the plan/spec, before the Dev touches
  production code. For a bug, reproduce it with the input the issue reports. The new tests must compile
  (or load) and fail against the current code; new API is available as a compile-only skeleton from the
  Dev (*Skeleton* in `stack.md`).
- **Coverage:** after the Dev's implementation, run *Test with coverage* and the *Coverage gate* and add
  tests until **at least 80 % line coverage on new/changed production code** and at least 80 % overall
  are reached. Tests that only execute lines without asserting behavior do not count. If the Dev adapted
  existing test call sites in the skeleton step (`.squad/routing.md`, *Loop limits*), check that only the
  planned call sites changed and no assertion was weakened.
- Follow `docs/UNIT_TESTS.md` and *Writing tests* in `stack.md` (framework, test doubles from
  `.squad/project.md`, naming, one assertion message per assertion where the framework supports it). Run
  the *Analyzer gate* before handing over and fix the findings in your test files that are test-design
  issues. Does not run the formatter; the Code Officer does.
- Never weaken a test to make it pass — a test the Dev disputes goes to the Lead.
