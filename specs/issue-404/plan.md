# Plan: Remove the unused client-side base-image chain resolution

Source: Issue #404
Tier: standard — removes production API (members of three public interfaces, their implementations and an unregistered resolver) without changing what runs in production, and it is the choice between the issue's two options.
Security areas touched: Registry and Docker Scout calls (`DockerHub/DockerHubClient.cs`, `Images/OciRegistryClient.cs`: request code of an unreachable path is deleted; no guard on a reachable path changes)

## Problem / root cause

Two base-image chain implementations exist. The registered one, `RegistryBaseImageResolver`
(`ServiceCollectionExtensions.cs:121`), reads labels from `IRegistryMetadataService.GetImageConfigurationAsync`, whose
labels dictionary is case-insensitive (`ImageConfigurationLabelReader.ReadLabels`, record 0021). The older client-side
chains were left in place when it replaced `DockerHubBaseImageResolver`: `OciRegistryClient.ResolveBaseImagesAsync`
(`OciRegistryClient.cs:1579` → `ResolveBaseImageChainAsync` :893 → `ExtractBaseImageFromConfigAsync` :1055, labels read
at :1090/:1097 via `TryGetString` :586) and `DockerHubClient.ResolveBaseImagesAsync` (`DockerHubClient.cs:1586` → :614 →
:833, labels at :869/:876 via `TryGetString` :488). Both `TryGetString` use `JsonElement.TryGetProperty`, which is
case-sensitive. Nothing in production reaches them, so the divergence is latent: it would surface only if someone wired
the dead path back in.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | Client chains read the base-image labels case-sensitively | `OciRegistryClient.cs:586-591`, `DockerHubClient.cs:488-497`: `element.TryGetProperty(propertyName, …)` | confirmed |
| 2 | The registered resolver reads them through the case-insensitive configuration labels | `RegistryBaseImageResolver.cs:87,93` on `RegistryImageConfigurationData.Labels`; built by `ImageConfigurationLabelReader.ReadLabels` (`StringComparer.OrdinalIgnoreCase`), used at `OciRegistryClient.cs:630`, `DockerHubClient.cs:933` | confirmed |
| 3 | `DockerHubBaseImageResolver` is not registered | Only `IBaseImageResolver` registration is `RegistryBaseImageResolver` (`ServiceCollectionExtensions.cs:121`); `ServiceCollectionExtensionsTests.cs:61` asserts it | confirmed |
| 4 | `RegistryMetadataService.ResolveBaseImagesAsync` has no caller | Repository grep for `ResolveBaseImagesAsync` outside tests: only declarations (`IRegistryMetadataClient.cs:59`, `IRegistryMetadataService.cs:50`, `IDockerHubClient.cs:72`), implementations, `RegistryMetadataService.cs:109` and `DockerHubBaseImageResolver.cs:40`; no `.razor` hit | confirmed |
| 5 | No production type other than the clients implements the three interfaces | grep `: IRegistryMetadataClient`/`IRegistryMetadataService`/`IDockerHubClient`: `DockerHubClient`, `OciRegistryClient`, `NullRegistryMetadataClient`, `RegistryMetadataService`; tests use NSubstitute substitutes only | confirmed |
| 6 | The private helpers `GetRegistryTokenAsync`, `GetImageConfigDigestAsync`, `GetConfigDigestFromManifestListAsync`, `SelectManifestDigest` (Docker Hub) and `GetManifestMetadataAsync`, `CreateBlobUri`, `TryGetString` (OCI) stay in use | Callers `DockerHubClient.cs:1653,1660` (`GetImageConfigurationAsync`); `OciRegistryClient.cs:1420,1441` (`GetImageConfigurationCoreAsync`) and the tag lookups | confirmed |
| 7 | `BaseImageLabelReader` stays in use | `RegistryBaseImageResolver.cs:95` | confirmed |
| 8 | Log events 3207 `DockerHubBaseImageChainResolved` and 3210 `DockerHubBaseImageResolutionFailed` are used only by `DockerHubClient.ResolveBaseImagesAsync`; 3208/3209 stay used | `DockerHubClient.cs:1612,1617`; 3208 at :694, 3209 at :735, :805; no doc lists 3207/3210 | confirmed |
| 9 | The `dockerhub.request` span around base-image resolution exists only in `DockerHubClient.ResolveBaseImagesAsync` | `DockerHubClient.cs:1600`; others at :1439, :1515; documented in `docs/areas/telemetry.md:52` | confirmed |
| 10 | `docs/areas/registries.md:56-57` describe outcomes of the dead path ("A base-image resolution ends its chain without base images and succeeds", "ends the chain (base images)") | With the registered resolver an observed image whose configuration lookup answers *not found* gives *not found* (`RegistryBaseImageResolver.cs:169-174`, `MapConfigurationFailure`), i.e. "resolution unavailable" per `base-images.md`; invalid base references are already dropped by `BaseImageLabelReader`; a related doc defect, fixed under *Documentation updates* | confirmed |
| 11 | Record 0021 is unreleased and its option 1 says `TryGetProperty` "reads the base-image labels elsewhere in both clients" | Added by 3189a14, `git tag --contains` empty after `git fetch --tags`; latest tag v1.3.8; the sentence is fixed by the Lead in this step | confirmed |
| 12 | Related defect in the dead code: the Docker Hub chain fetched every deeper base from Docker Hub whatever its registry | `DockerHubClient.cs:614-662` recurses with `baseImageRef` into the Docker Hub endpoints; `RegistryBaseImageResolverTests.cs:33` covers the correct behaviour of the live path; goes away with this change | confirmed |

