# 0005: URLs are output with their user info masked

- **Status:** Accepted
- **Date:** 2026-10-24
- **Area:** Telemetry
- **Source:** Issue #295, Issue #304
- **Supersedes:** —

## Context

Configuration validation accepts Docker instance and Portainer base URLs that carry user info
(`https://user:secret@host`), and the application wrote those URLs as they stand into log messages and their
attributes, into exception and result messages that end up in scan-run errors and on the pages, into the database
and onto the Docker Instances pages; the retry log dropped only the query string. Secrets in configuration must
never be logged, shown in the UI or put into exception messages or telemetry (`.squad/project.md`, *Security
areas*). The clients still have to connect with the URL as configured.

`Vulnerabilities:TrivyBaseUrl` is handed to the Trivy CLI as configured, and the last line of Trivy's error output
went into the log and the scan result message as it stood (issue #304). Go, which Trivy is written in, echoes the URL
there: its HTTP client masks only the password (`user:***@`; a user name alone not at all), and its URL parser quotes
the raw URL in a parse error and adds the offending part after it (`invalid URL escape "%zz"`, up to three characters
of the user info). For a user info with `#` or `?`, which configuration validation accepts, both the platform and Go
read the part before it as the host, so Go's resolver error names the would-be user name
(`lookup admin: no such host`). Measured with Trivy v0.58.1, the last line of its error output carries `user:***@` for
`https://user:secret@…`, `token@` for `https://token@…`, `"https://admin#secret@…"` for `https://admin:#secret@…`
and the full raw URL with `invalid URL escape "%zz"` for `https://user:p%zz@…`.

## Options considered

1. **Mask the user info with `***` wherever a URL is output, keep the configured value for connecting** — operators
   still see that credentials are configured and which host is meant; one helper serves every output site, and the
   stored display values are masked on write and again on display.
2. **Drop the user info entirely** — equally safe, but the output no longer shows that the configured URL carries
   credentials, which hides a configuration the operator may want to move into a proper credential setting.
3. **Reject URLs with user info in configuration validation** — removes the problem at its source, but makes
   startup fail for configurations that work today; a product decision beyond this bug.
4. **Redact in a log processor or enricher** — covers exported logs only: the console logger, exception and result
   messages, scan-run errors, the database and the UI would still carry the secret.
5. **Mask only the leading `scheme://userinfo@` with a regular expression, as the issue sketched** — a password with
   an unescaped `@` (`https://user:p@ss@host`, which the URI parser rejects) would leave the part after the first
   `@` visible, and one with a `/` would not be matched at all.
6. **Mask only on display, store the configured URL** — keeps the secret at rest in the database.

For Trivy's error output:

7. **Withhold Trivy's error text whenever `TrivyBaseUrl` contains an `@`** — complete without knowing what Trivy
   writes: without an `@` neither parser sees user info, so Trivy gets no credential it could echo. Operators of such a
   configuration lose Trivy's diagnostics in the log and on the pages and keep the exit code.
8. **Mask the error line with the helper of option 1** — keeps the diagnostics, but leaves every fragment Go writes
   after the last `@` visible: the escape fragment and, for a user info with `#` or `?`, the user name in the resolver
   error.
9. **Mask with the helper only for a user info Go parses like the platform, withhold otherwise** — keeps the
   diagnostics for the common case, but rests on a list of Go's parser forms and on Trivy never writing a credential
   without an `@` after it, which nobody measured.
10. **Reject user info in `TrivyBaseUrl`** — as option 3: startup would fail for a basic-auth proxy in front of the
    Trivy server that works today; a product decision.
11. **Strip the user info from `--server` and send it as an `Authorization` header (`--custom-headers`)** — Trivy
    would never see the credential in a URL, but how Trivy applies the header was not measured, and the header would
    still stand on the command line; a change of the integration beyond this bug.

## Decision

Option 1. A value with exactly one `@` that the platform's URI parser accepts as `scheme://` with non-empty user info
is output rebuilt from its parts with the user info replaced by `***`; every other value that contains `@` is masked
fail-closed from after its `scheme://` (or from its start) up to its last `@`, because the parser ends the authority at
the first `/`, `?` or `#` and so reads a password such as `admin:#secret@host` as a host without user info; text
without `@` is output unchanged. The rules and where they apply are specified in
[Telemetry](../areas/telemetry.md#credentials-and-external-data) and [Web UI](../areas/web-ui.md#secrets).

For Trivy's error output, option 7: when `TrivyBaseUrl` contains an `@`, a failed Trivy run reports a fixed text in
place of the last line of its error output, in the log and in the result message; the rule is in
[Vulnerabilities](../areas/vulnerabilities.md#trivy).

## Consequences

- No configured URL credential reaches logs, telemetry attributes, messages, scan runs, the database or the UI
  through the paths this change covers; the HTTP client spans and the HTTP client factory's own log lines already
  omit user info, because the platform builds their URL from scheme, host, port and path.
- A URL with user info is shown in the parser's normalized form (lower-case host, default port dropped, trailing
  slash added); a URL without user info is shown exactly as configured.
- A value with an `@` that is not user info (`https://host.example/@scope/pkg` → `https://***@scope/pkg`, or a second
  `@` in the path) is over-masked; accepted, it fails closed. Masking an already masked value changes nothing.
- Trivy's error output reaches the log and the pages only for a `TrivyBaseUrl` without `@`, unchanged; for one with
  an `@` (also a path such as `https://host/@x`) it is withheld, so a misconfigured basic-auth setup has to be
  diagnosed by running Trivy by hand. Free text from other sources is not masked.
- The credential still stands on Trivy's command line (`--server`), readable by processes that may list the
  container's processes; not covered here.
- Rows stored before the change are masked on display and rewritten masked by the next synchronization; scan-run
  error messages stored before either change are not rewritten.
