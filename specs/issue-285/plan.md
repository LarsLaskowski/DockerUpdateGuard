# Plan: Trust certificates in /app/certs for the default non-root container user

Source: Issue #285
Status: Revised (1)
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
| 7 | Trivy inherits the application's environment, so an exported `SSL_CERT_DIR` reaches it | `Infrastructure/ProcessRunner.cs:23-40` (`UseShellExecute = false`, variables only added to `startInfo.Environment`); `IProcessRunner.cs:15` "added to the inherited environment" | confirmed |
| 8 | .NET 10 stops reading an `SSL_CERT_FILE` bundle at the first undecodable block (base64-valid non-DER, or truncated with an END line), so later CAs in it are untrusted; Go keeps them | Security's measurement, plan review round 1: `X509Chain` probe and Go `x509.Verify` probe over `bundle_bad.crt` / `bundle_trunc.crt` (leaf of the later CA: `PartialChain` in .NET, trusted in Go) | confirmed |
| 9 | .NET 10 and Go read every file of every directory in a colon-separated `SSL_CERT_DIR`, any file name, each file on its own (a bad file next to a good one costs only itself) | Security's measurement, plan review round 1: directories `dirB` (`a-bad.crt` before `z-caB.crt`) and `dirC`, both probes trust the later CA (glibc sandbox; the Alpine image is fact 10) | confirmed |
| 10 | In the Alpine runtime image: `/etc/ssl/certs` is the default certificate directory of OpenSSL and Go, the system CAs stay trusted with `SSL_CERT_DIR=/etc/ssl/certs:<dir>`, and BusyBox `sh`, `awk` (output redirection to files, `close`) and `mktemp -d` exist; no `openssl` CLI | to verify: Dev, step 6 — in the built image or an Alpine minirootfs of the base image's release: `command -v`, the AC1 system-CA probe and the AC1/AC5 runs | unverified |
| 11 | The *Analyzer gate* checks every line of a changed shell script: `[ … ]` fails `shelldre:S7688`, and `[[ … ]]` under `#!/bin/sh` fails shellcheck SC3010; `test …` passes shellcheck | `.squad/tools/analyzer_common.py:32,150-161` (whole file); shellcheck run on a scratch file: SC3010 for `[[`, no finding for `test` | confirmed |
| 12 | Whether SonarQube Cloud's `shelldre:S7688` also reports the `test` command | to verify: orchestrator, step 7 *Sonar gate* (or step 11) | unverified |
| 13 | No `docs/ARCHITECTURE.md` passage mentions the certificate import | `grep -n -i "cert" docs/ARCHITECTURE.md` hits only `:143-144` (client certificates) | confirmed |
| 14 | The operator documentation of `/app/certs` is `DOCKER.md` (`:97`, `:113-117`, `:169`), not `INSTALL.md`; it still recommends `--user 0` | `DOCKER.md:97-169`; `INSTALL.md:127-131` only links `DOCKER.md` | confirmed |
| 15 | No compose file under `docs/` mounts `/app/certs` | `grep -n certs docs/*.yml` — no hit | confirmed |

## Acceptance criteria

All criteria on the built image use test CAs generated for the check, a local HTTPS server whose certificate one of
them signed, and the default entrypoint. "Trusted by .NET" means the in-container .NET probe of *Verification without
tests* succeeds.

- [ ] AC1: Default user (UID `64000`, GID `0`), `/app/certs` holding one `.crt` and one `.pem` CA file: the entrypoint
  prints M9 with `2` certificates from `2` files and no other certificate message, `SSL_CERT_DIR` in the
  application's environment is `/etc/ssl/certs:<private dir>`, an HTTPS request from .NET `HttpClient` to the test
  server succeeds (and fails when `/app/certs` is not mounted), and a CA taken from the system bundle is still
  trusted by .NET.
- [ ] AC2: As AC1 with `--user 12345:0`.
- [ ] AC3: As AC1 with `--user 0`; `/usr/local/share/ca-certificates` and `/etc/ssl/certs` are unchanged afterwards
  (no `update-ca-certificates` run).
