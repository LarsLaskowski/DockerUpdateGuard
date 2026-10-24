# Plan: Pagination links are followed to any host, sending the Docker Hub bearer token along

Source: Issue #264
Status: Draft
Tier: security — the change creates a guard whose purpose is to stop an attacker (an origin allow-list on next-page links) and protects the handling of a credential (the Docker Hub bearer token derived from the PAT).
Security areas touched: Registry and Docker Hub calls (`DockerHubClient`, `OciRegistryClient`, `DockerScoutVulnerabilityProvider`); Secrets in configuration (Docker Hub PAT and the bearer token derived from it); Logging and telemetry of external data (new warnings and failure messages that carry the origin a remote side named)

## Problem / root cause

Three paginated calls take the remote side's next-page link and request it without looking at its target:

- `src/DockerUpdateGuard/DockerHub/DockerHubClient.cs:1117` (`GetRepositoriesAsync`) and `:1285-1290` (`GetTagsAsync`)
  assign the raw `next` string to `requestUri`; `SendGetAsync` (`:831-842`) builds `new HttpRequestMessage(Get, string)`,
  so an absolute `next` bypasses `BaseAddress`, and `ApplyAuthenticationAsync` (`:873-904`) attaches
  `Authorization: Bearer <Docker Hub JWT>` to it whenever `DockerHub:Pat` is set.
- `src/DockerUpdateGuard/Vulnerabilities/DockerScoutVulnerabilityProvider.cs:348` assigns `scoutResponse?.Next` to `nextUrl`;
  every page gets the bearer token (`:319-321`).
- `src/DockerUpdateGuard/Images/OciRegistryClient.cs:658-661` (`GetNextTagsUri`) returns any absolute link target as is;
  `GetTagsAsync` follows it (`:1305`).

