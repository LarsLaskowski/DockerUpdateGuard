# 0066: Remote timestamps without an offset are read as UTC

- **Status:** Proposed
- **Date:** 2026-11-27
- **Area:** Integration failures
- **Source:** Issue #549
- **Supersedes:** —

## Context

The registry clients, the Docker Engine client and the .NET release feed read remote timestamps (`last_pushed`,
`last_updated`, `created`, `issued_at`, the Engine's stats `read` and image `Created`, the feed's `eol-date` and
`latest-release-date`) with the invariant culture and no style, so a value without an offset took the host's time
zone. The image runs in UTC by default, so only a host with another `TZ` was affected: there a tag's publication time
moved by the zone's offset, an offset-less epoch `created` of a reproducible build counted as a real build time west of
UTC, a token's `issued_at` shifted its cache expiry, and a channel's end-of-life date began at local midnight. A value
with an explicit offset kept that offset, although the fields are named and stored as UTC.

## Options considered

1. **Read a value without an offset as UTC and convert every value to offset zero** — one rule for every remote
   timestamp, independent of the host; the feed's date-only values become 00:00 UTC, which is how they are meant.
   Chosen.
2. **Reject a value without an offset as unreadable** (the issue's alternative) — no guessing, but the feed's dates
   carry no offset by format and would all become unknown, and an unknown publication time disables the
   published-since bounds and age rules for that tag.
3. **Fix only the two registry clients' `created` and `last_pushed`** (the issue's scope) — smallest change, but leaves
   the same defect in `last_updated`, `issued_at`, the Docker Engine client and the release feed.

## Decision

Every remote timestamp is parsed by one shared helper that reads a value without an offset as UTC and returns it
converted to offset zero; an unreadable value stays unknown as before. The rule is stated in
[Integration failures](../areas/integration-failures.md#remote-timestamps).

## Consequences

- Times no longer depend on the host's `TZ`. A source that meant its own local time without saying so is read as UTC;
  no source of the app is known to do that (apart from the tests of this rule, every timestamp in the tests' answers carries an offset or is a date).
- Values with an explicit offset keep their instant; only their offset becomes zero, which comparisons and the UI (UTC,
  [0058](0058-timestamps-are-shown-in-utc-with-an-explicit-suffix.md)) do not see.
- A new remote timestamp field is read through the same helper; a direct parse call reintroduces the defect.
