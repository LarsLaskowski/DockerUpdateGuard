# Plan: Over-long provider values abort the vulnerability refresh and leave remaining images unscanned

Source: Issue #271
Tier: standard — a behavior change of the vulnerability refresh (cut values, per-image failure isolation) that creates, changes or removes no security control.
Security areas touched: Database and migrations (entity and configuration files in `src/DockerUpdateGuard.Data/`; no column, index or migration changes); Failure messages and remote text (provider text is cut with `ResponseBodyExcerpt.CutToLength`, a new scan-run and assessment message with a fixed reason); Logging and telemetry of external data (a new warning with the image reference)

## Problem / root cause

`VulnerabilityEnrichmentService` stores provider values at any length: `UpdateFindingFromAdvisory`
(`src/DockerUpdateGuard/Images/VulnerabilityEnrichmentService.cs:192-205`), the new finding's `AdvisoryId` and
`AffectedPackage` (lines 254-260) and the assessment message (`failureMessage`, lines 310 and 318). The columns are
`varchar(n)` (`VulnerabilityFindingConfiguration.cs:24-60`, `ImageVersionConfiguration.cs:48-49`), so PostgreSQL rejects
a longer value at `SaveChangesAsync` (line 464). That save sits inside the batch loop, whose only handler is the run-level
`catch (Exception exception)` at line 485: the run ends *failed*, every later image version keeps its old assessment, the
stale-findings pass (469-483) is skipped, and the rejected entities stay tracked, so the final save (500) fails and only
the retry over a cleared tracker (503-517) saves the run.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | Column limits as the issue lists them | `VulnerabilityFindingConfiguration.cs:24-60` (100, 300, 200, 100, 100, 50, 50; `Summary`, `ReferenceUrl` text), `ImageVersionConfiguration.cs:48-49` (2000) | confirmed |
| 2 | Values reach the columns uncut; the issue's line numbers in the services match | `VulnerabilityEnrichmentService.cs:192-205, 254-260, 310-318, 397, 464, 485, 503` | confirmed |
| 3 | The providers copy the values "as-is" | `TrivyVulnerabilityProvider.cs:112-125`, `DockerScoutVulnerabilityProvider.cs:166-173` (moved from the issue's lines): control characters are replaced (so not as-is), but the length is not bounded | refuted |
| 4 | Trivy's "exited with code X" message can exceed 2000 | `TrivyVulnerabilityProvider.cs:294` cuts the error line to 500 plus `…`; no provider message path found above 2000 (Scout's rejection message: reference plus an origin cut to 300); the message cut is a backstop | refuted |
| 5 | A save failure aborts the batch and all later batches, skips the stale pass, and the final save then needs the retry | lines 437-485, 498-518 | confirmed |
| 6 | SQLite and InMemory do not enforce `varchar(n)` | SQLite datatype documentation (length arguments are ignored); `SqliteTestDatabase` uses SQLite | confirmed |
| 7 | PostgreSQL counts `varchar(n)` in characters, so a value of at most n UTF-16 units always fits | PostgreSQL documentation, *Character Types*; a .NET string never has fewer UTF-16 units than code points | confirmed |
| 8 | `ResponseBodyExcerpt.CutToLength` already implements the issue's rule: null and fitting values unchanged, else at most the limit ending in `…`, surrogate pair never split | `ResponseBodyExcerpt.cs:116-157`; `ResponseBodyExcerptTests.cs:82-120` | confirmed |
| 9 | A provider exception is raised at `Task.WhenAll` (line 446), outside the per-image work, so `ProviderExceptionMarksRunFailedAsync` keeps its run-level outcome | lines 443-447; test lines 40-104 | confirmed |
| 10 | On a cancelled token, `RefreshAsync` throws `OperationCanceledException` after saving the run, from `RefreshInventoryMetricsAsync` | line 520; `ApplicationTelemetry.cs:139-141` (`LongCountAsync(cancellationToken)`) | confirmed |
| 11 | When the save throws, EF Core keeps every entry in its pending state | to verify: Dev, step 6, by reading EF Core 10 `DbContext.SaveChangesAsync` / `StateManager` (changes accepted only after success); the AC6 test shows it for the injected failure | unverified |
| 12 | A detached added finding is not tracked again by `DetectChanges` through `ImageVersion.VulnerabilityFindings` or `ScanRun.VulnerabilityFindings` | to verify: Dev, step 6; the design removes it from both collections anyway, and AC6's test (a new advisory on the failed image version must leave no finding) fails otherwise | unverified |
| 13 | Event id 2055 is free | ids 2050-2054 and 2060+ in use (`grep EventId`) | confirmed |
| 14 | The query of live image versions has no order | `RefreshAsync` lines 548-551 (no `OrderBy`) | confirmed |

