# Plan: Docker Scout provider skips the transient-failure retry and scans by tag instead of digest

Source: Issue #267
Tier: standard — two behavior changes of the Docker Scout provider (retry of its requests, a digest check before the tag lookup) that create, change or remove no security control.
Security areas touched: Registry and Docker Scout calls (`DockerScoutVulnerabilityProvider`, `DockerHubClient` reads a new field of untrusted JSON); Failure messages and remote text (a new assessment message, the registry's message passed on); Logging and telemetry of external data (a new log event with the image reference); Secrets in configuration (the login body with the Docker Hub PAT may now be resent by the retry handler)

## Problem / root cause

1. The provider asks for the default client (`DockerScoutVulnerabilityProvider.cs:500`), which
   `ServiceCollectionExtensions.cs:72-101` never gives a `TransientHttpRetryHandler`; neither the login (line 279)
   nor the page requests (line 391) are retried.
2. `CreateVulnerabilitiesUri` (line 127-138) builds the `tags/<tag>` URL and nothing checks `imageReference.Digest`,
   which is used only in `FullReference` for logs and messages; a republished tag gives the advisories of the new image.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 2 | Issue's lines 108, 400-402, 226, 323 (registration, default client, login, pages) | now 112, 500-502, 279, 391 (main moved) | confirmed |
| 4 | `docs/areas/registries.md` says the retry "does not apply to … Docker Scout" | the sentence and the *Retry of transient HTTP failures* section are in `docs/areas/integration-failures.md:99-104`; `registries.md` has no such section | refuted |
| 5 | The login would be retried on 408/429/5xx like any other request | a `POST`: `TransientHttpRetryHandler.cs:341`, `integration-failures.md:112-119` retry it only on a 429 with `Retry-After` missing or at most 30 s and on an error before any byte was sent | refuted |
| 5a | Page `GET`s get the full rule (408/429/5xx, transport errors) | `TransientHttpRetryHandler.cs:92-112,332` | confirmed |
| 8 | With the handler inside the client, `HttpClient.Timeout` covers all attempts and waits | documented and relied on for Docker Hub, `integration-failures.md:125-127`; the provider sets `client.Timeout` per call (line 502) | confirmed |
| 9 | Only the final login response reaches `LoginRetryAfterRegistry`, so a 429 the handler waits out opens no window; a 429 with `Retry-After` > 30 s is returned and opens one | `DockerScoutVulnerabilityProvider.cs:279-284`; `integration-failures.md:156-158` | confirmed |
| 10 | The lookup URL uses the tag; the digest is used only in `FullReference` | `DockerScoutVulnerabilityProvider.cs:127-138`, `ImageReference.cs:28,33` | confirmed |
| 11 | No documented Docker Scout digest endpoint exists | to verify: not needed — the plan does not use one (0031, option 1); the session has no Docker Hub account to probe | unverified |
| 12 | Docker Hub's tag record has the index digest at top level and the platform image digests under `images[].digest`, including `unknown/unknown` attestation entries | real sample `specs/issue-267/docker-hub-tag-sample.json` (`library/nginx:1.27`, 16 `images`, ids and user name anonymized and checked) | confirmed |
| 14 | `DockerHubTagData` is serialized into `ImageVersion.MetadataJson`, a 4000-character column | `ImageScanOrchestrator.cs:607,730`; `ImageVersionConfiguration.cs:39-40`; 16 platform digests add about 1,200 characters | confirmed |
| 17 | Stored digests are the index digest for runtime containers pulled by tag (`RepoDigests`) and for observed images (tag record); a base-image label digest may be a platform digest | to verify: not needed, both kinds match (AC6); read: `DockerInstanceClient.cs:135,170-202`, `ImageScanOrchestrator.cs:725-730`, `RegistryBaseImageResolver.cs:93` | unverified |

Related defect found: the assessment source comes from the live options (follow-up issue below).

## Acceptance criteria

- AC1: The Docker Scout provider creates its client with `CreateClient(DockerScoutVulnerabilityProvider.HttpClientName)`,
  and the composition root registers that name with `TransientHttpRetryHandler` in its handler chain.
- AC2: A vulnerability page answered 503 and then 200 through the retry handler gives *succeeded* with the advisories
  of the page.
- AC3: With the retry handler in the pipeline, a page answered 401 is sent once (not retried by the handler), followed
  by exactly one more login and one more read; the second read's 200 gives *succeeded*.
- AC4: The login is retried by the handler on a 429 without `Retry-After` (two login requests, then *succeeded*) and
  not on a 503 (one login request, *failed* "Docker Hub authentication failed").
- AC5: For an image reference with a digest that equals neither the tag record's digest nor any of its platform
  digests (compared case-insensitively), or a tag record without any digest, the result is *not found* with the
  message `The tag of '<FullReference>' now points to a different digest; Docker Scout looks up tags only and cannot assess this version.`,
  and neither the login nor a Docker Scout request is sent. A non-cancellation exception from resolving the registry
  service or from `GetTagAsync` gives *failed* (`Docker Scout scan failed: ` prefix), sending nothing; the scope is
  disposed after every tag lookup.
- AC6: For an image reference whose digest equals the tag record's digest, or one of its platform digests, the Docker
  Scout tag endpoint is read as today and the result is *succeeded*.
- AC7: When the tag lookup gives *not found*, the result is *not found* with the registry's message; any other
  unsuccessful status gives *failed* with the registry's message (`Tag digest lookup failed for '<FullReference>'`
  when it has none). Neither the login nor a Docker Scout request is sent.
