# Plan: Nested image indexes are followed without a depth limit

Source: Issue #550
Tier: standard — a request bound on untrusted registry input (input handling, not a guard over what an attacker may reach), which changes the outcome of OCI lookups for a nested index.
Security areas touched: Registry and Docker Scout calls (`Images/OciRegistryClient.cs`: untrusted manifest answers)

## Problem / root cause

`OciRegistryClient.GetManifestMetadataAsync` (`src/DockerUpdateGuard/Images/OciRegistryClient.cs:760`) answers an
index content type (`IsManifestListContentType`, line 242, checked at line 798) by calling
`GetConfigDigestFromManifestListAsync` (line 829), which selects an entry and calls `GetManifestMetadataAsync` again
for its digest (lines 849–851) with the same handling. Nothing counts the levels, so every index answer to an entry
request starts one more request. Each level also keeps its response stream and parsed `JsonDocument` open until the
whole chain returns (the `await using`/`using` scope at lines 794–806 encloses the recursive call).

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | The OCI client recurses on every index answer, without limit | `OciRegistryClient.cs:798–806`, `849–851`; no depth or counter in either method | confirmed |
| 2 | Each request is bounded only by the request deadline; no bound covers the lookup | `RequestDeadlineHandler` per request ([0039](../../docs/decisions/0039-a-registry-request-and-the-read-of-its-response-body-share-one-deadline.md)); `ExecuteAsync` (line 1083) has no budget of its own | confirmed |
| 3 | The recursion is reached from the tag lookup (line 1366), the image configuration lookup (line 1238) and, per inspected tag, the tag listing (line 1060) | the three call sites | confirmed |
| 4 | Which answer recurses is decided only by the response's content type: `Content.Headers.ContentType?.MediaType` (line 791) compared case-insensitively with the Docker manifest-list and the OCI index media types | lines 242–250, 791 | confirmed |
| 5 | Docker Hub's manifest-list path does not recurse: it asks for the chosen entry with the two image-manifest media types only, does not look at the answer's content type, reads `config.digest` from it and returns null otherwise; the caller then answers *not found* "Config blob digest could not be resolved for '<reference>'" | `DockerHubClient.cs:627–680` (Accept at 655–656, read at 673–676), caller at 1508–1513 | confirmed |
| 6 | Docker Hub reaches manifests only through the image configuration lookup; its tag lookups use the web API | `ManifestsResource` used only at `DockerHubClient.cs:645` and `1489` | confirmed |
| 7 | A failed manifest result reaches the caller with its own message and status *failed* | `PropagateFailure` keeps `Message` and maps *failed* to *failed* (`ExternalOperationResult{T}.cs:53–62`); test `OciRegistryClientCallOnlyUnknownPlatformsReturnsFailedAsync` sees the inner message for Tag and Configuration | confirmed |
| 8 | No existing test answers an entry's digest request with an index, so no test relies on the recursion | the index responses in `OciRegistryClientTests.cs` and `OciRegistryClientPublicationTimeTests.cs` are all on tag URLs (`.../manifests/latest`, `/1.0`) | confirmed |
| 9 | The nested call passes no platform, so a second-level selection ignored the caller's platform (moot once nesting fails) | lines 849–851 pass neither `operatingSystem` nor `architecture` | confirmed |

## Acceptance criteria

- AC1: When the OCI tag lookup's chosen index entry is answered (2xx) with an index content type — the Docker manifest
  list or the OCI index media type, in any letter case, with or without media-type parameters — the lookup answers
  *failed* with exactly `Manifest list for '<reference>' named another manifest list instead of a platform manifest`
  (`<reference>` is `ImageReference.FullReference`, as in the sibling messages), and sends no request beyond the index
  and that entry: two requests in all.
- AC2: The OCI image configuration lookup answers the same case with the same status and message and sends no further
  request (no third manifest request, no blob request).
- AC3: A one-level index keeps working: the chosen entry answered with an image manifest gives the results of today
  (tag digest of the index, configuration of the entry).
- AC4: Docker Hub's image configuration lookup, when its chosen entry is answered with an index content type naming
  another digest, sends no request for that digest and answers *not found* with "Config blob digest could not be
  resolved for '<reference>'" — its behavior today, now pinned.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `OciRegistryClientCallNestedIndexReturnsFailedWithoutFurtherRequestAsync` — operation `Tag`, DataRows for the entry's content type: `application/vnd.oci.image.index.v1+json`, `application/vnd.docker.distribution.manifest.list.v2+json`, `APPLICATION/VND.OCI.IMAGE.INDEX.V1+JSON` and `application/vnd.oci.image.index.v1+json; charset=utf-8` | `src/Tests/DockerUpdateGuard.Tests/OciRegistryClientTests.cs` |
| AC2 | the same test, operation `Configuration`, the first two content types | `src/Tests/DockerUpdateGuard.Tests/OciRegistryClientTests.cs` |
| AC3 | existing, unchanged: `OciRegistryClientGetTagAsyncUsesRequestedArchitectureDigestAsync`, `OciRegistryClientGetImageConfigurationAsyncAttestationFirstSelectsLinuxManifestAsync`, the index tests of `OciRegistryClientPublicationTimeTests` | as named |
| AC4 | `DockerHubClientGetImageConfigurationAsyncNestedIndexEntryReturnsNotFoundWithoutFurtherRequestAsync` — passes on today's code, so deferred to step 6 | `src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs` |

