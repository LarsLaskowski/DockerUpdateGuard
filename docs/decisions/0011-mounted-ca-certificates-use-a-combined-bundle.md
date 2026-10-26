# 0011: Mounted CA certificates are trusted through a combined bundle and SSL_CERT_FILE

- **Status:** Proposed
- **Date:** 2026-10-26
- **Area:** Hosting and configuration
- **Source:** Issue #285
- **Supersedes:** —

## Context

The entrypoint imported CA certificates from `/app/certs` into the system trust store with `update-ca-certificates`,
but only as root or when the trust store was writable. The image runs as UID `64000` in group `0` and leaves the
Alpine trust store owned by root, so for the default user the import was always skipped, and operators with an
internal CA (private registry, Trivy server, Portainer, TLS Docker endpoint, OTLP collector) had to run the container
as root, which defeats the non-root hardening (`docs/ARCHITECTURE.md`, *Security posture*). Two programs in the
container make outbound TLS calls: the .NET application (OpenSSL on Linux) and the bundled Trivy CLI (Go), which
inherits the application's environment. Both read the variable `SSL_CERT_FILE` for their root certificates.

## Options considered

1. **Make the system trust store group-writable in the Dockerfile** and keep `update-ca-certificates` — rejected: it
   gives every process in group `0` lasting write access to the trust anchors of the container, and it fails with a
   read-only root filesystem.
2. **Build a combined bundle under `/tmp` and export `SSL_CERT_FILE`** — chosen: no root, no change to the system
   trust store, works with a read-only root filesystem when `/tmp` is writable, and covers .NET and Trivy alike.
3. **A directory of the mounted certificates added through `SSL_CERT_DIR`** — rejected: OpenSSL looks certificates
   up in a directory by subject-hash file names, which the image cannot compute (no `openssl` CLI), and a
   colon-separated directory list depends on the .NET version.
4. **Load the certificates in C# and pass them to each `HttpClient` handler** — rejected: it misses Trivy (a separate
   process) and Npgsql, and spreads trust configuration over every integration client.
5. **Keep `update-ca-certificates` for root and use the bundle only for other users** — rejected: two paths with two
   behaviors to verify; the bundle path works for root as well.

## Decision

The entrypoint copies the system CA bundle (or the file a pre-set `SSL_CERT_FILE` names) and then every complete PEM
certificate block of the `.crt` and `.pem` files in `/app/certs` into a new bundle in a private directory under
`/tmp`, and exports `SSL_CERT_FILE` with its path before it starts the application, for every UID including root;
`update-ca-certificates` is no longer run. When no certificate is usable or the bundle cannot be written,
`SSL_CERT_FILE` stays as it was. The behavior is described in [Hosting and configuration](../areas/hosting.md),
*Container image*.

## Consequences

- The system trust store is never written; mounted certificates add to the system CAs and never replace them.
- Only programs that honor `SSL_CERT_FILE` see the mounted certificates; in the image these are the application, its
  database driver and Trivy. A program that reads only `/etc/ssl/certs` would not.
- Private-key blocks in a `.pem` file never reach the bundle; a leaf certificate in `/app/certs` becomes a trust
  anchor, as before.
- `entrypoint.sh` has no unit test harness (no shell test runner in this repository, and the coverage measure covers
  only C# and Razor files, so no coverage exclusion follows); its behavior is verified by running the image or the
  script, as named in the plan of issue #285.