# 0011: Mounted CA certificates are trusted through a private SSL_CERT_DIR

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
container make outbound TLS calls: the .NET application (OpenSSL on Linux, also for Npgsql) and the bundled Trivy CLI
(Go), which inherits the application's environment. Both read `SSL_CERT_FILE` and a colon-separated `SSL_CERT_DIR`.
The image has no tool that can decode a certificate (no `openssl` CLI), so the entrypoint can check a PEM block's
structure but not its content.

## Options considered

1. **Make the system trust store group-writable in the Dockerfile** and keep `update-ca-certificates` — rejected: it
   gives every process in group `0` lasting write access to the trust anchors of the container, and it fails with a
   read-only root filesystem.
2. **Build one combined bundle under `/tmp` and export `SSL_CERT_FILE`** (the issue's recommendation) — rejected:
   .NET 10 stops reading a bundle at the first block it cannot decode, so one base64-valid but undecodable block
   silently drops every mounted CA after it (measured by Security on the plan of #285: the later CA's chain ended in
   `PartialChain`, while Go still trusted it); it also has to copy a pre-set `SSL_CERT_FILE` and depends on that file
   ending in a newline.
3. **One file per accepted certificate block in a private directory under `/tmp`, appended to `SSL_CERT_DIR`** —
   chosen: .NET 10 and Go read every file of every listed directory on its own, with any file name (measured by
   Security), so an undecodable block costs only itself; the system bundle and a pre-set `SSL_CERT_FILE` are not
   touched at all.
4. **Load the certificates in C# and pass them to each `HttpClient` handler** — rejected: it misses Trivy (a separate
   process) and Npgsql, and spreads trust configuration over every integration client.
5. **Keep `update-ca-certificates` for root and use the directory only for other users** — rejected: two paths with
   two behaviors to verify; the directory works for root as well.

## Decision

The entrypoint writes every complete, structurally valid PEM certificate block of the `.crt` and `.pem` files in
`/app/certs` to its own file in a new private directory under `/tmp`, and exports `SSL_CERT_DIR` as the previous value
(default `/etc/ssl/certs`) followed by that directory, for every UID including root; `update-ca-certificates` is no
longer run and `SSL_CERT_FILE` is never changed. When no block is usable or the directory cannot be written,
`SSL_CERT_DIR` stays as it was. The behavior is described in [Hosting and configuration](../areas/hosting.md),
*Container image*.

## Consequences

- The system trust store is never written; mounted certificates add to the system CAs and never replace them.
- Only programs that honor `SSL_CERT_DIR` see the mounted certificates; in the image these are the application, its
  database driver and Trivy.
- A block that is structurally valid PEM but not a decodable certificate is accepted and counted, then ignored by
  .NET and Go without a message; it affects no other certificate. Detecting it would need a decoder the image does
  not ship.
- Private-key blocks never reach the directory; a leaf certificate in `/app/certs` becomes a trust anchor, as before.
- Every start writes a new directory; nothing cleans up earlier ones, because `/tmp` may be shared and another
  instance's directory must not be removed. They are a few kilobytes each and vanish with a tmpfs `/tmp`.
- `entrypoint.sh` has no unit test harness (no shell test runner in this repository, and the coverage measure covers
  only C# and Razor files, so no coverage exclusion follows); its behavior is verified by running the image or the
  script, as named in the plan of issue #285.