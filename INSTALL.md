# Installation via Docker

This guide walks through installing DockerUpdateGuard with Docker Compose. It
covers three setup tiers of increasing scope, all built around the same
container image (`networlddev/dockerupdateguard`) documented in
[DOCKER.md](DOCKER.md).

| Tier | Compose file | Contains |
| --- | --- | --- |
| Minimal | `docs/docker-compose.minimal.yml` | DockerUpdateGuard + PostgreSQL |
| Minimal + Trivy | `docs/docker-compose.trivy.yml` | Minimal setup + Trivy server for vulnerability scanning |
| Maximal | `docs/docker-compose.full.yml` | Trivy setup + full OpenTelemetry stack (Grafana, Loki, Tempo, Prometheus) |

Pick the tier that matches your needs; each file is fully self-contained, so
you only ever need to download one of them.

## Prerequisites

- Docker Engine 24+ with the Compose plugin (`docker compose version`)
- On Linux: a user allowed to access `/var/run/docker.sock`
- Outbound network access to Docker Hub (and, if used, your registry,
  Portainer, and OTLP endpoints)

## 1. Choose a directory

```bash
mkdir dockerupdateguard && cd dockerupdateguard
```

## 2. Download the compose file and environment template

Pick **one** of the three tiers below.

### Minimal

```bash
curl -fsSL -o docker-compose.yml \
  https://raw.githubusercontent.com/LarsLaskowski/DockerUpdateGuard/main/docs/docker-compose.minimal.yml
curl -fsSL -o .env \
  https://raw.githubusercontent.com/LarsLaskowski/DockerUpdateGuard/main/docs/.env.example
```

### Minimal + Trivy

```bash
curl -fsSL -o docker-compose.yml \
  https://raw.githubusercontent.com/LarsLaskowski/DockerUpdateGuard/main/docs/docker-compose.trivy.yml
curl -fsSL -o .env \
  https://raw.githubusercontent.com/LarsLaskowski/DockerUpdateGuard/main/docs/.env.example
```

### Maximal (Trivy + Telemetry)

```bash
curl -fsSL -o docker-compose.yml \
  https://raw.githubusercontent.com/LarsLaskowski/DockerUpdateGuard/main/docs/docker-compose.full.yml
curl -fsSL -o .env \
  https://raw.githubusercontent.com/LarsLaskowski/DockerUpdateGuard/main/docs/.env.example
```

The compose file is saved as `docker-compose.yml` so the plain `docker
compose` commands below work without `-f`. Keep the original filename instead
if you plan to switch tiers later and want to keep them side by side.

## 3. Configure `.env`

Open `.env` and set at least:

```env
POSTGRES_PASSWORD=change-me
```

Find the Docker socket's group id and add it to `.env` so the container can
read `/var/run/docker.sock`:

```bash
echo "DOCKER_GID=$(stat -c '%g' /var/run/docker.sock)" >> .env
```

