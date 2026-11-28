# 0071: A Docker Engine string list item that is not a JSON string is left out

- **Status:** Accepted
- **Date:** 2026-11-28
- **Area:** Docker instances
- **Source:** Issue #575
- **Supersedes:** —

## Context

The image inspect answer of the Docker Engine carries four lists of strings (repo tags, repo digests, the environment
variables of its configuration, the root file-system layers), each image history entry one (its tags). The client read
every item with `JsonElement.GetString`, which answers null for a JSON `null` (dropped like a blank item) but throws for
a number, `true`/`false`, an object or an array. The error handler of the call then answered the whole inspect or
history *failed* with the reason `unexpected error`: one unreadable tag hid every other layer of the history and kept
derived runtime findings active ([Base images](../areas/base-images.md)), and one unreadable digest left all containers
of the image without a digest. The Engine sends strings only, so only a broken or hostile Engine (or a proxy in front
of it) sends another kind. [0069](0069-a-docker-engine-list-item-that-is-not-a-json-object-is-left-out.md) left this
case open; the planning of #572 found it.

## Options considered

1. **Leave the item out and read the others, in all five lists (chosen)** — what a `null` item already gets, and what
   discovery does for a container name that is not a string; one shared reader serves all five lists, so inspect and
   history follow one rule.
2. **Leave the item out in a history's tags only, keep failing the inspect** — the area document called such an
   inspect *failed*, but that described the code, it decided nothing; two rules for the same kind of list, and an
   unreadable environment variable would still hide the readable ones from base-image detection.
3. **Answer the call *failed* with a fixed message instead of `unexpected error`** — clearer log text, but one item
   still discards every readable value of the answer, the defect the issue reports.
4. **Read such a list as empty when any item is not a string** — no partial list, but it discards readable values for
   no gain: each item stands for itself (a tag, a digest, a variable, a layer).
5. **Leave the item out and log a warning for each** — needs a new log event for an input no real Engine sends, as
   [0069](0069-a-docker-engine-list-item-that-is-not-a-json-object-is-left-out.md) rejected for whole list items.

## Decision

An item of the repo tags, repo digests, environment variables or root file-system layers of an image inspect, or of the
tags of an image history entry, is read only when it is a JSON string; any other item is left out without a log entry
and the other items are still read. The rules are in [Docker instances](../areas/docker-instances.md#image-inspection-and-history).

## Consequences

- An inspect or history that failed only because of such an item now succeeds: discovery resolves the digest from the
  remaining repo digests (also as the only one left), and derived runtime findings are replaced by the runtime check
  instead of kept. An Engine able to send such items can send the list without them to the same effect today.
- An inspect answer that is not a JSON object, and a history that is not a JSON array, keep failing the call.
- The registry clients read the environment variables of an image configuration the same way and keep failing on such
  an item; that is a separate issue.
