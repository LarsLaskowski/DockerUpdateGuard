# Plan: Registry request paths refuse `.` and `..` segments

Source: Issue #360
Status: Draft
Tier: security — the change adds a guard that confines which path of the registry host a request reaches (`.squad/routing.md`, *Tiers*: "a check that confines what a path ... reaches").
Security areas touched: Registry and Docker Hub calls (`OciRegistryClient`, `DockerHubClient`,
`DockerScoutVulnerabilityProvider`). Docker Engine access is not touched (see *Out of scope*).

## Problem / root cause

Both clients escape each path segment with `Uri.EscapeDataString`, which leaves `.` unchanged, and the `System.Uri`
built from the path removes `.` and resolves `..` segments (RFC 3986 dot-segment removal):

- `src/DockerUpdateGuard/Images/OciRegistryClient.cs:483` `EscapeRepository`, used by `CreateManifestUri` (394),
  `CreateBlobUri` (406) and `CreateTagsUri` (418); the reference and digest segments are escaped inline at 396 and 408.
- `src/DockerUpdateGuard/DockerHub/DockerHubClient.cs:411` `EscapeRepository` plus `Uri.EscapeDataString(namespaceName)`
  inline at 651, 720, 767, 821 (registry `manifests`/`blobs`) and 1247, 1312, 1362 (web API).
- `src/DockerUpdateGuard/Vulnerabilities/DockerScoutVulnerabilityProvider.cs:306` does not escape at all: it
  interpolates namespace and name (`ParseNamespaceAndRepo`, 98–108, a split on the first `/`) and the tag of the
  registered reference into the Scout URI, sent with the Docker Hub bearer token (317). Its only gate is
  `Registry == "docker.io"` (410); the reference arrives through `VulnerabilityEnrichmentService.cs:289`.

