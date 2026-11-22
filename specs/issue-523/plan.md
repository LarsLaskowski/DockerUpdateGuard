# Plan: Bring the code up to Reihitsu RH2008 and RH2111 (member and type length)

Source: Issue #523
Tier: standard — a behavior-neutral refactoring, but it rewrites the control flow of 19 production members (scan engine, integration clients, composition root, UI facade) into new methods and types, which is more than `trivial`'s "no change to control flow"; in doubt the higher tier.
Security areas touched: Configured URLs and their user info (`Configuration/DockerUpdateGuardOptionsValidator.cs`); TLS relaxation of Docker instances and Docker Engine access (`Docker/DockerInstanceClient.cs`); Registry and Docker Scout calls (`DockerHub/DockerHubClient.cs`, `Vulnerabilities/DockerScoutVulnerabilityProvider.cs`); Secrets in configuration (the Docker Hub PAT the Docker Scout token step receives); Database and migrations (`src/DockerUpdateGuard.Data/Queries/`); Logging and telemetry of external data (log calls that move into extracted methods)

## Problem / root cause

Reihitsu 1.5.0 added RH2008 (member body over 60 code lines in production, 120 in tests) and RH2111 (type body over
1500 / 3000). The baseline sets both to `suggestion`, so the build stays green, but the *Analyzer gate* reports them
in every changed file. The findings on `3d84be7` (33; the issue's 34 included `ImageRegistrationService`, which no
longer reports):

| # | File | Member (line: code lines) |
| - | ---- | ------------------------- |
| P1 | `DockerUpdateGuard.Data/Queries/SharedBaseImageQueryService.cs` | `GetBaseImagesAsync` (64: 84) |
| P2 | `Configuration/DockerUpdateGuardOptionsValidator.cs` | `ValidateScanningOptions` (223: 74) |
| P3 | `Vulnerabilities/DockerScoutVulnerabilityProvider.cs` | `GetVulnerabilitiesAsync` (573: 69) |
| P4 | `Images/DockerHubAccountImageDiscoveryService.cs` | `SynchronizeAccountImagesAsync` (255: 64) |
| P5 | `Images/VulnerabilityEnrichmentService.cs` | `RunRefreshAsync` (518: 124) |
| P6 | `Docker/DockerInstanceClient.cs` | `DiscoverContainersAsync` (1088: 63) |
| P7 | `Docker/DockerInstanceClient.cs` | `CollectContainerResourceUsageAsync` (1175: 64) |
| P8 | `Images/ResourceStatisticsCollector.cs` | `CollectForInstanceAsync` (119: 68) |
| P9 | `Images/ImageScanOrchestrator.cs` | `ScanAsync` (652: 99) |
| P10 | `Images/RuntimeContainerScanOrchestrator.cs` | `ScanLockedInstanceAsync` (548: 84) |
| P11 | `Images/RuntimeContainerScanOrchestrator.cs` | `ProcessContainerAsync` (669: 128) |
| P12 | `DockerHub/DockerHubClient.cs` | `GetRepositoriesAsync` (1254: 66) |
| P13 | `Images/ScanCleanupBackgroundService.cs` | `ExecuteCoreAsync` (185: 90) |
| P14 | `Images/UpdateDetectionService.cs` | `Evaluate` (735: 80) |
| P15 | `ServiceCollectionExtensions.cs` | `AddDockerUpdateGuardHost` (43: 100) |
| P16–P19 | `UI/ApplicationViewService.cs` | `GetRuntimeContainersCoreAsync` (772: 91), `GetObservedImagesCoreAsync` (1576: 93), `GetObservedImageDetailAsync` (1882: 79), `GetRuntimeContainerDetailAsync` (1978: 112) |
| P20 | `UI/ApplicationViewService.cs` | type, RH2111 (18: 1557) |
| T1 | `Tests/DockerUpdateGuard.Tests/ApplicationViewServiceTests.cs` | type, RH2111 (19: 3052); methods at 427: 121, 751: 185 |
| T2 | `Tests/DockerUpdateGuard.Tests/RuntimeContainerScanOrchestratorTests.cs` | type, RH2111 (25: 3419); methods at 43: 139, 623: 126, 1228: 124, 1493: 132, 1897: 122 |
| T3 | `Tests/DockerUpdateGuard.Tests/DockerHubAccountImageDiscoveryServiceTests.cs` | 28: 154 |
| T4 | `Tests/DockerUpdateGuard.Tests/ImageScanOrchestratorTests.cs` | 44: 135, 255: 128 |
| T5 | `Tests/DockerUpdateGuard.Data.Tests/MappingTests.cs` | 311: 168 |

