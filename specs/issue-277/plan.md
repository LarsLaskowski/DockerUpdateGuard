# Plan: The OCI registry client picks attestation manifests and rarely knows publication times

Source: Issue #277
Tier: standard — a behavior change of the OCI client's index entry choice and tag publication time that creates, changes or removes no security control (no guard, credential or path rule changes).
Security areas touched: Registry and Docker Scout calls (`Images/OciRegistryClient.cs`, `DockerHub/DockerHubClient.cs`, new `Images/Helper/ManifestPlatformSelector.cs`: untrusted index and configuration JSON, one more request per tag lookup); Logging and telemetry of external data (an existing warning, 2100, from one new call site)

## Problem / root cause

- **Platform choice.** `OciRegistryClient.SelectPlatformManifestDigest` (`OciRegistryClient.cs:227-253`) tries an
  exact match (`:262-293`) and an architecture match (`:301-321`), then `SelectFirstManifestDigest` (`:328-341`) takes
  the first entry that has a digest, attestations included. `GetImageConfigurationCoreAsync` (`:1360-1364`) passes no
  platform, so a configuration lookup on an index always reads the first entry. Docker Hub has its own, different
  rule (`DockerHubClient.SelectManifestDigest`, `:271-310`).
- **Publication time.** `GetManifestMetadataAsync` sets `PublishedAtUtc` from the manifest's top-level `created`
  (`:938`), which image manifests do not carry (fact 4); for an index it is the platform manifest's value (`:973-984`).
  `GetTagAsync` (`:1440-1463`) never reads the configuration blob, whose `created` `ParseImageConfiguration` already
  parses (`:628-638`).

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | Issue's line numbers moved but the code is as described: fallback to the first entry with a digest; no platform on the configuration lookup | `OciRegistryClient.cs:227-341`, `:1360-1364`; `IRegistryMetadataClient.GetImageConfigurationAsync` has no platform | confirmed |
| 2 | Docker Hub rule: skip entries without digest; an entry without `platform` is a fallback; skip `os` or `architecture` equal to `unknown` (case-insensitive, untrimmed); first `linux`, else first remaining | `DockerHubClient.cs:271-310` | confirmed |
| 3 | A real BuildKit index lists `unknown/unknown` attestation entries, and the attestation's config is the empty blob (`application/vnd.oci.empty.v1+json`, `{}`): a configuration lookup on it succeeds with no labels or platform instead of failing | `samples/ghcr-home-assistant-stable-index.json`, `samples/ghcr-home-assistant-stable-attestation-manifest.json` (fetched anonymously 2026-11-26) | confirmed |
| 4 | A real platform manifest has no top-level `created`; its config blob has `created` with nine fractional digits | `samples/ghcr-home-assistant-stable-linux-amd64-manifest.json`, `…-config.json` (`2026-11-23T20:55:47.388026494Z`) | confirmed |
| 5 | Reproducible builds set `created` to the epoch | `samples/gcr-distroless-static-debian12-linux-amd64-config.json`: `1970-01-01T00:00:00Z` | confirmed |
| 6 | `DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, …)` parses nine fractional digits, `1970-01-01T00:00:00Z` and `0001-01-01T00:00:00Z`; a value without offset is taken as local time | `dotnet fsi` probe in the scratchpad (output: all `true`; offset-less → `+00:00` on this UTC host) | confirmed |
| 7 | An entry or `platform` that is not an object throws `InvalidOperationException` in both clients today, answered *failed* / `unexpected error` | `TryGetString` (`OciRegistryClient.cs:576-581`, `DockerHubClient.cs:458-467`) calls `TryGetProperty` unguarded; `ExecuteAsync` (`:1207-1227`) | confirmed |
| 8 | Runtime-container scans pass the container's platform to `GetTagAsync`/`GetTagsAsync`; observed-image and base-image tag lookups pass none | `RuntimeContainerScanOrchestrator.cs:887-900`; `ImageScanOrchestrator.cs:260-264`, `:555`, `:685` | confirmed |
| 9 | `RegistryTagQueryHelper.ShouldKeepTag` drops a tag older than `PublishedSinceUtc` only when both times are known and the digest differs from the current one; the current tag is always kept | `RegistryTagQueryHelper.cs:84-123` | confirmed |
| 10 | `SequenceHttpMessageHandler` answers an unregistered URL with 404, so existing tests without a blob route see an unknown time, not a failure | `Helper/SequenceHttpMessageHandler.cs:112-118` | confirmed |
| 11 | The token cache key is authority and repository, so the blob request reuses the manifest's bearer token | `GetTokenCacheKey` (`:537-540`), `SendRegistryRequestAsync` (`:1022-1077`) | confirmed |
| 12 | Update detection uses publication times registry-agnostically (newer-than-current, major-upgrade age) | `docs/areas/update-detection.md:86`, `:101-117` | confirmed |

