# Contributing

<!-- project:begin getting-started -->
## Getting started

### Machine setup

To begin you'll need Git, the .NET SDK, and a PostgreSQL instance set up on your machine.

The `DockerUpdateGuard` repository uses Git as its source control system. If you haven't already installed it, you can download it [here](https://git-scm.com/downloads) or, if you prefer a GUI-based approach, try [GitHub Desktop](https://desktop.github.com/).

Once Git is installed, you'll also need the .NET SDK matching the version targeted by the solution (currently `net10.0`). Instructions and downloads for your preferred OS can be found [here](https://dotnet.microsoft.com/download).

The application connects to a PostgreSQL database at runtime and applies EF Core migrations automatically on startup. A local PostgreSQL instance (native or via Docker) is enough for development.

Format checks rely on `reihitsu-format`, a .NET tool. Install it once with:

```shell
dotnet tool install -g Reihitsu.Cli
```

> [!IMPORTANT]
> The above steps are a one-time setup for your machine and do not need to be repeated after the initial configuration.

### Cloning the repository

Now that your machine is set up, you can clone the `DockerUpdateGuard` repository. Open a terminal and run this command:

```shell
git clone https://github.com/LarsLaskowski/DockerUpdateGuard.git
```

Cloning via SSH:

```shell
git clone git@github.com:LarsLaskowski/DockerUpdateGuard.git
```

### Installing and building

From within the folder where you've cloned the repo, restore, format, and build the solution with the following commands:

```shell
dotnet restore DockerUpdateGuard.slnx
reihitsu-format ./
dotnet build DockerUpdateGuard.slnx -c Release --no-restore
```

### Running tests

```shell
dotnet test DockerUpdateGuard.slnx -c Release --no-build
```

Coverage, single-test and analyzer commands are listed in [`.squad/stack.md`](../.squad/stack.md).

For detailed rules on how unit tests should be structured and named, see [`UNIT_TESTS.md`](UNIT_TESTS.md).
<!-- project:end getting-started -->

## Submitting a pull request

Nothing is ever committed or pushed directly to `main` — every change goes through a separate branch and
a pull request.

Pull requests are merged with **Squash and merge**: the PR title becomes the single commit subject on
`main` and the description its body, so the commits on the branch are working history and need not be
curated. Keep the branch up to date by merging the current `main` into it (no force-push needed); do not
use the plain *Create a merge commit* or *Rebase and merge* buttons (see the decision record on
squash-merging in [`decisions/`](decisions/README.md)).

For PR naming use the following convention: `[area] Description` (no period at the end).

- For the area, use one of the areas listed below, capitalized.
- For the description, do not reference an issue number in there. A clear, short summary of what
  the change entails is enough; there is room to elaborate in the description.

<!-- project:begin areas -->
Areas: `Data`, `Telemetry`, `UI`, `Scanning`, `Host`, `Tests`, `Docker`, `CI`, `Docs` — the affected project or
feature. Use before/after screenshots in the PR description when a change affects the UI.
<!-- project:end areas -->

When a PR is related to an issue, use the `Closes #issuenumber` syntax so the issue links to the
PR automatically and closes when the PR is merged.

The PR's label decides its section in the generated release notes (`.github/release.yml`): `breaking-change`,
`enhancement` or `feature`, `security`, `bug`; any other PR is listed under *Other changes*, and `ignore-for-release` leaves it out.

Follow the PR template in [`.github/pull_request_template.md`](../.github/pull_request_template.md).

## Contributions from outside

Issues and pull requests from forks are welcome. Changes in this repository are spec-driven: what a feature does
is agreed as acceptance criteria before it is built, and the maintainers own those criteria.

- **Bug fixes and documentation** may come as a pull request directly. Describe how to reproduce the bug, or
  link an issue that does.
- **Features and changes of behavior start with an issue** (the feature request template). A maintainer answers
  with the proposed scenarios and acceptance criteria; open a pull request once you agree on them, and link the
  issue with `Closes #…`. A feature pull request without an agreed issue is put on hold until there is one.