Test setup for AC1/AC2 (one test, DataRows over operation × content type, `[TestCategory("AC1")]` and `("AC2")`): the
tag URL answers an index whose one linux entry is `sha256:nested`; `.../manifests/sha256%3Anested` answers 200 with the
row's content type and an index body naming `sha256:leaf`; `.../manifests/sha256%3Aleaf` answers an image manifest
with a config digest, and its blob answers a configuration, so today's code succeeds and the test fails on it. Asserts:
status, exact message, `handler.Requests` has two entries. The content type with a parameter or upper case is set
through `MediaTypeHeaderValue.Parse` on the content headers. AC4 likewise: the platform entry answered with the OCI
index content type and an index body (no `config`) naming `sha256:leaf`; no request to `.../sha256%3Aleaf`.

## Approach

In `GetManifestMetadataAsync` add a parameter `bool isIndexEntry` before `cancellationToken` (CA1068: a required
parameter may not follow the token; six parameters, within S107). The two outer call sites
(`GetImageConfigurationCoreAsync`, line 1238; `GetTagAsync`, line 1366, which the listing also goes through) pass
`false`; `GetConfigDigestFromManifestListAsync` (line 849) passes `true`. After
the two status checks and after `contentType` is read, but before `ReadAsStreamAsync`: when `isIndexEntry` and
`IsManifestListContentType(contentType)`, return
`ExternalOperationResult.Failed<ManifestMetadata>($"Manifest list for '{imageReference.FullReference}' named another manifest list instead of a platform manifest")`.
The guard reuses the predicate that triggers the recursion on the same value, so every content-type form that recursed
before is caught (fact 4); the body of the nested answer is never read, and the response is disposed by its `using`.
A 404 or non-success answer for the entry keeps its present outcome (the status checks come first). No log entry, like
the sibling index failures. `DockerHubClient` is not changed (facts 5, 6; option 6 of the record).

Request bound and its seam: a lookup sends at most two manifest requests plus the token flow of each; the test
observes it through `SequenceHttpMessageHandler.Requests`.

Early draft PR: no

## Options considered

The options of [0067](../../docs/decisions/0067-an-image-index-whose-chosen-entry-is-again-an-index-answers-failed.md):
a depth limit above one (no supported image nests; multiplies the requests per listed tag), repeated-digest
detection (does not stop a chain of distinct digests), image-manifest-only Accept without checking the answer (the
registry still chooses the content type), a time budget (bounds duration, not requests), and aligning Docker Hub to
*failed* (already bounded; changes its outcome for nothing). An `int` depth with a constant instead of the `bool`:
equal at a limit of one, with a counter and a constant no second value needs — lost.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Images/OciRegistryClient.cs` | `GetManifestMetadataAsync` parameter and guard; its three call sites pass it |
| DockerUpdateGuard.Tests | `OciRegistryClientTests.cs`, `DockerHubClientTests.cs` | new tests |

## Signatures (for the Dev's skeleton)

None: the only changed member, `GetManifestMetadataAsync`, is private (described in *Approach*). No skeleton is needed.

## Test files

`src/Tests/DockerUpdateGuard.Tests/OciRegistryClientTests.cs`, `src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs`.

Existing test code that calls a changed signature: none (the method is private).

Files of the change no unit test can cover: none.

## Areas

- `docs/areas/registries.md` (4,269 words now, +95 words; within `AREA_MAX_WORDS`): in *What can be read*, *Image configuration*, after the sentence on an index
  without a usable entry: for other registries, a chosen entry answered with an index (either index media type) ends
  the tag and the image configuration lookup *failed* with the AC1 message, without reading that answer or requesting
  what it names, so a lookup sends at most two manifest requests (a listing: that per inspected tag); Docker Hub asks
  for the entry as an image manifest and follows nothing further (AC4's outcome). In *Limits and errors*: *Failed*
  gains "an index whose chosen entry is again an index"; the "Registry responses are untrusted" bullet says a lookup
  follows one index level. *Related decisions* gains 0067.

## Documentation updates

- `docs/areas/registries.md` as in *Areas* (Dev, after the code is green).
- Swept with no change needed: `docs/ARCHITECTURE.md` (*Integration clients* names the clients, not their limits),
  `docs/areas/integration-failures.md` (outcome model unchanged), `docs/areas/base-images.md` (reads configurations
  through the lookup; links Registries for its outcomes), `README.md` (no configuration), `.squad/project.md` (the
  file is already in *Registry and Docker Scout calls*). The repository has no changelog; the release-notes label is
  the orchestrator's.

## Architecture check

No guarantee of `.squad/project.md` is weakened: the new outcome is a fixed reason built from the image reference, as
decision 0009 requires; the layering is unchanged.

## Security considerations

- [x] No limits on parsed input: the bound counts requests; the nested body is not parsed.
- [x] Exceptions reaching a message: none new. The message is fixed text plus `FullReference`, as the existing
  "did not expose a target manifest digest" message.

## Decision records

- `docs/decisions/0067-an-image-index-whose-chosen-entry-is-again-an-index-answers-failed.md` (Proposed)

## Out of scope / follow-ups

- Docker Hub reads `config.digest` from its entry's answer whatever its content type (fact 5); bounded, so not changed.