Related defect found on the way (fixed by the same change): `GetNextTagsUri` tries `Uri.TryCreate(linkTarget,
UriKind.Absolute)` first (`:658`). On Linux — the platform of the container image — a root-relative target such as
`/v2/<repo>/tags/list?last=x&n=100` parses as the absolute URI `file:///v2/<repo>/tags/list%3Flast=x&n=100`, so the
relative-resolution branch (`:663`) is never reached and `HttpClient` throws `NotSupportedException` ("The 'file' scheme
is not supported."), which escapes `GetTagsAsync`. OCI tag listings with more than one page from a registry that sends
root-relative links (ghcr.io does) fail today.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | Docker Hub `GetRepositoriesAsync`/`GetTagsAsync` follow `next` unchecked and attach the bearer token to every page | `DockerHubClient.cs:1117`, `:1285-1290`, `:831-842`, `:873-904` | confirmed |
| 2 | An absolute URI string in `HttpRequestMessage` ignores `BaseAddress` | scratch console (.NET 10, Linux): `new HttpRequestMessage(Get, "https://attacker.example/x").RequestUri` is absolute on `attacker.example` | confirmed |
| 3 | Docker Scout follows `next` unchecked with the bearer token; the 50-page cap only limits the count | `DockerScoutVulnerabilityProvider.cs:305`, `:310-315`, `:319-321`, `:348` | confirmed |
| 4 | A relative Scout `next` fails today and becomes "Docker Scout scan failed: …" | scratch: `new HttpRequestMessage(Get, "/v2/x")` keeps a relative URI; the client from `CreateClient()` (`:400`) has no `BaseAddress`, and `HttpClient` rejects a relative URI without one with `InvalidOperationException`, caught at `:444-448` | confirmed |
| 5 | OCI `GetNextTagsUri` returns any absolute target unchanged | `OciRegistryClient.cs:658-661` | confirmed |
| 6 | On Linux a root-relative `Link` target becomes a `file:` URI and the request throws `NotSupportedException` | scratch console: `Uri.TryCreate("/v2/acme/app/tags/list?n=100&last=x", UriKind.Absolute, …)` gives `file:///v2/acme/app/tags/list%3Fn=100&last=x`; `HttpClient.SendAsync` on it throws `System.NotSupportedException: The 'file' scheme is not supported.` | confirmed |
| 7 | Issue claim "follows a relative link … as before" for OCI | refuted by fact 6 on Linux; a relative link only worked where `/…` is not an absolute path (Windows) | refuted |
| 8 | Real registries send root-relative `Link` targets | `curl` to ghcr.io (2026-10-24): `link: </v2/home-assistant/home-assistant/tags/list?last=2021.5.0.dev20210428&n=2>; rel="next"` | confirmed |
| 9 | Docker Hub sends absolute same-origin `next` links, so the default configuration keeps working | `curl` 2026-10-24: tags `"next":"https://hub.docker.com/v2/namespaces/library/repositories/nginx/tags?page=2&page_size=2"`, repositories `"next":"https://hub.docker.com/v2/repositories/library/?page=2&page_size=2"`; default `ApiBaseUrl` `https://hub.docker.com/` | confirmed |
| 10 | The OCI token cache is keyed per authority, but the foreign request is still sent and a `401` there triggers an anonymous token request to the realm it names | `OciRegistryClient.cs:526-529`, `:1027`, `:1046-1062` | confirmed |
| 11 | `GetTagsAsync` (OCI) drops the scheme and base path of an absolute registry URL, so the allowed origin must come from the first request URI | `OciRegistryClient.cs:496-506`, `:1259-1260` | confirmed |
| 12 | `DockerHubClient`'s `BaseAddress` is `GetBaseUri(options.DockerHub)` in production and set in every test that builds the client | `ServiceCollectionExtensions.cs:73-75`; `DockerHubClientTests.cs` (all tests), `RegistryBaseImageResolverTests.cs` | confirmed |
| 13 | `Uri` resolution of the link forms in *Approach* (base + link) | scratch console with `Uri.TryCreate(currentRequestUri, link, out …)`, each row of the table measured | confirmed |
| 14 | `Uri.Port` is the scheme default when absent, the host is lowercased, `:443` on `https` equals no port | scratch: `https://REG.example.com:443/v2/x` gives host `reg.example.com`, port 443, `SchemeAndServer` `https://reg.example.com` | confirmed |
| 15 | `GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped)` drops user info, path, query and fragment | scratch: `https://user:pw@reg.example.com/v2/x` gives `https://reg.example.com`; `https://reg.example.com:8443/v2/x` gives `https://reg.example.com:8443` | confirmed |
| 16 | Event IDs 3211, 3409 and 2099 are free | `grep -rn "EventId = " src` | confirmed |
| 17 | The form of Docker Scout's real `next` link (absolute on `api.scout.docker.com`, relative, or another host) | to verify: one authenticated Docker Scout listing with more than one page — needs Docker Hub credentials the session lacks; owner Product Manager after release; until then the design fails closed (a foreign host gives *failed*), recorded as residual risk in record 0004 | unverified |
| 18 | Same-origin pagination is already pinned by tests | `DockerHubClientGetRepositoriesAsyncReadsAllRepositoryPagesAsync`, `DockerHubClientGetTagsAsyncReadsAllTagPagesAsync`, `DockerScoutVulnerabilityProviderGetVulnerabilitiesAsyncMergesPaginatedResultsAsync`, `…StopsAtPageCapAndWarnsAsync`, `…RefreshesTokenOncePerPaginatedFetchAsync` | confirmed |

## Acceptance criteria

- [ ] AC1: `DockerHubClient.GetRepositoriesAsync` and `GetTagsAsync`, given a `next` link on another host, another port or
  `http` instead of `https` on the same host, send no request to it and return `Failed` with the exact message of
  *Approach* (messages), log warning 3211, and no request carrying an `Authorization` header goes anywhere but
  `https://hub.docker.com`.
- [ ] AC2: `DockerScoutVulnerabilityProvider.GetVulnerabilitiesAsync`, given a `next` link on another origin, sends no
  request to it and returns `Failed` with the exact message, logging warning 3409; a root-relative `next` is resolved
  against the current request and followed (pages merged, `Succeeded`).
- [ ] AC3: `OciRegistryClient.GetTagsAsync`, given a `Link: rel="next"` target on another origin, sends no request to it
  and returns `Failed` with the exact message, logging warning 2099; a root-relative target (the ghcr.io form) and a
  same-origin absolute target are followed.
- [ ] AC4: `PaginationLinkGuard.ResolveNextLink` returns the resolved link for every *follow* row and `null` for every
  *reject* row of the table in *Approach*, including the `https` to `http` downgrade on the same host.
- [ ] AC5: No failure message and no warning of AC1–AC3 contains the PAT, the bearer token, an `Authorization` value,
  the user info, path or query of the rejected link: the origin text is scheme, host and (non-default) port only, or
  `unparseable link`.
- [ ] AC6: Same-origin pagination behaves as before: all pages read, the Docker Scout 50-page cap, the token refresh once
  per paginated fetch, and a Docker Hub bounded listing that stops on its own conditions without looking at the link.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `DockerHubClientGetTagsAsyncForeignNextLinkReturnsFailedAsync` (`[DataRow]` link, expected origin: `https://attacker.example/collect?page=2&token=leak` → `https://attacker.example`; `http://hub.docker.com/v2/namespaces/acme/repositories/api/tags?page=2&page_size=100` → `http://hub.docker.com`; `https://hub.docker.com:8443/v2/namespaces/acme/repositories/api/tags?page=2&page_size=100` → `https://hub.docker.com:8443`), `DockerHubClientGetRepositoriesAsyncForeignNextLinkReturnsFailedAsync` (`https://attacker.example/collect?page=2&token=leak`) — assert status, exact message, no request whose URI starts with the expected origin, every request with an `Authorization` scheme starts with `https://hub.docker.com/`, one `Warning` entry with event 3211 | `src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs` |
| AC2 | `DockerScoutVulnerabilityProviderGetVulnerabilitiesAsyncForeignNextLinkReturnsFailedAsync`, `DockerScoutVulnerabilityProviderGetVulnerabilitiesAsyncRelativeNextLinkFollowsLinkAsync` (next `/v1/repositories/library/nginx/tags/latest/vulnerabilities?page=2`, request to `ScoutVulnerabilitiesPage2Url`) | `src/Tests/DockerUpdateGuard.Tests/DockerScoutVulnerabilityProviderTests.cs` |
| AC3 | `OciRegistryClientGetTagsAsyncForeignLinkHeaderReturnsFailedAsync`, `OciRegistryClientGetTagsAsyncRelativeLinkHeaderFollowsLinkAsync` (`</v2/mssql/server/tags/list?last=<tag>&n=100>; rel="next"`, the form of fact 8), `OciRegistryClientGetTagsAsyncSameOriginLinkHeaderFollowsLinkAsync` (deferred to step 6: passes today) | `src/Tests/DockerUpdateGuard.Tests/OciRegistryClientTests.cs` |
| AC4 | `PaginationLinkGuardResolveNextLinkSameOriginLinkReturnsLink`, `PaginationLinkGuardResolveNextLinkRelativeLinkResolvesAgainstCurrentRequest`, `PaginationLinkGuardResolveNextLinkForeignOriginReturnsNull`, `PaginationLinkGuardResolveNextLinkUnparseableLinkReturnsNull` — one `[DataRow]` per table row | `src/Tests/DockerUpdateGuard.Tests/PaginationLinkGuardTests.cs` |
| AC5 | `PaginationLinkGuardDescribeOriginLinkWithCredentialsReturnsSchemeHostAndPortOnly` (`https://user:secret@attacker.example:8443/collect?token=leak` → `https://attacker.example:8443`), `PaginationLinkGuardDescribeOriginUnparseableLinkReturnsPlaceholder`, `PaginationLinkGuardCreateRejectionMessageReturnsExactText`; and in every AC1–AC3 test: message and log entries contain neither `test-pat`, the login JWT, `token=leak` nor `/collect` | `src/Tests/DockerUpdateGuard.Tests/PaginationLinkGuardTests.cs`, and the AC1–AC3 files |
| AC6 | existing, not rewritten, must stay green: the tests of fact 18 and `DockerHubClientGetTagsAsyncStopsAfterBaselineAndSkipsLowerVersionsAsync`; new pin `DockerHubClientGetTagsAsyncStopConditionIgnoresForeignNextLinkAsync` (deferred to step 6: passes today) | `src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs`, `src/Tests/DockerUpdateGuard.Tests/DockerScoutVulnerabilityProviderTests.cs` |

The UNC row of the table (two backslashes, `attacker.example`, one backslash, `x`) is written as a verbatim string
(`@"..."`) and its test asserts that the input still contains a backslash character, so a lost backslash fails the run
instead of testing a plain relative path.

## Approach

**Guard** — `src/DockerUpdateGuard/Infrastructure/PaginationLinkGuard.cs`, pure and I/O-free:

1. `ResolveNextLink(currentRequestUri, allowedOrigin, nextLink)`: `null` for a null or whitespace link; otherwise
   `Uri.TryCreate(currentRequestUri, nextLink, out var resolved)` — never `Uri.TryCreate(nextLink, UriKind.Absolute)`
   (fact 6); `false` gives `null`. The link is followed only when `resolved.Scheme` equals `allowedOrigin.Scheme`
   (ordinal, ignore case), `resolved.IdnHost` equals `allowedOrigin.IdnHost` (ordinal, ignore case) and
   `resolved.Port == allowedOrigin.Port`; then `resolved` is returned, else `null`. Never throws.
2. `DescribeOrigin(currentRequestUri, nextLink)`: `resolved.GetComponents(UriComponents.SchemeAndServer,
   UriFormat.UriEscaped)` when `Uri.TryCreate(currentRequestUri, nextLink, …)` succeeds, else the constant
   `UnparseableLinkDescription` = `unparseable link`.
3. `CreateRejectionMessage(listingDescription, rejectedOrigin)` returns
   `{listingDescription} stopped: the next-page link pointed to a different origin ({rejectedOrigin})`.

The caller sends exactly the `Uri` instance the guard returned — no string round trip — so the check and the request
cannot parse the link differently.

Link forms `System.Uri` accepts, resolved against `https://reg.example.com/v2/acme/app/tags/list?n=100` with allowed
origin `https://reg.example.com/` (all measured, fact 13):

| Link | Resolves to | Guard |
| ---- | ----------- | ----- |
| `https://reg.example.com/v2/x?n=100&last=b` | itself | follow |
| `https://REG.example.com:443/v2/x` | `https://reg.example.com/v2/x` | follow |
| `/v2/acme/app/tags/list?n=100&last=b` (root-relative) | `https://reg.example.com/v2/acme/app/tags/list?n=100&last=b` | follow |
| `list?n=100&last=b` (path-relative) | `https://reg.example.com/v2/acme/app/tags/list?n=100&last=b` | follow |
| `https://user:pw@reg.example.com/v2/x` | same origin with user info | follow (the description drops the user info) |
| `https://attacker.example/x` | another host | reject |
| `https://reg.example.com:8443/v2/x` | another port | reject |
| `http://reg.example.com/v2/x` | scheme downgrade | reject |
| `//attacker.example/x` (scheme-relative) | `https://attacker.example/x` | reject |
| UNC form, two backslashes `attacker.example` backslash `x` | `file://attacker.example/x` | reject |
| `file:///etc/passwd`, `javascript:alert(1)`, `ftp://reg.example.com/x` | another scheme | reject |
| `https://attacker.example#@reg.example.com/` | host `attacker.example` | reject |
| `https://reg.example.com./v2/x` (trailing dot) | host `reg.example.com.` | reject (fails closed) |
| `http:/v2/x`, `https://reg.example.com%2eattacker.example/x` | `TryCreate` false | reject (unparseable) |
| empty, whitespace | — | reject (callers treat it as the last page first) |

**Call sites.** A null or whitespace raw link means "last page" and ends the listing as today; the guard runs only when
the listing would follow the link, so a listing that stops on its own (a Docker Hub stop condition, `MaximumTags`, the
Docker Scout page cap) ignores it. A rejected link: log the warning, send nothing, return `Failed` with the message —
pages read before are discarded.

- `DockerHubClient`: allowed origin `GetApiBaseUri()` = `_httpClient.BaseAddress ?? GetBaseUri(options.DockerHub)`;
  the loops of `GetRepositoriesAsync` and `GetTagsAsync` keep a `Uri? requestUri`, the first page
  `new Uri(GetApiBaseUri(), "<relative path as today>")`, sent through a new `SendGetAsync(Uri, …)` overload; the
  existing `SendGetAsync(string, …)` delegates to it with `new Uri(relativeUri, UriKind.Relative)`.
- `DockerScoutVulnerabilityProvider.FetchVulnerabilitiesAsync`: allowed origin `new Uri(DockerScoutBaseUrl,
  UriKind.Absolute)`, first page `new Uri($"{scoutBaseUrl}/v1/…/vulnerabilities", UriKind.Absolute)`; order per page:
  read, map, then `next` empty → done, page cap reached → existing cap warning and `Succeeded`, else guard. The 401 retry
  starts from the first page as today.
- `OciRegistryClient.GetTagsAsync`: allowed origin = the first `CreateTagsUri(...)` result; `GetNextTagsUri` becomes
  `GetNextTagsLink(response)` returning the raw target between `<` and `>` (null when absent, empty or whitespace); the
  guard resolves it against the current `requestUri`, which also fixes the root-relative defect.

**Messages** (exact, compared by the tests; `{origin}` from `DescribeOrigin`):

- `Docker Hub repository listing for '{accountName}' stopped: the next-page link pointed to a different origin ({origin})`
- `Docker Hub tag listing for '{repository}' stopped: the next-page link pointed to a different origin ({origin})`
- `Docker Scout vulnerability listing for '{imageReference.FullReference}' stopped: the next-page link pointed to a different origin ({origin})`
- `Registry tag listing for '{normalizedRegistry}/{repository}' stopped: the next-page link pointed to a different origin ({origin})`

**Warnings** (source-generated, `LogLevel.Warning`):

- 3211 `DockerHubClientLogging.DockerHubPaginationLinkRejected`: `Docker Hub {OperationName} pagination for {TargetName} stopped because the next-page link pointed to a different origin {RejectedOrigin}`
- 3409 `DockerScoutVulnerabilityProviderLogging.DockerScoutPaginationLinkRejected`: `Docker Scout pagination for {ImageReference} stopped because the next-page link pointed to a different origin {RejectedOrigin}`
- 2099 `ImageHostLoggingExtensions.OciRegistryPaginationLinkRejected`: `Registry tag listing for {Registry}/{Repository} stopped because the next-page link pointed to a different origin {RejectedOrigin}`

No new UI output; the messages reach scan-run messages and discovery logs as every other *failed* message.

## Options considered

The five options of record 0004: (1) same-origin only, else *failed* — chosen (Product Manager decision on the issue);
(2) follow without `Authorization` — loses: still a request to an attacker-chosen host and foreign data treated as the
registry's; (3) stop and return partial *succeeded* — loses: hides updates, and a truncated Scout result would resolve
live findings; (4) rebuild the next page from its query on the base address — loses: depends on undocumented paging
parameters per API; (5) a host-blocking `DelegatingHandler` — loses: the OCI token realm is legitimately on another host
and the Scout client has no handler pipeline. Within option 1, a guard per client instead of one helper loses: three
copies of a security rule drift apart.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Infrastructure/PaginationLinkGuard.cs` (new) | the guard |
| DockerUpdateGuard | `DockerHub/DockerHubClient.cs` | `GetApiBaseUri`, `SendGetAsync(Uri, …)`, guarded loops in `GetRepositoriesAsync`, `GetTagsAsync` |
| DockerUpdateGuard | `DockerHub/DockerHubClientLogging.cs` | event 3211 |
| DockerUpdateGuard | `Vulnerabilities/DockerScoutVulnerabilityProvider.cs` | guarded loop in `FetchVulnerabilitiesAsync` |
| DockerUpdateGuard | `Vulnerabilities/DockerScoutVulnerabilityProviderLogging.cs` | event 3409 |
| DockerUpdateGuard | `Images/OciRegistryClient.cs` | `GetNextTagsUri` replaced by `GetNextTagsLink`, guarded loop in `GetTagsAsync` |
| DockerUpdateGuard | `Images/ImageHostLoggingExtensions.cs` | event 2099 |

## Signatures (for the Dev's skeleton)

```csharp
// src/DockerUpdateGuard/Infrastructure/PaginationLinkGuard.cs
namespace DockerUpdateGuard.Infrastructure;
internal static class PaginationLinkGuard
{
    public const string UnparseableLinkDescription = "unparseable link";
    public static Uri? ResolveNextLink(Uri currentRequestUri, Uri allowedOrigin, string? nextLink);
    public static string DescribeOrigin(Uri currentRequestUri, string? nextLink);
    public static string CreateRejectionMessage(string listingDescription, string rejectedOrigin);
}

// DockerHubClient (private)
private Uri GetApiBaseUri();
private async Task<HttpResponseMessage> SendGetAsync(Uri requestUri, CancellationToken cancellationToken);

// OciRegistryClient (replaces GetNextTagsUri(HttpResponseMessage, string))
private static string? GetNextTagsLink(HttpResponseMessage response);

// DockerHubClientLogging
public static partial void DockerHubPaginationLinkRejected(this ILogger logger, string operationName, string targetName, string rejectedOrigin);
// DockerScoutVulnerabilityProviderLogging
public static partial void DockerScoutPaginationLinkRejected(this ILogger logger, string imageReference, string rejectedOrigin);
// ImageHostLoggingExtensions
public static partial void OciRegistryPaginationLinkRejected(this ILogger logger, string registry, string repository, string rejectedOrigin);
```

Skeleton files: `PaginationLinkGuard.cs` (new) and the three logging classes; the three clients keep compiling with the
private members added as stubs.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/PaginationLinkGuardTests.cs` (new)
- `src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs`
- `src/Tests/DockerUpdateGuard.Tests/DockerScoutVulnerabilityProviderTests.cs`
- `src/Tests/DockerUpdateGuard.Tests/OciRegistryClientTests.cs`

Existing test code that calls a changed signature: none (every changed member is private; the guard is new).

Files of the change no unit test can cover: none.

## Areas

- `docs/areas/registries.md`: new subsection *Pagination links* (heading exactly `### Pagination links`, after *Bounded
  tag listings*) with the rule, the relative resolution and the *failed* outcome; the tag-listing and account-discovery
  bullets point to it; *Limits and errors* adds the rejected link to *Failed*; *Configuration reload* or the `ApiBaseUrl`
  bullet states that a mirror or proxy must rewrite next links to its own origin; the #264 *Known gaps* entry is removed;
  *Related decisions* links 0004.
- `docs/areas/vulnerabilities.md`: *Docker Scout* states the same-origin rule (origin of `DockerScoutBaseUrl`), relative
  resolution and the *failed* message (stored findings stay untouched); the #264 *Known gaps* entry is removed;
  *Related decisions* links 0004.
- `docs/areas/telemetry.md`: *Credentials and external data*, the list of what logs may carry, adds "the scheme, host and
  port of a rejected next-page link".

## Documentation updates

| File | Edit | Owner |
| ---- | ---- | ----- |
| `docs/areas/registries.md` | as *Areas* | Dev |
| `docs/areas/vulnerabilities.md` | as *Areas* | Dev |
| `docs/areas/telemetry.md` | as *Areas* | Dev |
| `README.md` | `ApiBaseUrl` (line 113) and `DockerScoutBaseUrl` (line 138) rows: a mirror or proxy must rewrite next-page links to its own origin, otherwise listings fail | Dev |
| `docs/ARCHITECTURE.md` | *Security posture*: bullet *Pagination stays on its origin* linking the area and 0004 (written) | Lead |
| `docs/decisions/0004-pagination-follows-only-same-origin-links.md`, index row | written, `Proposed` | Lead |

Sweep: `grep -n -i "paginat\|next\|credential"` over `docs/areas/*.md`, `docs/ARCHITECTURE.md`, `README.md`,
`SECURITY.md`; hits outside the rows above (`docs/CONTRIBUTING.md:111` release plan, `container-actions.md`,
`web-ui.md`) need no change. `.squad/project.md` already lists pagination under *Registry and Docker Hub calls*.

## Architecture check

- *Integration clients never throw for expected failures*: a rejected link is `Failed`; the guard never throws.
- *Secrets in configuration*: the token never leaves the configured origin through pagination; messages and logs carry
  only scheme, host and port.
- Layering: the helper lives in the host's `Infrastructure` next to `ExternalOperationResult<T>`; no new dependency, no
  configuration, no migration. `docs/ARCHITECTURE.md` gains the posture bullet (Lead, written).

## Security considerations

- [ ] No limits: no byte or length limit is added; the existing Docker Scout page cap is unchanged.
- [ ] Exceptions that reach a user-visible message: none new. The guard and `DescribeOrigin` never throw.
  `new Uri(DockerScoutBaseUrl, UriKind.Absolute)` throws `UriFormatException` only for an invalid reloaded
  configuration, caught as today into `Docker Scout scan failed: …` (today's `HttpRequestMessage` throws the same).

## Decision records

- `docs/decisions/0004-pagination-follows-only-same-origin-links.md` (Proposed)

## Out of scope / follow-ups

- Unchanged, named by the issue: the token service of a `WWW-Authenticate` realm; HTTP 3xx redirects.
- Follow-up issue (orchestrator creates): **"[BUG] Docker Hub and OCI listings have no page cap: a registry that keeps
  returning a next link keeps a scan reading forever"** — body: `DockerHubClient.GetRepositoriesAsync`/`GetTagsAsync`
  and `OciRegistryClient.GetTagsAsync` (unbounded) follow same-origin next links without a page limit, so a registry
  that returns the same or an endless next link keeps the scan reading; Docker Scout already caps at 50 pages. Expected:
  a page cap per listing with a defined outcome. Found while planning #264.
