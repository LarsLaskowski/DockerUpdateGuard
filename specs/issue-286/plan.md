# Plan: Shared base images view lists unshared bases and counts disabled observed images

Source: Issue #286
Tier: standard — a behavior change of the shared base images view, its dashboard tile and its navigation entry that creates, changes or removes no security control.
Security areas touched: none

## Problem / root cause

- `src/DockerUpdateGuard.Data/Queries/SharedBaseImageQueryService.cs:66` starts from all `ObservedImages` without an
  `IsEnabled` filter, and `:108-146` returns every group with no minimum number of users; `ObservedImageCount`
  (`:137-139`) counts disabled images.
- `src/DockerUpdateGuard/UI/ApplicationViewService.cs:891-897` (`GetBaseImagesCoreAsync`) loads every base-image
  relationship that points at a grouped base version, whatever its child; parents (`:941-944`) and containers
  (`:927-933`) come from these relationships, so stale versions of an observed image and disabled images contribute.
- The dashboard (`:1777`, `BaseImageCount = baseImages.Count` at `:1818`) and `HasBaseImagesAsync` (`:2187-2196`,
  used by `Components/Layout/NavMenu.razor.cs:137`) count the same unfiltered list.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | The query has no `IsEnabled` filter and no minimum; disabled images are counted | `SharedBaseImageQueryService.cs:66-146` | confirmed |
| 2 | The page loads all relationships of the grouped bases and derives parents and containers from them | `ApplicationViewService.cs:891-897`, `:927-944` (issue cites `:885-893`, `:920-938`: lines moved, behavior as described) | confirmed |
| 3 | Dashboard tile and navigation use the same unfiltered list | `ApplicationViewService.cs:1777`, `:1818`, `:2187-2196`; `NavMenu.razor.cs:137` (issue cites `:1752`, `:2164-2172`) | confirmed |
| 4 | Every depth of a chain is stored with the observed image's current version as child, so "child = current version" covers "any depth" | `ImageScanOrchestrator.cs:570-578`; `docs/areas/base-images.md`, *Storing the chain* | confirmed |
| 5 | Relationships of an earlier current version stay when the observed image moves on | `ImageScanOrchestrator.cs:483-486` deletes only those of the current version; area document, *Storing the chain* | confirmed |
| 6 | `CurrentImageVersionId` is not unique: two observed images can share one current version | `ObservedImageConfiguration.cs:43` (`WithMany`), no unique index | confirmed |
| 7 | Today only discovery disables an observed image | `DockerHubAccountImageDiscoveryService.cs:309`; #282 is open | confirmed |
| 8 | The current container set is `GetLatestContainerSnapshotsAsync` (latest runtime scan per instance, fallback latest snapshot per container) | `ApplicationViewService.cs:1087-1141` | confirmed |
| 9 | For an entry without digest and without source references the page takes the relationships of every source reference of its version, unlike the query's grouping | `ApplicationViewService.cs:904-915` (set is `null` when `SourceReferences` is empty) against `SharedBaseImageQueryService.cs:56` | confirmed |
| 10 | Detail pages read the chain through `LoadBaseImageRelationshipsByChildVersionAsync`, not the shared query; the gauge `dockerupdateguard.base_images.deduplicated` reads `ImageRelationships` in `ApplicationTelemetry.cs:150` | `ApplicationViewService.cs:1885`; `ApplicationTelemetry.cs:150` | confirmed |
| 11 | The issue's data-layer test `SharedBaseImageQueryServiceGetBaseImagesSingleObservedImageIsExcluded` contradicts its own implementation note (query returns consumers "no minimum"; containers are counted in the host) | issue text; fact 8 — the single-image case is tested on the view service instead (AC1) | refuted |
| 12 | `GetObservedImagesByBaseImageAsync` has no production caller (tests only) and no `IsEnabled` filter | grep of `src/`; left out of scope | confirmed |
| 13 | Existing tests that seed a base used by two enabled images keep their results: `ApplicationViewServiceBaseImagesReturnsAggregatedUsageAsync`, `ImageScanOrchestratorScanAsyncBaseImagesOfDockerHubSpellingsShareOneEntryAsync`, both `SharedBaseImageQueryServiceTests` (no minimum in the query) | test sources read | confirmed |
| 14 | Every dashboard metric tile label and page heading is Title Case; summary labels are sentence case | `Dashboard.razor:83`, `:202`; all `Typo.h3` headings | confirmed |