- AC8: An image reference without a digest makes no tag lookup and reads the tag endpoint as today; one with a
  digest makes no tag lookup when the credentials, registry or path check answers first (*not configured*,
  *unsupported*, *not found* with `DotSegmentMessage`, as today).
- AC9: `DockerHubClient.GetTagAsync` fills `DockerHubTagData.PlatformDigests` with every `images[].digest` that passes
  `RegistryReferenceValidator.ValidDigestOrNull`, in document order, skipping the others, including entries that are
  not JSON objects (the lookup still succeeds with its top-level digest); the tag listing leaves it
  empty; `JsonSerializer.Serialize` of a `DockerHubTagData` does not contain it.
- AC10: The documentation updates below describe the new behavior, and the *Known gaps* entry of #267 is gone.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `ServiceCollectionExtensionsDockerScoutClientUsesRetryHandler`; `DockerScoutVulnerabilityProviderGetVulnerabilitiesAsyncRequestsNamedClient` | `ServiceCollectionExtensionsTests.cs`; `DockerScoutVulnerabilityProviderTests.cs` |
| AC2 | `DockerScoutVulnerabilityProviderGetVulnerabilitiesAsyncTransientFailureIsRetried` | `DockerScoutVulnerabilityProviderTests.cs` |
| AC3 | `DockerScoutVulnerabilityProviderGetVulnerabilitiesAsyncUnauthorizedIsNotRetriedByHandler` | `DockerScoutVulnerabilityProviderTests.cs` |
| AC4 | `DockerScoutVulnerabilityProviderGetVulnerabilitiesAsyncLoginTooManyRequestsIsRetried`; `DockerScoutVulnerabilityProviderGetVulnerabilitiesAsyncLoginServiceUnavailableIsNotRetried` | `DockerScoutVulnerabilityProviderTests.cs` |
| AC5 | `DockerScoutVulnerabilityProviderGetVulnerabilitiesAsyncWithRepublishedTagReturnsNotFound`; `DockerScoutVulnerabilityProviderGetVulnerabilitiesAsyncWithTagWithoutDigestReturnsNotFound`; `DockerScoutVulnerabilityProviderGetVulnerabilitiesAsyncWithDigestTagLookupThrowsReturnsFailed` | `DockerScoutVulnerabilityProviderTests.cs` |
| AC6 | `DockerScoutVulnerabilityProviderGetVulnerabilitiesAsyncWithDigestOfTagQueriesTag`; `DockerScoutVulnerabilityProviderGetVulnerabilitiesAsyncWithPlatformDigestOfTagQueriesTag` | `DockerScoutVulnerabilityProviderTests.cs` |
| AC7 | `DockerScoutVulnerabilityProviderGetVulnerabilitiesAsyncWithTagNotFoundReturnsNotFound`; `DockerScoutVulnerabilityProviderGetVulnerabilitiesAsyncWithTagLookupFailedReturnsFailed` | `DockerScoutVulnerabilityProviderTests.cs` |
| AC8 | `DockerScoutVulnerabilityProviderGetVulnerabilitiesAsyncWithoutDigestSkipsTagLookup`; `DockerScoutVulnerabilityProviderGetVulnerabilitiesAsyncWithDigestNotConfiguredSkipsTagLookup`; `DockerScoutVulnerabilityProviderGetVulnerabilitiesAsyncWithDigestUnsupportedRegistrySkipsTagLookup`; `DockerScoutVulnerabilityProviderGetVulnerabilitiesAsyncWithDigestDotSegmentSkipsTagLookup` | `DockerScoutVulnerabilityProviderTests.cs` |
| AC9 | `DockerHubClientGetTagAsyncReadsPlatformDigests`; `DockerHubClientGetTagAsyncSkipsInvalidPlatformDigest`; `DockerHubTagDataSerializeOmitsPlatformDigests` | `DockerHubClientTests.cs`; `DockerHubTagDataTests.cs` |
| AC10 | manual: the Reviewer, step 8, reads the listed documents against the code | — |

