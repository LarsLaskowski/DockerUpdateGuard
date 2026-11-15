# 0040: A scan item whose save fails is saved again alone as *failed*

- **Status:** Proposed
- **Date:** 2026-11-15
- **Area:** Scan engine
- **Source:** Issue #384
- **Supersedes:** —

## Context

The observed-image refresh and the runtime container refresh scan their items (observed images, Docker instances) one
after another through one scoped database context. An item's scan saves its scan run as *running* first and everything
else at the end; an error from either save left the batch's per-item error handler with all of the item's changes still
tracked. The next item's save then committed them: a run that never got past its first save was stored as *running* by
another item, and a change the database rejects (a constraint violation) failed every later item of the batch as well.
A run whose final save failed stayed *running* until the stale-run repair, while the vulnerability refresh already
retries its final save once over a cleared tracker ([0028](0028-a-failed-save-of-one-image-version-does-not-stop-the-vulnerability-refresh.md)).
The observed-image scan of one image is also called directly from the registration page, whose context lives as long as
the browser circuit, so the defect is not limited to a batch.

## Options considered

1. **On an error of either save (cancellation excepted), clear the change tracker, attach only the scan run (*added*
   when its first save failed, *modified* otherwise), mark it *failed* and save it once more; clear the tracker again
   when that save fails too; rethrow the original error. The batch handler also clears the tracker (chosen)** — the
   item's changes are gone before any later save, the run records the failure, and the caller still sees the item fail.
   Attaching the run by setting its entry state leaves its child collections, which change-tracking fix-up filled, out
   of the save.
2. **Only clear the tracker in the batch handler** — isolates later items, but a run whose final save failed stays
   *running* until the repair, one whose first save failed leaves no trace, and the registration page's direct scan
   keeps the defect.
3. **Discard pending changes entry by entry, as 0028 does** — needed there because later work is tracked; here nothing
   an item or the batch still uses is tracked (the image batch holds identifiers, the runtime batch reads only the
   instance's identifier and name), so a clear is simpler and leaves nothing behind.
4. **Re-attach the run with `Update` or `Add`** — both walk the run's child collections and would save the item's
   snapshots, findings and relationships after all, or fail on rows that were never inserted.
5. **A scope and context per item** — isolates fully, but changes how the orchestrators get their context, a
   composition-root concern, for a failure the clear handles.

## Decision

Option 1, in both orchestrators. The run's message and the rules of the recovery are described in
[Scan engine](../areas/scan-engine.md#limits-and-errors).

## Consequences

- A rejected change of one item no longer starves the rest of the batch, and no run is left *running* by a save failure
  unless the recovery save fails as well; then a stored run stays *running* until the stale-run repair, as before.
- A run saved by the recovery records no duration metric and refreshes no inventory gauges: the scan ended in an error.
- Cancellation still aborts the scan without a recovery save; the repair handles its run.
- The recovery save takes the scan's cancellation token, unlike the vulnerability refresh's retry: against an
  unreachable database it repeats the whole connection-retry sequence, which cancellation and host shutdown must be
  able to end. A cancelled recovery ends like a failed one, and the run is left to the stale-run repair.
- The clear also runs on the circuit-scoped context of the registration page (a direct scan of one observed image) and
  of the runtime containers page (a runtime batch), and detaches everything that context tracks, not only the scan's
  entities. It is safe only because no page or circuit service holds a tracked entity across calls; one that does
  needs its own context or must reload after a scan.
- Whether attaching the original run by its entry state keeps its child rows out of the recovery save is confirmed by
  the runtime test that no container snapshot of a failed run is stored; if it is not, the recovery attaches a new run
  with the same identifier and copied values instead. Outcome: to be recorded once the test has run.
