# Plan: Docker Scout listing ends failed at a repeated link or after the shared page cap

Source: Issue #436
Tier: standard — a behavior change of the Docker Scout listing (a page bound on remote input, no security control created, changed or removed) that touches the Docker Scout pagination and a log message.
Security areas touched: Registry and Docker Scout calls (pagination of `Vulnerabilities/DockerScoutVulnerabilityProvider.cs`); Logging and telemetry of external data (`Vulnerabilities/DockerScoutVulnerabilityProviderLogging.cs`: one changed and one new warning)

## Problem / root cause

`DockerScoutVulnerabilityProvider.FetchVulnerabilitiesAsync`
(`src/DockerUpdateGuard/Vulnerabilities/DockerScoutVulnerabilityProvider.cs`, lines 487–492) breaks out of the page
loop when `pageCount >= MaxVulnerabilityPages` (50, line 32) and a `next` link is still present; the loop then falls
through to lines 508–510 and returns the advisories read so far as `Succeeded`. `VulnerabilityEnrichmentService`
treats a succeeded list as complete and resolves every previously active finding the list lacks
(`src/DockerUpdateGuard/Images/VulnerabilityEnrichmentService.cs`, lines 292–297), so a cut list resolves findings that
still exist. The cap check also runs before the origin check (line 494), unlike the registry listings, and a link
that repeats a page is not detected, so a self link costs 50 requests.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | At the page cap the Scout listing returns the advisories read so far as *succeeded* (issue) | `DockerScoutVulnerabilityProvider.cs` lines 487–492 `break`, then 508–510 `Succeeded`; test `...StopsAtPageCapAndWarnsAsync` asserts `Succeeded` with 50 advisories | confirmed |
| 2 | The cap code is "around lines 408–413" (issue) — the line numbers only | It is at lines 487–492 on 76962a4; lines 404–413 are the token cache of `GetOrCreateTokenAsync` | refuted |
| 3 | A truncated list resolves findings that still exist (issue) | `VulnerabilityEnrichmentService.cs` lines 292–297 resolve every previously active finding not matched by the list | confirmed |
| 4 | 0004 rejected partial results for this reason (issue) | `docs/decisions/0004-…md`, option 3: "a truncated Docker Scout result would resolve findings that still exist" | confirmed |
| 5 | Docker Hub and OCI listings end *failed* at the cap (issue, 0024) | `DockerHubClient.cs` 902–913 and `OciRegistryClient.cs` 1335–1346: repeated link, then `requestedPages.Count >= PaginationLinkGuard.MaxListingPages`, both `Failed` with the shared messages | confirmed |
| 6 | 0024 left Scout to a separate decision; 0024 is released | 0024 *Consequences*, last-but-one bullet; added in 1795db2, contained in tag `v1.3.8` (`git tag --contains`) — so it is amended by a new record, not edited | confirmed |
| 7 | A *failed* provider result leaves the stored findings untouched | `VulnerabilityEnrichmentService.cs` line 335 returns before the finding update; [Vulnerabilities](../../docs/areas/vulnerabilities.md) line 70 | confirmed |
| 8 | A 401 restarts the fetch from the first page, so each fetch has its own page set | `DockerScoutVulnerabilityProvider.cs` lines 590–612 call `FetchVulnerabilitiesAsync` again | confirmed |
| 9 | Event ids 3401–3407 are free; Scout uses 3408–3419 | `grep "EventId = 340"` in `src/` finds only 3400 (`DefaultVulnerabilityProviderLogging.cs`) | confirmed |
| 10 | Docker Scout's real last page carries no `next` link to itself | to verify: never observed (0004 residual risk); after merge the Product Manager checks the first vulnerability refresh with `Provider` `DockerScout` on a real account logs no event 3407 | unverified |

## Acceptance criteria

The listing description is `Docker Scout vulnerability listing for '<reference>'`, as in today's origin rejection;
`<reference>` is `ImageReference.FullReference` (`docker.io/library/nginx:latest` in the tests).

- AC1: When the 1,000th page (`PaginationLinkGuard.MaxListingPages`) of a Docker Scout listing still has a same-origin
  `next` link to a page not yet requested, the provider returns `Failed` with the message
  `Docker Scout vulnerability listing for 'docker.io/library/nginx:latest' stopped: the listing did not end within 1000 pages`,
  no data, has sent exactly 1,000 vulnerability requests, and logs warning 3419 once.
