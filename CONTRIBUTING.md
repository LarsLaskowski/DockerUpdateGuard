# Contributing

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
dotnet test src\Tests\**\*.csproj -c Release --no-build --logger trx --collect:"XPlat Code Coverage"
```

To run a single test project or method, see the commands in `README.md` and `CLAUDE.md`.

### Submitting a pull request

If you'd like to contribute by fixing a bug, implementing a feature, or even correcting typos in the documentation, you'll need to submit a pull request.

Before submitting a pull request, be sure to [rebase](https://www.atlassian.com/git/tutorials/merging-vs-rebasing) your branch onto the current `main`. Do not use `git merge` or the *merge* button provided by GitHub.

For PR naming use the following convention: `[area] Description` (no period at the end).

- For the area, use the affected project or feature (for example `Data`, `Telemetry`, `UI`, `Scanning`).
- For the description, do not reference an issue number in there. A clear, short summary of what the change entails is enough; there is room to elaborate in the description.

When a PR is related to an issue, use the `Closes #issuenumber` syntax so the issue links to the PR automatically and closes when the PR is merged.

Use before/after screenshots in the PR description when a change affects the UI.

Follow the PR template in [`.github/PULL_REQUEST_TEMPLATE.md`](.github/PULL_REQUEST_TEMPLATE.md).

## Code style

Detailed C# code-style rules (naming, regions, formatting, XML docs, null handling) are documented in [`.github/instructions/csharp.instructions.md`](.github/instructions/csharp.instructions.md) and are binding for all contributions. Run `reihitsu-format ./` before opening a pull request.

## Stability policy

An essential consideration in every pull request is its impact on the system. Avoid introducing unnecessary breaking changes, performance or functional regressions, or negative impacts on usability.

## Reporting security issues

Do not report security vulnerabilities through public GitHub issues. See [`SECURITY.md`](SECURITY.md) for the private reporting process.

## License

By contributing to this project, you agree that your contributions will be licensed under the same [MIT License](LICENSE.md) that covers the project.
