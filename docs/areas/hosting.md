# Hosting and configuration

## Scope

How the application is configured, how it starts, how it serves HTTP and how its container image is built to run.
It covers loading and validating configuration, resolving the database connection, the startup sequence up to the
point where requests are served, configuration reload, the HTTP-level pipeline and error pages, the container image
and the security posture the deployment must provide. Operators rely on it to deploy and configure the application;
every feature area relies on it for validated configuration and a migrated database.

## Behavior

### Configuration sources

- Configuration is read from, in increasing precedence: `appsettings.json`, `appsettings.{Environment}.json`,
  environment variables and command-line arguments. Environment variables use `__` as the
  section separator (`DockerUpdateGuard__Scanning__RetryCount`).
- The application reads four top-level sections: `DockerUpdateGuard` (application settings), `Telemetry` (see
  [Telemetry](telemetry.md)), `ConnectionStrings` and `Logging`. The key-by-key reference with defaults and ranges is
  the configuration reference in `README.md`; what each feature key means is stated in the feature's area document.
- The environment defaults to `Production`. Only `Development` changes behavior here (see *HTTP pipeline*).
- Secrets (database password, Docker Hub PAT, Portainer password or API token) are ordinary configuration values;
  any of the sources above may supply them. There is no further secret-store integration.

### Validation

- The whole `DockerUpdateGuard` section is validated as one unit. All rule violations are collected and reported
  together, each naming the offending key (for example `'DockerUpdateGuard:Database:MigrationRetryDelaySeconds' must be
  greater than zero`). Messages name keys and, at most, configured instance names and a configured client-certificate
  path; they never echo connection strings, passwords, tokens or file contents.
- Validation reads the configured client-certificate file (`CertificatePath`) of each enabled Docker instance, at
  startup and on every reload (none when it is blank or its extension is not `.pfx`/`.p12`); it reads no other file.
  The rules are in [Docker instances](docker-instances.md), *TLS options* and *Limits and errors*.
- Any violation fails startup: the process exits before the database is contacted and before any request is served.
- The `Telemetry` section is validated even earlier, while services are registered; see [Telemetry](telemetry.md).
- Rules owned by this area:
  - a connection string must resolve to a non-blank value (see *Database connection*);
  - `DockerUpdateGuard:Database:MaxConnectionRetryCount`, `MaxConnectionRetryDelaySeconds`,
    `MigrationStartupTimeoutSeconds` and `MigrationRetryDelaySeconds` must each be greater than zero.
- The rules for feature keys (Docker Hub, release metadata, vulnerabilities, scanning, Docker instances, Portainer)
  are part of the respective area documents; they are enforced by the same validation step.

### Database connection

- The database is PostgreSQL. The connection string is resolved in this order:
  1. `DockerUpdateGuard:ConnectionString`, trimmed, when it is not blank;
  2. otherwise `ConnectionStrings:{name}`, where `{name}` is `DockerUpdateGuard:ConnectionStringName`
     (default `DockerUpdateGuard`).
- Validation and the database registration use the same resolution, so the validated connection string is always
  the one in use.
- During normal operation, a database command that fails transiently is retried up to
  `DockerUpdateGuard:Database:MaxConnectionRetryCount` times (default `8`) with a back-off of at most
  `DockerUpdateGuard:Database:MaxConnectionRetryDelaySeconds` seconds (default `30`) between attempts.

### Startup sequence

Startup runs these steps in order; requests are served and background jobs start only after all of them have
succeeded.

1. **Configuration** is bound and validated (see *Validation*).
2. **Wait for the database.** The application opens and closes a connection. A transient failure (a database error
   the driver reports as transient, a socket error or a timeout, also when wrapped in another error) is retried every
   `DockerUpdateGuard:Database:MigrationRetryDelaySeconds` seconds (default `5`) until
   `DockerUpdateGuard:Database:MigrationStartupTimeoutSeconds` (default `120`) have elapsed since the first attempt.
   Each retry is logged with its attempt number. A non-transient failure, or a transient one after the deadline, is
   logged and fails startup. A shutdown request during the wait ends it.
3. **Apply migrations.** Pending schema migrations are applied automatically. On PostgreSQL the application first
   takes the session-scoped advisory lock with key `5410320540103205` on the same connection that runs the
   migrations, so instances starting concurrently against one database apply migrations one at a time; the others
   wait for the lock and then find nothing left to apply. Taking the lock and migrating form one unit: if a transient
   fault forces a retry, both are repeated together. The lock is released afterwards; a failed release is logged and
   does not change the outcome, because the lock also ends with the session. A failed migration is logged and fails
   startup.
4. **Synchronize configured Docker instances** into the database (see [Docker instances](docker-instances.md)).
5. **Seed the inventory gauges** from the database (see [Telemetry](telemetry.md)).

