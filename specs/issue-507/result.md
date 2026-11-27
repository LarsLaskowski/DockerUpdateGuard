Issue #507 is tier `patch/security`, so there is no `plan.md`. The result below goes verbatim to `specs/issue-507/result.md`, and the orchestrator applies the edits. I wrote no files.

**Tier:** `patch/security`. The change is three exact edits in about 18 changed lines outside `specs/` (the limit is 30). It is one configuration entry plus two documentation sentences, with no logic, dependency, permission or schema change. The configuration edit is in a CI file (`.github/dependabot.yml`), which makes it `patch/security`. The review round is `squad-security` in mode `diff`.

**Security areas touched:** CI configuration, `.github/dependabot.yml`. The new entry adds an update source that opens pull requests against `docs/`. It gives no workflow a new permission or secret, and no area from `.squad/project.md` (*Security areas*) is touched.

**Decision record:** none. The two exclusions follow from released records that stay untouched:
- `aquasec/trivy` keeps the compose servers on the same version as the bundled CLI ([0051](/home/user/DockerUpdateGuard/docs/decisions/0051-the-bundled-trivy-and-the-compose-trivy-servers-move-to-v0-76-2.md), and step 6 of *Updating the bundled Trivy* in CONTRIBUTING).
- "The next major is a change of its own" ([0050](/home/user/DockerUpdateGuard/docs/decisions/0050-the-compose-files-move-postgresql-to-a-new-volume-carried-over-by-dump-and-restore.md)).

**Related stale text:** 0051's *Context* says "Dependabot does not cover the Docker images here". 0051 is released (tag `v1.3.10`), so it stays as written about its own time.

## Verified facts
1. Verified: `.github/dependabot.yml` has three entries (`nuget`, `dotnet-sdk`, `github-actions`) and no Docker entry. The file uses CRLF line endings.
2. Verified: these are the images in the compose files under `docs/`:
   - `docker-compose.full.yml`: `postgres:18-alpine`, `aquasec/trivy:0.76.2`, `grafana/otel-lgtm:0.41.0`, `networlddev/dockerupdateguard:latest`
   - `docker-compose.minimal.yml`: `postgres:18-alpine`, `networlddev/dockerupdateguard:latest`
   - `docker-compose.trivy.yml`: `postgres:18-alpine`, `aquasec/trivy:0.76.2`, `networlddev/dockerupdateguard:latest`
3. Verified: CONTRIBUTING.md line 159 begins with "Dependabot is not configured for Docker images in this repository, …". Step 6 of the same procedure already changes the compose Trivy servers together with `TRIVY_IMAGE`.
4. Verified: `.squad/stack.md` lines 14–21 say "Dependabot keeps the SDK and the packages current within that major" and that the Dockerfile has no `docker` entry. That second claim stays true: `docker-compose` is a separate ecosystem.
5. Verified: no area document, `ARCHITECTURE.md`, `README.md` or `INSTALL.md` lists the Dependabot ecosystems. `.github/release.yml` already sends Dependabot updates other than GitHub Actions to "Other changes", so it needs no edit. The PR template's project block asks for nothing here.
6. Unverified (I could not open these pages: proxy 403 for docs.github.com, and the dependabot-core repository cannot be attached):
   - the ecosystem value is `docker-compose`;
   - `ignore` with `dependency-name` and `update-types` applies to that ecosystem;
   - the Docker Hub official image's dependency name is `postgres`, not `library/postgres`.

   The Product Manager opens https://docs.github.com/en/code-security/dependabot/working-with-dependabot/dependabot-options-reference (the `package-ecosystem` table and `ignore`) before step 9. A page that does not confirm these is a blocking finding for the edit.
7. Unverified, and no criterion rests on it:
   - `postgres:18-alpine` names only a major, so with majors ignored Dependabot probably raises no PostgreSQL tag at all.
   - `latest` carries no version and gets no update.
   - In practice only `grafana/otel-lgtm` would be raised.

   This matches the issue's intent.
