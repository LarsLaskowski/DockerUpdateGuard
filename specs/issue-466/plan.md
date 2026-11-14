# Plan: Vulnerability assessment source comes from the resolved provider

Source: Issue #466
Tier: standard — a behavior change (which source a refresh stores during a configuration reload) that creates, changes or removes no security control.
Security areas touched: External process execution (`Vulnerabilities/TrivyVulnerabilityProvider.cs`), Registry and Docker Scout calls (`Vulnerabilities/DockerScoutVulnerabilityProvider.cs`) — each file gets one constant read-only property; arguments, environment, requests, tokens and parsing stay unchanged.

## Problem / root cause

`VulnerabilityEnrichmentService.RunRefreshAsync` resolves the provider once
(`src/DockerUpdateGuard/Images/VulnerabilityEnrichmentService.cs:515`), but every place that stores a source maps it
again from the live configuration, `MapSource(_optionsMonitor.CurrentValue.Vulnerabilities.Provider)`: findings at
line 260 (`UpsertVulnerabilityFindingsAsync`), the image's assessment on a provider failure at line 343 and on success
at line 364 (`ApplyImageVulnerabilitiesAsync`), and on a save failure at line 448
(`ApplyAndSaveImageVulnerabilitiesAsync`). A reload between line 515 and any of these writes labels results with a
provider that did not produce them. The provider contract (`Vulnerabilities/Interfaces/IVulnerabilityProvider.cs`)
carries no identity, so the service has nothing but the options to map from.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | The source is read from live options at the four places the issue names | `VulnerabilityEnrichmentService.cs:260, 343, 364, 448` | confirmed |
| 2 | The provider is resolved once per refresh and used for every scan of it | `VulnerabilityEnrichmentService.cs:515`, passed at line 555 | confirmed |
| 3 | The resolver picks the provider from the live options at the time of `Resolve()` | `Vulnerabilities/VulnerabilityProviderResolver.cs:62-77` | confirmed |
| 4 | `IVulnerabilityProvider` exposes nothing that identifies the provider | `Vulnerabilities/Interfaces/IVulnerabilityProvider.cs` (one method) | confirmed |
| 5 | Without a reload, today's labels are Trivy → Trivy, Docker Scout → DockerScout, `None` → NotSet; with `Enabled` true and `None` the resolver returns `DefaultVulnerabilityProvider` | `MapSource`, `VulnerabilityEnrichmentService.cs:108-116`; resolver lines 69-76 | confirmed |
| 6 | Related defect: `RefreshAsync` checks `Enabled` (line 648) before `Resolve()` reads the options again; a reload to `Enabled=false` in between resolves the default provider, whose *not configured* result is still labelled with the configured kind (e.g. Trivy). The fix labels it *not set* | `VulnerabilityEnrichmentService.cs:648, 515`; resolver lines 66-69 | confirmed |
| 7 | Production implementations of the interface: Default, Docker Scout, Trivy; one test double: `ConcurrencyTrackingVulnerabilityProvider` | grep `: IVulnerabilityProvider` over `src/` | confirmed |
| 8 | One existing test asserts a stored source, through an NSubstitute provider with no source set; NSubstitute returns `default` (`NotSet`) for an unconfigured enum property, so it fails after the fix unless the substitute reports Trivy | `VulnerabilityEnrichmentServiceTests.cs:493, 561`; no test asserts `VulnerabilityAssessmentSource` (grep) | confirmed |
| 9 | The test options monitor can change its value between resolution and persistence | `src/Tests/DockerUpdateGuard.Tests/Data/TestOptionsMonitor{TOptions}.cs:49` (`Reload`) | confirmed |
| 10 | The issue's expected behavior ("the source is taken once from the provider resolved at the start of the refresh") is not contradicted by an area document or record | `docs/areas/vulnerabilities.md:29-31` (provider chosen anew at the start of every refresh), line 87; no record on the topic in `docs/decisions/` | confirmed |

## Acceptance criteria

- AC1: When the configuration is reloaded (Trivy → Docker Scout) while the provider resolved as Trivy is scanning, a
  successful refresh stores source *Trivy* on the new finding, on an existing finding it updates and in the image
  version's `VulnerabilityAssessmentSource`.
- AC2: Under the same reload, an image version whose provider call returns a non-success result (*not found*) stores
  `VulnerabilityAssessmentSource` *Trivy*.
- AC3: Under the same reload, an image version whose results cannot be saved stores its *failed* assessment with
  `VulnerabilityAssessmentSource` *Trivy*.
