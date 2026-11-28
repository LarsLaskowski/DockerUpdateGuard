# 0067: An image index whose chosen entry is again an index answers *failed*

- **Status:** Accepted
- **Date:** 2026-11-28
- **Area:** Registries
- **Source:** Issue #550
- **Supersedes:** —

## Context

For a tag or image configuration lookup at a registry other than Docker Hub, the OCI client requests the manifest of
the tag; when the answer is a multi-platform index (its content type, which the registry chooses), it selects one entry
([0062](0062-both-registry-clients-choose-a-platform-manifest-by-one-shared-rule.md)) and requests that entry with the
same code path. When that answer is again an index, the client selects again and requests again, with no limit. A
hostile or broken registry can therefore make one lookup — and a tag listing, which looks up every inspected tag —
send an unbounded number of manifest requests, each bounded only by the request deadline
([0039](0039-a-registry-request-and-the-read-of-its-response-body-share-one-deadline.md)), while every level keeps its
response and parsed document open. The OCI image specification allows an index to name another index, but the images
the clients read (runnable images of one platform per entry) do not use it; the Docker Hub client already asks for the
chosen entry as an image manifest only and follows nothing further. The security review of #277 found the defect.

## Options considered

1. **Follow one index level: a chosen entry answered with an index content type ends the lookup *failed* with a
   fixed message, before its body is read** — a lookup sends at most two manifest requests (plus the token flow);
   simple to state and test. Loses images that really nest indexes, which the clients cannot use today anyway. Chosen.
2. **A depth limit above one (two or three levels)** — follows a nesting no supported image uses, and multiplies the
   requests a hostile registry gets per inspected tag in a listing, for no known case.
3. **Detect a repeated digest along the chain** — stops a loop, not an endless chain of distinct digests.
4. **Ask for the entry as an image manifest only (Docker Hub's request), without checking the answer** — the registry
   chooses the content type all the same; an index answered anyway would still recurse.
5. **Rely on the caller's cancellation or a per-lookup time budget** — bounds the duration, not the requests; a fast
   registry still gets an unbounded number of them.
6. **Align the Docker Hub client to answer *failed* the same way** — its path is already bounded at two manifest
   requests; it would change its outcome for such an entry from *not found* to *failed* without bounding anything more.

## Decision

The OCI client follows one index level: when the manifest of the chosen index entry is answered with an index content
type, the tag or image configuration lookup ends *failed* with a fixed message, without reading that answer or
requesting anything it names. The Docker Hub client stays as it is. The message and outcomes are in
[Registries](../areas/registries.md#what-can-be-read).

## Consequences

- A tag or configuration lookup at another registry sends a bounded number of manifest requests: the index and one
  chosen entry; a tag listing at most that per inspected tag.
- An image whose index nests another index is no longer resolved: its tag lookup is *failed* (a listing lists the tag
  by name only) and its configuration lookup is *failed*. Revisit, with a depth limit (option 2), if a real registry
  serves such images for runnable tags.
- An index served under a non-index content type is still read as an image manifest; it names no configuration, so the
  lookup ends there.
