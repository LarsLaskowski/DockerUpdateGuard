# Plan: Registry tag names and digests can exceed stored column lengths on the success path

Source: Issue #348
Status: Draft
Tier: standard — the registry clients get a value check that keeps malformed or oversized remote tags and digests out of the database and one log value gets bounded; input handling, no security control is created, changed or removed.
Security areas touched: Registry and Docker Hub calls (`DockerHubClient`, `OciRegistryClient`, `DockerScoutVulnerabilityProvider`: untrusted JSON and headers); Logging and telemetry of external data (the Docker Scout unknown-severity log entry)

## Problem / root cause

The registry clients pass tag names and digests on without checking them. From there they reach columns with a
length limit, and PostgreSQL rejects any value that is too long, so the save of the whole scan fails:

- `DockerHubClient.ParseTag` (`DockerHubClient.cs:241-249`) copies the record's `name` and `digest` as received. It is
  used by the single lookup (`:1307`) and by the listing (`AppendTagPage`, `:449`).
- `OciRegistryClient.ReadTagNamesAsync` (`OciRegistryClient.cs:720-727`) accepts every non-blank name, and
  `GetTagAsync` (`:1233-1238`) returns the raw `Docker-Content-Digest` header (`:513-517`, `:849`).
- Where the values end up:
  - **Serialized tag data in `ImageVersion.MetadataJson` (4000):** `ImageScanOrchestrator.cs:602` and `:725`, and a
    third write the issue does not list, `DockerHubAccountImageDiscoveryService.cs:194-200`. The save at
    `ImageScanOrchestrator.cs:772` is outside the scan's own `catch`. The exception escapes `ScanAsync`, and the scan
    run stays *running* until cleanup marks it.
  - **Tag names in evaluation texts and tag columns:** `UpdateDetectionService.cs:144`, `:201`, `:681` (summary,
    500), `RuntimeContainerScanOrchestrator.cs:187` (assessment message, 2000), `:883` (details) and `:898`
    (`TagCandidate.Tag`, 200). The recommended tag also goes through `GetOrCreateImageVersionAsync` (`:870-875`) into
    `ImageVersion.Tag` (200).
  - **Digests:** `ImageVersion.Digest` and `TagCandidate.Digest` (255 each).
- The Docker Scout `MapSeverity` (`DockerScoutVulnerabilityProvider.cs:157-160`) logs the unknown severity string as
  received. That is an unbounded log value, not a save failure.