- AC4: Each provider reports the source of its results: `TrivyVulnerabilityProvider` → *Trivy*,
  `DockerScoutVulnerabilityProvider` → *DockerScout*, `DefaultVulnerabilityProvider` → *NotSet*.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `VulnerabilityEnrichmentServiceRefreshAsyncConfigurationReloadDuringScanKeepsResolvedSourceOnFindingsAsync` | `src/Tests/DockerUpdateGuard.Tests/VulnerabilityEnrichmentServiceTests.cs` |
| AC2 | `VulnerabilityEnrichmentServiceRefreshAsyncConfigurationReloadDuringScanKeepsResolvedSourceOnUnavailableAssessmentAsync` | `src/Tests/DockerUpdateGuard.Tests/VulnerabilityEnrichmentServiceTests.cs` |
| AC3 | `VulnerabilityEnrichmentServiceRefreshAsyncConfigurationReloadDuringScanKeepsResolvedSourceOnSaveFailureAsync` | `src/Tests/DockerUpdateGuard.Tests/VulnerabilityEnrichmentServiceTests.cs` |
| AC4 | `TrivyVulnerabilityProviderSourceReturnsTrivy`; `DockerScoutVulnerabilityProviderSourceReturnsDockerScout`; `DefaultVulnerabilityProviderSourceReturnsNotSet` | `src/Tests/DockerUpdateGuard.Tests/TrivyVulnerabilityProviderTests.cs`; `src/Tests/DockerUpdateGuard.Tests/DockerScoutVulnerabilityProviderTests.cs`; `src/Tests/DockerUpdateGuard.Tests/DefaultVulnerabilityProviderTests.cs` |

Test design for AC1–AC3: the options start with `Enabled` true and provider Trivy; an NSubstitute provider reports
`Source` *Trivy* and, inside its `GetVulnerabilitiesAsync` callback, calls `Reload` on the test options monitor with
provider Docker Scout before returning its result — so the reload lies after `Resolve()` and before every write. The
service is built around that monitor (the existing `CreateService` helper builds its own monitor; the Tester adds the
monitor as a parameter of a helper overload or builds the service inline). AC1 seeds an active finding that the
provider reports again with a stored source other than Trivy (e.g. *Manual*), so its *Trivy* proves the overwrite.
AC3 uses `SqliteTestDatabase.CreateSaveChangesFailingDbContext` as `…SaveFailureForOneImageContinuesWithRemainingImagesAsync`
does, failing the first save of the image's results. All three fail on today's code (it stores *DockerScout*).

## Approach

1. `IVulnerabilityProvider` gets a read-only `Source` property: the persisted source of the results the provider
   reports. Trivy returns `VulnerabilitySource.Trivy`, Docker Scout `VulnerabilitySource.DockerScout`, the default
   provider `VulnerabilitySource.NotSet` — the values `MapSource` gives today without a reload (fact 5). Each
   implementation puts it in its `IVulnerabilityProvider` region; the interface gets a `Properties` region.
2. `RunRefreshAsync` reads `vulnerabilityProvider.Source` once, right after `Resolve()`, and passes it as a
   `VulnerabilitySource source` parameter to `ApplyAndSaveImageVulnerabilitiesAsync`, from there to
   `ApplyImageVulnerabilitiesAsync` and `UpsertVulnerabilityFindingsAsync` (private methods; the parameter goes before
   the cancellation token, documented with `<param>`). The four writes at lines 260, 343, 364 and 448 use it.
3. `MapSource` loses its last caller and is deleted, so the service reads the options only for `Enabled` and
   `MaxParallelScans`.

No new user-visible output; the stored source values are those of today without a reload.

## Options considered

- **Snapshot the configured kind next to `Resolve()`** (`var providerKind = _optionsMonitor.CurrentValue…Provider`
  at line 515, mapped once): the smallest edit, but the resolver reads `CurrentValue` separately, so a reload between
  the two reads still mislabels, and fact 6 (`Enabled` flipped off → default provider labelled Trivy) stays. Lost: it
  narrows the race without closing it.
- **`Resolve()` returns the provider together with its source** (a new result type): closes the race, but changes the
  resolver's signature and its twenty-odd test substitutes, and keeps the provider-to-source mapping in a second place
  that must follow every new provider. Lost against the provider stating its own source, which cannot disagree with
  the provider that produced the results.
