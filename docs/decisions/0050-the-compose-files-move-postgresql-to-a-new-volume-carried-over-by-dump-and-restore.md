# 0050: The compose files move PostgreSQL to a new volume, carried over by dump and restore

- **Status:** Accepted
- **Date:** 2026-11-18
- **Area:** Hosting and configuration
- **Source:** Issue #505
- **Supersedes:** —

## Context

The documented compose files (`docs/docker-compose.{minimal,trivy,full}.yml`) ran `postgres:16-alpine` with the named
volume `dockerupdateguard-postgres-data` mounted at `/var/lib/postgresql/data`. Issue #505 moves them to the current
major, PostgreSQL 18. Two things change with it: a data directory written by 16 cannot be started by 18 (a major
release changes the on-disk format), and the official 18 images keep their data in a major-specific subdirectory
(`PGDATA` `/var/lib/postgresql/18/docker`, `VOLUME` `/var/lib/postgresql`); their documentation asks for mounts at
`/var/lib/postgresql`, which is what lets a later major upgrade run `pg_upgrade --link` inside one volume. Existing
installations hold state an operator entered (update-target pins, disabled images, manual registrations, the action
audit), so they need a way to carry their data over. The application itself does not change: the same image and
migrations run against either major.

## Options considered

1. **A new volume, `dockerupdateguard-postgresql-data`, mounted at `/var/lib/postgresql`; data carried over by
   `pg_dump` from the 16 container and `pg_restore` into the 18 one** — follows the image's layout, so later majors
   upgrade in place; the 16 volume is never touched and stays a working fallback until the operator removes it; uses
   only the tools both official images contain. An installation that switches compose files without the procedure
   starts with an empty database, its old data intact in the old volume.
2. **The same volume name at the new mount point** — the 18 cluster is created in `18/docker` inside the volume that
   still holds the 16 files at its root: the two cannot be told apart by volume, and the old files can only be
   removed by hand inside the volume. Starts just as empty as option 1 when the procedure is skipped.
3. **The old mount point with `PGDATA=/var/lib/postgresql/data` set** — 18 refuses a 16 data directory, so a skipped
   procedure fails loudly instead of starting empty; but it keeps the layout the image documentation tells users to
   leave, gives up `pg_upgrade --link` for the next major, and the image's own `VOLUME` still creates an anonymous
   volume at `/var/lib/postgresql` beside it.
4. **`pg_upgrade` instead of dump and restore** — needs the binaries of both majors in one container, which no official
   image provides (a third-party upgrade image would), and leaves the 16 layout to be converted anyway.
5. **Stay on 16** — still supported, but the issue asks for the current major, and the move only grows with time.

## Decision

The compose files run `postgres:18-alpine` with its data in the new volume `dockerupdateguard-postgresql-data` at
`/var/lib/postgresql`, and an existing PostgreSQL 16 installation is moved by a documented dump of the 16 database and
its restore into the new one. The resulting layout is stated in [Hosting](../areas/hosting.md#database-connection),
the procedure in [`INSTALL.md`](../../INSTALL.md).

## Consequences

- An operator who replaces the compose file without the procedure gets an empty database; `INSTALL.md` points to the
  procedure where it describes updating, and the old volume keeps the data until it is removed by hand.
- The next major (19 and later) can be upgraded inside the same volume; that move is a change of its own.
- Revisit option 3 if operators report empty starts: it trades the image's layout for a loud failure.
