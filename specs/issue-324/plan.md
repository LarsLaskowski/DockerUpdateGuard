# Plan: Bound the response body excerpt in failure messages

Source: Issue #324
Status: Draft
Tier: standard — a size bound on remote input that keeps an oversized body from breaking the scan's save is input handling, not a security control; it changes what failure messages contain.
Security areas touched: Docker Engine access (`DockerInstanceClient`); Registry and Docker Hub calls (`DockerHubClient`, `OciRegistryClient`); Logging and telemetry of external data (the bounded text reaches log entries and scan-run errors)

## Problem / root cause

Nine non-success branches read the whole response body with `HttpContent.ReadAsStringAsync` and interpolate it into
the `Failed` message without any bound:

- `src/DockerUpdateGuard/Docker/DockerInstanceClient.cs`: lines 873/876 (image inspect), 926/929 (image history),
  995/1000 (container discovery), 1069/1072 (resource sampling — not named in the issue) and 1235/1238 (host info);
- `src/DockerUpdateGuard/Images/OciRegistryClient.cs` 681/683 (`CreateFailureResultAsync`, also appends the
  unbounded `ReasonPhrase`);
- `src/DockerUpdateGuard/DockerHub/DockerHubClient.cs` 230/233 (`CreateFailureResultAsync`);
- `src/DockerUpdateGuard/Images/DotNetReleaseMetadataService.cs` 155/158 and
  `src/DockerUpdateGuard/Images/NginxReleaseMetadataService.cs` 124/127.

A failed registry tag lookup reaches `RuntimeContainerScanOrchestrator.ApplyUnevaluatedTagResult`
(`src/DockerUpdateGuard/Images/RuntimeContainerScanOrchestrator.cs` 767/770), which stores the message in
`ContainerSnapshot.UpdateAssessmentMessage`, a `character varying(2000)` column; a body longer than that makes
PostgreSQL reject the row and the scan's save fails. The same text goes unbounded into the log entry at line 772 and
into scan-run errors.

Related defect found on the way: the OCI client also appends the remote `ReasonPhrase`, which is just as
remote-controlled; it is bounded by the same fix.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | `DockerInstanceClient.cs:875` appends the body | The read is at 873, the message at 876 (image inspect); the issue's line is off by one | confirmed |
| 2 | `DockerInstanceClient.cs:919` appends the body | 919 is the 404 branch; the body is read at 926 and appended at 929 (image history); the issue's line is off by ten | confirmed |
| 3 | `DockerInstanceClient.cs:1000` and `:1238` append the body | Discovery (995/1000) and host info (1235/1238) | confirmed |
| 4 | The issue's list of sites is complete | `CollectContainerResourceUsageAsync` appends the body too (1069/1072); grep of `ReadAsStringAsync` in `src/DockerUpdateGuard` finds no further failure site (Nginx 130 is the success path); one more site | refuted |
| 5 | `OciRegistryClient.cs:683`, `DockerHubClient.cs:230`, `DotNetReleaseMetadataService.cs:158`, `NginxReleaseMetadataService.cs:127` append the body | Read in each file; DockerHub composes at 233 | confirmed |
| 6 | `RuntimeContainerScanOrchestrator.cs:765-768` stores such a message in `UpdateAssessmentMessage` | File is under `Images/`; lines 767 and 770 assign `tagsResult.Message`, reached from `GetTagsAsync` at 610 | confirmed |
| 7 | `UpdateAssessmentMessage` is limited to 2000 characters | `ContainerSnapshotConfiguration.cs` 47-48 `HasMaxLength(2000)`; `Migrations/Update1.cs` 38-41 `character varying(2000)` | confirmed |
| 8 | A longer value makes the save fail | PostgreSQL documentation, *Character Types*: storing a longer string into `character varying(n)` is an error unless the excess is all spaces; Npgsql does not truncate | confirmed |
| 9 | The only client messages that reach the snapshot are tag lookup results | `ProcessContainerAsync` 560-640: only `tagsResult` feeds `ApplyUnevaluatedTagResult`; other failures go to scan-run errors (`text` column, `ScanRunConfiguration.cs` 37) and logs | confirmed |
| 10 | Registry and repository of the lookup are at most 200 and 300 characters | `GetOrCreateImageVersionAsync` (orchestrator 574) stores them first in `RegistryRepositories` (`RegistryRepositoryConfiguration.cs` 24-29: 200, 300), so a longer one fails before the tag lookup | confirmed |
| 11 | Registry clients stream the response; Docker Engine and feed calls are buffered by `HttpClient` | `OciRegistryClient.cs` 1097/1112 and `DockerHubClient.cs` 918 use `ResponseHeadersRead`; the Engine and feed calls use `GetAsync` (default `ResponseContentRead`) | confirmed |
| 12 | A `StreamReader` with a 1024-byte buffer that reads 501 characters reads at most a few KiB of the stream | to verify: AC2's byte-count test (Tester, step 5; green after step 6) | unverified |
| 13 | Existing tests assert no body longer than 500 characters | grep of the client test files for long bodies and `Body:` messages finds none | confirmed |

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
- [ ] AC5: With a registry of 200 characters (OCI only) and a repository of 300 characters, a 10 000-character body
  and (OCI) a 1000-character reason phrase, the tag lookup message of both registry clients is at most 2000
  characters — the limit of the stored assessment message.
