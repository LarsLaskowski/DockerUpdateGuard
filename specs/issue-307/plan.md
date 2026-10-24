# Plan: Keep the Trivy server credentials off the Trivy command line

Source: Issue #307
Status: Draft
Tier: security — the change alters how a configured credential (`TrivyBaseUrl` user info) is handed to the external Trivy process, i.e. the handling of a secret.
Security areas touched: External process execution (`TrivyVulnerabilityProvider`, `IProcessRunner`, `ProcessRunner`); Secrets in configuration (`Vulnerabilities:TrivyBaseUrl` user info)

## Problem / root cause

`TrivyVulnerabilityProvider.BuildArguments` (`src/DockerUpdateGuard/Vulnerabilities/TrivyVulnerabilityProvider.cs`,
lines 211–229) puts `trivyBaseUrl.TrimEnd('/')` right after `--server` (line 216–217), and `ProcessRunner.RunAsync`
(`src/DockerUpdateGuard/Infrastructure/ProcessRunner.cs`, lines 30–33) passes every entry to
`ProcessStartInfo.ArgumentList`. A `TrivyBaseUrl` with user info (`https://user:secret@trivy:4954`, accepted by
validation for a basic-auth proxy in front of the Trivy server) therefore stands in `/proc/<pid>/cmdline` of the Trivy
process, which any user who can see the process may read — on the host, every host user. `IProcessRunner`
(`src/DockerUpdateGuard/Infrastructure/IProcessRunner.cs`, line 18) has no way to pass environment variables.

Fix: `IProcessRunner.RunAsync` gets an environment-variables parameter that `ProcessRunner` adds to the inherited
environment; the Trivy provider drops `--server <url>` from the arguments and passes `TRIVY_SERVER=<url>` instead.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | The issue: `TrivyBaseUrl`, user info included, is passed as the `--server` argument | `TrivyVulnerabilityProvider.cs` lines 216–217, 290–298; `ProcessRunner.cs` lines 30–33 | confirmed |
| 2 | The issue: `IProcessRunner` has no environment support | `IProcessRunner.cs` line 18: `RunAsync(fileName, arguments, timeout, cancellationToken)`; `ProcessRunner` never touches `startInfo.Environment` | confirmed |
| 3 | The issue: the credential is readable in the process list of the container and the host | Measured in the sandbox (Linux, root starting `env SECRETX=… /bin/sh -c 'sleep 20' argsecret-marker`, read as UID 65534 via `setpriv`): `/proc/<pid>/cmdline` printed the marker argument; `/proc/<pid>/environ` gave "Permission denied" (mode `-r--------`). The issue's "of the host for root" understates it: arguments are readable by every host user | confirmed |
| 4 | Trivy reads the server address from `TRIVY_SERVER` exactly like `--server` | Trivy v0.58.1 source (shallow clone of tag `v0.58.1`): `pkg/flag/remote_flags.go` `ServerAddrFlag{Name: "server", ConfigName: "server.addr"}`; `pkg/flag/options.go` lines 268–312, `Bind` calls `BindEnv`, which binds `ConfigName` to `strings.ToUpper("trivy_" + Name)` = `TRIVY_SERVER`; `pkg/commands/app.go` line 267 gives `image` the client flags; `pkg/commands/artifact/run.go` lines 162–171 choose client mode on `opts.ServerAddr != ""` | confirmed |
| 5 | The image bundles Trivy v0.58.1 | `src/DockerUpdateGuard/Dockerfile` line 4 `ARG TRIVY_IMAGE="aquasec/trivy:0.58.1"`, line 55 | confirmed |
| 6 | The Trivy process inherits the application's environment today, and the image relies on it (`TRIVY_CACHE_DIR`, `PATH`) | `ProcessRunner.cs` lines 21–28 (`UseShellExecute = false`, `Environment` untouched); `Dockerfile` line 56 `ENV TRIVY_CACHE_DIR=/tmp/trivy` | confirmed |
| 7 | Setting entries in `ProcessStartInfo.Environment` adds to a copy of the current process's environment rather than replacing it | to verify: `ProcessRunnerTests.ProcessRunnerRunAsyncAddsEnvironmentVariablesToInheritedEnvironmentAsync` (child `env` output holds both the added variable and an inherited `PATH=` line); owner Tester, step 5/6 | unverified |
| 8 | `IProcessRunner` has one production caller | `grep RunAsync(` over `src/`: only `TrivyVulnerabilityProvider.cs` line 294; registration `ServiceCollectionExtensions.cs` line 106 | confirmed |
| 9 | Trivy still repeats the server URL in its error output when the address comes from the environment, so the withholding of #304 stays needed | Trivy builds its client URL from `opts.ServerAddr` whatever its source (fact 4); the measured lines of record 0005 come from that URL | confirmed |
| 10 | `ProcessRunner` has no unit test today | `src/Tests/DockerUpdateGuard.Tests/` has no `ProcessRunnerTests.cs` | confirmed |
| 11 | Record 0005 is unreleased, so it is extended in place | `git log --diff-filter=A` gives `d822166`; `git ls-remote --tags origin` lists `v1.1.0`–`v1.3.7`, none contains it (`git tag --contains d822166` empty) | confirmed |

