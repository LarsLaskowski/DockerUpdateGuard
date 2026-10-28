# Plan: Remote image references and names outside registry tag data can exceed stored column lengths

Source: Issue #355
Status: Draft
Tier: standard — the change keeps malformed or over-long remote values from breaking a save (input handling: grammar and length checks, cuts of display names); it creates, changes or removes no security control.
Security areas touched: Registry and Docker Hub calls (`OciRegistryClient`, `DockerHubClient`), Docker Engine access (`DockerInstanceClient`), Logging and telemetry of external data (log entries that carry a runtime image reference)

## Problem / root cause

PostgreSQL refuses a value longer than its `varchar(n)` column, and one such value fails the final `SaveChangesAsync` of a whole
scan or discovery run; the test databases (SQLite, InMemory) do not enforce lengths. #348 bounded tag names and digests
([0014](../../docs/decisions/0014-registry-tag-names-and-digests-are-validated-where-they-are-read.md)); these remote values still
reach length-limited columns unchecked:

1. **Base-image label reference.** `RegistryBaseImageResolver.TryParseBaseImageReference` (`RegistryBaseImageResolver.cs:84-103`,
   the production `IBaseImageResolver`, `ServiceCollectionExtensions.cs:121`) and the clients' own chains
   (`OciRegistryClient.cs:959-975`, `DockerHubClient.cs:788-805`) parse `org.opencontainers.image.base.name` without any check.
   `ImageScanOrchestrator.ProcessBaseImageAsync` (`ImageScanOrchestrator.cs:570-628`) stores registry, repository, the tag
   (`ResolveStoredBaseImageTagAsync` returns the label's tag at lines 273, 296 and 308-310) and digest in the catalog and
   `SourceReference` (500) on the relationship. A label the parser rejects (`:12`, `/`) throws `ArgumentException` out of the
   resolver and fails the whole observed-image scan (related defect, `ImageScanOrchestrator.cs:759-765`).
2. **Docker Hub account listing.** `DockerHubClient.ParseRepository` (`DockerHubClient.cs:330-345`) builds `namespace/name`
   unchecked; `DockerHubAccountImageDiscoveryService` stores it as catalog repository (300) and as `ObservedImage.Name` (200,
   lines 217 and 231).
3. **Docker Engine.** `DockerInstanceClient.ParseContainer`/`ParseResourceSample` (`DockerInstanceClient.cs:498-538`) pass ID,
   name and the compose/stack/service labels unbounded into `ContainerSnapshot` (200 each) and `RuntimeContainerResourceSample`
   (`ContainerId`, `ContainerName`, 200 each). `RuntimeContainerScanOrchestrator.ProcessContainerAsync` (lines 564-580) stores the
   parsed container image reference and digest in the catalog unchecked, and `ApplyUnevaluatedTagResult` (lines 756-794) puts
   the raw reference into `UpdateAssessmentMessage` (2000).

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | Issue: registry and repository columns are 300 | `RegistryRepositoryConfiguration.cs:24-29`: Registry 200, Repository 300 (the registry column is 200) | refuted |
| 2 | Issue: tag 200, digest 255, `SourceReference` 500, `ObservedImage.Name` 200, `ContainerSnapshot` names 200 | `ImageVersionConfiguration.cs:27-32`, `ImageRelationshipConfiguration.cs:27-28`, `ObservedImageConfiguration.cs:24-25`, `ContainerSnapshotConfiguration.cs:24-39` | confirmed |
| 3 | Also limited and not named by the issue: `RuntimeContainerResourceSample.ContainerId`/`ContainerName` (200), `UpdateAssessmentMessage` (2000) | `RuntimeContainerResourceSampleConfiguration.cs`, `ContainerSnapshotConfiguration.cs:47-48` | confirmed |
| 4 | Issue: `OciRegistryClient.cs:963-972` and the Docker Hub `ExtractBaseImageFromConfigAsync` are the stored path | Only `RegistryBaseImageResolver` is registered; `IRegistryMetadataService.ResolveBaseImagesAsync` and `DockerHubBaseImageResolver` have no production caller (grep); the stored path is `RegistryBaseImageResolver.cs:84-103`, the client chains are reachable only through their public interface | refuted |
| 5 | Issue: #348 keeps an invalid requested tag out of `MetadataJson` | `GetTagAsync` answers not found with `RegistryReferenceValidator.InvalidTagMessage` | confirmed |
| 6 | Issue: the full reference reaches the refresh log entry | `ImageScanOrchestrator.cs:608-613` (log and scan-run message, `ErrorMessage` is `text`); bounded once the parts are | confirmed |
| 7 | Issue: a U+0000 in a remote string can reach a `text` column | Every stored remote text field passes `ResponseBodyExcerpt.ReplaceControlCharacters` since #353/#359 (category Cc includes U+0000, `ResponseBodyExcerpt.cs:145-151`); tag names and digests are held to grammars without it; already fixed, no change | refuted |
| 8 | Reference grammar | `distribution/reference` `regexp.go` (commit 0965666): `domainNameComponent`, `ipv6address`, `optionalPort`, `pathComponent = [a-z0-9]+ ((?:[._]\|__\|[-]+)[a-z0-9]+)*`; `reference.go:255`: the **path** (without domain) is at most 255 characters (read in a scratch clone) | confirmed |
| 9 | The parser lowercases registry and repository and throws `ArgumentException` for a reference without a repository | `ImageReferenceParser.cs` `Parse` | confirmed |
| 10 | Container actions do not look containers up by the stored name | No production code builds a `PortainerActionRequest` (grep) | confirmed |
| 11 | PostgreSQL rejects an over-long `varchar(n)` value and the save of the run fails | `varchar(n)` semantics; #348 analysis (0014, *Context*) | confirmed |

## Acceptance criteria

- [ ] AC1: `RegistryReferenceValidator.IsValidRegistry` accepts a host of the reference grammar (dot-separated components of
  `[A-Za-z0-9]` with inner `-`, or a bracketed IPv6 address of `[A-Fa-f0-9:]`) with an optional `:port` (digits) and at most
  200 characters, and rejects anything else (null, empty, a scheme, a space, `_`, a leading or trailing `-` or `.`, 201
  characters). `IsValidRepository` accepts `/`-separated path components `[a-z0-9]+` joined by `.`, `_`, `__` or `-+`, at most 255
  characters, and rejects anything else (null, empty, uppercase, `___`, an empty segment, a leading separator, a space, 256
  characters). Length is checked before the expression runs.
- [ ] AC2: `RegistryReferenceValidator.ValidReferenceOrNull` returns null when registry, repository or tag (existing `IsValidTag`)
  is invalid; otherwise it returns the same instance with a digest outside `ValidDigestOrNull` set to null.
- [ ] AC3: `BaseImageLabelReader.ReadReference(name, digestLabel)` returns null for a blank name, a name the parser rejects or a
  reference AC2 rejects; otherwise the parsed reference, whose digest is the name's own or else `NormalizeDigest(digestLabel)`,
  null when outside the digest grammar. `RegistryBaseImageResolver`, `OciRegistryClient` and `DockerHubClient` use it, so an
  invalid label ends the chain at that depth (the chain so far is kept, the resolution succeeds) and a parser rejection no
  longer fails the scan.
- [ ] AC4: `ImageScanOrchestrator` stores a relationship's `SourceReference` longer than 500 characters cut to 500
  (`ResponseBodyExcerpt.CutToLength`); a shorter one unchanged.
- [ ] AC5: `DockerHubClient.GetRepositoriesAsync` reports a repository whose lowercased `namespace/name` fails `IsValidRepository`
  with an empty `Repository` (the discovery already skips it); `DockerHubAccountImageDiscoveryService` sets `ObservedImage.Name`
  (new and existing) to the repository cut to 200 characters.
- [ ] AC6: `DockerInstanceClient.DiscoverContainersAsync` and `CollectContainerResourceUsageAsync` leave out a container whose
  ID is longer than 200 characters (no stats request for it); the container name and the compose project, stack name and
  service name labels longer than 200 characters are cut to 200; a missing label stays null.
- [ ] AC7: `RuntimeContainerScanOrchestrator`: a container whose parsed reference AC2 rejects produces no snapshot and no catalog
  entry, and the run ends *partial* with exactly `Container '<name>' could not be processed: invalid image reference`; a digest
  outside the grammar (from the reference or the repository digests) leaves the catalog entry without digest; the fallback
  messages of `ApplyUnevaluatedTagResult` (`Unable to evaluate runtime image '…'`, `Runtime image '…' cannot be evaluated by the
  current registry adapters`) and its two log entries carry `parsedReference.FullReference` instead of the raw reference.
- [ ] AC8: `ResponseBodyExcerpt.CutToLength(text, maxLength)` returns null for null, the text unchanged when it has at most
  `maxLength` characters, otherwise `Truncate(text, maxLength - 1)`: at most `maxLength` characters ending in `…`, never
  splitting a surrogate pair.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `RegistryReferenceValidatorIsValidRegistryWhenHostMatchesGrammarReturnsTrue` (rows: `docker.io`, `localhost:5000`, `registry.example.com:443`, `[::1]:5000`, a 200-character host); `RegistryReferenceValidatorIsValidRegistryWhenInvalidReturnsFalse` (rows: null, empty, `https:`, `reg istry.example`, `under_score.example`, `-lead.example`, `trail.example.`, `host:port`, 201 characters); `RegistryReferenceValidatorIsValidRepositoryWhenPathMatchesGrammarReturnsTrue` (rows: `library/nginx`, `a__b/c-d.e`, `a---b`, 255 characters); `RegistryReferenceValidatorIsValidRepositoryWhenInvalidReturnsFalse` (rows: null, empty, `Library/nginx`, `_a`, `a___b`, `a//b`, `a/`, `a b`, 256 characters) | `src/Tests/DockerUpdateGuard.Tests/RegistryReferenceValidatorTests.cs` |
| AC2 | `RegistryReferenceValidatorValidReferenceOrNullWhenPartIsInvalidReturnsNull` (rows: registry, repository, 129-character tag); `RegistryReferenceValidatorValidReferenceOrNullWhenDigestIsInvalidRemovesDigest`; `RegistryReferenceValidatorValidReferenceOrNullWhenValidKeepsAllParts` | same |
| AC3 | `BaseImageLabelReaderReadReferenceWhenNameIsValidReturnsReference`; `BaseImageLabelReaderReadReferenceWhenNameCannotBeParsedReturnsNull` (rows: `:12`, `/`); `BaseImageLabelReaderReadReferenceWhenPartIsInvalidReturnsNull` (rows: 201-character registry, 256-character repository, 129-character tag, `debian 12`); `BaseImageLabelReaderReadReferenceWhenDigestIsInvalidReturnsReferenceWithoutDigest` | `src/Tests/DockerUpdateGuard.Tests/BaseImageLabelReaderTests.cs` |
| AC3 | `RegistryBaseImageResolverResolveAsyncWhenBaseImageLabelIsInvalidEndsChainAsync` (valid depth 1, 256-character repository at depth 2: one entry, succeeded); `RegistryBaseImageResolverResolveAsyncWhenBaseImageLabelCannotBeParsedReturnsEmptyChainAsync` | `src/Tests/DockerUpdateGuard.Tests/RegistryBaseImageResolverTests.cs` |
| AC3 | `OciRegistryClientResolveBaseImagesAsyncWhenBaseImageLabelIsInvalidReturnsEmptyChainAsync` | `src/Tests/DockerUpdateGuard.Tests/OciRegistryClientTests.cs` |
| AC3 | `DockerHubClientResolveBaseImagesAsyncWhenBaseImageLabelIsInvalidReturnsEmptyChainAsync` | `src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs` |
| AC4 | `ImageScanOrchestratorScanAsyncWhenSourceReferenceExceedsStoredLengthCutsItAsync` (501 characters: stored length 500, ends with `…`) | `src/Tests/DockerUpdateGuard.Tests/ImageScanOrchestratorTests.cs` |
| AC5 | `DockerHubClientGetRepositoriesAsyncWhenRepositoryNameIsInvalidReturnsEmptyRepositoryAsync` (rows: name with a space, 256-character total) | `src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs` |
| AC5 | `DockerHubAccountImageDiscoveryServiceSynchronizeAccountImagesAsyncWhenRepositoryExceedsNameLengthCutsNameAsync` (new and existing observed image; repository stays whole in the catalog) | `src/Tests/DockerUpdateGuard.Tests/DockerHubAccountImageDiscoveryServiceTests.cs` |
| AC6 | `DockerInstanceClientDiscoverContainersAsyncWhenContainerIdExceedsStoredLengthSkipsContainerAsync`; `DockerInstanceClientDiscoverContainersAsyncWhenNameOrLabelsExceedStoredLengthCutsThemAsync`; `DockerInstanceClientCollectContainerResourceUsageAsyncWhenContainerIdExceedsStoredLengthSkipsContainerAsync` (asserts no stats request); `DockerInstanceClientCollectContainerResourceUsageAsyncWhenNameExceedsStoredLengthCutsItAsync` | `src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientTests.cs` |
| AC7 | `RuntimeContainerScanOrchestratorScanAllAsyncWhenImageReferenceIsInvalidRecordsNoSnapshotAsync` (256-character repository: no snapshot, no image version, run *partial*, exact message); `RuntimeContainerScanOrchestratorScanAllAsyncWhenImageDigestIsInvalidStoresVersionWithoutDigestAsync`; `RuntimeContainerScanOrchestratorScanAllAsyncWhenTagResultHasNoMessageUsesNormalizedReferenceAsync` (raw reference padded with spaces) | `src/Tests/DockerUpdateGuard.Tests/RuntimeContainerScanOrchestratorTests.cs` |
| AC8 | `ResponseBodyExcerptCutToLengthWhenTextIsNullReturnsNull`; `ResponseBodyExcerptCutToLengthWhenTextFitsReturnsItUnchanged`; `ResponseBodyExcerptCutToLengthWhenTextIsLongerCutsWithMarker`; `ResponseBodyExcerptCutToLengthWhenCutWouldSplitSurrogatePairKeepsPairWhole` | `src/Tests/DockerUpdateGuard.Tests/ResponseBodyExcerptTests.cs` |

The test databases do not enforce column lengths, so the tests assert the stored values and their lengths, not a failing save.

## Approach

Rule per value, applied where the value is read or parsed (0014's principle):

| Value | Rule | Where |
| ----- | ---- | ----- |
| Image reference of a base-image label | validate registry, repository, tag (AC2); invalid → no base image; invalid digest → no digest | `BaseImageLabelReader`, called at the three parse sites |
| Image reference of a Docker Engine container | validate after the digest fallback (AC2); invalid → no snapshot, fixed message | `RuntimeContainerScanOrchestrator.ProcessContainerAsync`, right after the digest assignment, before `GetOrCreateImageVersionAsync` |
| Docker Hub `namespace/name` | `IsValidRepository`; invalid → empty repository (skipped) | `DockerHubClient.ParseRepository` |
| Container ID | longer than 200 → container left out | `DockerInstanceClient` listing and resource loops |
| Container name, compose/stack/service labels, discovered image name, `SourceReference` | cut with `CutToLength` | `DockerInstanceClient.ParseContainer`/`GetPrimaryName`, discovery service, `ImageScanOrchestrator.ProcessBaseImageAsync` |

- Lengths are UTF-16 code units of the decoded, control-character-replaced string; a PostgreSQL `varchar(n)` counts code points,
  so the bound is never too large.
- `BaseImageLabelReader.ReadReference`: blank name → null; `new ImageReferenceParser().Parse(name)` in a `try` catching only
  `ArgumentException` → null; when the parsed digest is blank and the digest label is not, set
  `ImageReferenceParser.NormalizeDigest(digestLabel)`; return `RegistryReferenceValidator.ValidReferenceOrNull(parsed)`. The three
  callers keep reading and cleaning the label values as today and replace their parse block with this call.
- `ValidReferenceOrNull` mutates and returns the given instance (documented); registry and repository checks run before the tag.
- Regexes: `[GeneratedRegex(..., RegexOptions.CultureInvariant, 100)]` like the existing ones:
  registry `\A(?:(?:[a-zA-Z0-9]|[a-zA-Z0-9][a-zA-Z0-9-]*[a-zA-Z0-9])(?:\.(?:[a-zA-Z0-9]|[a-zA-Z0-9][a-zA-Z0-9-]*[a-zA-Z0-9]))*|\[[a-fA-F0-9:]+\])(?::[0-9]+)?\z`,
  repository `\A[a-z0-9]+(?:(?:[._]|__|-+)[a-z0-9]+)*(?:/[a-z0-9]+(?:(?:[._]|__|-+)[a-z0-9]+)*)*\z`.
- Runtime scan rejection returns the failure message without logging (the run's error message names the container, as for an
  unparsable reference today); the inspect call before it stays where it is.
- No new user-visible layout: the one new text is fixed by AC7.

## Options considered

1. **Grammar and column-length checks where read, cuts only for display names** — chosen (see table).
2. **Length checks only** — keeps references Docker cannot pull (spaces, `_` in hosts) reaching requests, logs and pages; the
   grammar costs nothing more (0014, option 2).
3. **Cut every value where it is stored** — a cut registry, repository or tag names a different image (0014, option 4).
4. **Widen `SourceReference` to 1000 with a migration (`Update8`)** — a schema change for a display and grouping value whose real
   lengths stay far below 500 (a 255-character path with a 128-character tag and a SHA-256 digest is about 460 plus the registry);
   the deterministic cut keeps grouping stable. Lost.
5. **Docker Engine: drop a container with an invalid reference in the client** — the client does not parse references, and a
   silently missing container hides what the *partial* run now names.
6. **Container IDs: the Docker grammar (64 hexadecimal characters)** — an ID is an opaque key; other engines and many test
   fixtures use other forms; storage needs only the length. **Cut over-long IDs** — a cut ID addresses no container and can
   collide as a key.
7. **Skip a Docker Hub repository longer than 200 characters** — a pullable repository would vanish from *My images*; the name is
   display text and discovery matches by catalog repository.
8. **Fix only `RegistryBaseImageResolver`** — the client chains are public interface methods with tests; the shared reader costs
   one call each. Removing the unused chains is a follow-up.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Images/Helper/RegistryReferenceValidator.cs` | registry, repository and reference checks |
| DockerUpdateGuard | `Images/Helper/BaseImageLabelReader.cs` (new) | parse and validate a base-image label |
| DockerUpdateGuard | `Infrastructure/ResponseBodyExcerpt.cs` | `CutToLength` |
| DockerUpdateGuard | `Images/RegistryBaseImageResolver.cs`, `Images/OciRegistryClient.cs`, `DockerHub/DockerHubClient.cs` | use the reader; `ParseRepository` validates |
| DockerUpdateGuard | `Images/ImageScanOrchestrator.cs` | cut `SourceReference` |
| DockerUpdateGuard | `Images/DockerHubAccountImageDiscoveryService.cs` | cut `Name` |
| DockerUpdateGuard | `Docker/DockerInstanceClient.cs` | skip long IDs, cut names and labels |
| DockerUpdateGuard | `Images/RuntimeContainerScanOrchestrator.cs` | validate the reference, normalized reference in fallback messages |

## Signatures (for the Dev's skeleton)

- `RegistryReferenceValidator` (existing `internal static partial class`): `public const int MaxRegistryLength = 200;`,
  `public const int MaxRepositoryLength = 255;`, `public static bool IsValidRegistry([NotNullWhen(true)] string? registry)`,
  `public static bool IsValidRepository([NotNullWhen(true)] string? repository)`,
  `public static ImageReference? ValidReferenceOrNull(ImageReference reference)`, `private static partial Regex RegistryExpression()`,
  `private static partial Regex RepositoryExpression()`.
- `internal static class BaseImageLabelReader` (namespace `DockerUpdateGuard.Images.Helper`):
  `public static ImageReference? ReadReference(string? baseImageName, string? baseImageDigest)`.
- `ResponseBodyExcerpt`: `public static string? CutToLength(string? text, int maxLength)`.
- Private constants: `DockerInstanceClient.MaxContainerIdLength = 200`, `DockerInstanceClient.MaxNameLength = 200`,
  `DockerHubAccountImageDiscoveryService.MaxObservedImageNameLength = 200`, `ImageScanOrchestrator.MaxSourceReferenceLength = 500`.
- No existing signature changes; the skeleton rewrites none of the existing files beyond adding these members.

## Test files

The files of *Tests per criterion*; `BaseImageLabelReaderTests.cs` is new, the others exist.

Existing test call sites of a changed signature: none. Existing tests whose input a new criterion handles differently, adapted by
the **Tester** in step 5 (inputs moved so they still prove the control-character replacement and pass the grammar, for example
the replaced characters at the edges where trimming removes them, or the digest assertion changed to no digest):
`OciRegistryClientResolveBaseImagesAsyncWhenBaseImageLabelsHaveControlCharactersReplacesThemAsync`,
`DockerHubClientResolveBaseImagesAsyncWhenBaseImageLabelsHaveControlCharactersReplacesThemAsync` (both expect `dotnet/run time`,
`library/de bian`, `sha256:ab cd`) and `DockerHubClientGetRepositoriesAsyncWhenFieldsHaveControlCharactersReplacesThemAsync`
(expects `ac me/ap i`). The Tester sweeps `RuntimeContainerScanOrchestratorTests` and `RegistryBaseImageResolverTests` for further
references outside the grammar (none found by the Lead: digests such as `sha256:runtime-old` match the digest grammar).

Files no unit test can cover: none.

## Areas

- `docs/areas/image-catalog.md` — *Image references*: the rule for references read from a remote side; *Observed images*: a
  discovered image's name is cut to 200.
- `docs/areas/base-images.md` — *Resolving the chain*: an invalid or unparsable label ends the chain; invalid digest → none.
- `docs/areas/docker-instances.md` — container discovery, snapshots and resource statistics rules of AC6/AC7.
- `docs/areas/registries.md` — account discovery, *Remote text fields*, *Limits and errors*, *Known gaps*, related decisions.

## Documentation updates

Owned by the Dev, written against the code once green:

- `docs/areas/image-catalog.md` *Image references*: keep "no other validation when parsing", add that a reference read from a
  base-image label or a Docker Engine container is accepted only with registry, repository and tag of the grammar (limits 200,
  255, 128) and that an invalid digest becomes none, linking [0014]; *Observed images*: discovered name cut to 200 with `…`.
- `docs/areas/base-images.md` *Resolving the chain*: the chain also ends at a label that cannot be parsed or is invalid (link to
  the image-catalog rule); *Storing the chain*: the source reference is cut to 500 characters.
- `docs/areas/docker-instances.md` *Container discovery*: an ID over 200 characters leaves the container out; name and the three
  labels cut to 200; *Container snapshots*: the invalid-reference case and its exact message, and that a digest outside the
  grammar is stored as none; *Resource statistics*: the ID rule and the name cut.
- `docs/areas/registries.md`: *Docker Hub account discovery* (invalid repository names skipped like blank ones, name cut);
  *Remote text fields* (the "without a cut, the length stays as received" sentence gets the exception of the cut names, linking
  docker-instances); *Limits and errors* last bullet extended to names and references; *Known gaps*: remove the #355 bullet;
  *Related decisions*: 0014's new title.
- `docs/ARCHITECTURE.md` security posture (lines 426-429): add that names and image references read from remote sides are held
  to their grammars and stored lengths, or cut where they are display names ([0014]). Enumeration sweep: no other "only these"
  list in `ARCHITECTURE.md` or the area documents is extended.

## Architecture check

Integration clients still never throw for expected failures: the reader removes the parser's `ArgumentException` from the
base-image path. The catalog stays the single entry point; update detection stays pure; no entity, migration or layering
change. No guarantee of `.squad/project.md` is touched.

## Security considerations

- [ ] Limits apply after JSON decoding and control-character replacement, to UTF-16 lengths, before any regular expression runs
  (bounded input, 100 ms timeout as the existing expressions).
- [ ] Exceptions that can reach a user-visible message: a `RegexMatchTimeoutException` would surface as the existing fixed reason
  `unexpected error`; the new runtime-scan text is fixed (AC7). No raw remote text is added to any message; the fallback
  messages now carry the normalized, validated reference instead of the raw one.

## Decision records

- `docs/decisions/0014-registry-tag-names-and-digests-are-validated-where-they-are-read.md` — extended in place (unreleased:
  `git ls-remote --tags origin` shows v1.3.7 as the newest tag, and `git tag --contains 4477ce7` is empty), retitled, status
  `Proposed`, index row updated.

## Out of scope / follow-ups

- U+0000 (fact 7): already replaced since #359; no change.
- Manual registration (user input, not remote) keeps its behavior ([0009], *Consequences*).
- Follow-up issue (orchestrator): remove the unused base-image chain of the registry clients
  (`IRegistryMetadataClient.ResolveBaseImagesAsync`, `IRegistryMetadataService.ResolveBaseImagesAsync`,
  `IDockerHubClient.ResolveBaseImagesAsync`, `DockerHubBaseImageResolver`), which no production code calls.
