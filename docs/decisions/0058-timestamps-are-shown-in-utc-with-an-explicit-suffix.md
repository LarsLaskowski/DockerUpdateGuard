# 0058: Timestamps are shown in UTC with an explicit suffix

- **Status:** Accepted
- **Date:** 2026-11-23
- **Area:** Web UI
- **Source:** Issue #36
- **Supersedes:** —

## Context

Every page formatted its timestamps itself with `ToLocalTime()` and the short `g` pattern. The components run on the
server (Interactive Server), so the time was converted to the time zone of the server process and formatted in its
culture, without any zone indicator: a user in another zone than the server read wrong times, and nothing on the page
said which zone was meant. The stored values are UTC. The compose files set no time zone, so what an operator sees
depends on whether the container's zone was configured by hand.

## Options considered

1. **UTC with an explicit suffix in one fixed, culture-independent format, from one shared formatter** — the same
   text for every user and every server, deterministic in tests; users read UTC instead of their local time.
2. **Conversion to the browser's time zone** — the best reading experience, but it needs a browser script (no local
   test runner, a coverage exclusion), the zone arrives only once the page is interactive, so the server-rendered first
   paint shows another time and every page re-renders; converting by zone name needs time-zone data the image may not
   carry, and a single offset is wrong across daylight-saving changes for older timestamps.
3. **The server's time zone with an explicit offset** — honest, but keeps the server-dependent value the issue calls
   wrong for remote users, and tests depend on the machine's zone.
4. **A configured display time zone** — a new option and time-zone data in the image, still one zone for every user.
5. **The server culture's short pattern plus a `UTC` suffix** — the date order still follows the server culture
   (month first in the container), ambiguous for many readers, and tests vary with the machine's culture.

## Decision

All user-visible timestamps of the UI are formatted by one shared formatter, `TimestampFormatter`, as UTC with the
suffix `UTC` in a fixed format independent of the server's zone and culture; the resource charts label their ends the
same way. The formats are stated in [Web UI](../areas/web-ui.md#general).

## Consequences

- Every user sees the same, unambiguous time; no page depends on the server's zone or culture any more.
- An operator who had set the container's zone to their own sees UTC from now on.
- A later conversion to the browser's zone changes the formatter and adds the browser plumbing, not every page.
