# Plan: Trivy scan target can begin with a dash

Source: Issue #367
Status: Draft
Tier: security — the fix creates a guard on the Trivy command line that decides which Trivy options a registered image reference can set, and the diff, with its new test, exceeds the `patch` budget.
Security areas touched: External process execution

## Problem / root cause

`TrivyVulnerabilityProvider.BuildArguments` (`src/DockerUpdateGuard/Vulnerabilities/TrivyVulnerabilityProvider.cs`,
lines 241–257) puts the scan target from `GetScanTarget` (lines 218–233) as the last argument directly after the fixed
options, with no end-of-options marker. The target starts with the registry (non-`docker.io`) or the repository
(`docker.io`) of the image reference, and a reference the user registers is only split by `ImageReferenceParser.Parse`
(`src/DockerUpdateGuard/Images/ImageReferenceParser.cs`, lines 116–143), never held to the reference grammar. So a
registered `--config=x.y/z` (registry `--config=x.y`, because the first segment contains a `.`, line 45) gives the
argument `--config=x.y/z:latest`, and a registered `--input=/tmp/x.tar` (registry `docker.io`, repository
`--input=/tmp/x.tar`) gives `--input=/tmp/x.tar:latest`: Trivy's pflag parser reads either as an option of its own.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | The scan target begins with the registry for non-`docker.io` images and with the repository for `docker.io` images; no `--` precedes it | `TrivyVulnerabilityProvider.cs` lines 220–225 and 243–256 | confirmed |
| 2 | Registering `--config=x.y/z` yields registry `--config=x.y`, repository `z`, tag `latest`, so the target is `--config=x.y/z:latest` (issue's claim) | `ImageReferenceParser.cs` lines 116–139 (no `:` after the last `/`, so tag `latest`; first segment has a `.`, `LooksLikeRegistry` line 45) | confirmed |
| 3 | A `docker.io` repository can begin with `-` too: `-a/b` gives the target `-a/b:latest`, `--input=/tmp/x.tar` gives `--input=/tmp/x.tar:latest` | `ImageReferenceParser.cs` lines 140–144 (first segment without `.`/`:` → `docker.io`, all segments kept); `GetScanTarget` line 223 drops the `docker.io` registry | confirmed |
| 4 | The registered parts survive the round trip into the provider | `VulnerabilityEnrichmentService.cs` line 287 parses `Format(image)` again; `Format` (`ImageReferenceParser.cs` line 164) writes `registry/repository:tag`, which re-parses to the same parts for rows 2 and 3 | confirmed |
| 5 | Registration does not check the reference grammar | `ImageRegistrationService.cs` line 77 (plain `Parse`); record 0014, options 18 and 21 | confirmed |
| 6 | References from Docker Engine containers and base-image labels cannot begin with `-` | `RuntimeContainerScanOrchestrator.cs` line 590 (`ValidReferenceOrNull`); registry and repository patterns in `RegistryReferenceValidator.cs` lines 76 and 85 start with `[a-zA-Z0-9]` / `[a-z0-9]` or `\[`; record 0014, *Decision* | confirmed |
| 7 | No shell is involved; each argument reaches Trivy as one `argv` entry, so `--` is seen by Trivy as written | `ProcessRunner.cs` lines 28 (`UseShellExecute = false`) and 34 (`ArgumentList.Add`) | confirmed |
| 8 | The container image bundles Trivy v0.58.1 | `src/DockerUpdateGuard/Dockerfile` line 4 | confirmed |
| 9 | Without `--`, Trivy v0.58.1 reads `--config=x.y/z:latest` as its `--config` option and then fails for lack of an image argument (the issue's "likely an argument-count error") | to verify: Dev, step 6 — run the Trivy v0.58.1 Linux binary (GitHub release `v0.58.1`, archive checked against the release's checksums file, unpacked in the scratchpad) with `TRIVY_SERVER=http://127.0.0.1:9` and the current argument list ending in `--config=x.y/z:latest`; record exit code and last error line in the report | unverified |
| 10 | With `--` before the target, Trivy v0.58.1 takes a dash-leading target as the image name (no option is set from it) and exits non-zero, so the scan fails closed | to verify: Dev, step 6 — same binary, argument list ending in `--`, then each of `--config=x.y/z:latest`, `--input=/tmp/x.tar:latest`, `-a/b:latest`; the error must name the target as an image reference that cannot be parsed or found, not a flag or argument-count error, and no file `/tmp/x.tar:latest` is read (it does not exist; the error must not be a file-open error) | unverified |
| 11 | `--` does not change how Trivy v0.58.1 handles a normal target | to verify: Dev, step 6 — same binary, argument list ending in `-- library/alpine:3.20`; the error, if any, concerns the server or registry, not argument parsing | unverified |

If row 10 or 11 is refuted, the Dev stops and reports it; the Lead decides in mode `decide` (fallback: record 0015,
option 2).

## Acceptance criteria

- [ ] AC1: Every Trivy scan passes the arguments `image --format json --quiet --scanners vuln --pkg-types os,library
  --timeout <RequestTimeoutSeconds>s -- <target>`: the fixed options unchanged and in this order, then exactly one
  `--`, then the target as the last argument.
- [ ] AC2: A target that begins with `-` (registry `--config=x.y` with repository `z`; `docker.io` with repository
  `--input=/tmp/x.tar`; `docker.io` with repository `-a/b`) is passed unchanged as the last argument, directly after
  the only `--`, and the arguments before `--` are the fixed options of AC1 (nothing derived from the reference).

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `TrivyVulnerabilityProviderGetVulnerabilitiesAsyncBuildsClientModeCommandAsync` (existing; expected list gains `"--"` before `"library/nginx:latest"`) | `src/Tests/DockerUpdateGuard.Tests/TrivyVulnerabilityProviderTests.cs` |
| AC2 | `TrivyVulnerabilityProviderGetVulnerabilitiesAsyncPassesDashLeadingTargetAfterEndOfOptionsAsync` (new; `DataRow` per target of AC2: registry, repository, expected target) | `src/Tests/DockerUpdateGuard.Tests/TrivyVulnerabilityProviderTests.cs` |

Both fail on the current code (no `--`). The AC2 test asserts the full captured list: the ten fixed arguments, `--`,
the expected target, count 12, and that `--` occurs once.

## Approach

In `BuildArguments`, insert `--` as the element directly before `scanTarget`, through a new private constant (below).
Nothing else in the provider changes: no new failure path or message. A dash-leading target then reaches Trivy as an
image name and, when Trivy cannot resolve it, ends in the existing non-zero-exit path ("Trivy exited with code X:
…", area document *Trivy*). Design fails closed: the guard is a fixed argument, independent of the reference's
content. No new user-visible output.

## Options considered

See record 0015: `--` before the target (chosen); refusing a target that begins with `-` with a fixed failure reason
(lost: an extra failure path and message that `--` makes unreachable, and it must mirror pflag's notion of an option);
both (lost: the refusal adds nothing); holding registered references to the grammar (lost: record 0014, options 18
and 21).

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Vulnerabilities/TrivyVulnerabilityProvider.cs` | constant `EndOfOptionsMarker`; `BuildArguments` inserts it before the target; its XML summary says so |
| DockerUpdateGuard.Tests | `TrivyVulnerabilityProviderTests.cs` | AC1 test adapted, AC2 test added |

## Signatures (for the Dev's skeleton)

none — no public, internal or changed member signature; no skeleton step. The Dev adds in the *Constants* region,
with an XML summary:
`private const string EndOfOptionsMarker = "--";`

## Test files

`src/Tests/DockerUpdateGuard.Tests/TrivyVulnerabilityProviderTests.cs`.

Existing test code that calls a changed signature: none. Existing tests whose input AC1 handles differently:
`TrivyVulnerabilityProviderGetVulnerabilitiesAsyncBuildsClientModeCommandAsync` (exact list) — adapted by the Tester in
step 5 as the AC1 test. The other tests of the file read only the last argument, the absence of `--server` or of
base-URL parts, or the environment; `--` contains none of `secret`, `user`, `@`, `trivy.local`, so they stay unchanged.

Files no unit test can cover: none.

## Areas

- Vulnerabilities (`docs/areas/vulnerabilities.md`): the Trivy argument list gains `--` before the target, and why.
- Image catalog (`docs/areas/image-catalog.md`): the list of guards on a user-entered reference gains the Trivy one.

## Documentation updates

Dev, once the code is green (sweep done: `--pkg-types`, "scan target", `<target>` and the user-reference guards across
`docs/`, `README.md`, `docs/ARCHITECTURE.md`):

- `docs/areas/vulnerabilities.md`, *Trivy*, line 93: the argument list ends `--timeout <RequestTimeoutSeconds>s --
  <target>`; add that `--` ends Trivy's options, so a target beginning with `-` (possible for a registered reference)
  is read as the image name and, unresolvable, ends *failed* as described below. *Related decisions*: add 0015.
- `docs/areas/image-catalog.md`, line 34 ("A reference entered by the user keeps the plain parsing above; registry
  requests refuse …"): add that the Trivy scan passes it after `--` ([Vulnerabilities](vulnerabilities.md#trivy)) and
  link 0015.
- `docs/ARCHITECTURE.md`: none — no guarantee or flow changes; the security posture section names no argument rules.

## Architecture check

No guarantee of `.squad/project.md` is touched: the provider still returns `ExternalOperationResult` and never throws
for an expected failure; layering and DI are unchanged.

## Security considerations

- [x] No limits.
- [x] Exceptions reaching a user-visible message: none new; Trivy's non-zero exit keeps its existing message with the
  error line cleaned and withheld as the area document says.

## Decision records

- `docs/decisions/0015-the-trivy-scan-target-follows-an-end-of-options-marker.md` (Proposed)

## Out of scope / follow-ups

- Holding registered references to the reference grammar (record 0014, option 21; tracked in #343).
