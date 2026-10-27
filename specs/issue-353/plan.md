# Plan: Control characters in remote metadata fields reach logs and messages

Source: Issue #353
Status: Draft
Tier: security — extends output neutralization (an output-escaping guard, `.squad/routing.md` *Tiers*) from the bounded excerpts to every text field the integration clients pass on.
Security areas touched: Logging and telemetry of external data; Docker Engine access (`DockerInstanceClient`); Registry and Docker Hub calls (`DockerHubClient`, `OciRegistryClient`, `DockerScoutVulnerabilityProvider`); External process execution (`TrivyVulnerabilityProvider`, JSON parsing of stdout)

## Problem / root cause

`ResponseBodyExcerpt.ReplaceControlCharacters` (`src/DockerUpdateGuard/Infrastructure/ResponseBodyExcerpt.cs:73-103`)
is called only from `Truncate` (`:120`, `:130`), i.e. for body excerpts, the OCI reason phrase, rejected origins,
Trivy's error line and Scout's unknown severity. The clients read every other remote text field with a plain
`JsonElement.GetString()` (or `System.Text.Json` deserialization) and pass it on unchanged:

- Docker Engine: `DockerInstanceClient.TryGetString` (`:572-586`), `TryGetLabel` (`:594-604`), `GetPrimaryName`
  (`:548-564`), `ReadStringArray` (`:832-845`) and the environment/layer projections of `ParseImageInspect` (`:780-794`).
- Registries: `OciRegistryClient.TryReadEnvironmentVariables` / `TryReadLabels` / `ParseImageConfiguration`
  (`:567-620`) and `ExtractBaseImageFromConfigAsync` (`:959-967`); `DockerHubClient.ParseRepository` (`:330-344`),
  `ExtractBaseImageFromConfigAsync` (`:788-795`), `ExtractImageConfigurationAsync` (`:843-871`), `GetCurrentUserAsync`
  (`:1140-1151`), `GetRepositoryAsync` (`:1283`).
- Vulnerabilities: `TrivyVulnerabilityProvider.MapAdvisories` (`:109-120`), `DockerScoutVulnerabilityProvider.MapAdvisories` (`:127-134`).