- [ ] AC6: The .NET release index and the NGINX download listing (`GetChannelReleaseAsync`) answered with HTTP 400 and a
  10 000-character body report the bounded excerpt as in AC3.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `ResponseBodyExcerptTruncateWhenTextIsNullReturnsEmpty`, `ResponseBodyExcerptTruncateWhenTextFitsReturnsTextUnchanged`, `ResponseBodyExcerptTruncateWhenTextIsLongerReturnsPrefixAndMarker`, `ResponseBodyExcerptTruncateWhenCutSplitsSurrogatePairCutsBeforePair` | `src/Tests/DockerUpdateGuard.Tests/ResponseBodyExcerptTests.cs` |
| AC2 | `ResponseBodyExcerptReadAsyncWhenBodyFitsReturnsWholeBodyAsync`, `ResponseBodyExcerptReadAsyncWhenBodyIsLongerReturnsPrefixAndMarkerAsync`, `ResponseBodyExcerptReadAsyncWhenBodyIsLargeReadsBoundedBytesAsync` | `src/Tests/DockerUpdateGuard.Tests/ResponseBodyExcerptTests.cs` |
| AC3 | `DockerInstanceClientDiscoverContainersAsyncWhenErrorBodyIsLongReturnsBoundedExcerptAsync`, `DockerInstanceClientCollectContainerResourceUsageAsyncWhenErrorBodyIsLongReturnsBoundedExcerptAsync`, `DockerInstanceClientInspectImageAsyncWhenErrorBodyIsLongReturnsBoundedExcerptAsync`, `DockerInstanceClientGetImageHistoryAsyncWhenErrorBodyIsLongReturnsBoundedExcerptAsync`, `DockerInstanceClientGetHostMemoryTotalAsyncWhenErrorBodyIsLongReturnsBoundedExcerptAsync` | `src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientTests.cs` |
| AC4 | `OciRegistryClientGetTagsAsyncWhenErrorBodyIsLongReturnsBoundedExcerptAsync`, `OciRegistryClientGetTagsAsyncWhenReasonPhraseIsLongReturnsBoundedReasonAsync` | `src/Tests/DockerUpdateGuard.Tests/OciRegistryClientTests.cs` |
| AC4 | `DockerHubClientGetTagsAsyncWhenErrorBodyIsLongReturnsBoundedExcerptAsync` | `src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs` |
| AC5 | `OciRegistryClientGetTagsAsyncWhenNamesAreAtStoredLimitsKeepsMessageWithinAssessmentLimitAsync` | `src/Tests/DockerUpdateGuard.Tests/OciRegistryClientTests.cs` |
| AC5 | `DockerHubClientGetTagsAsyncWhenRepositoryIsAtStoredLimitKeepsMessageWithinAssessmentLimitAsync` | `src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs` |
| AC6 | `DotNetReleaseMetadataServiceGetChannelReleaseAsyncWhenErrorBodyIsLongReturnsBoundedExcerptAsync` | `src/Tests/DockerUpdateGuard.Tests/DotNetReleaseMetadataServiceTests.cs` |
| AC6 | `NginxReleaseMetadataServiceGetChannelReleaseAsyncWhenErrorBodyIsLongReturnsBoundedExcerptAsync` | `src/Tests/DockerUpdateGuard.Tests/NginxReleaseMetadataServiceTests.cs` |

