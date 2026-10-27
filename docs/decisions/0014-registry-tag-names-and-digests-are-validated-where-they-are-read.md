# 0014: Registry tag names and digests are validated where a client reads them

- **Status:** Proposed
- **Date:** 2026-10-27
- **Area:** Registries
- **Source:** Issue #348
- **Supersedes:** —

## Context

Tag names and digests come from the registry: the `name` and `digest` of a Docker Hub tag record, the names of an
OCI `tags/list` page and the `Docker-Content-Digest` header of a manifest response. The clients passed them on as
received, without a length or character check. From there they reach length-limited columns — tag columns of 200
characters, digest columns of 255, the serialized tag metadata of an image version (4000), finding summaries (500) and
assessment messages (2000) that quote a tag — and PostgreSQL rejects a value longer than its column, so one
over-long value fails the save of a whole scan (#348). The test databases (SQLite, EF Core InMemory) do not enforce
the lengths, so no test noticed.

The OCI distribution specification limits a tag to 128 characters of `[a-zA-Z0-9_][a-zA-Z0-9._-]{0,127}`, and the
image specification gives digests the grammar `algorithm ":" encoded`. Docker cannot pull a tag or digest outside
these grammars, so such a value is never a usable update target.

## Options considered

1. **Validate at the clients: a tag name outside the tag grammar is dropped from a listing, a digest outside the
   digest grammar or longer than 255 characters is treated as unknown; a Docker Hub single-tag lookup reports the tag
   it asked for** — every tag and digest leaving a registry client fits every column it reaches, and whatever is
   built from them (metadata, summaries, messages) stays bounded by construction. Chosen.
2. **A length bound only (128 / 255 characters)** — fits the columns too, but keeps names with spaces, line breaks or
   other characters Docker cannot pull, which then reach logs, pages and update recommendations; the grammar costs no
   more.
3. **Cut over-long tag names to the column length** — a cut name is a tag that does not exist, and it could be
   recommended as an update.
4. **Cut where the values are stored (each orchestrator, the metadata write)** — every store needs its own cut, a cut
   JSON document is no longer valid JSON, and a cut tag is wrong; the same reasoning as
   [0009](0009-integration-results-carry-fixed-failure-reasons.md), option 13.
5. **Validate once in the registry dispatcher** — one place, but Docker Hub account discovery calls the Docker Hub
   client directly, and the OCI client requests a manifest for every listed name before the dispatcher sees the
   result.
6. **Fail a Docker Hub single-tag lookup whose record carries an invalid name** — adds a failure for a record that
   answers the question asked; the OCI client already reports the tag it asked for.
7. **Stop writing the tag metadata, which nothing reads** — removing unused data-model values is
   [#293](https://github.com/LarsLaskowski/DockerUpdateGuard/issues/293).

## Decision

The registry clients accept a tag name only when it matches the tag grammar of the OCI distribution specification and
a digest only when it matches the digest grammar of the OCI image specification and has at most 255 characters; a
listed tag with another name is skipped, another digest is unknown, and a lookup for another requested tag finds
nothing. The rules are in
[Registries](../areas/registries.md#what-can-be-read).

## Consequences

- A registry that publishes a tag Docker cannot pull no longer shows it as a candidate or a recommendation, and the
  skip is not logged, so an operator does not learn of it.
- A tag whose digest is invalid is listed without a digest and therefore never becomes a stored tag candidate.
- A single-tag lookup for a requested tag outside the tag grammar is answered *not found* without a request, so a tag
  taken from a base-image label, which is parsed unchecked, no longer reaches the tag metadata or a manifest URL.
- Values that reach the catalog outside the tag data — the other parts of base-image label references, repository
  names of the Docker Hub account listing, Docker Engine container names and labels — are not covered and are tracked
  in [#355](https://github.com/LarsLaskowski/DockerUpdateGuard/issues/355). Vulnerability advisory fields are handled by
  [#271](https://github.com/LarsLaskowski/DockerUpdateGuard/issues/271).