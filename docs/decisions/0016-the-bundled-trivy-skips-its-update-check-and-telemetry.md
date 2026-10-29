# 0016: The bundled Trivy skips its update check and telemetry

- **Status:** Accepted
- **Date:** 2026-10-29
- **Area:** Hosting and configuration
- **Source:** Issue #372
- **Supersedes:** —

## Context

The image bundles the Trivy CLI, which the Trivy provider starts for every scan in client mode against the operator's
Trivy server ([Vulnerabilities](../areas/vulnerabilities.md#trivy)). Trivy v0.76.1, the version issue #372 bundles,
sends on every `trivy image` run a background request to `https://check.trivy.dev/updates` with a stable host
identifier (a SHA-256 of the host name and the first MAC address), the command, the flag names, the OS and the
architecture. `--quiet`, which the provider passes, only hides the printed notice; the request stops only when both
the version check and telemetry are switched off. v0.58.1, the version bundled before, makes no such call. A
deployment with restricted egress would gain an outbound destination it never allowed, and every scan would report
a host fingerprint to a third party, without anything in DockerUpdateGuard asking for it.

## Options considered

1. **Set `TRIVY_SKIP_VERSION_CHECK=true` and `TRIVY_DISABLE_TELEMETRY=true` in the image's environment** — no code
   change; every Trivy the application starts inside the image inherits them, a custom `TrivyExecutablePath`
   included, and an older Trivy ignores environment variables it does not know. Outside the image the operator
   sets them. Chosen.
2. **Add `--skip-version-check --disable-telemetry` to the provider's argument list** — also covers a Trivy run
   outside the image, but changes production code, its tests and the argument list of
   [0015](0015-the-trivy-scan-target-follows-an-end-of-options-marker.md), and an older custom Trivy without these
   flags fails every scan with `unknown flag`.
3. **Leave it to the operator** (document the call, block it by network policy) — every default deployment would
   send the fingerprint until someone notices.

## Decision

The container image sets `TRIVY_SKIP_VERSION_CHECK=true` and `TRIVY_DISABLE_TELEMETRY=true`, so the bundled Trivy
sends nothing to `check.trivy.dev`; the provider's arguments stay unchanged. The behavior is
described in [Hosting](../areas/hosting.md#container-image).

## Consequences

- An operator who sets either variable to `false` in the container gets Trivy's update notice request back; that is
  the operator's choice.
- A Trivy started outside the image (the application run without the container) gets the suppression only from the
  operator's environment.
- Measured on the bundled v0.76.1 with a control run (issue #372); a later Trivy update is measured again, since a
  new release may add another outbound call or rename the variables.