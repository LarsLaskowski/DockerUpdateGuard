# 0005: URLs are output with their user info masked

- **Status:** Proposed
- **Date:** 2026-10-24
- **Area:** Telemetry
- **Source:** Issue #295
- **Supersedes:** —

## Context

Configuration validation accepts Docker instance and Portainer base URLs that carry user info
(`https://user:secret@host`), and the application wrote those URLs as they stand into log messages and their
attributes, into exception and result messages that end up in scan-run errors and on the pages, into the database
and onto the Docker Instances pages; the retry log dropped only the query string. Secrets in configuration must
never be logged, shown in the UI or put into exception messages or telemetry (`.squad/project.md`, *Security
areas*). The clients still have to connect with the URL as configured.

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

## Decision

Option 1. A URL that the platform's URI parser accepts with user info is output rebuilt from its parts with the user
info replaced by `***`; any other text that contains `@` is masked fail-closed from after its `scheme://` (or from its
start) up to its last `@`; text without `@` is output unchanged. The rules and where they apply are specified in
[Telemetry](../areas/telemetry.md#credentials-and-external-data) and [Web UI](../areas/web-ui.md#secrets).

## Consequences

- No configured URL credential reaches logs, telemetry attributes, messages, scan runs, the database or the UI
  through the paths this change covers; the HTTP client spans and the HTTP client factory's own log lines already
  omit user info, because the platform builds their URL from scheme, host, port and path.
- A URL with user info is shown in the parser's normalized form (lower-case host, default port dropped, trailing
  slash added); a URL without user info is shown exactly as configured.
- Text that only looks like user info (an `@` in an unparseable value) is over-masked; accepted, it fails closed.
- Messages an external tool writes (Trivy's error output) are passed on as they stand; masking free text is not part
  of this decision.
- Rows stored before the change are masked on display and rewritten masked by the next synchronization; scan-run
  error messages stored before the change are not rewritten.
