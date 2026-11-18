# Plan: Update outdated image versions in the docker-compose files

Source: Issue #505
Tier: standard — a dependency of the documented deployment (the PostgreSQL major) changes, with a new data volume and an operator migration, but no security control and no production code.
Security areas touched: Docker configuration (`docs/docker-compose.minimal.yml`, `docs/docker-compose.trivy.yml`, `docs/docker-compose.full.yml`: image tags, the database volume)

## Problem / root cause

The compose files pin `postgres:16-alpine` (`docs/docker-compose.minimal.yml:12`, `docs/docker-compose.trivy.yml:12`,
`docs/docker-compose.full.yml:14`) and leave `grafana/otel-lgtm` floating on `latest` (`docs/docker-compose.full.yml:38`).
PostgreSQL 18 cannot be a tag change alone: its image keeps data under `/var/lib/postgresql/18/docker`, so the
current mount `dockerupdateguard-postgres-data:/var/lib/postgresql/data` (`:20`, `:20`, `:22`) would no longer hold the
database, and a 16 data directory cannot be started by 18.

Issue claims: confirmed — the three pins and versions (rows 1–5), the 18 volume layout (row 6), the 16→18
incompatibility (row 7), the Dockerfile's v0.76.1 digest and the documents naming that version (row 8). Refined: the
issue says "decide whether the bundled CLI follows" — the repository already decided it: client and server stay on
one version and a bundled-Trivy update is measured first (row 8); this change therefore keeps Trivy at 0.76.1 and
splits the 0.76.2 update off (*Options considered*). Related defect found: `INSTALL.md` *Updating* (lines 109–114)
says only `pull` and `up -d`, which is wrong for an operator who also replaces the compose file across this change.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | The three compose files pin `postgres:16-alpine` with `dockerupdateguard-postgres-data:/var/lib/postgresql/data`, declared under `volumes:` | minimal `:12`, `:20`, `:49`; trivy `:12`, `:20`, `:62`; full `:14`, `:22`, `:81` | confirmed |
| 2 | `aquasec/trivy:0.76.1` in trivy `:28` and full `:30`; the Dockerfile's `TRIVY_IMAGE` digest `sha256:790fd66d…6c06c` is the 0.76.1 index | `src/DockerUpdateGuard/Dockerfile:4-5`; Docker Hub tags API: `0.76.1` → same digest | confirmed |
| 3 | Trivy 0.76.2 exists (`sha256:8f6bb947…768fb`, also `latest`) | Docker Hub tags API, 2026-11-18 | confirmed |
| 4 | `grafana/otel-lgtm:0.41.0` is the newest release and today's `latest` (both `sha256:2e329065…48b6`) | Docker Hub tags API | confirmed |
| 5 | `postgres:18-alpine` = `18.7-alpine` (`sha256:b6972e11…f015`); 19 exists only as beta/rc tags | Docker Hub tags API, names `18*` and `19*` | confirmed |
| 6 | The 18 images set `PGDATA` `/var/lib/postgresql/18/docker` and `VOLUME` `/var/lib/postgresql`; mounts should target `/var/lib/postgresql` | Docker Hub description of `library/postgres`, section *PGDATA* (read through the Hub API) | confirmed |
| 7 | A 16 data directory cannot be started by 18; the move needs dump/restore or `pg_upgrade` | issue; PostgreSQL major releases change the storage format (postgresql.org unreachable from the session: proxy 403) | confirmed |
| 8 | Client and server Trivy stay on one version, and a new bundled version is measured first (CONTRIBUTING steps 1–4) and named in records 0005, 0015, 0016 | `docs/CONTRIBUTING.md:157-185`, `docs/areas/hosting.md:161-163`; the three records were added before `v1.3.8` (`git tag --contains`), so they are released | confirmed |
| 9 | The session cannot run those measurements nor start PostgreSQL 18 | `docker info`: no daemon at `/var/run/docker.sock`; `imagetools inspect` answers 429 | confirmed |
| 10 | Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3; the only PostgreSQL-specific SQL is the advisory lock; migrations hold no raw SQL; no CI job uses PostgreSQL | `Directory.Packages.props:20`; `DatabaseMigrator.cs:273,296`; grep of `Migrations/` and `.github/workflows/` | confirmed |
| 11 | The application starts and migrates against PostgreSQL 18 | to verify: dry run (AC3), Product Manager, step 7 | unverified |
| 12 | `pg_restore` reads the archive from standard input when no file is named; `--single-transaction` exists | `pg_restore --help` (16 client in the session) | confirmed |
| 13 | Inside the official container, `pg_dump`/`pg_restore` as `$POSTGRES_USER` over the local socket need no password; `docker compose up --wait` returns once the healthcheck passes; a custom-format dump survives `exec -T` redirection | to verify: dry run (AC2), Product Manager, step 7 | unverified |
| 14 | `docker compose config -q` validates the compose files without a daemon | Compose v5.7.1 in the session, all three files pass today with `POSTGRES_PASSWORD` set | confirmed |
| 15 | Dependabot has no Docker or docker-compose entry | `.github/dependabot.yml` | confirmed |
| 16 | Dependabot's docker-compose ecosystem reads `docker-compose.<tier>.yml` under `docs/` | to verify: after merge of follow-up issue B, by its owner (GitHub docs unreachable: proxy 403) | unverified |