The issue's first bullet (advisory fields) is a duplicate of open issue #271. #271 carries the Product Manager's
decisions (truncation before deduplication, isolating each image version) and fixes it there; this plan leaves it out.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | PostgreSQL rejects a string longer than a `character varying(n)` column (unless the excess is spaces) | PostgreSQL manual, *Character Types*; columns are `character varying(n)` in `DockerUpdateGuardDbContextModelSnapshot.cs:141,308,650,709-741` | confirmed |
| 2 | SQLite and EF Core InMemory do not enforce `HasMaxLength`, so tests must assert value lengths, not save failures | #28, #271 (*Note*); EF Core performs no length validation on save | confirmed |
| 3 | Bullet 1: advisory fields go unbounded into `VulnerabilityFinding` (100/300/200/100/100, also class/type 50) and can fail the refresh | `TrivyVulnerabilityProvider.cs:109-117`, `DockerScoutVulnerabilityProvider.cs:129-130`, `VulnerabilityEnrichmentService.cs:192-205,254-260,464`; open issue #271 covers exactly this, so it is a duplicate here | confirmed |
| 4 | Bullet 2: `MetadataJson` can exceed 4000: Docker Hub `name`/`digest` and the OCI digest header have no bound | `DockerHubClient.cs:245-246`, `OciRegistryClient.cs:1236`, `ImageScanOrchestrator.cs:602,725`, `DockerHubAccountImageDiscoveryService.cs:200` | confirmed |
| 5 | Bullet 3: registry tag names are not length-checked before `RuntimeContainerScanOrchestrator.cs:187` | No length check in `DockerHubClient.cs:245`, `OciRegistryClient.cs:720-727`, `RegistryTagQueryHelper.ShouldKeepTag`; the version regexes have an unbounded `-.+` suffix (`VersionTagResolutionHelper.cs:45,54,63`) | confirmed |
| 6 | Bullet 4: Docker Scout logs the remote severity unbounded | `DockerScoutVulnerabilityProvider.cs:157-160`, `DockerScoutVulnerabilityProviderLogging.cs:111-114` | confirmed |
| 7 | OCI tag grammar: at most 128 characters, `[a-zA-Z0-9_][a-zA-Z0-9._-]{0,127}` | distribution-spec `spec.md` lines 158-160 (main) | confirmed |
| 8 | OCI digest grammar: `algorithm ":" encoded`, components `[a-z0-9]+`, separators `[+._-]`, encoded `[a-zA-Z0-9=_-]+` | image-spec `descriptor.md` lines 81-85 (main) | confirmed |
| 9 | Every `DockerHubTagData` is created in the two clients only | `grep "new DockerHubTagData" src/DockerUpdateGuard`: `DockerHubClient.cs:243`, `OciRegistryClient.cs:756,1233` | confirmed |
| 10 | After the fix `MetadataJson` stays under 4000: System.Text.Json writes at most 6 characters per UTF-16 unit (`\uXXXX`); every tag a client reports is valid (at most 128) and a digest at most 255 characters: 6 × 383 + about 75 ≈ 2373 | the arithmetic; both single lookups report the requested tag (`OciRegistryClient.cs:1235`, AC4), which AC8 bounds; the listing's names are bounded by AC3/AC5 | confirmed |
| 11 | The test fixtures already satisfy both grammars (digests `sha256:<lowercase, digits, ->`, tags such as `1.2.3`, `latest`) | sweep of `Digest = "…"`, `"digest": "…"`, `Tag = "…"` in `src/Tests`; the full suite run in step 6 is the final check | confirmed |
| 12 | `MetadataJson` is never read | only writes and two test assertions (`grep MetadataJson src`) | confirmed |
| 13 | A tag `..` from an OCI listing is escaped by `Uri.EscapeDataString` (unchanged) and then removed as a dot segment, so the manifest request goes to `/v2/<repo>/` on the same origin; the grammar now drops it | `OciRegistryClient.cs:396`; a side effect only | confirmed |
| 14 | The base-image single lookup requests a tag parsed unchecked from the `org.opencontainers.image.base.name` label; its result is serialized into `MetadataJson` (`ImageScanOrchestrator.cs:602`) even when `ResolveStoredBaseImageTagAsync` (`:264-311`) stores a different, valid tag; `failureMessage` takes the lookup's `Message` (`:608`) | `ImageScanOrchestrator.cs:575-608`, `OciRegistryClient.cs:959-972`, `DockerHubClient.cs:764-771`, `OciRegistryClient.cs:1213-1218` → `CreateManifestUri` (`:396`) | confirmed |
| 15 | `CanStopDockerHubScan` (`RegistryTagQueryHelper.cs:21-39`) runs per record in `AppendTagPage` (`DockerHubClient.cs:470`); skipping an invalid record before it means an invalid record older than `PublishedSinceUtc` no longer ends the scan | `DockerHubClient.cs:449-472` | confirmed |

## Acceptance criteria

- [ ] AC1: `RegistryReferenceValidator.IsValidTag` returns true exactly for a string of 1–128 characters that matches
  `[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}` over ASCII only, and false for null, blank and every other string, including
  the cases in *Tests per criterion*.
- [ ] AC2: `RegistryReferenceValidator.ValidDigestOrNull` returns the digest unchanged when it has at most 255
  characters and matches the digest grammar of fact 8 over ASCII only, otherwise null.
- [ ] AC3: the Docker Hub tag listing (`GetTagsAsync`) leaves out a record whose `name` is not a valid tag and keeps
  the other records of the page in order. A record whose `digest` is invalid is kept with `Digest` null.
