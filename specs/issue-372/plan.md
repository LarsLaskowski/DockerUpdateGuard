# Plan: Bump bundled Trivy from 0.58.1 to the measured v0.76.1

Source: Issue #372
Status: Draft
Tier: standard — the bump replaces the bundled Trivy CLI whose argument parsing, `TRIVY_SERVER` handling and error text the provider relies on, and switches off the new binary's update check and telemetry in the image environment; no security control is created, changed or removed (the `--` guard of 0015 stays as built; the two variables only keep a dependency from adding an outbound call, they decide nothing an attacker may reach).
Security areas touched: Container image and runtime user (`src/DockerUpdateGuard/Dockerfile`, the Trivy CLI stage and the runtime environment); External process execution (the binary `TrivyVulnerabilityProvider` starts and the guard of record 0015 rests on); Docker/build configuration; a dependency (the bundled Trivy CLI, and the Trivy server image of the documented deployment tiers)

## Problem / root cause

`src/DockerUpdateGuard/Dockerfile:4` defaults `TRIVY_IMAGE` to `aquasec/trivy:0.58.1`; line 55 copies
`/usr/local/bin/trivy` from it. Record 0015 (Consequences, last item) says the `--` protection was measured on
v0.76.1 only, so the image ships a binary nobody measured. `docs/docker-compose.trivy.yml:28` and
`docs/docker-compose.full.yml:30` run the Trivy server at 0.58.1 as well, so a client bump alone would pair a
v0.76.1 client with a v0.58.1 server in the documented deployment tiers. Unlike v0.58.1, v0.76.1 sends an update
check with a stable host identifier to `https://check.trivy.dev/updates` on every `trivy image` run unless both the
version check and telemetry are off (Security, plan review; fact 12), so the bump alone would add an outbound
destination to every deployment.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | ------ | ------ |
| 1 | The Dockerfile pins `aquasec/trivy:0.58.1` on line 4 | `src/DockerUpdateGuard/Dockerfile:4`, `ARG TRIVY_IMAGE="aquasec/trivy:0.58.1"` | confirmed |
| 2 | v0.76.1 is the current release | Docker Hub API `GET /v2/repositories/aquasec/trivy/tags?ordering=last_updated`: `0.76.1` (2026-10-21) and `latest` share digest `sha256:790fd66d1697395d8693d1acaa1ea702406a20f109b6fc7bac8db623eca6c06c`; only `canary` is newer | confirmed |
| 3 | That digest is the OCI image index of `0.76.1`, with `linux/amd64` manifest `sha256:2e6c00d711ab4ae41d8a30c2c1fd3bd3b62704c177bb120f95012e939edfb3df` and `linux/arm64` | Docker Hub API `GET /v2/repositories/aquasec/trivy/tags/0.76.1`: `media_type` `application/vnd.oci.image.index.v1+json`, `images[]` | confirmed |
| 4 | The pinned image can be pulled | to verify: registry pull of `aquasec/trivy:0.76.1@sha256:790fd66d…` (manifest, `linux/amd64` manifest, layers), Dev, step 6. Not settled here: no Docker daemon in the session, and `registry-1.docker.io` answered HTTP 429 to an anonymous manifest `HEAD` | unverified |
| 5 | The image holds the binary at `/usr/local/bin/trivy` (the path line 55 copies), reports version 0.76.1 and is statically linked (it runs on the Alpine runtime stage) | to verify: layer listing, `trivy --version`, `file`/`readelf -l` (no `INTERP`), Dev, step 6 | unverified |
| 6 | The #367 cases behave on the bundled binary as record 0015 states for v0.76.1 | to verify: the cases of AC3, Dev, step 6; 0015 measured v0.76.1 from another source (the release archive), not from the image | unverified |
| 7 | v0.76.1 accepts the provider's fixed options (`image --format json --quiet --scanners vuln --pkg-types os,library --timeout <n>s`) and reads `TRIVY_SERVER` (no own database download) | to verify: AC3 cases, with the list `BuildArguments` builds (`TrivyVulnerabilityProvider.cs:246-263`) (no `unknown flag`, no database download in `TRIVY_CACHE_DIR`), Dev, step 6 | unverified |
| 8 | v0.76.1's error output for a missing tag still matches the provider's *unavailable* detection (`MANIFEST_UNKNOWN`, `manifest unknown`, or `manifest` and `not found`) | to verify: AC3 case f against the detection in `docs/areas/vulnerabilities.md#trivy`, Dev, step 6 | unverified |
| 9 | `trivy server --listen` and `--cache-dir` (bound to `TRIVY_CACHE_DIR`, Dockerfile line 56) exist in v0.76.1 | to verify: `trivy server --help` and `trivy image --help` of the extracted binary, Dev, step 6 | unverified |
| 10 | Records 0015 and 0005 are unreleased, so they are edited in place | `git tag --contains` of the commits that added them (`1ea22b3` for 0015, `d822166` for 0005) prints nothing; newest tag `v1.3.7` | confirmed |
| 11 | The release build passes no `TRIVY_IMAGE` build argument, so the Dockerfile default is what ships | `.github/workflows/release.yml:85-88` (`APP_VERSION`, `BASE_RUNTIME`, `BASE_RUNTIME_DIGEST` only) | confirmed |
| 12 | v0.76.1 sends `GET https://check.trivy.dev/updates` (headers `Trivy-Identifier`, `Trivy-Command`, `Trivy-Flags`, `Trivy-OS`, `Trivy-Arch`) in a background goroutine on every `trivy image` run; only `--skip-version-check` and `--disable-telemetry` together stop it; `--quiet` only hides the notice; v0.58.1 has no such call; the server is not affected | source of v0.76.1, read by Security in the plan review: `pkg/commands/artifact/run.go:143,170`, `pkg/notification/notice.go:39,54-90,57,64-67,110` | confirmed |
| 13 | v0.76.1 reads both switches from `TRIVY_SKIP_VERSION_CHECK` and `TRIVY_DISABLE_TELEMETRY` (Trivy binds each flag to `TRIVY_` plus its name) | to verify: AC6 control and final run, Dev, step 6 | unverified |
| 14 | A Trivy the provider starts inherits the application's environment, so an image `ENV` reaches it, a custom `TrivyExecutablePath` included | `ProcessRunner.cs:23-40`: `UseShellExecute = false`, only the provider's variables are set on `ProcessStartInfo.Environment`, which starts as the process environment; `docs/areas/vulnerabilities.md` line 98 | confirmed |

