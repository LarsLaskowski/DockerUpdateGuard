# Plan: Docker Hub and OCI listings stop at a repeated link or after a page cap

Source: Issue #301
Tier: standard — a behavior change (a bound on the pages a listing reads, with new *failed* outcomes) that creates, changes or removes no security control: it only keeps endless input from stalling a scan (input handling, `.squad/routing.md`, *Tiers*).
Security areas touched: Registry and Docker Scout calls (pagination of `DockerHub/DockerHubClient.cs`, `Images/OciRegistryClient.cs`, `Infrastructure/PaginationLinkGuard.cs`); Failure messages and remote text (new composed failure messages); Logging and telemetry of external data (new warnings)

## Problem / root cause

None of the three listing loops counts pages or remembers the pages requested:
`DockerHubClient.GetTagsCoreAsync` (`DockerHub/DockerHubClient.cs:1059`–1117) and `GetRepositoriesAsync` (1199–1254)
end only on an empty `next`, their own bound or a rejected link; `OciRegistryClient.GetTagsCoreAsync`
(`Images/OciRegistryClient.cs:1229`, `while (true)`) ends only on a missing `Link: rel="next"`, a rejected link or
`MaximumTags` inspected tags (1257), which tags skipped by `ShouldInspectTagName` (1158) never reach.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | Docker Hub tag and repository listings follow same-origin next links without a page limit | `DockerHubClient.cs:1059`–1117, 1199–1254: no counter | confirmed |
| 2 | The OCI tag listing is unbounded in pages | `OciRegistryClient.cs:1229`, 1158, 1257 | confirmed |
| 2a | The OCI tag listing is unbounded in tag inspections | every production caller passes `MaximumTags` (`RuntimeContainerScanOrchestrator.cs:638`, `ImageScanOrchestrator.cs:293`) | refuted |
| 3 | Docker Scout caps at 50 pages with a partial success | `DockerScoutVulnerabilityProvider.cs:26`, 408–413 | confirmed |
| 4 | Every caller handles a *failed* listing without data loss; a partial account listing would disable images | 0024 *Context*; `DockerHubAccountImageDiscoveryService.cs:187`–195, 280–286, 301–311 | confirmed |
| 5 | Real listings exceed 100 pages: ghcr.io/renovatebot/renovate 270 pages, Docker Hub renovate/renovate 470 pages | Devil's Advocate measurement, step 2, recorded in 0024 | confirmed |
| 6 | Event IDs 3214, 3215 (Docker Hub) and 2104, 2105 (OCI) are free | `grep -rn "EventId = "` over `src/DockerUpdateGuard`: 3200–3213, up to 2103 | confirmed |

## Acceptance criteria

`<cap>` is `PaginationLinkGuard.MaxListingPages` (1000); a page counts once its response was read, the first included.

- AC1: The Docker Hub tag listing whose every page names a same-origin `next` link to a page not yet requested sends
  exactly `<cap>` listing requests, then answers *failed* without data with
  `Docker Hub tag listing for '<repository>' stopped: the listing did not end within 1000 pages`, and logs one warning
  with EventId 3214.
- AC2: The Docker Hub repository listing in the same situation sends exactly `<cap>` listing requests and answers
  *failed* without data with `Docker Hub repository listing for '<account>' stopped: the listing did not end within 1000 pages`,
  logging one warning with EventId 3214.
- AC3: The OCI tag listing in the same situation, with tag query options whose filters skip every tag, sends exactly
  `<cap>` `tags/list` requests and answers *failed* without data with
  `Registry tag listing for '<registry>/<repository>' stopped: the listing did not end within 1000 pages`, logging one
  warning with EventId 2104.
- AC4: Each of the three listings whose page `<cap>` has no next link answers *succeeded* with the entries of all
  `<cap>` pages (more than the 100 pages the measured repositories exceed).
- AC5: Each of the three listings whose next link names a page it already requested (the same link again, also with
  only another fragment) sends no
  further request and answers *failed* without data with `<listing> stopped: the next-page link repeated a page already read`
  (listing descriptions as in AC1–AC3), logging one warning with EventId 3215 (Docker Hub) or 2105 (OCI).

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | DockerHubClientGetTagsAsyncEndlessNextLinkReturnsFailedAtPageCapAsync | src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs |
| AC2 | DockerHubClientGetRepositoriesAsyncEndlessNextLinkReturnsFailedAtPageCapAsync | src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs |
| AC3 | OciRegistryClientGetTagsAsyncEndlessLinkHeaderReturnsFailedAtPageCapAsync | src/Tests/DockerUpdateGuard.Tests/OciRegistryClientTests.cs |
| AC4 | DockerHubClientGetTagsAsyncLastPageAtPageCapReturnsSucceededAsync, DockerHubClientGetRepositoriesAsyncLastPageAtPageCapReturnsSucceededAsync, OciRegistryClientGetTagsAsyncLastPageAtPageCapReturnsSucceededAsync | DockerHubClientTests.cs, OciRegistryClientTests.cs (as above) |
| AC5 | DockerHubClientGetTagsAsyncRepeatedNextLinkReturnsFailedAsync, DockerHubClientGetRepositoriesAsyncRepeatedNextLinkReturnsFailedAsync, OciRegistryClientGetTagsAsyncRepeatedLinkHeaderReturnsFailedAsync, DockerHubClientGetTagsAsyncRepeatedNextLinkWithOtherFragmentReturnsFailedAsync | DockerHubClientTests.cs, OciRegistryClientTests.cs (as above) |

