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
- AC5: Each of the three listings whose next link names a page it already requested (the same link again) sends no
  further request and answers *failed* without data with `<listing> stopped: the next-page link repeated a page already read`
  (listing descriptions as in AC1–AC3), logging one warning with EventId 3215 (Docker Hub) or 2105 (OCI).

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | DockerHubClientGetTagsAsyncEndlessNextLinkReturnsFailedAtPageCapAsync | src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs |
| AC2 | DockerHubClientGetRepositoriesAsyncEndlessNextLinkReturnsFailedAtPageCapAsync | src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs |
| AC3 | OciRegistryClientGetTagsAsyncEndlessLinkHeaderReturnsFailedAtPageCapAsync | src/Tests/DockerUpdateGuard.Tests/OciRegistryClientTests.cs |
| AC4 | DockerHubClientGetTagsAsyncLastPageAtPageCapReturnsSucceededAsync, DockerHubClientGetRepositoriesAsyncLastPageAtPageCapReturnsSucceededAsync, OciRegistryClientGetTagsAsyncLastPageAtPageCapReturnsSucceededAsync | DockerHubClientTests.cs, OciRegistryClientTests.cs (as above) |
| AC5 | DockerHubClientGetTagsAsyncRepeatedNextLinkReturnsFailedAsync, DockerHubClientGetRepositoriesAsyncRepeatedNextLinkReturnsFailedAsync, OciRegistryClientGetTagsAsyncRepeatedLinkHeaderReturnsFailedAsync | DockerHubClientTests.cs, OciRegistryClientTests.cs (as above) |

The endless tests (AC1–AC3) answer page `n` with a link to page `n + 1` (a handler that builds the response from the
request); request counts use `PaginationLinkGuard.MaxListingPages`, never a literal. AC3's fixture lets no tag be
inspected: only version tags that `VersionTagResolutionHelper.TryCompareDisplayVersionTags` compares and that are
lower than `MinimumVersionTag`, and a `CurrentTag` that is on no page; it counts `tags/list` requests only. AC5 links
the first page to itself.

## Approach

- `PaginationLinkGuard` gets the cap and two message builders (*Signatures*):
  `$"{listingDescription} stopped: the listing did not end within {MaxListingPages} pages"` and
  `$"{listingDescription} stopped: the next-page link repeated a page already read"`, with the listing descriptions
  the rejection message already uses in each client.
- Each loop counts pages and keeps the `AbsoluteUri` of every request it sent (ordinal set, first request included).
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

## Security notes

## Security plan review: issue #301, delta confirmation (round 2)

I checked the revision through HEAD (1a21c36, plan revision 7b1a3d6). I read `/home/user/DockerUpdateGuard/specs/issue-301/plan.md` (*Acceptance criteria*, *Approach*, *Signatures*, *Areas*, *Security considerations*, *Security review*) and `/home/user/DockerUpdateGuard/docs/decisions/0024-registry-listings-stop-failed-after-a-page-cap.md` against the loops already read in round 1: `DockerHubClient.cs` 1052–1117 and 1185–1254, and `OciRegistryClient.cs` 1229–1281. I ran no measurements. The new check compares the client's own request URIs with an ordinal string set, and the plan claims no library or measured behavior for it.

### My round-1 findings: both resolved
1. **First *Security considerations* line:** replaced word for word with the line I gave. *Areas* now tells the Dev to claim a bound on pages and requests only, with no size, memory or time bound.
2. **Time bound in 0024:** *Consequences* now says a listing is bounded in requests, not duration (page cap × request timeout), and not in memory. The wording differs from mine, which is fine for an unproposed record.
3. **Logging area:** *Logging and telemetry of external data* is now listed under *Security areas touched*.

### The revision
- **Tier `standard` still holds.** Stopping on a repeated link and capping pages both keep endless input from stalling a listing. Neither decides what an attacker may reach, so both are input handling.
- **Order: origin guard, then repeated link, then cap.** The 0004 origin rule runs first and keeps its rejection message. The repeat check runs on the resolved, same-origin link before any further request is sent, so AC5's "sends no further request" holds. At page 1000 a repeated link gets the repeat message; either way the listing fails with nothing sent. Both stops throw away the entries read, so a truncated account listing cannot disable discovered images.
- **Messages:** `CreateRepeatedLinkMessage` and `CreatePageCapMessage` hold only fixed text and the listing descriptions that `CreateRejectionMessage` already uses. Those descriptions are built from validated repository, account and registry names. Neither the repeated URI nor the cap URI reaches a message or a log.
- **Log calls 3214, 3215, 2104, 2105:** their arguments are `operationName`, `targetName`, `pageCap`, `registry` and `repository`. These match the existing rejection entries, and none is a sensitive name.
- **Request bound:** at most `MaxListingPages` (1000) listing requests, plus the per-page token exchanges and retries that already exist. OCI tag inspections stay bounded by `MaximumTags` in every production caller.
- **Set size:** at most `MaxListingPages` entries, because the set holds only requests actually sent.
- **The repeat check is not the bound, the cap is.** Every way around the repeat check therefore costs at most the cap. 0024 states this for ignored parameters, and the cap tests (AC1–AC3) pin it with distinct links. These URI forms all evade the `AbsoluteUri` comparison while naming the same page:
  - a varying fragment (`#x`)
  - extra or ignored query parameters
  - reordered query parameters
  - percent-encoding variants that `Uri` does not normalize
  - user info in the link (`https://u@host/...`): it passes the origin guard (scheme, IdnHost, port) and shows up in `AbsoluteUri`

  Case of scheme and host, the default port and dot segments are normalized by `Uri`. A trailing-dot host fails the origin guard.

### New findings (both non-blocking)
1. **non-blocking: the fragment is part of the repeat key but is never sent.**
   - **Problem:** `AbsoluteUri` includes `#fragment`, but HTTP never sends it, so `…?page=2#a` and `…?page=2#b` are the same request yet count as different pages. A server can then repeat one page with nothing more than a changing fragment, which costs 1000 requests instead of 2. This is exactly the cost 0024's option 3 rejects.
   - **Smallest fix:** in *Approach*, replace "keeps the `AbsoluteUri` of every request it sent" with:
     `keeps the request identity `uri.GetComponents(UriComponents.HttpRequestUrl, UriFormat.UriEscaped)` (scheme, host, port, path and query, without user info and fragment) of every request it sent`
     Also add one AC5 test case: the same link with a different fragment ends *failed* with the repeat message.
   - **Recommendation:** fix now. It is one expression in the Dev's change. Ignored or reordered query parameters stay out of scope, as 0024 already says.
2. **non-blocking: 0024's memory sentence is now slightly inaccurate.**
   - **Problem:** the request set keeps up to 1000 URIs in addition to the entries. Docker Hub `next` links come from the unbounded body, so the length of each URI is limited only by `Uri` itself.
   - **Smallest fix:** in 0024 *Consequences*, replace
     `Nor is it bounded in memory beyond the entries of the pages read.`
     with
     `Nor is it bounded in memory beyond the entries and the request URIs of the pages read.`
   - **Recommendation:** fix now, alongside finding 1.

No blocking findings.