From there the values are logged (container name and image reference, `RuntimeContainerScanOrchestrator.cs:678-681`;
repository names, `DockerHubAccountImageDiscoveryService.cs:181,192`; base-image references,
`ImageScanOrchestrator.cs:610-613`; the Docker Hub user name, EventId 3204), stored (container snapshots, catalog
entries, observed-image descriptions, vulnerability findings) and shown. Fix: clean each field where its client reads
it, with the existing replacement and without a cut.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | The shared helper replaces Cc, Cf, Zl, Zp with a space and a lone surrogate with U+FFFD; production calls it only through `Truncate` | `ResponseBodyExcerpt.cs:73-103,120,130`; `grep -rn ReplaceControlCharacters src` (only `ResponseBodyExcerpt.cs` and its tests) | confirmed |
| 2 | Issue: the inspect failure message interpolates the image reference or ID the Engine reported | `DockerInstanceClient.cs:876` (the issue says 875), argument from `RuntimeContainerScanOrchestrator.cs:1076-1081` (`container.LocalImageId` or `container.ImageReference`) | confirmed |
| 3 | Issue: that inspect message reaches logs and messages | Both callers drop it: `GetRepositoryDigestsAsync` (`DockerInstanceClient.cs:138-148`) and `GetCurrentImageInspectDataAsync` (`RuntimeContainerScanOrchestrator.cs:1079-1085`) keep only the data. The same reference does reach the log at `:678-681` and the snapshot and catalog (`:560-595`) (refuted for that message; the field itself is confirmed in row 4) | refuted |
| 4 | Issue: container names and labels pass unchanged into logs, snapshots and pages | `ParseContainer` / `GetPrimaryName` / `TryGetLabel` (`DockerInstanceClient.cs:498-604`); snapshot `RuntimeContainerScanOrchestrator.cs:584-595`; log `:678-681` | confirmed |
| 5 | Issue: registry tag names pass unchanged | Refuted since #348: `RegistryReferenceValidator` (`Images/Helper/RegistryReferenceValidator.cs:36-48`, grammars without control characters) at `DockerHubClient.cs:245,264,1304` and `OciRegistryClient.cs:724,1215,1241` | refuted |
| 6 | Issue: vulnerability advisory texts pass unchanged into findings and pages | `TrivyVulnerabilityProvider.cs:109-120`, `DockerScoutVulnerabilityProvider.cs:129-133`; shown in `Vulnerabilities.razor:76-96`, `VulnerabilityFindingsTable.razor:39-64` (summary as `title` tooltip) | confirmed |
| 7 | Docker Hub repository names and descriptions, the base-image labels and the user name pass unchanged; the user name is logged; `GetCurrentUserAsync` has no production caller | `DockerHubClient.cs:330-344,788-803,1140-1151,1279-1284`; `OciRegistryClient.cs:959-973`; `grep -rn GetCurrentUserAsync src` (tests only) | confirmed |
| 8 | `ImageReferenceParser.Parse` keeps every character of a base-image reference (splits on `@`, `/`, `:` only) | `Images/ImageReferenceParser.cs`, `Parse` | confirmed |
| 9 | Image-configuration labels are read only by fixed names, by `RegistryBaseImageResolver` | `grep -rn "\.Labels\b" src/DockerUpdateGuard` → `RegistryBaseImageResolver.cs:86,97` only | confirmed |
| 10 | The registry clients' `TryGetString` also reads values they consume themselves: tokens, next-page links (input of `PaginationLinkGuard`, record 0004), `issued_at`, manifest and config digests, manifest-list platforms used only to select a digest | `DockerHubClient.cs:276-310,627-628,680,740,1088,1205,1404`; `OciRegistryClient.cs:232-335,538,869-871,1129-1130` | confirmed |
| 11 | No release-feed text reaches a sink: the .NET channel in a message equals the lookup key the detector builds from a parsed version, `support-phase` is only compared, `releases.json` is never read, nginx channels come from parsed versions | `DotNetReleaseMetadataService.cs:187-195,221,240`; `DerivedBaseRuntimeDetector.cs:158`; `RuntimeContainerScanOrchestrator.cs:384,391`; `NginxReleaseMetadataService.cs:150-153`; `grep ReleasesJsonUrl` (assignment only) | confirmed |
| 12 | Portainer already cleans the one remote text it passes on (environment names, `?`, documented) and only compares container names; its logs name the requested container | `PortainerClient.cs:123-155,226-230,481-517`; `docs/areas/container-actions.md:37-39` | confirmed |
| 13 | Inspect environment and history text reach no sink today (`Evidence` is set, never read) | `DerivedBaseRuntimeDetector.cs:307`; `grep -rn "\.Evidence" src` | confirmed |
| 14 | A JSON escape such as `\\u001b` or `\\u202e` in a string arrives decoded in `GetString()`; an escaped control character in a test's JSON input reaches the client | RFC 8259 section 7; precedent `DockerHubClientTests.cs:1088-1094` (escaped line feed in a tag name) | confirmed |
| 15 | Two label names that differ only in case make `ToDictionary(…, StringComparer.OrdinalIgnoreCase)` throw `ArgumentException` (related defect, not fixed here) | `OciRegistryClient.cs:599-603`, `DockerHubClient.cs:856-861`; documented behavior of `Enumerable.ToDictionary` | confirmed |
| 16 | Record 0009 is unreleased | `git log --diff-filter=A` → `0458e3c`; `git tag --contains 0458e3c` empty | confirmed |

## Acceptance criteria

"Cleaned" means: every character the helper replaces today is replaced by one space (a lone surrogate by U+FFFD),
without a cut; a field without such a character is unchanged.

- [ ] AC1: `ResponseBodyExcerpt.ReplaceControlCharacters(null)` returns `null`; for non-null text the behavior is unchanged.
- [ ] AC2: Docker Engine container listing and resource samples: the container ID, the name, the image reference, the
  local image ID and the labels `com.docker.compose.project`, `com.docker.stack.namespace` and
  `com.docker.swarm.service.name` of a discovered container, and the ID and name of a resource sample, are cleaned.
- [ ] AC3: Docker Engine image inspect and history: the ID, repo tags, repo digests (and so the container's resolved
  digest), environment variables, root file-system layers, operating system and architecture of an inspect, and the
  created-by command, comment and tags of each history entry, are cleaned.
- [ ] AC4: Registry image configuration (OCI and Docker Hub clients): environment variables, label **values**,
  operating system and architecture are cleaned; label names stay as received.
- [ ] AC5: Base-image resolution (OCI and Docker Hub clients): the values of `org.opencontainers.image.base.name` and
  `org.opencontainers.image.base.digest` are cleaned before they are parsed, so registry, repository, tag and digest of
  the returned base image carry no such character.
- [ ] AC6: Docker Hub: the repository name built from `namespace` and `name` and the description of each listed
  repository, the description of a single repository, and the current user's name — in the result and in the log
  entry EventId 3204 — are cleaned.