## Acceptance criteria

Definitions are the issue's (*Expected behavior*, normative): consuming observed images, consuming runtime containers,
usage count, shared (usage count at least 2).

- AC1: A base used by exactly one enabled observed image and no current container is not returned by
  `IApplicationViewService.GetBaseImagesAsync`, `HasBaseImagesAsync` returns false when it is the only base, and the
  dashboard's `BaseImageCount` does not count it.
- AC2: A base used by two enabled observed images is returned with `ObservedImageCount` 2, and the query reports the
  current image versions of both images as its consumers.
- AC3: A base used by one enabled observed image whose current version runs in one or two current containers is
  returned, with `ObservedImageCount` 1 and exactly those containers listed.
- AC4: A disabled observed image neither counts towards the usage count nor appears among the parent image
  references, and a container running only its version does not count; a base used by one enabled and one disabled
  image (no container) is not listed.
- AC5: Relationships of a non-current image version of an observed image contribute no parent, no container and no
  usage.
- AC6: `/base-images` shows the heading "Shared Base Images", the description "Shared base images: bases used by two or
  more enabled images or running containers, with the images built on them, their running containers, and
  vulnerability posture." and, without entries, the empty state "No base image is used by two or more enabled images
  or containers yet."; the browser title is "Shared Base Images".
- AC7: The dashboard metric tile reads "Shared Base Images" and links to `/base-images`, the summary card reads
  "Shared base images"; the navigation entry reads "Shared Base Images" and is rendered only when
  `HasBaseImagesAsync` is true.
- AC8: `docs/areas/base-images.md` and `docs/areas/web-ui.md` describe the new behavior; the *Known gaps* entry for
  #286 is removed.

## Tests per criterion