## Acceptance criteria

- AC1: The values stored in `VulnerabilityFinding.AdvisoryId`, `Title`, `AffectedPackage`, `InstalledVersion`,
  `FixedVersion`, `PackageClass` and `PackageType` are at most their column length: a longer reported value is stored as
  `ResponseBodyExcerpt.CutToLength(value, limit)` (length equal to the limit, ending with `…`).
- AC2: The cut keeps `null` and a value of at most the limit unchanged, cuts a longer value to the limit including `…`,
  and never splits a surrogate pair.
- AC3: The cut happens before deduplication and matching: two advisories of one image version whose affected packages
  differ only after the 199th character give one active finding (the higher severity kept), and a second refresh with
  the same report keeps that one finding (same id).
- AC4: A provider failure message longer than 2000 characters is stored as the assessment message cut to 2000 ending
  with `…`, and the scan run's error message carries the same cut text.
- AC5: The eight limits are constants on the entities, used by `VulnerabilityFindingConfiguration`,
  `ImageVersionConfiguration` and the cut; the model's lengths stay unchanged, no migration.
- AC6: When applying or saving one image version's results throws (not a cancellation), the refresh discards that image
  version's pending changes, stores its assessment *Failed* with the message
  `Vulnerability results for '<reference>' could not be saved: <reason>` (`<reference>` from
  `IImageReferenceParser.Format`, `<reason>` from `FailureReason.FromException`, `unexpected error` for the test's
  exception), keeps its findings as they were, adds the message to the run, logs warning 2055 with the exception, and
  continues with the remaining image versions of this and later batches; the run ends *Failed*.
- AC7: After such a failure a full refresh still runs the stale-findings pass, and the failed image version's findings
  stay active.
- AC8: A cancellation of the refresh's token while an image version is applied is not turned into a per-image failure:
  no assessment gets the AC6 message, no later image version is scanned, the run ends *Failed* with
  `Vulnerability refresh failed: cancelled`, and `RefreshAsync` throws `OperationCanceledException`.
- AC9: The existing tests stay green, among them `VulnerabilityEnrichmentServiceRefreshAsyncProviderExceptionMarksRunFailedAsync`
  and `VulnerabilityEnrichmentServiceRefreshAsyncTerminalSaveFailureIsLoggedAndRetriedAsync`.
