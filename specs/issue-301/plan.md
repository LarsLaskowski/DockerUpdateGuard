# Plan: Docker Hub and OCI listings stop after a page cap

Source: Issue #301
Tier: standard — a behavior change (a bound on the pages a listing reads, with a new *failed* outcome) that creates, changes or removes no security control: the page cap only keeps endless input from stalling a scan (input handling, `.squad/routing.md`, *Tiers*).
Security areas touched: Registry and Docker Scout calls (pagination of `DockerHub/DockerHubClient.cs`, `Images/OciRegistryClient.cs`, `Infrastructure/PaginationLinkGuard.cs`); Failure messages and remote text (a new composed failure message)

## Problem / root cause

The three listing loops have no page counter:

- `DockerHubClient.GetTagsCoreAsync` (`src/DockerUpdateGuard/DockerHub/DockerHubClient.cs:1059`–1117) loops
  `while (requestUri is not null)` and only ends on an empty `next`, a stop of `AppendTagPage` (kept-tag maximum or
  published-since bound, line 495–536) or a rejected link.
- `DockerHubClient.GetRepositoriesAsync` (same file, 1199–1254) ends only on an empty `next` or a rejected link.
- `OciRegistryClient.GetTagsCoreAsync` (`src/DockerUpdateGuard/Images/OciRegistryClient.cs:1229`–1281) loops
  `while (true)` and ends only on a missing `Link: rel="next"`, a rejected link, or `MaximumTags` inspected tags
  (1257); tags that fail `ShouldInspectTagName` (1158) are not counted, so pages of filtered or no tags never reach it.

A same-origin next link that repeats or never ends therefore keeps the listing reading until the caller cancels.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | Docker Hub tag and repository listings follow same-origin next links without a page limit | `DockerHubClient.cs:1059`–1117, 1199–1254: no counter | confirmed |
| 2 | The OCI tag listing is unbounded in pages | `OciRegistryClient.cs:1229`: `while (true)`; with query options `MaximumTags` bounds only inspected tags (1163, 1257), and pages of filtered or no tags never reach it | confirmed |
| 2a | The OCI tag listing is unbounded in tag inspections | every production caller passes options with `MaximumTags` (`RuntimeContainerScanOrchestrator.cs:638`, `ImageScanOrchestrator.cs:293`), which bounds the inspections | refuted |
| 3 | Docker Scout caps at 50 pages | `DockerScoutVulnerabilityProvider.cs:26` `MaxVulnerabilityPages = 50`, 408–413: it returns the advisories read as *succeeded* and logs EventId 3419 | confirmed |
| 4 | A *failed* tag listing is handled by every caller without data loss | discovery skips the repository and keeps its image (`DockerHubAccountImageDiscoveryService.cs:187`–195); a failed account listing changes nothing (280–286); the runtime scan records an unevaluated assessment (`RuntimeContainerScanOrchestrator.cs:661`); base-image resolution keeps the tag (`ImageScanOrchestrator.cs:299`) | confirmed |
| 5 | A partial account listing would disable discovered images beyond the cut | `DockerHubAccountImageDiscoveryService.cs:301`–311 disables every discovery image not in the listing | confirmed |
| 6 | Page sizes are 100 entries (Docker Hub `page_size=100`, OCI `n=100`) | `DockerHubClient.cs:1055`, 1197; `OciRegistryClient.cs:455` | confirmed |
| 7 | Event IDs 3214 (Docker Hub client) and 2104 (OCI client) are free | `grep -rn "EventId = "` over `src/DockerUpdateGuard`: Docker Hub uses 3200–3213, `ImageHostLoggingExtensions` up to 2103 | confirmed |
| 8 | The tests can reach `PaginationLinkGuard` constants | `AssemblyInfo.cs:3` `InternalsVisibleTo("DockerUpdateGuard.Tests")` | confirmed |

## Acceptance criteria

`<cap>` is `PaginationLinkGuard.MaxListingPages` (100); a page counts once its response was read, the first page
included.

- AC1: The Docker Hub tag listing whose every page names a same-origin `next` link sends exactly `<cap>` listing
  requests, then answers *failed* without data, with the message
  `Docker Hub tag listing for '<repository>' stopped: the listing did not end within 100 pages`, and logs one warning
  with EventId 3214.