- AC2: When a page's same-origin `next` link resolves to a request already sent in this fetch (the first page, the
  page itself, or the page itself with only a different fragment), the provider returns `Failed` with the message
  `Docker Scout vulnerability listing for 'docker.io/library/nginx:latest' stopped: the next-page link repeated a page already read`,
  no data, sends no request for that link, and logs warning 3407 once.
- AC3: A listing of exactly 1,000 pages whose last page has no `next` link returns `Succeeded` with the advisories of
  all 1,000 pages.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `DockerScoutVulnerabilityProviderGetVulnerabilitiesAsyncWhenListingDoesNotEndWithinPageCapReturnsFailedAsync` | `src/Tests/DockerUpdateGuard.Tests/DockerScoutVulnerabilityProviderTests.cs` |
| AC2 | `DockerScoutVulnerabilityProviderGetVulnerabilitiesAsyncWhenNextLinkRepeatsPageReturnsFailedAsync` (one `[DataRow]` per repeated form: first page, same page, same page with a fragment; two vulnerability requests each) | `src/Tests/DockerUpdateGuard.Tests/DockerScoutVulnerabilityProviderTests.cs` |
| AC3 | `DockerScoutVulnerabilityProviderGetVulnerabilitiesAsyncWhenListingEndsAtPageCapSucceedsAsync` | `src/Tests/DockerUpdateGuard.Tests/DockerScoutVulnerabilityProviderTests.cs` |

AC1 and AC3 generate distinct page links (`?page=2` … `?page=1001`) from a `DelegateHttpMessageHandler`, as
`DockerHubClientTests` does for its cap tests, and compare against `PaginationLinkGuard.MaxListingPages`, not a
literal. AC2's first page links to `?page=2`, whose `next` is the repeated form. All three fail on the current code,
which follows any link up to 50 pages and then succeeds.

## Approach

In `FetchVulnerabilitiesAsync`, mirror `DockerHubClient.AdvanceListing` (lines 884–918):

1. Seed a `HashSet<string>` (ordinal) with `PaginationLinkGuard.CreateRequestKey(requestUri)` of the first page; it
   replaces `pageCount`.
2. After a page is read and its `next` link is not blank: resolve it with `PaginationLinkGuard.ResolveNextLink`
   (origin rejection unchanged, message and event 3409 as today); then, when the set contains the key of the resolved
   URI, log 3407 and return `Failed` with `PaginationLinkGuard.CreateRepeatedLinkMessage(listingDescription)`; then,
   when the set's count is `>= PaginationLinkGuard.MaxListingPages`, log 3419 and return `Failed` with
   `PaginationLinkGuard.CreatePageCapMessage(listingDescription)`; otherwise add the key and follow the link.
3. `listingDescription` is the existing `Docker Scout vulnerability listing for '<reference>'` string, built once and
   shared by the three stops. The `Unauthorized` flag of all three stops is `false`.
4. The constant `MaxVulnerabilityPages` is deleted. The Dev may move steps 2's checks into a private helper (like
   `AdvanceListing`) to keep the method under the cognitive-complexity rule; that is no API.

No new user-visible output beyond the two messages above (the assessment message of the image version, shown as
today). The page bound is observed in the tests through the request log of the test handler.

## Options considered

- **Chosen: the rule of 0024 (repeated link, then the shared cap of 1,000 pages, both *failed*).**
- **Keep the cap of 50 and only end *failed*:** smallest diff, but Docker Scout's page size is undocumented, so a
  heavily affected image could fail on every refresh where it succeeds today; a self link still costs 50 requests.
- **Keep the partial success:** the defect itself — a cut list resolves findings that still exist.
- **Return the partial list as *partial* and keep the findings it lacks:** `ExternalOperationResult` has no partial
  status with data, and the refresh would need a second path for a listing no real image reaches.

