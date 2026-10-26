# Plan: Trust certificates in /app/certs for the default non-root container user

Source: Issue #285
Status: Draft
Tier: security — the change replaces the way the container establishes the trust anchors of all outbound TLS (a cryptography control: which CAs the application and Trivy trust), for every runtime UID.
Security areas touched: Container image and runtime user (`src/DockerUpdateGuard/Dockerfile` comment, `src/DockerUpdateGuard/entrypoint.sh`); TLS and transport relaxations (which certificates outbound TLS trusts; `SkipCertificateValidation` and `AllowInsecureHttp` are not touched); Docker/build configuration (Dockerfile)

## Problem / root cause

`src/DockerUpdateGuard/entrypoint.sh:30` imports only when `id -u` is `0` or `/usr/local/share/ca-certificates` is
writable. `src/DockerUpdateGuard/Dockerfile:68-71` changes ownership and mode of `/app` only and `:73` switches to
`USER 64000:0`, so the trust store installed at `:51` stays root-owned and the `else` branch at `entrypoint.sh:53`
("No permission to write system trust store; skipping certificate import") is taken for every non-root UID. The
design itself (writing the system store) cannot work without root, so the fix replaces it rather than relaxing the
permission (record 0011).

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | Import is gated on root or a writable `/usr/local/share/ca-certificates` | `entrypoint.sh:29-30` | confirmed |
| 2 | The image runs as `USER 64000:0` and only `/app` is re-owned and group-mirrored | `Dockerfile:68-73` | confirmed |
| 3 | `ca-certificates` is installed as root in the runtime stage | `Dockerfile:51` | confirmed |
| 4 | The issue's line numbers for the entrypoint (`:104-129`, settings check `:85-94`) | The file has 61 lines; the import is `:23-58`, the settings check `:10-19` (the quoted code itself is accurate) | refuted |
| 5 | The Dockerfile comment at `:62-67` says the import "degrades gracefully when the trust store is not writable" | `Dockerfile:66-67` | confirmed |
| 6 | The entrypoint prints nothing when `/app/certs` does not exist (issue: "missing directory ... produce a message") | `entrypoint.sh:24` has no `else`; the fix adds message M1 (AC4) | confirmed |
| 7 | Trivy inherits the application's environment, so an exported `SSL_CERT_FILE` reaches it | `Infrastructure/ProcessRunner.cs:23-40` (`UseShellExecute = false`, variables only added to `startInfo.Environment`); `IProcessRunner.cs:15` "added to the inherited environment" | confirmed |
| 8 | .NET on Linux loads root certificates from the file named by `SSL_CERT_FILE` (and its default directory) when the process starts | to verify: Dev, step 6 — HttpClient GET to a local TLS server signed by a test CA succeeds with `SSL_CERT_FILE` set to the bundle and fails without it (AC1 check) | unverified |
| 9 | Go programs (Trivy 0.58.1) read their roots from `SSL_CERT_FILE` on Linux | to verify: Dev, step 6 — the AC5 check (bundled `trivy` against the test server, with and without the bundle); basis: Go `crypto/x509` documentation of `SystemCertPool` (SSL_CERT_FILE / SSL_CERT_DIR override the defaults on Unix) | unverified |
| 10 | The Alpine runtime image has `/etc/ssl/certs/ca-certificates.crt`, BusyBox `sh`, `awk` and `mktemp -d`, and no `openssl` CLI | to verify: Dev, step 6 — `ls`/`command -v` in the built image or an Alpine minirootfs of the base image's release | unverified |
| 11 | The *Analyzer gate* checks every line of a changed shell script: `[ … ]` fails `shelldre:S7688`, and `[[ … ]]` under `#!/bin/sh` fails shellcheck SC3010; `test …` passes shellcheck | `.squad/tools/analyzer_common.py:32,150-161` (whole file); shellcheck run on a scratch file: SC3010 for `[[`, no finding for `test` | confirmed |
| 12 | Whether SonarQube Cloud's `shelldre:S7688` also reports the `test` command | to verify: orchestrator, step 7 *Sonar gate* (or step 11) | unverified |
| 13 | No `docs/ARCHITECTURE.md` passage mentions the certificate import | `grep -n -i "cert" docs/ARCHITECTURE.md` hits only `:143-144` (client certificates) | confirmed |
| 14 | The operator documentation of `/app/certs` is `DOCKER.md` (`:97`, `:113-117`, `:169`), not `INSTALL.md`; it still recommends `--user 0` | `DOCKER.md:97-169`; `INSTALL.md:127-131` only links `DOCKER.md` | confirmed |
| 15 | No compose file under `docs/` mounts `/app/certs` | `grep -n certs docs/*.yml` — no hit | confirmed |

