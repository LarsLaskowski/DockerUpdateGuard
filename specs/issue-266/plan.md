# Plan: Docker instance with surrounding whitespace in Name is never scanned or sampled

Source: Issue #266
Tier: standard — a behavior change (which configured instance a scan, a collection and a container action finds) that creates, changes or removes no security control.
Security areas touched: Container actions (`Portainer/ContainerActionService.cs` finds the configuration of an instance with surrounding whitespace in its name for the first time, so its actions become available; the code of the service and its checks stay unchanged); TLS relaxation of Docker instances (`Docker/DockerInstanceClient.cs` keys its once-per-instance `SkipCertificateValidation` warning by the now trimmed name; no code change there); Insecure Portainer HTTP (`Portainer/PortainerClient.cs` keys its once-per-instance `AllowInsecureHttp` warning by the now trimmed name; no code change there)

## Problem / root cause

Synchronization stores an instance under its trimmed name (`src/DockerUpdateGuard/Images/InstanceDiscoveryService.cs:92`,
`:101`, obsolete-instance set `:171`), but three consumers look the configured instance up by the untrimmed configured
name:

- `src/DockerUpdateGuard/Images/RuntimeContainerScanOrchestrator.cs:1475` builds `optionsByName` from `entity.Name`;
  `:1493` misses, counts the instance as skipped and logs `RuntimeContainerScanSkippedConfigurationMissing`.
- `src/DockerUpdateGuard/Images/ResourceStatisticsCollector.cs:212` the same dictionary; `:219` skips silently.
- Related defect found on the way: `src/DockerUpdateGuard/Portainer/ContainerActionService.cs:156`
  (`FindInstanceOptions`) compares `entity.Name` untrimmed; `:521-525` then shows no actions, and `:344-346` rejects an
  action with `Docker instance '<name>' is disabled`.

Every Engine and Portainer call receives the untrimmed `DockerInstanceOptions.Name`
(`Docker/DockerInstanceClient.cs:205` connection-pool key, `:242-244` the warning, `:260`, `:1096` messages).

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | Synchronization trims the name when matching, creating and computing obsolete instances | `InstanceDiscoveryService.cs:92`, `:101`, `:171` (issue: `:91`, `:100`, `:169`; lines shifted) | confirmed |
| 2 | The validator compares trimmed names for duplicates and treats a whitespace-only name as missing | `DockerUpdateGuardOptionsValidator.cs:461-470` (issue: `:283`; lines shifted) | confirmed |
| 3 | Scan and collection key their lookup by the untrimmed name | `RuntimeContainerScanOrchestrator.cs:1475`, `:1493-1500`; `ResourceStatisticsCollector.cs:212`, `:219-223` (issue: `:1179`, `:226`; lines shifted) | confirmed |
| 4 | `DockerInstanceClient` uses the untrimmed name for the pool key and the warning | `DockerInstanceClient.cs:205`, `:242-244` (issue: `:251`, `:289`; lines shifted) | confirmed |
| 5 | Container actions also look the configuration up untrimmed | `ContainerActionService.cs:156`, used at `:469`, `:521`; not in the issue | confirmed |
| 6 | "There are no tests for `ResourceStatisticsCollector` yet" | `src/Tests/DockerUpdateGuard.Tests/ResourceStatisticsCollectorTests.cs` exists with five tests | refuted |
| 7 | The area document's entry for this issue is *Open questions* item 1 | `docs/areas/docker-instances.md:221-226`: it is the only entry of *Known gaps* | refuted |
| 8 | The area document already states identity by trimmed name, case-insensitive | `docs/areas/docker-instances.md:23` | confirmed |
| 9 | All consumers read `DockerUpdateGuardOptions` through the options pipeline; the only other binding (`applicationOptions`, `ServiceCollectionExtensions.cs:49-51`) reads no instance setting | grep of `DockerInstances` in `src/DockerUpdateGuard`: only the three consumers, `InstanceDiscoveryService` and the validator | confirmed |
| 10 | `OptionsFactory<T>.Create` runs every `IConfigureOptions`, then every `IPostConfigureOptions`, then every `IValidateOptions`, and `IOptionsMonitor` rebuilds through it on each reload of a bound section | Microsoft.Extensions.Options `OptionsFactory<TOptions>.Create` documentation; held by the tests of AC4 and AC5 | confirmed |
| 11 | No production code constructs `DockerInstanceOptions` outside binding | grep `new DockerInstanceOptions` in `src/DockerUpdateGuard`: no hit | confirmed |
| 12 | Tests hand consumers raw options through `TestOptionsMonitor<T>` (`Data/TestOptionsMonitor{TOptions}.cs`), which runs no post-configure step | `ResourceStatisticsCollectorTests.cs:61`, `RuntimeContainerScanOrchestratorTests.cs:63` | confirmed |