Only local database work runs on this path. Work that needs the network, such as Docker Hub account image
discovery, runs in the background jobs after startup (see [Scan engine](scan-engine.md)), so an unreachable
registry never delays or prevents startup.

### Configuration reload

- `appsettings.json` and `appsettings.{Environment}.json` are watched; a change is picked up without a restart.
  Values that are read on use take effect at their next use; the feature areas state which of their keys do, and the
  [Scan engine](scan-engine.md) states when job intervals change.
- These values are read once at startup and need a restart to change:
  - the connection string, `DockerUpdateGuard:ConnectionStringName` and all `DockerUpdateGuard:Database:*` keys;
  - `DockerUpdateGuard:DockerHub:ApiBaseUrl`, `DockerUpdateGuard:DockerHub:RequestTimeoutSeconds` (also the request
    timeout for generic registries and release-metadata feeds) and `DockerUpdateGuard:ReleaseMetadata:*`;
  - the whole `Telemetry` section.
- A reloaded configuration is validated by the same rules as at startup when it is next read; a reload that fails
  validation raises an error where it is read (see *Known gaps*). Because validation reads the client-certificate
  files again, a certificate file deleted or broken while the application runs makes the reloaded configuration
  invalid.

### HTTP pipeline

- The application serves only its interactive web UI and static assets; there is no separate HTTP API.
- Outside `Development`:
  - an unhandled exception while processing a request renders the `/error` page with a generic message
    ("Unexpected error"); no exception details, stack traces or configuration values are shown;
  - HSTS is applied and HTTP requests are redirected to HTTPS when an HTTPS port is known to the server.
- In `Development`, the platform's developer exception page is shown instead and there is no HSTS or HTTPS
  redirection.
- In every environment, a response with an error status code and no body (such as `404`) is re-rendered as the
  `/not-found` page, and an unknown route inside the UI shows the same page. Both pages are described in
  [Web UI](web-ui.md).
- Form posts are protected by anti-forgery validation.

### Container image

- The runtime image is based on the Alpine variant of the ASP.NET Core runtime (`mcr.microsoft.com/dotnet/aspnet`,
  `10.0-alpine` by default). The base image name and digest are recorded as the OCI labels
  `org.opencontainers.image.base.name` and `org.opencontainers.image.base.digest`.
- The application listens on plain HTTP on port `8080` on all interfaces. TLS is not terminated inside the container.
- The process runs as UID `64000` in the root group (GID `0`), never as UID `0`. `/app` is owned by `64000:0` with
  group permissions equal to owner permissions, so the application also works when started with another UID in
  group `0`. Membership in group `0` keeps a `root:root` Docker socket readable where the socket grants group access.
- The Trivy CLI is bundled at `/usr/local/bin/trivy` (taken from `aquasec/trivy:0.58.1` by default) with its cache in
  `/tmp/trivy`, so Trivy client mode works without extra installation (see [Vulnerabilities](vulnerabilities.md)).
- The image sets `DockerUpdateGuard__DisplayVersion` to the version it was built with.
- CA certificates are installed so outbound TLS connections can be verified.
- Before the application starts, the entrypoint:
  1. checks that `/app/appsettings.json` and `/app/appsettings.{Environment}.json`, if present, are readable by the
     running user; if one exists but is not readable, it prints the UID/GID and how to fix the host permissions,
     and exits with code `1`;
  2. trusts the CA certificates mounted into `/app/certs`, for any UID, root included: every complete PEM
     certificate block of the `.crt` and `.pem` files (other file names are skipped with a message; files in
     subdirectories and dotfiles are ignored) is written to its own file in a new private directory under `/tmp`
     (mode `0700`, owned by the running user), and that directory is appended to `SSL_CERT_DIR`, after a pre-set
     value (a message names it) or else after `/etc/ssl/certs`. The application, its database driver and the bundled
     Trivy read `SSL_CERT_DIR`, so they trust the mounted certificates in addition to the system CA certificates. The
     system trust store, `SSL_CERT_FILE` and `update-ca-certificates` are never used or changed. Only blocks whose
     marker lines are exactly `-----BEGIN CERTIFICATE-----` and `-----END CERTIFICATE-----` (not indented, label
     `CERTIFICATE` only, so neither `TRUSTED CERTIFICATE` nor DER files are accepted) around at least one base64
     body line are taken; a byte order mark and CRLF line endings are tolerated, and the files written hold the
     standard markers and the body lines only, so a private key in the same file is never copied. A block that is
     well-formed PEM but no certificate is counted and then ignored by the TLS libraries without affecting the
     other files. When no file yields a block, or `/tmp` is not writable, a message is printed, only the system CA
     certificates apply and the container starts; certificate handling never stops the container. A read-only root
     filesystem therefore needs a writable `/tmp` (for example `--tmpfs /tmp`). `/tmp` should not be shared between
     containers, and the directories of earlier starts are not removed.

