# 0014: Remote tag names, digests, image references and names are validated or bounded where they are read

- **Status:** Accepted
- **Date:** 2026-10-28
- **Area:** Registries
- **Source:** Issue #348, issue #355, issue #360, issue #364
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

Other remote values reached the same kind of columns unchecked (#355): the image reference of a base-image label
(`org.opencontainers.image.base.name`, parsed into registry (200), repository (300), tag and digest, and quoted as the
source reference of a relationship (500)), the `namespace/name` of the Docker Hub account listing (catalog repository
and the observed image's name, 200), and from the Docker Engine the container ID, name and compose, stack and service
labels (200 each, also in resource samples) and the container's image reference and digest. A base-image label the
reference parser rejects even failed the whole observed-image scan. Docker's reference grammar (`distribution/reference`)
gives registry hosts the form of DNS names or bracketed IPv6 addresses with an optional port, and repository paths
lowercase components joined by `.`, `_`, `__` or dashes, at most 255 characters without the host. The parser kept
`.` and `..` path segments, and the clients' path escaping leaves `.` alone, so `System.Uri` collapsed such a segment into
another path of the registry host; the grammar admits no such segment. Container IDs have no
grammar the application relies on; names and labels are display text.

The request builders of both clients (#360) put every repository segment, tag, digest and reference through
`Uri.EscapeDataString`, which leaves `.` alone, and `System.Uri` removes a `.` segment and resolves a `..` segment, so
`corp/../../../v2/_catalog` addressed `/v2/_catalog/…` of the registry host. A reference the user registers is not held
to the grammar, and the digests a manifest names (a manifest-list entry, the configuration blob) reach a request path
unchecked. The Docker Scout provider puts the namespace, name and tag of a registered `docker.io` reference into its
request path without any escaping, with the Docker Hub token attached, so `..` steered it to another path of the
Scout host and a `?` or `#` in the tag added a query or fragment. Measured on .NET 10: `%2E` and `%2E%2E` in a path are unescaped and removed the same way, and after the
escaping only a segment that is exactly `.` or `..` is affected (`...`, `.%20`, a percent sign, escaped as `%25`, and a
backslash, escaped as `%5C`, stay as they are).

The registry part of a registered reference is the first segment when it contains a `.` or a `:` or is `localhost`, with
no further check, so `.`, `..`, `a.`, `evil.com?x`, `user:pw` or `mailto:x` become the registry (#364). The OCI client
took a registry that `Uri.TryCreate` accepts as an absolute URI as a base URL and built every other one as the host of a
`UriBuilder`. Measured on .NET 10: `.`, `..`, `.a`, `a..b`, `http:evil.com`, `a b.c` and `127.0.0.1:5000` make
`UriBuilder.Uri` throw `UriFormatException`, which escaped the lookups and failed the scan as an unexpected error; no
request was sent and nothing resolved relatively. `a.`, `-a.com` and `a_b.com` were requested as hosts; `evil.com?x` and
`evil.com#x` were requested from `evil.com` with the request path replaced by `/` and the rest moved into the query or
fragment. Every registry of a host name with a port (`localhost:5000`, `registry.example.com:5000`) parses as an absolute URI whose scheme is the host name, so `SocketsHttpHandler` refused it with `NotSupportedException`,
and the tag listing normalized it to an empty host. `https://<registry>/` parsed as a URI keeps host and port for every
registry of the grammar and rejects a port above 65535 and a bracketed value that is no IPv6 address; it accepts port
0, to which no connection can be made. Before the OCI client was even chosen, the Docker Hub client's host check, which
both clients' `CanHandle` use, threw `ArgumentException` for a value that parses as an absolute URI without a host
(`localhost:5000`, `user:pw`), because it compared the empty host.

## Options considered

1. **Validate at the clients: a tag name outside the tag grammar is dropped from a listing, a digest outside the
   digest grammar or longer than 255 characters is treated as unknown; a Docker Hub single-tag lookup reports the tag
   it asked for** — every tag and digest leaving a registry client fits every column it reaches, and whatever is
   built from them (metadata, summaries, messages) stays bounded by construction. Chosen.
2. **A length bound only (128 / 255 characters)** — fits the columns too, but keeps names with spaces, line breaks or
   other characters Docker cannot pull, which then reach logs, pages and update recommendations; the grammar costs no
   more. The same holds for registries and repositories.
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
8. **For image references read from a remote side (base-image labels, Docker Engine containers): hold registry,
   repository and tag to Docker's reference grammar with the stored limits (registry 200, repository 255, tag 128) where
   the reference is parsed; reject the reference otherwise, and treat an invalid digest as unknown** — the parts fit
   their columns and name an image Docker could pull. Chosen. A base-image label that fails ends the chain at that depth;
   a container whose reference fails gets no snapshot and is named in the run's error message.
9. **For display names (container name, the three labels, a discovered image's name) and the relationship's source
   reference: cut to the column length with a `…` marker** — they name nothing a request uses, and a cut keeps the
   container or image visible. Chosen.
10. **For container IDs: leave a container out when its ID is longer than its column** — an ID is a key; a cut one
    addresses no container and can collide. Holding IDs to Docker's 64 hexadecimal characters would exclude other
    engines for no storage benefit. Chosen.
11. **Widen the source-reference column with a migration** — fits every reference the catalog can hold, but changes the
    schema for a display and grouping value that exceeds 500 only when its parts are near their limits at once (a valid
    reference can reach about 657 characters); the cut is deterministic, so grouping by source reference is unchanged.
12. **Drop a container with an invalid reference in the Docker Engine client** — the client does not parse references,
    and a container that vanishes silently hides what the run's error message now names.
13. **Skip a Docker Hub repository whose name exceeds the observed image's name column** — a pullable repository would
    disappear from the user's own images; discovery matches by catalog repository, not by name.
14. **Refuse a request path with a `.` or `..` segment in the request builders, answered as a 404 of that request
    would be, without sending it** — one check where each path is composed covers user-entered references and the
    digests read from manifests alike, and a new lookup gets it by construction; the Docker Scout provider, which sends
    the same registered reference, refuses the same way before it logs in and escapes each segment like the registry
    clients. Chosen.
15. **Encode the dots as `%2E`** — `System.Uri` unescapes `%2E` and removes the segment all the same (measured), and a
    server or proxy normalizes such a path as well (RFC 3986, *Remove Dot Segments*).
16. **Turn off the path canonicalization (`UriCreationOptions.DangerousDisablePathAndQueryCanonicalization`)** — the
    segment then reaches the registry, or a proxy in front of it, which resolves it to another path.
17. **Check the reference at each public lookup** — nine entry points, a new one can miss the check, and the digests a
    manifest names never pass an entry point.
18. **Hold a user-entered reference to the full reference grammar** — closes more than the defect: it changes what a
    user can register, which [0009](0009-integration-results-carry-fixed-failure-reasons.md) and this record leave to
    plain parsing.
19. **Check the configured Docker Hub user name, the path segment of the account listing, here as well** — a
    configured value is checked at startup with the other options ([Hosting](../areas/hosting.md)), so it is the follow-up
    [#363](https://github.com/LarsLaskowski/DockerUpdateGuard/issues/363).
20. **Build the OCI request URI only from a registry that matches the registry grammar within its stored limit and
    parses as `https://<registry>/`, refusing any other registry where the URI is composed, answered as a 404 would be,
    without sending a request** — the same place and outcome as option 14: it covers references registered before the
    change, every lookup and the base-image chain, and a new lookup gets it by construction. Chosen.
21. **Reject the reference at registration** — leaves the references already in the catalog, which is never cleaned,
    and changes what a user can register, which option 18 leaves to plain parsing; checking the registration form
    against the stored rules is [#343](https://github.com/LarsLaskowski/DockerUpdateGuard/issues/343).
22. **Let the OCI client decline such a registry in `CanHandle`, so the lookup is *unsupported*** — one check, but the
    dispatcher's message repeats the raw registry (control characters included) in scan messages, and the base-image
    chain, which calls the client's own lookups, would still reach the URI builder.
23. **Keep the absolute-URL form of a registry for `http` and `https` URLs** — no source produces it: the parser splits
    at `/`, remote references are held to the grammar, Docker Hub URLs go to the Docker Hub client; the form only kept a
    way to plain-HTTP requests and is what broke registries with a port.
24. **Catch `UriFormatException` and answer *failed*** — leaves `a.`, `evil.com?x` and the non-`http` schemes, which do
    not throw, and keeps registries with a port broken.

## Decision

The registry clients accept a tag name only when it matches the tag grammar of the OCI distribution specification and
a digest only when it matches the digest grammar of the OCI image specification and has at most 255 characters; a
listed tag with another name is skipped, another digest is unknown, and a lookup for another requested tag finds
nothing. Image references read from a base-image label or a Docker Engine container, and repository names of the Docker
Hub account listing, are accepted only when they match Docker's reference grammar within the stored limits; display
names and the source reference are cut to their columns, a container whose ID exceeds its column is left out, and
the request builders of both registry clients and of the Docker Scout provider refuse a path with a segment that is
exactly `.` or `..`, answered as a 404 of that request would be, without sending it; Docker Scout escapes each segment.
The OCI client addresses only a registry that is a host with an optional port from 1 to 65535 by that grammar and
refuses any other registry the same way; the Docker Hub host check compares a value that has no URI host as a plain
host. The rules are in
[Registries](../areas/registries.md#what-can-be-read) and [its request paths](../areas/registries.md#request-paths),
[Image catalog](../areas/image-catalog.md#image-references), [Vulnerabilities](../areas/vulnerabilities.md#docker-scout) and
[Docker instances](../areas/docker-instances.md).

## Consequences

- A registry that publishes a tag Docker cannot pull no longer shows it as a candidate or a recommendation, and the
  skip is not logged, so an operator does not learn of it.
- A tag whose digest is invalid is listed without a digest and therefore never becomes a stored tag candidate.
- A single-tag lookup for a requested tag outside the tag grammar is answered *not found* without a request.
- A base-image label that names no valid reference ends the chain without a log entry, so its base images are not shown;
  a label the parser rejects no longer fails the scan.
- A registry host longer than 200 characters, legal in DNS up to 253, is not accepted.
- A container whose reference Docker would refuse is no longer recorded; its findings stay as they were, and the run
  ends *partial* naming it, without a log entry, as one whose reference cannot be parsed. A container with an over-long ID is not recorded and not sampled, without a message.
- Cut names no longer match the remote value: a lookup by the stored container name (none exists today) would miss a
  container whose name was cut; two names equal in their first 199 characters read alike on the pages.
- No reference, remote or entered by the user, and no digest a manifest names can steer a registry, Docker Hub or
  Docker Scout request to another path of the host, and a tag no longer reaches Scout's query. A registered reference with such a segment is answered *not found* without a
  log entry, so its scan reports it like an image the registry does not have. The configured Docker Hub user name
  still reaches the path of the account listing unchecked ([#363](https://github.com/LarsLaskowski/DockerUpdateGuard/issues/363)); the Docker Engine requests are not
  covered: their container and image IDs come from the configured endpoint.
- A registered reference whose registry is not a host with an optional port is answered *not found* without a request
  or log entry instead of failing its scan with an unexpected error; it can still be registered and stays in the
  catalog. Registries with a port (`localhost:5000`) are reached for the first time. A registry given to the OCI client
  as an absolute URL is no longer accepted, and port 0 is refused. Choosing the client no longer throws for a registry
  with a port; which hosts the Docker Hub client serves is unchanged.
- Vulnerability advisory fields are handled by [#271](https://github.com/LarsLaskowski/DockerUpdateGuard/issues/271);
  references entered by a user at registration are not remote and keep the behavior of
  [0009](0009-integration-results-carry-fixed-failure-reasons.md).