## Acceptance criteria

- AC1: With one enabled instance configured as `Name` = `" Production "`, `RuntimeContainerScanOrchestrator.ScanAllAsync`
  records one runtime container scan run for the stored instance `Production` and does not log
  `RuntimeContainerScanSkippedConfigurationMissing`.
- AC2: With the same configuration, `ResourceStatisticsCollector.CollectAsync` records resource samples for `Production`.
- AC3: With the same configuration and Portainer enabled for it, `ContainerActionService.GetAvailabilityAsync` for a
  container of the latest scan of `Production` shows its actions.
- AC4: The `DockerUpdateGuardOptions` resolved from `AddDockerUpdateGuardHost` carry the `Name` of every Docker instance
  trimmed of leading and trailing whitespace, inner spaces and case kept — on first resolution and after a reload
  of the configuration.
- AC5: Validation is unchanged in effect: a whitespace-only `Name` still fails startup with
  `'Name' must be configured for every entry of 'DockerUpdateGuard:DockerInstances'`, and `"Production"` beside
  `"Production "` fails with `'DockerUpdateGuard:DockerInstances' contains duplicate instance name 'Production'`.
- AC6: Instances whose names have no surrounding whitespace behave as before.
- AC7: `docs/areas/docker-instances.md` states the trimming for every consumer, and its *Known gaps* entry for #266 is removed.

## Tests per criterion

Tests for AC1–AC3 build `IOptionsMonitor<DockerUpdateGuardOptions>` through the options pipeline with
`DockerUpdateGuardOptionsPostConfigure` registered (for example `new ServiceCollection().AddOptions<DockerUpdateGuardOptions>().Configure(…)`
plus the post-configure as `IPostConfigureOptions<DockerUpdateGuardOptions>`), never with `TestOptionsMonitor<T>`
fed a pre-trimmed name: the stored instance comes from the real synchronization of `" Production "`.

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `RuntimeContainerScanOrchestratorScanAllAsyncWithUntrimmedConfiguredNameScansInstanceAsync` | `src/Tests/DockerUpdateGuard.Tests/RuntimeContainerScanOrchestratorTests.cs` |
| AC2 | `ResourceStatisticsCollectorCollectAsyncWithUntrimmedConfiguredNameRecordsSamplesAsync` | `src/Tests/DockerUpdateGuard.Tests/ResourceStatisticsCollectorTests.cs` |
| AC3 | `ContainerActionServiceGetAvailabilityAsyncWithUntrimmedConfiguredNameShowsActionsAsync` | `src/Tests/DockerUpdateGuard.Tests/ContainerActionServiceTests.cs` |
| AC4 | `DockerUpdateGuardOptionsPostConfigurePostConfigureTrimsEveryInstanceName` (two instances, `" Production "` and `"\tStaging Host "` → `Production`, `Staging Host`); `DockerUpdateGuardOptionsPostConfigurePostConfigureKeepsEmptyName`; `ServiceCollectionExtensionsAddDockerUpdateGuardHostTrimsDockerInstanceNames`; `ServiceCollectionExtensionsAddDockerUpdateGuardHostTrimsDockerInstanceNamesAfterReload` (set the key to `" Staging "`, `Reload()` the configuration root, `CurrentValue` names `Staging`) | `src/Tests/DockerUpdateGuard.Tests/DockerUpdateGuardOptionsPostConfigureTests.cs`; `src/Tests/DockerUpdateGuard.Tests/ServiceCollectionExtensionsTests.cs` |
| AC5 | `ServiceCollectionExtensionsAddDockerUpdateGuardHostRejectsWhitespaceOnlyInstanceName`; `ServiceCollectionExtensionsAddDockerUpdateGuardHostRejectsInstanceNamesDifferingOnlyInWhitespace` (both: `CurrentValue` throws `OptionsValidationException` whose failures contain the exact text) | `src/Tests/DockerUpdateGuard.Tests/ServiceCollectionExtensionsTests.cs` |
| AC6 | `CI:` the existing suite (*Test* in step 7 and CI) stays green unchanged | — |
| AC7 | `manual:` the Reviewer reads `docs/areas/docker-instances.md` against the diff in step 8 | — |

## Approach