## Acceptance criteria

All criteria on the built image use a test CA generated for the check, a local HTTPS server whose certificate it
signed, and the default entrypoint.

- [ ] AC1: Default user (UID `64000`, GID `0`), `/app/certs` holding one `.crt` and one `.pem` CA file: the entrypoint
  prints the success message of *Approach* with count `2` and no skip or warning message, `SSL_CERT_FILE` in the
  application's environment names the bundle, and an HTTPS request from .NET `HttpClient` in the container to the
  test server succeeds (and fails with the same server when `/app/certs` is not mounted).
- [ ] AC2: As AC1 with `--user 12345:0`.
- [ ] AC3: As AC1 with `--user 0`; `/usr/local/share/ca-certificates` and `/etc/ssl/certs` are unchanged afterwards
  (no `update-ca-certificates` run).
- [ ] AC4: No `/app/certs`; an empty `/app/certs`; `/app/certs` holding only a `.txt`, a `.pfx`, a `.pem` with a
  private key and no certificate, an unreadable `.crt` and a `.crt` with a truncated certificate block: each case
  prints its message of *Approach*, the container starts (the application process runs), and `SSL_CERT_FILE` is not
  set, so the system CA bundle applies unchanged; in the mixed case (these files beside one valid `.crt`) only the
  valid file is counted.
- [ ] AC5: The bundled Trivy CLI, started by the application or by hand with the application's environment, trusts
  the mounted CA: against the test server its error is no longer `x509: certificate signed by unknown authority`
  (it is with `/app/certs` empty).
- [ ] AC6: The bundle holds the system bundle first, unchanged, followed by exactly the complete PEM certificate
  blocks of the accepted files; a `.pem` holding a certificate and a private key contributes the certificate only,
  and no `PRIVATE KEY` line reaches the bundle; the bundle's directory is mode `0700` and owned by the running UID.
- [ ] AC7: `SSL_CERT_FILE` pre-set to a readable file: the bundle starts with that file instead of the system bundle
  and the pre-set message is printed; pre-set to a missing file: the warning of *Approach* is printed and
  `SSL_CERT_FILE` keeps its pre-set value.
- [ ] AC8: An unwritable `/tmp` (`--read-only` without a tmpfs): the bundle warning is printed, the container starts,
  `SSL_CERT_FILE` is not set; with `--read-only --tmpfs /tmp` AC1 holds.
- [ ] AC9: The settings readability check is unchanged: a mounted, unreadable `appsettings.json` still exits with
  code `1` and the same message; no other path of the entrypoint exits non-zero before `exec`.
- [ ] AC10: The image still ends in `USER 64000:0`; the Dockerfile diff changes comments only (no new `chown`,
  `chmod`, package or permission).
- [ ] AC11: The documentation updates below are in the diff and match the built behavior.

## Tests per criterion

No criterion has a unit test: no C# production or test code changes (see *Verification without tests*).

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1–AC11 | none — verified as in *Verification without tests* | none |

## Verification without tests