- [ ] AC4: the Docker Hub single lookup (`GetTagAsync`) returns the requested tag as `Tag`, whatever `name` the record
  carries, and `Digest` null for a digest that is invalid or longer than 255 characters.
- [ ] AC5: the OCI tag listing neither lists nor inspects (no manifest request) a name from `tags/list` that is not a
  valid tag. The valid names of the page are listed as before.
- [ ] AC6: the OCI single lookup returns `Digest` null when the `Docker-Content-Digest` header is invalid or longer than
  255 characters, and keeps a valid header of exactly 255 characters.
- [ ] AC7: an unknown Docker Scout severity is logged as `ResponseBodyExcerpt.Truncate(severity,
  ResponseBodyExcerpt.MaxReasonPhraseLength)`: at most 100 characters plus `…`, control characters replaced. The
  severity still maps to *not set*.
- [ ] AC8: both single lookups (`DockerHubClient.GetTagAsync`, `OciRegistryClient.GetTagAsync`) answer *not found*
  without sending any HTTP request and without logging when the requested tag is not a valid tag (AC1); the result's
  message is the fixed text `Requested tag is not a valid registry tag` and quotes no part of the tag. An unsupported
  registry is still answered *unsupported* first.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `RegistryReferenceValidatorIsValidTagAcceptsGrammarTags` (DataRows: `latest`, `1.2.3`, `v1.0-rc_1`, `_x`, `A`, 128 × `a`); `RegistryReferenceValidatorIsValidTagRejectsInvalidTags` (null, empty, one space, 129 × `a`, `.hidden`, `-x`, `1.2.3` + U+000A (`$` matches before a final line feed), `1.2.3` + U+000D U+000A, `1.2 3`, `1.2.3+build`, `a/b`, `a:b`, `a@b`, `..`, `a` + U+0000, U+FF11 `.2.3` (fullwidth digit, `\d` trap), `é1` (Unicode letter, `\w` trap), U+212A (Kelvin sign, `IgnoreCase` trap)) | `src/Tests/DockerUpdateGuard.Tests/RegistryReferenceValidatorTests.cs` |
| AC2 | `RegistryReferenceValidatorValidDigestOrNullKeepsValidDigests` (`sha256:` + 64 hex, `sha512:` + 128 hex, `sha256:current`, `sha256+b64u:LCa0a2j_xo_5m0U8HTBBNBNCLXBkg7-g-YpeiGJm564`, a valid digest of exactly 255 characters); `RegistryReferenceValidatorValidDigestOrNullRejectsInvalidDigests` (null, empty, `sha256`, `:abc`, `sha256:`, `SHA256:abc`, `sha256:abc` + U+000A, `sha256:ab c`, `sha256:abc/def`, `sha256..x:abc`, a valid-grammar digest of 256 characters, `sha256:` + U+FF41 `bc`) | same |
| AC3 | `DockerHubClientGetTagsAsyncOmitsInvalidTagNamesAsync` (page with `1.0`, a 129-character name, `bad` + U+000A, `2.0` → exactly `1.0`, `2.0`); `DockerHubClientGetTagsAsyncInvalidDigestIsUnknownAsync` (10 000-character digest → tag kept, `Digest` null) | `src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs` |
| AC4 | `DockerHubClientGetTagAsyncOversizedNameAndDigestReturnsRequestedTagWithoutDigestAsync` (record `name` and `digest` of 10 000 characters, request tag `latest`) | same |
| AC5 | `OciRegistryClientGetTagsAsyncSkipsInvalidTagNamesAsync` (`tags/list` with `1.0`, a 129-character name, `..`, `2.0`; assert listed tags and that no manifest request named an invalid name) | `src/Tests/DockerUpdateGuard.Tests/OciRegistryClientTests.cs` |
| AC6 | `OciRegistryClientGetTagAsyncOversizedDigestHeaderReturnsNoDigestAsync` (256-character valid-grammar header → null); `OciRegistryClientGetTagAsyncMaximumLengthDigestHeaderIsKeptAsync` (255 characters → kept) | same |
| AC7 | `DockerScoutVulnerabilityProviderGetVulnerabilitiesAsyncUnknownSeverityIsLoggedBoundedAsync` (severity of 1 000 characters with a U+000A inside the first 100 → the logged value is 101 characters, ends with `…`, contains no U+000A; the finding is *not set*) | `src/Tests/DockerUpdateGuard.Tests/DockerScoutVulnerabilityProviderTests.cs` |
| AC8 | `DockerHubClientGetTagAsyncInvalidRequestedTagReturnsNotFoundWithoutRequestAsync` (DataRows: 129 × `a`, `..`; status *not found*, the handler saw no request, message equals the fixed text) | `src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs` |
| AC8 | `OciRegistryClientGetTagAsyncInvalidRequestedTagReturnsNotFoundWithoutRequestAsync` (same DataRows and assertions; no token or manifest request) | `src/Tests/DockerUpdateGuard.Tests/OciRegistryClientTests.cs` |