- AC2: The Docker Hub repository listing in the same situation sends exactly `<cap>` listing requests, answers *failed*
  without data with `Docker Hub repository listing for '<account>' stopped: the listing did not end within 100 pages`,
  and logs one warning with EventId 3214.
- AC3: The OCI tag listing in the same situation — also with tag query options whose filters skip every tag, so
  `MaximumTags` is never reached — sends exactly `<cap>` `tags/list` requests, answers *failed* without data with
  `Registry tag listing for '<registry>/<repository>' stopped: the listing did not end within 100 pages`, and logs one
  warning with EventId 2104.
- AC4: Each of the three listings whose page `<cap>` has no next link answers *succeeded* with the entries of all
  `<cap>` pages.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | DockerHubClientGetTagsAsyncEndlessNextLinkReturnsFailedAtPageCapAsync | src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs |
| AC2 | DockerHubClientGetRepositoriesAsyncEndlessNextLinkReturnsFailedAtPageCapAsync | src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs |
| AC3 | OciRegistryClientGetTagsAsyncEndlessLinkHeaderReturnsFailedAtPageCapAsync | src/Tests/DockerUpdateGuard.Tests/OciRegistryClientTests.cs |
| AC4 | DockerHubClientGetTagsAsyncLastPageAtPageCapReturnsSucceededAsync, DockerHubClientGetRepositoriesAsyncLastPageAtPageCapReturnsSucceededAsync, OciRegistryClientGetTagsAsyncLastPageAtPageCapReturnsSucceededAsync | DockerHubClientTests.cs, OciRegistryClientTests.cs (as above) |

The endless tests answer every listing request with the same page whose link points to itself (the issue's
"same next link"); AC1 uses no query options, AC3 uses options with `MinimumVersionTag` above every listed tag. Request
counts use `PaginationLinkGuard.MaxListingPages`, never a literal.

## Approach

- `PaginationLinkGuard` gets the shared cap and the message builder (*Signatures*); the message is
  `$"{listingDescription} stopped: the listing did not end within {MaxListingPages} pages"`, with the listing
  descriptions the rejection message already uses in each client.
- Each loop counts the pages read. After a page, the existing order stays: an empty link (or a stop of the
  listing's own bound) ends it as *succeeded*; otherwise the origin guard runs first (a foreign link keeps its
  rejection message); then, when the count has reached `MaxListingPages`, the listing logs the warning and returns
  *failed* with the cap message, discarding the entries read, and sends no further request.
- `DockerHubClient`: one private helper beside `RejectPaginationLink` that logs `DockerHubPaginationPageCapReached`
  and builds the failed result, used by both listings.
- `OciRegistryClient.GetTagsCoreAsync`: the same check inline after the guard, logging
  `OciRegistryPaginationPageCapReached`.
- Docker Scout is unchanged (its own cap and partial success).
- No new user-visible layout: the message is a result message like the rejection message.

## Options considered

1. **Fixed cap, *failed* without the entries** — chosen ([0024](../../docs/decisions/0024-registry-listings-stop-failed-after-a-page-cap.md)).
2. **Cap with partial *succeeded*, as Docker Scout** — lost: a partial account listing disables discovered images
   (fact 5), a partial OCI list is an arbitrary lexical cut, a repeated link fills the result with copies, and 0004
   rejected partial results for the same reason.