Notes for the Tester: use HTTP 400 (not retried by `TransientHttpRetryHandler`) and the existing stub handlers in
`Helper/`; the OCI registry of AC5 is a host of dotted labels of at most 63 characters each. The AC2 byte bound needs a
content whose stream counts the bytes read (extend `Helper/CountingHttpContent.cs` or add a helper). The AC3–AC6
tests fail on the current code (the whole body is in the message); AC1/AC2 compile against the skeleton.

## Approach

1. New `internal static class ResponseBodyExcerpt` in `src/DockerUpdateGuard/Infrastructure/ResponseBodyExcerpt.cs`
   (constants `MaxBodyLength = 500`, `MaxReasonPhraseLength = 100`, `TruncationMarker = "…"`).
   - `Truncate`: as AC1; `ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLength)`.
   - `ReadAsync`: `ReadAsStreamAsync`, then a `StreamReader` (UTF-8, byte-order-mark detection, `bufferSize: 1024`)
     reads up to `MaxBodyLength + 1` characters with `ReadBlockAsync`; the result goes through `Truncate`. The bound
     applies after decoding (characters, a U+FFFD replacement counts as one); the stream is disposed without reading
     the rest. `.ConfigureAwait(false)` throughout.
2. Each of the nine sites replaces `ReadAsStringAsync(token)` by `ResponseBodyExcerpt.ReadAsync(response.Content, token)`
   with the same token (the Engine sites keep `requestTimeoutSource.Token`); the message templates stay as they are.
   `OciRegistryClient.CreateFailureResultAsync` also uses `ResponseBodyExcerpt.Truncate(response.ReasonPhrase,
   ResponseBodyExcerpt.MaxReasonPhraseLength)`; its `IsNullOrWhiteSpace(body)` check stays.
3. No change in the orchestrator: with AC5 the stored message is bounded at its source (fact 10: at most about
   1150 characters for OCI).

Layout: no new user-visible output beyond the marker `…` at the cut, which AC1 fixes.

## Options considered

- **Shared bounded excerpt at every read site, reason phrase included** — chosen: bounds results, logs and scan-run
  errors alike, and stops reading a streamed error body after a few KiB.
- **Cut `UpdateAssessmentMessage` where the orchestrator stores it** — lost: logs and scan-run errors keep the whole
  body, the body is still read in full, and every future stored message needs its own cut.
- **Drop the body from messages** — lost: the body is often the only explanation of a refusal (registry error code,
  rate-limit note); record 0009 keeps the remote answer, now bounded.
- **Both the excerpt and a cut at the store** — lost: fact 10 bounds the other parts of the message, so a second cut
  duplicates the column limit in code without a case that reaches it.
- **Decode with the response's declared charset** — lost: the sources answer UTF-8 JSON or ASCII pages; an unknown
  charset would need its own fallback for a text that is only an excerpt (record 0009, option 15).
- **Per-client private truncation** — lost: nine copies that drift apart (record 0009, option 2).

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Infrastructure/ResponseBodyExcerpt.cs` | new helper |
| DockerUpdateGuard | `Docker/DockerInstanceClient.cs` | five body reads through the helper |
| DockerUpdateGuard | `Images/OciRegistryClient.cs` | `CreateFailureResultAsync`: body and reason phrase through the helper |
| DockerUpdateGuard | `DockerHub/DockerHubClient.cs` | `CreateFailureResultAsync`: body through the helper |
| DockerUpdateGuard | `Images/DotNetReleaseMetadataService.cs`, `Images/NginxReleaseMetadataService.cs` | `LoadIndexAsync`: body through the helper |

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
```