- **Chosen: the provider reports its source** (`IVulnerabilityProvider.Source`).

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Vulnerabilities/Interfaces/IVulnerabilityProvider.cs` | new `Source` property |
| DockerUpdateGuard | `Vulnerabilities/TrivyVulnerabilityProvider.cs`, `DockerScoutVulnerabilityProvider.cs`, `DefaultVulnerabilityProvider.cs` | implement `Source` |
| DockerUpdateGuard | `Images/VulnerabilityEnrichmentService.cs` | source taken once from the resolved provider; `MapSource` deleted |
| DockerUpdateGuard.Tests | `Helper/ConcurrencyTrackingVulnerabilityProvider.cs` | implements `Source` (*Test files*) |

## Signatures (for the Dev's skeleton)

- `IVulnerabilityProvider`: `VulnerabilitySource Source { get; }` (`using DockerUpdateGuard.Data.Entities;`)
- `TrivyVulnerabilityProvider`: `public VulnerabilitySource Source => VulnerabilitySource.Trivy;`
- `DockerScoutVulnerabilityProvider`: `public VulnerabilitySource Source => VulnerabilitySource.DockerScout;`
- `DefaultVulnerabilityProvider`: `public VulnerabilitySource Source => VulnerabilitySource.NotSet;`

Each implementation documents it with `/// <inheritdoc/>`; the skeleton gives the three production getters the
*Skeleton* body (`.squad/stack.md`), so AC4's tests fail in step 5 — the service does not read `Source` until step 6,
so no existing test reaches the throwing getter. `VulnerabilityEnrichmentService.MapSource` (private) is deleted in
step 6.

## Test files

`src/Tests/DockerUpdateGuard.Tests/VulnerabilityEnrichmentServiceTests.cs`,
`src/Tests/DockerUpdateGuard.Tests/TrivyVulnerabilityProviderTests.cs`,
`src/Tests/DockerUpdateGuard.Tests/DockerScoutVulnerabilityProviderTests.cs`,
`src/Tests/DockerUpdateGuard.Tests/DefaultVulnerabilityProviderTests.cs`.

Existing test code affected:

- `src/Tests/DockerUpdateGuard.Tests/Helper/ConcurrencyTrackingVulnerabilityProvider.cs` stops compiling with the new
  interface member: the **Dev** adds `public VulnerabilitySource Source => VulnerabilitySource.Trivy;` in step 4
  (mechanical, *Loop limits*).
- `VulnerabilityEnrichmentServiceTests.VulnerabilityEnrichmentServiceRefreshAsyncReplacesVulnerabilitiesWithoutChangingUpdatesAsync`
  (provider substitute at line 493, assertion at line 561) expects *Trivy* from the configured kind; after the fix it
  gets the substitute's unconfigured `NotSet`. The **Tester** adds `vulnerabilityProvider.Source.Returns(VulnerabilitySource.Trivy)`
  in step 5 and rewords its assert message to "The resolved provider's source must be persisted…"; the assertion stays.
  The **Tester** also makes the `CreateProvider` helper (line 86) report *Trivy*. The other inline substitutes assert
  no source and stay unchanged.

Files no unit test can cover: none.

## Areas

`docs/areas/vulnerabilities.md` — the source of findings and assessments is the provider resolved at the start of the
refresh, also when the configuration is reloaded during it.

## Documentation updates

Dev, against the code as built:

- `docs/areas/vulnerabilities.md` line 29-31 (*Configuration* bullet "The provider is chosen anew…"): add that the
  provider chosen at the start of a refresh is used, and named as the source, for the whole refresh; a reload during it
  takes effect with the next refresh.
- `docs/areas/vulnerabilities.md` line 87 (*Findings*): "The source is the configured provider" → the provider resolved
  for the refresh (*Trivy* or *Docker Scout*).
- Sweep: `docs/ARCHITECTURE.md` (lines 234, 271, 348) names the provider and resolver without their members — no
  change; `docs/UNIT_TESTS.md:212` (substitute example) stays valid — no change; no changelog in this repository; the
  PR template asks for nothing further.

## Architecture check

No guarantee of `.squad/project.md` or `docs/ARCHITECTURE.md` changes: the provider is still resolved per refresh,
database work stays on the refresh flow, the provider contract gains one read-only member.

## Security considerations

- [x] No limits: no input is parsed or bounded differently.
- [x] Exceptions reaching a user-visible message: none new; the save-failure message path is unchanged.

## Decision records

- none: no decision beyond the obvious fix; the alternatives are recorded under *Options considered*.

## Out of scope / follow-ups

- `MaxParallelScans` is still read from the live options at the start of the run (line 544) — read once, so it is
  consistent for the run; no change.