Host tests go to `ApplicationViewServiceTests.cs` (EF Core InMemory, real `SharedBaseImageQueryService`), query
tests to `SharedBaseImageQueryServiceTests.cs` (SQLite). Tests marked *(green today)* guard behavior the fix must keep.

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `ApplicationViewServiceGetBaseImagesSingleObservedImageIsExcludedAsync`, `ApplicationViewServiceHasBaseImagesOnlyUnsharedBaseReturnsFalseAsync`, `ApplicationViewServiceDashboardCountsOnlySharedBaseImagesAsync` (one base of two images, one of a single image: count 1) | `src/Tests/DockerUpdateGuard.Tests/ApplicationViewServiceTests.cs` |
| AC2 | `ApplicationViewServiceGetBaseImagesTwoEnabledImagesAreSharedAsync` (green today); `SharedBaseImageQueryServiceGetBaseImagesTwoEnabledImagesReturnConsumingImageVersionIdsAsync` | `src/Tests/DockerUpdateGuard.Tests/ApplicationViewServiceTests.cs`; `src/Tests/DockerUpdateGuard.Data.Tests/SharedBaseImageQueryServiceTests.cs` |
| AC3 | `ApplicationViewServiceGetBaseImagesOneImageWithContainerIsSharedAsync` (`[DataRow(1)]`, `[DataRow(2)]` containers; green today) | `src/Tests/DockerUpdateGuard.Tests/ApplicationViewServiceTests.cs` |
| AC4 | `SharedBaseImageQueryServiceGetBaseImagesDisabledImageIsNotCountedAsync`; `ApplicationViewServiceGetBaseImagesEnabledAndDisabledImageIsNotSharedAsync`, `ApplicationViewServiceGetBaseImagesDisabledImageIsNotListedAsParentAsync` (two enabled, one disabled: count 2, two parents), `ApplicationViewServiceGetBaseImagesContainerOfDisabledImageIsNotCountedAsync` | `src/Tests/DockerUpdateGuard.Data.Tests/SharedBaseImageQueryServiceTests.cs`; `src/Tests/DockerUpdateGuard.Tests/ApplicationViewServiceTests.cs` |
| AC5 | `ApplicationViewServiceGetBaseImagesIgnoresNonCurrentChildVersionsAsync` (image A moved from A1 to A2, both related to the base, a container on A1, image B on the base: count 2, parents A2 and B, no container), `ApplicationViewServiceGetBaseImagesNonCurrentVersionContainerDoesNotShareAsync` (only A, container on A1: not listed) | `src/Tests/DockerUpdateGuard.Tests/ApplicationViewServiceTests.cs` |
| AC6 | `SharedBaseImagesRendersSharedBaseImagesHeadingAsync`, `SharedBaseImagesWithoutEntriesRendersEmptyStateAsync`; the browser title: manual: the Reviewer reads `<PageTitle>` in step 8 | `src/Tests/DockerUpdateGuard.Tests/SharedBaseImagesRenderTests.cs` |
| AC7 | `DashboardWithSharedBaseImagesRendersSharedBaseImagesLabelsAsync`; `DashboardGetMetricNavigationTargetKnownMetricReturnsExpectedRoute` (row adapted); `NavMenuWithSharedBaseImagesRendersSharedBaseImagesEntryAsync`, `NavMenuWithoutSharedBaseImagesHidesSharedBaseImagesEntryAsync` | `src/Tests/DockerUpdateGuard.Tests/DashboardRenderTests.cs`; `src/Tests/DockerUpdateGuard.Tests/DashboardTests.cs`; `src/Tests/DockerUpdateGuard.Tests/NavMenuTests.cs` |
| AC8 | manual: the Reviewer, step 8, reads both area documents against the code as built | — |

## Approach

1. **Query** (`SharedBaseImageQueryService.GetBaseImagesAsync`): start from `ObservedImages` filtered by `IsEnabled`;
   project the observed image's `CurrentImageVersionId` beside its id; per group fill the new
   `ConsumingImageVersionIds` (distinct). Grouping, `ObservedImageCount` (now enabled images only), the other fields and
   the order stay; no threshold here.
2. **One host helper** (private, `ApplicationViewService`): given the current snapshots, it reads the query and keeps
   each entry whose `ObservedImageCount` plus its containers reaches a private constant of 2; an entry's containers are
   the snapshots whose `ImageVersionId` is in `ConsumingImageVersionIds`, distinct by `CreateContainerKey`. It returns
   each kept entry with its containers (a value tuple or a list of pairs; no nested type, RH2101/RH2106).
3. **Callers:** `GetBaseImagesCoreAsync` loads the snapshots once and calls the helper; `GetDashboardAsync` passes the
   snapshots it already loads and sets `BaseImageCount` to the helper's count; `HasBaseImagesAsync` loads the
   snapshots and returns whether the helper found any entry.
4. **Page data:** the relationship load at `:891-897` is removed. One query loads the `ImageVersions` (with
   `RegistryRepository`) of all kept entries' `BaseImageVersionIds` and `ConsumingImageVersionIds`. Per entry: the
   base versions give the representative and the worst vulnerability summary as today; `ParentImageReferences` are the
   formatted consuming versions (distinct, ordered as today); `RuntimeContainers` are the helper's containers, ordered
   and mapped as today; ordering of entries unchanged.