- [ ] AC7: Vulnerability providers: Trivy's advisory id, title (also when it falls back to the id), package, installed
  and fixed version, package class and type, summary and reference URL, and Docker Scout's advisory id, title, summary
  and reference URL, are cleaned; a Trivy class or type that is only such characters becomes none.

## Tests per criterion

Inputs are JSON whose strings carry the control characters as JSON escapes in a raw string literal (pattern of
`DockerHubClientTests.cs:1088-1094`): each test uses at least one Cc (`\\u000A` or `\\u001B`) and one Cf (`\\u202E`
or `\\u200B`) character, asserts that its input contains the backslash escape (`Assert.Contains("\\u202E", json, …)`),
and expects the decoded value with a space in each place. (In this plan an escape is written with a doubled
backslash; the JSON text itself holds one backslash, as a raw string literal shows it.) AC1 builds nothing.

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `ResponseBodyExcerptReplaceControlCharactersWhenTextIsNullReturnsNull` | `src/Tests/DockerUpdateGuard.Tests/ResponseBodyExcerptTests.cs` |
| AC2 | `DockerInstanceClientDiscoverContainersAsyncWhenFieldsHaveControlCharactersReplacesThemAsync`; `DockerInstanceClientCollectContainerResourceUsageAsyncWhenFieldsHaveControlCharactersReplacesThemAsync` | `src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientTests.cs` |
| AC3 | `DockerInstanceClientInspectImageAsyncWhenFieldsHaveControlCharactersReplacesThemAsync`; `DockerInstanceClientGetImageHistoryAsyncWhenFieldsHaveControlCharactersReplacesThemAsync` | `src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientTests.cs` |
| AC4 | `OciRegistryClientGetImageConfigurationAsyncWhenFieldsHaveControlCharactersReplacesThemAsync`; `DockerHubClientGetImageConfigurationAsyncWhenFieldsHaveControlCharactersReplacesThemAsync` | `src/Tests/DockerUpdateGuard.Tests/OciRegistryClientTests.cs`; `src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs` |
| AC5 | `OciRegistryClientResolveBaseImagesAsyncWhenBaseImageLabelsHaveControlCharactersReplacesThemAsync`; `DockerHubClientResolveBaseImagesAsyncWhenBaseImageLabelsHaveControlCharactersReplacesThemAsync` | `src/Tests/DockerUpdateGuard.Tests/OciRegistryClientTests.cs`; `src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs` |
| AC6 | `DockerHubClientGetRepositoriesAsyncWhenFieldsHaveControlCharactersReplacesThemAsync`; `DockerHubClientGetRepositoryAsyncWhenDescriptionHasControlCharactersReplacesThemAsync`; `DockerHubClientGetCurrentUserAsyncWhenUserNameHasControlCharactersReplacesThemInResultAndLogAsync` | `src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs` |
| AC7 | `TrivyVulnerabilityProviderGetVulnerabilitiesAsyncWhenAdvisoryFieldsHaveControlCharactersReplacesThemAsync`; `DockerScoutVulnerabilityProviderGetVulnerabilitiesAsyncWhenAdvisoryFieldsHaveControlCharactersReplacesThemAsync` | `src/Tests/DockerUpdateGuard.Tests/TrivyVulnerabilityProviderTests.cs`; `src/Tests/DockerUpdateGuard.Tests/DockerScoutVulnerabilityProviderTests.cs` |

Notes for the Tester: a cleaned container ID or local image ID is what the client then requests, so in AC2 and AC3 the
fake handler answers the stats or inspect URL of the **cleaned** value (`Uri.EscapeDataString`, a space as `%20`), or
`404` where the test does not need the follow-up. AC5 tests set the cleaned character inside the repository and the
tag of the base name and inside the digest label (with a base name without `@`), and stop the chain with a base image
whose configuration has no label. The AC7 Trivy test includes one vulnerability without `Title` (fallback) and one
result block whose `Class` is only `\\u001B`.

## Approach

1. **Helper.** Change `ReplaceControlCharacters` to accept and return `null` (signature below); the body adds one
   `if (text is null) return null;` guard, nothing else.
2. **Docker Engine client** — clean inside the read helpers, so every field of AC2/AC3 is covered:
   `TryGetString` and `TryGetLabel` return `ResponseBodyExcerpt.ReplaceControlCharacters(property.GetString())`;
   `ReadStringArray`, the `Names` projection of `GetPrimaryName` and the `Env` and `RootFS.Layers` projections of
   `ParseImageInspect` map each item through it **before** their `IsNullOrWhiteSpace` filter (a value of only such
   characters is then dropped like a blank one) and before `Trim('/')`. Every `TryGetString` use in this client reads
   data (state, timestamps and IDs included), so no exemption is needed there.
