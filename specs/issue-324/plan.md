# Plan: Bound remote-derived text in failure messages

Source: Issue #324
Status: Revised (1)
Tier: standard — a size bound on remote input that keeps an oversized body, link or error line from breaking a scan's save is input handling, not a security control; it changes what failure messages contain.
Security areas touched: Docker Engine access (`DockerInstanceClient`); Registry and Docker Hub calls (`DockerHubClient`, `OciRegistryClient`, `DockerScoutVulnerabilityProvider` through `PaginationLinkGuard`); External process execution (`TrivyVulnerabilityProvider`'s error excerpt); Logging and telemetry of external data (the bounded text reaches log entries and scan-run errors)

## Problem / root cause

Remote-controlled text goes into `Failed` messages without a bound:

- Nine non-success branches read the whole response body with `HttpContent.ReadAsStringAsync`:
  `src/DockerUpdateGuard/Docker/DockerInstanceClient.cs` 873/876 (image inspect), 926/929 (image history), 995/1000
  (container discovery), 1069/1072 (resource sampling — not named in the issue), 1235/1238 (host info);
  `src/DockerUpdateGuard/Images/OciRegistryClient.cs` 681/683 (`CreateFailureResultAsync`, also the unbounded
  `ReasonPhrase`); `src/DockerUpdateGuard/DockerHub/DockerHubClient.cs` 230/233;
  `src/DockerUpdateGuard/Images/DotNetReleaseMetadataService.cs` 155/158;
  `src/DockerUpdateGuard/Images/NginxReleaseMetadataService.cs` 124/127.
- `src/DockerUpdateGuard/Infrastructure/PaginationLinkGuard.cs` 54-69: `DescribeOrigin` returns
  `{Scheme}://{IdnHost}[:port]` of a rejected next-page link with no bound; used by `OciRegistryClient.cs` 1312/1316,
  `DockerHubClient.cs` 890/894 and `DockerScoutVulnerabilityProvider.cs` 364/368.
- `src/DockerUpdateGuard/Vulnerabilities/TrivyVulnerabilityProvider.cs` 258-269: `GetErrorExcerpt` returns the last
  line of Trivy's error output unbounded; 343 puts it into the message.

A failed tag lookup reaches `RuntimeContainerScanOrchestrator.ApplyUnevaluatedTagResult`
(`src/DockerUpdateGuard/Images/RuntimeContainerScanOrchestrator.cs` 767/770), which stores it in
`ContainerSnapshot.UpdateAssessmentMessage` (`character varying(2000)`); a failed vulnerability lookup reaches
`VulnerabilityEnrichmentService.cs` 310/318, which stores it in `ImageVersion.VulnerabilityAssessmentMessage`
(`HasMaxLength(2000)`, `ImageVersionConfiguration.cs` 48-49). A longer value makes PostgreSQL reject the row and
the scan's save fails. The same text goes unbounded into log entries (orchestrator 772) and scan-run errors.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | `DockerInstanceClient.cs:875` appends the body | Read at 873, message at 876 (image inspect); the issue's line is off by one | confirmed |
| 2 | `DockerInstanceClient.cs:919` appends the body | 919 is the 404 branch; read at 926, appended at 929 (image history) | confirmed |
| 3 | `DockerInstanceClient.cs:1000` and `:1238` append the body | Discovery (995/1000) and host info (1235/1238) | confirmed |
| 4 | The issue's list of sites is complete | Resource sampling appends the body too (1069/1072); the rejected-link origin and Trivy's error line are further unbounded remote text (Problem); grep of `ReadAsStringAsync` finds no further failure site (Nginx 130 is the success path) | refuted |
| 5 | `OciRegistryClient.cs:683`, `DockerHubClient.cs:230`, `DotNetReleaseMetadataService.cs:158`, `NginxReleaseMetadataService.cs:127` append the body | Read in each file; Docker Hub composes at 233 | confirmed |
| 6 | `RuntimeContainerScanOrchestrator.cs:765-768` stores such a message in `UpdateAssessmentMessage` | Lines 767/770 assign `tagsResult.Message`, from `GetTagsAsync` at 610 | confirmed |
| 7 | `UpdateAssessmentMessage` is limited to 2000 characters | `ContainerSnapshotConfiguration.cs` 47-48; `Migrations/Update1.cs` 38-41 `character varying(2000)` | confirmed |
| 8 | A longer value makes the save fail | PostgreSQL documentation, *Character Types*: a longer string into `character varying(n)` is an error unless the excess is spaces; Npgsql does not truncate | confirmed |
| 9 | Client messages reach two 2000-character columns: tag lookups → `ContainerSnapshot.UpdateAssessmentMessage`; vulnerability provider results → `ImageVersion.VulnerabilityAssessmentMessage` | Orchestrator 560-640, 669-675 (fixed reason + container name, 200), 787; `VulnerabilityEnrichmentService.cs` 310/318; `ScanRun.ErrorMessage` is `text` (`ScanRunConfiguration.cs` 37); `UpdateFinding.Details` (2000) and orchestrator 187 carry evaluation texts, no client failure message | confirmed |
| 10 | Registry, repository, tag and digest are at most 200, 300, 200 and 255 characters | Stored first (`RegistryRepositoryConfiguration.cs` 24-29; `ImageVersionConfiguration.cs` Tag 200, Digest 255), so a longer one fails before any lookup | confirmed |
| 11 | Registry clients stream the response; Docker Engine and feed calls are buffered by `HttpClient` | `OciRegistryClient.cs` 1097/1112 and `DockerHubClient.cs` 918 use `ResponseHeadersRead`; Engine and feed calls use `GetAsync` (`ResponseContentRead`) | confirmed |
| 12 | A `StreamReader` with a 1024-byte buffer that reads 501 characters reads at most 2048 bytes of the stream | Measured by Security in plan review round 1 | confirmed |
| 13 | Existing tests assert no body longer than 500 characters, no origin longer than 300, no Trivy line longer than 500 | grep of the client, guard and Trivy test files | confirmed |
| 14 | `DescribeOrigin` is unbounded | Security measured a host of 60 labels of 63 characters: a 3847-character origin; the bound covers the whole description, scheme and port included | confirmed |
| 15 | Every message of both `GetTagsAsync` is bounded by the fix | OCI 1251 (registry 200), 1278 (repository 300), 1283 (`CreateFailureResultAsync`), 1316 (rejection); Docker Hub 1323, 1341, 1350, 1383; `RegistryMetadataService.cs` 93 (registry); exceptions propagate to the orchestrator's `FailureReason` (669) | confirmed |
| 16 | On a failed token exchange the inner message (token request URI from the remote realm + inner excerpt) becomes the content of a synthetic 401 response, so the outer read bounds it | `OciRegistryClient.cs` 1061/1118-1120/691-697; `DockerHubClient.cs` 866-869; the outer 500 may cut the inner body away | confirmed |
| 17 | Messages within 2000 after the fix: OCI tag lookup at most about 1150, OCI rejection about 890, Docker Hub about 700, Docker Scout rejection about 1360, Trivy about 540 characters | Sum of fact 10, the bounds of this plan and the fixed text | confirmed |

## Acceptance criteria

- [ ] AC1: `ResponseBodyExcerpt.Truncate(text, maxLength)` returns `string.Empty` for `null`, the text unchanged when it
  has at most `maxLength` characters, and otherwise its first `maxLength` characters followed by `…` (U+2026); when the
  character at index `maxLength - 1` is a high surrogate, the cut is made before it, so no pair is split.
- [ ] AC2: `ResponseBodyExcerpt.ReadAsync(content, cancellationToken)` returns a body of at most 500 characters whole,
  and of a longer body its first 500 characters followed by `…`; of a 1 MiB body it reads at most 8192 bytes from the
  content stream.
- [ ] AC3: Each of the five Docker Engine operations (`DiscoverContainersAsync`, `CollectContainerResourceUsageAsync`,
  `InspectImageAsync`, `GetImageHistoryAsync`, `GetHostMemoryTotalAsync`) answered with HTTP 400 and a body of 10 000
  `x` reports *failed* with a message that contains 500 `x` followed by `…` and does not contain 501 `x`; the text
  before the excerpt is unchanged.
- [ ] AC4: The OCI and Docker Hub tag lookups (`GetTagsAsync`) answered with HTTP 400 and a 10 000-character body
  report the bounded excerpt as in AC3; the OCI message bounds a 1000-character reason phrase to 100 characters
  followed by `…`.
- [ ] AC5: With a registry of 200 characters (OCI only) and a repository of 300 characters, the tag lookup message of
  both registry clients is at most 2000 characters — the limit of the stored assessment message — (a) for HTTP 400
  with a 10 000-character body and (OCI) a 1000-character reason phrase, and (b) for a next-page link to another
  origin whose host is 60 labels of 63 characters.
- [ ] AC6: The .NET release index and the NGINX download listing (`GetChannelReleaseAsync`) answered with HTTP 400 and a
  10 000-character body report the bounded excerpt as in AC3.
- [ ] AC7: `PaginationLinkGuard.DescribeOrigin` returns an origin of at most 300 characters whole and of a longer one
  its first 300 characters followed by `…`; the cut is `ResponseBodyExcerpt.Truncate`'s, so it applies to the whole
  description (scheme, host and port).
- [ ] AC8: A Trivy run that exits non-zero with a last error line of 10 000 `x` (base URL without `@`) reports *failed*
  with a message that contains 500 `x` followed by `…` and does not contain 501 `x`; the withheld text for a base URL
  with `@` is unchanged.
- [ ] AC9: A Docker Scout listing whose next-page link points to another origin with a host of 60 labels of 63
  characters reports *failed* with a message of at most 2000 characters that ends in `…)`.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `ResponseBodyExcerptTruncateWhenTextIsNullReturnsEmpty`, `ResponseBodyExcerptTruncateWhenTextFitsReturnsTextUnchanged`, `ResponseBodyExcerptTruncateWhenTextIsLongerReturnsPrefixAndMarker`, `ResponseBodyExcerptTruncateWhenCutSplitsSurrogatePairCutsBeforePair` | `src/Tests/DockerUpdateGuard.Tests/ResponseBodyExcerptTests.cs` |
| AC2 | `ResponseBodyExcerptReadAsyncWhenBodyFitsReturnsWholeBodyAsync`, `ResponseBodyExcerptReadAsyncWhenBodyIsLongerReturnsPrefixAndMarkerAsync`, `ResponseBodyExcerptReadAsyncWhenBodyIsLargeReadsBoundedBytesAsync` | `src/Tests/DockerUpdateGuard.Tests/ResponseBodyExcerptTests.cs` |
| AC3 | `DockerInstanceClientDiscoverContainersAsyncWhenErrorBodyIsLongReturnsBoundedExcerptAsync`, `DockerInstanceClientCollectContainerResourceUsageAsyncWhenErrorBodyIsLongReturnsBoundedExcerptAsync`, `DockerInstanceClientInspectImageAsyncWhenErrorBodyIsLongReturnsBoundedExcerptAsync`, `DockerInstanceClientGetImageHistoryAsyncWhenErrorBodyIsLongReturnsBoundedExcerptAsync`, `DockerInstanceClientGetHostMemoryTotalAsyncWhenErrorBodyIsLongReturnsBoundedExcerptAsync` | `src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientTests.cs` |
| AC4 | `OciRegistryClientGetTagsAsyncWhenErrorBodyIsLongReturnsBoundedExcerptAsync`, `OciRegistryClientGetTagsAsyncWhenReasonPhraseIsLongReturnsBoundedReasonAsync` | `src/Tests/DockerUpdateGuard.Tests/OciRegistryClientTests.cs` |
| AC4 | `DockerHubClientGetTagsAsyncWhenErrorBodyIsLongReturnsBoundedExcerptAsync` | `src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs` |
| AC5 | `OciRegistryClientGetTagsAsyncWhenNamesAreAtStoredLimitsKeepsMessageWithinAssessmentLimitAsync`, `OciRegistryClientGetTagsAsyncWhenForeignLinkHostIsOversizedKeepsMessageWithinAssessmentLimitAsync` | `src/Tests/DockerUpdateGuard.Tests/OciRegistryClientTests.cs` |
| AC5 | `DockerHubClientGetTagsAsyncWhenRepositoryIsAtStoredLimitKeepsMessageWithinAssessmentLimitAsync`, `DockerHubClientGetTagsAsyncWhenForeignLinkHostIsOversizedKeepsMessageWithinAssessmentLimitAsync` | `src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs` |
| AC6 | `DotNetReleaseMetadataServiceGetChannelReleaseAsyncWhenErrorBodyIsLongReturnsBoundedExcerptAsync` | `src/Tests/DockerUpdateGuard.Tests/DotNetReleaseMetadataServiceTests.cs` |
| AC6 | `NginxReleaseMetadataServiceGetChannelReleaseAsyncWhenErrorBodyIsLongReturnsBoundedExcerptAsync` | `src/Tests/DockerUpdateGuard.Tests/NginxReleaseMetadataServiceTests.cs` |
| AC7 | `PaginationLinkGuardDescribeOriginOversizedHostReturnsBoundedOrigin`, `PaginationLinkGuardDescribeOriginOriginAtLimitReturnsOriginUnchanged` | `src/Tests/DockerUpdateGuard.Tests/PaginationLinkGuardTests.cs` |
| AC8 | `TrivyVulnerabilityProviderGetVulnerabilitiesAsyncWhenErrorLineIsLongReturnsBoundedExcerptAsync` | `src/Tests/DockerUpdateGuard.Tests/TrivyVulnerabilityProviderTests.cs` |
| AC9 | `DockerScoutVulnerabilityProviderGetVulnerabilitiesAsyncWhenForeignLinkHostIsOversizedReturnsBoundedOriginAsync` | `src/Tests/DockerUpdateGuard.Tests/DockerScoutVulnerabilityProviderTests.cs` |

Notes for the Tester: use HTTP 400 (not retried by `TransientHttpRetryHandler`) and the existing stub handlers in
`Helper/`; the OCI registry of AC5 is a host of dotted labels of at most 63 characters each. The oversized host of
AC5(b)/AC7/AC9 is 60 labels of 63 `a` joined by `.` under `https://`; build the foreign links as the existing
foreign-link tests do (OCI `Link` header, Docker Hub and Docker Scout JSON `next`). AC7's at-limit case uses an
origin of exactly 300 characters. The AC2 byte bound needs a content whose stream counts the bytes read (extend
`Helper/CountingHttpContent.cs` or add a helper). The Trivy test stubs `IProcessRunner` as the existing failure tests
do. The AC3–AC9 client tests fail on the current code; AC1/AC2 and AC7 compile against the skeleton.

## Approach

1. New `internal static class ResponseBodyExcerpt` in `src/DockerUpdateGuard/Infrastructure/ResponseBodyExcerpt.cs`
   (constants `MaxBodyLength = 500`, `MaxReasonPhraseLength = 100`, `TruncationMarker = "…"`).
   - `Truncate`: as AC1; `ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLength)`.
   - `ReadAsync`: `ReadAsStreamAsync`, then a `StreamReader` (UTF-8, byte-order-mark detection, `bufferSize: 1024`)
     reads up to `MaxBodyLength + 1` characters with `ReadBlockAsync`; the result goes through `Truncate`. The bound
     applies after decoding (a U+FFFD replacement counts as one); the stream is disposed without reading the rest.
     `.ConfigureAwait(false)` throughout.
2. Each of the nine sites replaces `ReadAsStringAsync(token)` by `ResponseBodyExcerpt.ReadAsync(response.Content, token)`
   with the same token (the Engine sites keep `requestTimeoutSource.Token`); the message templates stay.
   `OciRegistryClient.CreateFailureResultAsync` also uses `ResponseBodyExcerpt.Truncate(response.ReasonPhrase,
   ResponseBodyExcerpt.MaxReasonPhraseLength)`; its `IsNullOrWhiteSpace(body)` check stays.
3. `PaginationLinkGuard`: new constant `MaxOriginDescriptionLength = 300` (scheme, a 253-character DNS name and a
   port fit whole); `DescribeOrigin` returns `ResponseBodyExcerpt.Truncate(origin, MaxOriginDescriptionLength)`. The
   placeholder `unparseable link` and `ResolveNextLink` are unchanged; the three callers need no change, and their
   log entries get the bounded text too.
4. `TrivyVulnerabilityProvider.GetErrorExcerpt` returns `ResponseBodyExcerpt.Truncate(lastLine,
   ResponseBodyExcerpt.MaxBodyLength)`; the `no error output` and withheld branches stay. The full error output is
   still read into memory by `ProcessRunner` (out of scope).
5. No change in the orchestrators: facts 9, 10, 15 and 17 enumerate every part of each message that reaches the two
   2000-character columns, each now bounded at its source; the AC5 and AC9 tests check the sum.

Layout: no new user-visible output beyond the marker `…` at the cut, which AC1 fixes.

## Options considered

- **Shared bounded excerpt at every source of remote text (body, reason phrase, rejected origin, Trivy line)** —
  chosen: bounds results, logs and scan-run errors alike, and stops reading a streamed error body after a few KiB.
- **Cut the message where the orchestrators store it** — lost: logs and scan-run errors keep the whole text, the body
  is still read in full, and three stores (two orchestrator branches, the enrichment service) each need a cut.
- **Both the source bound and a cut at the store** — lost: the round-1 review showed the first sweep was incomplete,
  so this plan replaces the claim with an enumeration per column (facts 9, 15, 17) and tests of the sum at the client
  (AC5, AC9); a store-side cut would also hide a future unbounded source in the log, where it should surface.
- **Drop the body from messages** — lost: the body is often the only explanation of a refusal; record 0009 keeps the
  remote answer, now bounded.
- **Decode with the response's declared charset** — lost: record 0009, option 15.
- **Per-client private truncation** — lost: copies that drift apart (record 0009, option 2).

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Infrastructure/ResponseBodyExcerpt.cs` | new helper |
| DockerUpdateGuard | `Infrastructure/PaginationLinkGuard.cs` | new constant; `DescribeOrigin` bounded |
| DockerUpdateGuard | `Docker/DockerInstanceClient.cs` | five body reads through the helper |
| DockerUpdateGuard | `Images/OciRegistryClient.cs` | `CreateFailureResultAsync`: body and reason phrase through the helper |
| DockerUpdateGuard | `DockerHub/DockerHubClient.cs` | `CreateFailureResultAsync`: body through the helper |
| DockerUpdateGuard | `Images/DotNetReleaseMetadataService.cs`, `Images/NginxReleaseMetadataService.cs` | `LoadIndexAsync`: body through the helper |
| DockerUpdateGuard | `Vulnerabilities/TrivyVulnerabilityProvider.cs` | `GetErrorExcerpt`: last line through `Truncate` |

## Signatures (for the Dev's skeleton)

```csharp
namespace DockerUpdateGuard.Infrastructure;

internal static class ResponseBodyExcerpt
{
    public const int MaxBodyLength = 500;
    public const int MaxReasonPhraseLength = 100;
    public const string TruncationMarker = "…";

    public static async Task<string> ReadAsync(HttpContent content, CancellationToken cancellationToken);
    public static string Truncate(string? text, int maxLength);
}

internal static class PaginationLinkGuard
{
    public const int MaxOriginDescriptionLength = 300;   // added; existing members unchanged
}
```

No existing signature changes; the skeleton adds the constant to `Infrastructure/PaginationLinkGuard.cs`.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/ResponseBodyExcerptTests.cs` (new)
- `src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientTests.cs`, `OciRegistryClientTests.cs`,
  `DockerHubClientTests.cs`, `DotNetReleaseMetadataServiceTests.cs`, `NginxReleaseMetadataServiceTests.cs`,
  `PaginationLinkGuardTests.cs`, `TrivyVulnerabilityProviderTests.cs`, `DockerScoutVulnerabilityProviderTests.cs`
  (extended)

Existing test code that calls a changed signature: none. Existing foreign-link, origin and Trivy error-line tests use
short inputs (fact 13) and stay unchanged.

Files of the change no unit test can cover: none.

## Areas

- Registries: failure model, pagination links and *Limits and errors* — bounded excerpt of body (500), reason phrase
  (100) and rejected origin (300).
- Vulnerabilities: Trivy's last error line bounded to 500; the Docker Scout rejected origin as in Registries.
- Docker instances, Base images: their pointers to "the response body" become the bounded excerpt.
- Telemetry: the list of what logs may carry about external systems gains the bounded excerpts.

## Documentation updates

Sweep: `grep -n -i "body\|bodies\|excerpt\|truncat\|origin\|last line"` over `docs/areas/`, `docs/ARCHITECTURE.md` and
`README.md`. All by the Dev, against the code as built; the text says the *message* is bounded everywhere and the
*read* is bounded only where the response is streamed (OCI and Docker Hub clients; Engine and feed responses are
buffered by `HttpClient` before the read, fact 11):

- `docs/areas/registries.md` line 28 (*Failure model*): "includes the response body the remote side sent" → the first
  500 characters of the body, followed by `…` when it was longer, decoded as UTF-8; the OCI reason phrase bounded to
  100 characters; after a failed token exchange the excerpt is of the token failure message, which starts with the
  token request URI, so the token endpoint's own body may be cut away. Line 100 (*Pagination links*): the origin text
  is cut to 300 characters followed by `…`. Line 206 (*Limits and errors*): "the remote response body" → the bounded
  excerpt, linking the failure model.
- `docs/areas/vulnerabilities.md` line 105: the last line of the error output, cut to 500 characters followed by `…`;
  line 133: the origin bounded as in [Registries](registries.md#pagination-links).
- `docs/areas/docker-instances.md` line 175-176: "the response body" → the bounded excerpt, linking Registries.
- `docs/areas/base-images.md` line 123: "(status code and response body, …)" → "(status code and an excerpt of the
  response body, …)".
- `docs/areas/telemetry.md` line 104-108 (*Credentials and external data*): add "the bounded excerpt of a remote
  error response ([Registries](registries.md#failure-model-of-integrations))"; the rejected origin and Trivy's line
  there are bounded as in their areas.
- `docs/ARCHITECTURE.md`: no hit; the guarantee *Failure messages carry fixed reasons* (line 418) is unchanged — none.
- `README.md`: no hit — none.

## Architecture check

No guarantee of `.squad/project.md` changes: the clients return `ExternalOperationResult<T>` with the same statuses;
only message text is shorter. The pagination guard's decision (record 0004) is unchanged; only its description is
bounded. Layering holds (the helper lives in the host's `Infrastructure`). Record 0009 covers remote text in results
and is extended.

## Security considerations

- [x] The 500-, 100- and 300-character limits apply after decoding (UTF-16 code units; a U+FFFD replacement counts as
  one); the bytes read are bounded by AC2 (8192) only for streamed responses (fact 11).
- [x] Exceptions that can reach a user-visible message: an `IOException`, `HttpRequestException` or the timeout's
  `OperationCanceledException` while reading the excerpt are caught by each site's existing handlers and become fixed
  reasons, as with `ReadAsStringAsync` today. `Truncate` throws only for a non-positive constant.
- The excerpt is not sanitized (control characters stay, as today); structured logging keeps it a parameter value.
  Sanitizing is output escaping, a security control, and out of scope (follow-up filed by Security).

## Decision records

- `docs/decisions/0009-integration-results-carry-fixed-failure-reasons.md` (Accepted, unreleased — in no tag of
  `git ls-remote --tags origin`, latest `v1.3.7`): extended in place (context, options 12–13, decision, consequences
  on the read bound and the token chain). Index row unchanged.

## Challenge

- DA 1 (major, Trivy error line into `VulnerabilityAssessmentMessage`): accepted — Approach step 4, AC8, fact 9; the
  sweep of the other 2000-character columns (fact 9) also found the Docker Scout rejection (AC9). `UpdateFinding.Details`
  and orchestrator line 187 carry evaluation texts, not client failure messages (follow-ups).
- DA 2 (minor, byte bound only for streamed responses): accepted — *Documentation updates* preamble, *Security
  considerations*, record 0009 consequences.

## Security review

- Finding 1 (blocking, unbounded rejected origin): option (a) — Approach step 3, AC5(b), AC7, AC9; the false claim
  "the source bound is complete" is replaced by facts 15 and 17 (sweep of every `GetTagsAsync` message of both
  clients) in plan and record 0009 option 13.
- Finding 2 (token chain cuts the inner body): accepted as documentation — fact 16, Registries failure model text.
- Findings 3 and 4: follow-ups filed by Security; noted under *Out of scope*.
- Fact 12: marked confirmed with Security's measurement.

## Out of scope / follow-ups

- The NGINX success path reads the whole download listing (`NginxReleaseMetadataService.cs` 130).
- No time limit while reading a streamed body (Security finding 3, filed by Security).
- Control characters in remote text before logging or display (Security finding 4, filed by Security).
- `ProcessRunner` reads Trivy's whole output into memory.
- Registry tag names in evaluation texts (orchestrator 187, `UpdateFinding.Details`) are not bounded there; a tag
  longer than 200 characters would already fail at `AvailableUpdateVersionTag` (200) — whether tags are length-checked
  before is unverified (proposed follow-up below).