The endless tests (AC1–AC3) answer page `n` with a link to page `n + 1` (a handler that builds the response from the
request); request counts use `PaginationLinkGuard.MaxListingPages`, never a literal. AC3's fixture lets no tag be
inspected: only version tags that `VersionTagResolutionHelper.TryCompareDisplayVersionTags` compares and that are
lower than `MinimumVersionTag`, and a `CurrentTag` that is on no page; it counts `tags/list` requests only. AC5 links
the first page to itself; its fragment test links the first page to its own URL with `#other` appended.

## Approach

- `PaginationLinkGuard` gets the cap and two message builders (*Signatures*):
  `$"{listingDescription} stopped: the listing did not end within {MaxListingPages} pages"` and
  `$"{listingDescription} stopped: the next-page link repeated a page already read"`, with the listing descriptions
  the rejection message already uses in each client.
- Each loop counts pages and keeps the request identity `uri.GetComponents(UriComponents.HttpRequestUrl, UriFormat.UriEscaped)`
  (scheme, host, port, path and query, without user info and fragment) of every request it sent (ordinal set, first
  request included).
  After a page: an empty link or the listing's own bound ends it as *succeeded*; otherwise the origin guard runs first
  (a foreign link keeps its rejection message); then a resolved link already in the set ends it *failed* with the
  repeat message; then a count at `MaxListingPages` ends it *failed* with the cap message. Both log their warning,
  discard the entries read and send no further request.
- `DockerHubClient`: private helpers beside `RejectPaginationLink` that log and build the failed result, used by both
  listings. `OciRegistryClient.GetTagsCoreAsync`: the same checks inline after the guard.
- Docker Scout is unchanged. No new user-visible layout: the messages are result messages like the rejection message.

## Options considered

