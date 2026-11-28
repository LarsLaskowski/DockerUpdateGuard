# 0069: A Docker Engine list item that is not a JSON object is left out

- **Status:** Accepted
- **Date:** 2026-11-28
- **Area:** Docker instances
- **Source:** Issue #572
- **Supersedes:** —

## Context

Discovery and resource sampling read the Engine's container list, the image history reads the list of history entries;
each item of these JSON arrays describes one container or one layer. The client read every item as a JSON object, so an
item of another kind (a string, a number, `true`/`false`, `null`, an array) made the reader throw, and the error handler
of the call turned the whole discovery, resource round or history into *failed* with the reason `unexpected error`. One
unreadable item thereby hid every readable container of the instance, failed its scan run, and kept stale derived
runtime findings. The Engine sends objects only, so only a broken or hostile Engine (or a proxy in front of it) sends
another kind. The planning of #570 found the defect.

## Options considered

1. **Leave the item out and read the others (chosen)** — the rule a container with an over-long ID already follows in
   both container lists, and that the Docker Hub client follows for a platform image entry that is not an object; an
   item that is no object carries no container or layer to record, so nothing readable is lost.
2. **Answer the whole call *failed* with a fixed message instead of `unexpected error`** — clearer log text, but one
   unreadable item still discards every readable container of the instance, the defect the issue reports.
3. **Leave the item out and log a warning for each** — would name the broken Engine, but needs a new log event for an
   input no real Engine sends; an over-long ID is left out without a log entry as well.
4. **Make the shared string reader answer null for an element that is not an object** — fixes only resource
   sampling: the history reads its creation time, and discovery its labels, before or beside any string, and the stats body
   and the inspect answer rely on a non-object root still failing ([0026](0026-a-failed-stats-request-drops-only-its-container-from-a-resource-round.md), the inspect failure rule of
   [Docker instances](../areas/docker-instances.md#container-discovery)).

## Decision

Each item of the container list of discovery and of resource sampling, and each entry of an image history, is read only
when it is a JSON object; any other item is left out without a request and without a log entry, and the other items are
still read. The rules are in [Docker instances](../areas/docker-instances.md).

## Consequences

- A list that holds only such items answers like an empty list: discovery succeeds without containers, a resource round
  samples no container and succeeds with count 0, and a history succeeds without entries, after which derived runtime
  findings that rested on history text can be deactivated instead of kept. An Engine able to send such items can send
  an empty list to the same effect today.
- A root that is not a JSON array, and a value of an unexpected kind inside an object (a number among a history entry's
  tags, say), keep failing the call; this record does not decide them.
- Revisit, with a warning per left-out item, if operators need to find an Engine that sends such items.