The change touches no C# production or test code: steps 4 (*Skeleton*), 5 (*Tests first*) and the *Coverage gate*
are not applicable. `entrypoint.sh` is startup glue without a shell test harness (*Test files*). Owner of every
check below is the **Dev in step 6**, reporting each case with its output; the Reviewer checks the report in step 8.

- **Where Docker runs** (the sandbox has the Docker CLI but no daemon; the Dev tries to start one): *Image build
  check* (`docker build`), then AC1–AC10 against the built image. Test material in a scratch directory: a test CA
  and a server certificate made with `openssl`, the server `openssl s_server -www` on the host network. The
  HTTPS request of AC1–AC4 runs inside the container in .NET: the published app has no request command, so the Dev
  builds a scratch derived image `FROM` the built image (never committed) that adds a framework-dependent probe
  (`/probe/Probe.dll`, `net10.0`, `HttpClient` GET of its argument, exit `0` on success) and a wrapper `dotnet`
  earlier on `PATH` that prints `SSL_CERT_FILE` and runs the real `dotnet` with the probe; `/app/entrypoint.sh`
  runs unchanged in it, under the same `USER`. AC5: the bundled `trivy` in that container, with the environment the
  entrypoint exported, against the test server. AC9 and AC10 run against the built image itself (`docker inspect`
  for the user).
- **Where Docker does not run:** *Image build check* by the restore-order reproduction of `.squad/stack.md`
  (Dockerfile input changed); the entrypoint runs in a chroot of the Alpine minirootfs of the base image's Alpine
  release (real BusyBox `sh`, `awk`, `mktemp`; `apk add ca-certificates` if the bundle is missing) under
  `setpriv --reuid <uid> --regid 0 --clear-groups` for UIDs `64000`, `12345` and `0`, with a stub `dotnet` on `PATH`
  that prints `SSL_CERT_FILE` and copies the bundle out. AC1–AC4: the sandbox's .NET SDK runs the same probe with
  `SSL_CERT_FILE` set to that bundle (fact 8). AC5: a Trivy `0.58.1` release binary downloaded with its checksum, if
  the network allows. AC8: a read-only bind of `/tmp` in the chroot. AC10: read-only check of the Dockerfile diff.
  Any criterion that cannot run here is reported as **not run** with the reason; the orchestrator names it in the PR
  description as a manual check before merging, never as passed.
- **Every case:** `shellcheck`, `hadolint` and the shell rules through the *Analyzer gate* (step 7); `sh -n` on the
  script before the runs. AC11: the Reviewer, step 8.

## Approach

Rewrite the certificate block of `entrypoint.sh` (`:21-58`) and its header comment (`:3-8`); keep the settings check
`:10-19` behaviorally identical. Write every condition with `test …` (fact 11), no `[ … ]`, no `[[ … ]]`, no shell
functions, no `local`; quote every path. No `set -e`: nothing in the certificate block may end the script.

1. `certs_dir=/app/certs`. Not a directory → message M1, skip to `exec`.
2. Collect regular files `"$certs_dir"/*` (glob; `test -f` follows symlinks, as Kubernetes secret mounts need;
   dotfiles and subdirectories are ignored as today). None → M2, skip.
3. Base bundle: `SSL_CERT_FILE` when set and not empty (print M3), otherwise `/etc/ssl/certs/ca-certificates.crt`.
   Not a readable file → M4, skip (never build a bundle without the system CAs).
