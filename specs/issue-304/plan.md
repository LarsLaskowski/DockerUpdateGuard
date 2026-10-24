# Plan: Trivy error output may echo credentials embedded in TrivyBaseUrl

Source: Issue #304
Status: Draft
Tier: security — the change adds a guard on the handling of a configured credential (the user info of `TrivyBaseUrl`) so that it no longer reaches logs, scan-run messages and the pages through Trivy's error output.
Security areas touched: Secrets in configuration (`TrivyBaseUrl` user info); External process execution (`TrivyVulnerabilityProvider`, handling of the process's standard error); Logging and telemetry of external data (`TrivyCommandFailed` log entry and its `Error` attribute)

## Problem / root cause

`TrivyVulnerabilityProvider.GetVulnerabilitiesAsync` passes the configured `TrivyBaseUrl` unchanged (only a trailing
`/` trimmed) to Trivy as `--server` (`src/DockerUpdateGuard/Vulnerabilities/TrivyVulnerabilityProvider.cs:206-212`).
On a non-zero exit code that is not a missing manifest, `GetErrorExcerpt` (lines 231-237) returns the last non-empty
line of standard error as it stands, and lines 305-311 write it into the `TrivyCommandFailed` log entry (event 3421,
attribute `Error`) and into the `Failed` result message `Trivy exited with code X: <line>`, which becomes the scan-run
message shown on the pages. Trivy is a Go program, and Go's HTTP client and URL parser repeat the URL — and parts of its
user info — in their error texts (facts 3-6), so a credential in `TrivyBaseUrl` can reach the log, the telemetry export,
the database and the UI. Record 0005 (#295) scoped external-tool output out.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | Issue: the last line of Trivy's stderr goes into the log (`TrivyCommandFailed`) and the scan-run message | `TrivyVulnerabilityProvider.cs:231-237` (`GetErrorExcerpt`), `:305-311`; `TrivyVulnerabilityProviderLogging.cs:27-33` | confirmed |
| 2 | `TrivyBaseUrl` with user info is accepted and passed to Trivy as configured | `DockerUpdateGuardOptionsValidator.cs:110-125` checks only `Uri.TryCreate` absolute + http/https; scratch run: `https://user:secret@trivy:4954` → accepted, user info `user:secret`; `BuildArguments` line 212 passes `trivyBaseUrl.TrimEnd('/')` | confirmed |
| 3 | Go's HTTP client error masks only the password and keeps the user name; a user name without password is printed in full | Go 1.24 source `net/http/client.go:1033-1039` (`stripPassword`: replaces `u.User.String()+"@"` with `Username()+":***@"` only when a password is set), used for `url.Error.URL` at `:624-631`; Trivy 0.58.1's Go version not checked, `stripPassword` is long-standing | confirmed |
| 4 | Go's URL parse error quotes the raw URL, user info included | Go `net/url/url.go:32` (`%s %q: %s`), `:480` (`&Error{"parse", u, err}` with the raw string) | confirmed |
| 5 | Go's escape error repeats up to three characters of the user info after the URL, i.e. after its last `@` | `net/url/url.go:215,224` (`EscapeError(s[i:i+3])`), `:606-617` (user info unescaped after `validUserinfo`, which allows `%`); scratch run: the validator's `Uri.TryCreate` accepts `https://u:p%zz@trivy:4954` | confirmed |
| 6 | A user info with `#` or `?` is accepted by validation and both parsers read the part before it as the host, so a resolver error names the would-be user name without an `@` near it | scratch run: `https://admin:#secret@trivy:4954` and `https://admin:?secret@trivy:4954` → accepted, host `admin`, no user info; Go `net/url/url.go:477` cuts `#` first; the resolver error form `lookup <host>: no such host` is from knowledge, not measured (the design does not rest on it) | confirmed |
| 7 | Masking the error line with `UrlRedaction.RedactUserInfo` (the issue's first option) is therefore incomplete: it keeps everything after the last `@` | `UrlRedaction.cs:31-58` (fallback keeps `url.AsSpan(last)`); facts 5 and 6 put fragments there or nowhere near an `@` | confirmed |
| 8 | Without an `@` in `TrivyBaseUrl`, neither parser sees user info, so Trivy gets no URL credential to echo | Go `parseAuthority` `net/url/url.go:588-600` (`LastIndex(authority, "@") < 0` → no user); .NET user info needs `@` as well | confirmed |
| 9 | What Trivy 0.58.1 itself writes to stderr for a failing server call with user info | to verify: run `trivy image --server https://user:secret@127.0.0.1:1 --quiet --format json alpine:3` in a scratch environment and read the last stderr line; owner: Security, step 3, where the sandbox allows it (the Lead's download and run of the release binary was denied here). The chosen design does not rest on it: it withholds the line whatever form it has | unverified |
| 10 | The other failure texts carry no Trivy output: timeout and missing manifest are fixed texts; an exception gives `Trivy scan failed: {ex.Message}` | `TrivyVulnerabilityProvider.cs:289-303`, `:325-329`; `Process.Start` failure message is `An error occurred trying to start process '{0}' with working directory '{1}'. {2}` (resource string in `System.Diagnostics.Process.dll` 10.0.13) — file name and directory only, no arguments | confirmed |
| 11 | Record 0005 is unreleased and may be extended in place | `git log --diff-filter=A` → d822166; `git merge-base --is-ancestor d822166 <v1.3.7>` fails; `git ls-remote --tags origin` latest `v1.3.7` | confirmed |
| 12 | `TestLogger<T>` records formatted messages and event ids the tests can assert | `src/Tests/DockerUpdateGuard.Tests/Data/TestLogger{TCategoryName}.cs:34` (`Entries`), `TestLogEntry.cs` (`EventId`, `LogLevel`, `Message`) | confirmed |

Claims of the issue: "Trivy may repeat it there" — confirmed for the Go layers Trivy uses (facts 3-6), Trivy's own
output unverified (fact 9); "passes the last line … into logs and scan-run messages" — confirmed (fact 1).

## Acceptance criteria

- [ ] AC1: When `TrivyBaseUrl` contains an `@` and Trivy exits with a non-zero code whose error output has a non-empty
  line and is not a missing manifest, the result is *failed* with exactly the message
  `Trivy exited with code <code>: error output withheld because TrivyBaseUrl may carry credentials`, and the
  `TrivyCommandFailed` entry (event id 3421) ends with `: error output withheld because TrivyBaseUrl may carry credentials`;
  neither contains any part of the configured user info nor of the error line. Decided in
  `TrivyVulnerabilityProvider.GetErrorExcerpt`.
- [ ] AC2: When `TrivyBaseUrl` contains no `@`, the message and the log entry carry the last non-empty, trimmed line of
  the error output unchanged (today's behavior).
- [ ] AC3: When the error output has no non-empty line, the message is `Trivy exited with code <code>: no error output`
  whether or not `TrivyBaseUrl` contains an `@`.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `TrivyVulnerabilityProviderGetVulnerabilitiesAsyncWithholdsErrorOutputWhenBaseUrlCarriesUserInfoAsync` (data rows below) | `src/Tests/DockerUpdateGuard.Tests/TrivyVulnerabilityProviderTests.cs` |
| AC2 | `TrivyVulnerabilityProviderGetVulnerabilitiesAsyncReportsLastErrorLineWithoutUserInfoAsync` (cannot fail yet: deferred to step 6) | `src/Tests/DockerUpdateGuard.Tests/TrivyVulnerabilityProviderTests.cs` |
| AC3 | `TrivyVulnerabilityProviderGetVulnerabilitiesAsyncReportsNoErrorOutputWhenBaseUrlCarriesUserInfoAsync` (cannot fail yet: deferred to step 6) | `src/Tests/DockerUpdateGuard.Tests/TrivyVulnerabilityProviderTests.cs` |

AC1 data rows (base URL; last stderr line; two fragments that must appear in neither the message nor the log entry),
each modelled on the Go texts of facts 3-6, exit code 1, image `docker.io/library/nginx:latest`:

1. `https://user:secret@trivy:4954`; `FATAL Fatal error run error: Post "https://user:***@trivy:4954/twirp/trivy.cache.v1.Cache/MissingBlobs": dial tcp: lookup trivy: no such host`; `user`, `trivy.cache`
2. `https://token@trivy:4954`; `FATAL Fatal error run error: Post "https://token@trivy:4954/twirp/trivy.cache.v1.Cache/MissingBlobs": dial tcp: lookup trivy: no such host`; `token`, `trivy.cache`
3. `https://admin:#secret@trivy:4954`; `FATAL Fatal error run error: Post "https://admin:#secret@trivy:4954/twirp/trivy.cache.v1.Cache/MissingBlobs": dial tcp: lookup admin: no such host`; `admin`, `secret`
4. `https://user:p%zz@trivy:4954`; `FATAL Fatal error run error: parse "https://user:p%zz@trivy:4954/twirp/trivy.cache.v1.Cache/MissingBlobs": invalid URL escape "%zz"`; `user`, `%zz`

AC1 asserts status *failed*, the exact message, exactly one entry with event id 3421 whose message ends with the
withheld text, and `DoesNotContain` of both fragments in message and entry. The Tester may add a `CreateProvider`
overload that takes the `TestLogger<TrivyVulnerabilityProvider>` (test helper only). AC2: `http://trivy.local:4954`,
standard error of two lines, `first line` and `FATAL Fatal error run error: image scan error` (with surrounding
spaces on the last one); message `Trivy exited with code 1: FATAL Fatal error run error: image scan error`, the log
entry ends with the same line. AC3: `https://user:secret@trivy:4954`, standard error of only whitespace and line
breaks; message `Trivy exited with code 1: no error output`.

## Approach

- In `TrivyVulnerabilityProvider`, add the constant `WithheldErrorOutput` =
  `error output withheld because TrivyBaseUrl may carry credentials` (region *Constants*).
- `GetErrorExcerpt` takes the configured base URL: no non-empty line → `no error output`; else `trivyBaseUrl`
  contains `@` → `WithheldErrorOutput`; else the last line as today. The call at line 305 passes `trivyBaseUrl`
  (non-null after the guard at line 269). Log and message keep their templates; both receive the same excerpt.
- `IsManifestNotFound` keeps reading the full standard error (boolean only; its result text is fixed).
- No masking with `UrlRedaction` and no change to the validator, the arguments or `UrlRedaction` itself.
- User-visible output: one fixed text, as in AC1; no layout beyond it.

## Options considered

1. **Withhold the error line whenever `TrivyBaseUrl` contains an `@`** — chosen: complete without knowing Trivy's
   output (fact 8), one condition, fails closed (also for an `@` that is not user info).
2. **Mask the line with `UrlRedaction.RedactUserInfo`** (the issue's first option) — keeps the diagnostics, but leaves
   the fragments of facts 5 and 6 visible (fact 7).
3. **Mask only for a user info Go parses like the platform, withhold otherwise** — keeps diagnostics in the common
   case, but rests on an enumeration of Go's parser forms and on fact 9, which nobody measured.
4. **Reject user info in `TrivyBaseUrl` in validation** (the issue's second option) — breaks startup for a basic-auth
   proxy in front of the Trivy server that works today (Go sends the user info as basic auth); a product decision, as
   record 0005 option 3 found.
5. **Send the user info as an `Authorization` header via `--custom-headers` and strip it from `--server`** — Trivy's
   handling of the header is unmeasured, the header would still stand on the command line, and it changes the
   process arguments; beyond this bug.

The reasoning lives in record 0005 (options 7-11).

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Vulnerabilities/TrivyVulnerabilityProvider.cs` | constant `WithheldErrorOutput`; `GetErrorExcerpt` gets the base URL; call site at line 305 |
| DockerUpdateGuard.Tests | `TrivyVulnerabilityProviderTests.cs` | three tests of *Tests per criterion*; optional `CreateProvider` overload |

## Signatures (for the Dev's skeleton)

No public or internal API changes; the skeleton step has nothing to build. Private members:

- `private const string WithheldErrorOutput = "error output withheld because TrivyBaseUrl may carry credentials";`
- `private static string GetErrorExcerpt(string standardError, string trivyBaseUrl)` (replaces
  `GetErrorExcerpt(string standardError)`)

## Test files

- `src/Tests/DockerUpdateGuard.Tests/TrivyVulnerabilityProviderTests.cs`

Existing test code that calls a changed signature: none (`GetErrorExcerpt` is private; the existing tests'
`CreateProvider(IProcessRunner, string)` stays).

Files of the change no unit test can cover: none.

## Areas

- `docs/areas/vulnerabilities.md` (*Trivy*): the failed-exit bullet gains the withholding rule; *Related decisions*
  gains 0005.
- `docs/areas/telemetry.md` (*Credentials and external data*): error messages from external tools, Trivy's only for a
  `TrivyBaseUrl` without `@`.

## Documentation updates

Sweep: `grep -n -i "external tool\|error output\|user info\|Trivy exited\|last line"` over `docs/ARCHITECTURE.md`,
`docs/areas/*.md`, `README.md`, `DOCKER.md`, `INSTALL.md`, `SECURITY.md`; hits that this change extends are the three
below (the others describe URL masking of other clients and stay true). All owned by the **Dev**:

1. `docs/areas/vulnerabilities.md`, after the sentence ending `"Trivy exited with code X: <last line of the error output, or 'no error output'>".`
   add: `When TrivyBaseUrl contains an @ (user info, or an @ anywhere else in it), a non-empty error output is
   replaced by "error output withheld because TrivyBaseUrl may carry credentials" in the message and the log, because
   Trivy may repeat parts of the URL's user info there.` (with `TrivyBaseUrl` and `@` in code spans); and under
   *Related decisions* add `- [0005](../decisions/0005-urls-are-output-with-user-info-masked.md): URLs are output with
   their user info masked; Trivy's error output is withheld for a TrivyBaseUrl with an @.`
2. `docs/areas/telemetry.md`, *Credentials and external data*: `counts, durations and error messages from external
   tools.` becomes `counts, durations and error messages from external tools (Trivy's only for a TrivyBaseUrl without
   @, [Vulnerabilities](vulnerabilities.md#trivy)).`
3. `docs/ARCHITECTURE.md`, *Security posture*, bullet *Secrets in configuration*: after `masked as ***` add
   `, and Trivy's error output is withheld when TrivyBaseUrl contains an @` before the existing links.

Record 0005 is extended by the Lead (done in this step). `README.md`'s `TrivyBaseUrl` row stays.

## Architecture check

Guarantee "integration clients never throw for expected failures" kept: the result stays `Failed` with a message.
Layering unchanged (one provider class). The *Secrets in configuration* posture is strengthened; `docs/ARCHITECTURE.md`
gets the sentence of update 3, linked to record 0005 already.

## Security considerations

- [ ] Byte or length limits on parsed input: no limits (no new parsing; standard error is still split as today).
- [ ] Exceptions that can reach a user-visible message: none new. `Trivy scan failed: {ex.Message}` is unchanged; its
  sources carry no Trivy output or arguments (fact 10).
- Residual: Trivy's command line still carries the credential (`--server`), readable to processes that can list the
  container's processes — follow-up below. Scan-run messages stored before the change are not rewritten.
- Fact 9 stays unverified; the design withholds the line whatever Trivy writes.

## Decision records

- `docs/decisions/0005-urls-are-output-with-user-info-masked.md` (Accepted, unreleased) — extended in place: context
  for Trivy's error output, options 7-11, the decision for option 7, consequences. Index row unchanged.

## Out of scope / follow-ups

- Follow-up issue (for the orchestrator): title `Trivy server credentials are visible on the Trivy command line`;
  body: `TrivyVulnerabilityProvider passes TrivyBaseUrl, user info included, as the --server argument, so the
  credential is readable in the process list of the container (and of the host for root). Decide whether to pass the
  server address through the environment (TRIVY_SERVER; IProcessRunner has no environment support yet), to support
  Trivy's token authentication (--token / --token-header) as a proper credential setting, or to document the
  limitation. Found while working on #304.`
- Masking free text from other external sources: not covered (no other tool output is passed on).

## Security notes

Non-blocking findings of Security's plan review (verdict APPROVED), verbatim:

1. Update fact 9 from "unverified" to "confirmed" with the measurement above (Trivy v0.58.1 built from the Go module proxy; the last stderr line shows `user:***@` for `https://user:secret@…`, `token@` for `https://token@…`, `"https://admin#secret@…"` for `https://admin:#secret@…`, `"https://admin?secret@…"` for `https://admin:?secret@…`, and the full raw URL with `invalid URL escape "%zz"` for `https://user:p%zz@…`).
2. Fact 6 says the error names the would-be host with no `@` nearby. In practice Trivy prints the full URL, `@` included (`admin#secret@`). AC1 data row 3 could use that measured form instead; the design is unaffected.
3. Fact 2 cites `DockerUpdateGuardOptionsValidator.cs:110-125`, but the `TrivyBaseUrl` call is at line 141.
4. The follow-up issue for the credential on the command line (`--server`) is correctly left out of scope.