3. **Detect a repeated next link** — lost: does not stop an endless sequence of distinct links.
4. **Time budget per listing** — lost: bounds time, not requests.
5. **Configurable cap** — lost: no known operator need; the record names it as the revisit path.
6. **Cap counted in entries** — lost: a server can send empty pages; pages are what bounds the requests.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Infrastructure/PaginationLinkGuard.cs` | cap constant and message builder |
| DockerUpdateGuard | `DockerHub/DockerHubClient.cs` | page counter in both listings, private cap helper |
| DockerUpdateGuard | `DockerHub/DockerHubClientLogging.cs` | warning EventId 3214 |
| DockerUpdateGuard | `Images/OciRegistryClient.cs` | page counter in `GetTagsCoreAsync` |
| DockerUpdateGuard | `Images/ImageHostLoggingExtensions.cs` | warning EventId 2104 |

## Signatures (for the Dev's skeleton)

In `internal static class PaginationLinkGuard` (region *Constants*, *Methods*):

- `public const int MaxListingPages = 100;`
- `public static string CreatePageCapMessage(string listingDescription)`

In `internal static partial class DockerHubClientLogging`:

- `[LoggerMessage(EventId = 3214, Level = LogLevel.Warning, Message = "Docker Hub {OperationName} pagination for {TargetName} stopped at the page cap of {PageCap}")] public static partial void DockerHubPaginationPageCapReached(this ILogger logger, string operationName, string targetName, int pageCap);`

In `internal static partial class ImageHostLoggingExtensions`:

- `[LoggerMessage(EventId = 2104, Level = LogLevel.Warning, Message = "Registry tag listing for {Registry}/{Repository} stopped at the page cap of {PageCap}")] public static partial void OciRegistryPaginationPageCapReached(this ILogger logger, string registry, string repository, int pageCap);`

No existing file the skeleton must rewrite beyond adding these members.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs`
- `src/Tests/DockerUpdateGuard.Tests/OciRegistryClientTests.cs`

Existing test code that calls a changed signature: none. No existing test reads 100 or more pages.

Files of the change no unit test can cover: none.

## Areas

- `docs/areas/registries.md` — *Pagination links*: the page cap, its order after the origin check and its message;
  *Docker Hub account discovery*: listings of at most 100 pages; *Limits and errors*: the cap among the *failed*
  cases, and the sentence "An unbounded listing (account discovery) reads every page the registry offers" replaced by
  the cap.
- `docs/areas/integration-failures.md` — *Excerpts of remote text*: a listing stopped by the page cap has neither
  status code nor body and names the cap.

## Documentation updates

- The two area documents above (Dev).
- `docs/ARCHITECTURE.md`, *Pagination stays on its origin*: one sentence that the Docker Hub and OCI listings also
  stop *failed* after `PaginationLinkGuard.MaxListingPages` pages, linking 0024 (Dev).
- No `README.md` key, no changelog in this repository.

## Architecture check

Integration calls still answer an expected failure with an outcome and a fixed, application-composed message
(`.squad/project.md`, *Guarantees*; 0009): the cap message holds only fixed text and the listing description already
used for rejected links. No other guarantee is touched; the origin rule of 0004 is unchanged and checked first.

## Security considerations

- [x] Limits: the cap counts pages (responses read), independent of their bytes; each body stays bounded by the
  existing readers.
- [x] Exceptions reaching a user-visible message: none new; the cap ends the listing with a composed message.

## Decision records

- `docs/decisions/0024-registry-listings-stop-failed-after-a-page-cap.md` (Proposed)

## Out of scope / follow-ups

Proposed issues for the orchestrator:

- *Docker Scout returns a partial vulnerability list as succeeded at its page cap* — body: at 50 pages
  `DockerScoutVulnerabilityProvider.FetchVulnerabilitiesAsync` (line 408–413) returns the advisories read as
  *succeeded*, so a truncated list can resolve findings that still exist — the reason 0004 rejected partial results;
  decide whether it should end *failed* like the registry listings (0024).
- *Account discovery reads every tag of each repository to track the newest one* — body:
  `DockerHubAccountImageDiscoveryService.SynchronizeRepositoryAsync` calls `GetTagsAsync` without bounds, though it
  tracks only the most recently pushed tag; a repository with more than 10 000 tags now fails at the page cap (0024)
  and is skipped. A bounded query (first page, newest first) would serve it.

## Security notes

## Security plan review: issue #301, round 1