## Acceptance criteria

- AC1: The base-image chain has a single implementation: in the application assembly, the only public instance
  methods returning `Task<ExternalOperationResult<IReadOnlyList<BaseImageDescriptor>>>` are declared by
  `IBaseImageResolver` and `RegistryBaseImageResolver`. (`ResolveBaseImagesAsync` is gone from `IRegistryMetadataClient`,
  `IRegistryMetadataService`, `IDockerHubClient`, `RegistryMetadataService`, `NullRegistryMetadataClient`,
  `OciRegistryClient`, `DockerHubClient`; `DockerHubBaseImageResolver` is deleted.)
- AC2: Behaviour of code that stays in production and was tested only through the removed methods keeps a test:
  (a) the Docker Hub image configuration lookup skips an `unknown/unknown` (attestation) entry of a multi-platform
  index; (b) a base-image digest label that carries an image reference (`mcr.microsoft.com/dotnet/runtime@sha256:runtime`)
  yields only the digest `sha256:runtime`.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `RegistryBaseImageResolverWhenAssemblyIsScannedIsTheOnlyBaseImageChainResolver` (reflection over `typeof(RegistryBaseImageResolver).Assembly.GetTypes()`, `BindingFlags.Public \| Instance \| DeclaredOnly`, interfaces included; expected declaring types exactly `IBaseImageResolver`, `RegistryBaseImageResolver`) | `src/Tests/DockerUpdateGuard.Tests/RegistryBaseImageResolverTests.cs` |
| AC2 | (a) `DockerHubClientGetImageConfigurationAsyncSkipsAttestationManifestAsync` — the fixture of the removed `DockerHubClientResolveBaseImagesAsyncSkipsAttestationManifestAsync`, asserting the platform manifest's configuration is read and the attestation digest is never requested; (b) `BaseImageLabelReaderReadReferenceWhenDigestLabelHasImageReferenceKeepsDigestOnly` | `src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs`, `src/Tests/DockerUpdateGuard.Tests/BaseImageLabelReaderTests.cs` |

AC1's test fails on the current code (step 5). AC2's tests pass on it (they keep coverage the deletions would drop): deferred
to step 6, written by the Tester in its coverage pass.

## Approach

1. Step 4 (skeleton) does not apply: no new or changed signature; members are only removed, which is the implementation.
2. Step 5, Tester: write the AC1 test; delete the tests that call the removed members (list under *Test files*), so
   *Build* passes once the Dev removes them.
3. Step 6, Dev: remove
   - `ResolveBaseImagesAsync` from `IRegistryMetadataClient`, `IRegistryMetadataService`, `IDockerHubClient`,
     `RegistryMetadataService`, `NullRegistryMetadataClient`;
   - from `OciRegistryClient`: `ResolveBaseImagesAsync`, `ResolveBaseImageChainAsync`, `ExtractBaseImageFromConfigAsync`,
     constants `MaxBaseImageDepth`, `OciBaseImageNameLabel`, `OciBaseImageDigestLabel`;
   - from `DockerHubClient`: the same three methods and three constants;
   - from `DockerHubClientLogging`: `DockerHubBaseImageChainResolved` (3207), `DockerHubBaseImageResolutionFailed` (3210);
   - the file `Images/DockerHubBaseImageResolver.cs`;
   - `using` directives that become unused (analyzer decides).
   Event IDs 3207/3210 are left unassigned, not reused.
4. Step 6, Tester: AC2 tests; then coverage.

No user-visible output changes.

## Options considered

1. **Remove the client-side chains (chosen).** Nothing calls them; one implementation cannot disagree with itself, and the
   live path already has the case-insensitive labels, the registry-aware recursion and its tests.