## Acceptance criteria

- [ ] AC1: For any configured `TrivyBaseUrl`, the argument list the provider passes to `IProcessRunner.RunAsync` contains
  neither `--server` nor any part of the base URL; for the URL `https://user:secret@trivy.local:4954` and an image reference without digest no
  argument contains `secret`, `user` or `@`. The other arguments keep their order:
  `image --format json --quiet --scanners vuln --pkg-types os,library --timeout <N>s <target>`.
- [ ] AC2: The provider passes exactly one environment variable, `TRIVY_SERVER`, whose value is `TrivyBaseUrl`
  without trailing `/`, user info unchanged (`https://user:secret@trivy.local:4954/` → `https://user:secret@trivy.local:4954`;
  `http://trivy.local:4954` → `http://trivy.local:4954`).
- [ ] AC3: `ProcessRunner.RunAsync` starts the process with the given environment variables added to the environment
  it inherits: the child sees each given name with its value and still sees inherited variables such as `PATH`;
  a given name the process also inherits is replaced, so the child sees exactly one entry of that name, with the given value.
- [ ] AC4: `ProcessRunner.RunAsync` throws `ArgumentNullException` for a null `environmentVariables`, before any
  process starts.

The existing behavior of the provider (not-configured result, timeout, manifest-not-found, error-output withholding of
#304, report mapping) is unchanged; the existing tests pin it.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `TrivyVulnerabilityProviderGetVulnerabilitiesAsyncBuildsClientModeCommandAsync` (existing, expected list changed to drop `--server` and the URL); new `TrivyVulnerabilityProviderGetVulnerabilitiesAsyncKeepsUserInfoOffCommandLineAsync` (`https://user:secret@trivy.local:4954`: no argument contains `secret`, `user`, `@` or `trivy.local`) | `src/Tests/DockerUpdateGuard.Tests/TrivyVulnerabilityProviderTests.cs` |
| AC2 | `TrivyVulnerabilityProviderGetVulnerabilitiesAsyncPassesServerAddressThroughEnvironmentAsync` with `[DataRow]`s `("https://user:secret@trivy.local:4954/", "https://user:secret@trivy.local:4954")` and `("http://trivy.local:4954", "http://trivy.local:4954")`; asserts the captured dictionary has count 1 and `TRIVY_SERVER` equals the expected value | `src/Tests/DockerUpdateGuard.Tests/TrivyVulnerabilityProviderTests.cs` |
| AC3 | `ProcessRunnerRunAsyncAddsEnvironmentVariablesToInheritedEnvironmentAsync`: runs `env` (no arguments) with `{ "DUG_PROCESS_RUNNER_TEST": "value-from-runner" }`, 30 s timeout; asserts exit code 0, a stdout line `DUG_PROCESS_RUNNER_TEST=value-from-runner` and a stdout line starting with `PATH=`; marked `[OSCondition(OperatingSystems.Linux)]` (CI runs on `ubuntu-latest`); `ProcessRunnerRunAsyncReplacesInheritedEnvironmentVariableAsync` (added in step 9): asserts as precondition that the test process has a non-empty `HOME` (read only, never `Environment.SetEnvironmentVariable`), runs `env` with `{ "HOME": "/dug-overridden" }`, asserts exit code 0 and exactly one stdout line starting with `HOME=`, equal to `HOME=/dug-overridden`; same `OSCondition` | `src/Tests/DockerUpdateGuard.Tests/ProcessRunnerTests.cs` (new) |
| AC4 | `ProcessRunnerRunAsyncThrowsForNullEnvironmentVariablesAsync`: `RunAsync("env", [], null!, 30 s)` throws `ArgumentNullException` with `ParamName` `environmentVariables` | `src/Tests/DockerUpdateGuard.Tests/ProcessRunnerTests.cs` (new) |

## Approach

1. `IProcessRunner.RunAsync` gets a required parameter `IReadOnlyDictionary<string, string> environmentVariables`
   between `arguments` and `timeout`. Required, not optional: every caller states what it hands the child; an empty
   dictionary means "inherited environment only".
2. `ProcessRunner.RunAsync`: `ArgumentNullException.ThrowIfNull(environmentVariables)` next to the existing guards
   (before `Process.Start`); after the argument loop, `startInfo.Environment[name] = value` for each entry. Never
   `Environment.Clear()` — the Trivy process needs the inherited `PATH` and `TRIVY_CACHE_DIR`. No logging of names or
   values.
3. `TrivyVulnerabilityProvider`: a constant `TrivyServerVariable = "TRIVY_SERVER"` (Constants region, XML doc);
   `BuildArguments(string scanTarget, int requestTimeoutSeconds)` loses `--server` and the URL; a new private static
   `BuildEnvironmentVariables(string trivyBaseUrl)` returns a dictionary with the single entry
   `TRIVY_SERVER = trivyBaseUrl.TrimEnd('/')`; `GetVulnerabilitiesAsync` passes it as the new argument. The
   `@`-based withholding in `GetErrorExcerpt` stays unchanged (fact 9).
4. A `TRIVY_SERVER` inherited from the application's environment is overwritten by the provider's value, as the
   `--server` argument overrode it before; other inherited `TRIVY_*` variables (e.g. `TRIVY_TOKEN`) still reach Trivy.

No new user-visible output.

## Options considered

1. **`TRIVY_SERVER` in the environment (chosen)** — no configuration change, every working setup keeps working, the
   credential leaves the argument list; readable only by the same UID and root (fact 3).
2. **A token setting (`--token`/`--token-header`)** — a new configuration option (a feature), and as a flag it would
   be on the command line too; `TRIVY_TOKEN` already reaches Trivy through the inherited environment.
3. **User info stripped from `--server`, sent as `Authorization` via `TRIVY_CUSTOM_HEADERS`** — more code, rebuilds
   Go's basic-auth handling, and Trivy's splitting of a list value from the environment is unmeasured.
4. **A Trivy config file with `server.addr`** — puts the secret on disk with permission and cleanup handling per scan.
5. **Document the limitation only** — leaves the credential readable to every host user.
6. **An optional `environmentVariables` parameter, or a second overload** — a default would let a future caller
   forget the choice, and an overload leaves the existing NSubstitute set-ups on the old method answering `null` for
   the new call; the existing test call sites need adapting either way.

Details and reasons are in record 0005 (options 12–16).

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Infrastructure/IProcessRunner.cs` | `RunAsync` gains `environmentVariables` |
| DockerUpdateGuard | `Infrastructure/ProcessRunner.cs` | null guard; adds the entries to `startInfo.Environment` |
| DockerUpdateGuard | `Vulnerabilities/TrivyVulnerabilityProvider.cs` | constant `TrivyServerVariable`; `BuildArguments` without the URL; new `BuildEnvironmentVariables`; call passes the dictionary |
| DockerUpdateGuard.Tests | `TrivyVulnerabilityProviderTests.cs`, new `ProcessRunnerTests.cs` | see *Test files* |

## Signatures (for the Dev's skeleton)

- `Task<ProcessExecutionResult> IProcessRunner.RunAsync(string fileName, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> environmentVariables, TimeSpan timeout, CancellationToken cancellationToken = default)` — XML `<param name="environmentVariables">Environment variables added to the inherited environment of the process</param>`
- `public async Task<ProcessExecutionResult> ProcessRunner.RunAsync(string fileName, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> environmentVariables, TimeSpan timeout, CancellationToken cancellationToken = default)` (`/// <inheritdoc/>`)
- `private const string TrivyServerVariable = "TRIVY_SERVER";` in `TrivyVulnerabilityProvider`
- `private static List<string> BuildArguments(string scanTarget, int requestTimeoutSeconds)` (changed: `trivyBaseUrl` removed)
- `private static Dictionary<string, string> BuildEnvironmentVariables(string trivyBaseUrl)` (new)

Files the skeleton rewrites: `IProcessRunner.cs`, `ProcessRunner.cs`, `TrivyVulnerabilityProvider.cs` (read; the
three are final apart from these members). For the skeleton the existing `ProcessRunner.RunAsync` body may stay as
it is with the new parameter unused (the AC3/AC4 tests then fail on it), reported as a deviation per *Skeleton* in
`.squad/stack.md`.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/TrivyVulnerabilityProviderTests.cs` (existing)
- `src/Tests/DockerUpdateGuard.Tests/ProcessRunnerTests.cs` (new, class `ProcessRunnerTests`)

Existing test call sites of the changed signature, adapted by the **Dev in step 4** (the old signature goes away),
mechanically, no assertion touched: in `TrivyVulnerabilityProviderTests.cs` every `processRunner.RunAsync(…)`
set-up — lines 32, 91, 186, 342, 388, 416, 486, 554, 645, 729, 801, 859, 913 — gets
`Arg.Any<IReadOnlyDictionary<string, string>>()` as third argument, and the `DidNotReceiveWithAnyArgs().RunAsync`
check at line 470 a further `default!`. `VulnerabilityProviderResolverTests.cs` line 86 only creates a substitute and
needs no change. The Tester changes the expected list in `…BuildsClientModeCommandAsync` (AC1) in step 5.

Files no unit test can cover: none.

## Areas

- `docs/areas/vulnerabilities.md`, *Trivy*: the argument list without `--server <TrivyBaseUrl …>`; a new statement
  that the server address is passed as `TRIVY_SERVER` (without trailing `/`) in the process environment, added to the
  environment inherited from the application (an inherited `TRIVY_SERVER` is replaced), so it does not stand on the
  command line; the withholding bullet stays. *Related decisions*, 0005 line: add "the Trivy server address is passed
  in the environment".

## Documentation updates

Sweep: `grep -n -i "trivy\|--server\|IProcessRunner"` over `README.md`, `DOCKER.md`, `INSTALL.md`, `SECURITY.md`,
`docs/ARCHITECTURE.md`, `docs/areas/*.md`, `.squad/project.md`. Hits that change:

| File | Edit | Owner |
| ---- | ---- | ----- |
| `docs/areas/vulnerabilities.md` | as under *Areas* | Dev |
| `DOCKER.md` line 9 | "run through `trivy image --server <TrivyBaseUrl>` in client mode" → "run through `trivy image` in client mode against `<TrivyBaseUrl>`, passed to the CLI as `TRIVY_SERVER` in its environment" | Dev |
| `.squad/project.md`, *Security areas*, External process execution | "(argument construction, untrusted image references, JSON parsing of stdout)" → "(argument and environment construction, untrusted image references, JSON parsing of stdout)" | Dev |
| `docs/ARCHITECTURE.md` §11, *Secrets in configuration* bullet | add after the withholding clause: "and the Trivy server address reaches the Trivy process in its environment (`TRIVY_SERVER`), not on its command line" (record 0005 stays the link) | Lead (mode `docs`) |
| `docs/decisions/0005-urls-are-output-with-user-info-masked.md` | extended in this step (context, options 12–16, decision, consequences) | Lead (done) |

Hits that stay true: `README.md` line 135, `docs/ARCHITECTURE.md` lines 224 and 469, `docs/areas/hosting.md` lines
114–115, `docs/areas/telemetry.md` lines 106–107, `INSTALL.md` (no `--server`), `docs/docker-compose.*.yml`.

## Architecture check

No guarantee of `.squad/project.md` is touched: the provider still returns `ExternalOperationResult<T>` for every
expected failure, layering is unchanged (`Infrastructure` in the host project), no new service or registration
(`ServiceCollectionExtensions.cs` line 106 unchanged). The integration contract with Trivy changes from a flag to its
documented environment binding (fact 4). `docs/ARCHITECTURE.md` §11 gets the sentence above.

## Security considerations

- [ ] No limits: no parsed input changes.
- [ ] Exceptions reaching a user-visible message: unchanged — "Trivy scan failed: <ex.Message>" for an unexpected
  error; a `Process.Start` failure message names the file name and working directory only, and the arguments no
  longer hold the URL anyway. The new `ArgumentNullException` cannot reach a user: the provider always passes a
  dictionary.
- Residual: the address stays readable in `/proc/<pid>/environ` by UID 64000 and root, who can read the
  configuration anyway; a custom `TrivyExecutablePath` that ignores `TRIVY_SERVER` would scan without a server
  (downloading the database itself, then *succeeded* or *failed* as usual), not leak the credential. Both are in
  record 0005, *Consequences*.

## Decision records

- `docs/decisions/0005-urls-are-output-with-user-info-masked.md` — unreleased (fact 11), extended in place with
  options 12–16 and the decision for the command line; status stays `Accepted` as the records README prescribes for
  an in-place edit; index row unchanged.

## Out of scope / follow-ups

- A dedicated Trivy token setting: not needed for this fix (`TRIVY_TOKEN` is inherited); no follow-up issue unless the
  Product Manager wants one.
- An allow-list for the Trivy child environment: no follow-up issue (step 9); the reason is in record 0005,
  *Consequences*.
- Other places a credential could reach a process list: none — Trivy is the only external process (fact 8).

## Challenge notes

1. `minor` — no test anywhere checks that the bundled Trivy binary actually reads `TRIVY_SERVER`, and getting it wrong fails silently. The unit tests only pin the dictionary the provider builds. With a mistyped name or an unbound variable, Trivy would quietly switch to standalone mode: it downloads its own database and scans report *succeeded*, so no criterion would catch it. Instruction: before handing back, the Tester or Dev runs the bundled binary once in the image, or a local `aquasec/trivy:0.58.1`. Run `TRIVY_SERVER=http://127.0.0.1:1 trivy image --quiet --scanners vuln alpine:3.20`, then the same command with `--server` instead of the variable. Record in `specs/issue-307/log.md` that both fail with the same error connecting to the server at `127.0.0.1:1` and neither starts a database download. Ranked minor because the source evidence for fact 4 is concrete and this only confirms it without changing the plan.

## Security notes

1. **Trivy's `TRIVY_SERVER` handling was confirmed from source only, never run.** I tried to download the Trivy v0.58.1 binary to run `TRIVY_SERVER=… trivy image` against a local listener, but the sandbox's permission check refused the download, so no run happened. The credential-hiding guarantee doesn't depend on this: it only needs the URL to be off `ArgumentList`, which AC1 pins. If the binding failed, Trivy would quietly scan without the server and download the vulnerability database itself (an unexpected outbound call). That is a functional regression, not a credential leak. I recommend the Dev or the user run the bundled image once against a Trivy server, or against a listener that logs the request, and record the result in `log.md`.
2. **Existing hardening gap, outside this change.** Trivy, a third-party binary, inherits the application's whole environment. That includes any secrets an operator supplies as environment variables, such as the database connection string, the Docker Hub PAT and Portainer credentials. This exposure is not new and this change does not widen it. If the Product Manager wants it, it belongs in its own issue (for example, an allow-list for the child environment), not in this PR.