## Acceptance criteria

- [ ] AC1: `src/DockerUpdateGuard/Dockerfile` line 4 reads exactly
  `ARG TRIVY_IMAGE="aquasec/trivy:0.76.1@sha256:790fd66d1697395d8693d1acaa1ea702406a20f109b6fc7bac8db623eca6c06c"`,
  no Dockerfile line changes besides it and the lines of *Approach* 2, and that reference can be pulled: the registry serves the index at that digest
  and its `linux/amd64` manifest and layers.
- [ ] AC2: the `linux/amd64` image of AC1 contains `usr/local/bin/trivy`; the extracted binary prints version
  `0.76.1` and is statically linked.
- [ ] AC3: the extracted binary, run with the provider's fixed options, `TRIVY_SERVER=http://127.0.0.1:9`
  (closed port), `TRIVY_SKIP_VERSION_CHECK=true`, `TRIVY_DISABLE_TELEMETRY=true` (the image's environment),
  `TRIVY_CACHE_DIR` set to an empty scratch directory and a scratch working directory:
  - a. without `--`, target `--config=x.y/z:latest`: Trivy reads it as its configuration file option (error output
    about loading the configuration file), non-zero exit;
  - b–d. with `--` before `--config=x.y/z:latest`, `--input=/tmp/x.tar:latest` and `-a/b:latest`: each is treated as an
    image name (error output about the image or its reference, not about a configuration file or archive), non-zero
    exit, and the file the option would name (`x.y/z:latest` in the working directory, `/tmp/x.tar:latest`) is never
    opened;
  - e. `-- library/alpine:3.20`: the normal scan path — no option error, no vulnerability database download (no
    `db/` under `TRIVY_CACHE_DIR`, no download message), non-zero exit at the unreachable server, or at the image
    pull when the registry refuses the session;
  - f. `-- library/alpine:0.0.0-does-not-exist`: error output that the provider's *unavailable* detection matches.
  No run prints `unknown flag`.
- [ ] AC4: `docs/docker-compose.trivy.yml` and `docs/docker-compose.full.yml` run `aquasec/trivy:0.76.1`, and
  `trivy server --help` of the extracted binary lists `--listen`; `trivy image --help` lists `--cache-dir`.
- [ ] AC5: no `0.58.1` remains outside `docs/decisions/` and the measured history in
  `docs/areas/vulnerabilities.md` (line 111) — `git grep -n "0\.58\.1" -- ':!docs/decisions' ':!specs'` prints only that line;
  records 0015 and 0005 name the bundled v0.76.1 as measured (wording under *Decision records*).
- [ ] AC6: the Dockerfile's runtime stage sets `TRIVY_SKIP_VERSION_CHECK=true` and `TRIVY_DISABLE_TELEMETRY=true`
  (*Approach* 2), and with them the binary makes no request to `check.trivy.dev`: AC3 case e, run as AC3 says but
  with the proxy variables unset and `GODEBUG=netdns=go+2` under `strace -f -e trace=connect`, looks up no
  `check.trivy.dev` (no `hostLookupOrder(check.trivy.dev)` line in the error output) and connects only to
  `127.0.0.1:9`, the DNS resolver and the registry; the same run without the two variables (control) does look up
  `check.trivy.dev`.

## Tests per criterion

None: no production or test code changes; every criterion is verified under *Verification without tests*.

## Verification without tests

Steps 4 (*Skeleton*), 5 (*Tests first*) and the *Coverage gate* are not applicable (*Changes without production
or test code*). What can be checked in this session: the Dockerfile and compose edits, hadolint, the *Image build
check*'s restore-order reproduction, and the binary measurements, provided the registry serves the image over HTTPS
(no Docker daemon is running, so `docker pull`/`docker build` are not possible). Not checkable here: a real
`docker build` and a scan against a live Trivy server; the first runs in CI's release build.

| AC | Where, by whom |
| -- | -------------- |
| AC1 | Dev, step 6: fetch the index by digest from `registry-1.docker.io` (anonymous token from `auth.docker.io`, scope `repository:aquasec/trivy:pull`), check its `Docker-Content-Digest` equals the pinned digest and that it lists the `linux/amd64` manifest of fact 3, then fetch that manifest and its layers. On HTTP 429 retry later or use `mirror.gcr.io/aquasec/trivy` with the same digest (content-addressed, so the bytes are the same). Downloads go into their own new, empty scratch directory outside the repository. Then the *Analyzer gate* with `--lint-only` (hadolint on the whole Dockerfile; the `# hadolint ignore=DL3018` line stays) and the *Image build check* (restore-order reproduction, since Docker does not run). If neither registry serves the image, the Dev stops and reports it; the orchestrator launches the Lead (`decide`) — the pin is not merged unmeasured. |
| AC2 | Dev, step 6: list the layers, extract `usr/local/bin/trivy`, run `trivy --version`, `file` and `readelf -l` on it. |
| AC3 | Dev, step 6: each case run under `strace -f -e trace=openat,open,stat,newfstatat` (installed in the session) with the exact argument list of `BuildArguments` (`TrivyVulnerabilityProvider.cs:246-263`) and `--timeout 30s`; report per case the command, the exit code, the last error line and whether the trace names `x.y/z:latest` or `/tmp/x.tar:latest`. Case a is the control: its trace must show the configuration file being looked up, so an empty trace in b–d is meaningful. A case whose outcome differs from AC3 stops the change and goes to the Lead (`decide`); case f alone may be reported "not measured" when the registry refuses the session, which the Lead then decides. |
| AC4 | Dev, step 6: the edits, and the two `--help` runs of AC2's binary. |
| AC5 | Dev, step 6: the `git grep`; Lead, record edits after the Dev's measurement report (below). Reviewer, step 8: reads both. |
| AC6 | Dev, step 6: the Dockerfile lines, then the control run first and the final run, each reported with the command, the `hostLookupOrder` lines and the `connect` addresses. Only the control may send the request; the Dev records whether it could leave the session (direct egress or not). The control makes the empty final result meaningful: a control without the lookup is repeated up to three times (the request runs in a background goroutine and may lose the race against the exit), then the change stops and goes to the Lead (`decide`), as does a final run with the lookup. |

## Approach

1. Dockerfile line 4 as AC1: tag and digest, so the build gets exactly the measured binary, even if the tag is moved.
2. Dockerfile, right after `ENV TRIVY_CACHE_DIR=/tmp/trivy` (line 58), these four lines (AC6, record 0016):

   ```dockerfile
   # Trivy's update check and telemetry would send a host identifier to check.trivy.dev on every scan;
   # both are off so Trivy contacts only the Trivy server and the registries.
   ENV TRIVY_SKIP_VERSION_CHECK=true
   ENV TRIVY_DISABLE_TELEMETRY=true
   ```
3. Both compose files: `image: aquasec/trivy:0.76.1` (tag only, like the other images there; operators maintain it).
   The server makes no update call (fact 12), so they get no variables.
4. The Dev measures AC1–AC4 and AC6 and reports the raw results; then the orchestrator launches the Lead (`revise`) to apply
   the record wording below, adjusted only where a measurement differs within what AC3 allows (case e's stopping
   point, case f "not measured").

No new user-visible output.

## Options considered

1. **Tag and digest in the Dockerfile** — chosen: the measurement binds to the bytes the build copies; a tag is
   mutable.
2. **Tag only** (`aquasec/trivy:0.76.1`) — lost: a re-pushed tag would ship a binary nobody measured, which is the
   gap this issue closes. Updating needs a digest edit as well; Dependabot covers no Docker ecosystem here
   (`.github/dependabot.yml`: NuGet and actions only), so either form is updated by hand.
3. **Keep 0.58.1 and measure it from its image** — lost: the issue asks for a current release, and the old binary
   stays a year behind on fixes.
4. **Bump the client only, leave the compose servers at 0.58.1** — lost: a v0.76.1 client against a v0.58.1 server
   was not measured, and this plan does not measure it; the documented tiers keep client and server at one version.
5. **Update check and telemetry: image environment** (chosen), **flags in `BuildArguments`** or **left to the
   operator** — weighed in record 0016: the flags change production code, tests and 0015's argument list and break
   an older custom Trivy with `unknown flag`; leaving it would send a host fingerprint from every default deployment.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| `src/DockerUpdateGuard` | `Dockerfile` | line 4, `TRIVY_IMAGE` default (AC1); two `ENV` lines after line 58 (AC6) |
| docs | `docs/docker-compose.trivy.yml`, `docs/docker-compose.full.yml` | Trivy server image (AC4) |

## Signatures (for the Dev's skeleton)

none

## Test files

None. Existing test call sites affected: none. Files no unit test can cover: none (no code changes; the Dockerfile
and compose files are not measured for coverage).

## Areas

- `docs/areas/hosting.md`: the bundled Trivy version and its pinning.
- `docs/areas/hosting.md`: the image switches off Trivy's update check and telemetry.
- `docs/areas/vulnerabilities.md`: Trivy contacts only the Trivy server and the registries (pointer to Hosting); the
  argument list and error handling are unchanged; line 111's "measured with Trivy v0.58.1" is the measured reason
  for a rule that holds for every version and stays.

## Documentation updates

Sweep: `git grep -n "0\.58\|aquasec"` over the repository; `docs/ARCHITECTURE.md` (line 476), `README.md`,
`INSTALL.md`, `.squad/project.md` and `.squad/stack.md` name no Trivy version and stay.

- `docs/areas/hosting.md` line 138 (Dev): the bundled CLI is taken from `aquasec/trivy:0.76.1`, pinned by digest, by
  default; the deployment tiers run the Trivy server of the same version. Same bullet or the next: the image sets
  `TRIVY_SKIP_VERSION_CHECK=true` and `TRIVY_DISABLE_TELEMETRY=true`, so Trivy sends no update check or usage data
  to `check.trivy.dev` and contacts only the Trivy server and the registries; setting either to `false` brings the
  request back, and a Trivy run outside the image needs both set by the operator. Link record 0016.
- `docs/areas/vulnerabilities.md#trivy`, after the `TRIVY_SERVER` bullet (lines 97-98) (Dev): one sentence that the
  Trivy process contacts only the Trivy server and the registries, the update check and telemetry being switched off
  by the image ([Hosting](hosting.md#container-image)); add 0016 to the area's record list (line 195 onward).
- `docs/docker-compose.trivy.yml:28`, `docs/docker-compose.full.yml:30` (Dev): as *Approach* 2.

## Architecture check

No guarantee of `.squad/project.md` is touched; the composition, the process boundary and the client-mode design of
`docs/ARCHITECTURE.md` (lines 230, 476) stay. No `docs/ARCHITECTURE.md` update.

## Security considerations

- [x] No limits: no parsed input changes.
- [x] None: no exception path changes.
- The guard of record 0015 depends on the binary, so it is measured on exactly the bytes the build copies (AC1–AC3),
  and the change fails closed: an unmeasurable or deviating binary stops it before review.
- v0.76.1's update check (fact 12) is switched off in the image (AC6, record 0016); a control run proves the
  measurement can see the request, and a missing control or a lookup in the final run stops the change.

## Decision records

New record `docs/decisions/0016-the-bundled-trivy-skips-its-update-check-and-telemetry.md` (Proposed, Area
Hosting and configuration, indexed): why the update check is suppressed in the image environment. The bump itself needs none. Two
unreleased records are edited in place by the Lead after the Dev's report:

- `docs/decisions/0015-the-trivy-scan-target-follows-an-end-of-options-marker.md`, *Consequences*, last item:
  "The protection rests on Trivy honoring `--`. It was measured on Trivy v0.76.1, the version the image bundles
  (pinned by tag and digest, so the build copies the measured binary): without `--`, `--config=x.y/z:latest` is read
  as the configuration file option; after `--`, `--config=…`, `--input=…` and `-a/b` targets are taken as image names
  and fail without opening the file they name, and a normal target takes the normal scan path. Any later Trivy update
  is measured again with the same cases."
- `docs/decisions/0005-urls-are-output-with-user-info-masked.md` line 118: "the bundled Trivy v0.58.1 reads it" →
  "the bundled Trivy v0.76.1 reads it" (AC3 case e). Line 24's and 32's v0.58.1 measurements stay: they are the
  reason the rules exist.

## Out of scope / follow-ups

- Re-measuring the credential repetition in Trivy's error output (record 0005, area line 111) on v0.76.1: the output
  is withheld for every version, so the result changes no behavior.
- Pinning the other base images by digest.

## Security review

- B1 (update check to `check.trivy.dev`): option a taken — `ENV TRIVY_SKIP_VERSION_CHECK=true` and
  `ENV TRIVY_DISABLE_TELEMETRY=true` in the runtime stage (*Approach* 2), pinned by AC6 with a control run; why in
  record 0016 (option b breaks an older custom Trivy and changes code and 0015; the environment reaches every Trivy
  the application starts in the image, fact 14). Documented in `docs/areas/hosting.md` and
  `docs/areas/vulnerabilities.md#trivy`. Tier stays `standard`: the variables are a dependency setting, not a guard.
- N1 (the measurements call home): every AC3 run carries both variables; only AC6's control sends the request, on
  purpose, and the Dev records whether it could leave the session.
- N2: nothing to change.

## Security notes

- NB1 (Security, non-blocking): AC6 says the final run "connects only to `127.0.0.1:9`, the DNS resolver and the registry". In the Security measurement strace also showed `connect` calls on port 53 to the resolved registry and CDN addresses (Go UDP connects for source-address selection, no traffic; the control shows the same set). The deciding signal is the `hostLookupOrder(check.trivy.dev)` line; the final run's connect set must equal the control's minus whatever `check.trivy.dev` resolved to.
- NB2 (Security, non-blocking): the sentence "only the Trivy server and the registries" is broad; when an image contains JARs the client still pulls Trivy's Java DB from its OCI repository. Where the Dev writes the area text, use wording such as "the registries, including Trivy's Java DB repository when an image contains Java archives".
