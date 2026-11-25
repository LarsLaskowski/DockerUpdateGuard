# 0060: A detail page acts only on the state of its current route

- **Status:** Accepted
- **Date:** 2026-11-25
- **Area:** Web UI
- **Source:** Issues #540, #544
- **Supersedes:** —

## Context

Blazor reuses a detail page when the route moves from one record to another (browser back and forward, a link between
containers or images). The runtime container page kept the previous container's detail and action availability until
the new load completed, while its actions used the new route's identifiers, so a confirmation could name one container
and act on another. Its loads are not serialized either: an answer for the earlier route that arrives late overwrites
the current one, and a confirmation dialog opened before the move still executes after it.

The other three detail pages (Docker instance, own image, observed image) offer no action, but kept the previous
record shown under the new route until its load completed, and a late successful answer of the earlier route replaced
the current record. The shared load helper of all four pages
([decision 0059](0059-a-detail-page-shows-a-missing-record-or-a-failed-load-inline.md)) applied the outcome of a late
load unconditionally: a late failure showed the load alert and dropped the current detail (on the runtime container
page with its actions), a late success hid the alert of the current route's failed load and left the skeleton up
(#544).

## Options considered

1. **Clear the page's state on a route change, drop late answers of an earlier route, and send nothing for a
   confirmation given after the move** — every piece of state the dialog names and every request it sends belong to
   the route shown; a user who confirmed just before navigating has to confirm again.
2. **Only clear the detail and availability before the new load** (the smallest fix of #540) — closes the window
   while the load is pending, but a late answer of the earlier route and a dialog open across the move still pair
   one container's name with another's identifiers.
3. **Execute with the identifiers the dialog named** — acts on what the user confirmed, but on a container the page
   no longer shows, with its outcome landing on another container's page.
4. **The same clearing and late-answer guard in the shared load helper for all four detail pages, keyed by the
   route** — one place decides whether a load's answer still belongs to the route shown; first left to a follow-up
   (#540), taken with #544.
5. **A load sequence number in the helper instead of the route key** — would also order two loads of the same route,
   but those answers describe the same record, and the route key is what the runtime container page already compares
   for its availability checks and actions.
6. **The route comparison and the clearing as a copy in each page** — no change to the helper, but four copies of the
   same comparison, and the helper would still apply a late failure.

## Decision

Options 1 and 4: every detail page records the route of each parameter set in the shared load helper and drops its
detail at once when the route identifier changes (the runtime container page also its action availability, action
outcome and error); the helper keeps the answer of a load — record, missing record or failure — only while the route
it was requested for is still current. The runtime container page also keeps the answer of an availability check or
action only for its current route, and sends no action whose confirmation ended after the route changed. The
resulting behavior is in [Web UI](../areas/web-ui.md#general) and its
[container actions section](../areas/web-ui.md#container-actions-section).

## Consequences

- A confirmation always names the container it acts on; the server's checks at confirmation stay the final word on
  the target ([Container actions](../areas/container-actions.md#actions)).
- Every detail page shows its skeleton during a route change, also after its first render reused the prerendered
  record; a re-render with the same route keeps the shown record until its reload completes.
- A late load of an earlier route changes nothing shown; its failure is still logged.
- Two loads of the same route are not ordered: the answer that ends last stands, as both describe the shown record.
- A page that never records its route keeps no load answer at all, so a new detail page wires the helper completely
  or shows only its skeleton.
- An action that runs while the user moves away keeps the new container's buttons disabled, without progress, until
  it ends; its run is in its own container's history.
- The action is sent with the identifiers of the detail the dialog names, and only while they are the route's.