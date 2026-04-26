# DockerUpdateGuard container image

This document is intended for container registries or image catalogs that need a deployment-focused description of the DockerUpdateGuard image.

For the full project overview and complete configuration reference, see [README.md](README.md).

## What the image does

The image runs the DockerUpdateGuard web UI and background workers. It connects to:

- a PostgreSQL database for persistent state
- one or more Docker Engine endpoints for runtime discovery and resource sampling
- optional Docker Hub, Portainer, Trivy, and OTLP endpoints

The container listens on port `8080`.

## Required configuration

At minimum, provide:

- a PostgreSQL connection string
- at least one Docker instance definition

Example:

```bash
docker run -d \
  --name dockerupdateguard \
  -p 8080:8080 \
  --mount type=bind,source=/var/run/docker.sock,target=/var/run/docker.sock \
  -e ConnectionStrings__DockerUpdateGuard="Host=postgres;Port=5432;Database=dockerupdateguard;Username=dockerupdateguard;Password=change-me" \
  -e DockerUpdateGuard__DockerInstances__0__Name="Local Docker" \
  -e DockerUpdateGuard__DockerInstances__0__BaseUrl="unix:///var/run/docker.sock" \
  networlddev/dockerupdateguard:latest
```

## Linux Docker socket usage

If the image should inspect the Docker Engine of the Linux host it runs on, bind-mount the Unix socket:

```bash
--mount type=bind,source=/var/run/docker.sock,target=/var/run/docker.sock
```

and configure the matching Docker instance URL:

```text
DockerUpdateGuard__DockerInstances__0__BaseUrl=unix:///var/run/docker.sock
```

Both are required. Mounting the socket without setting the matching `BaseUrl`, or configuring the `BaseUrl` without mounting the socket, is not enough.

## Important environment variables

| Variable | Purpose |
| --- | --- |
| `ConnectionStrings__DockerUpdateGuard` | PostgreSQL connection string |
| `DockerUpdateGuard__DockerInstances__0__Name` | Display name of the first Docker instance |
| `DockerUpdateGuard__DockerInstances__0__BaseUrl` | Docker endpoint such as `unix:///var/run/docker.sock` |
| `DockerUpdateGuard__DockerHub__UserName` | Optional Docker Hub username |
| `DockerUpdateGuard__DockerHub__Pat` | Optional Docker Hub personal access token |
| `DockerUpdateGuard__Vulnerabilities__Enabled` | Enables vulnerability refresh |
| `DockerUpdateGuard__Vulnerabilities__Provider` | `None`, `DockerScout`, or `Trivy` |
| `DockerUpdateGuard__Vulnerabilities__TrivyBaseUrl` | Required when `Provider=Trivy` |
| `Telemetry__OtlpEndpoint` | Optional OTLP collector endpoint |
| `ASPNETCORE_URLS` | ASP.NET Core bind address; defaults to `http://+:8080` in the image |
| `DockerUpdateGuard__DisplayVersion` | Version label shown in the UI; set automatically in the published image |

## Volumes and mounts

Common mounts for containerized deployments:

| Mount | When needed |
| --- | --- |
| `/var/run/docker.sock` | Required for Linux host Docker Engine access through the Unix socket |
| client certificate file | Required when a Docker instance uses TLS client certificates |

## Networking

The image needs outbound access to:

- PostgreSQL
- the configured Docker Engine endpoint
- Docker Hub or another supported registry endpoint
- optional Portainer
- optional Trivy
- optional OTLP collector

## Startup behavior

On startup, the application:

1. applies database migrations
2. synchronizes configured Docker instances
3. synchronizes Docker Hub account images
4. refreshes telemetry inventory metrics

After that, the web UI and background scan workers keep the inventory current.