5. **Wording** (layout as today, only texts change): `SharedBaseImages.razor` `<PageTitle>` and heading "Shared Base
   Images", the description and empty state of AC6, section description "Track shared base images, the images built on
   them, their running containers, and their vulnerability status."; `Dashboard.razor` tile label and its
   `GetMetricNavigationTarget` key "Shared Base Images" (`Dashboard.razor.cs` switch arm), summary label "Shared base
   images"; `NavMenu.razor` entry "Shared Base Images". Title Case follows fact 14; the issue's "Shared base images" is
   kept where its neighbors use sentence case. XML docs of `ISharedBaseImageQueryService.GetBaseImagesAsync`,
   `IApplicationViewService.HasBaseImagesAsync`/`GetBaseImagesAsync`, `SharedBaseImageUsageData.ObservedImageCount`,
   `DashboardViewData.BaseImageCount` and `NavMenu._showBaseImages` say "enabled" / "shared".

## Options considered

1. **Chosen:** consumers from the query, threshold in one host helper — see Approach and decision record 0054.
2. **Threshold in the data query on observed images only** — lost: the Product Manager's definition counts
   containers (one image in one container is shared), and the current-container selection lives in the host.
3. **Keep the relationship load, restricted to consuming child versions** (the issue's note) — lost: it keeps the
   source-reference re-filter that differs from the query's grouping (fact 9), so parents could disagree with the
   count; the query result already names each entry's consumers.
4. **Dashboard and navigation call `GetBaseImagesCoreAsync` and count** — lost: loads vulnerability summaries and
   versions only to count; the lean helper serves all three callers identically.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard.Data | `Queries/SharedBaseImageQueryService.cs` | enabled filter, consuming version ids |
| DockerUpdateGuard.Data | `Queries/SharedBaseImageUsageData.cs` | new property, doc of `ObservedImageCount` |
| DockerUpdateGuard.Data | `Queries/ISharedBaseImageQueryService.cs` | XML doc |
| DockerUpdateGuard | `UI/ApplicationViewService.cs` | helper, three callers, page data |
| DockerUpdateGuard | `UI/IApplicationViewService.cs`, `UI/DashboardViewData.cs` | XML docs |
| DockerUpdateGuard | `Components/Pages/SharedBaseImages.razor`, `Dashboard.razor`, `Dashboard.razor.cs`, `Components/Layout/NavMenu.razor`, `NavMenu.razor.cs` | texts, doc comment |

## Signatures (for the Dev's skeleton)

- `SharedBaseImageUsageData`: `public IReadOnlyList<Guid> ConsumingImageVersionIds { get; set; } = [];` — distinct
  current image version ids of the entry's consuming (enabled) observed images.

No other public member changes signature; the helper and its constant are private (Approach 2).

## Test files

- `src/Tests/DockerUpdateGuard.Data.Tests/SharedBaseImageQueryServiceTests.cs`,
  `src/Tests/DockerUpdateGuard.Tests/ApplicationViewServiceTests.cs`, `DashboardRenderTests.cs`, `DashboardTests.cs`,
  `NavMenuTests.cs` (existing); `src/Tests/DockerUpdateGuard.Tests/SharedBaseImagesRenderTests.cs` (new, beside
  `SharedBaseImagesPersistentStateTests.cs`).

Existing test call sites: `DashboardTests` row `("Base Images", "/base-images")` becomes `("Shared Base Images",
"/base-images")`, and the assertion at `DashboardRenderTests.cs:50` checks "Shared Base Images" — the Tester. No
changed signature; the tests of fact 13 stay unchanged.

Files no unit test can cover: none.

## Areas

- `docs/areas/base-images.md` (1805 words, +60 words): *Shared base images* states the definitions and the threshold;
  *Known gaps* loses the #286 entry; *Related decisions* lists 0054.
- `docs/areas/web-ui.md` (2872 words, +10 words): navigation, dashboard row and `/base-images` row.

## Documentation updates

All by the Dev, once the code is green:

- `docs/areas/base-images.md`, *Shared base images*: replace the first bullet by the issue's definitions (consuming
  enabled observed images by current version at any depth, their current containers, shared at a usage count of at
  least 2; disabled images and non-current versions contribute nothing); the entry's figures are computed from
  consumers only; state that the detail pages' chain and the deduplicated-base gauge
  ([Telemetry](telemetry.md)) still cover every recorded base. Remove the #286 *Known gaps* bullet; add 0054 under
  *Related decisions*.
