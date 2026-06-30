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
  -v /path/to/appsettings.json:/app/appsettings.json:ro \
  networlddev/dockerupdateguard:latest
```

## Linux Docker socket usage

If the image should inspect the Docker Engine of the Linux host it runs on, bind-mount the Unix socket:

```bash
--mount type=bind,source=/var/run/docker.sock,target=/var/run/docker.sock
```

and configure the matching Docker instance URL in appsettings.json, for example:

```json
{
  "DockerUpdateGuard": {
    "DockerInstances": [
      {
        "Name": "Local Docker",
        "BaseUrl": "unix:///var/run/docker.sock",
        "Enabled": true
      }
    ]
  }
}
```

Both are required. Mounting the socket without setting the matching `BaseUrl`, or configuring the `BaseUrl` without mounting the socket, is not enough.

The image runs as a non-root user (UID `64000`) in the root group (GID `0`) by default. This keeps the container off a root UID while still allowing access to a Docker socket that is owned by `root:root` (the common case on Synology DSM and similar appliances) without any extra flags.

On standard Linux hosts the socket is usually owned by the host `docker` group instead, so the container process must additionally be a member of that group to read it. Grant access by passing the host socket's group id, for example:

```bash
--group-add "$(stat -c '%g' /var/run/docker.sock)"
```

Granting access to the Docker socket is equivalent to root on the host regardless of the container user — only mount it into trusted deployments.

## Configuration via appsettings.json

All configuration should be provided via appsettings.json (or appsettings.{Environment}.json). When running in a container, mount your configuration file into the container's content root, for example: `-v /path/to/appsettings.json:/app/appsettings.json:ro`.

Common JSON paths:

| JSON path | Purpose |
| --- | --- |
| `ConnectionStrings:DockerUpdateGuard` | PostgreSQL connection string |
| `DockerUpdateGuard:DockerInstances[0]:Name` | Display name of the first Docker instance |
| `DockerUpdateGuard:DockerInstances[0]:BaseUrl` | Docker endpoint such as `unix:///var/run/docker.sock` |
| `DockerUpdateGuard:DockerHub:UserName` | Optional Docker Hub username |
| `DockerUpdateGuard:DockerHub:Pat` | Optional Docker Hub personal access token |
| `DockerUpdateGuard:Vulnerabilities:Enabled` | Enables vulnerability refresh |
| `DockerUpdateGuard:Vulnerabilities:Provider` | `None`, `DockerScout`, or `Trivy` |
| `DockerUpdateGuard:Vulnerabilities:TrivyBaseUrl` | Required when `Provider=Trivy` |
| `Telemetry:OtlpEndpoint` | Optional OTLP collector endpoint |
| `DockerUpdateGuard:DisplayVersion` | Version label shown in the UI; set by the image build argument |

## Volumes and mounts

Common mounts for containerized deployments:

| Mount | When needed |
| --- | --- |
| `/var/run/docker.sock` | Required for Linux host Docker Engine access through the Unix socket |
| `/app/appsettings.json` (mount file) | Application configuration (appsettings.json) |
| `/app/certs` (directory) | Optional: client certificates for TLS-secured Docker engine access |

Client certificates

When a Docker Engine endpoint requires TLS client authentication, mount the certificate files into the container and set the instance's `CertificatePath` in appsettings.json to the in-container file path. Recommended pattern:

- Place certificates on the host (for example `/opt/dockerupdateguard/certs/`) and mount read-only into the container:

```bash
-v /opt/dockerupdateguard/certs:/app/certs:ro
```

- Use a stable in-container path (for example `/app/certs`) and reference that path from `CertificatePath`.

Automatic CA import on startup

The published image now includes a small entrypoint script that will import any PEM/CRT files found under `/app/certs` into the container's system trust store at startup (it copies them into `/usr/local/share/ca-certificates` and runs `update-ca-certificates`). The image also ensures the `ca-certificates` package is available. Behavior:

- If `/app/certs` contains one or more `*.crt`/`*.pem` files and the container has permission to write the system trust store, the root certificates are imported automatically and become trusted by HttpClient, Npgsql, and other system TLS consumers.
- If no certificates are provided, the import step is skipped and the container starts normally.
- The image runs as a non-root user (UID `64000`) by default and therefore cannot write the system trust store, so this automatic import is skipped. To use it, run the container as root for the trust-store import (for example `--user 0`) or bake the CA into a derived image at build time; the application still runs as non-root otherwise.

Certificate formats and usage

- Root CA: provide the CA as a PEM or CRT file (e.g. `ca-root.crt` or `ca-root.pem`). This is sufficient for the runtime to validate HTTPS servers for PostgreSQL, Portainer, Trivy, etc.
- Wildcard/server cert (.crt): not required for client‑side trust; the Root CA that signed the server certificate is what must be trusted.
- Client certificate (for mutual TLS to a Docker engine): store the client certificate (PFX/P12 or PEM) in `/app/certs` and set the Docker instance `CertificatePath` to the in-container file path (e.g. `/app/certs/client.pfx`). The application will load PFX/.p12 or PEM client certificates when the `CertificatePath` points to a readable file.

Example appsettings.json snippet (root CA used by Postgres + client cert for a Docker instance):

```json
{
  "ConnectionStrings": {
    "DockerUpdateGuard": "Host=db.example.local;Port=5432;Database=dug;Username=user;Password=pass;Ssl Mode=VerifyFull;Trust Server Certificate=false;Root Certificate=/app/certs/ca-root.crt"
  },
  "DockerUpdateGuard": {
    "DockerInstances": [
      {
        "Name": "Remote Engine",
        "BaseUrl": "https://docker.example.local:2376",
        "UseTls": true,
        "CertificatePath": "/app/certs/client.pfx"
      }
    ],
    "Vulnerabilities": {
      "Provider": "Trivy",
      "TrivyBaseUrl": "https://trivy.example.local:4954"
    }
  }
}
```

Run example (optional certificates mount):

```bash
docker run -d \
  --name dockerupdateguard \
  -p 8080:8080 \
  -v /path/to/appsettings.json:/app/appsettings.json:ro \
  -v /opt/dockerupdateguard/certs:/app/certs:ro \ # optional: root CA, client certs
  networlddev/dockerupdateguard:latest
```

Permissions and security

- Mount certificate files read-only into the container.
- Keep host-side files owned and permissioned so only authorized users can read private keys.
- Do not commit private keys into source control or include them in image layers.

Notes

- The automatic import is optional; the image works without any mounted certificates.
- The image runs as the non-root user `64000` (root group, GID `0`) by default. If the environment requires importing certificates, bake the CA into a derived image or run the container as root (for example `--user 0`) so the script can update the trust store.
## Networking

The image needs outbound access to:

- PostgreSQL
- the configured Docker Engine endpoint
- Docker Hub or another supported registry endpoint
- optional Portainer
- optional Trivy
- optional OTLP collector