## Acceptance criteria

- AC1: Each of the three compose files names `postgres:18-alpine`, mounts the volume `dockerupdateguard-postgresql-data`
  at `/var/lib/postgresql` and declares it under `volumes:` (no `dockerupdateguard-postgres-data` left); the two Trivy
  servers stay on `aquasec/trivy:0.76.1`, the bundled CLI's version; `docs/docker-compose.full.yml` names
  `grafana/otel-lgtm:0.41.0`.
- AC2: `INSTALL.md` documents moving an installation from PostgreSQL 16 to 18 that keeps its data: the 16 database is
  dumped, the 18 one restored from it, the application started; the procedure stops at its first failing command,
  and the 16 volume is removed only by a separate, final step.
- AC3: With each compose file, a fresh installation starts PostgreSQL 18 and DockerUpdateGuard applies its migrations
  and serves the UI.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | check: `POSTGRES_PASSWORD=x docker compose -f docs/docker-compose.<tier>.yml config -q` for each tier, and a grep for `postgres-data`/`16-alpine`/`otel-lgtm:latest` finding none — Dev, step 6; Reviewer reads the diff | — |
| AC2 | manual: Product Manager dry run, step 7 (below) | — |
| AC3 | manual: Product Manager, step 7, on the published `networlddev/dockerupdateguard:latest` | — |

## Verification without tests

No production or test code: steps 4, 5 and the *Coverage gate* are not applicable.

- AC1: check: as in the table, Dev before handing back; the stamp of step 7 runs the other gates.
- AC2: manual: the Product Manager runs the documented procedure as written on a host with an existing 16 installation
  of the minimal tier (Docker Engine 24+ with Compose v2, the published image) before step 8, and confirms rows 11 and
  13: earlier data visible after the move; once, a forced failure (`docker compose stop postgres` before pasting)
  shows the chain stopping with the 16 volume intact.
- AC3: manual: the Product Manager runs `docker compose up -d` with each compose file in an empty directory (fresh
  volumes) and checks `docker compose logs dockerupdateguard` for the applied migrations and the UI at :8080. The build is
  the published image: the change does not touch the application, so no branch build is needed and no early draft PR.

## Approach

1. **Compose files** (Dev): in all three, `image: postgres:18-alpine`; the mount
   `dockerupdateguard-postgresql-data:/var/lib/postgresql`; the top-level `volumes:` key renamed to match. In
   `docs/docker-compose.full.yml`, `image: grafana/otel-lgtm:0.41.0`. Trivy lines unchanged. CRLF and the final line
   ending stay as they are.
2. **Upgrade procedure** (Dev, `INSTALL.md`, new section `## Upgrading from PostgreSQL 16` after `## Updating`). Design,
   for an installation whose `docker-compose.yml` still names `postgres:16-alpine`, run in its directory: one command
   chain joined by `&&` at each line end, in this order —
   1. download the new compose file of the operator's tier to `docker-compose.pg18.yml` (`curl -fsSL`, the URLs of
      step 2), so a wrong tier or URL fails before anything stops;
   2. `docker compose stop dockerupdateguard`;
   3. `docker compose exec -T postgres sh -c 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" --format=custom' > dockerupdateguard-pg16.dump`;
   4. `docker compose down` (volumes kept);
   5. `mv docker-compose.yml docker-compose.pg16.yml` and `mv docker-compose.pg18.yml docker-compose.yml`;
   6. `docker compose up -d --wait postgres`;
   7. `docker compose exec -T postgres sh -c 'pg_restore -U "$POSTGRES_USER" -d "$POSTGRES_DB" --no-owner --single-transaction' < dockerupdateguard-pg16.dump`;
   8. `docker compose up -d`.

   Then, as its own block once the data shows in the UI: `docker volume ls` to find `<project>_dockerupdateguard-postgres-data`
   and `docker volume rm` it, and the dump and `docker-compose.pg16.yml` may go. A rollback note: until that step,
   `docker compose down`, moving `docker-compose.pg16.yml` back and `up -d` restores the 16 installation. A note for
   an installation that already started on 18 with an empty database: stop the app and recreate the database
   (`dropdb --force` / `createdb` as `$POSTGRES_USER` inside the container) before step 7, since the restore needs an
   empty database. The *Updating* section gets one sentence: a compose file that still names `postgres:16-alpine` is
   moved with that section, not by `pull`. Link the image's documentation only as `https://hub.docker.com/_/postgres`
   (opened from the session).

   Shell cases: (a) pasted at once — one compound command; it stops at the first failure. (b) one line at a time — each
   line ends in `&&`, so the shell keeps reading the same command; same as (a). (c) a line fails — nothing after it
   runs; before step 4 the stack is restarted with `docker compose up -d`, after it the rollback note applies; the 16
   volume is never written. (d) a variable lost — the chain uses no host variable: `$POSTGRES_USER`/`$POSTGRES_DB` are
   single-quoted and expand inside the container from its own environment. No unit or CI test can hold these: the
   dry run of AC2 shows (a) and (c); no CI-only rows.