If the socket is owned by `root:root` (common on Synology DSM and similar
appliances), `DOCKER_GID=0` — the `.env.example` default — already works and
this step can be skipped. See [DOCKER.md](DOCKER.md#linux-docker-socket-usage)
for background on why the container needs this.

Optionally set `DOCKERHUB_USERNAME` / `DOCKERHUB_PAT` for authenticated
Docker Hub API access (higher rate limits, private repositories).

## 4. Start the stack

```bash
docker compose up -d
```

Open http://localhost:8080. On the Maximal tier, Grafana is available at
http://localhost:3000 (default login `admin` / `admin`) with
DockerUpdateGuard's logs, metrics, and traces already flowing in through the
bundled OpenTelemetry Collector.

## 5. Check status and logs

```bash
docker compose ps
docker compose logs -f dockerupdateguard
```

The application applies its database migrations automatically on startup;
give it a few seconds after `up -d` before the UI responds.

## Updating

```bash
docker compose pull
docker compose up -d
```

If your `docker-compose.yml` still names `postgres:16-alpine`, do not update it
with `pull` or by replacing the file: follow
[Upgrading from PostgreSQL 16](#upgrading-from-postgresql-16) instead, or the
new stack starts with an empty database.

## Upgrading from PostgreSQL 16

The compose files run PostgreSQL 18 with its data in the new volume
`dockerupdateguard-postgresql-data`. An installation whose
`docker-compose.yml` still names `postgres:16-alpine` keeps its data in the
volume `dockerupdateguard-postgres-data`, which PostgreSQL 18 cannot read, so
the data is dumped from the 16 database and restored into the 18 one. The 16
volume is not written to and stays as a fallback until the last step below.
The image's documentation is at <https://hub.docker.com/_/postgres>.

Run the commands in the directory of your `docker-compose.yml`. Replace the
file name in the first `curl` with the one of your tier (`docker-compose.minimal.yml`,
`docker-compose.trivy.yml` or `docker-compose.full.yml`, see step 2 above) and
paste the whole block at once: every line ends in `&&`, so the first command
that fails stops the rest.

```bash
grep -q 'postgres:16-alpine' docker-compose.yml && \
test ! -e dockerupdateguard-pg16.dump && \
test ! -e docker-compose.pg16.yml && \
curl -fsSL -o docker-compose.pg18.yml \
  https://raw.githubusercontent.com/LarsLaskowski/DockerUpdateGuard/main/docs/docker-compose.minimal.yml && \
docker compose stop dockerupdateguard && \
(umask 077 && docker compose exec -T postgres sh -c 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" --format=custom' > dockerupdateguard-pg16.dump) && \
docker compose down && \
mv docker-compose.yml docker-compose.pg16.yml && \
mv docker-compose.pg18.yml docker-compose.yml && \
docker compose up -d --wait postgres && \
docker compose exec -T postgres sh -c 'pg_restore -U "$POSTGRES_USER" -d "$POSTGRES_DB" --no-owner --single-transaction' < dockerupdateguard-pg16.dump && \
docker compose up -d
```

The first three commands make the block safe to run once only: it refuses to
start when `docker-compose.yml` no longer names `postgres:16-alpine` (the move
already happened, so a second run would replace the good dump with a dump of
the empty 18 database) or when the dump or `docker-compose.pg16.yml` already
exists, so neither is overwritten.

The dump holds the whole database, that is the inventory of your containers
and images and their vulnerability findings. It is created readable only by
its owner; like `.env`, keep it out of backups and shares you do not control.

If a command fails, nothing after it runs and the 16 volume is untouched:

- Failure before `docker compose down` (the download, the dump): fix the cause,
  delete the incomplete `dockerupdateguard-pg16.dump` and
  `docker-compose.pg18.yml`, run `docker compose up -d` to restart the old
  stack, and start the block again.
- Failure after it (the restore, the start): the old setup comes back with
  `docker compose down`, `mv docker-compose.pg16.yml docker-compose.yml` and
  `docker compose up -d`. Once the cause is fixed, run the remaining lines of
  the block from the failed one on; the restore is a single transaction and
  leaves the 18 database as it was when it fails.

If an 18 stack was already started with an empty database before the restore
(for example after replacing the compose file without this procedure), stop the
application and recreate the database before the restore line:

```bash
docker compose stop dockerupdateguard
docker compose exec -T postgres sh -c 'dropdb -U "$POSTGRES_USER" --force "$POSTGRES_DB" && createdb -U "$POSTGRES_USER" "$POSTGRES_DB"'
```

Open the UI and check that your earlier data is there. Only then remove the old
volume, as a separate step. List the volumes and find the one named
`<project>_dockerupdateguard-postgres-data`:

```bash
docker volume ls | grep dockerupdateguard
```

> **Warning:** the new volume `<project>_dockerupdateguard-postgres`**ql**`-data`
> differs from the old one only by the letters `ql`. Removing the new one deletes
> the migrated database. Copy the name of the old one exactly, without `ql`:

```bash
docker volume rm <project>_dockerupdateguard-postgres-data
```

The dump `dockerupdateguard-pg16.dump` and `docker-compose.pg16.yml` can then
be deleted as well.

## Stopping / removing

```bash
docker compose down        # stop and remove containers, keep volumes
docker compose down -v     # also remove volumes (deletes the database!)
```

## Beyond the quick start

The compose files configure DockerUpdateGuard entirely through environment
variables (`DockerUpdateGuard__...`, `ConnectionStrings__...`,
`Telemetry__...`), following the standard ASP.NET Core configuration
convention where `__` maps to a JSON path segment. For advanced options not
covered by the tiers above — TLS-secured Docker engines, client
certificates, Portainer integration, DockerScout instead of Trivy, custom
scan intervals — mount an `appsettings.json` instead and see the full
configuration reference in [DOCKER.md](DOCKER.md) and the [README](README.md#configuration-reference).
An internal CA is trusted by mounting its PEM file into `/app/certs` (read-only), also with the default
non-root user; see [DOCKER.md](DOCKER.md).

## Security notes

- Mounting `/var/run/docker.sock` grants the container root-equivalent
  access to the Docker host. Only run these compose files on trusted hosts.
- Set a strong, unique `POSTGRES_PASSWORD`; do not commit your `.env` file.
- Pin image tags (e.g. `networlddev/dockerupdateguard:1.2.3` instead of
  `:latest`) for reproducible, auditable deployments once you move past the
  initial evaluation.

## Troubleshooting

See [DOCKER.md](DOCKER.md#troubleshooting) for common issues, including
`appsettings.json` permission errors and Docker socket access on NAS
appliances.