Test notes for the Tester:

- AC2–AC4 put a real `TransientHttpRetryHandler` (its internal constructor with a delay callback that completes at
  once) in front of `SequenceHttpMessageHandler`, returned by the factory substitute for the named client.
- AC5–AC8: the digest test cases set up the `IServiceScopeFactory` substitute so that its scope's `IServiceProvider`
  returns an `IRegistryMetadataService` substitute; "no tag lookup" (AC8) is asserted as no received
  `CreateScope` call, "no login and no Docker Scout request" as no request in the HTTP handler. Stub
  `GetService(typeof(IRegistryMetadataService))`: a substitute `IServiceProvider` is no `ISupportRequiredService`,
  so `GetRequiredService` falls back to it. The AC5 and AC6 tests also assert that the scope was disposed.
- AC1's chain test resolves `IOptionsMonitor<HttpClientFactoryOptions>`, takes `.Get(DockerScoutVulnerabilityProvider.HttpClientName)`,
  runs its `HttpMessageHandlerBuilderActions` on an `HttpMessageHandlerBuilder` from the service provider and asserts
  that `AdditionalHandlers` contains a `TransientHttpRetryHandler`.
- AC9's `DockerHubClientGetTagAsyncReadsPlatformDigests` reads the content of `specs/issue-267/docker-hub-tag-sample.json`
  copied verbatim into a raw string literal constant (the stack has no test data folder) and expects its 16 digests in
  document order and the top-level index digest unchanged. `…SkipsInvalidPlatformDigest` uses one entry without
  `digest`, one whose digest breaks the grammar, and one `null`, one number and one string entry; it expects
  *succeeded* with the top-level digest unchanged and only the valid digests.

## Approach

