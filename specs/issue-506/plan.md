# Plan: Update the bundled Trivy and the compose Trivy servers to 0.76.2

Source: Issue #506
Tier: standard — a dependency update (the Trivy CLI the image bundles and the Trivy server image of the compose files) that creates, changes or removes no security control: the `--` marker, `TRIVY_SERVER` and the two Trivy settings of the image stay, only the binary they rest on changes, and it is measured again.
Security areas touched: Docker/build configuration (`src/DockerUpdateGuard/Dockerfile`, `docs/docker-compose.trivy.yml`, `docs/docker-compose.full.yml`); dependency (Trivy CLI and Trivy server image); External process execution (the binary `Vulnerabilities/TrivyVulnerabilityProvider.cs` starts, whose handling of `--`, `TRIVY_SERVER` and the update check the protections rest on)

## Problem / root cause

No defect: the image pins Trivy v0.76.1 (`src/DockerUpdateGuard/Dockerfile:4-5`) and both compose Trivy servers run
`aquasec/trivy:0.76.1` (`docs/docker-compose.trivy.yml:28`, `docs/docker-compose.full.yml:30`), while v0.76.2 is the
newest release. *Updating the bundled Trivy* in `docs/CONTRIBUTING.md:157-185` prescribes the measurements (steps
1–4) and the edits (step 5).

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | The Dockerfile pins v0.76.1 by its index digest | `Dockerfile:4` comment `# Trivy v0.76.1 (digest of the multi-arch image index)`, `:5` `aquasec/trivy@sha256:790fd66d…6c06c` | confirmed |
| 2 | Both compose Trivy servers run 0.76.1 with `server --listen 0.0.0.0:4954` | `docker-compose.trivy.yml:28-31`, `docker-compose.full.yml:30-33` | confirmed |
| 3 | Outside the records, only `docs/areas/hosting.md:164` names the bundled version | `grep -rn "0\.76"` over the repository (specs excluded): Dockerfile, the two compose files, `hosting.md:164`, records 0005/0015/0016; README.md, DOCKER.md, INSTALL.md, `docs/ARCHITECTURE.md:558`, `vulnerabilities.md` name no version; the v0.58.1 mentions (`vulnerabilities.md:120`, 0005, CONTRIBUTING:185) are past measurements and stay | confirmed |
| 4 | Records 0005 (`:118`), 0015 (`:43-44`), 0016 (`:12`, `:45`) name v0.76.1 and are released | added in d822166, 1ea22b3, d2181d7; `git tag --contains` lists v1.3.8 and v1.3.9; `git ls-remote --tags origin` ends at v1.3.9 — so they are amended by a new record, never edited | confirmed |
| 5 | v0.76.2 exists; its index digest is `sha256:8f6bb947dbc528902e11f2f5be52f7b519f6a39d5d148f41166f2040499768fb` (issue) | Docker Hub tags API: `0.76.2` and `latest` carry that digest (0.76.2 published 2026-11-05); HEAD `mirror.gcr.io/v2/aquasec/trivy/manifests/0.76.2` and `ghcr.io/v2/aquasecurity/trivy/manifests/0.76.2`: `docker-content-digest` that digest, content type OCI image index; tag `v0.76.2` (3f6334d) in `github.com/aquasecurity/trivy`. Bytes not yet hashed: AC2 | confirmed |
| 6 | `registry-1.docker.io` refuses anonymous manifest requests from this session | HEAD for 0.76.2 and 0.76.1 answered HTTP 429 (2026-11-18); the v0.76.1 measurement (#376 log) also fell back to `mirror.gcr.io` | confirmed |
| 7 | No Docker daemon here; `docker` CLI, `curl`, `jq`, `tar`, `sha256sum`, `file`, `strace`, `unshare` exist; no `crane`/`skopeo`; host `x86_64` | `docker info`: no `/var/run/docker.sock`; `command -v` | confirmed |
| 8 | The release image is built for linux/amd64 only, and no PR workflow builds the image | `release.yml:79-82` build-push-action without `platforms:` on `ubuntu-latest`; `ci.yml` runs restore/build/test/Sonar and `decision-records`, no image build | confirmed |
| 9 | The provider's argument list is `image --format json --quiet --scanners vuln --pkg-types os,library --timeout <n>s -- <target>`, server in `TRIVY_SERVER` | `TrivyVulnerabilityProvider.cs:246-262`, `:30`; `vulnerabilities.md:98` | confirmed |
| 10 | The v0.76.2 binary at `/usr/local/bin/trivy` of the amd64 image is statically linked and reports `Version: 0.76.2`, and `trivy server --help` still lists `--listen` | to verify: AC3, Dev, step 6, before any edit | unverified |
| 11 | v0.76.2 reads a leading-`-` target as an option without `--` and as an image name after it, opening no file it names | to verify: AC4 cases 1–4, Dev, step 6 | unverified |
| 12 | v0.76.2 reads `TRIVY_SERVER`; in client mode it contacts the server only after fetching the image's manifest and config, so case 5 needs a reachable registry | to verify: AC4 case 5, Dev, step 6 | unverified |
| 13 | With both settings `true`, v0.76.2 contacts no destination outside Hosting's list and never `check.trivy.dev`; without them it requests `check.trivy.dev` | to verify: AC5, Dev, step 6 | unverified |

## Acceptance criteria

- AC1: The `TRIVY_IMAGE` default in `src/DockerUpdateGuard/Dockerfile` is
  `aquasec/trivy@sha256:8f6bb947dbc528902e11f2f5be52f7b519f6a39d5d148f41166f2040499768fb` (the digest AC2 measured),
  its comment reads `# Trivy v0.76.2 (digest of the multi-arch image index)`, and the `trivy` service of
  `docs/docker-compose.trivy.yml` and `docs/docker-compose.full.yml` runs `aquasec/trivy:0.76.2`; outside
  `docs/decisions/` and `specs/` no file names `0.76.1`.
- AC2: The index fetched by tag `0.76.2` hashes to that digest and is the one fetched by digest; the linux/amd64
  manifest, its config and every layer blob it names are fetched and each hashes to the digest and has the size of
  its descriptor (CONTRIBUTING step 1).
- AC3: `/usr/local/bin/trivy` taken from those layers prints `Version: 0.76.2` for `--version`, `file` reports it
  `statically linked`, and `trivy server --help` lists `--listen` (step 2).
- AC4: with `TRIVY_SERVER=http://127.0.0.1:9` and the provider's options, (1) without `--`, the target
  `--config=x.y/z:latest` is read as the configuration file option; after `--`, the targets (2)
  `--config=x.y/z:latest`, (3) `--input=<scratch>/x.tar:latest` and (4) `-a/b` each exit non-zero with no `open`,
  `openat` or `openat2` of the path they name; (5) a normal target fails with an error naming `127.0.0.1:9` (step 3).
- AC5: a scan with `TRIVY_SKIP_VERSION_CHECK=true` and `TRIVY_DISABLE_TELEMETRY=true` contacts only the destinations
  Hosting lists (the Trivy server, registries, Trivy's Java DB repository) and never `check.trivy.dev`; the control
  run without them contacts `check.trivy.dev` (step 4).
- AC6: `docs/areas/hosting.md` names v0.76.2 as the bundled version and links record 0051, which amends 0005, 0015 and
  0016 and carries the measured outcome of the four measurements; 0005, 0015 and 0016 are unchanged, and
  `decision-check.py` passes.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | check: Verification without tests | — |
| AC2 | check: Verification without tests | — |
| AC3 | check: Verification without tests | — |
| AC4 | check: Verification without tests | — |
| AC5 | check: Verification without tests | — |
| AC6 | CI: `decision-records` job and the Reviewer's reading (below) | — |

## Verification without tests

No production or test code changes: steps 4 (*Skeleton*), 5 (*Tests first*) and the *Coverage gate* are not
applicable. The four measurements run in step 6, by the Dev, before any edit, in a scratch directory under the
session's scratchpad (*Approach*); the Dev reports each command with its quoted output (`orchestration.md`, *A denied
measurement*: otherwise not run), and the Reviewer checks the criteria against that output in step 8. A case with
another outcome stops the Dev before any edit and goes to the Lead: no bump on an unmeasured or changed binary.

- AC1: check: Dev and Reviewer — `git grep -n "0\.76\.1" -- ':!docs/decisions' ':!specs'` prints nothing, and the
  digest in the Dockerfile equals the measured index digest. The Dev runs the *Image build check* reproduction
  (`.squad/stack.md`); it does not reach the Trivy stage, whose `FROM` and `COPY` the registry fetch and the extracted
  binary stand in for (no daemon, no PR image build: fact 8). Step 7 runs the *Analyzer gate* (hadolint).
- AC2: check: Dev runs *Approach* step 1; Reviewer reads the hashes against the descriptors.
- AC3: check: Dev runs *Approach* step 2; Reviewer reads the quoted output.
- AC4: check: Dev runs *Approach* step 3; Reviewer reads exit codes, error lines and the `open*` lines per case.
- AC5: check: Dev runs *Approach* step 4; Reviewer reads the host list of both runs.
- AC6: CI: `decision-records` (and `decision-check.py` in step 7); the Reviewer reads hosting.md and 0051 against the
  Dev's output.

## Approach

**Measurements without a daemon.** Registry access with `curl` replaces `docker buildx imagetools inspect`, `docker
pull`, `docker create` and `docker cp`; it performs the same fetches and digest checks. Each download goes into its
own new directory; scripts stay outside it. Trivy runs with `HOME` and `TRIVY_CACHE_DIR` in the scratch directory and
the session's proxy and CA variables kept.

1. **Digest and pull (AC2).** Registry `registry-1.docker.io` (token from `auth.docker.io`, anonymous pull); on 429
   (fact 6) `mirror.gcr.io`, Google's pull-through mirror of Docker Hub, path `v2/aquasec/trivy/…` — a digest names
   the bytes, so a blob that hashes to it is the blob Docker Hub serves. Fetch `manifests/0.76.2` and
   `manifests/sha256:8f6bb…` with `Accept: application/vnd.oci.image.index.v1+json,
   application/vnd.docker.distribution.manifest.list.v2+json`; `sha256sum` of both bodies and the
   `docker-content-digest` header equal the digest. Take the `linux`/`amd64` entry (fact 8), fetch it, its config and
   each layer from `blobs/<digest>` (`curl -L`), and compare `sha256sum` and byte count with each descriptor.
2. **Binary (AC3).** List each layer (`tar -tzf`; `tar --zstd` for a zstd media type) in manifest order; take
   `usr/local/bin/trivy` from the last layer holding it, provided no later layer holds `usr/local/bin/.wh.trivy` or
   `usr/local/bin/.wh..wh..opq`. Run `--version`, `file`, `sha256sum`, and `server --help | grep -- --listen`.
3. **Option cases (AC4).** `env TRIVY_SERVER=http://127.0.0.1:9 strace -f -e trace=open,openat,openat2 -o <case>.txt
   <trivy> image --format json --quiet --scanners vuln --pkg-types os,library --timeout 30s [--] <target>`, one run
   per case; report the exit code, the last error line and every `open*` line that names the path. Case 1 shows the
   observation sees an option read (an open of `x.y/z:latest` or an error naming that configuration file). Case 5
   targets `library/alpine:3.20`; when Docker Hub refuses it with 429 before the server is reached, run it again with
   `mirror.gcr.io/library/alpine:3.20` (a registry-qualified target, as the provider builds for other registries). Go
   never sends a loopback address through the proxy, so the server connection is direct.
4. **Destinations (AC5).** Target as in case 5, `strace -f -e trace=connect,sendto,sendmsg,write -s 1024`, once with
   both settings `true` and once with both unset (`env -u`). Here every outbound HTTPS connection goes to the
   session's proxy, so each destination shows as a `CONNECT <host>:<port>` written to the proxy socket, a direct DNS
   lookup as a connection to port 53 carrying the name. Report every distinct host per run. A control run that does
   not show `check.trivy.dev` proves nothing: the measurement fails, and the Dev reports it. The control run sends the
   session host's identifier to `check.trivy.dev`; the procedure accepts that, and a refusal by the egress proxy still
   shows the `CONNECT`.

**Edits (step 5)**, after the measurements: the Dockerfile lines 4–5 and the two compose `image:` lines as AC1 states;
the documentation below.

## Options considered

1. **Move CLI and both servers to v0.76.2 after the measurements** — chosen (record 0051).
2. **Stay on v0.76.1** — lost: the issue asks for the update, v0.76.2 is the current release, and a server pulled as
   `latest` would be newer than the client.

The measurement method (registry fetch instead of `docker pull`) is no option of the change: no daemon runs here, and
both methods check the same digests.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard (host) | `src/DockerUpdateGuard/Dockerfile` | `TRIVY_IMAGE` default and its comment |
| — | `docs/docker-compose.trivy.yml`, `docs/docker-compose.full.yml` | Trivy server image tag |

## Signatures (for the Dev's skeleton)

None: no production or test code changes.

## Test files

None. Existing test code that calls a changed signature: none. Files no unit test can cover: none (no code).

## Areas

Hosting and configuration: the bundled version under *Container image* and a *Related decisions* entry for 0051.
Vulnerabilities: a *Related decisions* entry for 0051 (it amends 0015). No behavior changes.

## Documentation updates

All by the Dev, once the measurements passed:

- `docs/areas/hosting.md:164`: `v0.76.1` → `v0.76.2`; *Related decisions*: after 0016, an entry for 0051 (the bundled
  Trivy and the compose Trivy servers move to v0.76.2, measured again).
- `docs/areas/vulnerabilities.md`, *Related decisions*: after 0016, the same entry for 0051.
- None in README.md, DOCKER.md, INSTALL.md, `docs/ARCHITECTURE.md`, `docs/CONTRIBUTING.md` (fact 3; the procedure is
  unchanged); no changelog exists. The PR description's *Test Plan* carries the measurement table (CONTRIBUTING:163),
  from the Dev's report.

## Architecture check

No guarantee of `.squad/project.md` or `docs/ARCHITECTURE.md` changes: client mode against a separate server, the
argument list, the server address in the environment and the image's two settings stay. AC4 and AC5 show they still
hold for the new binary; a failed case stops the change.

## Security considerations

- [x] No limits: no parsed input changes.
- [x] None: no exception path changes.

## Decision records

- `docs/decisions/0051-the-bundled-trivy-and-the-compose-trivy-servers-move-to-v0-76-2.md` (Proposed, index row
  added) amends the released 0005, 0015 and 0016, which stay unedited. After the Dev's step-6 report the orchestrator
  launches the Lead once to add the measured outcome of AC2–AC5 to its *Consequences*, from the quoted output, and to
  set facts 10–13 here.

## Out of scope / follow-ups

- Pinning the compose servers by digest: CONTRIBUTING step 5 prescribes the tag.