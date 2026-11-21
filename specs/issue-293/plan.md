# Plan: Remove unused data-model values and dead code paths

Source: Issue #293
Tier: standard — removes enum members, a UI formatter arm and a dashboard hint (visible behavior for stored values no code writes), without creating, changing or removing a security control.
Security areas touched: Database and migrations — only enum members under `src/DockerUpdateGuard.Data/Entities/` are removed; no query, configuration, migration or schema changes.

## Problem / root cause

Members of four stored enums are never produced, and the dashboard's *Trivy base URL missing* hint can only appear
in a state where it is wrong:

- `ImageVersionSource.RegistryMetadata = 4` (`src/DockerUpdateGuard.Data/Entities/ImageVersionSource.cs:31`),
  `ImageRelationshipType.BuildStage = 2` (`ImageRelationshipType.cs:21`), `UpdateFindingType.BaseImageUpdate = 1`
  (`UpdateFindingType.cs:16`), `VulnerabilitySource.RegistryAdvisory = 3` / `Manual = 4` (`VulnerabilitySource.cs:26`,
  `:31`) have no writer; the UI formats them (`src/DockerUpdateGuard/UI/ApplicationViewService.cs:689`, `:727-728`).
- The hint (`UI/VulnerabilityConfigurationHintKind.cs:21`, `UI/VulnerabilityConfigurationHintResolver.cs:40-44`,
  `Components/Pages/Dashboard.razor:127-132`) needs `Enabled` and provider `Trivy` now, which validation only lets
  through with a `TrivyBaseUrl` (`Configuration/DockerUpdateGuardOptionsValidator.cs:193-198`, also on reload:
  `docs/areas/hosting.md`, *Configuration reload*): whenever it shows, it asks for a URL that is already set.