1. New `Configuration/DockerUpdateGuardOptionsPostConfigure.cs`: implements `IPostConfigureOptions<DockerUpdateGuardOptions>`
   (region `IPostConfigureOptions`, as the validator's `IValidateOptions`). `PostConfigure` guards `options` with
   `ArgumentNullException.ThrowIfNull`, ignores `name`, and for every entry of `options.DockerInstances` whose `Name` is
   not null or empty sets `Name = Name.Trim()`. A whitespace-only name becomes empty and the validator reports it as
   missing, as today. No other key is touched.
2. `ServiceCollectionExtensions.AddDockerUpdateGuardHost` (`:53`): register it beside the validator with
   `services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<DockerUpdateGuardOptions>, DockerUpdateGuardOptionsPostConfigure>());`.
3. No consumer changes: the two dictionaries, `FindInstanceOptions`, `DockerInstanceClient`, `PortainerClient` and the
   synchronization stay as they are (its `Trim()` calls become no-ops on bound options and stay).

No new user-visible output; existing messages now show the trimmed name.

## Options considered

1. Post-configure step (chosen) — one place, before validation and on every reload; every consumer, message and log
   entry sees one spelling.
2. Trim at each lookup (`entity => entity.Name.Trim()` in both dictionaries, `FindInstanceOptions`) — the issue's
   alternative; lost because clients, messages and logs keep the untrimmed spelling and each new lookup must repeat it.
3. Both — lost: four places for one rule; the remaining benefit (hand-built options in tests) is no production path.

Recorded in `docs/decisions/0049-docker-instance-names-are-trimmed-once-where-the-options-are-built.md`.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Configuration/DockerUpdateGuardOptionsPostConfigure.cs` | new |
| DockerUpdateGuard | `ServiceCollectionExtensions.cs` | one registration |

## Signatures (for the Dev's skeleton)

- `public sealed class DockerUpdateGuardOptionsPostConfigure : IPostConfigureOptions<DockerUpdateGuardOptions>` in namespace `DockerUpdateGuard.Configuration`
- `public void PostConfigure(string? name, DockerUpdateGuardOptions options)`

The skeleton also adds the registration of step 2. No existing file is rewritten beyond that line.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/DockerUpdateGuardOptionsPostConfigureTests.cs` (new)
- `src/Tests/DockerUpdateGuard.Tests/ServiceCollectionExtensionsTests.cs`
- `src/Tests/DockerUpdateGuard.Tests/RuntimeContainerScanOrchestratorTests.cs`
- `src/Tests/DockerUpdateGuard.Tests/ResourceStatisticsCollectorTests.cs`
- `src/Tests/DockerUpdateGuard.Tests/ContainerActionServiceTests.cs`

Existing test code that calls a changed signature: none (no signature changes). Existing tests keep their inputs:
none of them configures a name with surrounding whitespace through the options pipeline; the validator's own test
with `Name = "   "` (`DockerUpdateGuardOptionsValidatorTests.cs:707`) calls the validator directly and is unaffected.

Files no unit test can cover: none.

## Areas

- `docs/areas/docker-instances.md` — *Configured instances*, *Known gaps*.
- `docs/areas/hosting.md` — *Validation*.

## Documentation updates

All by the Dev, once the code is green:

- `docs/areas/docker-instances.md`, *Configured instances* (`:23`): the configured `Name` is trimmed when the
  configuration is read, on startup and on every reload, before validation; the synchronization, runtime scans,
  resource collection, container actions, the Engine and Portainer clients, their messages and log entries all use
  the trimmed name. Link record 0049 under *Related decisions*.
- `docs/areas/docker-instances.md`, *Known gaps* (`:221-226`): remove the #266 entry and, being the only one, the heading.
- `docs/areas/hosting.md`, *Validation*: one sentence that Docker instance names are trimmed before validation, so
  messages name the trimmed name (link *Docker instances*).
- `docs/ARCHITECTURE.md` §3 step 1 (`:86`): "Binds, normalizes (trims Docker instance names) and validates
  `DockerUpdateGuardOptions`"; §4 (`:147`): a bullet that one post-configure step trims each Docker instance name
  before validation and on reload, linking record 0049.
- No README, configuration, migration or changelog impact (the repository keeps no changelog; the PR template's
  checklist asks only for the area documents and `ARCHITECTURE.md` above).

## Architecture check

The composition root gains one options registration (§3, §4 updated above). The guarantees of `.squad/project.md` are
not weakened: synchronization semantics (removal of an instance no longer configured) are unchanged, since stored
names were already trimmed; the container-action checks (disabled instance, disabled Portainer, not current) are
unchanged and now apply to the instance they were meant for.

## Security considerations

- [x] No limits.
- [x] Exceptions reaching a user: none new; validation messages are the existing ones with the trimmed name.

## Decision records

- `docs/decisions/0049-docker-instance-names-are-trimmed-once-where-the-options-are-built.md` (Proposed)

## Out of scope / follow-ups

- #4: the same untrimmed-search pattern in `ImageRegistrationService` — its own issue.

## Security notes

Security plan review: issue 266 (mode `plan`, round 1)

I read `/home/user/DockerUpdateGuard/specs/issue-266/plan.md` and `/home/user/DockerUpdateGuard/docs/decisions/0049-docker-instance-names-are-trimmed-once-where-the-options-are-built.md`, and checked them against the code on the branch (diff vs origin/main: plan, log, record 0049 and the decisions index only).

**Tier:** `standard` is correct. The change adds input normalization of operator-owned configuration in one post-configure step. It creates, changes and removes no security control:
- **Container-action checks.** `CheckTargetAsync` (`Portainer/ContainerActionService.cs:341-359`) still rejects a disabled instance, a disabled Portainer and a stale snapshot. The availability gate (`:521-525`) is also unchanged. The code of those checks stays the same; they now read the configuration entry they were meant to read.
- **No attacker input.** Today an instance whose configured name has surrounding whitespace fails closed (no actions). After the change it gets the actions its own `Enabled`/`Portainer.Enabled` grant. The name comes only from the operator's configuration.
- **No wrong-instance matches.** The validator already drops names that are empty after trimming and rejects names that are equal after trimming, ignoring case (`Configuration/DockerUpdateGuardOptionsValidator.cs:461-470`). So the trimmed lookup in `FindInstanceOptions` (`:156`) and the two `ToDictionary` calls can neither match a second instance nor throw on a duplicate key.
- **Legacy names fail closed.** A stored instance that still carries an untrimmed name finds no options after the change and stays without actions.

**TLS relaxation:** not affected beyond the warning label.
- `BuildHttpClientCacheKey` (`Docker/DockerInstanceClient.cs:202-211`) keys the pooled client on BaseUrl, engine URI, UseTls, SkipCertificateValidation and CertificatePath as well as the name. Trimming the name cannot make a relaxed-TLS client be reused for a verifying configuration.
- The once-per-instance warning (`:240-244`) only changes which spelling it is keyed and logged under. Two names that differ only in whitespace cannot both exist.

**Coverage of every consumer:** confirmed (plan claim 9).
- `IOptions<DockerUpdateGuardOptions>` (Dashboard, MyImages, NavMenu, ApplicationInitializationExtensions) and `IOptionsMonitor` both resolve through `OptionsFactory`, so they all get the post-configure.
- The early eager `Bind` into `applicationOptions` (`ServiceCollectionExtensions.cs:49-100`) reads only Database, DockerHub and ReleaseMetadata, never `DockerInstances`.

**Measurement:** I ran none, and none is required. No security property in this plan rests on the library's ordering (plan claim 10: PostConfigure before Validate, rebuilt on reload).
- The duplicate and empty-name checks trim on their own, so they hold whichever runs first.
- The container-action checks fail closed if the post-configure did not run.
- The ordering only matters for the functional criteria, and the plan's AC4/AC5 tests through `AddDockerUpdateGuardHost` (including the `Reload()` case) pin it.

## Findings

1. **[NON-BLOCKING] Unlisted security area: Insecure Portainer HTTP.**
   - *Security areas touched* lists "TLS relaxation of Docker instances" because `DockerInstanceClient` keys its once-per-instance `SkipCertificateValidation` warning by the now trimmed name.
   - `Portainer/PortainerClient.cs:672-674` does the same for the `AllowInsecureHttp` warning: `_insecureHttpWarningInstances.TryAdd(instanceOptions.Name, 0)` and `_logger.PortainerInsecureHttpInUse(instanceOptions.Name)`. That is the *Insecure Portainer HTTP* area of `.squad/project.md` (`:29-30`), and it is not listed.
   - The effect is the same harmless one (key and logged name become the trimmed spelling, no code change), so the tier does not change.
   - Smallest fix, which I recommend making now: append this to the *Security areas touched* line in `plan.md`, verbatim:
     `; Insecure Portainer HTTP (`Portainer/PortainerClient.cs` keys its once-per-instance `AllowInsecureHttp` warning by the now trimmed name; no code change there)`

No blocking findings. Log calls that carry the instance name now get the trimmed spelling of an operator-configured value, not external data, so the logging area is not affected.
