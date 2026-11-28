# 0070: Discovery leaves out a Docker Engine container without an ID

- **Status:** Proposed
- **Date:** 2026-11-28
- **Area:** Docker instances
- **Source:** Issue #574
- **Supersedes:** —

## Context

Discovery and resource sampling read the same container list of the Docker Engine. Resource sampling leaves out a
container whose `Id` is missing, not a string, empty or blank; discovery left out only an ID longer than the stored 200
characters and recorded every other container with the ID `""` (or blank), named by that empty ID when `Names` held no
name. Such a snapshot cannot be addressed: the detail route has no empty segment, the container key trims the ID, so two
such containers of one instance merge into one, and a container action needs a 64-character ID. The Engine always sends an
ID, so only a broken or hostile Engine (or a proxy in front of it) sends a container without one. The planning of #572
found the difference ([0069](0069-a-docker-engine-list-item-that-is-not-a-json-object-is-left-out.md) decided list items
that are no JSON object).

## Options considered

1. **Leave the container out of discovery, with the rule sampling applies (chosen)** — one rule for both readers of the
   list, held by one helper; nothing addressable is lost, since the record could not be opened, compared or acted on.
2. **Keep recording it with an empty ID** — the container stays visible in the lists, but as a record no page, action or
   resource history can reach, and two of them collapse into one.
3. **Give it a substitute ID (its name, a generated value)** — makes it addressable, but an invented ID can collide with a
   real container's ID, and container actions address the Engine by the snapshot's ID.
4. **Fail the whole discovery** — one broken item would hide every readable container of the instance, the defect
   [0069](0069-a-docker-engine-list-item-that-is-not-a-json-object-is-left-out.md) removed for non-object items.
5. **Leave it out and log a warning** — would name the broken Engine, but needs a new log event for an input no real
   Engine sends; an over-long ID and a non-object item are left out without a log entry as well.

## Decision

Discovery reads a container of the Engine's list only when it has a storable ID, by the same rule and helper resource
sampling uses; any other container is left out without a log entry and the others are still read. The rules are in
[Docker instances](../areas/docker-instances.md#container-discovery).

## Consequences

- A list holding only such containers answers like an empty list: discovery succeeds without containers and inspects
  none of their images.
- A container recorded with an empty ID by an earlier version is no longer current after the next runtime scan; its old
  snapshots stay until retention removes them.
- A discovered container's name falls back to a non-blank ID, so it is never empty.
- Revisit, with a warning per left-out container, if operators need to find an Engine that sends such items.