4. `rm -rf /tmp/dockerupdateguard-ca.*` (errors ignored; removes this container's bundles of earlier starts), then
   `bundle_dir=$(mktemp -d /tmp/dockerupdateguard-ca.XXXXXX)` (mode `0700`), bundle `"$bundle_dir/ca-certificates.crt"`.
   `mktemp` or the copy of the base fails → M5, skip.
5. For each file in glob order: not `*.crt`/`*.pem` → M6 (unchanged text); not readable → M7; otherwise extract with
   `awk` only **complete** blocks from a line starting `-----BEGIN CERTIFICATE-----` to a line starting
   `-----END CERTIFICATE-----`, emitting a block only at its END line and only when every line between holds base64
   characters (`A-Za-z0-9+/=`, a trailing carriage return allowed); a new BEGIN discards an open block, an open
   block at end of file is dropped. No block → M8; otherwise append the blocks to the bundle (append failure → M5,
   skip) and count the file.
6. Count `0` → remove `bundle_dir`, M9, skip. Otherwise `export SSL_CERT_FILE="$bundle"` and print M10.
7. `exec dotnet DockerUpdateGuard.dll "$@"` unchanged.

"Skip" means: leave `SSL_CERT_FILE` as it was and continue to `exec`. The system bundle is copied as-is first, so a
bad mounted block can only affect mounted certificates after it. `update-ca-certificates` and
`/usr/local/share/ca-certificates` are no longer used, for root too.

Messages, to standard output, exact text (`<…>` filled in; paths only, never file contents):

- M1 `No /app/certs directory; using the system CA certificates only`
- M2 `No certificate files found in /app/certs; using the system CA certificates only`
- M3 `SSL_CERT_FILE is set; building the CA bundle from <base> instead of the system CA certificates`
- M4 `Warning: CA bundle <base> is not readable; certificates in /app/certs are not used`
- M5 `Warning: cannot write a CA bundle under /tmp; certificates in /app/certs are not used`
- M6 `Skipping non-certificate file: <path>` (today's text)
- M7 `Warning: cannot read <path>; skipping it`
- M8 `Warning: <path> contains no complete PEM certificate; skipping it`
- M9 `No usable certificates in /app/certs; using the system CA certificates only`
- M10 `Added certificates from <n> file(s) in /app/certs to <bundle>; SSL_CERT_FILE=<bundle>`

M1/M2/M9 replace today's "Found certificate files ... attempting import into system trust store..." and
"No permission to write system trust store; skipping certificate import", which disappear. Order: M3 before the
per-file messages, M10 or M9 last.

Dockerfile: comment changes only — `:48` ("CA certificates ... e.g., for telemetry") may stay; `:60` and `:62-67`
say that the entrypoint adds mounted CA certificates through a bundle under `/tmp` and `SSL_CERT_FILE`, so no
write access to the system trust store is needed. No functional line changes.

## Options considered

Group-writable trust store with `update-ca-certificates` (issue option 1); bundle plus `SSL_CERT_FILE` (chosen,
issue option 2); `SSL_CERT_DIR` with a directory of mounted certificates; loading the certificates in C# per
`HttpClient`; keeping `update-ca-certificates` for root beside the bundle; and a bundle of the mounted certificates
alone, relying on .NET and Go also reading the default directory (rejected: depends on facts 8 and 9 for the
directory too, while a combined bundle needs only the file). Reasons in record 0011.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `src/DockerUpdateGuard/entrypoint.sh` | certificate block and header comment rewritten (*Approach*) |
| DockerUpdateGuard | `src/DockerUpdateGuard/Dockerfile` | comments `:60`, `:62-67` only |

## Signatures (for the Dev's skeleton)

none

## Test files

none — no C# changes.

Existing test code that calls a changed signature: none.

Files of the change no unit test can cover: `src/DockerUpdateGuard/entrypoint.sh` — no shell test runner in this
repository; it is outside `COVERAGE_PATHSPECS` (`src/*.cs`, `src/*.razor`) and not measured by SonarQube
coverage of C#, so no coverage exclusion is added. Recorded in 0011, *Consequences*.

## Areas

`docs/areas/hosting.md` (Hosting and configuration): *Container image* and *Limits and errors* change; the
*Known gaps* entry for #285 is removed.

## Documentation updates

Owner: the Dev, after the checks of *Verification without tests* pass, against the built behavior.

- `docs/areas/hosting.md`: *Container image* — entrypoint step 2 rewritten: `.crt`/`.pem` files in `/app/certs`
  are added, as their complete PEM certificate blocks, to a copy of the system CA bundle (or of the file a pre-set
  `SSL_CERT_FILE` names) under `/tmp`, and `SSL_CERT_FILE` points the application and Trivy at it, for any UID,
  root included; the system trust store is never written; when nothing is usable or `/tmp` is not writable, a
  message is printed, the system CAs alone apply and the container starts; a read-only root filesystem needs a
  writable `/tmp`. The bullet "CA certificates are installed ..." stays. *Limits and errors*: the row
  "Certificate import not possible" becomes rows for "no usable certificate in `/app/certs`" and "`/tmp` not
  writable" (message, system CAs only, container starts). *Known gaps*: remove the #285 entry. *Related
  decisions*: add 0011.
- `DOCKER.md`: mounts table `:97` — `/app/certs` holds CA certificates and client certificates; section
  *Automatic CA import on startup* `:113-117` rewritten for the bundle (works for the default user, no `--user 0`,
  PEM only, private keys never copied, pre-set `SSL_CERT_FILE`, read-only root filesystem needs a writable `/tmp`);
  *Notes* `:169` — drop the advice to run as root or bake the CA into a derived image for the import; keep the
  warning at `:123` that every `*.pem` in `/app/certs` is treated as a CA file (its certificate blocks are trusted).
- `INSTALL.md` *Beyond the quick start* (`:124-131`): one sentence that an internal CA is trusted by mounting its
  PEM file into `/app/certs` (read-only), also with the default user, linking `DOCKER.md`.
- `README.md` *Notes for Docker-based deployments* (`:293-299`): one bullet, the same statement.
- `docs/ARCHITECTURE.md` §11 *Security posture*, bullet "Container runs as a non-root user" (`:397-402`): one
  sentence that CA certificates mounted at `/app/certs` are trusted through a bundle under `/tmp` and `SSL_CERT_FILE`,
  so the import needs neither root nor write access to the system trust store, linking record 0011 and
  [Hosting and configuration](areas/hosting.md).
- `src/DockerUpdateGuard/entrypoint.sh` header comment and Dockerfile comments: part of the code change (*Approach*).

Sweep done: `grep -rn -i "app/certs\|trust store\|ca-certificates\|update-ca\|64000"` over `*.md`, `*.yml`,
`*.sh`, Dockerfiles outside `specs/` and `.squad/`; every hit not listed above is about client certificates or
the runtime user and stays true.

## Architecture check

Non-root UID `64000`, GID `0`, the `/app` ownership model and exit code `1` only for unreadable settings are kept
(AC9, AC10); the non-root guarantee gets stronger, as no root is needed for custom CAs. `.squad/project.md`
guarantees are not touched. `docs/ARCHITECTURE.md` gains one sentence (*Documentation updates*), owner Dev.

## Security considerations

- [ ] No limits: `/app/certs` is operator-controlled (a mount); its size is not bounded, and the bundle under `/tmp`
  is about the size of the system bundle plus the mounted certificates.
- [ ] Exceptions reaching a user-visible message: none (no C#). The entrypoint messages carry paths and a count,
  never file contents; `awk`/`cp` error output is discarded or replaced by M5/M7.
- [ ] Trust: whoever can write `/app/certs` can add trust anchors — as today for root; the bundle directory is
  private (`0700`) to the running UID; no private-key line is copied (AC6); the system bundle is never replaced
  (step 3 skips when it is unreadable).

## Decision records

- `docs/decisions/0011-mounted-ca-certificates-use-a-combined-bundle.md` (Proposed), index row added.

## Out of scope / follow-ups

- DER-encoded certificate files: not accepted (M8), as before in practice; documented in `DOCKER.md`.
- `SSL_CERT_DIR` and other trust consumers that ignore `SSL_CERT_FILE`: none in the image.
- #171 (Trivy private registries) benefits from this fix; no change there.