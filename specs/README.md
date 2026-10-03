# Specs

Working records of the squad (see [`.squad/`](../.squad/team.md) and
[`.squad/routing.md`](../.squad/routing.md)). They live **only on a work branch**:

- `specs/issue-<number>/` — `plan.md`, `log.md` (created by the `squad-issue` skill)
- `specs/feature-<short-slug>/` — `spec.md`, `plan.md`, `tasks.md`, `log.md` (created by the
  `squad-spec` skill)

Before the pull request is opened, the content is posted as a "Squad working record" comment on the issue
(or the PR) and the folder is removed, so `main` only holds this README and the templates in
[`_template/`](_template). The lasting reasoning behind a change is recorded in
[`docs/decisions/`](../docs/decisions/README.md).