## Acceptance criteria

"Index" means an image index or Docker manifest list; an entry is *usable* when its `digest` is a non-blank string and
neither its `platform.os` nor `platform.architecture` equals `unknown` (ordinal, case-insensitive). An entry without
`platform` is usable.

- AC1: `ManifestPlatformSelector.SelectManifestDigest` returns, of the usable entries in order: with a requested OS
  and architecture, the first exact match; then, with a requested architecture, the first entry with that
  architecture; then the first entry whose OS is `linux`; then the first usable entry; `null` when none is usable.
  Requested values are trimmed (blank = not requested); all comparisons are case-insensitive.
- AC2: For an index `[attestation unknown/unknown, linux/amd64]`, `OciRegistryClient.GetImageConfigurationAsync` reads
  the configuration of the `linux/amd64` manifest.
- AC3: For an index `[windows/amd64, linux/arm64]`, `GetImageConfigurationAsync` (no platform) reads the `linux/arm64`
  manifest; `GetTagAsync` with `windows`/`amd64` follows the Windows entry.
- AC4: `GetTagAsync` with a platform no entry matches (`linux`/`s390x` on `[unknown/unknown, linux/amd64]`) follows
  `linux/amd64`, never the attestation.
- AC5: An index whose entries are all unusable answers `GetTagAsync` and `GetImageConfigurationAsync` *failed* with
  exactly `Manifest list for '<FullReference>' did not expose a target manifest digest` (decided in
  `GetConfigDigestFromManifestListAsync`, `:966-971`, when the selector returns `null`).