- [ ] AC4: No `/app/certs`; an empty `/app/certs`; `/app/certs` holding only a `.txt`, a `.pfx`, a `.pem` with a
  private key and no certificate, an unreadable `.crt`, a `.crt` with a block missing its END line, a `.crt` whose
  block has no body line and a `.crt` with indented marker lines: each case prints its messages of *Approach* (the
  `.txt` and `.pfx` M5, the unreadable file M6, the others M7, then M8), the container starts (the application
  process runs), and `SSL_CERT_DIR` is not set; in the mixed case (these files beside one valid `.crt`) only the
  valid file is counted in M9.
- [ ] AC5: Trivy trusts the mounted CA: the bundled `trivy`, with the environment the entrypoint exported, runs
  `trivy image --image-src remote <test server host:port>/probe:latest` so that the test server is the first TLS
  endpoint it contacts; with the CA mounted its error is not `x509: certificate signed by unknown authority`, with
  `/app/certs` empty it is.
- [ ] AC6: The private directory is mode `0700`, owned by the running UID, and holds one file per accepted block,
  each exactly the line `-----BEGIN CERTIFICATE-----`, the block's body lines without carriage return or trailing
  whitespace, and the line `-----END CERTIFICATE-----`. Cases: a `.pem` with a certificate and a private key gives
  the certificate only; a line `-----BEGIN CERTIFICATE----------BEGIN PRIVATE KEY-----` is no marker (that block is
  not accepted); a CRLF file and a file starting with a UTF-8 byte order mark are accepted; a `.crt` with two
  certificates gives two files. No file in the directory contains `PRIVATE KEY`.
- [ ] AC7: `SSL_CERT_DIR` pre-set: M3 is printed and the exported value is the pre-set value followed by
  `:<private dir>`. `SSL_CERT_FILE` pre-set (also to a file without a final newline): it is never read, copied or
  changed, and a CA present only in it stays trusted by .NET next to the mounted CA.
- [ ] AC8: An unwritable `/tmp` (`--read-only` without a tmpfs): M4 is printed, the container starts, `SSL_CERT_DIR`
  is not set; with `--read-only --tmpfs /tmp` AC1 holds.
- [ ] AC9: The settings readability check keeps its behavior: a mounted, unreadable `appsettings.json` still exits with
  code `1` and the same messages; no other path of the entrypoint exits non-zero before `exec`.
- [ ] AC10: The image still ends in `USER 64000:0`; the Dockerfile diff changes comments only (no new `chown`,
  `chmod`, package or permission).
