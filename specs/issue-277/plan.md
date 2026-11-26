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
| 7 | An entry or `platform` that is not an object throws `InvalidOperationException` in Docker Hub and in OCI lookups with a requested platform, answered *failed* / `unexpected error`; OCI lookups without one did not read `platform` | `TryGetString` (`OciRegistryClient.cs:576-581`, `DockerHubClient.cs:458-467`) calls `TryGetProperty` unguarded; `ExecuteAsync` (`:1207-1227`) | confirmed |
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

## Challenge notes

Devil's Advocate, mode `plan`, issue #277 (`/home/user/DockerUpdateGuard/specs/issue-277/plan.md`)

The plan holds up. I found no major objections and two minor ones.

**What I checked and found correct (no objection):**
- **Root cause, in `src/DockerUpdateGuard/Images/OciRegistryClient.cs`:**
  - The fallback takes the first entry that has any digest (`:328-341`).
  - The configuration lookup passes no platform (`:1362-1364`).
  - A manifest's `PublishedAtUtc` comes only from its top-level `created` (`:938`).
  - `GetTagAsync` never reads the blob (`:1445-1461`).
- **Listings:** `GetTagsAsync` inspects each tag through `GetTagAsync` (`AppendInspectedTagsAsync`, `:1184`), so the blob fallback reaches listings and AC8 can pass.
- **Docker Hub:** `DockerHubClient.SelectManifestDigest` (`:271-310`) is AC1 with no platform. Both `TryGetString` copies behave the same (`OciRegistryClient.cs:576-581`, `DockerHubClient.cs:458-467`), so the shared selector does not change Docker Hub.
- **AC5's exact message survives:** `PropagateFailure` keeps the original message (`Infrastructure/ExternalOperationResult{T}.cs:53-62`).
- **No other selection code exists:** nothing else in `src/DockerUpdateGuard` reads `manifests` or `platform`.
  - Base-image resolution only calls `GetImageConfigurationAsync` (`RegistryBaseImageResolver.cs:144`, `:169`).
  - Its OCI test (`RegistryBaseImageResolverTests.cs:55-150`) runs no tag lookup, so its single queued blob response is not used up.
- **Existing tests:** the three tests the plan names are the only tag-lookup tests that assert exact request lists (`OciRegistryClientTests.cs:1508`, `:1883`, `:2038`). The `DelegateHttpMessageHandler` fixtures answer a blob path with a 404 or a parseable JSON body, so they log no extra 2100:
  - page-cap tests: `CreateTagListingResponse`, `:2738-2750`
  - transport-error listing test: `:2632-2641`
  - repeated-link test, whose listing is empty: `:794`
- **Impact on update detection:** the effect of now-known times on existing users' results is already stated in decision 0063's *Consequences*.
- **Tier and samples:** tier `standard` fits the routing table (no security control changes). Real samples are named for the index, attestation, platform manifest and configuration formats.

**Objections**

1. `minor`: AC9 says "a tag lookup sends at most one blob request", but `SendRegistryRequestAsync` (`OciRegistryClient.cs:1030-1074`) can send the same blob URL up to three times: cached token answered 401, then anonymous, then with a new token. In production the retry handler can also resend transient failures. Plan step 4 already says "plus the existing token flow on a 401".
   - Instruction for Dev and Tester: read AC9 as "at most one blob lookup (one blob URL) per tag lookup".
   - `OciRegistryClientGetTagsAsyncSendsAtMostOneBlobRequestPerInspectedTagAsync` uses a fixture without a 401 and asserts each tag's blob URL is requested once.
   - The area text names the token retry as an exception, as step 4 does.

2. `minor`: AC7 lists "refused token" as a blob answer that leaves the time unknown, but the test row tests "401 without challenge". In code these are two different paths:
   - A 401 without a challenge is returned as it is, with no token request (`OciRegistryClient.cs:1053-1056`).
   - A refused token is a failed token request, answered through `CreateAuthenticationFailureResponse` (`:1063-1066`).

   Instruction for the Tester: give `OciRegistryClientGetTagAsyncConfigBlobAnswerFailsReturnsSucceededWithoutPublishedTimeAsync` both cases. One DataRow is a blob 401 without a challenge. The other is a blob 401 with a `Bearer` challenge whose token request fails. Each asserts *succeeded*, the manifest digest, `PublishedAtUtc` null and no warning 2100.

## Security notes

I approve the plan, with two non-blocking findings. The `standard` tier is correct and *Security areas touched* lists everything it touches.

