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
Existing installations hold such entries; two kinds of tracked rows never re-parse (facts 10, 12).

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
| 9 | The vulnerability refresh re-parses the stored entry, so a legacy `index.docker.io` entry reaches Docker Scout as `docker.io` after the fix | `VulnerabilityEnrichmentService.cs:313` | confirmed |
| 10 | A manual observed image's current version never moves; its scan only refreshes metadata | `ImageScanOrchestrator.cs:701-709` | confirmed |
| 11 | A tag selection is unique per Docker instance and container, so a selection on the canonical repository beside the old one cannot exist; the issue's "drop the non-canonical selection" case never occurs | `RuntimeContainerTagSelectionConfiguration.cs` unique index (`DockerInstanceId`, `ContainerId`) | refuted |
| 12 | A tag selection applies only while it points at the latest snapshot's registry repository | `ApplicationViewService.cs:1998-2002`; written at `RuntimeContainerTagSelectionService.cs:70` | confirmed |
| 13 | Base-image relationships belong to the child image version and are replaced on the next successful observed-image scan | `ImageScanOrchestrator.cs:483-486`, `:570-578` | confirmed |
| 14 | Docker's reference library maps `index.docker.io` to `docker.io` and adds `library/` to any single-component `docker.io` path (`docker.io/ghcr.io` → `docker.io/library/ghcr.io`); it does not map `registry-1.docker.io` (the issue adds it) | `distribution/reference` v0.6.0 `normalize.go:126-170`, read in a scratch clone | confirmed |
| 15 | Repo-digest matching compares parsed registry and repository, so an aliased container reference missed its repo digest when Docker reported several; the fix repairs it (related defect, fixed as a side effect) | `DockerInstanceClient.cs:99-137` | confirmed |
| 16 | Event ID 1009 is free; startup events are documented as `1000`–`1008` | grep of `EventId = 10`; `docs/areas/telemetry.md:67` | confirmed |
| 17 | Remote references are validated after parsing (repository at most 255), so a single-segment name of 248–255 characters becomes invalid with `library/` (accepted: Docker's own form) | `BaseImageLabelReader.cs:39-41`, `RuntimeContainerScanOrchestrator.cs:697` | confirmed |
| 18 | Record 0048 is unreleased and names #275 in a consequence; edited in place | `git tag --contains` empty for its commit | confirmed |
| 19 | Only two existing tests pin the old behavior | grep of the test projects for non-canonical Docker Hub spellings: `ImageReferenceParserTests.cs:130-132`, `:189` | confirmed |

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
- AC8: `CanonicalizeTrackedStateAsync`: (a) moves an observed image whose current version is on a non-canonical Docker
  Hub entry to the canonical image version (same tag and digest; a new one takes the old one's publication time and
  metadata) and deletes no catalog entry; (b) reuses an existing canonical image version unchanged; (c) moves a tag
  selection to the canonical registry repository; (d) a second run returns 0 and changes no row; (e) leaves rows of
  other registries unchanged; (f) leaves a row unchanged whose canonical repository is longer than 300 characters. It
  returns the number of moved rows.
- AC9: Startup initialization runs the canonicalization after the migrations, so a seeded non-canonical observed image
  points at the canonical version afterwards, and logs event 1009 (Information, "Moved {Count} tracked image
  references to canonical Docker Hub catalog entries") exactly when the count is above 0.

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
| AC8 | `ImageCatalogCanonicalizerCanonicalizeTrackedStateAsyncMovesObservedImageToCanonicalVersionAsync` (a), `…ReusesExistingCanonicalVersionAsync` (b), `…MovesTagSelectionToCanonicalRepositoryAsync` (c), `…SecondRunChangesNothingAsync` (d), `…KeepsOtherRegistriesAsync` (e), `…KeepsOverLongCanonicalRepositoryAsync` (f), on `SqliteTestDatabase` | `src/Tests/DockerUpdateGuard.Tests/ImageCatalogCanonicalizerTests.cs` |
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
1. loads the observed images (tracked) whose current version's registry is in `DockerHubRegistryHosts`, with version
   and repository, and the tag selections whose repository's registry is in that list — two filtered queries, so a
   start with nothing left costs two small queries;
2. computes each row's canonical registry and repository by parsing `registry/repository` (one private helper for both
   kinds); a row whose canonical pair equals the stored one, or whose canonical repository is longer than 300
   characters (the stored limit, a private constant), is skipped;
3. an observed image gets `GetOrCreateImageVersionAsync(canonical registry, canonical repository, tag, digest,
   PublishedAtUtc, MetadataJson)` as its current version; a tag selection gets `GetOrCreateRegistryRepositoryAsync`'s
   id; nothing else (name, update time, history) changes;
4. saves once when anything moved and returns the count.

It runs before any background job, so no owner of these rows writes concurrently; a second instance starting at once
is safe through get-or-create and identical updates.

**Startup** (`ApplicationInitializationExtensions.InitializeDockerUpdateGuardAsync`): after
`ContainerActionRunReconciler`, before the inventory seeding, call the canonicalizer with `CancellationToken.None`
and log `TrackedImageReferencesCanonicalized` when the count is above 0 (same pattern as the reconciled-runs log).

No new user-visible text besides the log message fixed in AC9.

## Options considered

- **Targeted startup reconciliation in C#** — chosen; see record 0053.
- **Full merge of duplicates** — unique-index collisions (relationships, active vulnerability findings, image
  versions), history rewrite, deletes catalog entries.
- **Do nothing for existing data** — manual images stay non-canonical; tag selections silently stop applying (fact 12).
- **SQL data migration `Update8`** — repeats the parser rules in SQL, bypasses get-or-create, untestable here.
- **Normalize on comparison instead of in the parser** — every reader would need it; breaks the catalog's
  exact-comparison contract.
- **Share `DockerHubHosts` for the alias list** — it includes web hosts (`hub.docker.com`, `docker.com`), which the
  issue excludes from references.

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
  `HostLoggingExtensions`, `[LoggerMessage(EventId = 1009, Level = LogLevel.Information, Message = "Moved {Count} tracked image references to canonical Docker Hub catalog entries")]`
  (source-generated: written complete in the skeleton)

Files the skeleton rewrites: `ImageReferenceParser.cs`, `HostLoggingExtensions.cs`, new `ImageCatalogCanonicalizer.cs`.
Names checked against *Writing code*: no nested type, at most two parameters, no keyword names.

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

- `docs/areas/image-catalog.md` (+180 words)
- `docs/areas/hosting.md` (+50 words)
- `docs/areas/telemetry.md` (+0 words)
- `docs/areas/vulnerabilities.md` (+20 words)

## Documentation updates

All by the Dev, against the code as built:

- `docs/areas/image-catalog.md`, *Image references*: the registry bullet names the three Docker Hub hosts that become
  `docker.io`; the repository bullet gives `library/` to every `docker.io` repository without a namespace (any
  spelling) and to no other registry; the mirror bullet applies after that mapping; the bullet "Docker Hub spellings are
  not unified" is replaced by the canonical-form statement. *The catalog* (or a new subsection): the startup move of
  tracked state — which rows move, what stays, no deletion, the 300-character skip, base images of a moved observed
  image return with its next scan. *Known gaps*: the #275 entry is removed. *Related decisions*: 0053.
- `docs/areas/hosting.md`, *Startup sequence*: a new step after "Reconcile interrupted container actions" linking
  Image catalog, with the count logged when above zero; following step renumbered.
- `docs/areas/telemetry.md`, *Logs*: startup and database events `1000`–`1009`.
- `docs/areas/vulnerabilities.md`, *Docker Scout* (line 135): every Docker Hub spelling counts as `docker.io` after
  parsing, linking Image catalog.
- `docs/ARCHITECTURE.md` §3: "four steps" becomes five with the canonicalizer step, linking record 0053.
- Swept, no change: `docs/areas/base-images.md` (grouping unchanged, no old-behavior statement), `docs/areas/registries.md`
  (host list and `library/` addressing still true), `README.md`, `.squad/project.md`; no changelog exists.

## Architecture check

The catalog keeps one write path: the canonicalizer creates entries only through `ImageCatalogRepository`, which still
compares exactly. It rewrites two tracked columns owned by registration and the tag-selection service, before any job
runs, so no owner writes concurrently. No migration, so migration serialization is untouched; layering holds (host
uses Data). The startup sequence changes: `docs/ARCHITECTURE.md` §3 and Hosting are updated.

## Security considerations

- [x] Limits: the 300-character skip counts UTF-16 code units of the parsed repository; no decoding is involved.
  Remote references keep their 255-character validation after parsing (fact 17).
- [x] Exceptions: the parser throws the same `ArgumentException` as today. A database error in the canonicalizer
  propagates and fails startup through the existing startup error logging; no page shows it. The new log carries only
  a count. Trivy and Docker Scout receive references of the same shape as today's `docker.io` ones.

## Decision records

- `docs/decisions/0053-tracked-state-moves-to-the-canonical-docker-hub-catalog-entries-at-startup.md` (Proposed)
- `docs/decisions/0048-…` (unreleased): consequence naming #275 edited in place to link 0053.

## Out of scope / follow-ups

- Registry spellings with a port (`docker.io:443/…`) and the web hosts stay separate registries.
- Historical rows on old entries are not merged; they age out (record 0053).
- Digest-less base images stay grouped per source reference (fact 6, by design).

## Challenge notes

Devil's Advocate, plan challenge of `/home/user/DockerUpdateGuard/specs/issue-275/plan.md` (with record `/home/user/DockerUpdateGuard/docs/decisions/0053-tracked-state-moves-to-the-canonical-docker-hub-catalog-entries-at-startup.md`) for issue #275, against origin/main at 049dfc4.

The parser approach holds up. The facts I checked are right: parser lines 130-146, exact lookup at `ImageCatalogRepository.cs:108-111` and `:124-125`, event 1009 is free, startup runs before `app.RunAsync` (`Program.cs:43-46`), the unique index on tag selections, and the digest-only merge in `SharedBaseImageQueryService.BuildGroupingKey`. AC6 and AC7 fail on today's code. The objections are about the data side: what the move does to rows it lands on, and what the parser change does to containers.

## Major

1. **Moving observed images can leave two images on one version, a state registration prevents. The plan has no rule and no criterion for it.**
   - Registration keeps at most one manual observed image per image version. It refuses a reference that an enabled discovered image already tracks (`ImageRegistrationService.cs:126-145`; `docs/areas/image-catalog.md:78-82`, `:105-107`; record 0048).
   - The canonicalizer moves `CurrentImageVersionId` with no check of who already sits on the target version. Two cases follow:
     - A manual `nginx:1.27` and a manual `docker.io/nginx:1.27` end up as two manual images on one version.
     - A manual `index.docker.io/me/app:1` lands on the version of the enabled discovered image `docker.io/me/app:1`.
   - Effects: `/observed-images` shows duplicates, the base-image overview counts each image separately (`SharedBaseImageQueryService.cs:137-139`), and the same version is scanned twice.
   - Why major: the plan would create a state that the documented registration rule (record 0048) forbids, without deciding on it.
   - Needed: the Lead states the rule (keep both and document it in Image catalog and 0053, or skip/merge the colliding row). AC8 gets a case for each collision kind: manual with manual, and manual with an enabled discovered image.

2. **Containers lose their carried-over version tags when they first move to the canonical version (a documented guarantee the plan does not mention).**
   - The plan and 0053 say containers "re-parse by themselves" and need no work. But the runtime scan only carries version tags forward while the image version id stays the same:
     - `CreateSnapshot` (`RuntimeContainerScanOrchestrator.cs:178-196`) carries `ResolvedVersionTag` and `AvailableUpdateVersionTag` only when `previous.Snapshot.ImageVersionId == imageVersionId`.
     - `GetPreviousResolvedVersionTagAsync` (`:1385-1389`) filters on `entity.ImageVersionId == imageVersionId`.
   - After the parser change, a container started from `docker.io/nginx:latest` or `index.docker.io/...` gets a new image version id on its next scan. If its running digest has dropped out of the tag list, it loses its resolved version for good.
   - This is the guarantee in `docs/areas/update-detection.md:52-58`; it is not in the plan, the documentation sweep or 0053.
   - Why major: a sibling path of the same defect, and the plan's "no extra work for containers" claim is refuted.
   - Needed: either add a criterion and a fix (for example, the carry-over lookups also accept a previous snapshot whose registry/repository is the non-canonical spelling of the same reference), or record the one-time loss as an accepted consequence in 0053 and `docs/areas/update-detection.md`.

## Minor

3. **"Saves once when anything moved" (Approach step 4) is not what the code does.** `GetOrCreateRegistryRepositoryAsync` and `GetOrCreateImageVersionAsync` call `SaveChangesAsync` themselves when they create a row (`ImageCatalogRepository.cs:147`, `:209`). That also flushes the earlier moves pending on the same context. Do not write or test for a single save. The step stays idempotent, so a partial run is fine; say so in the area text instead of "saves once".

4. **The no-op start is not "two small queries".** Step 1 filters on the registry only, so on every start it loads every observed image and tag selection on `docker.io`, including all discovered ones. Narrow the query to rows that can change: registry is `index.docker.io` or `registry-1.docker.io`, or registry is `docker.io` and the repository has no `/`. Otherwise drop the "small" claim.

5. **The move can target a registry other than Docker Hub.** A stored `index.docker.io` / `ghcr.io/owner/app` entry (written before this fix, never unwrapped) re-parses to `ghcr.io` / `owner/app`. So the log text "to canonical Docker Hub catalog entries", the 0053 title and the area text are inaccurate for that row. Add an AC8 case for a wrapped `index.docker.io/ghcr.io/...` row moving to `ghcr.io`, and word the log and docs as "canonical catalog entries".

6. **The moved observed image is blank in two places until the next scans; document both.**
   - A newly created canonical version has no vulnerability findings or assessment until the next vulnerability refresh. The detail page reads findings by `CurrentImageVersionId` (`ApplicationViewService.cs:1893`).
   - A moved tag selection does not apply until the container's next runtime scan stores a snapshot on the canonical repository (`ApplicationViewService.cs:1998-2002`).
   - In the Image catalog subsection on the startup move, state both alongside the existing base-image sentence.

## Security notes

Security plan review for issue #275 (mode `plan`)

I read `specs/issue-275/plan.md`, `docs/decisions/0053-tracked-state-moves-to-the-canonical-docker-hub-catalog-entries-at-startup.md`, `.squad/project.md` (*Security areas*), and the code the plan relies on: `ImageReferenceParser.cs`, `ImageCatalogRepository.cs`, `RegistryReferenceValidator.cs`, `ImageRegistrationService.cs`, the entity configurations, `DockerHubClient.cs`/`DockerHubHosts.cs` and `ApplicationInitializationExtensions.cs`. I ran no measurement. No claim here depends on measured or library behavior. The column limits come from the EF configurations, and how PostgreSQL treats an over-long `varchar` value is standard behavior.

**Tier and areas**
- `standard` is the right tier.
- The parser change maps aliases and unwraps prefixes. It adds, changes or removes no guard on what can be reached.
- Credentials go to the same place as before. `DockerHubClient.IsSupportedRegistry` already accepts every alias through `DockerHubHosts.IsDockerHubHost`, and `OciRegistryClient` has no `docker.io` special case.
- Unwrapping behind `index.docker.io`/`registry-1.docker.io` sends a request to a host that `docker.io/<host>/…` and a direct `<host>/…` reference can already name today.
- Docker Scout's `docker.io`-only check is unchanged.
- The two areas listed are complete: Database and migrations, and Logging and telemetry with a count-only event 1009.

**Findings**

1. [BLOCKING] The canonicalizer writes the new registry without the length check that the normal paths apply, so one stored row can make every startup fail.
   - Evidence: plan, *Approach* step 2 and AC8(f), skips only rows whose canonical **repository** is over 300 characters. The `Registry` column is `HasMaxLength(200)` (`src/DockerUpdateGuard.Data/Configurations/RegistryRepositoryConfiguration.cs:25`). `ImageCatalogRepository.ValidateRegistryRepository` (`ImageCatalogRepository.cs:41-52`) checks only for blank values.
   - How it happens: with the new parser, a stored row with registry `index.docker.io` (or `registry-1.docker.io`) and repository `harbor<~200 chars>.io/app` unwraps through `IsKnownWrappedRegistry` (any segment with "harbor" and a dot) to a registry of more than 200 characters.
   - Today such rows can exist two ways:
     - **Manual registration** (`ImageRegistrationService.cs:112-123`) parses and stores without `RegistryReferenceValidator`. Old parser result: registry `index.docker.io` (15 characters), repository about 210 characters, under the 300 limit.
     - **Tag selections** store a runtime container's repository. That repository came from a container image reference, which anyone who can create a container on a monitored host controls. It passed `ValidReferenceOrNull` (`RuntimeContainerScanOrchestrator.cs:698`), because the repository is at most 255 characters and the regex allows dots.
   - Effect on PostgreSQL: `GetOrCreateRegistryRepositoryAsync` saves, the save throws `DbUpdateException` (value too long), and the catch (`ImageCatalogRepository.cs:152-160`) then runs `SingleAsync` on a row that does not exist. That throws `InvalidOperationException`, which per decision record 0053 fails startup. It fails again on every restart until someone edits the database by hand.
   - In the regular paths the same reference is rejected per request: the runtime scan validates it, and manual registration fails only that request.
   - The SQLite tests the plan names cannot catch this, because SQLite does not enforce `varchar` length.
   - Smallest fix: in step 2, skip a row whose canonical reference fails the reference rules, i.e. `RegistryReferenceValidator.IsValidRegistry(registry) == false || IsValidRepository(repository) == false`. At minimum, skip a registry over 200 characters alongside the repository limit of 300. Add an AC8(g) with its test: a stored `index.docker.io` row whose unwrapped registry is longer than 200 characters stays unchanged. The test can only check the skip on SQLite, so it must check the decision, not rely on a database error.
   - Also update *Security considerations*, which names only the 300-character limit, and the consequence in decision record 0053 ("A tracked row whose canonical repository would exceed the stored length…"). It should say canonical registry and repository.

2. [NON-BLOCKING] The canonicalizer can store references that no regular path would store.
   - Fact 17 accepts that a single-segment name of 248–255 characters becomes invalid with `library/`, because remote references are rejected after parsing. The canonicalizer, however, would write such a 256–263 character repository (still under 300) into the catalog. The same applies to an unwrapped host that fails the registry grammar, such as one with `_` (allowed in a repository segment, not in a host).
   - Later registry calls then fail at their own validation (`OciRegistryClient.cs:477`, `:1479`), so this is not exploitable, only inconsistent.
   - Smallest fix: the validator-based skip from finding 1 covers it. Recommendation: fix now together with finding 1.

**Checked, no finding**
- Queries are EF LINQ with a constant host list, so there is no SQL built from input.
- The 300-character comparison counts UTF-16 code units of strings that are already decoded.
- Event 1009 carries only an `int`.
- A database error still fails startup the same way as the other startup steps, and no page shows it.
- A second instance starting at the same time: a race in get-or-create falls back to reading the winner's row (`ImageCatalogRepository.cs:152-160`, `:214-224`). That holds only once finding 1 is fixed, because otherwise the fallback lookup throws.
- The canonicalizer runs after `DatabaseMigrator.MigrateAsync` and before any background job (`ApplicationInitializationExtensions.cs:41-58`).

Files referenced:
- /home/user/DockerUpdateGuard/specs/issue-275/plan.md
- /home/user/DockerUpdateGuard/docs/decisions/0053-tracked-state-moves-to-the-canonical-docker-hub-catalog-entries-at-startup.md
- /home/user/DockerUpdateGuard/src/DockerUpdateGuard/Images/ImageReferenceParser.cs
- /home/user/DockerUpdateGuard/src/DockerUpdateGuard/Images/Helper/RegistryReferenceValidator.cs
- /home/user/DockerUpdateGuard/src/DockerUpdateGuard/Images/ImageRegistrationService.cs
- /home/user/DockerUpdateGuard/src/DockerUpdateGuard.Data/Repositories/ImageCatalogRepository.cs
- /home/user/DockerUpdateGuard/src/DockerUpdateGuard.Data/Configurations/RegistryRepositoryConfiguration.cs