**Retry.** Add `HttpClientName` to the provider; register `services.AddHttpClient(DockerScoutVulnerabilityProvider.HttpClientName).AddHttpMessageHandler<TransientHttpRetryHandler>()`
beside the Portainer client; the provider calls `CreateClient(HttpClientName)` and keeps setting `client.Timeout` per
call from the current options, so a request's timeout covers its retries and waits. The 401 re-login, token cache, login
lock, request slot (now held through the login's retries, as for the Docker Hub client) and 50-page cap stay.

**Digest check.** In `GetVulnerabilitiesAsync`, after the credential, registry and path checks and inside the existing
`try`, when `imageReference.Digest` is not blank: create a scope from the injected `IServiceScopeFactory`, resolve
`IRegistryMetadataService`, call `GetTagAsync(imageReference, cancellationToken: cancellationToken)` and dispose the
scope after the call. Then:

- *succeeded* with `Data.Digest` or an entry of `Data.PlatformDigests` equal to the reference's digest
  (`StringComparison.OrdinalIgnoreCase`) → continue with the login and lookup as today;
- *succeeded* otherwise (including `Data` null or no digest at all) → log `DockerScoutTagDigestMismatch` and return
  *not found* with the AC5 message (built from `FullReference`, no remote text);
- *not found* → *not found* with the registry's message;
- every other status → *failed* with the registry's message, or `Tag digest lookup failed for '<FullReference>'` when
  it is null.

A cancellation still propagates; any other exception falls into the existing `catch` (`Docker Scout scan failed: <fixed reason>`).

**Platform digests.** `DockerHubTagData` gets `PlatformDigests`, marked `[JsonIgnore]` so `MetadataJson` keeps its
size. Only `DockerHubClient.GetTagAsync` fills it (a private helper reading `images` when it is an array; it skips
every element whose `ValueKind` is not `JsonValueKind.Object`, as `DockerHubClient.cs:759,769-771` do, because
`TryGetProperty` throws on other kinds; each object's `digest` goes through `ValidDigestOrNull`); `CreateTagData` for listings stays as it is, so listing pages hold no
extra strings. No new limit: the tag record is parsed whole by `RemoteJsonDocument` as today.

No new user-visible output besides the AC5 and AC7 messages, which appear in the existing assessment message and scan
run message.

## Options considered

Reasons in decision 0031: a Docker Scout digest endpoint (undocumented, option 1); the check in
`VulnerabilityEnrichmentService`, the issue's preference (repeats Docker Scout's preconditions, option 3); the check
after Docker Scout answered (reads the wrong advisories, option 4); index digest only (option 5). Storing the platform
digests in `MetadataJson` lost to its 4000-character column (fact 14).

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Vulnerabilities/DockerScoutVulnerabilityProvider.cs` | named client, scope factory, digest check |
| DockerUpdateGuard | `Vulnerabilities/DockerScoutVulnerabilityProviderLogging.cs` | event 3408 |
| DockerUpdateGuard | `DockerHub/DockerHubTagData.cs` | `PlatformDigests` |
| DockerUpdateGuard | `DockerHub/DockerHubClient.cs` | read `images[].digest` in `GetTagAsync` |
| DockerUpdateGuard | `ServiceCollectionExtensions.cs` | named client registration |

## Signatures (for the Dev's skeleton)

- `public const string HttpClientName = "DockerScout";` in `DockerScoutVulnerabilityProvider` (region `Constants`)
- `public DockerScoutVulnerabilityProvider(IHttpClientFactory httpClientFactory, IOptionsMonitor<DockerUpdateGuardOptions> optionsMonitor, ILogger<DockerScoutVulnerabilityProvider> logger, LoginRetryAfterRegistry loginRetryAfterRegistry, DockerHubRequestLimiter requestLimiter, IServiceScopeFactory serviceScopeFactory)` — replaces the five-parameter constructor
- `[JsonIgnore] public IReadOnlyList<string> PlatformDigests { get; set; } = [];` in `DockerHubTagData`
- `public static partial void DockerScoutTagDigestMismatch(this ILogger logger, string imageReference);` in `DockerScoutVulnerabilityProviderLogging`, `EventId = 3408`, `LogLevel.Information`, message `Docker Scout skipped {ImageReference}: its tag now points to a different digest`

## Test files

`src/Tests/DockerUpdateGuard.Tests/DockerScoutVulnerabilityProviderTests.cs`,
`src/Tests/DockerUpdateGuard.Tests/ServiceCollectionExtensionsTests.cs`,
`src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs`, `src/Tests/DockerUpdateGuard.Tests/DockerHubTagDataTests.cs` (new).

Existing call sites:

- The constructor change breaks `DockerScoutVulnerabilityProviderTests.CreateProvider` (line 1870) and line 609,
  `DockerScoutTokenExpiryTests.cs:159` and `VulnerabilityProviderResolverTests.cs:84-88`: the **Dev** adds
  `Substitute.For<IServiceScopeFactory>()` in step 4, mechanically.
- The factory substitutes at `DockerScoutVulnerabilityProviderTests.cs:607,1863,1867` answer only `CreateClient()`
  (the default name), so every existing test would get no client once the provider asks for the named one: the
  **Tester** changes them to `CreateClient(Arg.Any<string>())` in step 5 (works before and after the change). No
  existing test uses a reference with a digest, so none needs a tag lookup.

Files no unit test can cover: none.

## Areas

- Vulnerabilities: the Docker Scout section (retry, digest check, the new outcomes, timeout per request with retries),
  the *Unavailable* row of the assessment table, *Known gaps*, *Related decisions* (0031).
- Integration failures: the retry applies to Docker Scout; the Docker Scout login among the non-idempotent requests.
- Registries: the tag lookup reads platform digests; the Docker Scout login slot covers its retries.

## Documentation updates

All by the Dev, after the code is green:

- `docs/areas/vulnerabilities.md`, *Docker Scout*: replace "by tag; the digest is not used" with the digest check
  (order after the path check, match rule, AC5/AC7 outcomes, one Docker Hub web API request per lookup with a digest,
  which takes a Docker Hub request slot and is limited by `DockerHub:RequestTimeoutSeconds`, linking
  [Registries](registries.md#request-timeout), not by `Vulnerabilities:RequestTimeoutSeconds`); the slot sentence "Lookups with a cached token and the Docker Scout API
  requests take no slot" names the tag lookup; "Each HTTP request is limited to `RequestTimeoutSeconds`" becomes
  "…, its retries and waits included", with a link to the retry rules; the login is retried as a non-idempotent
  request. Assessment table, *Unavailable*: add "or, for Docker Scout, the tag now points to another digest". Remove
  the #267 entry from *Known gaps*; add 0031 to *Related decisions*. `:197-198` (*Limits and errors*) reads:
  "Messages are built from the image reference and the provider's error output (Trivy's excerpt, or for Docker
  Scout the failure message of Docker Hub's tag lookup with its body excerpt,
  [Integration failures](integration-failures.md#excerpts-of-remote-text)) or a fixed reason; no tokens or
  passwords are put into them."
- `docs/areas/integration-failures.md:101-104`: Docker Scout (login and vulnerability pages) joins the list and leaves
  the "does not apply" sentence; `:118-119`: the Docker Scout login joins "The requests concerned".
- `docs/areas/registries.md:65-66` (*Tag lookup*, Docker Hub): the platform image digests of the tag record are read
  (validated like the digest), used only by Docker Scout's digest check and not stored; `:204-206`: the Docker Scout
  login slot covers its retries and `Retry-After` waits.
- `docs/ARCHITECTURE.md:233` (client table row): Docker Scout uses the named client and checks the tag's digest through
  `IRegistryMetadataService` in a scope of its own (link 0031); `:243-246`: only the Docker Engine client creates its
  `HttpClient`s without the retry handler.
- `README.md:151` (`Vulnerabilities:RequestTimeoutSeconds`): "Timeout for vulnerability provider requests; for Docker
  Scout, per login or page request with its retries".

Sweep (`grep -i scout` over `docs/`, `README.md`, `INSTALL.md`): the hits not listed stay true.

## Architecture check

Layering holds: everything stays in the host project. The singleton provider resolves a scoped service only inside a
scope it creates and disposes per lookup, as the background services do; no scoped instance is captured. The guarantee
"integration calls answer expected failures with an outcome and a fixed reason" holds: the new outcomes carry fixed
text or the registry's message, which `DockerHubClient` already builds under decision 0009/0029. Update evaluation
stays free of I/O. No configuration key, entity or migration changes.

## Security considerations

- [x] No limits: the platform digests are read from a document `RemoteJsonDocument` already parses whole; each digest
  passes `ValidDigestOrNull` (grammar, at most 255 characters) before it is kept.
- [x] Exceptions reaching a message: none new. The AC5 message holds only `FullReference` (validated reference); the
  AC7 message is the registry's, built by `DockerHubClient` from fixed text and `ResponseBodyExcerpt`; other exceptions
  become `Docker Scout scan failed: <fixed reason>` as today.
- The PAT is resent only under the handler's non-idempotent rule, to the same URL; the tag lookup uses `DockerHubClient`
  with its token cache, limiter and path validation.

## Decision records

- `docs/decisions/0031-docker-scout-assesses-a-digest-only-while-its-tag-still-points-to-it.md` (Proposed)

## Out of scope / follow-ups

- Follow-up issue: "Vulnerability assessment source is read from live options instead of the resolved provider" —
  `VulnerabilityEnrichmentService.cs:260,343,364,448` map the source from `_optionsMonitor.CurrentValue.Vulnerabilities.Provider`;
  a configuration reload during a refresh labels assessments with a provider that did not produce them. Expected: the
  source comes from the provider resolved at the start of the refresh.
- A Docker Scout digest endpoint (0031, *Consequences*).
- The shared User-Agent on the Docker Scout client.

## Challenge

1. minor — no test pins the order of the checks. Accepted: AC8 now covers a digest-bearing reference that the
   credentials, registry or path check answers first, with three tests asserting the existing outcome and no
   `CreateScope` call.
2. minor — error path and scope disposal untested. Accepted: AC5 gains the exception case (`…WithDigestTagLookupThrowsReturnsFailed`)
   and scope disposal; the test notes name the `GetService(Type)` stub.
3. minor — the tag lookup's timeout is not documented. Accepted: the `vulnerabilities.md` edit states that it is bound by
   `DockerHub:RequestTimeoutSeconds` with a link to Registries; the README line names only the login and page requests.
4. minor — AC1's chain test technique. Accepted: the test notes name the `HttpClientFactoryOptions` technique.

## Security review

1. BLOCKING — non-object `images[]` entries make `TryGetProperty` throw and every Docker Hub tag lookup fail.
   Resolved: *Approach* skips every element that is not a JSON object, AC9 says so, and
   `DockerHubClientGetTagAsyncSkipsInvalidPlatformDigest` adds a `null`, a number and a string entry expecting
   *succeeded*. Re-check of the accepted forms: `images` absent or not an array → empty list; element not an object →
   skipped; object without `digest`, or `digest` not a string (`TryGetString` returns null) → skipped; string outside the
   digest grammar or over 255 characters → skipped (`ValidDigestOrNull`); valid digest → kept, in document order.
2. non-blocking — `vulnerabilities.md:197-198` goes stale. Fixed now: the stated sentence is in *Documentation updates*.