Production paths are under `src/DockerUpdateGuard/` unless they say otherwise; test paths under `src/`.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | The 33 findings above are every diagnostic in the solution today | `analyzer-check.py` on `899d690`: "Diagnostics in unchanged files (not gating): 33"; the SARIF logs list exactly these RH2008/RH2111 results | confirmed |
| 2 | The issue's `ImageRegistrationService` finding is gone | not in the SARIF logs (the file changed in #528) | refuted |
| 3 | `DOTNET_RULES_PENDING` is empty; RH2008/RH2009/RH2111 are `suggestion` in the baseline | `squad_settings.py:26`; `.editorconfig:238,240,251` | confirmed |
| 4 | Only code lines count (not the signature, the body's braces, blank, comment or `#region` lines); a lambda counts toward its method; each part of a `partial` type is checked alone; test projects get 120/3000 | Reihitsu `documentation/rules/RH2008.md`, `RH2111.md` | confirmed |
| 5 | No `reihitsu.json` overrides the limits | none in the repository | confirmed |
| 6 | No test reaches a member this plan moves by reflection | `BindingFlags` uses: `ServiceCollectionExtensionsTests` (fields of `DockerHubClient`, unchanged) and component statics only | confirmed |
| 7 | The `catch` of `ProcessContainerAsync` reads `snapshot`, `previousSnapshot`, `previousFindings` and `parsedReference`, assigned one by one inside the `try` | `RuntimeContainerScanOrchestrator.cs:669–826` | confirmed |
| 8 | `ScanAsync` logs `resolvedBaseImageCount`, assigned inside its inner `try`, after that `try` | `ImageScanOrchestrator.cs:738,767` | confirmed |
| 9 | The three scan-run completions (`Status`, `CompletedAtUtc`, `ErrorMessage`) are identical | `RuntimeContainerScanOrchestrator.cs:635–637`, `VulnerabilityEnrichmentService.cs:638–640`, `ImageScanOrchestrator.cs:750–752` | confirmed |
| 10 | `ExecuteCoreAsync` of the cleanup writes nothing between its first and last read; reordering its reads changes no result | `ScanCleanupBackgroundService.cs:197–270`: reads only, then `RemoveRange` and one `SaveChangesAsync` | confirmed |
| 11 | EF Core translates a member-init projection into a named class like the anonymous one | to verify: `SharedBaseImageQueryServiceTests` (SQLite) green after P1, Dev, step 6 | unverified |
| 12 | `docs/UNIT_TESTS.md` names only the `RenderTests` / `PersistentStateTests` splits | `docs/UNIT_TESTS.md:66–71,96–97,364–365` | confirmed |
| 13 | The composition root's registration order is documented | `docs/ARCHITECTURE.md:80–100` (§3, "in order") | confirmed |

## Acceptance criteria

- AC1: The *Analyzer gate* passes with no diagnostic in a changed file and reports "Diagnostics in unchanged files
  (not gating): 0" — no RH2008 or RH2111 finding remains anywhere.
- AC2: Behavior is unchanged: the full *Test* run passes; every test method on `main` exists afterwards with the same
  name, attributes and assertions (moved to another class at most), and only tests the coverage pass adds are new.
- AC3: No public or protected signature changes; every new type is `internal`.
- AC4: Each split keeps the invariants under *Approach* (order of reads, writes, registrations and log calls;
  exception handlers and the locals they read; log operation names).

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | check: *Analyzer gate* in step 7's stamp | — |
| AC2 | check: *Test* in step 7's stamp, and the Tester's before/after list of `[TestMethod]` names with their attributes per test project, reported identical (step 5, again after the coverage pass) | all test files below |
| AC3 | check: Reviewer, step 8, the diff's public and protected declarations | — |
| AC4 | check: Reviewer, step 8, each split against *Approach* | — |

## Approach

Pure restructuring: code moves verbatim into new private methods or the new types of *Signatures*; no expression,
condition, message text or call order changes. Names of private methods are indicative; the Dev may split further
where a count is still over the limit, under the same invariants, and reports it. Every new method keeps
`.ConfigureAwait(false)`, regions and XML docs; no method takes more than seven parameters (S107).

Invariants for every split: database reads, writes, registrations, lock waits/releases and log calls keep their
relative order; each `try`/`catch`/`finally` stays in the method that holds it today, and a local its handler reads
stays a local of that method, assigned at the same point; a log or result keeps the operation name it has today
(`nameof` of the public member, never of the new method); the caller's `cancellationToken` and a request-timeout
token stay where each is used today.

Production (Dev, step 6):

- P1: the query becomes `QueryBaseImageUsageRows()` projecting into `SharedBaseImageUsageRowData`; the group
  projection becomes static `CreateUsageData(group)`. Sort and grouping key unchanged.
- P2: two private static halves, called in today's order so the failure list keeps its order: up to the
  reserved-requests check, then from `DockerHubAccountDiscoveryIntervalMinutes` on.
- P3: the `using` client block with both token attempts becomes `FetchWithTokenRefreshAsync(hubOptions,
  imageReference, requestUri, timeout, cancellationToken)`; the guards and the `try`/`catch` stay.
- P4: static `SelectLatestRepositories(...)`; `DisableObsoleteObservedImages(...)` returns the count of images
  enabled before it disables them.
- P5: the batch loop becomes `ScanBatchesAsync(...)` returning the status (it starts at `Succeeded`, as today; the
  status list is the caller's); the locked stale-finding pass and the final save with its retry become methods; the
  completion uses `ScanRunCompletion.Complete`.
- P6: static `ParseContainers(JsonElement)`. P7: the sampling loop becomes `ReadContainerSamplesAsync(httpClient,
  instanceOptions, rootElement, cancellationToken)` returning samples and requested count, inside the `JsonDocument`
  scope.
- P8: static `CreateContainerSample(dockerInstanceId, resourceSample, previousSample)`.
- P9: `ScanAsync` keeps the lock and its `finally`; the rest becomes `ScanObservedImageAsync(...)`, with
  `RefreshTagMetadataAsync(...)` returning a status, a static `CreateScanRun` and `ScanRunCompletion.Complete`;
  `resolvedBaseImageCount` stays a local of the method holding the inner `try` (fact 8).
- P10: static `CreateScanRun`; the loop over discovered containers and the deactivation of removed findings become
  `ProcessDiscoveredContainersAsync(scanContext, containers, statusMessages, cancellationToken)` returning `Partial`
  or `Succeeded`; `ScanRunCompletion.Complete`. The `RuntimeContainerScanContext` is built inside the `try`, after
  `LoadOwnRepositoryKeysAsync`.
- P11: `ProcessContainerAsync(scanContext, container, cancellationToken)` keeps the parse, the four locals of fact
  7, the digest and validation steps, both previous-state loads, the snapshot creation and the `catch`; it calls
  `GetOrCreateRuntimeImageVersionAsync(...)`, then `EvaluateContainerSnapshotAsync(scanContext, container, snapshot,
  imageVersion, parsedReference, previousFindings, cancellationToken)` (tag loading as `LoadContainerTagDataAsync`,
  the evaluated/unevaluated branches, the derived-finding step); the `catch` calls a static
  `MarkSnapshotFailed(snapshot, failureMessage, previousFindings)`. Decision records 0042–0044 stay true.
- P12: the lambda body becomes `ListRepositoriesAsync(accountName, cancellationToken)`; every `nameof` in it stays
  `nameof(GetRepositoriesAsync)`.
- P13: `LoadRemovableScanRunsAsync(dbContext, cleanupStartedAtUtc, cutoff, ct)` (the unreferenced-run query, the
  retained ids, old and excess runs, merged), `LoadRemovableTagCandidatesAsync(dbContext, cutoff, ct)` and one
  static method for the two resource-sample queries; the retained-ids read moves later (fact 10). Both reads of
  `RetainScanRunsDays` stay.
- P14: static `OrderTagsByPublication(...)` and `CreateReviewOrUpToDateResult(...)`; no I/O (guarantee).
- P15: `AddOutboundHttpClients(services, applicationOptions)` and `AddApplicationServices(services)`, called where
  their registrations stand today; every registration keeps its position (§3 stays true).
- P16–P19: lambda bodies become `*CoreAsync` methods like the existing ones; queries become loaders
  (`LoadTagCandidatesBySnapshotAsync`, `LoadDerivedBaseRuntimeAlertsAsync`, `LoadRuntimeDerivedFindingsAsync`);
  per-item projections become `MapRuntimeContainerListItem` / `MapObservedImageListItem`. Awaits inside object
  initializers keep their order.
- P20: the static vulnerability helpers (today lines 104–354 with `FormatVulnerabilityAssessmentStatus` and
  `FormatVulnerabilitySource`) move to `VulnerabilityViewMapper`, the tag-candidate helpers (470–664 with
  `MaxAvailableTagCandidates`) to `TagCandidateViewHelper`; call sites gain the class name.

Tests (Tester, step 5, on the unchanged production code, which they must pass, before the Dev starts): no test is
new and none fails first; the Tester records the AC2 name list, restructures and runs *Test*.

- T1: the six `ApplicationViewServiceVulnerabilityOverview…` tests (lines 2558–3068) and `_expectedSortedAdvisoryIds`
  move to `ApplicationViewServiceVulnerabilityOverviewTests` with a private `CreateOptionsMonitor()` of its own; the
  two long tests get private arrange helpers.
- T2: the 21 tests of what a failed evaluation keeps (lines 2168–2349 and 3195–3484) move to
  `RuntimeContainerScanOrchestratorFailureTests` with the helpers only they use (`CreateTagsWithNewerVersion` through
  `RunTwoScansAsync`, lines 3601–4116); `CreateGatedDockerInstanceClient`, `DiscoverGatedAsync`,
  `ReadInstanceIdAsync`, `CountRuntimeScanRunsAsync` and `CreateFailureOrchestrator` stay. The five long tests get
  arrange helpers.
- T3–T5: arrange helpers in the same class.

Early draft PR: no

## Options considered

- Chosen: split into private methods and collaborator types, as RH2111.md's "how to fix" recommends.
- Raise the limits in a `reihitsu.json`: the issue asks for the findings to be fixed; the limits are the template's
  baseline for every repository.
- `partial` parts for `ApplicationViewService` and the two test classes (each part is checked alone, fact 4): meets
  the count without separating any responsibility, spreads one type over several files, and the RH2008 findings need
  the method splits anyway.
- Member-scoped suppressions in `src/GlobalSuppressions.cs`: 33 exemptions would switch the rule off in fact.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | the 15 production files of P2–P20 | method splits (P20: helpers moved out) |
| DockerUpdateGuard | `UI/VulnerabilityViewMapper.cs`, `UI/TagCandidateViewHelper.cs`, `Images/ScanRunCompletion.cs`, `Images/Data/RuntimeContainerScanContext.cs` | new |
| DockerUpdateGuard.Data | `Queries/SharedBaseImageQueryService.cs`; `Queries/SharedBaseImageUsageRowData.cs` | split; new |
| Tests | the five test files of T1–T5; two new test files | restructured |

## Signatures (for the Dev's skeleton)

New internal types; moved members keep today's signature, only `private` becomes `internal`:

- `internal static class VulnerabilityViewMapper` — `internal static (VulnerabilityAssessmentViewData Assessment, List<VulnerabilityFindingViewData> Findings) BuildVulnerabilityAssessmentDetail(ImageVersion imageVersion, List<VulnerabilityFinding> vulnerabilityFindings)`, `internal static List<VulnerabilityFinding> SortFindingsForDisplay(IEnumerable<VulnerabilityFinding> findings)`, `internal static VulnerabilitySeveritySummaryViewData CreateSeveritySummary(IEnumerable<KeyValuePair<VulnerabilitySeverity, int>> severityCounts)`, `internal static VulnerabilitySeveritySummaryViewData GetSummaryOrEmpty(Dictionary<Guid, VulnerabilitySeveritySummaryViewData> summaries, Guid key)`, `internal static string? SanitizeHttpUrl(string? url)`, `internal static VulnerabilityAssessmentViewData CreateVulnerabilityAssessment(ImageVersion imageVersion, VulnerabilitySeveritySummaryViewData severitySummary)`, `internal static (int ActiveFindingCount, string? Summary) SummarizeBaseImageVulnerabilities(IReadOnlyList<BaseImageRelationshipData>? baseImageRelationships)`, `internal static string FormatVulnerabilityAssessmentStatus(VulnerabilityAssessmentStatus status)`; private there: `MapVulnerabilityFinding`, `ComputeVulnerabilityScanDelta`, `CreateSeveritySummaryFromFindings`, `CountFixableActiveFindings`, `FormatVulnerabilitySource`.
- `internal static class TagCandidateViewHelper` — `internal static List<TagCandidateViewData> FilterVisibleTagCandidates(IEnumerable<TagCandidateViewData> candidates, int take = MaxAvailableTagCandidates)`, `internal static List<TagCandidateViewData> FilterRuntimeAvailableTagCandidates(IEnumerable<TagCandidateViewData> candidates, string currentTag, string? resolvedVersionTag, DateTimeOffset? currentPublishedAtUtc)`, `internal static void PopulateResolvedVersionTags(IList<TagCandidateViewData> candidates)`, `internal static string? ResolveResolvedVersionTag(string currentTag, string? currentDigest, IEnumerable<TagCandidateViewData> candidates)`, `internal static string? ResolveAvailableUpdateVersionTag(IEnumerable<TagCandidateViewData> candidates, string? preferredVariantFamilySourceTag = null)`; private there: `MaxAvailableTagCandidates`, `CreateTagCandidateKey`, `ResolveComparableVersionTag`, `IsCandidatePublishedOnOrAfterBaseline`, `HasSameOrNewerComparableVersion`.
- `internal static class ScanRunCompletion` — `internal static void Complete(ScanRun scanRun, ScanRunStatus finalStatus, List<string> statusMessages)`.
- `internal sealed class RuntimeContainerScanContext` — `public DockerInstance DockerInstance { get; init; } = null!;`, `public DockerInstanceOptions ConfiguredInstance { get; init; } = null!;`, `public ScanRun ScanRun { get; init; } = null!;`, `public IReadOnlySet<string> OwnRepositoryKeys { get; init; } = null!;`.
- `internal sealed class SharedBaseImageUsageRowData` (namespace `DockerUpdateGuard.Data.Queries`) — `public Guid BaseImageVersionId { get; init; }`, `public string Registry { get; init; } = string.Empty;`, `public string Repository { get; init; } = string.Empty;`, `public string Tag { get; init; } = string.Empty;`, `public string? Digest { get; init; }`, `public string? SourceReference { get; init; }`, `public Guid ObservedImageId { get; init; }`, `public Guid CurrentImageVersionId { get; init; }`.

No name ends in `Flags`/`Enum`, none is nested, none exceeds seven parameters, none is a keyword of another language.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/ApplicationViewServiceTests.cs`, new `ApplicationViewServiceVulnerabilityOverviewTests.cs`
- `src/Tests/DockerUpdateGuard.Tests/RuntimeContainerScanOrchestratorTests.cs`, new `RuntimeContainerScanOrchestratorFailureTests.cs`
- `src/Tests/DockerUpdateGuard.Tests/DockerHubAccountImageDiscoveryServiceTests.cs`, `ImageScanOrchestratorTests.cs`
- `src/Tests/DockerUpdateGuard.Data.Tests/MappingTests.cs`

Existing test code that calls a changed signature: none (every changed signature is private).

Files no unit test can cover: none. Moved lines count as changed for the *Coverage gate*; an uncovered branch among
them gets a test in the Tester's coverage pass.

## Areas

none (no change in behavior).

## Documentation updates

- `docs/UNIT_TESTS.md` (Tester): the split rule (*Where tests live*, *Naming your tests*, the checklist) allows
  `{TypeUnderTest}{Concern}Tests` for a concern worth separating, `RenderTests` / `PersistentStateTests` as examples.
- `docs/ARCHITECTURE.md`: none — §3's order and §9's single facade stay true; the new types are internal helpers.

## Architecture check

Layering unchanged (the Data row type stays in `.Data`, UI helpers in `UI/`). Guarantees: no I/O in update evaluation
(P14), fixed failure reasons (handlers and texts unchanged), catalog write path (P11 still calls
`GetOrCreateImageVersionAsync` once), per-subject guards and lock order (P5, P9 unchanged).

## Security considerations

- [x] No limits.
- [x] Exceptions reaching a user-visible reason: none new; every handler and its `FailureReason` text stays where it is.

## Decision records

none: the limits are the template baseline and the splits routine refactoring; the test split rule lands in
`docs/UNIT_TESTS.md`.

## Out of scope / follow-ups

RH2009 reports nothing. `DOTNET_RULES_PENDING` holds no entry to remove (issue comment of 2026-11-22).