The Trivy provider's own *not configured* branch (`Vulnerabilities/TrivyVulnerabilityProvider.cs:330-335`) is **not**
dead and stays (fact 6).

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | The enums are stored as plain integers: no conversion, value list or check constraint on them; removing members changes neither model nor snapshot | `DockerUpdateGuardDbContextModelSnapshot.cs:185`, `:263`, `:316`, `:337`, `:374`, `:678` (`Property<int>`); the only `HasConversion` calls are digest converters, the only check constraints `CK_ImageRelationships_*` (`Configurations/ImageRelationshipConfiguration.cs:47-48`) | confirmed |
| 2 | `ImageVersionSource` is written only as `ObservedImage`, `RuntimeContainer`, `BaseImageResolution`, on every get-or-create by those components, and read by no production code | `ImageRegistrationService.cs:141`, `DockerHubAccountImageDiscoveryService.cs:220`, `RuntimeContainerScanOrchestrator.cs:717`, `ImageScanOrchestrator.cs:552`; grep of `ImageVersionSource` / `.Source` (the issue's line numbers are stale) | confirmed |
| 3 | `ImageRelationshipType.BuildStage` has no writer; every reader filters on `BaseImage` | writer `ImageScanOrchestrator.cs:575`; readers `ImageScanOrchestrator.cs:484`, `ApplicationViewService.cs:1193`, `ApplicationTelemetry.cs:151`, `LiveImageInventoryQueryService.cs:99`, `SharedBaseImageQueryService.cs:95`, `:186` | confirmed |
| 4 | `UpdateFindingType.BaseImageUpdate` has no writer | writers `RuntimeContainerScanOrchestrator.cs:1125-1126`, `:1278`, `:1329`, `ImageScanOrchestrator.cs:389`, `:438` | confirmed |
| 5 | Vulnerability sources come from the provider resolved for the refresh: `Trivy`, `DockerScout` or `NotSet` | `TrivyVulnerabilityProvider.cs:320`, `DockerScoutVulnerabilityProvider.cs:570`, `DefaultVulnerabilityProvider.cs:38`, used at `VulnerabilityEnrichmentService.cs:221`, `:346`, `:368`, `:457` (the issue cites `VulnerabilityEnrichmentService.cs:108-116`, which maps statuses) | confirmed |
| 6 | The Trivy *not configured* result is unreachable | Refuted. The provider is resolved once per refresh (`VulnerabilityEnrichmentService.cs:524`; `docs/areas/vulnerabilities.md`, *Provider*), but each Trivy call reads `CurrentValue` again (`TrivyVulnerabilityProvider.cs:327-328`). A valid reload during a refresh to Docker Scout, or to `Enabled` `false`, without `TrivyBaseUrl` gives every remaining Trivy call *not configured* (status *Not configured*, run *partial*); `VulnerabilityEnrichmentServiceTests.ReloadToDockerScout` models that configuration | refuted |
| 7 | The dashboard hint shows only with `Enabled`, provider `Trivy` and a latest run whose message holds "TrivyBaseUrl is not configured" | `VulnerabilityConfigurationHintResolver.cs:27-44`; an invalid reload makes `CurrentValue` throw, never serves the value (`hosting.md`, *Configuration reload*) | confirmed |
| 8 | `FormatUpdateFindingType` falls back to `type.ToString()`, `FormatVulnerabilitySource` to "Not set"; both private, reached through `GetObservedImageDetailAsync` (`MapUpdateFinding` `:1494`, `CreateVulnerabilityAssessment` `:314`) | `ApplicationViewService.cs:685-695`, `:721-730` | confirmed |
| 9 | Test sites of removed members: `ApplicationViewServiceTests.cs:452`, `:3015-3027`, `VulnerabilityEnrichmentServiceTests.cs:593`, `:1050`, `MappingTests.cs:251`, `DashboardRenderTests.cs:137-156` | grep; `VulnerabilityEnrichmentServiceTests.cs:1050` (`Manual`) is missing from the issue | confirmed |
| 10 | `ExternalOperationResult.NotConfigured` stays in use (Docker Scout, Portainer, Docker Hub) | issue; grep | confirmed |
| 11 | `docs/ARCHITECTURE.md` lists none of the removed values; `README.md`'s `TrivyBaseUrl` row stays true | `ARCHITECTURE.md` 5.1; `README.md:149` | confirmed |

## Acceptance criteria

- AC1: The stored enums have exactly these members and numbers: `ImageVersionSource` NotSet=0, ObservedImage=1,
  RuntimeContainer=2, BaseImageResolution=3; `ImageRelationshipType` NotSet=0, BaseImage=1; `UpdateFindingType`
  NotSet=0, RuntimeImageUpdate=2, TagRecommendation=3, DerivedBaseRuntimeUpdate=4; `VulnerabilitySource` NotSet=0,
  Trivy=1, DockerScout=2.
- AC2: A stored row holding a retired number (image version source 4, relationship type 2, finding type 1,
  vulnerability source 3 and 4) still loads, with that number.
- AC3: The observed image detail shows an update finding of stored type 1 with type text "1" (the `type.ToString()`
  fallback) and an assessment of stored source 3 or 4 with source "Not set"; the formatters have no arm for a removed
  member.
- AC4: `VulnerabilityConfigurationHintKind` is None=0, ScanningDisabled=1, DockerHubCredentialsMissing=3; with
  `Enabled` and provider `Trivy`, a latest run *partial* or *failed* whose message contains "TrivyBaseUrl is not
  configured" gives no hint (`None`); the dashboard has no Trivy banner; the other hints behave as before.
- AC5: The Trivy provider still answers a blank `TrivyBaseUrl` with *not configured* and the message "TrivyBaseUrl is
  not configured".
- AC6: No migration is added and the model snapshot is unchanged.
- AC7: The area documents and `docs/ARCHITECTURE.md` state the new behavior (*Documentation updates*); the *Known gaps*
  entries for #293 are gone.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `DockerUpdateGuardDbContextEnumColumnsKeepStoredNumbers` | `src/Tests/DockerUpdateGuard.Data.Tests/MappingTests.cs` |
| AC2 | `DockerUpdateGuardDbContextRetiredEnumNumbersLoadAsync` (passes on today's code: deferred to step 6) | `src/Tests/DockerUpdateGuard.Data.Tests/MappingTests.cs` |
| AC3 | `ApplicationViewServiceObservedImageDetailRetiredUpdateFindingTypeShowsNumberAsync`, `ApplicationViewServiceObservedImageDetailRetiredVulnerabilitySourceShowsNotSetAsync` (data rows 3 and 4) | `src/Tests/DockerUpdateGuard.Tests/ApplicationViewServiceTests.cs` |
| AC4 | `VulnerabilityConfigurationHintKindValuesKeepNumbers`; `ApplicationViewServiceDashboardTrivyNotConfiguredMessageReportsNoHintAsync` (replaces `…TrivyNotConfiguredReportsTrivyBaseUrlHintAsync`); the *Image build check*, run by the Dev (`Dashboard.razor` changes) | `src/Tests/DockerUpdateGuard.Tests/VulnerabilityConfigurationHintKindTests.cs`, `src/Tests/DockerUpdateGuard.Tests/ApplicationViewServiceTests.cs` |
| AC5 | `TrivyVulnerabilityProviderGetVulnerabilitiesAsyncReturnsNotConfiguredWithoutBaseUrlAsync` (exists; the Tester adds `[TestCategory("AC5")]`) | `src/Tests/DockerUpdateGuard.Tests/TrivyVulnerabilityProviderTests.cs` |
| AC6 | check: the Reviewer confirms `git diff --name-only <base>...HEAD -- src/DockerUpdateGuard.Data/Migrations` is empty | — |
| AC7 | check: the Reviewer reads the documents against *Documentation updates* | — |

The AC1 test describes each enum as `Name=number` pairs ordered by number, like
`ContainerActionTypeTests.DescribeStoredNumbers`, through one private generic helper in `MappingTests`
(`where TEnum : struct, Enum`), so the four asserts share it. The AC2 test saves each retired number by a cast
(`(UpdateFindingType)1`, …) on SQLite and reads it back in a cleared context.

## Approach

1. Tester (step 5): the tests above, and the existing call sites (*Test files*) moved to produced values, so the suite
   compiles before and after the removal.
2. Dev (step 6): delete the members of AC1 and `TrivyBaseUrlMissing`; delete the formatter arms
   (`ApplicationViewService.cs:689`, `:727-728`), the resolver's Trivy branch (`:40-44`) and the banner
   (`Dashboard.razor:127-132`, the following `else if` stays). XML documentation: each changed enum's summary says it
   is stored as an integer and names its retired numbers, never to be reused (decision 0055); `ImageVersion.Source`
   says it records which kind of component registered or last refreshed the version through the catalog,
   informational only, last writer wins, no logic may depend on it; the summary of `VulnerabilityProviderMessages`
   says the UI recognizes the Docker Scout message (the Trivy one stays for the provider). Regions unchanged.
3. `TrivyVulnerabilityProvider.cs`, `TrivyVulnerabilityProviderLogging.cs` (event 3420) and the Trivy message
   constant are not changed (fact 6).

No new user-visible output. No skeleton: nothing is added.

## Options considered

1. **Remove the Trivy *not configured* branch, as the issue asks, with the URL read in the `try`** — lost: the branch
   is reached by a reload during a refresh (fact 6); the remaining calls would end *failed* with "Trivy scan failed:
   unexpected error" and an error log with a stack trace instead of *not configured*.
2. **A data migration that rewrites retired numbers, or renumbering** — lost (decision 0055).
3. **Drop `ImageVersion.Source` instead of documenting it** — lost: a migration for a field the issue keeps.
4. **A permanent `HasPendingModelChanges` test on the Npgsql model for AC6** — lost here: no member removal enters the
   model (fact 1), so the read-only check settles it; a model-drift guard would be a change of its own.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard.Data | `Entities/ImageVersionSource.cs`, `ImageRelationshipType.cs`, `UpdateFindingType.cs`, `VulnerabilitySource.cs` | members removed, summaries |
| DockerUpdateGuard.Data | `Entities/ImageVersion.cs` | `Source` documentation |
| DockerUpdateGuard | `UI/ApplicationViewService.cs` | two formatter arms removed |
| DockerUpdateGuard | `UI/VulnerabilityConfigurationHintKind.cs`, `UI/VulnerabilityConfigurationHintResolver.cs`, `Components/Pages/Dashboard.razor` | Trivy hint removed |
| DockerUpdateGuard | `Vulnerabilities/VulnerabilityProviderMessages.cs` | class summary |

## Signatures (for the Dev's skeleton)

No new API. Deleted: `ImageVersionSource.RegistryMetadata`, `ImageRelationshipType.BuildStage`,
`UpdateFindingType.BaseImageUpdate`, `VulnerabilitySource.RegistryAdvisory`, `VulnerabilitySource.Manual`,
`VulnerabilityConfigurationHintKind.TrivyBaseUrlMissing`.

## Test files

`src/Tests/DockerUpdateGuard.Data.Tests/MappingTests.cs`, `src/Tests/DockerUpdateGuard.Tests/ApplicationViewServiceTests.cs`,
`src/Tests/DockerUpdateGuard.Tests/VulnerabilityConfigurationHintKindTests.cs` (new),
`src/Tests/DockerUpdateGuard.Tests/TrivyVulnerabilityProviderTests.cs`.

Existing test code using removed members, adapted by the Tester in step 5 (each value exists today):

- `ApplicationViewServiceTests.cs:452`: `BaseImageUpdate` → `RuntimeImageUpdate` (the dashboard counts stay 3 and 1).
- `VulnerabilityEnrichmentServiceTests.cs:593`: `BaseImageUpdate` → `RuntimeImageUpdate`.
- `VulnerabilityEnrichmentServiceTests.cs:1050`: `Manual` → `NotSet` — it must differ from both `Trivy` and
  `DockerScout`, or the assertion that the update writes the resolved source passes without a write.
- `MappingTests.cs:251`: `BaseImageUpdate` → `RuntimeImageUpdate` (summary text and its assertion kept or changed together).
- `ApplicationViewServiceTests.cs:3015-3027`: replaced by the AC4 test.
- `DashboardRenderTests.cs:137-156` (`DashboardWithMissingTrivyBaseUrlRendersWarningBanner`): deleted;
  `DashboardWithConfiguredVulnerabilityProviderRendersNoBanner` keeps asserting the URL key is absent.

Files no unit test can cover: none.

## Areas

- `docs/areas/image-catalog.md` (2030 words, +30 words): *The catalog* states the source semantics (registered or
  last refreshed, informational, last writer wins, no logic depends on it); the #293 known gap goes.
- `docs/areas/base-images.md` (1876 words, +0 words): the build-stage sentence under resolution and its known gap go.
- `docs/areas/update-detection.md` (2353 words, +20 words): the *base image update* sentences (findings, known gaps)
  go; a stored finding of retired type 1 is shown with its number.
- `docs/areas/vulnerabilities.md` (3407 words, +40 words): sources are *Trivy*, *Docker Scout* or *not set*, a stored
  retired source is shown as "Not set"; *Configuration hints* loses the Trivy hint; the *Not configured* row and a
  sentence under *Provider* say a reload during a refresh to a configuration without `TrivyBaseUrl` gives the
  remaining Trivy calls *not configured*; the known gap goes.
- `docs/areas/web-ui.md` (2882 words, +0 words): the Trivy line of *Vulnerability configuration hint* goes.

## Documentation updates

All by the Dev, against the built code: the five area documents above, and `docs/ARCHITECTURE.md` 5.1 after
"Every enum used by these entities lives alongside them": entity enums are stored as their integer number and a
removed member's number is never reused, linking decision 0055. No `README.md`, `INSTALL.md` or `.squad/` change;
the repository has no changelog.

## Architecture check

No guarantee of `.squad/project.md` is touched: the catalog write path, layering and fixed failure reasons are
unchanged; no schema change, so migrations are untouched.

## Security considerations

- [x] No limits.
- [x] None: no new exception path; the remaining texts are unchanged.

## Decision records

- `docs/decisions/0055-entity-enum-members-nothing-produces-are-removed-and-their-numbers-are-never-reused.md` (Proposed)

## Out of scope / follow-ups

- `DockerHubBaseImageResolver` dead code: #9.
- The issue's removal of the Trivy *not configured* branch, message and event 3420: not done (fact 6, option 1).
