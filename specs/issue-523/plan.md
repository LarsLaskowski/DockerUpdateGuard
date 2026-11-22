# Plan: Bring the code up to Reihitsu RH2008 and RH2111 (member and type length)

Source: Issue #523
Tier: standard — behavior-neutral, but it rewrites the control flow of 19 production members into new methods and types, beyond `trivial`'s "no change to control flow"; in doubt the higher tier.
Security areas touched: Configured URLs and their user info (`Configuration/DockerUpdateGuardOptionsValidator.cs`); TLS relaxation of Docker instances and Docker Engine access (`Docker/DockerInstanceClient.cs`); Registry and Docker Scout calls (`DockerHub/DockerHubClient.cs`, `Vulnerabilities/DockerScoutVulnerabilityProvider.cs`); Secrets in configuration (the Docker Hub PAT the Docker Scout token step receives); Database and migrations (`src/DockerUpdateGuard.Data/Queries/`); Logging and telemetry of external data (log calls that move into extracted methods); Failure messages and remote text (scan-run ErrorMessage via ScanRunCompletion, the snapshot's UpdateAssessmentMessage in MarkSnapshotFailed, ReferenceUrl sanitization moved to VulnerabilityViewMapper)

## Problem / root cause

RH2008 (member over 60 code lines, tests 120) and RH2111 (type over 1500, tests 3000) report, at `suggestion`, in
every changed file. Findings on `3d84be7` (33; `ImageRegistrationService` no longer reports), under
`src/DockerUpdateGuard/` unless named:

| # | File | Member (line: code lines) |
| - | ---- | ------------------------- |
| P1 | `DockerUpdateGuard.Data/Queries/SharedBaseImageQueryService.cs` | `GetBaseImagesAsync` (64: 84) |
| P2 | `Configuration/DockerUpdateGuardOptionsValidator.cs` | `ValidateScanningOptions` (223: 74) |
| P3 | `Vulnerabilities/DockerScoutVulnerabilityProvider.cs` | `GetVulnerabilitiesAsync` (573: 69) |
| P4 | `Images/DockerHubAccountImageDiscoveryService.cs` | `SynchronizeAccountImagesAsync` (255: 64) |
| P5 | `Images/VulnerabilityEnrichmentService.cs` | `RunRefreshAsync` (518: 124) |
| P6, P7 | `Docker/DockerInstanceClient.cs` | `DiscoverContainersAsync` (1088: 63), `CollectContainerResourceUsageAsync` (1175: 64) |
| P8 | `Images/ResourceStatisticsCollector.cs` | `CollectForInstanceAsync` (119: 68) |
| P9 | `Images/ImageScanOrchestrator.cs` | `ScanAsync` (652: 99) |
| P10, P11 | `Images/RuntimeContainerScanOrchestrator.cs` | `ScanLockedInstanceAsync` (548: 84), `ProcessContainerAsync` (669: 128) |
| P12 | `DockerHub/DockerHubClient.cs` | `GetRepositoriesAsync` (1254: 66) |
| P13 | `Images/ScanCleanupBackgroundService.cs` | `ExecuteCoreAsync` (185: 90) |
| P14 | `Images/UpdateDetectionService.cs` | `Evaluate` (735: 80) |
| P15 | `ServiceCollectionExtensions.cs` | `AddDockerUpdateGuardHost` (43: 100) |
| P16–P19 | `UI/ApplicationViewService.cs` | `GetRuntimeContainersCoreAsync` (772: 91), `GetObservedImagesCoreAsync` (1576: 93), `GetObservedImageDetailAsync` (1882: 79), `GetRuntimeContainerDetailAsync` (1978: 112) |
| P20 | `UI/ApplicationViewService.cs` | type (18: 1557) |
| T1 | `Tests/DockerUpdateGuard.Tests/ApplicationViewServiceTests.cs` | type (19: 3052); 427: 121, 751: 185 |
| T2 | `Tests/DockerUpdateGuard.Tests/RuntimeContainerScanOrchestratorTests.cs` | type (25: 3419); 43: 139, 623: 126, 1228: 124, 1493: 132, 1897: 122 |
| T3 | `Tests/DockerUpdateGuard.Tests/DockerHubAccountImageDiscoveryServiceTests.cs` | 28: 154 |
| T4 | `Tests/DockerUpdateGuard.Tests/ImageScanOrchestratorTests.cs` | 44: 135, 255: 128 |
| T5 | `Tests/DockerUpdateGuard.Data.Tests/MappingTests.cs` | 311: 168 |

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | These 33 are every diagnostic in the solution | `analyzer-check.py` on `899d690`: "unchanged files (not gating): 33", SARIF logs | confirmed |
| 2 | Only code lines count; a lambda counts toward its method; each `partial` part is checked alone; no `reihitsu.json` | Reihitsu `RH2008.md`, `RH2111.md`; repository | confirmed |
| 3 | No test reaches a moved member by reflection | `BindingFlags` uses in tests | confirmed |
| 4 | The `catch` of `ProcessContainerAsync` reads `snapshot`, `previousSnapshot`, `previousFindings`, `parsedReference`, assigned one by one in the `try` | `RuntimeContainerScanOrchestrator.cs:669–826` | confirmed |
| 5 | `ScanAsync` logs `resolvedBaseImageCount` (assigned in its inner `try`) after that `try` | `ImageScanOrchestrator.cs:738,767` | confirmed |
| 6 | The three scan-run completions are identical | `RuntimeContainerScanOrchestrator.cs:635–637`, `VulnerabilityEnrichmentService.cs:638–640`, `ImageScanOrchestrator.cs:750–752` | confirmed |
| 7 | The cleanup writes nothing between its reads, so their order changes no result | `ScanCleanupBackgroundService.cs:196–284` | confirmed |
| 8 | EF Core translates the member-init projection into a named class like the anonymous one | to verify: `SharedBaseImageQueryServiceTests` (SQLite) green after P1, Dev, step 6 | unverified |
| 9 | `docs/UNIT_TESTS.md` allows only `RenderTests` / `PersistentStateTests` splits | lines 66–71, 96–97, 364–365 | confirmed |

## Acceptance criteria

- AC1: The *Analyzer gate* passes with no diagnostic in a changed file and reports "Diagnostics in unchanged files
  (not gating): 0" — no RH2008 or RH2111 finding remains anywhere.
- AC2: Behavior is unchanged: the full *Test* run passes; every test method on `main` exists afterwards with the same
  name, attributes and assertions (moved to another class at most), and only tests the coverage pass adds and the overview sanitization test of T1 are new.
- AC3: No public or protected signature changes; every new type is `internal`.
- AC4: Each split keeps the invariants under *Approach* (order of reads, writes, registrations and log calls;
  exception handlers and the locals they read; log operation names).

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | check: *Analyzer gate* in step 7's stamp | — |
| AC2 | check: *Test* in step 7's stamp, and the Tester's before/after list of `[TestMethod]` names with their attributes per test project, reported identical but for the new T1 test (step 5, again after the coverage pass) | all test files below |
| AC3 | check: Reviewer, step 8, the diff's public and protected declarations | — |
| AC4 | check: Reviewer, step 8, each split against *Approach* | — |

## Approach

Code moves verbatim into new private methods or the types of *Signatures*. Private method names are indicative; the
Dev may split further under the same invariants and reports it. At most seven parameters (S107).

Invariants: database reads, writes, registrations, lock waits/releases and log calls keep their relative order; a
`try`/`catch`/`finally` moves only as a whole, with the lock wait it guards, into one method, and every local its
handler or `finally` reads is declared in that same method and assigned at the same point (P9 and P11 below place the
handlers of facts 4 and 5); a log or result keeps its operation name (`nameof` of the public member); the caller's
`cancellationToken` and a request-timeout token stay where each is used today.

Production (Dev, step 6):

- P1: `QueryBaseImageUsageRows()` projects into `SharedBaseImageUsageRowData`; static `CreateUsageData(group)`.
- P2: two private static halves in today's order (the failure list keeps its order), split before
  `DockerHubAccountDiscoveryIntervalMinutes`.
- P3: the `using` client block with both token attempts becomes `FetchWithTokenRefreshAsync(...)`, called inside the
  existing `try`.
- P4: `SelectLatestRepositories`; `DisableObsoleteObservedImages` returns the count enabled before.
- P5: `ScanBatchesAsync(...)` returns the status (starting at `Succeeded`, the caller's status list); the locked
  stale-finding pass and the retrying final save become methods; `ScanRunCompletion`.
- P6: static `ParseContainers`. P7: `ReadContainerSamplesAsync`, inside the `JsonDocument` scope. P8: static
  `CreateContainerSample`.
- P9: `ScanAsync` keeps the lock and its `finally`; the rest becomes `ScanObservedImageAsync(...)`, holding the inner
  `try` and `resolvedBaseImageCount`, with `RefreshTagMetadataAsync` returning a status, a static `CreateScanRun` and
  `ScanRunCompletion`.
- P10: static `CreateScanRun`; the discovered-container loop and removed-finding deactivation become
  `ProcessDiscoveredContainersAsync(scanContext, containers, statusMessages, cancellationToken)` returning `Partial` or
  `Succeeded`; the `RuntimeContainerScanContext` is built in the `try` after `LoadOwnRepositoryKeysAsync`.
- P11: `ProcessContainerAsync(scanContext, container, cancellationToken)` keeps the parse, the four locals of fact 4,
  digest and validation, both previous-state loads, the snapshot creation and the `catch`; it calls
  `GetOrCreateRuntimeImageVersionAsync`, then `EvaluateContainerSnapshotAsync(scanContext, container, snapshot,
  imageVersion, parsedReference, previousFindings, cancellationToken)` (with `LoadContainerTagDataAsync`); the `catch`
  calls static `MarkSnapshotFailed(snapshot, failureMessage, previousFindings)`. Records 0042–0044 stay true.
- P12: the lambda body becomes `ListRepositoriesAsync`, its `nameof`s unchanged.
- P13: `LoadRemovableScanRunsAsync`, `LoadRemovableTagCandidatesAsync`, one method for both sample queries (fact 7);
  both reads of `RetainScanRunsDays` stay.
- P14: static `OrderTagsByPublication` and `CreateReviewOrUpToDateResult`; no I/O (guarantee).
- P15: `AddOutboundHttpClients(services, applicationOptions)` and `AddApplicationServices(services)`; every
  registration keeps its position (`ARCHITECTURE.md` §3).
- P16–P19: lambda bodies become `*CoreAsync` methods, queries loaders, per-item projections `Map…ListItem`.
- P20: the static helpers move to the two UI types of *Signatures*.

Tests (Tester, step 5, before the Dev, on the unchanged production code, which they must pass): the Tester records the
AC2 name list, restructures and runs *Test*.

- T1: the six `ApplicationViewServiceVulnerabilityOverview…` tests (2557–3067) and `_expectedSortedAdvisoryIds` move
  to `ApplicationViewServiceVulnerabilityOverviewTests` (own `CreateOptionsMonitor()`); it gains
  `ApplicationViewServiceVulnerabilityOverviewDropsUnsafeReferenceUrlAsync` (one finding with `ReferenceUrl =
  "javascript:alert(1)"`, `GetVulnerabilityOverviewAsync` returns `ReferenceUrl` null), which passes today and pins
  the guard call P20 rewrites. The two long tests get private arrange helpers.
- T2: the 19 tests declared in 2168–2306 and 3195–3467 move with the helpers at 3601–4116 to
  `RuntimeContainerScanOrchestratorFailureTests`, which gets its own public `TestContext TestContext { get; set; }`;
  `…ContinuesAfterInstanceScanFailureAsync` (2350) stays. The five long tests get arrange helpers.
- T3–T5: arrange helpers in place.

Early draft PR: no

## Options considered

- Chosen: private methods and collaborator types (RH2111.md's "how to fix").
- `reihitsu.json` with higher limits: the issue asks for a fix; the limits are the template baseline.
- `partial` parts (each checked alone): meets the count, separates nothing; RH2008 still needs the splits.
- 33 suppressions in `src/GlobalSuppressions.cs`: the rule switched off in fact.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard, .Data, Tests | the files of P1–P20, T1–T5 | splits |
| DockerUpdateGuard, .Data | the five types of *Signatures* (`UI/`, `Images/`, `Images/Data/`, `Queries/`) | new |

## Signatures (for the Dev's skeleton)

New internal types. Members moved from `ApplicationViewService` keep today's signatures, `private` becoming
`internal` where the facade calls them:

- `internal static class VulnerabilityViewMapper` — internal: `BuildVulnerabilityAssessmentDetail`, `SortFindingsForDisplay`, `CreateSeveritySummary`, `GetSummaryOrEmpty`, `SanitizeHttpUrl`, `CreateVulnerabilityAssessment`, `SummarizeBaseImageVulnerabilities`, `FormatVulnerabilityAssessmentStatus`; private: `MapVulnerabilityFinding`, `ComputeVulnerabilityScanDelta`, `CreateSeveritySummaryFromFindings`, `CountFixableActiveFindings`, `FormatVulnerabilitySource`.
- `internal static class TagCandidateViewHelper` — internal: `FilterVisibleTagCandidates`, `FilterRuntimeAvailableTagCandidates`, `PopulateResolvedVersionTags`, `ResolveResolvedVersionTag`, `ResolveAvailableUpdateVersionTag`; private: `MaxAvailableTagCandidates`, `CreateTagCandidateKey`, `ResolveComparableVersionTag`, `IsCandidatePublishedOnOrAfterBaseline`, `HasSameOrNewerComparableVersion`.
- `internal static class ScanRunCompletion` — `internal static void Complete(ScanRun scanRun, ScanRunStatus finalStatus, List<string> statusMessages)`.
- `internal sealed class RuntimeContainerScanContext` — `public DockerInstance DockerInstance { get; init; } = null!;`, `public DockerInstanceOptions ConfiguredInstance { get; init; } = null!;`, `public ScanRun ScanRun { get; init; } = null!;`, `public IReadOnlySet<string> OwnRepositoryKeys { get; init; } = null!;`.
- `internal sealed class SharedBaseImageUsageRowData` (`DockerUpdateGuard.Data.Queries`) — `init` properties named and typed as today's anonymous projection: `BaseImageVersionId` (`Guid`), `Registry`, `Repository`, `Tag` (`string`, default `string.Empty`), `Digest`, `SourceReference` (`string?`), `ObservedImageId`, `CurrentImageVersionId` (`Guid`).

Checked: no `Flags`/`Enum` suffix, no nested type, no keyword names.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/ApplicationViewServiceTests.cs`, new `ApplicationViewServiceVulnerabilityOverviewTests.cs`
- `src/Tests/DockerUpdateGuard.Tests/RuntimeContainerScanOrchestratorTests.cs`, new `RuntimeContainerScanOrchestratorFailureTests.cs`
- `src/Tests/DockerUpdateGuard.Tests/DockerHubAccountImageDiscoveryServiceTests.cs`, `ImageScanOrchestratorTests.cs`
- `src/Tests/DockerUpdateGuard.Data.Tests/MappingTests.cs`

Existing test code that calls a changed signature: none (every changed signature is private).

Files no unit test can cover: none (moved lines count as changed for the *Coverage gate*).

## Areas

none (no change in behavior).

## Documentation updates

- `docs/UNIT_TESTS.md` (Tester): the split rule (*Where tests live*, *Naming your tests*, the checklist) allows
  `{TypeUnderTest}{Concern}Tests` for a concern worth separating, `RenderTests` / `PersistentStateTests` as examples.
- `docs/ARCHITECTURE.md`: none (§3, §9 stay true).

## Architecture check

Layering unchanged; guarantees kept: no I/O in update evaluation (P14), fixed failure reasons, one catalog write
path (P11), per-subject guards (P5, P9).

## Security considerations

- [x] No limits.
- [x] Exceptions reaching a user-visible reason: none new, handlers and texts unchanged.

## Decision records

none: routine refactoring under the template's limits.

## Out of scope / follow-ups

RH2009 reports nothing; `DOTNET_RULES_PENDING` has no entry to remove.

## Challenge notes

Devil's Advocate, mode `plan`: challenge of specs/issue-523/plan.md (/home/user/DockerUpdateGuard/specs/issue-523/plan.md) for issue #523

The plan holds up. I checked these claims in the code and they are correct:
- **Fact 7:** the `catch` of `ProcessContainerAsync` reads `snapshot`, `previousSnapshot`, `previousFindings` and `parsedReference` (src/DockerUpdateGuard/Images/RuntimeContainerScanOrchestrator.cs:669–826).
- **Fact 8:** `resolvedBaseImageCount` is assigned inside the inner `try` and logged after it (ImageScanOrchestrator.cs:738, 767).
- **Fact 9:** the three scan-run completions are identical (Runtime…:635–637, VulnerabilityEnrichmentService.cs:638–640, ImageScanOrchestrator.cs:750–752).
- **Fact 10:** the cleanup only reads between `RepairStaleRunningScanRunsAsync` and `RemoveRange` (ScanCleanupBackgroundService.cs:196–284), and `RetainScanRunsDays` is read twice (lines 200 and 292).
- **P20:** none of the moved `ApplicationViewService` helpers is referenced outside that file. The `PopulateResolvedVersionTags` in RuntimeContainerScanOrchestrator.cs:282 is a separate method with a different signature. The helpers that become private in the new types are only called by other moved helpers (lines 104/171, 132/165, 248/163, 260/167, 720/314, 470/485, 597/512+637, 615/514, 628/515, 25/481).
- **P15:** the registration order in ServiceCollectionExtensions.cs:43–158 can be kept with two extracted blocks.
- **T1:** the six `ApplicationViewServiceVulnerabilityOverview…` tests are lines 2557–3067, and `_expectedSortedAdvisoryIds` (line 26) is used only at line 2794.
- **T2:** the helpers at 3601–4116 are used only inside the ranges to be moved. `CreateFailureOrchestrator` and `CreateGatedDockerInstanceClient` are used only at 2587–3030, so they rightly stay.
- **Fact 11 (P1):** the risk is low. Real SQLite tests call `GetBaseImagesAsync` (src/Tests/DockerUpdateGuard.Data.Tests/SharedBaseImageQueryServiceTests.cs:116, 216 and two more tests).
- **Docs:** no area document, decision record or ARCHITECTURE.md passage names a moved helper. ARCHITECTURE.md mentions only `AddDockerUpdateGuardHost` (line 83) and `ExecuteCoreAsync` (lines 359, 364), and both keep their names.

## Objections

1. **minor, Approach invariants (AC4 wording).** The plan says "each `try`/`catch`/`finally` stays in the method that holds it today". Read literally, it conflicts with the plan's own splits:
   - **P5** moves the batch loop's lock `try`/`finally`, the locked stale-finding `try`/`finally` and the final save's retry `try`/`catch` into new methods (VulnerabilityEnrichmentService.cs:570–620 and 642–660).
   - **P9** moves the inner `try`/`catch` into `ScanObservedImageAsync` (ImageScanOrchestrator.cs:699–747).

   Why minor: the Reviewer's AC4 check, step 8, could flag every one of these splits. The intent is clear and the Dev can follow it without changing the plan. **Instruction for the Dev and Reviewer:** read the invariant as "a `try`/`catch`/`finally` moves only as a whole, with the lock wait it guards, into one method. Every local its handler or `finally` reads is declared in that same method and assigned at the same point. The handler of `ProcessContainerAsync` (fact 7) and the inner `try` of `ScanAsync` with `resolvedBaseImageCount` (fact 8) keep the placement P9 and P11 give them."

2. **minor, T2 test count and class shape.** T2 says "the 21 tests … (lines 2168–2349 and 3195–3484)". Those ranges hold 19 tests: 8 at 2168–2306 and 11 at 3195–3467. The test at 2350, `…ContinuesAfterInstanceScanFailureAsync`, is not a failed-evaluation test and lies outside the range. Also, `RunAliasScansAsync` (3835), `RunTwoDerivedScansAsync` (3960) and `RunTwoScansAsync` (3990) are instance methods that use the `TestContext` property (RuntimeContainerScanOrchestratorTests.cs:32).

   Why minor: the Tester's before/after name list for AC2 would catch a lost test, so this needs no plan change. **Instruction for the Tester:** "move the 19 tests declared in lines 2168–2306 and 3195–3467, plus the helpers at 3601–4116, to `RuntimeContainerScanOrchestratorFailureTests`. Give the new class its own public `TestContext TestContext { get; set; }` property. `…ContinuesAfterInstanceScanFailureAsync` (2350) stays in `RuntimeContainerScanOrchestratorTests`."

## Security notes

## Security plan review: issue #523 (mode `plan`)

**Scope:** `/home/user/DockerUpdateGuard/specs/issue-523/plan.md` at `2a10e01`. To check it I read the production code behind P3, P7, P11, P12, P15, P20 and the scan-run completions of P5, P9 and P10.

**Tier: `standard` is correct.** No security control is created, changed or removed.
- **Moved verbatim with their guards:** the Docker Scout token-refresh path (P3), the Docker Hub listing with its `PaginationLinkGuard` (P12), and the Docker Engine sampling loop with its `MaxContainerIdLength` bound and caller token (P7).
- **P3 exceptions:** `FetchWithTokenRefreshAsync` is called inside the existing `try`, so its exceptions still reach the same `catch`.
- **Composition root (P15):** keeps every registration in place. That includes the `RequestDeadlineHandler`/`TransientHttpRetryHandler` order and the named Scout/Portainer clients.
- **`SanitizeHttpUrl` (P20):** this is an output guard (an allow-list of `href` schemes). It moves unchanged to `VulnerabilityViewMapper`, so this is a relocation, not a control change.

### Findings

1. **[NON-BLOCKING] A touched security area is not listed.** The plan touches *Failure messages and remote text* (`.squad/project.md`), but *Security areas touched* does not list it.
   - **Evidence:** P5, P9 and P10 move the write of `scanRun.ErrorMessage` (built from `FailureReason.FromException` messages) into `ScanRunCompletion.Complete`.
   - `/home/user/DockerUpdateGuard/src/DockerUpdateGuard/Images/RuntimeContainerScanOrchestrator.cs:635-637`
   - `/home/user/DockerUpdateGuard/src/DockerUpdateGuard/Images/VulnerabilityEnrichmentService.cs:638-640`
   - `/home/user/DockerUpdateGuard/src/DockerUpdateGuard/Images/ImageScanOrchestrator.cs:750-752`
   - P11 moves `snapshot.UpdateAssessmentMessage = failureMessage` into `MarkSnapshotFailed` (`RuntimeContainerScanOrchestrator.cs:809-813`).
   - P20 moves the handling of the remote `ReferenceUrl` that a page renders as `href`.
   - The plan's own *Security considerations* already rely on this area ("fixed failure reasons").
   - **Smallest fix:** append to the *Security areas touched* line: `; Failure messages and remote text (scan-run ErrorMessage via ScanRunCompletion, the snapshot's UpdateAssessmentMessage in MarkSnapshotFailed, ReferenceUrl sanitization moved to VulnerabilityViewMapper)`.
   - Recommendation: fix now

2. **[NON-BLOCKING] P20 rewrites the overview's call to the URL guard, and no test pins that call.**
   - **Where it is:** `/home/user/DockerUpdateGuard/src/DockerUpdateGuard/UI/ApplicationViewService.cs:1741` is the only check between a remote advisory URL and `<MudLink Href="@context.ReferenceUrl">` in `/home/user/DockerUpdateGuard/src/DockerUpdateGuard/Components/Pages/Vulnerabilities.razor:78`.
   - **What P20 does:** it rewrites that line to `VulnerabilityViewMapper.SanitizeHttpUrl(...)`.
   - **Existing coverage:** the only `javascript:` test, `ApplicationViewServiceSanitizesVulnerabilityReferenceUrlsAsync` (`/home/user/DockerUpdateGuard/src/Tests/DockerUpdateGuard.Tests/ApplicationViewServiceTests.cs:959-1024`), covers the detail path (line 117). None of the six overview tests sets an unsafe URL. A dropped or mis-wired call at 1741 would therefore pass *Test* and the coverage gate (the line is covered by safe URLs).
   - **Smallest fix:** add one test to T1 in the new `ApplicationViewServiceVulnerabilityOverviewTests`, e.g. `ApplicationViewServiceVulnerabilityOverviewDropsUnsafeReferenceUrlAsync`. It seeds one finding with `ReferenceUrl = "javascript:alert(1)"` and asserts that `GetVulnerabilityOverviewAsync` returns `ReferenceUrl` null. AC2's "only tests the coverage pass adds are new" also needs to read "...and the overview sanitization test of T1".
   - Recommendation: fix now

### Checked, no finding
- **P11:** the `catch` keeps `snapshot ??= CreateFallbackSnapshot(...)`, `KeepActiveFindings`, and the four locals of fact 7.
- **P12:** every `nameof` stays `nameof(GetRepositoriesAsync)`, which keeps the log operation names.
- **P2:** the validator's checks on URL user info keep their order. **P1:** the query takes no user input.
- **Secrets:** the PAT is still read only from `hubOptions` and passed through, with no new logging of it.
- **Log calls:** the *Logger arguments* check on log calls that move belongs to the Reviewer's diff review in step 8, since the tier is `standard`.
- **Measurements:** no security property here depends on measured or library behavior, so none was required. Fact 11 (EF projection) is functional and the Dev verifies it.