For AC3, the skip comes before `CanStopDockerHubScan` (fact 15): an invalid record older than `PublishedSinceUtc` does
not end the scan, paging continues to the next valid record. This is intended; no AC3 test expects the scan to stop
on an invalid record.

Oversized strings are generated in the test, for example `new string('a', n)`. A test whose case is a control
character builds it with `(char)0x0A` or a `\u000A` literal, and asserts that its input contains that character.

## Approach

1. New `Images/Helper/RegistryReferenceValidator` (signatures below):
   - It checks the length first: the tag 1–128, the digest 1–255. Only then does it run a source-generated regex
     (`CultureInvariant`, 100 ms timeout, as `VersionTagResolutionHelper` does). After the length check no input can
     reach the timeout.
   - The patterns use explicit ASCII classes, never `\w` or `\d` (in .NET those match Unicode letters and digits), and
     no `IgnoreCase` (it lets U+212A match `k`).
   - They anchor with `\A` … `\z`, never `$`.
2. `DockerHubClient`:
   - `ParseTag` returns null when `name` is not a valid tag, otherwise `CreateTagData(element, name)`.
   - `CreateTagData` sets `Digest = ValidDigestOrNull(<top-level digest>)` and `PublishedAtUtc` as today.
   - `AppendTagPage` skips a null record before any other check (current-digest tracking, keep, stop).
   - `GetTagAsync` returns `CreateTagData(root, imageReference.Tag)`.
   - `GetTagAsync`, right after the registry support check and before the activity, the request and any log: when
     `IsValidTag(imageReference.Tag)` is false, return `NotFound("Requested tag is not a valid registry tag")` (AC8).
3. `OciRegistryClient`:
   - `ReadTagNamesAsync` adds a name only when `IsValidTag`.
   - `GetTagAsync`: the same guard and message as Docker Hub, after `CanHandle`, before `GetManifestMetadataAsync`.
   - `GetTagAsync` sets `Digest = ValidDigestOrNull(manifestResult.Data.Digest)`.
   - The message text is one shared constant on `RegistryReferenceValidator`.
4. `DockerScoutVulnerabilityProvider.MapSeverity` logs
   `ResponseBodyExcerpt.Truncate(severity, ResponseBodyExcerpt.MaxReasonPhraseLength)`.
5. Skipped names and dropped digests are not logged, so the rejected remote value never reaches a log.

No new user-visible output.

## Options considered

The fix is placed and shaped as in [0014](../../docs/decisions/0014-registry-tag-names-and-digests-are-validated-where-they-are-read.md):
- at the clients, not in the dispatcher (discovery calls the Docker Hub client directly) and not at each store;
- by grammar, not by a length bound alone;
- a tag is dropped, never cut;
- the Docker Hub single lookup reports the requested tag instead of failing.

Two scope choices:
- Advisory fields are left to #271, which already holds the Product Manager's decisions. Fixing them here would split
  that work.
- The other remote values (below) become a follow-up issue rather than part of this change.

