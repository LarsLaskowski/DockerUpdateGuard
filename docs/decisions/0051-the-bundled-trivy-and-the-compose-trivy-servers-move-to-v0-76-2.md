# 0051: The bundled Trivy and the compose Trivy servers move to v0.76.2

- **Status:** Accepted
- **Date:** 2026-11-18
- **Area:** Hosting and configuration
- **Source:** Issue #506
- **Supersedes:** — (amends [0005](0005-urls-are-output-with-user-info-masked.md),
  [0015](0015-the-trivy-scan-target-follows-an-end-of-options-marker.md) and
  [0016](0016-the-bundled-trivy-skips-its-update-check-and-telemetry.md): the bundled Trivy they name as v0.76.1 is
  v0.76.2, bundled only once the cases they rest on hold for it)

## Context

The image bundles the Trivy CLI from the `aquasec/trivy` image, pinned by the digest of its multi-arch image index,
and the documented compose files run a Trivy server of the same version, so client and server stay on one version.
Three released records rest on the bundled binary and name v0.76.1: [0005](0005-urls-are-output-with-user-info-masked.md)
on it reading the server address from `TRIVY_SERVER`, [0015](0015-the-trivy-scan-target-follows-an-end-of-options-marker.md)
on it honoring `--` before the scan target, and [0016](0016-the-bundled-trivy-skips-its-update-check-and-telemetry.md)
on `TRIVY_SKIP_VERSION_CHECK` and `TRIVY_DISABLE_TELEMETRY` stopping its request to `check.trivy.dev`. Trivy v0.76.2
was published on 2026-11-05 and is the newest release (`latest` carries its digest). Dependabot does not cover the
Docker images here, so the update follows *Updating the bundled Trivy* in [CONTRIBUTING](../CONTRIBUTING.md), which
measures each of these properties again before a version is bundled.

## Options considered

1. **Move the bundled CLI and both compose servers to v0.76.2 together, after the measurements** — the image ships the
   current release, client and server stay on one version, and the properties the three records rely on are
   measured on the binary the build copies. Chosen.
2. **Stay on v0.76.1** — keeps the binary already measured, but the image falls behind the release the Trivy project
   maintains, and an operator who pulls the server's `latest` gets a server newer than the client.

## Decision

The image bundles Trivy v0.76.2, pinned by the digest of its multi-arch image index, and the Trivy servers of
`docs/docker-compose.trivy.yml` and `docs/docker-compose.full.yml` run `aquasec/trivy:0.76.2`. The provider's
arguments, its `TRIVY_SERVER` environment and the image's two Trivy settings stay as 0005, 0015 and 0016 decided; the
bundled version is stated in [Hosting](../areas/hosting.md#container-image).

## Consequences

- Where 0005, 0015 and 0016 name v0.76.1 as the bundled version, v0.76.2 applies; what they say was measured with
  v0.58.1 stays a statement about that version.
- Measured on v0.76.2 before it was bundled ([CONTRIBUTING](../CONTRIBUTING.md#updating-the-bundled-trivy), linux/amd64
  binary from the index above, sha256 `6a972c69606c7b3ea4c6bdcc6fc9332ea9ad38381624310c1e85cc77345909f3`): the index,
  the amd64 manifest, its config and its four layers match their digests and sizes; the binary reports
  `Version: 0.76.2`, is statically linked, and `trivy server` still offers `--listen`. Without `--`,
  `--config=x.y/z:latest` is read as the configuration file; after `--`, `--config=…`, `--input=…` and `-a/b` are
  image names, each run exits 1 and opens no file they name, and `library/alpine:3.20` fails on the unreachable
  `TRIVY_SERVER`. With both settings `true` the scan connects only to the registry, the Docker socket and the server;
  without them it also connects to `check.trivy.dev`. The properties 0005, 0015 and 0016 rest on hold for v0.76.2.
- The next Trivy update is measured again with the same procedure; once this record is released, it names the new
  version through a record of its own that amends this one.