- **Out of scope for an outside pull request:** the AI squad and agent instructions (`.squad/` except
  `stack.md` and `project.md`, `.claude/`, `AGENTS.md`, `CLAUDE.md`, `.github/copilot-instructions.md`), the
  text outside the `<!-- project:… -->` blocks of the template's marked files, and the workflows under
  `.github/workflows/`. They come from a shared template and are changed there; the *Fork scope* check fails
  on the first two. Open an issue instead.
- **Checks on a fork:** pull requests from forks run without repository secrets, so the analysis that needs one
  (e.g. SonarQube Cloud) does not run there; a maintainer runs it before merging. Run the quality gates below
  locally so the pull request arrives clean. The CI run of a first-time contributor's fork waits for a maintainer's
  approval before it starts.
- Maintainers may push to your branch ("Allow edits by maintainers") or carry the change on in a branch of their
  own; you stay credited as co-author of the squashed commit.

## Quality gates

Code-style rules are documented in [`AGENTS.md`](../AGENTS.md) and in
[`.squad/stack.md`](../.squad/stack.md), and are binding for all contributions. Before opening a pull
request, run the commands from `stack.md`: *Format*, *Build*, the *Analyzer gate* (no analyzer diagnostic
of any severity in a changed file) and the *Coverage gate* (line coverage on new or changed production code
and overall, thresholds in [`UNIT_TESTS.md`](UNIT_TESTS.md#code-coverage)). A pull request is
expected to arrive clean (see the decision record on quality gates in [`decisions/`](decisions/README.md)).

<!-- project:begin releases -->
## Versioning and releases

Releases are cut by pushing a `v*.*.*` tag on `main` (with explicit approval): `.github/workflows/release.yml`
builds and tests, pushes the Docker image (version and `latest`) to Docker Hub
(`networlddev/dockerupdateguard`) and creates a GitHub Release with generated notes. Merging a PR by itself
never publishes a release (see [`ARCHITECTURE.md`](ARCHITECTURE.md), *Build, CI, and deployment*).

### Release plan from 1.3.8

The GitHub milestones are the release plan: one milestone is one release. A release is due when the last open
issue of its milestone has been closed, not earlier and not later. The current release is **1.3.7**.

| Milestone                                  | Issues                                                            | Version     | What an operator gets afterwards                                                   |
| ------------------------------------------ | ----------------------------------------------------------------- | ----------- | ---------------------------------------------------------------------------------- |
| M1 – Security and release hygiene          | #264, #295, #276, #269, #273, #279, #285, #34, #37, #39            | **1.3.8**   | No leaked URL credentials or forwarded tokens, safe Portainer targeting, correct release labels |
| M2 – Scan resilience                       | #281, #272, #271, #270, #287, #288, #292, #267, #265, #6, #25, #19 | **1.3.9**   | Scans and startup survive bad config, slow containers and registry errors          |
| M3 – Image identity and catalog            | #275, #284, #286, #266, #268, #293, #9, #4, #10                    | **1.3.10**  | One identity per image, correct manual registration and shared base images         |
| M4 – Update detection and UI correctness   | #277, #289, #290, #294, #278, #274, #82, #15, #40, #36             | **1.3.11**  | Correct publication times, retention, gauges, texts and status chips               |
| M5 – Catalog control and hosting           | #282, #291, #283, #171, #24                                        | **1.4.0**   | Disable/delete images, pin update targets, reverse-proxy headers, private registries for Trivy |
| M6 – Data integrity and performance        | #14, #3, #21, #22, #27, #80, #81, #28                              | **1.5.0**   | Unique findings, scoped DbContext, no N+1 loads, tests against PostgreSQL          |
| M7 – Portainer actions and telemetry       | #280, #83, #84, #86, #87                                           | **1.6.0**   | Portainer container actions from the UI, trace spans, telemetry cleanup            |
| M8 – Notifications                         | #220–#224                                                          | **1.7.0**   | Webhook notifications for updates and critical CVEs with reliable delivery         |
| M9 – Notification history (optional)       | #225                                                               | **1.8.0**   | Read-only notification history page                                                |

- M1–M4 are patch releases without new features; M5–M9 are minor releases, each adding one capability.
- M5 comes before M7 because the Portainer action uses the pinned update target (#291); M1 comes first because
  it fixes the retry of non-idempotent POSTs (#273), the ambiguous endpoint (#279) and the missing warning (#276)
  the action depends on.
- #4 and #9 are covered by #284 and #293; they close with those issues.
- An issue opened later gets the milestone of the earliest release that has not been tagged yet, unless the
  maintainer assigns a later one.
- A tag is pushed only with explicit approval (see above); the milestone, not the merge of a pull request, makes a
  release due.

### Updating the bundled Trivy

Dependabot is not configured for Docker images in this repository, so the Trivy CLI the image bundles (the `TRIVY_IMAGE` build argument in
`src/DockerUpdateGuard/Dockerfile`, pinned by digest) is updated by hand. Decision records
[0015](decisions/0015-the-trivy-scan-target-follows-an-end-of-options-marker.md) and
[0016](decisions/0016-the-bundled-trivy-skips-its-update-check-and-telemetry.md) rest on measurements of the bundled
version, so every new version is measured again before it is bundled, and the results go into the pull request:

1. Take the digest of the new version's multi-arch image index (`docker buildx imagetools inspect
   aquasec/trivy:<version>`, line `Digest:`) and check that `docker pull aquasec/trivy@<digest>` succeeds.
2. Extract `/usr/local/bin/trivy` from that image (`docker create`, `docker cp`) and check that `trivy --version`
   names the new version and that `file` reports the binary as statically linked, since the runtime image is
   Alpine-based.
3. With `TRIVY_SERVER` pointing at an unreachable server (for example `http://127.0.0.1:9`), re-run the cases of
   #367 with the provider's argument list ([Vulnerabilities](areas/vulnerabilities.md#trivy)): without `--`, the
   target `--config=x.y/z:latest` is read as an option; after `--`, the targets `--config=…`, `--input=…` and `-a/b`
   are taken as image names, the run exits non-zero and opens no file they name (for example checked with
   `strace -f -e trace=open,openat`); `-- library/alpine:3.20` takes the normal scan path and fails on the
   unreachable server, which shows that `TRIVY_SERVER` is read.
4. Run one scan with `TRIVY_SKIP_VERSION_CHECK=true` and `TRIVY_DISABLE_TELEMETRY=true` and a control run without
   them, watching the process's DNS lookups and connections: the first reaches no destination other than those
   [Hosting](areas/hosting.md#container-image) lists, and never `check.trivy.dev`; the control run shows the request
   to `check.trivy.dev`, so the observation is known to see it.
5. Change together: the `TRIVY_IMAGE` default and the version in its comment in the Dockerfile, the
   `aquasec/trivy:<version>` image of both Trivy servers in `docs/docker-compose.trivy.yml` and
   `docs/docker-compose.full.yml` (client and server stay on one version), and the bundled version named in
   [Hosting](areas/hosting.md#container-image) and in records 0005, 0015 and 0016: in place while a record is
   unreleased, through a new record that amends it once it is released ([Decision records](decisions/README.md)).
   A version named as the one a past measurement was made with (v0.58.1) stays.
<!-- project:end releases -->

<!-- project:begin stability -->
## Stability policy

An essential consideration in every pull request is its impact on the system. Avoid introducing unnecessary
breaking changes, performance or functional regressions, or negative impacts on usability. In particular,
preserve the guarantees listed in [`.squad/project.md`](../.squad/project.md) (*Guarantees*) and described in
[`ARCHITECTURE.md`](ARCHITECTURE.md) unless a change explicitly intends to alter one.
<!-- project:end stability -->

## Reporting security issues

Do not report security vulnerabilities through public GitHub issues. See
[`SECURITY.md`](../SECURITY.md) for the private reporting process.

## License

<!-- project:begin license -->
By contributing to this project, you agree that your contributions will be licensed under the same
[MIT License](../LICENSE.md) that covers the project.
<!-- project:end license -->