Details in [0047](../../docs/decisions/0047-the-docker-scout-listing-stops-failed-at-a-repeated-link-or-after-the-shared-page-cap.md).

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Vulnerabilities/DockerScoutVulnerabilityProvider.cs` | Repeated-link check and shared cap in `FetchVulnerabilitiesAsync`; `MaxVulnerabilityPages` deleted |
| DockerUpdateGuard | `Vulnerabilities/DockerScoutVulnerabilityProviderLogging.cs` | Message of 3419 changed; new warning 3407 |
| DockerUpdateGuard.Tests | `DockerScoutVulnerabilityProviderTests.cs` | Three tests; the 50-page test and its constant removed |

## Signatures (for the Dev's skeleton)

- `public static partial void DockerScoutPaginationLinkRepeated(this ILogger logger, string imageReference);` in
  `DockerScoutVulnerabilityProviderLogging` (internal class), `[LoggerMessage(EventId = 3407, Level = LogLevel.Warning,
  Message = "Docker Scout pagination for {ImageReference} stopped because the next-page link repeated a page already read")]`.
- `DockerScoutPageCapReached(this ILogger logger, string imageReference, int pageCap)` keeps its signature and event
  3419; its message becomes `Docker Scout pagination for {ImageReference} stopped at the page cap of {PageCap}`.
- The private constant MaxVulnerabilityPages is deleted.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/DockerScoutVulnerabilityProviderTests.cs` (existing).

Existing test code affected: `DockerScoutVulnerabilityProviderGetVulnerabilitiesAsyncStopsAtPageCapAndWarnsAsync`
asserts the partial success and uses a link that repeats, so it now fails with the repeated-link message; the
Tester deletes it in step 5, together with the test constants `MaxVulnerabilityPages` and, when no other test uses it,
`ScoutVulnerabilitiesLoopUrl` (AC1 replaces it). No signature change reaches other tests. The other pagination tests
(`MergesPaginatedResultsAsync`, `RefreshesTokenOncePerPaginatedFetchAsync`, `RelativeNextLinkFollowsLinkAsync`,
the foreign-link tests) follow distinct links within one fetch and are unchanged.

Files no unit test can cover: none.

## Areas

- [Vulnerabilities](../../docs/areas/vulnerabilities.md), *Docker Scout*: the 50-page cap with partial success is
  replaced by the repeated-link and page-cap stops.
- [Registries](../../docs/areas/registries.md), *Pagination links*: the stops now apply to Docker Scout too.

## Documentation updates

All by the Dev, once the code is green. Sweep: `grep` for "50 pages", "page cap", "not to Docker Scout", "Docker
Hub and OCI listings", "3419" over `docs/` and `README.md`; hits below (`integration-failures.md` line 62 and
`registries.md` line 213 were read and stay true).

- `docs/areas/vulnerabilities.md` lines 165–170: drop "for at most **50 pages**" and the sentence "When the cap is
  reached, the advisories read so far are returned as a successful result."; add after the origin bullet (line
  171–176) that a repeated link and the page cap end the listing *failed* with the messages of AC1/AC2, no advisories,
  stored findings untouched, warnings 3407 and 3419, linking [Registries](registries.md#pagination-links) for the
  rule; add 0024 and 0047 to *Related decisions*.
- `docs/areas/registries.md` lines 140–143: the event list gains "Docker Scout vulnerability listing: 3407 and 3419",
  and "They apply to the Docker Hub tag and repository listings and the OCI tag listing, not to Docker Scout" names
  the Docker Scout vulnerability listing among them; line 148 and *Related decisions* (line 297–298) add 0047.
- `docs/ARCHITECTURE.md` lines 508–511 (*Pagination stays on its origin*): "The Docker Hub and OCI listings also end
  *failed*" becomes the three clients, linking 0047 beside 0024.
- No README, changelog or `.squad/project.md` change (no changelog exists; no new security area).

## Architecture check

The guarantee that integration clients answer expected failures with `ExternalOperationResult<T>` and never throw
holds (two more `Failed` returns). The pagination origin guarantee (0004) is unchanged and checked first. No
guarantee of `.squad/project.md` is weakened; the change removes a path where a remote side could resolve findings.

## Security considerations

- [x] No byte or length limits on parsed input; the bound is a page count.
- [x] No exception reaches a message: the two new messages are fixed texts plus the image reference, which today's
  origin message already carries; the logs carry the image reference only, never the link.

## Decision records

- `docs/decisions/0047-the-docker-scout-listing-stops-failed-at-a-repeated-link-or-after-the-shared-page-cap.md`
  (Proposed), amending released 0024.

## Out of scope / follow-ups

- Bounding the duration of a listing (0024, *Consequences*): unchanged.
