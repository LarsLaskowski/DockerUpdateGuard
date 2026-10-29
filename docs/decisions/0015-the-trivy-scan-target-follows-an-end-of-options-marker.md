# 0015: The Trivy scan target follows an end-of-options marker

- **Status:** Accepted
- **Date:** 2026-10-29
- **Area:** Vulnerabilities
- **Source:** Issue #367
- **Supersedes:** —

## Context

The Trivy provider starts the Trivy CLI with a fixed list of options followed by the scan target, built from the
image reference: `<registry>/<repository>:<tag>`, or `<repository>:<tag>` for `docker.io`. A reference the user
registers keeps plain parsing ([0014](0014-registry-tag-names-and-digests-are-validated-where-they-are-read.md),
options 18 and 21), so its registry or repository can begin with `-`: `--config=x.y/z` gives the target
`--config=x.y/z:latest`, `--input=/tmp/x.tar` the target `--input=/tmp/x.tar:latest`. Trivy parses its command line
with Cobra and pflag and reads such a target as one of its own options (a configuration file, a local archive to
scan, an output file) instead of as the image. No shell is involved, so only the options of Trivy itself are reachable.
References read from a Docker Engine container or a base-image label are held to Docker's reference grammar, which
admits no leading `-`.

## Options considered

1. **Put `--` between the options and the target** — pflag ends option parsing at `--`, so every target, whatever it
   begins with, reaches Trivy as the image name; one fixed argument, no new failure path, and a target Trivy cannot
   resolve fails its scan the way any unresolvable image does. Chosen.
2. **Refuse a target that begins with `-` before starting Trivy, with a fixed failure reason** — covers the same
   targets, but adds a failure path and a message of its own, and must match pflag's idea of an option exactly; once
   `--` is in place it is unreachable for every target it would refuse.
3. **Both** — the refusal adds nothing that option 1 leaves open.
4. **Hold registered references to the reference grammar** — changes what a user can register and leaves the
   references already in the catalog; rejected for the same reasons as
   [0014](0014-registry-tag-names-and-digests-are-validated-where-they-are-read.md), options 18 and 21.

## Decision

The Trivy provider passes `--` as the argument directly before the scan target, so no part of an image reference is
ever read as a Trivy option. The argument list is described in [Vulnerabilities](../areas/vulnerabilities.md#trivy).

## Consequences

- A registered reference that begins with `-` is scanned as an image name; when Trivy cannot pull it, its scan ends
  with a non-zero exit code and is reported like any other image Trivy cannot find.
- The protection rests on Trivy honoring `--`. It was measured on Trivy v0.76.1, the version the image bundles
  (pinned by tag and digest, so the build copies the measured binary): without `--`, `--config=x.y/z:latest` is read
  as the configuration file option; after `--`, `--config=…`, `--input=…` and `-a/b` targets are taken as image names
  and fail without opening the file they name, and a normal target takes the normal scan path. Any later Trivy update
  is measured again with the same cases.