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
