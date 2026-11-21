# Plan: Docker Hub image references are not normalized to one canonical form

Source: Issue #275
Tier: standard — the parser's output and a startup data correction change behavior, and no security control is created, changed or removed.
Security areas touched: Database and migrations (a startup step rewrites the current version of observed images and the repository of tag selections, creating catalog entries through the catalog's write path; no schema change, no migration); Logging and telemetry of external data (one new startup log event carrying only a count)

## Problem / root cause

`ImageReferenceParser.Parse` (`src/DockerUpdateGuard/Images/ImageReferenceParser.cs:130-144`) adds `library/` only
when the whole reference has one segment, and takes any host-like first segment as the registry, so `docker.io/nginx`
yields repository `nginx` and `index.docker.io/…`, `registry-1.docker.io/…` stay registries of their own.
`NormalizeWrappedRegistry` (`:57-82`) checks only `docker.io`, so `index.docker.io/ghcr.io/owner/app` is not unwrapped.
The catalog compares exactly (`ImageCatalogRepository.cs:108-111`, `:124-125`), so each spelling is its own entry.
Two kinds of tracked rows never re-parse (facts 10, 12).

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | The parser's results for the issue's table are as stated | `ImageReferenceParser.cs:130-146`; pinned by `ImageReferenceParserTests.cs:130-132` | confirmed |
| 2 | The catalog looks up registry and repository exactly | `ImageCatalogRepository.cs:108-111`, `:124-125` | confirmed |
| 3 | Registry access treats the aliases alike | `DockerHubClient.cs:344-366` delegates to `DockerHubHosts.IsDockerHubHost` (`DockerHub/DockerHubHosts.cs:15`), which also lists the web hosts; issue's lines `358-363` are stale | confirmed |
| 4 | Writers that parse references | `RuntimeContainerScanOrchestrator.cs:683`, `ImageRegistrationService.cs:112`, `BaseImageLabelReader.cs:29` (called from `RegistryBaseImageResolver.cs:95`), `DockerInstanceClient.cs:171`; `ImageScanOrchestrator.cs:701` and `VulnerabilityEnrichmentService.cs:313` re-parse `Format(version)`. Refuted part: the issue's `OciRegistryClient.cs:971` and `DockerHubClient.cs:747` parse no reference | confirmed |
| 5 | Discovery writes `docker.io` + `namespace/name` | `DockerHubClient.cs:317-333` | confirmed |
| 6 | `/base-images` merges two observed images' base only when the base has a digest; without one the grouping key includes the source reference (the image that carries the label), so two different observed images never share a digest-less base (the issue's consequence 1 holds only for bases with a digest) | `SharedBaseImageQueryService.cs` `BuildGroupingKey`; `docs/areas/base-images.md:72-73`; `RegistryBaseImageResolver.cs:136` | confirmed |
| 7 | Own-image link compares a key of registry and repository, case-insensitive | `RuntimeContainerScanOrchestrator.cs:1428-1440` (issue's `1155-1167` stale), `:355-358`; `ApplicationViewService.cs:1148-1159` | confirmed |
| 8 | Docker Scout refuses a registry other than `docker.io`; Trivy strips only `docker.io`; neither needs a change once the parser normalizes | `DockerScoutVulnerabilityProvider.cs:587` (issue's `389` stale); `TrivyVulnerabilityProvider.cs:222-236` | confirmed |
| 10 | A manual observed image's current version never moves; its scan only refreshes metadata | `ImageScanOrchestrator.cs:701-709` | confirmed |
| 11 | A tag selection is unique per Docker instance and container, so a selection on the canonical repository beside the old one cannot exist; the issue's "drop the non-canonical selection" case never occurs | `RuntimeContainerTagSelectionConfiguration.cs` unique index (`DockerInstanceId`, `ContainerId`) | refuted |
| 12 | A tag selection applies only while it points at the latest snapshot's registry repository | `ApplicationViewService.cs:1998-2002`; written at `RuntimeContainerTagSelectionService.cs:70` | confirmed |
| 13 | Base-image relationships belong to the child image version and are replaced on the next successful observed-image scan | `ImageScanOrchestrator.cs:483-486`, `:570-578` | confirmed |
| 14 | Docker's reference library maps `index.docker.io` to `docker.io` and adds `library/` to any single-component `docker.io` path (`docker.io/ghcr.io` → `docker.io/library/ghcr.io`); it does not map `registry-1.docker.io` (the issue adds it) | `distribution/reference` v0.6.0 `normalize.go:126-170`, read in a scratch clone | confirmed |
| 15 | Related defect fixed as a side effect: repo-digest matching compares parsed registry and repository, so an aliased reference missed its digest among several | `DockerInstanceClient.cs:99-137` | confirmed |
| 16 | Event ID 1009 is free; startup events are documented as `1000`–`1008` | grep of `EventId = 10`; `docs/areas/telemetry.md:67` | confirmed |
| 17 | Remote references are validated after parsing (repository at most 255), so a single-segment name of 248–255 characters becomes invalid with `library/` (accepted: Docker's own form; the canonicalizer applies the same rules, Security review 1) | `BaseImageLabelReader.cs:39-41`, `RuntimeContainerScanOrchestrator.cs:697` | confirmed |
| 18 | Record 0048 is unreleased and names #275 in a consequence; edited in place | `git tag --contains` empty for its commit | confirmed |
| 19 | Only two existing tests pin the old behavior | grep of the test projects for non-canonical Docker Hub spellings: `ImageReferenceParserTests.cs:130-132`, `:189` | confirmed |
| 20 | The runtime scan carries resolved and available update versions over only from a snapshot of the same image version id | `RuntimeContainerScanOrchestrator.cs:178-196` (`CreateSnapshot`), `:1385-1389` (`GetPreviousResolvedVersionTagAsync`); `docs/areas/update-detection.md:52-58` | confirmed |
| 21 | Registration keeps at most one manual observed image per version and rejects a version an enabled discovered image tracks; it never checks rows created otherwise | `ImageRegistrationService.cs:126-145`; record 0048 | confirmed |
| 22 | Get-or-create saves on its own when it creates an entry, flushing other pending changes of the context | `ImageCatalogRepository.cs:147`, `:209` | confirmed |
| 23 | The registry column holds 200 characters, the repository 300; the catalog checks only for blank values | `RegistryRepositoryConfiguration.cs:25`, `:29`; `ImageCatalogRepository.cs:41-52` | confirmed |

## Acceptance criteria

- AC1: `Parse` returns registry `docker.io` and repository `library/nginx` for `nginx`, `library/nginx`,
  `docker.io/nginx`, `docker.io/library/nginx`, `index.docker.io/nginx`, `index.docker.io/library/nginx`,
  `registry-1.docker.io/nginx`, `registry-1.docker.io/library/nginx` and `DOCKER.IO/Nginx`, each with tag and digest
  as today (cases with `:1.27` and `@sha256:ABC` → tag `1.27`, digest `sha256:abc`).
- AC2: `docker.io/myuser/app`, `index.docker.io/myuser/app`, `REGISTRY-1.DOCKER.IO/MyUser/App` and `myuser/app` yield
  `docker.io` / `myuser/app`.
- AC3: `index.docker.io/ghcr.io/owner/app:1` and `registry-1.docker.io/ghcr.io/owner/app:1` yield `ghcr.io` /
  `owner/app` / `1`; `docker.io/ghcr.io` yields `docker.io` / `library/ghcr.io`.
- AC4: `localhost:5000/app`, `localhost/app`, `ghcr.io/owner/app` and `registry.example.com/app` keep their registry
  and repository (no `library/` outside Docker Hub); `INDEX.DOCKER.IO/Foo/Bar:RC1` yields `docker.io` / `foo/bar` /
  tag `RC1`.
- AC5: For every input of AC1–AC4, parsing `Format` of the parsed parts gives the same registry, repository, tag and
  digest.
- AC6: A runtime scan of a container started from `index.docker.io/company/api:1.0.0`, with a discovered image whose
  current version is `docker.io/company/api`, stores the container's snapshot on that discovered image's registry
  repository and records the derived finding "Own image uses an outdated .NET base runtime".
- AC7: Two observed images whose configuration labels name the base `docker.io/nginx:1.27` and `nginx:1.27`, both
  with the same base digest label, produce one `/base-images` entry (`SharedBaseImageQueryService.GetBaseImagesAsync`)
  with an observed image count of 2.
- AC8: `CanonicalizeTrackedStateAsync` returns the number of moved rows and:
  (a) moves an observed image on `docker.io` / `nginx` (tag, digest) to the canonical image version `docker.io` /
  `library/nginx` with the same tag and digest; a newly created version takes the old one's publication time and
  metadata; the old catalog entries still exist;
  (b) reuses an existing canonical image version without changing it;
  (c) moves a tag selection on `index.docker.io` / `company/api` to the registry repository `docker.io` / `company/api`;
  (d) a second run returns 0 and changes no row;
  (e) leaves rows of other registries and canonical `docker.io` rows unchanged;
  (f) leaves a row unchanged whose canonical repository fails the reference rules (a stored `docker.io` repository of
  250 characters without a namespace, which becomes 258 with `library/`);
  (g) leaves a row unchanged whose canonical registry fails the reference rules (a stored `index.docker.io` row whose
  repository is a `harbor…` host of more than 200 characters followed by `/app`);
  (h) moves a stored `index.docker.io` / `ghcr.io/owner/app` observed image to `ghcr.io` / `owner/app`;
  (i) moves two manual observed images (`docker.io` / `nginx` and `index.docker.io` / `library/nginx`, same tag and
  digest) onto one canonical version and keeps both;
  (j) moves a manual observed image on `index.docker.io` / `me/app` onto the version of an enabled discovered image on
  `docker.io` / `me/app` (same tag and digest) and leaves the discovered image unchanged.
- AC9: Startup initialization runs the canonicalization after the migrations, so a seeded non-canonical observed image
  points at the canonical version afterwards, and logs event 1009 (Information, "Moved {Count} tracked image
  references to canonical catalog entries") exactly when the count is above 0.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `ImageReferenceParserParseDockerIoSingleSegmentAddsLibraryNamespace`, `ImageReferenceParserParseIndexDockerIoNormalizesRegistry`, `ImageReferenceParserParseRegistryOneDockerIoNormalizesRegistry` (data-driven) | `src/Tests/DockerUpdateGuard.Tests/ImageReferenceParserTests.cs` |
| AC2 | `ImageReferenceParserParseNamespacedDockerHubSpellingReturnsCanonicalReference` | `src/Tests/DockerUpdateGuard.Tests/ImageReferenceParserTests.cs` |
| AC3 | `ImageReferenceParserParseWrappedRegistryBehindDockerHubAliasUnwraps`; the existing `ImageReferenceParserParseMirrorWrappedReferenceOfUnknownHostKeepsDockerHub` row `docker.io/ghcr.io` changes to `library/ghcr.io` | `src/Tests/DockerUpdateGuard.Tests/ImageReferenceParserTests.cs` |
| AC4 | `ImageReferenceParserParseOtherRegistryKeepsRegistryAndRepository` | `src/Tests/DockerUpdateGuard.Tests/ImageReferenceParserTests.cs` |
| AC5 | `ImageReferenceParserFormatCanonicalReferenceRoundTrips` | `src/Tests/DockerUpdateGuard.Tests/ImageReferenceParserTests.cs` |
| AC6 | `RuntimeContainerScanOrchestratorScanAllAsyncLinksAliasSpelledContainerToOwnImageAsync` | `src/Tests/DockerUpdateGuard.Tests/RuntimeContainerScanOrchestratorTests.cs` |
| AC7 | `ImageScanOrchestratorScanAsyncBaseImagesOfDockerHubSpellingsShareOneEntryAsync` (real `RegistryBaseImageResolver`, registry metadata substituted) | `src/Tests/DockerUpdateGuard.Tests/ImageScanOrchestratorTests.cs` |
| AC8 | `ImageCatalogCanonicalizerCanonicalizeTrackedStateAsyncMovesObservedImageToCanonicalVersionAsync` (a), `…ReusesExistingCanonicalVersionAsync` (b), `…MovesTagSelectionToCanonicalRepositoryAsync` (c), `…SecondRunChangesNothingAsync` (d), `…KeepsOtherRegistriesAsync` (e), `…KeepsRowWithInvalidCanonicalRepositoryAsync` (f), `…KeepsRowWithInvalidCanonicalRegistryAsync` (g), `…MovesWrappedRegistryRowToUnwrappedRegistryAsync` (h), `…KeepsBothManualImagesOnSharedVersionAsync` (i), `…MovesManualImageOntoDiscoveredImageVersionAsync` (j), on `SqliteTestDatabase`; (f) and (g) assert the stored row and the absence of a new catalog entry, since SQLite enforces no column length | `src/Tests/DockerUpdateGuard.Tests/ImageCatalogCanonicalizerTests.cs` |
| AC9 | `ApplicationInitializationExtensionsInitializeDockerUpdateGuardCanonicalizesTrackedImageReferencesAsync`, `ApplicationInitializationExtensionsInitializeDockerUpdateGuardLogsCanonicalizedReferenceCountAsync` (rows with and without a seeded row) | `src/Tests/DockerUpdateGuard.Tests/ApplicationInitializationExtensionsTests.cs` |

## Approach

**Parser** (`ImageReferenceParser.Parse`): in the branch where the first segment looks like a registry, a first segment
equal (ordinal, ignoring case) to one of `DockerHubRegistryHosts` (`docker.io`, `index.docker.io`,
`registry-1.docker.io`) becomes `docker.io`. `NormalizeWrappedRegistry` runs unchanged after it, so unwrapping works
behind every alias. Then, when the registry is `docker.io` and the repository contains no `/`, it is prefixed with
`library/` (this subsumes today's single-segment branch). Tag, digest and `Format` stay as they are. A private
`IsDockerHubRegistryHost` reads the one list; `DockerHubHosts` is not reused because it also lists web hosts, which are
no registry in a reference.

**Canonicalizer** (`Images/ImageCatalogCanonicalizer.cs`, static like `ContainerActionRunReconciler`): builds an
`ImageCatalogRepository` and an `ImageReferenceParser` on the given context, then:
1. loads, tracked, only rows the new rules can change: observed images (with version and repository) and tag
   selections (with repository) whose registry is `index.docker.io` or `registry-1.docker.io`, or `docker.io` with a
   repository without `/` — one EF query each, empty once everything is canonical;
2. computes each row's canonical registry and repository by parsing `registry/repository` (one private helper for both
   kinds) and skips the row when the pair equals the stored one or when
   `RegistryReferenceValidator.IsValidRegistry` or `IsValidRepository` rejects the canonical registry or repository
   (this bounds both columns, 200 and 255 characters, and the grammar, before any write);
3. an observed image gets `GetOrCreateImageVersionAsync(canonical registry, canonical repository, tag, digest,
   PublishedAtUtc, MetadataJson)` as its current version, whoever else already tracks that version (record 0053); a tag
   selection gets `GetOrCreateRegistryRepositoryAsync`'s id; nothing else (name, enabled flag, update time, history)
   changes;
4. saves pending moves at the end and returns the count. Get-or-create also saves as it goes (fact 22); the step is
   idempotent, so no test or document claims a single save.

It runs before any background job; a second instance starting at once is safe through get-or-create.

**Startup** (`ApplicationInitializationExtensions.InitializeDockerUpdateGuardAsync`): after
`ContainerActionRunReconciler`, before the inventory seeding, call the canonicalizer with `CancellationToken.None`
and log `TrackedImageReferencesCanonicalized` when the count is above 0, following the pattern of the
reconciled-runs log.

**Containers:** no code change; their one-time loss of carried-over version tags (fact 20) is accepted (record 0053).

No new user-visible text but the AC9 log message.

## Options considered

The data-side alternatives and why they lost are options 2–7 of record 0053 (full merge, no data correction, SQL
migration `Update8`, normalizing on comparison, skipping or merging colliding observed images, carrying container
tags across versions). One more: sharing `DockerHubHosts` for the alias list lost because it includes web hosts
(`hub.docker.com`, `docker.com`), which the issue excludes from references.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Images/ImageReferenceParser.cs` | alias mapping, `library/` rule, `DockerHubRegistryHosts` |
| DockerUpdateGuard | `Images/ImageCatalogCanonicalizer.cs` (new) | startup canonicalization |
| DockerUpdateGuard | `ApplicationInitializationExtensions.cs` | new startup step and log |
| DockerUpdateGuard | `HostLoggingExtensions.cs` | event 1009 |

## Signatures (for the Dev's skeleton)

- `internal static IReadOnlyList<string> DockerHubRegistryHosts { get; }` on `ImageReferenceParser` (new region
  `Properties`; values lowercase, built once)
- `public static class ImageCatalogCanonicalizer` in namespace `DockerUpdateGuard.Images`
- `public static async Task<int> CanonicalizeTrackedStateAsync(DockerUpdateGuardDbContext dbContext, CancellationToken cancellationToken)`
- `public static partial void TrackedImageReferencesCanonicalized(this ILogger logger, int count);` in
  `HostLoggingExtensions`, `[LoggerMessage(EventId = 1009, Level = LogLevel.Information, Message = "Moved {Count} tracked image references to canonical catalog entries")]`
  (source-generated: written complete in the skeleton)

Files the skeleton rewrites: `ImageReferenceParser.cs`, `HostLoggingExtensions.cs`, new `ImageCatalogCanonicalizer.cs`.
Names checked against *Writing code*.

## Test files

`src/Tests/DockerUpdateGuard.Tests/ImageReferenceParserTests.cs`, `RuntimeContainerScanOrchestratorTests.cs`,
`ImageScanOrchestratorTests.cs`, `ImageCatalogCanonicalizerTests.cs` (new), `ApplicationInitializationExtensionsTests.cs`
(all under `src/Tests/DockerUpdateGuard.Tests/`).

Existing test call sites of a changed signature: none. Existing tests whose input a criterion handles differently
(fact 19), adapted by the Tester in step 5: `ImageReferenceParserParseDockerHubSpellingKeepsSpelledRegistryAndRepository`
is removed (AC1 contradicts it), and the `docker.io/ghcr.io` row of
`ImageReferenceParserParseMirrorWrappedReferenceOfUnknownHostKeepsDockerHub` expects `library/ghcr.io` (AC3). The
existing `ApplicationInitializationExtensionsTests` run on an empty InMemory database and stay unchanged.

Files no unit test can cover: none.

## Areas

- `docs/areas/image-catalog.md` (+260 words)
- `docs/areas/hosting.md` (+50 words)
- `docs/areas/telemetry.md` (+0 words)
- `docs/areas/vulnerabilities.md` (+20 words)
- `docs/areas/update-detection.md` (+50 words)

## Documentation updates

All by the Dev, against the code as built:

- `docs/areas/image-catalog.md`, *Image references*: the registry bullet names the three Docker Hub hosts that become
  `docker.io`; the repository bullet gives `library/` to every `docker.io` repository without a namespace (any
  spelling) and to no other registry; the mirror bullet applies after that mapping; the bullet "Docker Hub spellings are
  not unified" is replaced by the canonical-form statement. *The catalog* (or a new subsection on the startup move to
  canonical catalog entries): which rows move (only those the new rules change, possibly to another registry such as
  `ghcr.io`), the skip of a row whose canonical registry or repository fails the reference rules, that no entry or
  observed image is deleted and two observed images may then share a version (also a manual one with an enabled
  discovered one), and what is empty until the next scans: base images and, on a new version, the vulnerability
  assessment of a moved observed image; a moved tag selection applies from the container's next runtime scan.
  *Known gaps*: #275 entry removed. *Related decisions*: 0053.
- `docs/areas/update-detection.md`, at the carry-over bullets (lines 52-58): a container whose reference spelling
  became canonical gets a new image version once, so nothing is carried over in that scan (record 0053).
- `docs/areas/hosting.md`, *Startup sequence*: a new step after "Reconcile interrupted container actions" linking
  Image catalog, with the count logged when above zero; following step renumbered.
- `docs/areas/telemetry.md`, *Logs*: startup and database events `1000`–`1009`.
- `docs/areas/vulnerabilities.md`, *Docker Scout* (line 135): every Docker Hub spelling counts as `docker.io` after
  parsing, linking Image catalog.
- `docs/ARCHITECTURE.md` §3: "four steps" becomes five with the canonicalizer step, linking record 0053.
- Swept, no change: `base-images.md`, `registries.md`, `README.md`, `.squad/project.md`; no changelog exists.

## Architecture check

One catalog write path kept (entries only through `ImageCatalogRepository`, still exact); the two rewritten columns
change before any job runs; no migration; layering holds. Startup sequence: `docs/ARCHITECTURE.md` §3 and Hosting.

## Security considerations

- [x] Limits: before any write, the canonicalizer checks the canonical registry (at most 200 characters, host grammar)
  and repository (at most 255, path grammar) with `RegistryReferenceValidator`, the rules remote references meet. Both
  count UTF-16 code units of parsed strings, no decoding. A failing row stays unchanged, so no stored row can
  overflow a column or fail startup.
- [x] Exceptions: the parser throws the same `ArgumentException` as today. Any other database error in the
  canonicalizer propagates and fails startup through the existing startup error logging; no page shows it. The new
  log carries only a count. Trivy and Docker Scout receive references of the same shape as today's `docker.io` ones.

## Decision records

- `docs/decisions/0053-tracked-state-moves-to-the-canonical-catalog-entries-at-startup.md` (Proposed)
- `docs/decisions/0048-…` (unreleased): consequence naming #275 edited in place to link 0053.

## Challenge

1. Major, collisions of moved observed images — accepted. The rule is to move anyway and keep both, with no deletion
   and no disabling: skipping would leave the row non-canonical for good, and merging deletes history (record 0053,
   option 6). AC8 (i) covers manual with manual and AC8 (j) covers manual onto an enabled discovered image. Image catalog
   documents the shared version.
2. Major, containers lose carried-over version tags — accepted as a documented one-time consequence (record 0053,
   option 7; `docs/areas/update-detection.md`). Fact 20 is added, and the claim that containers need no work is
   corrected in *Approach* and 0053.
3. Minor, "saves once" — accepted. Approach step 4 now says get-or-create saves as it goes and the step is
   idempotent.
4. Minor, the no-op start query — accepted. Step 1 reads only the aliases and `docker.io` repositories without `/`.
5. Minor, a move can target another registry — accepted. The log, the record title and file name, and the docs say
   "canonical catalog entries", and AC8 (h) covers `index.docker.io/ghcr.io/…` moving to `ghcr.io`.
6. Minor, blank vulnerability data and tag selection until the next scans — accepted. Both are listed in the Image
   catalog update and in 0053.

## Security review

1. Blocking, unchecked canonical registry length — fixed. Approach step 2 skips a row whose canonical registry or
   repository fails `RegistryReferenceValidator`, replacing the 300-character repository check. AC8 (f) and (g) test
   the decision on SQLite, and *Security considerations* and 0053 are updated.
2. Non-blocking, references the regular paths would reject — fixed now by the same skip, as Security recommended:
   the canonical repository is bounded at 255 characters and by the grammar.

## Out of scope / follow-ups

- Registry spellings with a port (`docker.io:443/…`) and the web hosts stay separate registries.
- Historical rows on old entries are not merged; they age out (record 0053).
- Digest-less base images stay grouped per source reference (fact 6, by design).