The values reaching these segments: the repository and the tag or digest of a reference the user registered
(`ImageReferenceParser.Parse` checks nothing, `ImageRegistrationService.cs:77`), and digests read from a manifest — the
selected manifest-list entry (OCI 901, Docker Hub 711) and the configuration digest (OCI 933/1382, Docker Hub 767/821),
none of them validated. Remote references (base-image labels, Docker Engine images, the account listing) are already
held to the repository grammar by #355 / [0014](../../docs/decisions/0014-registry-tag-names-and-digests-are-validated-where-they-are-read.md).

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | `Uri.EscapeDataString` leaves `.` and `..` unchanged | Scratch program on .NET 10 (Lead): `EscapeDataString("..")` = `..` | confirmed |
| 2 | `System.Uri` collapses dot segments: `corp/../../../v2/_catalog` becomes `/v2/_catalog/…` | Scratch: `new Uri("https://reg.example/v2/corp/../../../v2/_catalog/manifests/latest").AbsolutePath` = `/v2/_catalog/manifests/latest`; the same through `UriBuilder.Path`, `new Uri(base, relative)` and a relative `Uri` against `https://hub.docker.com/` (`v2/namespaces/../repositories/x` → `/v2/repositories/x`) | confirmed |
| 3 | `%2E` does not survive `System.Uri` (issue: "measure first") | Scratch: `/v2/corp/%2E%2E/%2E%2E/x/…` → `/x/…`; `%2e%2e`, `.%2E` and `%2E` are unescaped and removed alike. Encoding is therefore no option | confirmed |
| 4 | After `EscapeDataString`, only a segment that is exactly `.` or `..` collapses | Scratch, each form as one segment through `EscapeDataString` and all four URI constructions of #2: `...`, `.%20`, `..%20`, `%20..`, `..%09`, `%2E`/`%2E%2E` (escaped to `%252E`), `..%5Cx`/`\..` (backslash escaped to `%5C`), `..;x`, `..?`, `..#`, U+3002, U+FF0E, U+FF0E U+FF0E, U+2024 U+2024, `.` U+200B `.` (all percent-encoded) stay unchanged in the path | confirmed |
| 5 | #355 keeps `.`/`..` out of remote references | `RegistryReferenceValidator.cs` `RepositoryExpression` admits no dot-only component; used by `BaseImageLabelReader`, `ValidReferenceOrNull`, `DockerHubClient.ParseRepository` (338) | confirmed |
| 6 | A registered reference reaches the builders unchecked, with `..` possible in repository, tag and digest | `ImageReferenceParser.Parse`: `reg.example/corp/../x` keeps `..` in the repository, `x:..` gives tag `..`, `x@..` digest `..` (`NormalizeDigest` only trims and lowercases) | confirmed |
| 7 | The requested tag of `GetTagAsync` cannot be a dot segment | Both clients check `IsValidTag` first (OCI 1208, Docker Hub 1301); the grammar's first character is `[A-Za-z0-9_]` | confirmed |
| 8 | Today a 404 of each request gives: OCI manifest → *not found*; OCI tags list → *not found*; OCI configuration blob → *not found* (lookup) / chain ends (base images); Docker Hub web API → *not found*; Docker Hub registry manifest → no config digest → *not found* (configuration) / chain ends; Docker Hub configuration blob → *failed* "Image configuration blob could not be read for '<ref>'" | OCI 839, 1276, 1392, 941; Docker Hub 1249, 1314, 1370, 663–668 with 1496 and 565–570, 1505 | confirmed |
| 9 | Docker Hub requests the pull token before the manifest (`ResolveBaseImageChainAsync` 558, `GetImageConfigurationAsync` 1485) | code read | confirmed |
| 10 | The container ID (`DockerInstanceClient.cs:1126`) and image ID (889, 942) segments can be `.`/`..` (out of scope) | `Uri.EscapeDataString` only; values come from the configured Docker endpoint | confirmed |
| 11 | The account name of `GetRepositoriesAsync` (1167) can be `.`/`..` (out of scope) | It is the configured `DockerHub:UserName` (`DockerHubAccountImageDiscoveryService.cs:274`), not validated at startup | confirmed |
| 12 | Record 0014 is unreleased | `git ls-remote --tags origin`: newest tag `v1.3.7` at 252b2eb (2026-09-16); `git tag --contains 4477ce7` (the commit that added 0014) is empty | confirmed |
| 13 | Scout's path is steerable and its tag injects a query | Code read (lines in *Problem*); measured by the Devil's Advocate and Security: `acme/../../../x` with tag `..` reaches `https://api.scout.docker.com/x/vulnerabilities`, `x/../../../v2/users` reaches `/v1/v2/users/tags/1/vulnerabilities`, tag `1?page=9#` adds a query and cuts the path | confirmed |
| 14 | Scout logs in before the request (`GetOrCreateTokenAsync`, 425), so a refusal without login must sit in `GetVulnerabilitiesAsync` | code read | confirmed |
| 15 | Escaping changes no valid Scout path: repository and tag grammars use only RFC 3986 unreserved characters (`[a-z0-9._-]`, `/` kept by `EscapeRepository`; `[A-Za-z0-9_.-]`), so `library/nginx:latest` of the existing tests stays byte-identical | RFC 3986 §2.3; `Uri.EscapeDataString` escapes only outside the unreserved set | confirmed |
| 16 | `System.Uri` keeps `%3F` and `%23` escaped in `AbsoluteUri` (AC10's exact URI) | to verify: AC10's test asserts the exact URI, owner Tester, step tests-first | unverified |

## Acceptance criteria

The fixed message is `Registry request path would contain a '.' or '..' segment` (`DotSegmentMessage`). "Dot segment"
means a `/`-separated segment equal to `.` or `..`. A refusal sends no request and writes no log entry.

- [ ] AC1: `RegistryReferenceValidator.HasDotSegment` returns true for a path with a dot segment (`.`, `..`, `a/.`,
  `a/../b`, `./a`, `a//..`) and false otherwise (`a`, `a.b/c`, `...`, `.a`, `a.`, `..a`, `%2E%2E`, `a//b`, empty).
- [ ] AC2: OCI manifest builder — `GetTagAsync` for a repository with a dot segment, and `GetImageConfigurationAsync`
  for a reference whose repository, tag (no digest) or digest is a dot segment, answer *not found* with the fixed
  message and send no request; `ResolveBaseImagesAsync` for such a repository succeeds with no base images and sends no
  request.
- [ ] AC3: OCI tags builder — `GetTagsAsync` for a repository with a dot segment answers *not found* with the fixed
  message and sends no request.
- [ ] AC4: OCI digests from a manifest — `GetImageConfigurationAsync` whose manifest names the configuration digest
  `..` answers *not found* with the fixed message after exactly one request (the manifest); `GetTagAsync` whose
  manifest list selects an entry with digest `..` answers *not found* with the fixed message after exactly one request.
- [ ] AC5: Docker Hub web API path — `GetRepositoryAsync`, `GetTagAsync` and `GetTagsAsync` for a repository with a dot
  segment — in the namespace (`../api`, `./api`), in the name (`acme/..`, `acme/../../v2/x`) or a single segment `..`
  (namespace `library`) — answer *not found* with the fixed message and send no request (no login either).
- [ ] AC6: Docker Hub registry path — `GetImageConfigurationAsync` for a reference whose repository, tag (no digest) or
  digest has a dot segment answers *not found* with the fixed message and sends no request (no token request);
  `ResolveBaseImagesAsync` for such a repository succeeds with no base images and sends no request.
- [ ] AC7: Docker Hub digests from a manifest — `GetImageConfigurationAsync` whose manifest names the configuration
  digest `..` sends no blob request and answers *failed* "Image configuration blob could not be read for '<reference>'";
  one whose manifest list selects an entry with digest `..` sends no second manifest request and answers *not found*
  "Config blob digest could not be resolved for '<reference>'" (both as for a 404 today, VF 8).
- [ ] AC8: no over-refusal, pins VF 4 — a segment that only contains dots or percent text is requested with every
  segment kept — OCI `GetTagAsync` for `corp/my.app`, `corp/.../api` and `corp/%2E%2E/api` requests
  `https://mcr.microsoft.com/v2/corp/my.app/manifests/1.0`, `…/v2/corp/.../api/…` and `…/v2/corp/%252E%252E/api/…`;
  Docker Hub `GetTagAsync` for `acme/my.app` requests `…/v2/namespaces/acme/repositories/my.app/tags/1.0`.

- [ ] AC9: Docker Scout — `GetVulnerabilitiesAsync` for a `docker.io` reference whose repository has a dot segment
  (`acme/..`, `../x`, `acme/../../../x`, `acme/./x`, `..`, looked up under `library`) or whose tag is `.` or `..`
  answers *not found* with the fixed message, sends no request (no login either) and writes no log entry.
- [ ] AC10: Docker Scout escapes each segment — `acme/app` with tag `1?page=9#` requests
  `https://api.scout.docker.com/v1/repositories/acme/app/tags/1%3Fpage%3D9%23/vulnerabilities`; `acme/my.app` and
  `acme/.../x` with tag `1.0` request `…/v1/repositories/acme/my.app/tags/1.0/vulnerabilities` and
  `…/v1/repositories/acme/.../x/tags/1.0/vulnerabilities`.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `RegistryReferenceValidatorHasDotSegmentDotSegmentReturnsTrue`, `RegistryReferenceValidatorHasDotSegmentNoDotSegmentReturnsFalse` (DataRows as listed) | `src/Tests/DockerUpdateGuard.Tests/RegistryReferenceValidatorTests.cs` |
| AC2 | `OciRegistryClientGetTagAsyncDotSegmentRepositoryReturnsNotFoundWithoutRequestAsync` (DataRows `corp/../../../v2/_catalog`, `./corp/api`, `corp/.`, `..`), `OciRegistryClientGetImageConfigurationAsyncDotSegmentReferenceReturnsNotFoundWithoutRequestAsync` (DataRows: repository `corp/..`; tag `..`; tag `.`; digest `..`), `OciRegistryClientResolveBaseImagesAsyncDotSegmentRepositoryReturnsNoBaseImagesWithoutRequestAsync` | `src/Tests/DockerUpdateGuard.Tests/OciRegistryClientTests.cs` |
| AC3 | `OciRegistryClientGetTagsAsyncDotSegmentRepositoryReturnsNotFoundWithoutRequestAsync` | same |
| AC4 | `OciRegistryClientGetImageConfigurationAsyncDotSegmentConfigDigestReturnsNotFoundWithoutBlobRequestAsync`, `OciRegistryClientGetTagAsyncDotSegmentManifestListDigestReturnsNotFoundAsync` | same |
| AC5 | `DockerHubClientGetTagAsyncDotSegmentRepositoryReturnsNotFoundWithoutRequestAsync` (DataRows as listed), `DockerHubClientGetRepositoryAsyncDotSegmentRepositoryReturnsNotFoundWithoutRequestAsync`, `DockerHubClientGetTagsAsyncDotSegmentRepositoryReturnsNotFoundWithoutRequestAsync` | `src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs` |
| AC6 | `DockerHubClientGetImageConfigurationAsyncDotSegmentReferenceReturnsNotFoundWithoutRequestAsync` (DataRows: repository `acme/..`; tag `..`; digest `..`), `DockerHubClientResolveBaseImagesAsyncDotSegmentRepositoryReturnsNoBaseImagesWithoutRequestAsync` | same |
| AC7 | `DockerHubClientGetImageConfigurationAsyncDotSegmentConfigDigestSendsNoBlobRequestAsync`, `DockerHubClientGetImageConfigurationAsyncDotSegmentManifestListDigestReturnsNotFoundAsync` | same |
| AC8 | `OciRegistryClientGetTagAsyncDotsInsideSegmentKeepsSegmentAsync` (DataRows as listed, asserting the exact request URI), `DockerHubClientGetTagAsyncDotsInsideSegmentKeepsSegmentAsync` | the two client test files |
| AC9 | `DockerScoutVulnerabilityProviderGetVulnerabilitiesAsyncDotSegmentReferenceReturnsNotFoundWithoutRequestAsync` (DataRows as listed: repository and tag) | `src/Tests/DockerUpdateGuard.Tests/DockerScoutVulnerabilityProviderTests.cs` |
| AC10 | `DockerScoutVulnerabilityProviderGetVulnerabilitiesAsyncEscapesPathSegmentsAsync` (DataRows as listed, asserting the exact Scout request URI after the login) | same |

Every refusal test asserts the status, the exact message, `handler.Requests` (empty, or the exact URIs sent) and an
empty `logger.Entries` where no request was sent.

## Approach

1. `RegistryReferenceValidator` gets `DotSegmentMessage` and `HasDotSegment(path)`: split on `/` (empty entries kept)
   and compare each segment ordinally with `.` and `..`. Applied to the raw value before escaping: by VF 4 the escaping
   turns no other form into a dot segment, and a value like `a/..` in a single escaped segment (`a%2F..`) is refused
   too, which only refuses an invalid tag or digest.
2. **OCI.** `CreateManifestUri`, `CreateBlobUri` and `CreateTagsUri` return `null` when the repository or the
   reference/digest has a dot segment. `GetManifestMetadataAsync` builds the manifest URI first and answers
   `NotFound(DotSegmentMessage)` on `null`; `SendManifestRequestAsync` takes the built `Uri` and the repository.
   `GetTagsAsync` answers `NotFound(DotSegmentMessage)` when `CreateTagsUri` gives `null`; `GetImageConfigurationAsync`
   the same for `CreateBlobUri`; `ExtractBaseImageFromConfigAsync` returns `null` (the chain ends, as for a 404).
   `ResolveBaseImagesAsync` needs no change (the chain stops on a non-succeeded manifest, as for a 404).
3. **Docker Hub.** `CreateRepositoryPath(repository)` returns the escaped `namespace/name` (via `SplitRepository` and
   `EscapeRepository`), or `null` when the repository has a dot segment; the three web API lookups use it and answer
   `NotFound(DotSegmentMessage)` on `null` before `SendGetAsync`. `CreateRegistryRequestUri(repository, resource,
   reference)` builds `https://registry-1.docker.io/v2/<path>/<resource>/<escaped reference>` (`resource` `manifests`
   or `blobs`), or `null` when the path is `null` or the reference has a dot segment, and replaces the four inline
   constructions. `GetImageConfigurationAsync` and `ResolveBaseImageChainAsync` build the manifest URI before the token
   request: `null` answers `NotFound(DotSegmentMessage)` or ends the chain without a request; `GetImageConfigDigestAsync`
   takes that `Uri`. `GetConfigDigestFromManifestListAsync`, `ExtractBaseImageFromConfigAsync` and
   `ExtractImageConfigurationAsync` return `null` on a `null` URI. The token scope (a query value) stays as it is.
4. **Docker Scout.** `CreateVulnerabilitiesUri(scoutBaseUrl, imageReference)` returns `null` when
   `HasDotSegment(imageReference.Repository)` or `HasDotSegment(imageReference.Tag)` (the namespace is the first
   segment of the repository, so one check covers both), else
   `<scoutBaseUrl>/v1/repositories/<EscapeDataString(ns)>/<EscapeRepository(repo)>/tags/<EscapeDataString(tag)>/vulnerabilities`
   (`EscapeRepository` a private copy of the Docker Hub client's one-liner). `GetVulnerabilitiesAsync` builds it after
   the registry check and before `GetOrCreateTokenAsync`, answering `NotFound(DotSegmentMessage)` on `null` (status
   *unavailable* on the vulnerability pages, as a Scout 404 today); `FetchVulnerabilitiesAsync` takes the built `Uri`
   instead of composing it; its origin check for next-page links is unchanged.
5. No new user-visible output beyond the fixed message, which is the existing *not found* outcome's text.

## Options considered

1. **Refuse in the request builders (chosen)** — one check where each path is composed; covers user-entered references
   and the digests a manifest names; a new lookup inherits it.
2. **Encode the dots as `%2E`** — refuted by VF 3: `System.Uri` unescapes and removes them.
3. **`UriCreationOptions.DangerousDisablePathAndQueryCanonicalization`** — the segment then reaches the registry or a
   proxy in front of it, which applies the same dot-segment removal.
4. **Check at each public lookup** — nine entry points, a future one can miss it, and manifest digests never pass one.
5. **Full repository/tag/digest grammar at the builders, or at registration** — closes more than the defect and
   changes what a user can register, which 0009/0014 leave to plain parsing; registration alone would also miss stored
   references and manifest digests.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Images/Helper/RegistryReferenceValidator.cs` | `DotSegmentMessage`, `HasDotSegment` |
| DockerUpdateGuard | `Images/OciRegistryClient.cs` | builders return `Uri?`; callers as in *Approach* 2 |
| DockerUpdateGuard | `DockerHub/DockerHubClient.cs` | two builders; callers as in *Approach* 3 |
| DockerUpdateGuard | `Vulnerabilities/DockerScoutVulnerabilityProvider.cs` | builder, escaping, refusal before login (*Approach* 4) |

## Signatures (for the Dev's skeleton)

`RegistryReferenceValidator`:
- `public const string DotSegmentMessage = "Registry request path would contain a '.' or '..' segment";`
- `public static bool HasDotSegment(string path)`

`OciRegistryClient` (private):
- `private static Uri? CreateManifestUri(string registry, string repository, string reference)`
- `private static Uri? CreateBlobUri(string registry, string repository, string digest)`
- `private static Uri? CreateTagsUri(string registry, string repository, string? lastTag)`
- `private Task<HttpResponseMessage> SendManifestRequestAsync(Uri manifestUri, string repository, HttpMethod method, CancellationToken cancellationToken)`

`DockerHubClient` (private):
- `private static string? CreateRepositoryPath(string repository)`
- `private static Uri? CreateRegistryRequestUri(string repository, string resource, string reference)`
- `private async Task<string?> GetImageConfigDigestAsync(ImageReference imageReference, Uri manifestUri, string registryToken, CancellationToken cancellationToken)`

`DockerScoutVulnerabilityProvider` (private):
- `private static string EscapeRepository(string repository)`
- `private static Uri? CreateVulnerabilitiesUri(string scoutBaseUrl, ImageReference imageReference)`
- `private async Task<(ExternalOperationResult<IReadOnlyList<VulnerabilityAdvisoryData>> Result, bool Unauthorized)> FetchVulnerabilitiesAsync(HttpClient client, string jwtToken, ImageReference imageReference, Uri requestUri, CancellationToken cancellationToken)`

No public API changes. The skeleton rewrites none of the existing files beyond these members.

## Test files

`src/Tests/DockerUpdateGuard.Tests/RegistryReferenceValidatorTests.cs`,
`src/Tests/DockerUpdateGuard.Tests/OciRegistryClientTests.cs`,
`src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs`,
`src/Tests/DockerUpdateGuard.Tests/DockerScoutVulnerabilityProviderTests.cs` (existing helpers `SequenceHttpMessageHandler`,
`StubHttpMessageHandler`, `TestLogger`, `CreateClient`, `CreateManifestResponse`, `CreateManifestListResponse`).

Existing test call sites of a changed signature: none (only private members change). Existing tests keep their inputs:
none sends a dot segment into a request path (the `..` cases today are tag-grammar or account-listing cases), and
the Scout tests use `library/nginx:latest` and `acme/api:1.0.0`, unchanged by the escaping (VF 15).

Files no unit test can cover: none.

## Areas

- `docs/areas/registries.md`: new subsection *Request paths* (anchor `#request-paths`, linked from 0014) under
  *Behavior*, after *Choice of client per registry*: segments are percent-encoded; a path whose repository, tag, digest
  or reference has a segment that is exactly `.` or `..` is never sent; the outcome per lookup (AC2–AC7, "as for a 404",
  with the fixed message where the lookup itself refuses); no log entry; Docker Scout's vulnerability path is held to
  the same rule and escaped per segment (AC9, AC10); the account name and the Docker Engine requests are not covered. *Limits and errors*, *Not found*: add the refusal. *Known gaps*: the configured
  `DockerHub:UserName` reaches the account listing's path unchecked, with the follow-up issue.
- `docs/areas/image-catalog.md` (*Image references*, the sentence "A reference entered by the user keeps the plain
  parsing above"): add that registry requests refuse its `.`/`..` segments, linking *Request paths*.
- `docs/areas/vulnerabilities.md`, *Docker Scout*, the bullet "Vulnerabilities are read from …": the segments are
  percent-encoded, and a repository or tag with a `.`/`..` segment gives *unavailable* with the fixed message, without
  login or request, linking [Registries](registries.md#request-paths).

## Documentation updates

Owner Dev, after the code is green: the two area documents above, and `docs/ARCHITECTURE.md`, *Security posture*, the
bullet ending with record 0014 ("Names and image references read from remote sides are held to their grammars …"):
one sentence that the registry request builders refuse `.`/`..` path segments of any reference or digest. The sweep
of `docs/areas/` and `ARCHITECTURE.md` for enumerations found the *Not found* list of `registries.md` and the
`0014` bullet of `ARCHITECTURE.md`, and the Scout request path in `vulnerabilities.md` (*Docker Scout*), listed
above; no other area document or `ARCHITECTURE.md` section spells out a request path (the Docker Engine paths of
`docker-instances.md` stay out of scope). `README.md`: none (no configuration).

## Architecture check

Preserved: integration clients never throw for expected failures (a refusal is a `NotFound` result or the chain's
existing early return, no exception); update detection stays pure; layering unchanged (helper in the host's
`Images/Helper`). No guarantee of `.squad/project.md` is weakened.

## Security considerations

- [x] No limits: the check is on the raw value, before escaping; no byte or length bound is added.
- [x] Exceptions reaching a user-visible message: none new. The builders return `null`; the message is the fixed text.
- Guard forms: Scout's path takes the same raw-value check and the same `EscapeDataString` escaping, so VF 4's forms
  hold for it too; a tag can no longer carry `?` or `#` into the query or fragment (AC10).
- Guard forms: enumerated in VF 4 from measurement of `EscapeDataString` plus `System.Uri` (all four constructions the
  clients use). A server that decodes `%252E` twice is outside what the client controls.

## Challenge

- Devil's Advocate, objection 1 (major), Scout's unescaped path: accepted. Scout is in scope (*Approach* 4, AC9,
  AC10, VF 13–16), listed in *Security areas touched*, *Affected projects and types* and the area documents; the
  sweep statement in *Documentation updates* is corrected.

## Security review

- B1 (blocking), Scout path: accepted, fixed as for objection 1 — refusal of `.`/`..` before login and request,
  per-segment escaping so a tag cannot add a query or fragment.
- N1 (empty segments): no change; AC1 pins that `a//..` is refused, so the OCI/Docker Hub escaping difference cannot
  hide a dot segment.
- N2 (registry part `.`/`..` becomes the host): out of scope, follow-up issue below. Scout is not affected (its gate
  admits only `docker.io`).

## Decision records

- [0014](../../docs/decisions/0014-registry-tag-names-and-digests-are-validated-where-they-are-read.md) (Accepted,
  unreleased, extended in place): source #360, context, options 14–19 (14 covers Docker Scout), decision and
  consequences.

## Out of scope / follow-ups

- Docker Engine container and image ID segments (`DockerInstanceClient.cs:889, 942, 1126`): configured, trusted
  endpoint, pre-existing, as the issue states.
- Follow-up issue for the orchestrator — title: "Validate `DockerHub:UserName` as a Docker Hub namespace at startup";
  body: "`DockerHubClient.GetRepositoriesAsync` puts the configured user name into `v2/repositories/{name}/` with
  `Uri.EscapeDataString` only, so `.` or `..` addresses another path of the Docker Hub web API (found while planning
  #360). Validate the key at startup with the other DockerHub options (Hosting, *Validation*), so an invalid value
  fails startup instead of a silent request to another path."
- Follow-up issue for the orchestrator — title: "Check the registry host of a registered image reference";
  body: "A registered reference whose registry part is `.`, `..` or another non-host value becomes the host of the
  OCI client's request URI (`OciRegistryClient` builds it with `UriBuilder`), not a path segment, so #360 does not
  cover it. Measure what `UriBuilder`/`Uri` does with such a host (an exception, a relative resolution) and answer it
  as *not found* or reject it at registration (found in the security review of #360)."
- Next-page links of a listing that contain `..` stay on the listing's origin (record 0004); unchanged.