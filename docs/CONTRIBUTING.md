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

Follow the PR template in [`.github/pull_request_template.md`](../.github/pull_request_template.md).

## Quality gates

Code-style rules are documented in [`CLAUDE.md`](../CLAUDE.md) (mirrored in `AGENTS.md` and
[`.github/copilot-instructions.md`](../.github/copilot-instructions.md)) and in
[`.squad/stack.md`](../.squad/stack.md), and are binding for all contributions. Before opening a pull
request, run the commands from `stack.md`: *Format*, *Build*, the *Analyzer gate* (no analyzer diagnostic
of any severity in a changed file) and the *Coverage gate* (at least 80 % line coverage on new or changed
production code and overall, see [`UNIT_TESTS.md`](UNIT_TESTS.md#code-coverage)). A pull request is
expected to arrive clean (see the decision record on quality gates in [`decisions/`](decisions/README.md)).

<!-- project:begin releases -->
## Versioning and releases

Releases are cut by pushing a `v*.*.*` tag on `main` (with explicit approval): `.github/workflows/release.yml`
builds and tests, pushes the Docker image (version and `latest`) to Docker Hub
(`networlddev/dockerupdateguard`) and creates a GitHub Release with generated notes. Merging a PR by itself
never publishes a release (see [`ARCHITECTURE.md`](ARCHITECTURE.md), *Build, CI, and deployment*).
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
