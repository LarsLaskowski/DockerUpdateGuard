# 0059: A detail page shows a missing record or a failed load inline

- **Status:** Accepted
- **Date:** 2026-11-24
- **Area:** Web UI
- **Source:** Issues #40, #539
- **Supersedes:** —

## Context

The detail pages of Docker instances, own images, observed images and runtime containers rendered their loading
skeleton whenever they held no detail. The view service answers no detail for an identifier that does not exist (a
stale bookmark, a record removed by cleanup), so such a page showed the skeleton forever. Their initial load had no
error handling either: an exception of the view service ended the server prerender on the error page or ended the
interactive connection, while the user actions of the same pages report failures inline with a fixed reason
([decision 0009](0009-integration-results-carry-fixed-failure-reasons.md)). The router has a generic not-found page.
The list pages, the dashboard and the layout around every page (top bar summary, navigation) load their data the
same way, without error handling either, so a database outage still ended on the error page (#539).

## Options considered

1. **Loading, not found and failed load as three states inside the page** — the page keeps its header, names what is
   missing and links back to its list; a failed load shows a fixed-reason alert like the user actions. One shared
   helper guards the load, so the four pages carry no copies of the error handling.
2. **The router's not-found page through `NavigationManager.NotFound()`** — answers HTTP 404 on the prerender, but
   replaces the page with a generic "page not found" for a valid route, and its interplay with the prerendered state
   was not measured.
3. **An error boundary around the page body** — layout-wide, changes how other pages fail, and cannot name the page's
   load in a fixed message.
4. **The error handling inside each page's load method** — the reload after a runtime container's user action would
   then report under the load's message instead of the action's.

## Decision

A detail page shows a loading skeleton only while its load is pending; a load without a record shows a not-found note
with a link to the page's list, and a failed load shows an error alert with a fixed reason and is logged like a failed
user action. A list page or the dashboard whose first load fails shows the same alert in place of its skeleton, through
the same helper, until a later load of the page succeeds; the layout logs a failed load through it and leaves out
the values it could not load. The texts are in
[Web UI](../areas/web-ui.md#limits-and-errors).

## Consequences

- A stale link explains itself instead of looking stuck; load failures are visible and logged without the error page.
- A missing detail still answers HTTP 200; a 404 would need option 2.
- A user action's reload keeps its own error message; only the page's own load uses the load message.
- A failed first load persists nothing for the interactive render, which loads again: a failure on both renders is
  logged twice.
- The layout shows no alert of its own: the page's alert reports an outage once, and the top bar and the menu simply
  lack what they could not load.
