# Plan: Remove unused raw Evidence from derived runtime detection

Source: Issue #338
Tier: trivial — the change deletes a member no code reads and the parameter that fills it, so behavior and control flow stay the same; at about 37 changed lines it exceeds the `patch` budget of 30.
Security areas touched: none

## Problem / root cause

`DerivedBaseRuntimeDetector` copies the raw input it matched into `RuntimeCandidate.Evidence`: the whole environment
entry (`src/DockerUpdateGuard/Images/DerivedBaseRuntimeDetector.cs:39`, `:50`, `:61`) or the whole history line
`$"{CreatedBy} {Comment}"` (`:81`, passed at `:91`, `:102`, `:115`, `:126`; stored at `:163`). `Detect` hands it on to
`DerivedBaseRuntimeDescriptor.Evidence` (`:307`). No code reads either property. A history line of an `ENV`
instruction can hold the values of other variables, so the descriptor carries raw image metadata out of the detector
for no purpose.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | The detector stores the raw environment entry or the full history line as evidence | `DerivedBaseRuntimeDetector.cs:39,50,61` (inspect entries), `:81,91,102,115,126` (history line), `:163` (stored), `:307` (copied to the descriptor) | confirmed |
| 2 | No code reads `Evidence` | `grep -rn Evidence src` (`*.cs`, `*.razor`): only the two declarations (`DerivedBaseRuntimeDescriptor.cs:35`, `Images/Data/RuntimeCandidate.cs:50`) and the two writes above; no test sets or reads it | confirmed |
| 3 | No current leak: the descriptor is not persisted, logged or shown with this field | follows from fact 2; the orchestrators use `Kind`, `RuntimeVersion`, `ChannelVersion`, `Source` only | confirmed |
| 4 | An `ENV` history line can hold other variables' values | the history regex matches anywhere in the line (`:218`), so `ENV DOTNET_VERSION=8.0.11 API_TOKEN=x` yields a candidate whose evidence is the whole line | confirmed |
| 5 | Limiting evidence to source and parsed version would only repeat `Source` and `RuntimeVersion` | `DerivedBaseRuntimeDescriptor.cs:15-30` | confirmed |
| 6 | The raw inputs otherwise stay in transient DTOs (`DockerImageHistoryEntryData`, `DockerImageInspectData`, `RegistryImageConfigurationData`) | grep of `CreatedBy`, `EnvironmentVariables` in `src/`: read in `DockerInstanceClient.cs:777,795`, `OciRegistryClient.cs:632`, `DockerHubClient.cs:792`, consumed only by the detector | confirmed |

## Acceptance criteria

- AC1: For a history entry whose `CreatedBy` is `/bin/sh -c #(nop)  ENV DOTNET_VERSION=8.0.11 API_TOKEN=s3cr3t-value`
  and no inspect data, `Detect` returns a descriptor with source `HistoryEnvironment` and version `8.0.11`, and the
  string form of none of the descriptor's public properties contains `s3cr3t-value` or `API_TOKEN`.
- AC2: Detection results (kind, version, channel, source) are unchanged for every existing input.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | DerivedBaseRuntimeDetectorDetectHistoryEnvironmentWithFurtherVariablesKeepsNoRawText | src/Tests/DockerUpdateGuard.Tests/DerivedBaseRuntimeDetectorTests.cs |
| AC2 | the existing tests of DerivedBaseRuntimeDetectorTests, ImageScanOrchestratorTests and RuntimeContainerScanOrchestratorTests, unchanged | src/Tests/DockerUpdateGuard.Tests/DerivedBaseRuntimeDetectorTests.cs |

The AC1 test enumerates the public instance properties of `DerivedBaseRuntimeDescriptor` by reflection and checks
each value's `ToString()`, so a raw-text member added later fails it too; it compiles before and after the change and
fails on the unfixed code. The Tester writes it in step 6.

## Approach

- `RuntimeCandidate`: delete the `Evidence` property with its documentation.
- `DerivedBaseRuntimeDescriptor`: delete the `Evidence` property with its documentation.
- `DerivedBaseRuntimeDetector.AddVersionCandidate`: delete the `evidence` parameter, its `<param>` line and the
  `Evidence = evidence` assignment; the seven call sites drop their last argument (`environmentVariable`,
  `entryText`). `entryText` at `:81` stays: the history regexes still match on it.
- `Detect`: delete `Evidence = bestCandidate.Evidence`.

No user-visible output changes.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Images/DerivedBaseRuntimeDetector.cs` | private parameter and two assignments removed |
| DockerUpdateGuard | `Images/DerivedBaseRuntimeDescriptor.cs` | property removed |
| DockerUpdateGuard | `Images/Data/RuntimeCandidate.cs` | property removed |

## Signatures (for the Dev's skeleton)

Deleted: DerivedBaseRuntimeDescriptor.Evidence (public string property).
Deleted: RuntimeCandidate.Evidence (internal type's property).
The private method AddVersionCandidate loses its last parameter (Approach); no new member.

## Test files

src/Tests/DockerUpdateGuard.Tests/DerivedBaseRuntimeDetectorTests.cs (one new test).

Existing test code that calls a changed signature: none (no test sets or reads `Evidence`).

Files of the change no unit test can cover: none.

## Areas

docs/areas/base-images.md (+25 words): *Derived runtime detection* gains one bullet that the detection result
carries only the runtime kind, version, channel and signal source, never the raw environment entry or history text.

## Documentation updates

- `docs/areas/base-images.md`, *Derived runtime detection*: the bullet of *Areas* (Dev). No changelog exists; no
  `README.md` or `docs/ARCHITECTURE.md` statement names the field (grep `Evidence`/`evidence` in `docs/`: only the
  unrelated heading `ARCHITECTURE.md:582`).

## Architecture check

No guarantee of `.squad/project.md` is touched; the change narrows what the detector returns and does not alter
layering or any flow in `docs/ARCHITECTURE.md`.

## Security considerations

- [x] Every byte or length limit on parsed input says whether it applies before or after decoding — no limits.
- [x] Every exception that can reach a user-visible failure reason or message — none.

No security area of `.squad/project.md` is touched: the field is neither logged nor persisted, and no guard is added
or removed; the change removes a latent path for raw metadata.

## Decision records

- none: no decision beyond the obvious fix; the area document states the rule.

## Out of scope / follow-ups

- none.