- [ ] AC11: The documentation updates below are in the diff and match the built behavior.
- [ ] AC12: `/app/certs` holding `a-bad.crt` (a complete block whose body is valid base64 but no certificate),
  `b-trunc.crt` (a certificate cut short in the middle of its body, with its END line) and `z-ca.crt` (the test CA):
  the container starts, M9 counts all three files, and the test CA is trusted by .NET (AC1's request succeeds) and
  by Trivy (AC5's check).

## Tests per criterion

No criterion has a unit test: no C# production or test code changes (see *Verification without tests*).

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1–AC12 | none — verified as in *Verification without tests* | none |

## Verification without tests

The change touches no C# production or test code: steps 4 (*Skeleton*), 5 (*Tests first*) and the *Coverage gate*
are not applicable. `entrypoint.sh` is startup glue without a shell test harness (*Test files*). Owner of every
check below is the **Dev in step 6**, reporting each case with its output; the Reviewer checks the report in step 8.
Security's probes and test material in the session scratchpad (`m/probe`, `m/gop`, `m/*.crt`) may be reused.

- **Where Docker runs** (the sandbox has the Docker CLI but no daemon; the Dev tries to start one): *Image build
  check* (`docker build`), then AC1–AC10 and AC12 against the built image. Test material in a scratch directory: test
  CAs and a server certificate made with `openssl`, the server `openssl s_server -www` on the host network. The
  .NET check runs inside the container: the Dev builds a scratch derived image `FROM` the built image (never
  committed) that adds a framework-dependent probe (`net10.0`, `HttpClient` GET of a URL and `X509Chain` build of a
  certificate file, exit `0` on success) and a wrapper `dotnet` earlier on `PATH` that prints `SSL_CERT_DIR` and
  `SSL_CERT_FILE` and runs the real `dotnet` with the probe; `/app/entrypoint.sh` runs unchanged in it, under the same
  `USER`. AC5/AC12: the bundled `trivy` in that container with the exported environment. AC9 and AC10 run against
  the built image itself (`docker inspect` for the user).
- **Where Docker does not run:** *Image build check* by the restore-order reproduction of `.squad/stack.md`
  (Dockerfile input changed); the entrypoint runs in a chroot of the Alpine minirootfs of the base image's Alpine
  release (real BusyBox `sh`, `awk`, `mktemp`; `apk add ca-certificates` if the bundle is missing) under
  `setpriv --reuid <uid> --regid 0 --clear-groups` for UIDs `64000`, `12345` and `0`, with a stub `dotnet` on `PATH`
  that prints the environment and copies the private directory out. AC1–AC4, AC7, AC12: the sandbox's .NET runs the
  probe with `SSL_CERT_DIR` set to `/etc/ssl/certs:<copied directory>`. AC5/AC12: a Trivy `0.58.1` release binary
  downloaded with its checksum, if the network allows. AC8: a read-only bind of `/tmp` in the chroot. AC10: read-only
  check of the Dockerfile diff. Any criterion that cannot run here is reported as **not run** with the reason; the
  orchestrator names it in the PR description as a manual check before merging, never as passed.
- **Every case:** `sh -n` before the runs; `shellcheck`, `hadolint` and the shell rules through the *Analyzer gate*
  (step 7). AC11: the Reviewer, step 8.

## Approach

Rewrite the certificate block of `entrypoint.sh` (`:21-58`) and its header comment (`:3-8`). The settings check
`:10-19` keeps its messages, exit code and conditions; only its line 12 is rewritten as
`if test -f "$config_file" && test ! -r "$config_file"; then`, so the whole file passes the *Analyzer gate*. Write
every condition with `test …` (fact 11), no `[ … ]`, no `[[ … ]]`, no shell functions, no `local`; quote every path.
No `set -e`: nothing in the certificate block may end the script.

1. `certs_dir=/app/certs`. Not a directory → M1, skip.
2. Collect regular files `"$certs_dir"/*` (glob; `test -f` follows symlinks, as Kubernetes secret mounts need;
   dotfiles and subdirectories are ignored as today). None → M2, skip.
3. `cert_dir=$(mktemp -d /tmp/dockerupdateguard-ca.XXXXXX)` (mode `0700`); failure → M4, skip. No cleanup of
   earlier directories (record 0011).
4. For each file in glob order, numbered `n`: not `*.crt`/`*.pem` → M5; not readable → M6; otherwise one `awk` run
   over the file writes each accepted block to `"$cert_dir/$n-<k>.pem"` (closing each file) and prints the number of
   blocks. Line rules, after removing a UTF-8 byte order mark from line 1 and a trailing carriage return and
   trailing spaces or tabs from every line:
   - a marker is a line **exactly** `-----BEGIN CERTIFICATE-----` or `-----END CERTIFICATE-----` (no prefix match,
     no leading whitespace);
   - after BEGIN, every line up to END must be non-empty and consist only of `A-Za-z0-9+/=`; any other line
     discards the open block; a new BEGIN restarts it;
   - at END, a block with at least one body line is written as the standard BEGIN line, its body lines, the
     standard END line; an END without open block and an open block at end of file are dropped.
   `0` blocks → M7. `awk` exits non-zero → remove `cert_dir`, M4, skip. Otherwise add the file and its blocks to the
   counts.
5. Block count `0` → remove `cert_dir`, M8, skip. Otherwise `SSL_CERT_DIR` set and not empty → print M3 and export
   `SSL_CERT_DIR="$SSL_CERT_DIR:$cert_dir"`; unset or empty → export `SSL_CERT_DIR="/etc/ssl/certs:$cert_dir"`. Print M9.
6. `exec dotnet DockerUpdateGuard.dll "$@"` unchanged.

"Skip" means: leave `SSL_CERT_DIR` as it was and continue to step 6. `SSL_CERT_FILE`, the system bundle,
`update-ca-certificates` and `/usr/local/share/ca-certificates` are not used, for root too. A block that passes the
line rules but is no certificate is written and counted; .NET and Go ignore that file alone (facts 8, 9; AC12).

Messages, to standard output, exact text (`<…>` filled in; paths and counts only, never file contents), in this
order: M1/M2 alone, or the per-file messages M5–M7 in file order, then M8, or M3 followed by M9; M4 ends the block:

- M1 `No /app/certs directory; using the system CA certificates only`
- M2 `No certificate files found in /app/certs; using the system CA certificates only`
- M3 `SSL_CERT_DIR is set; adding the certificates from /app/certs after <previous value>`
- M4 `Warning: cannot write the certificates under /tmp; certificates in /app/certs are not used`
- M5 `Skipping non-certificate file: <path>` (today's text)
- M6 `Warning: cannot read <path>; skipping it`
- M7 `Warning: <path> contains no complete PEM block "-----BEGIN CERTIFICATE-----" (other labels, such as TRUSTED CERTIFICATE, and indented markers are not accepted); skipping it`
- M8 `No usable certificates in /app/certs; using the system CA certificates only`
- M9 `Added <k> certificate(s) from <n> file(s) in /app/certs; SSL_CERT_DIR=<exported value>`

Today's "Found certificate files ... attempting import into system trust store..." and "No permission to write
system trust store; skipping certificate import" disappear.

Dockerfile: comment changes only — `:60` and `:62-67` say that the entrypoint adds mounted CA certificates through
a private directory under `/tmp` and `SSL_CERT_DIR`, so no write access to the system trust store is needed. No
functional line changes.

## Options considered

Group-writable trust store with `update-ca-certificates` (issue option 1); one combined bundle plus `SSL_CERT_FILE`
(issue option 2, the first plan; rejected after fact 8); one file per block in a private directory appended to
`SSL_CERT_DIR` (chosen); loading the certificates in C# per `HttpClient`; keeping `update-ca-certificates` for root
beside the directory. Reasons in record 0011.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `src/DockerUpdateGuard/entrypoint.sh` | certificate block, header comment and settings-check line 12 rewritten (*Approach*) |
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

- `docs/areas/hosting.md`: *Container image* — entrypoint step 2 rewritten: every complete PEM certificate block of
  the `.crt`/`.pem` files in `/app/certs` is written to its own file in a new private directory under `/tmp`, which
  is appended to `SSL_CERT_DIR` (after a pre-set value, else after `/etc/ssl/certs`), so the application, its
  database driver and Trivy trust them in addition to the system CAs, for any UID, root included; the system trust
  store and `SSL_CERT_FILE` are never touched; the accepted form (unindented `CERTIFICATE` markers, base64 body,
  byte order mark and CRLF tolerated; DER and other labels not accepted); a block that is valid PEM but no
  certificate is counted and then ignored without affecting the others; when nothing is usable or `/tmp` is not
  writable, a message is printed, the system CAs alone apply and the container starts; a read-only root filesystem
  needs a writable `/tmp`; `/tmp` should not be shared between containers, and directories of earlier starts are not
  removed. *Limits and errors*: the row "Certificate import not possible" becomes rows for "no usable certificate in
  `/app/certs`" and "`/tmp` not writable" (message, system CAs only, container starts). *Known gaps*: remove the #285
  entry. *Related decisions*: add 0011.
- `DOCKER.md`: mounts table `:97` — `/app/certs` holds CA certificates and client certificates; section
  *Automatic CA import on startup* `:113-117` rewritten (works for the default user, no `--user 0`, PEM only with
  unindented markers, DER and `TRUSTED CERTIFICATE` not accepted, private keys never copied, pre-set `SSL_CERT_DIR`
  extended, read-only root filesystem needs a writable `/tmp`); *Notes* `:169` — drop the advice to run as root or
  bake the CA into a derived image for the import; keep the warning at `:123` that every `*.pem` in `/app/certs` is
  treated as a CA file (its certificate blocks are trusted).
- `INSTALL.md` *Beyond the quick start* (`:124-131`): one sentence that an internal CA is trusted by mounting its
  PEM file into `/app/certs` (read-only), also with the default user, linking `DOCKER.md`.
- `README.md` *Notes for Docker-based deployments* (`:293-299`): one bullet, the same statement.
- `docs/ARCHITECTURE.md` §11 *Security posture*, bullet "Container runs as a non-root user" (`:397-402`): one
  sentence that CA certificates mounted at `/app/certs` are trusted through a private directory under `/tmp` and
  `SSL_CERT_DIR`, so the import needs neither root nor write access to the system trust store, linking record 0011
  and [Hosting and configuration](areas/hosting.md).
- `src/DockerUpdateGuard/entrypoint.sh` header comment and Dockerfile comments: part of the code change (*Approach*).

Sweep done: `grep -rn -i "app/certs\|trust store\|ca-certificates\|update-ca\|64000"` over `*.md`, `*.yml`,
`*.sh`, Dockerfiles outside `specs/` and `.squad/`; every hit not listed above is about client certificates or
the runtime user and stays true.

## Architecture check

Non-root UID `64000`, GID `0`, the `/app` ownership model and exit code `1` only for unreadable settings are kept
(AC9, AC10); the non-root guarantee gets stronger, as no root is needed for custom CAs. `.squad/project.md`
guarantees are not touched. `docs/ARCHITECTURE.md` gains one sentence (*Documentation updates*), owner Dev.

## Security considerations

- [ ] No limits: `/app/certs` is operator-controlled (a mount); its size is not bounded, and the private directory
  holds no more than the mounted certificate blocks.
- [ ] Exceptions reaching a user-visible message: none (no C#). The entrypoint messages carry paths and counts,
  never file contents; `awk`/`mktemp` error output is discarded or replaced by M4/M6.
- [ ] Trust: whoever can write `/app/certs` can add trust anchors — as today for root; the directory is private
  (`0700`) to the running UID; only standard marker lines and base64 lines are written, so no private-key line can
  be copied (AC6); the system CAs and a pre-set `SSL_CERT_FILE`/`SSL_CERT_DIR` stay in force (AC1, AC7); an
  undecodable block costs only itself (AC12).

## Decision records

- `docs/decisions/0011-mounted-ca-certificates-use-a-private-cert-dir.md` (Proposed), index row updated (renamed from
  the combined-bundle record of the first plan; unreleased, edited in place).

## Challenge

- **DA 1 (newline before append to a pre-set `SSL_CERT_FILE`):** accepted in effect — the design no longer copies or
  appends to any bundle; `SSL_CERT_FILE` is never read. AC7 keeps a pre-set file without final newline as a case.
- **DA 2 (cleanup on shared `/tmp`):** accepted — the cleanup is dropped (step 3); `hosting.md` states that `/tmp`
  should not be shared and that earlier directories stay; record 0011, *Consequences*.
- **DA 3 (AC5 target):** accepted — AC5 scans `<test server>/probe:latest` with `--image-src remote`, so the test
  server is the first TLS endpoint, and compares the error text with and without the CA.
- **DA 4 (settings check line 12):** accepted — rewritten with `test` (*Approach*), AC9 reworded to "keeps its
  behavior".

## Security review

- **Finding 1 (blocking, undecodable block drops later CAs in .NET):** resolved by switching to per-block files in a
  private directory appended to `SSL_CERT_DIR` (fact 8, fact 9, record 0011 rewritten: the former option 3
  rationale was wrong and is now the decision). AC12 covers a base64-valid non-DER block and a truncated block with
  END before a valid `.crt`. Residual: such a block is counted in M9 and ignored without a message, since the image
  has no certificate decoder (record 0011, *Consequences*; `hosting.md`).
- **Finding 2 (blocking, prefix-matched markers):** resolved — markers match exactly after removing only trailing
  carriage return and whitespace, and only the standard marker lines are written (step 4); AC6 has the glued-marker
  case.
- **Finding 3 (BOM, empty body, indented PEM, labels):** taken in — BOM removed on line 1, at least one body line
  required, indented markers rejected and named in M7 and `DOCKER.md`, M7 names unsupported labels; AC4 and AC6
  cases added.
- **Finding 4 (cleanup on shared `/tmp`):** resolved with DA 2.

## Out of scope / follow-ups

- DER-encoded certificate files: not accepted (M7); documented in `DOCKER.md`.
- Trust consumers that ignore `SSL_CERT_DIR`: none in the image.
- #171 (Trivy private registries) benefits from this fix; no change there.