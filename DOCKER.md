## What the image does

The image runs the DockerUpdateGuard web UI and background workers. It connects to:

- a PostgreSQL database for persistent state
- one or more Docker Engine endpoints for runtime discovery and resource sampling
- optional Docker Hub, Portainer, Trivy, and OTLP endpoints

The image bundles the Trivy CLI. When `Provider=Trivy` is configured, image scans run through `trivy image` in client mode against `<TrivyBaseUrl>`, passed to the CLI as `TRIVY_SERVER` in its environment: the CLI pulls and analyzes the image locally and uses the configured Trivy server as the vulnerability database backend. The Trivy cache is written to `/tmp/trivy` (configurable via the `TRIVY_CACHE_DIR` environment variable).

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
| `DockerUpdateGuard:Vulnerabilities:TrivyBaseUrl` | Required when `Provider=Trivy`; base URL of the Trivy server used by the bundled Trivy CLI in client mode |
| `DockerUpdateGuard:Vulnerabilities:TrivyExecutablePath` | Optional path to the Trivy executable; defaults to `trivy` (bundled in the image) |
| `DockerUpdateGuard:Vulnerabilities:RequestTimeoutSeconds` | Timeout for vulnerability provider requests; defaults to `120` because first-time scans of large images are slow |
| `DockerUpdateGuard:Vulnerabilities:MaxParallelScans` | Number of vulnerability scans running at the same time (`1` to `8`); defaults to `1` |
| `Telemetry:OtlpEndpoint` | Optional OTLP collector endpoint |
| `DockerUpdateGuard:DisplayVersion` | Version label shown in the UI; set by the image build argument |

## Volumes and mounts

Common mounts for containerized deployments:

| Mount | When needed |
| --- | --- |
| `/var/run/docker.sock` | Required for Linux host Docker Engine access through the Unix socket |
| `/app/appsettings.json` (mount file) | Application configuration (appsettings.json) |
| `/app/certs` (directory) | Optional: CA certificates to trust (`.crt`/`.pem`) and client certificates for TLS-secured Docker engine access |

Client certificates

When a Docker Engine endpoint requires TLS client authentication, mount the certificate files into the container and set the instance's `CertificatePath` in appsettings.json to the in-container file path. Recommended pattern:

- Place certificates on the host (for example `/opt/dockerupdateguard/certs/`) and mount read-only into the container:

```bash
-v /opt/dockerupdateguard/certs:/app/certs:ro
```

- Use a stable in-container path (for example `/app/certs`) and reference that path from `CertificatePath`.

Automatic CA trust on startup

The published image includes a small entrypoint script that trusts the CA certificates found under `/app/certs` at startup. It does not touch the system trust store: every complete PEM certificate block of the `*.crt`/`*.pem` files is written to its own file in a private directory under `/tmp`, and that directory is appended to `SSL_CERT_DIR`. The application, Npgsql and the bundled Trivy read `SSL_CERT_DIR`, so they trust these certificates in addition to the system CA certificates. Behavior:

- It works for the default user (UID `64000`) and for any other user, including root; no `--user 0` and no derived image is needed.
- Certificates must be PEM with unindented `-----BEGIN CERTIFICATE-----` and `-----END CERTIFICATE-----` lines. DER files and `-----BEGIN TRUSTED CERTIFICATE-----` blocks are not accepted; the entrypoint prints a message for a file without an accepted block and skips it. A byte order mark and CRLF line endings are tolerated.
- Private keys are never copied: only the certificate blocks are written, so a `.pem` holding a certificate and a key contributes the certificate only.
- If `SSL_CERT_DIR` is already set, the private directory is appended after the existing value (the entrypoint says so); `SSL_CERT_FILE` is never read or changed.
- If no certificates are provided, or none is usable, a message is printed and the container starts with the system CA certificates only.
- With a read-only root filesystem (`--read-only`), provide a writable `/tmp` (for example `--tmpfs /tmp`); otherwise a message is printed and the mounted certificates are not used. Do not share `/tmp` between containers; directories of earlier starts are not removed.

Certificate formats and usage

- Root CA: provide the CA as a PEM-encoded file named `*.crt` or `*.pem` (e.g. `ca-root.crt` or `ca-root.pem`); a DER-encoded file is not accepted. This is sufficient for the runtime to validate HTTPS servers for PostgreSQL, Portainer, Trivy, etc.
- Wildcard/server cert (.crt): not required for client‑side trust; the Root CA that signed the server certificate is what must be trusted.
- Client certificate (for mutual TLS to a Docker engine): store the client certificate as a password-less PKCS#12 bundle (`.pfx` or `.p12`) with certificate and private key in `/app/certs` (readable by UID `64000`) and set the Docker instance `CertificatePath` to the in-container file path (e.g. `/app/certs/client.pfx`). PEM or DER files are not supported and the application refuses to start when an enabled instance names a missing, unreadable, non-PKCS#12 or password-protected file. Convert a PEM pair with `openssl pkcs12 -export -in cert.pem -inkey key.pem -out client.pfx -passout pass:`. Keep `cert.pem` and `key.pem` out of `/app/certs`: the entrypoint treats every `*.pem` file there as a CA certificate.

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

- The automatic CA trust is optional; the image works without any mounted certificates.
- The image runs as the non-root user `64000` (root group, GID `0`) by default; mounted CA certificates are trusted without root (see *Automatic CA trust on startup*).
## Troubleshooting

### UnauthorizedAccessException / "Permission denied" reading appsettings.json

The image runs as UID `64000` in GID `0`, not root. A bind-mounted `appsettings.json` keeps
whatever ownership and permission bits it has on the host — the `chown`/`chmod` steps in the
Dockerfile only apply to files baked into the image, not to files mounted at `docker run` time.
If the host file is not readable by UID `64000` or GID `0`, the app fails immediately at startup
with:

```
Unhandled exception. System.UnauthorizedAccessException: Access to the path '/app/appsettings.json' is denied.
 ---> System.IO.IOException: Permission denied
```

The entrypoint script checks this before starting the app and now fails with a short, actionable
message instead of the raw .NET stack trace.

This is common when the file lives on a network share (for example a Synology NAS exposed over
SMB/CIFS and mounted on the Docker host) where the share's permission mapping does not grant read
access to arbitrary container UIDs/GIDs. Fixes:

- On a local bind mount: `chmod 644 /path/to/appsettings.json` (or `chown` the file to GID `0`) on
  the host so it is group- or world-readable.
- On a CIFS/SMB mount of the host directory, pass mount options that make the file readable, e.g.
  `file_mode=0644,dir_mode=0755` (and `uid=`/`gid=` if the share requires a specific owner).
- Marking the volume `:ro` only makes the mount read-only inside the container; it does not affect
  whether the container's user can read the file at all — the underlying host permissions still
  govern read access.

No matching local account for UID `64000` needs to exist anywhere — Linux permission checks are
purely numeric, so `chmod`/`chown` work on a bare UID/GID even without a corresponding `/etc/passwd`
entry. On Synology DSM in particular, File Station's per-user permission picker only offers
existing DSM accounts, so it cannot grant access to UID `64000` directly. Do not create a DSM user
for this. Instead, either:

- set an "Everyone" read entry in File Station's advanced permissions for the file/folder, or
- SSH into the NAS and run `chmod 644` on the file directly.

## Networking

The image needs outbound access to:

- PostgreSQL
- the configured Docker Engine endpoint
- Docker Hub or another supported registry endpoint
- optional Portainer
- optional Trivy
- optional OTLP collector