I reviewed `/home/user/DockerUpdateGuard/specs/issue-301/plan.md` and `/home/user/DockerUpdateGuard/docs/decisions/0024-registry-listings-stop-failed-after-a-page-cap.md`. I checked them against the code at HEAD:
- `src/DockerUpdateGuard/DockerHub/DockerHubClient.cs` (lines 1052–1117 and 1185–1254, plus `SendThrottledAsync` at around 870)
- `src/DockerUpdateGuard/Images/OciRegistryClient.cs` (`ReadTagNamesAsync` at 804, `AppendInspectedTagsAsync`, `GetTagsCoreAsync` at 1229–1281)
- `src/DockerUpdateGuard/Infrastructure/PaginationLinkGuard.cs` and `RemoteJsonDocument.cs`
- `docs/areas/registries.md` (*Pagination links*, *Request timeout*, *Limits and errors*) and `docs/areas/integration-failures.md`

I ran no measurements. The plan relies on no library or measured behavior for the property it adds: a page counter in the client's own loop. The request counts in AC1–AC3 pin it directly.

**Tier `standard` is correct.** The cap decides nothing about what an attacker can reach. It only keeps endless input from stalling a listing, which is the "size bound" kind of check that `.squad/routing.md` (*Tiers*, `security` row) classes as input handling. The same-origin guard (0004) does not change and still runs before the cap.

**Security areas are complete.** The plan lists *Registry and Docker Scout calls* and *Failure messages and remote text*. It also adds two log messages (`DockerHubClientLogging.cs` EventId 3214, `ImageHostLoggingExtensions.cs` EventId 2104), which falls under *Logging and telemetry of external data*. Their arguments are `operationName`, `targetName`, `pageCap`, `registry` and `repository`. These are the same values the existing rejection log entries already carry, and none matches a sensitive name. I don't count the missing area as a finding, but the Lead may add it to *Security areas touched* for completeness.

**Checks that pass:**
- **Cap message:** it is fixed text plus the listing descriptions that `CreateRejectionMessage` already uses. Those descriptions hold the repository or account name and the normalized registry, all validated before any request is sent. No remote text reaches the message, so the *Guarantees* rule (0009) still holds.
- **Order in the loop:** empty link or the listing's own stop, then the origin guard, then the cap. A foreign link keeps its rejection message. At the cap no further request is sent, and the entries already read are thrown away rather than returned as a partial success, so no truncated account listing can disable discovered images (fact 5).
- **No way around the cap:** the counter counts pages read, not entries. Empty pages, pages whose tags are all filtered out, and pages with names that fail validation all still count. Each page can add only a bounded number of extra requests: token exchange, the retries of the retry handler, and OCI tag inspections, which `MaximumTags` caps in every production caller (fact 2a). AC3 with the skip-all `MinimumVersionTag` pins exactly the OCI case the issue is about.
- **No other listing missed:** `ResolveNextLink` is called only in the three listings this plan covers and in Docker Scout, which already has its own cap. The plan files Docker Scout's partial success as a follow-up issue, which is the right call.

### Findings

1. **non-blocking: the plan's *Security considerations* claims each body is bounded by the existing readers, and it is not.**
   - **What the code does:** both clients send with `HttpCompletionOption.ResponseHeadersRead` (`DockerHubClient.cs` around 870, `OciRegistryClient.cs` 1094/1108). `RemoteJsonDocument.ParseAsync` then calls `JsonDocument.ParseAsync(stream)` and buffers the whole stream.
   - **What limits a body:** nothing in bytes. The only limit is the per-request time in `DockerHub:RequestTimeoutSeconds` (`registries.md`, *Request timeout*).
   - **Memory:** the list of entries can grow over up to 100 pages before a capped listing discards it.
   - **What this change claims:** a bound on requests, and AC1–AC3 pin that. It neither claims nor needs a byte or memory bound, so this is not a defect of the change. The risk is that the false statement ends up in the area document the Dev writes.
   - **Smallest fix:** replace the first checklist line under *Security considerations* in `plan.md`, verbatim, with:
     `- [x] Limits: the cap bounds the number of listing requests (pages read), not bytes or memory: a body is bounded only in time by DockerHub:RequestTimeoutSeconds, and the entries of up to MaxListingPages pages are held until the listing ends.`
   - In the *Areas* section, the Dev should state the cap as a bound on pages and requests only. In particular, the replacement for the `registries.md` sentence "An unbounded listing (account discovery) reads every page the registry offers" must not claim a size or memory bound.
   - **Recommendation:** fix it now in the Lead's revision. The plan only needs this one edit.