2. **Make the client chains read the configuration labels (the issue's alternative).** Keeps two recursions with their own
   depth, failure and registry handling — the Docker Hub one already follows deeper bases into the wrong registry (fact 12) —
   plus ~16 tests for code with no caller; the two paths could still disagree on everything but the label case.
3. **Keep the chains and mark them obsolete.** Same divergence risk, and `[Obsolete]` on interface members adds analyzer
   noise for code nobody uses.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Images/Interfaces/IRegistryMetadataClient.cs`, `IRegistryMetadataService.cs`, `DockerHub/IDockerHubClient.cs` | member removed |
| DockerUpdateGuard | `Images/RegistryMetadataService.cs`, `Images/NullRegistryMetadataClient.cs` | member removed |
| DockerUpdateGuard | `Images/OciRegistryClient.cs`, `DockerHub/DockerHubClient.cs` | chain methods and constants removed |
| DockerUpdateGuard | `DockerHub/DockerHubClientLogging.cs` | two log methods removed |
| DockerUpdateGuard | `Images/DockerHubBaseImageResolver.cs` | deleted |

## Signatures (for the Dev's skeleton)

None (removals only; no skeleton).

## Test files

- `src/Tests/DockerUpdateGuard.Tests/RegistryBaseImageResolverTests.cs` (AC1), `DockerHubClientTests.cs` (AC2a),
  `BaseImageLabelReaderTests.cs` (AC2b).

Existing call sites of removed members, deleted by the Tester in step 5 (surviving test of the same behaviour on the live
path in brackets):

- `OciRegistryClientTests.cs`: `[DataRow("BaseImages")]` and its `case` in
  `OciRegistryClientCallsWhenRegistryIsDockerHubReturnUnsupportedAsync`; `…ResolveBaseImagesAsyncWhenConfigHasLoneSurrogateReturnsInvalidResponseAsync`
  (`…GetImageConfigurationAsyncWhenConfigHasLoneSurrogate…`), `…NormalizesDigestLabelReferenceAsync` (AC2b),
  `…WhenTransportFailsReportsConnectionFailedAsync` (none: OCI configuration lookups let transport errors escape, known gap
  #272), `…WhenBaseImageLabelsHaveControlCharactersReplacesThemAsync` (`…GetImageConfigurationAsyncWhenFieldsHaveControlCharacters…`),
  `…WhenBaseImageLabelIsInvalidReturnsEmptyChainAsync` (`RegistryBaseImageResolverTests` :157, :193),
  `…DotSegmentRepositoryReturnsNoBaseImagesWithoutRequestAsync` (`…GetImageConfigurationAsyncDotSegmentReference…`),
  `…DotSegmentConfigDigestEndsChainWithoutBlobRequestAsync` (`…GetImageConfigurationAsyncDotSegmentConfigDigest…`),
  `…InvalidRegistryReturnsNoBaseImagesWithoutRequestAsync` (`…GetImageConfigurationAsyncInvalidRegistry…`).
- `DockerHubClientTests.cs`: the `DockerHubClientResolveBaseImagesAsync…` tests at :351, :947 (AC2b), :1041 (AC2a), :1319
  (`…GetImageConfigurationAsyncWhenTransportFails…`), :1712, :1802, :2088, :2252, with the same counterparts as above.

Files no unit test can cover: none.

## Areas

- Registries (`docs/areas/registries.md`): the request-path outcomes no longer list a base-image resolution of its own.
- Telemetry (`docs/areas/telemetry.md`): `dockerhub.request` no longer wraps a base-image resolution.
- Base images: no change (it already describes the registered resolver).

## Documentation updates

Owned by the Dev, written against the code once green:

- `docs/areas/registries.md:56-57`: drop "A base-image resolution ends its chain without base images and succeeds." and
  the "ends the chain (base images)" outcome of a manifest-named digest; base-image resolution reads configurations
  through these lookups, and its outcome is described in [Base images](base-images.md#resolving-the-chain).
- `docs/areas/telemetry.md:52`: "A Docker Hub repository lookup or tag lookup".
- `docs/ARCHITECTURE.md`: none (§ on `IBaseImageResolver` stays true). `README.md`: none. No changelog in the repository.
- Record 0021 option 1 wording: done by the Lead in step 2.

Sweep: `docs/areas/*.md`, `docs/ARCHITECTURE.md`, `README.md` searched for `ResolveBaseImages`, `DockerHubBaseImageResolver`,
`base-image resolution`, `base image chain`, `dockerhub.request`, `3207`, `3210`; hits other than the two above are generic
references to the base-image scan and stay true.

## Architecture check

No guarantee of `.squad/project.md` or `docs/ARCHITECTURE.md` is touched; the dispatcher stays as described (one fewer
method), and the only base-image path is the one the architecture already names.

## Security considerations

- [x] No limits: no parsing changes.
- [x] Exceptions reaching a user-visible message: none new; removed code only. The live path's failure texts are unchanged.

## Decision records

- none: removing dead code is routine; the reason sits in this plan and the PR. Record 0021 (unreleased) gets a wording
  fix only, no change of decision.

## Out of scope / follow-ups

- Transport errors escaping OCI configuration lookups: known gap #272.