3. **Area document and record** (Dev; record: Lead) — *Documentation updates*.

## Options considered

1. **Bump Trivy to 0.76.2 here, servers and bundled CLI together** — lost: the bump requires the four measurements of
   CONTRIBUTING *Updating the bundled Trivy* and a new record amending the released 0005, 0015 and 0016; the session
   cannot run them (row 9), and the move would add the Dockerfile and two security areas to a deployment change. Split
   into follow-up issue A.
2. **Bump only the compose servers to 0.76.2** — lost: breaks "client and server stay on one version"
   (CONTRIBUTING step 5, `docs/areas/hosting.md:162-163`).
3. **Add Dependabot's docker-compose ecosystem for `docs/` now** — lost here: it needs an ignore policy (Trivy moves
   with the Dockerfile by hand, PostgreSQL majors need a procedure like this one), and whether it reads
   `docker-compose.<tier>.yml` can only be seen after merge (row 16). Split into follow-up issue B.
4. **PostgreSQL volume and migration path** — the options of record 0050 (same volume at the new mount, old mount with
   `PGDATA` set, `pg_upgrade`, staying on 16); chosen: new volume, dump and restore.
5. **Pin `postgres:18.7-alpine`** — lost: the files follow a major tag as before, so `docker compose pull` brings minor
   releases.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| — | `docs/docker-compose.minimal.yml`, `docs/docker-compose.trivy.yml`, `docs/docker-compose.full.yml` | Approach 1 |
| — | `INSTALL.md` | Approach 2 |
| — | `docs/areas/hosting.md` | *Areas* |

## Signatures (for the Dev's skeleton)

None: no code changes.

## Test files

None. Existing test call sites affected: none. Files no unit test can cover: none (no code).

## Areas

- Hosting (`docs/areas/hosting.md`), *Database connection*: one bullet — the documented compose files run PostgreSQL 18
  with its data in the volume `dockerupdateguard-postgresql-data` at `/var/lib/postgresql`, where the image keeps a
  major-specific subdirectory; moving a 16 installation: `INSTALL.md`, linked. *Related decisions*: a 0050 entry.

## Documentation updates

Sweep: `postgres-data`, `16-alpine`, `/var/lib/postgresql`, `otel-lgtm`, `0.76` across `README.md`, `INSTALL.md`,
`DOCKER.md`, `SECURITY.md`, `docs/`; hits outside the compose files: only the Trivy statements (unchanged, Trivy
stays) and the tier list of `docs/ARCHITECTURE.md:562-567`, which names no version (unchanged). The PR template asks
for no changelog.

- `INSTALL.md` (Dev): Approach 2.
- `docs/areas/hosting.md` (Dev): *Areas*.
- `docs/decisions/0050-…md` and its index row (Lead, written).

## Architecture check

No guarantee of `.squad/project.md` is touched: the application, its migrations and their lock are unchanged; the
deployment tiers of `docs/ARCHITECTURE.md` keep their services. No `ARCHITECTURE.md` update.

## Security considerations

- [x] No limits.
- [x] None: no code, no message.
- The procedure prints no secret: it uses no password (local socket inside the container) and the dump stays in the
  operator's directory — `INSTALL.md` says it holds the whole database and is removed after the move, like `.env`.

## Decision records

- `docs/decisions/0050-the-compose-files-move-postgresql-to-a-new-volume-carried-over-by-dump-and-restore.md` (Proposed)

## Out of scope / follow-ups

- Issue A, "[Hosting] Update the bundled Trivy and the compose Trivy servers to 0.76.2": body — the Dockerfile's
  `TRIVY_IMAGE` (index digest `sha256:8f6bb947dbc528902e11f2f5be52f7b519f6a39d5d148f41166f2040499768fb` per Docker
  Hub, to confirm in step 1) and both compose servers move together per CONTRIBUTING *Updating the bundled Trivy*,
  steps 1–5, with a new record amending 0005, 0015 and 0016; split from #505.
- Issue B, "[CI] Let Dependabot update the images of the compose files": body — a `docker-compose` entry for `/docs`
  that ignores `aquasec/trivy` (moves with the Dockerfile) and PostgreSQL majors (need a migration like #505's);
  confirm after merge that Dependabot reads `docker-compose.<tier>.yml`; update CONTRIBUTING's "Dependabot is not
  configured for Docker images" and `.squad/stack.md` accordingly; split from #505.