The severity log has one option: the shared helper already bounds remote text in logs.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Images/Helper/RegistryReferenceValidator.cs` | new |
| DockerUpdateGuard | `DockerHub/DockerHubClient.cs` | `ParseTag`, new `CreateTagData`, `AppendTagPage`, `GetTagAsync` |
| DockerUpdateGuard | `Images/OciRegistryClient.cs` | `ReadTagNamesAsync`, `GetTagAsync` (guard, digest) |
| DockerUpdateGuard | `Vulnerabilities/DockerScoutVulnerabilityProvider.cs` | `MapSeverity` |

## Signatures (for the Dev's skeleton)

- `internal static partial class RegistryReferenceValidator` (namespace `DockerUpdateGuard.Images.Helper`)
  - `public const int MaxTagLength = 128;`
  - `public const int MaxDigestLength = 255;`
  - `public const string InvalidTagMessage = "Requested tag is not a valid registry tag";`
  - `public static bool IsValidTag([NotNullWhen(true)] string? tag)`
  - `public static string? ValidDigestOrNull(string? digest)`
  - `[GeneratedRegex(@"\A[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}\z", RegexOptions.CultureInvariant, 100)] private static partial Regex TagExpression();`
  - `[GeneratedRegex(@"\A[a-z0-9]+(?:[+._-][a-z0-9]+)*:[a-zA-Z0-9=_-]+\z", RegexOptions.CultureInvariant, 100)] private static partial Regex DigestExpression();`
- `DockerHubClient`: `private static DockerHubTagData? ParseTag(JsonElement element)` (was non-nullable) and new
  `private static DockerHubTagData CreateTagData(JsonElement element, string tag)`.

The skeleton rewrites no file beyond these.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/RegistryReferenceValidatorTests.cs` (new)
- `src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs`
- `src/Tests/DockerUpdateGuard.Tests/OciRegistryClientTests.cs`
- `src/Tests/DockerUpdateGuard.Tests/DockerScoutVulnerabilityProviderTests.cs`

Existing test code that calls a changed signature: none. The changed members are private, and the validator is new.
Existing tests keep their fixtures (fact 11). A test that still fails because of a fixture value outside a grammar is
adapted by the Tester.

Files no unit test can cover: none.

## Areas

- Registries: what a tag lookup and a listing accept (*What can be read*), the untrusted-response bullet, the
  excerpt rule naming the severity log.
- Update detection: tags reach an evaluation only with valid names.
- Vulnerabilities: the Docker Scout unknown-severity log.
- Telemetry: the log enumeration.

## Documentation updates

Dev, against the code as built:

- `docs/areas/registries.md`:
  - *What can be read*, tag lookup: Docker Hub reports the requested tag, and a digest outside the grammar or over 255
    characters is unknown (both clients); a requested tag outside the tag grammar is *not found* without a request.
    Describe the check as what the clients accept, not as a URL or request guard.
  - *Known gaps*: one entry for the follow-up issue
    ([#355](https://github.com/LarsLaskowski/DockerUpdateGuard/issues/355)): the base-image label
    reference is stored unchecked (repository, digest, source reference, the tag where no lookup resolves one),
    repository names of the Docker Hub account listing and Docker Engine values are not checked against their columns.
  - *What can be read*, tag listing: names outside the tag grammar are skipped, by OCI before any inspection.
  - *Failure model of integrations*: add Docker Scout's unknown severity to the list of remote texts that are cut and
    cleaned ("response body, reason phrase, rejected origin and Trivy's error line").
  - *Limits and errors*: in "Registry responses are untrusted", one sentence linking the above.
  - *Related decisions*: add 0014.
- `docs/areas/update-detection.md`, *Input of an evaluation*: registry tags reach an evaluation only with names valid
  under the tag grammar ([Registries](registries.md#what-can-be-read)).
- `docs/areas/vulnerabilities.md`, *Docker Scout* mapping bullet: an unknown severity maps to *not set* and is logged
  cut to 100 characters plus `…`, with control characters replaced.
- `docs/areas/telemetry.md`, line 108 enumeration: add Docker Scout's unknown severity value (bounded, cleaned).
- `docs/ARCHITECTURE.md`, line 426 enumeration "(body excerpts, reason phrase, rejected origin, Trivy's last error
  line)": add Docker Scout's unknown severity.

Sweep, no edit needed:
- `docs/areas/image-catalog.md:42` (metadata at most 4000) stays true.
- `docs/areas/update-detection.md:166` and `docs/areas/vulnerabilities.md:163` (stored lengths) are unchanged. The
  *Known gaps* entry for #271 stays.

## Architecture check

- Clients still answer with `ExternalOperationResult<T>` and add no exception path: the regex input is
  length-checked first.
- Update detection stays pure; only its input is cleaner.
- `IImageCatalogRepository` is untouched.
- Layering: the validator lives in the host's `Images/Helper`.
- No guarantee of `.squad/project.md` changes. `docs/ARCHITECTURE.md` changes only in the enumeration above.

## Security considerations

- [ ] Limits: the tag limit of 128 and the digest limit of 255 count UTF-16 code units of the decoded JSON string or
  header value, after decoding. Both grammars are ASCII-only, so code units equal characters.
- [ ] Exceptions reaching a user-visible message: none new. A dropped tag or digest gives no message.

## Decision records

- `docs/decisions/0014-registry-tag-names-and-digests-are-validated-where-they-are-read.md` (Proposed)

## Out of scope / follow-ups

- Bullet 1 (advisory fields): #271. Proposed comment on #348: the advisory part is fixed with #271.
- Follow-up issue (for the orchestrator):
  - Title: "[BUG] Remote image references and names outside registry tag data can exceed stored column lengths".
  - Body: Found while planning #348, which validates registry tag names and digests (record 0014). Other remote values
    still reach length-limited columns unchecked:
    - the base-image reference of an image's labels (`OciRegistryClient.cs:963-972`, the Docker Hub
      `ExtractBaseImageFromConfigAsync`), parsed without checks. #348 makes the single lookup answer *not found* for
      an invalid requested tag, so its tag no longer reaches `MetadataJson`, but the parsed values are still stored
      unchecked: registry and repository (300), the tag (200) where `ResolveStoredBaseImageTagAsync` keeps the
      requested one, digest (255), `ImageRelationship.SourceReference` (500), and the full reference in the
      base-image refresh log entry (`ImageScanOrchestrator.cs:610-613`);
    - repository names of the Docker Hub account listing (`DockerHubAccountImageDiscoveryService`,
      `ObservedImage.Name` 200);
    - container names and Compose labels from the Docker Engine (`ContainerSnapshot`, 200 each; not traced in
      detail);
    - a U+0000 in any remote string, which PostgreSQL refuses in `text` columns (unverified).
  - Expected: decide per value whether to validate, cut or drop it before it is stored.
  - Security area: Registry and Docker Hub calls, Docker Engine access.
## Challenge

- MAJOR 1 (fact 10 wrong; the base-image label tag reaches `MetadataJson` at `ImageScanOrchestrator.cs:602` and the
  manifest URI): accepted. Fact 10 now rests on a 128-character bound for every reported tag; fact 14 records the
  base-image path; AC8 adds the guard to both single lookups with two tests; 0014 has a Consequences line.
- MINOR 1 (*Known gaps* and the follow-up link): accepted. *Documentation updates* adds the `registries.md` entry;
  0014 links #355; the follow-up body names what AC8 now covers and what stays unchecked.
- MINOR 2 (skip before `CanStopDockerHubScan`): accepted as intended behavior; fact 15 and the note under *Tests per
  criterion* tell the Tester.

## Security review

Security approved the plan; its two notes are taken:
- The area documents describe the check as what the clients accept, not as a URL guard (*Documentation updates*).
- Invalid OCI names skipped by AC5 do not count toward `MaximumTags`: accepted, not a regression (an invalid name
  never was a usable candidate).
- Round 2 (AC8), non-blocking: AC8 promises "without logging", but its two tests check only the status, that no request was sent, and the message. A Tester who has a logger fake available should also assert that no log entry is written.