8. Unverified until after merge (the issue's own scope): whether Dependabot reads files named `docker-compose.<tier>.yml`. A wrong guess fails closed: no update pull requests open, and nothing else changes. This goes to the linked issue below.

## Exact edits (all CRLF, no final newline where the file has none)

**E1 — `.github/dependabot.yml`.** Append after the `github-actions` entry, with a blank line before it and the existing two-space indent:
```yaml
  # The images of the example compose files under docs/. aquasec/trivy moves together with the TRIVY_IMAGE of the
  # Dockerfile, which Dependabot does not read (docs/CONTRIBUTING.md, "Updating the bundled Trivy"); a PostgreSQL
  # major needs a data migration of its own (decision record 0050).
  - package-ecosystem: docker-compose
    directory: "/docs"
    schedule:
      interval: weekly
    ignore:
      - dependency-name: "aquasec/trivy"
      - dependency-name: "postgres"
        update-types: ["version-update:semver-major"]
```

**E2 — `docs/CONTRIBUTING.md`, lines 159–160.** Replace
```
Dependabot is not configured for Docker images in this repository, so the Trivy CLI the image bundles (the `TRIVY_IMAGE` build argument in
`src/DockerUpdateGuard/Dockerfile`, pinned by digest) is updated by hand. Decision records
```
with
```
Dependabot does not read the images of the Dockerfile (it names them through build arguments) and leaves
`aquasec/trivy` of the example compose files alone (`.github/dependabot.yml`), so the Trivy CLI the image bundles (the
`TRIVY_IMAGE` build argument in `src/DockerUpdateGuard/Dockerfile`, pinned by digest) and the compose Trivy servers are
updated by hand, together. Decision records
```

**E3 — `.squad/stack.md`, lines 14–15.** Replace
```
Dependabot keeps the
  SDK and the packages current within that major.
```
with
```
Dependabot keeps the
  SDK and the packages current within that major; its `docker-compose` entry also raises the images of the example
  compose files under `docs/`, except `aquasec/trivy` (it moves with `TRIVY_IMAGE`) and PostgreSQL majors (each needs
  a data migration).
```
This adds about 30 words to `.squad/stack.md`. It is documentation, not a command the pipeline runs.

## Acceptance criteria and their checks
- **AC1:** `.github/dependabot.yml` keeps its three existing entries unchanged and in order, and adds exactly one `docker-compose` entry with directory `/docs` and a weekly schedule. That entry ignores `aquasec/trivy` entirely and only `version-update:semver-major` of `postgres`.
  - Check (E1, gate row "configuration file"; the orchestrator runs it from the repository root):
    `python3 -I -c "import yaml; c=yaml.safe_load(open('.github/dependabot.yml')); assert [u['package-ecosystem'] for u in c['updates']]==['nuget','dotnet-sdk','github-actions','docker-compose']; e=c['updates'][3]; assert e['directory']=='/docs' and e['schedule']['interval']=='weekly'; assert {d['dependency-name']:d.get('update-types') for d in e['ignore']}=={'aquasec/trivy':None,'postgres':['version-update:semver-major']}; print('ok')"`
  - Also `git diff origin/main -- .github/dependabot.yml` shows only added lines.
  - The page confirmation in fact 6 comes before step 9.
- **AC2:** `docs/CONTRIBUTING.md` no longer says Dependabot is not configured for Docker images. It says the bundled Trivy CLI and the compose Trivy servers are updated by hand, together.
  - Check (E2): *Format check*, plus `grep -n "not configured for Docker images" docs/CONTRIBUTING.md` finds nothing. The reviewer reads it against E2.
- **AC3:** `.squad/stack.md` names the `docker-compose` entry and its two exclusions, and still states that the Dockerfile has no `docker` entry.
  - Check (E3): *Format check*; the reviewer reads it against E3.
- **AC4 (after merge, not a merge condition):** Dependabot reads all three `docs/docker-compose.<tier>.yml` files.
  - Verified in the linked issue below.

## Options considered
1. **One `docker-compose` entry for `/docs` with both exclusions.** Chosen.
2. **Let Dependabot raise `aquasec/trivy` too.** Lost: the compose servers must stay on the same version as the digest-pinned CLI in the Dockerfile. Dependabot does not read that pin, and every new version must be measured first (0051, CONTRIBUTING).
3. **Rename the compose files to `compose.<tier>.yml` now, in case Dependabot does not read `docker-compose.<tier>.yml`.** Lost: the need is unconfirmed, and the rename would break every link in INSTALL and the docs. The after-merge check decides this.

## Linked verification issue (the orchestrator creates it when the PR opens)
- **Title:** `[CI] Confirm that Dependabot reads docs/docker-compose.<tier>.yml`
- **Body:** "After #507 is merged, open Insights → Dependency graph → Dependabot and check the `docker-compose` entry for `/docs`. Its last run finished without an error, and it lists `docs/docker-compose.full.yml`, `docs/docker-compose.minimal.yml` and `docs/docker-compose.trivy.yml`. On the next weekly run, an update to `grafana/otel-lgtm` (if one is published) opens a pull request, and none opens for `aquasec/trivy` or a PostgreSQL major. If the files are not listed, plan their rename to a pattern Dependabot reads, with every link to them updated."

PR area: `[CI]`.

RESULT: DONE