### Security posture

These properties hold today and must be provided for by the deployment:

- **No authentication or authorization.** Every page and every action (manual scans, tag selection, container
  actions) is available to anyone who can reach port `8080`. The application must run behind a trusted perimeter: a
  private network, a VPN or a reverse proxy that authenticates users.
- **No health-check endpoint and no rate limiting.**
- **Docker socket access is root-equivalent on the host.** Mounting the host's Docker socket gives the application,
  and anyone who controls it, the power of root on that host. This is inherent to the feature.
- **Single instance.** Several instances may share one database for migrations (see *Startup sequence*), but the
  background jobs do not coordinate across instances (see [Scan engine](scan-engine.md)).
- That secrets never appear in logs or telemetry is stated in [Telemetry](telemetry.md); that the UI never shows
  them is stated in [Web UI](web-ui.md).

## Limits and errors

| Situation | Result |
| --- | --- |
| Invalid `DockerUpdateGuard` configuration at startup | Startup fails; every violation is reported with its key. |
| No connection string resolves | Startup fails: `'DockerUpdateGuard:ConnectionString' or 'ConnectionStrings:{name}' must be configured`. |
| Invalid `Telemetry` configuration | Startup fails before the host is built (see [Telemetry](telemetry.md)). |
| Database unreachable within `MigrationStartupTimeoutSeconds` | The attempts are logged; startup fails. |
| Non-transient database error while waiting | Startup fails immediately. |
| Migration fails | Logged; startup fails. |
| Advisory lock cannot be released | Logged; the migration outcome stands. |
| Mounted settings file not readable by the container user | Entrypoint exits with code `1` and a fix-it message. |
| No usable certificate in `/app/certs` (missing directory, no files, nothing that is a PEM certificate block) | A message is printed; only the system CA certificates apply; the container starts. |
| `/tmp` not writable when certificates are mounted | A message is printed; the mounted certificates are not used; the container starts. |
| Unhandled exception in a request (non-Development) | The `/error` page with a generic message. |
| Unknown URL or bodyless error status | The `/not-found` page. |

### Known gaps

- An invalid configuration reload is not rejected while the last valid configuration stays in use: the background
  jobs read their interval from the reloaded configuration outside their error handling, so a reload that fails
  validation can stop the host ([Scan engine](scan-engine.md)). Since validation also reads the client-certificate
  files, this includes a reload while a configured certificate file is missing or unusable.
  Tracked in [#281](https://github.com/LarsLaskowski/DockerUpdateGuard/issues/281).
- Forwarded headers from a reverse proxy are not supported. Behind a TLS-terminating proxy the application sees
  plain HTTP, so it never sends HSTS and HTTPS redirection cannot determine a port; both are left to the proxy.
  Tracked in [#283](https://github.com/LarsLaskowski/DockerUpdateGuard/issues/283).
- Waiting for the migration advisory lock is bounded by the 30-second database command timeout and then retried like
  any transient database failure (see *Database connection*), so an instance that starts while another one runs a
  long migration can fail its startup after several minutes.
  Tracked in [#287](https://github.com/LarsLaskowski/DockerUpdateGuard/issues/287).
- The shipped `appsettings.json` configures one Docker instance with an empty name, which fails configuration
  validation; the application does not start with the shipped settings until that instance is named or removed.
  Tracked in [#288](https://github.com/LarsLaskowski/DockerUpdateGuard/issues/288).

## Related decisions

- [0007](../decisions/0007-client-certificates-are-validated-as-pkcs12-at-startup.md): client certificates are
  validated as PKCS#12 at startup, so validation reads a file.
- [0011](../decisions/0011-mounted-ca-certificates-use-a-private-cert-dir.md): mounted CA certificates are trusted
  through a private directory appended to `SSL_CERT_DIR`, so no write access to the system trust store is needed.

## Not here

- What each feature key means and its valid range: the feature areas ([Docker instances](docker-instances.md),
  [Registries](registries.md), [Base images](base-images.md), [Update detection](update-detection.md),
  [Vulnerabilities](vulnerabilities.md), [Container actions](container-actions.md), [Scan engine](scan-engine.md)).
- The complete configuration key reference: `README.md`, *Configuration reference*.
- Retry of outbound HTTP calls: [Registries](registries.md).
- Logs, traces, metrics and the rule that credentials stay out of them: [Telemetry](telemetry.md).
- Pages and what they show: [Web UI](web-ui.md).
- Building, testing, CI and releasing the image: `docs/CONTRIBUTING.md`.
- Compose files and the walkthrough for the deployment tiers: `INSTALL.md`.