- `docs/areas/web-ui.md`: navigation list `:24` "Base Images" → "Shared Base Images", `:26` condition "*Shared Base
  Images* only when at least one shared base image exists"; dashboard row `:38` "resolved base images" → "shared base
  images"; `/base-images` row `:47` page name "Shared Base Images" and "Base images used by two or more enabled images
  or running containers, with …".
- No changelog exists; `docs/ARCHITECTURE.md` (`:26` "reuse across observed images") and `README.md` stay true.

## Architecture check

Layering kept: the query stays in `.Data`, the container selection and the threshold in the host's view service. No
entity, migration or configuration change. No guarantee of `.squad/project.md` is touched; the catalog's single write
path is not involved (read-only queries).

## Security considerations

- [ ] No limits: no parsed input.
- [ ] Exceptions reaching a user-visible message: none (read-only queries; failures surface through the existing error
  page).
- The changed query takes no user input, so *Database and migrations* (queries built from user input) is not touched.

## Decision records

- `docs/decisions/0054-a-base-image-is-shared-from-two-users-among-enabled-observed-images-and-their-containers.md`
  (Proposed)

## Out of scope / follow-ups

- Spellings of one Docker Hub base: #275 (merged in #518). User-controlled enabled state: #282 — it must keep the
  `IsEnabled` filter of this query.
- `GetObservedImagesByBaseImageAsync` (fact 12) is unchanged: no production caller.

## Challenge notes

Devil's Advocate, mode plan, issue #286 (`/home/user/DockerUpdateGuard/specs/issue-286/plan.md`)

I checked the plan's claims against the code, and they hold:
- `SharedBaseImageQueryService.cs:66-146` has no `IsEnabled` filter and no minimum count.
- In `ApplicationViewService.cs`, the relationship load at `:891-897` takes every relationship of a grouped base, whatever its child. Parents (`:941-944`) and containers (`:927-933`) are derived from it.
- The dashboard (`:1777`, `:1818`) and `HasBaseImagesAsync` (`:2187-2196`) count the unfiltered query.
- `ImageScanOrchestrator.cs:483-486` deletes only the current version's relationships, and `:570-578` stores every depth with the current version as child. So "child = current version" does cover "any depth".
- `NavMenu` calls `HasBaseImagesAsync` only in `OnInitializedAsync`, so the extra snapshot load happens once per session, as record 0054 states.
- The existing `ApplicationViewServiceBaseImagesReturnsAggregatedUsageAsync` (ApplicationViewServiceTests.cs:500-586) seeds two enabled images with current-version relationships and no stale data, so it stays green.

Option 3 (dropping the source-reference re-filter of fact 9) and fact 11 (the single-image test belongs on the view service, not the query) are sound.

Objections:

1. minor: Wording says "running containers" where the rule also counts stopped containers. The current container set comes from runtime scans that list every container, stopped ones included: `DockerInstanceClient.cs:1114` uses `containers/json?all=1` and `PortainerClient.cs:281` uses `?all=true`. `GetLatestContainerSnapshotsAsync` (`ApplicationViewService.cs:1087-1141`) does not filter on `IsRunning`, and the plan's helper does not either. So a base used by one enabled image with one stopped container is listed as shared, while the AC6 page description ("bases used by two or more enabled images or running containers"), the `web-ui.md` `/base-images` row text in *Documentation updates* and the *Shared base images* definition in `base-images.md` say "running". Why minor: only the wording is off, and the Dev can fix it without changing the plan. Instruction for the Dev and Tester: in the AC6 description, the `web-ui.md` row and the `base-images.md` definition, write "containers" (or "runtime containers") instead of "running containers", matching the issue's empty state "…enabled images or containers yet". In the AC6 render test, assert the description text as changed.