Weighed in [0024](../../docs/decisions/0024-registry-listings-stop-failed-after-a-page-cap.md): repeated-link detection
plus a cap of 1,000 pages (chosen); the cap alone at 100 (breaks fact 5) or at 1,000 (a repeating link costs a
thousand requests per scan); partial *succeeded* as Docker Scout (fact 4, 0004); repeated-link detection alone; a time
budget; a configurable cap; a cap in entries.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Infrastructure/PaginationLinkGuard.cs` | cap constant and message builders |
| DockerUpdateGuard | `DockerHub/DockerHubClient.cs` | page counter and request set in both listings, private helpers |
| DockerUpdateGuard | `DockerHub/DockerHubClientLogging.cs` | warnings 3214, 3215 |
| DockerUpdateGuard | `Images/OciRegistryClient.cs` | page counter and request set in `GetTagsCoreAsync` |
| DockerUpdateGuard | `Images/ImageHostLoggingExtensions.cs` | warnings 2104, 2105 |

## Signatures (for the Dev's skeleton)

In `internal static class PaginationLinkGuard`:

- `public const int MaxListingPages = 1000;`
- `public static string CreatePageCapMessage(string listingDescription)`
- `public static string CreateRepeatedLinkMessage(string listingDescription)`

In `internal static partial class DockerHubClientLogging` (both `Level = LogLevel.Warning`):

- `[LoggerMessage(EventId = 3214, Message = "Docker Hub {OperationName} pagination for {TargetName} stopped at the page cap of {PageCap}")] public static partial void DockerHubPaginationPageCapReached(this ILogger logger, string operationName, string targetName, int pageCap);`
- `[LoggerMessage(EventId = 3215, Message = "Docker Hub {OperationName} pagination for {TargetName} stopped because the next-page link repeated a page already read")] public static partial void DockerHubPaginationLinkRepeated(this ILogger logger, string operationName, string targetName);`

In `internal static partial class ImageHostLoggingExtensions` (both `Level = LogLevel.Warning`):

- `[LoggerMessage(EventId = 2104, Message = "Registry tag listing for {Registry}/{Repository} stopped at the page cap of {PageCap}")] public static partial void OciRegistryPaginationPageCapReached(this ILogger logger, string registry, string repository, int pageCap);`
- `[LoggerMessage(EventId = 2105, Message = "Registry tag listing for {Registry}/{Repository} stopped because the next-page link repeated a page already read")] public static partial void OciRegistryPaginationLinkRepeated(this ILogger logger, string registry, string repository);`

## Test files

- `src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs`
- `src/Tests/DockerUpdateGuard.Tests/OciRegistryClientTests.cs`

Existing test code that calls a changed signature: none; no existing test repeats a link or reads 100 pages.

Files of the change no unit test can cover: none.

## Areas

- `docs/areas/registries.md` — *Pagination links*: the repeated-link stop, the page cap, their order after the origin
  check and their messages; *Docker Hub account discovery*: listings of at most 1,000 pages; *Limits and errors*: both
  among the *failed* cases, and "An unbounded listing (account discovery) reads every page the registry offers"
  replaced by a bound on pages and requests only — no size, memory or time bound is claimed.
- `docs/areas/integration-failures.md` — *Excerpts of remote text*: a listing stopped by a repeated link or the page
  cap has neither status code nor body.

## Documentation updates

- The two area documents above (Dev).
- `docs/ARCHITECTURE.md`, *Pagination stays on its origin*: one sentence that the Docker Hub and OCI listings also stop
  *failed* at a repeated link or after `PaginationLinkGuard.MaxListingPages` pages, linking 0024 (Dev).
- No `README.md` key, no changelog in this repository.

## Architecture check

Integration calls still answer an expected failure with an outcome and a fixed, application-composed message
(`.squad/project.md`, *Guarantees*; 0009). The origin rule of 0004 is unchanged and checked first.

## Security considerations

- [x] Limits: the cap bounds the number of listing requests (pages read), not bytes or memory: a body is bounded only in time by DockerHub:RequestTimeoutSeconds, and the entries of up to MaxListingPages pages are held until the listing ends.
- [x] Exceptions reaching a user-visible message: none new; both stops end the listing with a composed message.

## Decision records

- `docs/decisions/0024-registry-listings-stop-failed-after-a-page-cap.md` (Proposed)

## Challenge

1. **major — a cap of 100 pages breaks real repositories (ghcr.io/renovatebot/renovate 270 pages, Docker Hub
   renovate/renovate 470).** Accepted. The cap is 1,000 pages, more than twice the largest measured listing, and a
   repeated link now stops the listing after one extra page (the issue's "same next link"), so the larger cap costs
   requests only for an endless sequence of distinct links. The measurements and the new options are in 0024; AC4 now
   proves a listing of `<cap>` pages (well over 100) succeeds; AC5 is new; the AC1–AC3 fixtures use distinct links.
2. **minor — AC3's fixture must inspect no tag.** Accepted; the fixture rules are under *Tests per criterion*.

## Security review

Round 1 `APPROVED`, two non-blocking findings:

1. *The first Security considerations line claimed bodies are bounded.* Fixed: replaced verbatim with the line
   Security gave; *Areas* tells the Dev to claim no size, memory or time bound.
2. *Name the time bound in 0024.* Taken: 0024 *Consequences* states that a listing is bounded in requests, not in
   duration (up to the page cap times the request timeout), nor in memory beyond the pages read.

Also added *Logging and telemetry of external data* to *Security areas touched*, as Security's review noted.

Round 2 (delta) `APPROVED`, two non-blocking findings, both applied:

1. *The fragment is part of an `AbsoluteUri` key but never sent, so a changing fragment defeats the repeat check.*
   Fixed: *Approach* keys requests by `GetComponents(UriComponents.HttpRequestUrl, UriFormat.UriEscaped)` (no user
   info, no fragment); AC5 covers a link differing only in its fragment, with its own test. Ignored or reordered query
   parameters stay bounded by the cap only, as 0024 says.
2. *0024's memory sentence omitted the request set.* Fixed verbatim: "Nor is it bounded in memory beyond the entries
   and the request URIs of the pages read."

## Out of scope / follow-ups

Proposed issues for the orchestrator:

- *Docker Scout returns a partial vulnerability list as succeeded at its page cap* — at 50 pages
  `DockerScoutVulnerabilityProvider.FetchVulnerabilitiesAsync` (408–413) returns the advisories read as *succeeded*,
  so a truncated list can resolve findings that still exist, the reason 0004 rejected partial results; decide whether
  it should end *failed* like the registry listings (0024).
- *Account discovery reads every tag of each repository to track the newest one* —
  `DockerHubAccountImageDiscoveryService.SynchronizeRepositoryAsync` calls `GetTagsAsync` without bounds, though it
  tracks only the most recently pushed tag; a bounded query (first page, newest first) would serve it and keep large
  repositories far from the page cap (0024).