- AC10: The documentation updates below are in the diff, and the *Known gaps* entry for #271 is gone.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `VulnerabilityEnrichmentServiceRefreshAsyncTruncatesOverlongAdvisoryValuesAsync` | `src/Tests/DockerUpdateGuard.Tests/VulnerabilityEnrichmentServiceTests.cs` |
| AC2 | existing: `ResponseBodyExcerptCutToLengthWhenTextIsNullReturnsNull`, `ResponseBodyExcerptCutToLengthWhenTextFitsReturnsItUnchanged`, `ResponseBodyExcerptCutToLengthWhenTextIsLongerCutsWithMarker`, `ResponseBodyExcerptCutToLengthWhenCutWouldSplitSurrogatePairKeepsPairWhole` | `src/Tests/DockerUpdateGuard.Tests/ResponseBodyExcerptTests.cs` |
| AC3 | `VulnerabilityEnrichmentServiceRefreshAsyncAdvisoriesDifferingOnlyBeyondLimitAreDeduplicatedAsync` | `src/Tests/DockerUpdateGuard.Tests/VulnerabilityEnrichmentServiceTests.cs` |
| AC4 | `VulnerabilityEnrichmentServiceRefreshAsyncTruncatesLongProviderFailureMessageAsync` | `src/Tests/DockerUpdateGuard.Tests/VulnerabilityEnrichmentServiceTests.cs` |
| AC5 | `DockerUpdateGuardDbContextModelVulnerabilityValueColumnsUseEntityMaxLengths` (cannot fail first: deferred to step 6); check: Reviewer — no length literal left for the eight properties, no file under `Migrations/` in the diff | `src/Tests/DockerUpdateGuard.Data.Tests/MappingTests.cs` |
| AC6 | `VulnerabilityEnrichmentServiceRefreshAsyncSaveFailureForOneImageContinuesWithRemainingImagesAsync` | `src/Tests/DockerUpdateGuard.Tests/VulnerabilityEnrichmentServiceTests.cs` |
| AC7 | `VulnerabilityEnrichmentServiceRefreshAsyncSaveFailureForOneImageStillDeactivatesStaleFindingsAsync` | `src/Tests/DockerUpdateGuard.Tests/VulnerabilityEnrichmentServiceTests.cs` |
| AC8 | `VulnerabilityEnrichmentServiceRefreshAsyncCancellationWhileApplyingImageIsNotIsolatedAsync` (passes on today's code: deferred to step 6) | `src/Tests/DockerUpdateGuard.Tests/VulnerabilityEnrichmentServiceTests.cs` |
| AC9 | CI: *Test* in the step-7 stamp | — |
| AC10 | check: Reviewer, against *Documentation updates* | — |

Test design the Tester follows:

- AC1: one image version; one advisory with advisory id 150, title 412, package 250, versions 150 and class and type 80
  characters; each stored value has the limit's length, ends with `…` and starts with the reported value's first
  limit − 1 characters.
- AC3: the two packages share a 199-character prefix, are 250 characters long, severities *low* and *high*.
- AC6: three live image versions, `MaxParallelScans` 2 (two batches), each seeded with an active finding `OLD`; the
  provider reports one other advisory `NEW` per image version. `SqliteTestDatabase.CreateSaveChangesFailingDbContext()`
  with `FailOnSaveAttempt` = the attempt count after the arrange + 2 (the first attempt of the run creates the scan run).
  The image order is not defined (fact 14), so assert without it: exactly one image version is *Failed* with the AC6
  message, keeps `OLD` active with no resolution and has no `NEW`; the other two are *findings detected* with `NEW`
  active and `OLD` resolved; three provider calls; run *Failed* containing the message; one 2055 entry with the
  exception. Read the results through a second context of the same database.
- AC7: two live image versions and one image version that is not live with an active finding; the same failure; the
  latter's finding is resolved by the run, the failed one's stays active.
- AC8: two live image versions, `MaxParallelScans` 1; the provider substitute cancels the token source and answers
  success; one provider call.

## Approach

1. **Constants** (`Constants` region, before `Properties`): on `VulnerabilityFinding` the seven column lengths, on
   `ImageVersion` the assessment-message length (*Signatures*). `VulnerabilityFindingConfiguration` and
   `ImageVersionConfiguration` pass them to `HasMaxLength`; the values are unchanged, so the model and its snapshot are
   too.
2. **Cut advisory values.** A private static `LimitAdvisoryValues(IReadOnlyList<VulnerabilityAdvisoryData>)` returns new
   `VulnerabilityAdvisoryData` objects with every property copied and the seven values put through
   `ResponseBodyExcerpt.CutToLength` with their constant (the provider's objects stay untouched; the two non-nullable
   values keep `string.Empty` as fallback, as the existing callers do). `UpsertVulnerabilityFindingsAsync` calls it
   first and passes its result to `DeduplicateAdvisories`, so keys, matching, new findings and updates all see the cut
   values.
3. **Cut the provider message.** In `ApplyImageVulnerabilitiesAsync` the `failureMessage` is cut to
   `ImageVersion.MaxVulnerabilityAssessmentMessageLength` once and used for the assessment and the returned run message;
   the log call keeps `advisoryResult.Message` uncut, as today.
4. **Per-image isolation.** A new private `ApplyAndSaveImageVulnerabilitiesAsync` (same parameters and result as
   `ApplyImageVulnerabilitiesAsync`) holds the apply and the `SaveChangesAsync` that the loop now does inline, in
   `try` / `catch (Exception exception) when (exception is not OperationCanceledException || cancellationToken.IsCancellationRequested == false)`.
   The loop calls it instead. On an error it:
   - discards every pending entry of `_dbContext.ChangeTracker`: an added `VulnerabilityFinding` is removed from
     `image.VulnerabilityFindings` and `scanRun.VulnerabilityFindings` and detached, any other added entry detached; a
     modified or deleted entry gets `CurrentValues.SetValues(OriginalValues)` and state `Unchanged`. The pending entries
     are exactly this image version's: each earlier one was saved or discarded before, the scan run is saved before the
     loop and changed only after it. No `ChangeTracker.Clear()`;
   - builds the AC6 message, cut to the assessment-message limit, and sets the image version's status *Failed*,
     source, message, check time and scan run as the provider-failure path does;
   - logs `VulnerabilityRefreshImageSaveFailed(exception, formattedReference)`;
   - saves again, outside the handler: a second failure reaches the run-level `catch`, as any unexpected error today;
   - returns `(ScanRunStatus.Failed, message)`.

   Nothing else in `RunRefreshAsync` changes: the stale pass and the final save run after the loop as before, and
   `processedImageVersionIds` already holds every image version.
5. **Log** 2055, `LogLevel.Warning`, "Vulnerability refresh could not save the results for {ImageReference}", the exception as
   first parameter, as `RuntimeContainerProcessingFailed` (2098).

## Options considered

- One place for the cut, the enrichment service, with the existing `CutToLength`; lost: a cut in each provider (the
  issue's alternative: duplicated, misses the assessment message and future providers), a new `VulnerabilityValueLimiter`
  (the issue's example: repeats `CutToLength` and its tests), a cut without `…` for identifiers and versions (a literal
  reading of the issue's "free-text" sentence: a cut id or version would look real), widening the columns by migration,
  dropping the advisory, and leaving the message uncut as 0009 option 13 prefers ([0027](../../docs/decisions/0027-over-long-vulnerability-values-are-cut-to-their-column-length-where-the-refresh-stores-them.md)).
- Discard all pending entries; lost: discarding only the image version's own entries (the issue's wording: the same
  set today, more code), `ChangeTracker.Clear()` (ruled out by the issue), a context or transaction per image version,
  and a length check before the save ([0028](../../docs/decisions/0028-a-failed-save-of-one-image-version-does-not-stop-the-vulnerability-refresh.md)).

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard.Data | `Entities/VulnerabilityFinding.cs`, `Entities/ImageVersion.cs` | length constants |
| DockerUpdateGuard.Data | `Configurations/VulnerabilityFindingConfiguration.cs`, `Configurations/ImageVersionConfiguration.cs` | `HasMaxLength` with the constants |
| DockerUpdateGuard | `Images/VulnerabilityEnrichmentService.cs` | cut, per-image isolation |
| DockerUpdateGuard | `Images/ImageHostLoggingExtensions.cs` | log 2055 |

## Signatures (for the Dev's skeleton)

On `VulnerabilityFinding`:

- `public const int MaxAdvisoryIdLength = 100;`
- `public const int MaxTitleLength = 300;`
- `public const int MaxAffectedPackageLength = 200;`
- `public const int MaxInstalledVersionLength = 100;`
- `public const int MaxFixedVersionLength = 100;`
- `public const int MaxPackageClassLength = 50;`
- `public const int MaxPackageTypeLength = 50;`

On `ImageVersion`: `public const int MaxVulnerabilityAssessmentMessageLength = 2000;`

On `ImageHostLoggingExtensions`: `public static partial void VulnerabilityRefreshImageSaveFailed(this ILogger logger, Exception exception, string imageReference);`

All are complete declarations, no stub bodies. The configurations are rewritten in step 6.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/VulnerabilityEnrichmentServiceTests.cs` (new tests)
- `src/Tests/DockerUpdateGuard.Data.Tests/MappingTests.cs` (new test)

Existing test code that calls a changed signature: none (no signature changes).

Files no unit test can cover: none.

## Areas

- Vulnerabilities: the cut values, per-image failure isolation, the new message.
- Scan engine: the per-subject failure messages.
- Integration failures: the remote text fields of the providers are now cut where stored.

## Documentation updates

All by the Dev, against the code as built:

- `docs/areas/vulnerabilities.md`:
  - *What a refresh covers*, last bullet: a failure of one image version does not stop the others (link *Limits and
    errors*).
  - *Assessment per image version*: the *Failed* row adds "or its results could not be saved" with the AC6 message; the run
    rule adds that case to "ends *failed*".
  - *Findings*, first bullet: identity and deduplication use the cut values.
  - *Limits and errors*: the stored-lengths bullet says longer values (the seven and the assessment message) are cut
    with `…` inside the limit, a surrogate pair kept whole (link [Integration failures](../../docs/areas/integration-failures.md#excerpts-of-remote-text));
    a new bullet for per-image isolation (discard, *Failed* with the message, findings untouched, refresh continues, run
    *failed*, stale pass runs and keeps them, a second save failure ends the refresh as an unexpected error,
    cancellation not converted, warning with the image reference).
  - *Known gaps*: the #271 entry removed. *Related decisions*: 0027 and 0028.
- `docs/areas/scan-engine.md`, *Limits and errors*, the bullet listing run messages (`Container '<name>' could not be
  processed: <reason>`): add the vulnerability refresh's per-image message.
- `docs/areas/integration-failures.md`, *Remote text fields*: next to "the names that Docker instances cuts to their
  stored length", name the vulnerability values the refresh cuts where it stores them (link Vulnerabilities).
- `docs/ARCHITECTURE.md`, `README.md`: none (no guarantee, flow or configuration changes; sweep found no enumeration
  of these limits there). No changelog exists.

## Architecture check

Layering holds: the constants live in `.Data` (its entities), the host reads them. Fixed failure reasons (0009) hold:
the new message carries `FailureReason.FromException`, never exception text. One active finding per key holds: keys are
built from the cut values. The catalog write path and the scan engine's single-instance assumption are untouched.
Integration clients are unchanged.

## Security considerations

- [x] Lengths are counted in UTF-16 units of the decoded string, after the providers' control-character replacement;
  PostgreSQL counts characters, so the bound holds (fact 7).
- [x] The per-image exception reaches the assessment and the run only as `FailureReason.FromException` in the AC6
  message; the exception goes to the log only, as for 2051 and 2053. No provider value enters a log template.

## Decision records

- `docs/decisions/0027-over-long-vulnerability-values-are-cut-to-their-column-length-where-the-refresh-stores-them.md` (Proposed; amends 0009)
- `docs/decisions/0028-a-failed-save-of-one-image-version-does-not-stop-the-vulnerability-refresh.md` (Proposed)

## Out of scope / follow-ups

- The batch-abort pattern of the scan orchestrators: #19.
- Test database fidelity for `varchar(n)`: #28.
- Deviations from the issue: no new helper (AC2 rests on the existing `CutToLength` tests; "shorter value" is the
  same branch as "exactly the limit"), the `…` on every cut value, and no `dotnet ef migrations add` run (the tool is not in
  the manifest; AC5's check replaces it).