2. **non-blocking: the time bound per listing is worth naming, and no change is needed.** A slow same-origin server can still hold a capped listing for about `MaxListingPages × RequestTimeoutSeconds`, which is up to about 50 minutes at the defaults. Option 4 in 0024 already explains why the plan bounds requests rather than time, so this is consistent. If the Lead wants it on record, add it to 0024's *Consequences*, verbatim:
   `- A listing is bounded in requests, not in duration: a slow server can hold one for up to the page cap times the request timeout.`
   **Recommendation:** optional, fix now or leave out. It does not justify a new issue.

No blocking findings.

## Challenge notes

Devil's Advocate, mode plan: /home/user/DockerUpdateGuard/specs/issue-301/plan.md (issue #301)

I checked the plan's verified facts 1–8 against the code and they hold. Specifically:
- The loops have no page counter: DockerHubClient.cs:1059–1117 and 1199–1254, OciRegistryClient.cs:1229–1281.
- The callers treat any non-succeeded result as "no answer": DockerHubAccountImageDiscoveryService.cs:187–195 and 280–286, RuntimeContainerScanOrchestrator.cs:640–665.
- RegistryMetadataService.cs:81–97 dispatches with no fallback client, so a capped listing cannot spill into a second client.
- Option 2 (keep a partial result as *succeeded*) is rightly rejected, because a truncated account listing disables discovered images (DockerHubAccountImageDiscoveryService.cs:301–311).

That leaves the size of the cap.

## Objections

1. **major — the cap of 100 pages breaks real public repositories today, and nobody measured it.** The plan and docs/decisions/0024-registry-listings-stop-failed-after-a-page-cap.md pick 100 pages (10,000 entries) only because there is "no known operator need". The record's own revisit condition is "a real repository is found that legitimately needs more pages". That condition is already met:
   - The anonymous `tags/list` walk of `ghcr.io/renovatebot/renovate`, with n=100 as in OciRegistryClient.cs:455, followed `Link: rel="next"` for **270 pages (26,988 tags)**.
   - Docker Hub reports `count` **46,986** for `renovate/renovate`, which is 470 pages of 100.

   Where this bites:
   - **OCI tag listing.** It is ordered by name, and its only early stop is `MaximumTags` (250) *inspected* tags (OciRegistryClient.cs:1257). With a pinned version tag, `MinimumVersionTag` filters out the lower versions without inspecting them (RegistryTagQueryHelper.cs:63–70, 131–143). A container on such a repository can reach page 100 before 250 tags qualify, and its update assessment would then fail on every scan where it succeeds today.
   - **Docker Hub tag listing.** It stops early only through `PublishedSinceUtc` (RegistryTagQueryHelper.cs:27–38). When the current tag lookup fails, `PublishedSinceUtc` is null (RuntimeContainerScanOrchestrator.cs:262) and the listing reads every page.
   - **Account discovery.** It reads all tags without bounds (DockerHubAccountImageDiscoveryService.cs:182).

   The plan would turn listings that work today into permanent failures for well-known images, which is a regression the issue did not ask for.

   Fix: size the cap from evidence and record the measured repositories in 0024. One way is a cap well above the largest known listing (for example 1,000 pages = 100,000 entries, which still bounds an endless sequence of distinct links). Another is to combine a larger cap with option 3 (stop failed on a repeated next link), which ends the issue's "same next link" case after one page. Add an acceptance criterion and test showing that a listing longer than 100 pages but shorter than the cap still succeeds, if the cap is above that.

2. **minor — AC3's fixture must not let any tag be inspected, or it exercises `MaximumTags` instead of the cap.** Instruction for the Tester: in the OCI endless-link test, use only version tags that `VersionTagResolutionHelper.TryCompareDisplayVersionTags` can compare and that are lower than `MinimumVersionTag`. Set a `CurrentTag` that does not appear on the page. Tags that cannot be compared and the current tag are always inspected (RegistryTagQueryHelper.cs:56–61, 133–139), and that would add `GetTagAsync` requests to the count. Assert the number of `tags/list` requests, not the total number of requests.