- AC6: `GetTagAsync` returns as `PublishedAtUtc` the configuration blob's `created` when the manifest has no usable
  top-level `created`, for a single-platform manifest and for an index (the selected platform manifest's blob); a
  usable top-level `created` wins and no blob is requested. The digest stays the first response's
  `Docker-Content-Digest`.
- AC7: A `created` (manifest or blob) that is missing, not a string, unparseable, or at or before
  `1970-01-01T00:00:00Z` is unusable; a manifest's unusable `created` falls back to the blob. An unusable blob
  `created`, a blob answer other than success (404, 500, refused token), a transport error, a timeout or malformed
  blob JSON yields *succeeded* with the digest and `PublishedAtUtc = null`; an error (not a status) logs one warning
  2100. A caller cancellation during the blob request still throws `OperationCanceledException`.
- AC8: `GetTagsAsync` with `CurrentTag`, `CurrentDigest` and `PublishedSinceUtc` drops an OCI tag whose blob
  `created` is older than the bound, keeps the current tag and an older tag with the current digest.
- AC9: A tag lookup sends at most one blob request, none when the manifest has a usable `created` or no config
  digest; a listing sends at most one per inspected tag.
- AC10: Docker Hub selection is unchanged: `DockerHubClient` calls the selector without a platform and every
  existing `DockerHubClientTests` test passes unchanged.
- AC11: `docs/areas/registries.md` states the shared rule, the publication-time rule with its cost and build-time
  meaning, and no longer lists the #277 gap.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `ManifestPlatformSelectorSelectManifestDigestAttestationFirstReturnsLinuxDigest`, `ManifestPlatformSelectorSelectManifestDigestNoPlatformPrefersLinux`, `ManifestPlatformSelectorSelectManifestDigestWithoutLinuxReturnsFirstUsableEntry`, `ManifestPlatformSelectorSelectManifestDigestEntryWithoutDigestIsSkipped`, `ManifestPlatformSelectorSelectManifestDigestRequestedPlatformReturnsExactMatch`, `ManifestPlatformSelectorSelectManifestDigestRequestedArchitectureReturnsArchitectureMatch`, `ManifestPlatformSelectorSelectManifestDigestUnmatchedPlatformSkipsUnknownEntries`, `ManifestPlatformSelectorSelectManifestDigestRequestedUnknownPlatformIsNeverSelected`, `ManifestPlatformSelectorSelectManifestDigestPaddedRequestMatchesCaseInsensitively`, `ManifestPlatformSelectorSelectManifestDigestOnlyUnknownEntriesReturnsNull`, `ManifestPlatformSelectorSelectManifestDigestRealBuildKitIndexReturnsLinuxAmd64` | `src/Tests/DockerUpdateGuard.Tests/ManifestPlatformSelectorTests.cs` |
| AC2 | `OciRegistryClientGetImageConfigurationAsyncAttestationFirstSelectsLinuxManifestAsync` | `src/Tests/DockerUpdateGuard.Tests/OciRegistryClientTests.cs` |
| AC3 | `OciRegistryClientGetImageConfigurationAsyncNoPlatformPrefersLinuxAsync`, `OciRegistryClientGetTagAsyncRequestedWindowsPlatformSelectsWindowsManifestAsync` | `src/Tests/DockerUpdateGuard.Tests/OciRegistryClientTests.cs` |
| AC4 | `OciRegistryClientGetTagAsyncUnmatchedPlatformSkipsUnknownEntriesAsync` | `src/Tests/DockerUpdateGuard.Tests/OciRegistryClientTests.cs` |
| AC5 | `OciRegistryClientCallOnlyUnknownPlatformsReturnsFailedAsync` (DataRow `Tag`, `Configuration`) | `src/Tests/DockerUpdateGuard.Tests/OciRegistryClientTests.cs` |
| AC6 | `OciRegistryClientGetTagAsyncManifestWithoutCreatedUsesConfigCreatedAsync`, `OciRegistryClientGetTagAsyncIndexUsesSelectedPlatformConfigCreatedAsync`, `OciRegistryClientGetTagAsyncManifestCreatedWinsWithoutBlobRequestAsync` | `src/Tests/DockerUpdateGuard.Tests/OciRegistryClientPublicationTimeTests.cs` |
| AC7 | `OciRegistryClientGetTagAsyncUnusableConfigCreatedReturnsUnknownPublishedTimeAsync` (DataRows: epoch, `0001-01-01T00:00:00Z`, `1969-12-31T23:59:59Z`, missing, number, `not a date`), `OciRegistryClientGetTagAsyncEpochManifestCreatedFallsBackToConfigCreatedAsync`, `OciRegistryClientGetTagAsyncConfigBlobAnswerFailsReturnsSucceededWithoutPublishedTimeAsync` (404, 500, 401 without challenge), `OciRegistryClientGetTagAsyncConfigBlobErrorReturnsSucceededWithoutPublishedTimeAsync` (transport error, timeout, malformed JSON; one warning 2100), `OciRegistryClientGetTagAsyncCallerCancelledDuringConfigBlobThrowsAsync` | `src/Tests/DockerUpdateGuard.Tests/OciRegistryClientPublicationTimeTests.cs` |
| AC8 | `OciRegistryClientGetTagsAsyncPublishedSinceDropsOlderTagsAsync` | `src/Tests/DockerUpdateGuard.Tests/OciRegistryClientPublicationTimeTests.cs` |
| AC9 | `OciRegistryClientGetTagAsyncManifestWithoutConfigDigestSendsNoBlobRequestAsync`, `OciRegistryClientGetTagsAsyncSendsAtMostOneBlobRequestPerInspectedTagAsync` (with AC6's `…CreatedWinsWithoutBlobRequestAsync`) | `src/Tests/DockerUpdateGuard.Tests/OciRegistryClientPublicationTimeTests.cs` |
| AC10 | existing: `DockerHubClientGetImageConfigurationAsyncSkipsAttestationManifestAsync` and the rest of `DockerHubClientTests`, unchanged | `src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs` |
| AC11 | check: the Reviewer reads `docs/areas/registries.md` against AC1–AC9 in step 8 | — |

Fixtures: the selector and index tests build their JSON from the real samples in `samples/` (copied into raw string
literals, entries reordered or their `platform` edited only where a criterion needs another order, such as AC2's
attestation first); the samples hold only public data. The nine-digit `created` of fact 4 is compared with
`DateTimeOffset.Parse` of the same text (100 ns precision).

## Approach

Early draft PR: no

1. **Selector** (`Images/Helper/ManifestPlatformSelector.cs`, `internal static`): AC1's rule in one pass over the
   array, keeping the first exact, architecture, `linux` and usable digest; entry values are compared as sent with
   `StringComparison.OrdinalIgnoreCase`, only the requested values are trimmed. It reads entries with a private
   `TryGetString` like the clients', unguarded, so a non-object entry or `platform` still throws (fact 7, unchanged).
   The caller keeps the `manifests` array check.
2. **Clients.** `OciRegistryClient.GetConfigDigestFromManifestListAsync` calls the selector with the caller's
   platform; `DockerHubClient.GetConfigDigestFromManifestListAsync` (`:692`) with `null, null`. The old selection
   members are deleted (*Signatures*).
3. **Publication time** (`OciRegistryClient`, private):
   - `TryReadPublishedAtUtc(JsonElement element)` — `TryReadCreatedAtUtc` with values `<= DateTimeOffset.UnixEpoch`
     as `null`; used for the manifest's `PublishedAtUtc` (`:938`). `ParseImageConfiguration`'s `CreatedAtUtc` stays
     as parsed (epoch included).
   - `ReadConfigurationBlobAsync<T>(ImageReference imageReference, string configDigest, Func<JsonElement, T> parse,
     CancellationToken cancellationToken)` — the blob part of `GetImageConfigurationCoreAsync` (`:1376-1408`) moved
     unchanged (refusal, 404 and failure messages included); `GetImageConfigurationCoreAsync` calls it with
     `ParseImageConfiguration`.
   - `ReadConfigurationPublishedAtUtcAsync(ImageReference imageReference, string? configDigest, CancellationToken
     cancellationToken)` — `null` without a request for a blank digest; otherwise `ExecuteAsync(nameof(…),
     FullReference, "Registry tag publication time lookup failed for '<FullReference>'", …)` around
     `ReadConfigurationBlobAsync` with `TryReadPublishedAtUtc` as `parse`; `null` for any result but *succeeded*.
     `ExecuteAsync` already rethrows the caller's cancellation and logs other errors as 2100; its message never
     leaves the method.
   - `GetTagAsync`: `PublishedAtUtc = manifest value ?? await ReadConfigurationPublishedAtUtcAsync(…)`.
4. **Resource bound.** At most one blob request per tag lookup (plus the existing token flow on a 401), only when
   the manifest gave no usable time; a listing therefore adds at most one request per inspected tag, so at most
   `MaximumTags`. The tests observe it through the request log of `SequenceHttpMessageHandler` /
   `DelegateHttpMessageHandler`.

## Options considered

- **Shared helper (chosen) vs. fixing the OCI fallback in place** — two copies drifted once already; the Product
  Manager chose one rule ([0062](../../docs/decisions/0062-both-registry-clients-choose-a-platform-manifest-by-one-shared-rule.md)).
- **A platform parameter on `GetImageConfigurationAsync`** — changes the interface and every caller; out of scope per
  the issue, and the `linux` default makes the call correct.
- **Trimming entry values as the OCI client did** — would change Docker Hub (which compares untrimmed), which the
  issue forbids; untrimmed entries change only padded values on other registries.
- **The `org.opencontainers.image.created` manifest annotation** — optional, absent from the sampled manifests
  ([0063](../../docs/decisions/0063-other-registries-take-a-tags-publication-time-from-its-image-configuration.md)).
- **Always reading the blob** — an extra request where the manifest already answers.
- **Failing the tag lookup on a blob failure** — rejected by the issue: the digest is the essential part.
- **Epoch rule for the blob only** — a manifest `created` at the epoch would then win over a real blob value; one
  rule for both sources is simpler and never worse.
- **Parsing the whole configuration in the tag path** — a malformed `Env` entry would then lose a valid `created`;
  the generic reader parses only `created`.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Images/Helper/ManifestPlatformSelector.cs` | new |
| DockerUpdateGuard | `Images/OciRegistryClient.cs` | selector call, publication time, blob reader extracted; selection members deleted |
| DockerUpdateGuard | `DockerHub/DockerHubClient.cs` | selector call; `SelectManifestDigest` deleted |

## Signatures (for the Dev's skeleton)

- `internal static class ManifestPlatformSelector` (namespace `DockerUpdateGuard.Images.Helper`)
- `public static string? SelectManifestDigest(JsonElement manifestsElement, string? operatingSystem, string? architecture)`

Deleted: `OciRegistryClient.SelectPlatformManifestDigest`, `SelectExactPlatformManifestDigest`, `SelectArchitectureManifestDigest`, `SelectFirstManifestDigest` and `NormalizePlatformValue` are removed in step 6.
Deleted: `DockerHubClient.SelectManifestDigest` is removed in step 6.

The skeleton adds only the selector (body `Skeleton`); the clients are rewired in step 6, so Docker Hub and the
existing OCI tests stay green on the skeleton. Names checked against *Writing code*: no nested type, three
parameters, no keyword names.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/ManifestPlatformSelectorTests.cs` (new)
- `src/Tests/DockerUpdateGuard.Tests/OciRegistryClientTests.cs` (AC2–AC5)
- `src/Tests/DockerUpdateGuard.Tests/OciRegistryClientPublicationTimeTests.cs` (new split class, declaring its own
  response helpers, `docs/UNIT_TESTS.md`)

Existing test code that calls a changed signature: none. Existing tests whose input AC6/AC9 handle differently
(a tag manifest with a config digest and no `created`, asserting the exact requests), adapted by the **Tester** in
step 5 by giving their manifest a top-level `created`, which keeps them green before and after:
`OciRegistryClientGetTagsAsyncSkipsInvalidTagNamesAsync`, `OciRegistryClientGetTagAsyncDotsInsideSegmentKeepsSegmentAsync`,
`OciRegistryClientGetTagAsyncRegistryWithPortRequestsHostAndPortAsync`. Other tag-lookup tests get a 404 for the blob
(fact 10) and keep their assertions.

Files no unit test can cover: none.

## Areas

- `docs/areas/registries.md` (+90 words; 4103 words now) — *What can be read*: the tag lookup's publication-time
  rule for other registries (blob fallback, unusable values, best effort, build time vs. Docker Hub's push time, one
  extra request per tag lookup and so up to the maximum count per bounded listing); the image configuration's
  platform choice as one rule for both clients; *Known gaps* entry (and the then empty heading) removed; *Related
  decisions* gains 0062 and 0063.

## Documentation updates

- `docs/areas/registries.md` — as *Areas* (Dev).
- `.squad/project.md (+1 words)` — *Security areas*, *Registry and Docker Scout calls*: add
  `Images/Helper/ManifestPlatformSelector.cs` to the file list (Dev reports the exact text, the orchestrator writes it).
- Swept with no change: `docs/ARCHITECTURE.md` (client table and guarantees name no selection or time rule),
  `docs/areas/update-detection.md` and `base-images.md` (registry-agnostic), `README.md` (no option). The repository
  has no changelog; the release-notes label is the orchestrator's.

## Architecture check

Outcome model kept: a blob failure never changes a tag lookup's outcome, and every surfaced message is unchanged
(decision 0009). Update evaluation stays pure: `UpdateDetectionService` and `VersionTagResolutionHelper` are
untouched; they only receive more known times. Catalog write path, layering (helper in the host's `Images/Helper`)
and the tag digest semantics are unchanged.

## Security considerations

- [x] No limits: the blob goes through `RemoteJsonDocument` and `RequestDeadlineHandler` as the configuration lookup
  does; the selected digest is still path-checked by `CreateManifestUri`.
- [x] Exceptions reaching a user-visible message: none new; the tag path's blob errors become an unknown time and a
  2100 warning carrying the operation name and the full reference, as existing 2100 entries do.

## Decision records

- `docs/decisions/0062-both-registry-clients-choose-a-platform-manifest-by-one-shared-rule.md` (Proposed)
- `docs/decisions/0063-other-registries-take-a-tags-publication-time-from-its-image-configuration.md` (Proposed)

## Out of scope / follow-ups

- Proposed issue — title "Registry timestamps without an offset are read as local time"; body: both clients parse
  `created`/`last_pushed` with `DateTimeOffset.TryParse(…, CultureInfo.InvariantCulture)` without
  `DateTimeStyles.AssumeUniversal`, so a value without offset takes the host's time zone (fact 6); the image runs in
  UTC by default, so the effect is limited to hosts with another `TZ`.
- A platform parameter for `GetImageConfigurationAsync` and the platform `variant` (`arm/v7` vs. `v6`): not asked for.