No existing signature changes; the skeleton rewrites no existing file.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/ResponseBodyExcerptTests.cs` (new)
- `src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientTests.cs`, `OciRegistryClientTests.cs`,
  `DockerHubClientTests.cs`, `DotNetReleaseMetadataServiceTests.cs`, `NginxReleaseMetadataServiceTests.cs` (extended)

Existing test code that calls a changed signature: none.

Files of the change no unit test can cover: none.

## Areas

- Registries: the failure model and *Limits and errors* say a failed outcome carries a bounded excerpt of the body
  (first 500 characters, `…` when cut) and of the reason phrase (100 characters).
- Docker instances, Base images: their pointers to "the response body" become the bounded excerpt.
- Telemetry: the list of what logs may carry about external systems gains the bounded body excerpt.

## Documentation updates

Sweep: `grep -n -i "body\|bodies\|excerpt\|truncat"` over `docs/areas/`, `docs/ARCHITECTURE.md` and `README.md`. All by
the Dev, against the code as built:

- `docs/areas/registries.md` line 28 (*Failure model*): "includes the response body the remote side sent" → the first
  500 characters of the body, followed by `…` when it was longer, decoded as UTF-8; the OCI client's reason phrase
  likewise bounded to 100 characters; line 206 (*Limits and errors*): "the remote response body" → the bounded excerpt,
  linking the failure model.
- `docs/areas/docker-instances.md` line 175-176: "the response body" → the bounded excerpt, linking Registries.
- `docs/areas/base-images.md` line 123: "(status code and response body, …)" → "(status code and an excerpt of the
  response body, …)". The feed failures there (line 124) state no message text, so no further edit.
- `docs/areas/telemetry.md` line 104-108 (*Credentials and external data*, the enumeration of what logs and spans may
  carry): add "the bounded excerpt of a remote error response ([Registries](registries.md#failure-model-of-integrations))".
- `docs/ARCHITECTURE.md`: no hit; the guarantee *Failure messages carry fixed reasons* (line 418) is unchanged — none.
- `README.md`: no hit — none.

## Architecture check

No guarantee of `.squad/project.md` changes: the clients still return `ExternalOperationResult<T>` with the same
statuses; only the message text is shorter. Layering holds (the helper lives in the host's `Infrastructure`, next to
`FailureReason`). The *Failure messages carry fixed reasons* guarantee in `docs/ARCHITECTURE.md` is untouched; record
0009 already covers response bodies and is extended.

## Security considerations

- [x] The 500- and 100-character limits apply after decoding (UTF-16 code units of the decoded text; a U+FFFD
  replacement counts as one character); the bytes read are bounded separately by AC2 (8192 bytes).
- [x] Exceptions that can reach a user-visible message: an `IOException` or `HttpRequestException` while reading the
  excerpt, and the timeout's `OperationCanceledException`, are caught by the existing handlers of each site and become
  the fixed reasons `connection failed` / `timed out after <n> seconds` — unchanged from today's `ReadAsStringAsync`.
  `Truncate` throws `ArgumentOutOfRangeException` only for a non-positive constant, never from remote input.
- The excerpt is not sanitized (control characters, newlines stay as the remote side sent them, as today); structured
  logging keeps it a parameter value. Sanitizing would be output escaping, a security control, and is out of scope.

## Decision records

- `docs/decisions/0009-integration-results-carry-fixed-failure-reasons.md` (Accepted, unreleased — not contained in
  `v1.3.7`, the latest tag of `git ls-remote --tags origin`): extended in place with the excerpt decision (context,
  options 12–15, decision sentence, consequence). Index row unchanged.

## Out of scope / follow-ups

- The NGINX success path reads the whole download listing (`NginxReleaseMetadataService.cs` 130); it is a success
  answer, not a failure message.
- Sanitizing control characters in remote text before logging or display.