3. **Registry clients** — clean at the data sites only, never inside their `TryGetString` (fact 10):
   - `OciRegistryClient`: `TryReadEnvironmentVariables` items (before the filter), `TryReadLabels` values (not
     names), `ParseImageConfiguration` `os`/`architecture`, and `baseImageName` / `baseImageDigest` right after they
     are read in `ExtractBaseImageFromConfigAsync` (before `IsNullOrWhiteSpace` and `Parse`).
   - `DockerHubClient`: the same four in `ExtractImageConfigurationAsync` and `ExtractBaseImageFromConfigAsync`;
     `namespaceName`, `repositoryName` and the description in `ParseRepository` (before `Trim`/`ToLowerInvariant`);
     the description in `GetRepositoryAsync`; `userName` in `GetCurrentUserAsync` before the blank check and the log.
   - `RegistryBaseImageResolver` reads the already cleaned label values: no change.
4. **Vulnerability providers** — wrap each string assignment of `MapAdvisories` (Trivy: the nine of AC7, the title
   after its fallback, class/type inside `NormalizeBlockValue` before the blank check; Scout: the four of AC7).
5. **Not changed, with the reason in the area document:** tag names and digests (grammar, #348), tokens and next-page
   links (consumed by the client; the guard of record 0004 keeps its input), manifest-list platforms (selection only),
   Portainer (own rule, fact 12), release feeds (fact 11), values stored before this change (record 0009).

No field is exempted on the strength of the Docker Engine's own name or reference grammar: the clients do not check
it. No new user-visible output; texts change only where such a character was present.

## Options considered

The full list is in record 0009 (options 22–28). In short: the same replacement where each client reads a field
(chosen); in the data types' setters (hides the rule, rewrites values the application sets); only fields whose
source can carry such characters (relies on grammars the clients do not check); every string including tokens and
next-page links (changes the guard's input and a credential); validate and drop (only some fields have a grammar;
#355); keep line breaks in advisory summaries (a second rule for one tooltip); clean label names too (collisions,
names are only looked up). A central cleaning step in `VulnerabilityEnrichmentService` (where #271 plans its
truncation) lost because it covers only the advisories and not what a provider itself would log.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Infrastructure/ResponseBodyExcerpt.cs` | nullable signature of `ReplaceControlCharacters` |
| DockerUpdateGuard | `Docker/DockerInstanceClient.cs` | read helpers and inspect projections clean (Approach 2) |
| DockerUpdateGuard | `Images/OciRegistryClient.cs` | image configuration and base-image labels clean (Approach 3) |
| DockerUpdateGuard | `DockerHub/DockerHubClient.cs` | image configuration, base-image labels, repositories, user name clean (Approach 3) |
| DockerUpdateGuard | `Vulnerabilities/TrivyVulnerabilityProvider.cs` | advisory fields clean (Approach 4) |
| DockerUpdateGuard | `Vulnerabilities/DockerScoutVulnerabilityProvider.cs` | advisory fields clean (Approach 4) |

## Signatures (for the Dev's skeleton)

```csharp
// ResponseBodyExcerpt (changed; add `using System.Diagnostics.CodeAnalysis;`)
[return: NotNullIfNotNull(nameof(text))]
public static string? ReplaceControlCharacters(string? text)
```

XML doc: `<param name="text">Text to clean, or null</param>`, `<returns>Null for null, the text itself when nothing is
replaced, otherwise the cleaned copy</returns>`. Skeleton: change the signature and keep the existing body (it
dereferences `text`, so the AC1 test fails on it; a throwing stub would break the passing `Truncate` tests). No other
new or changed member; the skeleton rewrites no other file.

## Test files

`ResponseBodyExcerptTests.cs`, `DockerInstanceClientTests.cs`, `OciRegistryClientTests.cs`, `DockerHubClientTests.cs`,
`TrivyVulnerabilityProviderTests.cs`, `DockerScoutVulnerabilityProviderTests.cs` (all existing, under
`src/Tests/DockerUpdateGuard.Tests/`).

Existing test code that calls a changed signature: the `ReplaceControlCharacters` calls in `ResponseBodyExcerptTests.cs`
pass non-null strings and compile unchanged through `NotNullIfNotNull`; no adaptation. No existing test of the
affected clients has a control character in a field this plan cleans (`grep` for `ConvertFromUtf32` and `\\u00` in the
five client test files: only error bodies, Trivy's error line, Scout's severity and an invalid tag name, none of which
changes), so they stay unchanged.

Files no unit test can cover: none.

## Areas

- [Registries](../../docs/areas/registries.md): new subsection *Remote text fields* (the rule, the cleaned fields per
  source, the exemptions); the failure-model sentence "remote text a message carries outside an excerpt … is not
  cleaned" changes to point there.
- [Docker instances](../../docs/areas/docker-instances.md), [Vulnerabilities](../../docs/areas/vulnerabilities.md),
  [Base images](../../docs/areas/base-images.md), [Telemetry](../../docs/areas/telemetry.md): one sentence each, linking it.

## Documentation updates

All by the Dev, against the code as built:

- `docs/areas/registries.md`: under *Behavior*, after *Failure model of integrations*, a subsection headed exactly
  `### Remote text fields` (record 0009 links `#remote-text-fields`): the same characters as the excerpt rule are
  replaced by one space where the client reads the field, without a cut; the fields of AC2–AC7 grouped by Docker
  Engine, registries and vulnerability providers; the exemptions of Approach 5, each with its link (*What can be
  read*, *Pagination links*, Container actions); values stored earlier stay until rewritten. In the failure-model
  bullet (line 36-37) replace "remote text a message carries outside an excerpt, such as an image reference or a
  container name, is not cleaned" with a pointer to *Remote text fields*; "This is the one place that states the rule"
  becomes true of the two subsections together.
- `docs/areas/docker-instances.md`: *Container discovery* (line 86-89) and *Image inspection and history* (line 101-103):
  the text values arrive with control characters replaced, link to Registries.
- `docs/areas/vulnerabilities.md`: the Trivy mapping bullet (line 110-115) and the Docker Scout mapping bullet (line 140):
  the text values are cleaned (Trivy: a class or type of only such characters is none), link.
- `docs/areas/base-images.md` (line 31-33): the two label values are cleaned before parsing, link.
- `docs/areas/telemetry.md`, *Credentials and external data* (line 105-108): image references, container and repository
  names and the Docker Hub account name taken from remote answers carry control characters replaced, link.
- `docs/ARCHITECTURE.md`, *Failure messages carry fixed reasons* (line 425-428): add that the text fields the clients
  read from remote answers get the same replacement where they are read, linking record 0009.

Sweep done: area documents and `ARCHITECTURE.md` searched for "not cleaned", "control character", the label names and
the field enumerations; the hits are the lines above. `README.md`: no change (no configuration).

## Architecture check

- Integration clients never throw for expected failures: the null-tolerant replacement throws nothing; no outcome changes.
- Update detection and tag comparison stay pure: untouched. `IImageCatalogRepository` remains the catalog entry point:
  untouched; it receives cleaned values.
- Layering: all edits in `src/DockerUpdateGuard`; no new dependency, no entity or migration change.
- `ARCHITECTURE.md` security-posture bullet updated (Documentation updates), linking record 0009.

## Security considerations

- [x] No limits: the replacement never lengthens a text (a supplementary code point becomes one space), so the bounds
  of #324/#348 and the column limits #271/#355 address are unaffected; no new cut.
- [x] Exceptions reaching a user-visible reason: none new.
- The replacement runs on the decoded string (after JSON unescaping), so an escaped form cannot bypass it (fact 14);
  the raw JSON forms a parser accepts for a string — a literal character, `\\n`-style short escapes, `\\uXXXX`, a
  surrogate pair `\\uD83D\\uDE00` — all decode before the client sees the value.
- The pagination guard's input and the bearer tokens are deliberately not cleaned (fact 10, record 0009 option 25).

## Decision records

- `docs/decisions/0009-integration-results-carry-fixed-failure-reasons.md` (Accepted, unreleased, fact 16): extended in
  place — context paragraph, options 22–28, one decision sentence, consequences for #353. Index row unchanged (same title).

## Out of scope / follow-ups

- Proposed follow-up issue, title "[BUG] Image configuration labels whose names differ only in case fail the read":
  body — `OciRegistryClient.TryReadLabels` (`OciRegistryClient.cs:599-603`) and `DockerHubClient.ExtractImageConfigurationAsync`
  (`DockerHubClient.cs:856-861`) build the label dictionary with `ToDictionary(…, StringComparer.OrdinalIgnoreCase)`,
  which throws `ArgumentException` when an image configuration has two labels that differ only in case; decide which
  one wins and test it. Security area: Registry and Docker Hub calls.
- #355 (lengths and grammars of remote values): unchanged; a U+0000 is now replaced in every cleaned field, which the
  orchestrator may note on #355.
- #271 (advisory value truncation): unchanged and independent; the replacement does not change lengths it bounds.
- Portainer environment names keep their `?` rule; user-entered values (registration) are not remote text.