**Tier and areas**
- No security control is created, changed or removed:
  - The new blob request goes through the existing guard (`CreateBlobUri`, `OciRegistryClient.cs:421-433`: dot-segment check, `Uri.EscapeDataString`, registry host check), because the plan moves the blob part of `GetImageConfigurationCoreAsync` (`:1376-1408`) into `ReadConfigurationBlobAsync<T>` unchanged.
  - The selected index digest still goes through `CreateManifestUri` (`:396-410`).
  - Choosing an index entry does not decide what an attacker can reach, so it is not a guard (`.squad/routing.md`, *Tiers*, `security` row).
- The two listed areas, *Registry and Docker Scout calls* and *Logging and telemetry of external data*, are complete. *Failure messages and remote text* is not touched: `ReadConfigurationPublishedAtUtcAsync` drops the failure message it builds (plan step 3), so no new remote text reaches a result.
- The plan correctly adds `Images/Helper/ManifestPlatformSelector.cs` to the area's file list in `.squad/project.md`.

**What I checked against the code**
- **Credentials:** the token cache key is the authority plus the repository (`:537-540`). The blob URI is built on the same registry host, so the bearer token goes only to the host that issued the challenge (fact 11 holds).
- **Cancellation and errors:** `ExecuteAsync` (`:1207-1227`) rethrows only when the caller cancelled. A timeout from `HttpClient` or `RequestDeadlineHandler` becomes *failed*, which gives an unknown time plus warning 2100. A blob whose root is not an object makes `TryGetString` throw `InvalidOperationException`, which ends the same way. AC7's behavior holds for every one of these cases.
- **Logging:** the new 2100 call site logs the operation name and `FullReference`. The tag in it has already passed `RegistryReferenceValidator.IsValidTag` (`:1453`), the same input as the existing 2100 entries, and the log message is unchanged (`ImageHostLoggingExtensions.cs:768-774`). There are no new log arguments.
- **Resource bound:** the blob document is parsed through `RemoteJsonDocument`, its size limit applies, and it is disposed inside `ReadConfigurationBlobAsync`. The only value kept is a `DateTimeOffset?`, so nothing retains derived data. AC9's two request-log tests pin "at most one blob request per tag lookup and per inspected tag". That count is of logical requests: `TransientHttpRetryHandler` can still resend a transient failure, which `docs/areas/registries.md` already states for every integration.
- **Samples:** the seven files in `specs/issue-277/samples` (27 KB) contain no token, password, secret or authorization text (`grep -il "bearer\|token\|password\|secret\|authorization" *` printed nothing).
- **Measurement:** no security property here rests on measured library behavior. The epoch rule and fact 6 are about correct publication times, not security, so I ran no probe.

**Findings**

1. [NON-BLOCKING] Decision 0062 (*Consequences*, third bullet) and plan fact 7 claim more than the code shows. They say a non-object entry or `platform` "still makes the lookup *failed* … as before in both clients". For `platform` that is not true of the OCI client today when no platform is requested, which is every `GetImageConfigurationAsync` call:
   - The old fallback `SelectFirstManifestDigest` (`OciRegistryClient.cs:328-341`) reads only `digest` and never reads `platform`. So an index whose first entry has a digest and a non-object `platform` (including `"platform": null`) succeeds today.
   - With the new selector, which reads `platform` of every entry to test usability, that lookup fails.

   The change fails closed and is not a weakening, but the accepted record should state it. Replace the bullet in `docs/decisions/0062-both-registry-clients-choose-a-platform-manifest-by-one-shared-rule.md`, verbatim:
   ```
   - An entry that is not a JSON object, or a `platform` that is not one (`null` included), makes the lookup *failed*
     with `unexpected error` in both clients
     ([0029](0029-registry-calls-answer-every-error-but-the-callers-cancellation-as-failed.md)); before, the OCI
     client's lookups without a requested platform did not read `platform`, so only there a non-object `platform` now
     fails a lookup that used to succeed.
   ```
   Correct fact 7's *Claim* cell the same way ("… in Docker Hub and in OCI lookups with a requested platform; OCI lookups without one did not read `platform`"). Recommendation: fix now

2. [NON-BLOCKING] Nested indexes are followed without a depth limit, and this predates the change. `GetConfigDigestFromManifestListAsync` (`:966-985`) calls `GetManifestMetadataAsync` on the selected digest. When that response again has an index content type, a value the registry controls (`IsManifestListContentType`, `:917`), the client recurses with nothing stopping it. A hostile or broken registry can therefore chain indexes and make one tag lookup send unbounded manifest requests, each bounded only by the request deadline. The plan neither adds nor widens this; the new blob request comes only after the chain ends. Smallest fix: a depth parameter that answers a nested index *failed* after one level. Recommendation: issue
