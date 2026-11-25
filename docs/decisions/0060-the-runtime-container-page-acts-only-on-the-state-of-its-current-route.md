# 0060: The runtime container page acts only on the state of its current route

- **Status:** Proposed
- **Date:** 2026-11-25
- **Area:** Web UI
- **Source:** Issue #540
- **Supersedes:** —

## Context

Blazor reuses the runtime container page when the route moves from one container to another (browser back and
forward, a link between containers). The page kept the previous container's detail and action availability until
the new load completed, while its actions used the new route's identifiers, so a confirmation could name one container
and act on another. Its loads are not serialized either: an answer for the earlier route that arrives late overwrites
the current one, and a confirmation dialog opened before the move still executes after it. The other three detail
pages keep the previous detail shown during a reload ([decision 0059](0059-a-detail-page-shows-a-missing-record-or-a-failed-load-inline.md)),
but offer no action on it.

## Options considered

1. **Clear the page's state on a route change, drop late answers of an earlier route, and send nothing for a
   confirmation given after the move** — every piece of state the dialog names and every request it sends belong to
   the route shown; a user who confirmed just before navigating has to confirm again.
2. **Only clear the detail and availability before the new load** (the issue's smallest fix) — closes the window
   while the load is pending, but a late answer of the earlier route and a dialog open across the move still pair
   one container's name with another's identifiers.
3. **Execute with the identifiers the dialog named** — acts on what the user confirmed, but on a container the page
   no longer shows, with its outcome landing on another container's page.
4. **The same clearing and late-answer guard in the shared load helper for all four detail pages** — consistent,
   but changes three pages without actions for a cosmetic gain; left to a follow-up.

## Decision

Option 1: on a change of Docker instance or container identifier the runtime container page drops its detail,
action availability, action outcome and error at once; it keeps the answer of a load, availability check or action
only while the route it was requested for is still current; and it sends no action whose confirmation ended after
the route changed. The resulting behavior is in [Web UI](../areas/web-ui.md#container-actions-section).

## Consequences

- A confirmation always names the container it acts on; the server's checks at confirmation stay the final word on
  the target ([Container actions](../areas/container-actions.md#actions)).
- The runtime container page now shows its skeleton during a route change, unlike the other detail pages, which keep
  the previous detail until the new load completes.
- A late load of an earlier route still sets the outcome of the shared load helper: a failure shows the load alert
  and drops the current detail, a success hides the current route's load alert and leaves the skeleton up. Neither
  shows actions; guarding the shared helper against late answers is the follow-up of option 4.
- An action that runs while the user moves away keeps the new container's buttons disabled, without progress, until
  it ends; its run is in its own container's history.
- The action is sent with the identifiers of the detail the dialog names, and only while they are the route's.
