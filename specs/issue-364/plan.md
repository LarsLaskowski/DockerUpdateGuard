# Plan: Check the registry host of a registered image reference

Source: Issue #364
Status: Draft
Tier: security — the change creates a guard that confines the OCI client's requests to a registry that is a host with an optional port, keeping the path of the request where the client composed it (`.squad/routing.md`, *Tiers*: a check that confines what a URL reaches).
Security areas touched: Registry and Docker Hub calls (`OciRegistryClient`, `DockerHubClient.IsSupportedRegistry`)

## Problem / root cause

`ImageReferenceParser.Parse` (`src/DockerUpdateGuard/Images/ImageReferenceParser.cs:135-138`) takes the first segment
of a reference as the registry whenever it contains a `.` or `:` or is `localhost` (`LooksLikeRegistry`, line 43), with
no further check, and `ImageRegistrationService.RegisterAsync` (`ImageRegistrationService.cs:77-89`) stores it as is.
The observed-image scan formats and re-parses the stored reference (`ImageScanOrchestrator.cs:724`) and hands it to the
OCI client, whose `CreateRegistryUri` (`src/DockerUpdateGuard/Images/OciRegistryClient.cs:453-476`) uses the registry
as an absolute base URL when `Uri.TryCreate(registry, UriKind.Absolute)` accepts it (line 459) and otherwise as the
host of `new UriBuilder(Uri.UriSchemeHttps, registry)` (line 469). Neither branch checks the value. The dot-segment
guard of #360 (`CreateManifestUri`/`CreateBlobUri`/`CreateTagsUri`, lines 394-445) checks repository, tag and digest only.

Measured (scratch programs, .NET SDK 10.0.403, the two branches of `CreateRegistryUri` copied verbatim, sent through a
real `HttpClient`/`SocketsHttpHandler`):

- `.`, `..`, `...`, `.a`, `a..b`, `http:`, `http:evil.com`, `file:x`, `a:b`, `:5000`, `a%2eb`, `a b.c`, `evil.com\x`,
  `[::1` and `127.0.0.1:5000`: `UriBuilder.Uri` throws `UriFormatException`. Nothing catches it in `GetTagAsync`,
  `GetTagsAsync` or `GetImageConfigurationAsync`, so the observed-image scan ends *failed* with "Observed image scan
  failed: unexpected error" and an error log (`ImageScanOrchestrator.cs:765-772`); `ResolveBaseImagesAsync` turns it
  into *failed* (line 1372-1379). No request is sent and nothing resolves relatively.
- `a.`, `-a.com`, `a_b.com`, `ex\u00e4mple.com`: requested as that host.
- `evil.com?x`, `evil.com#x`: requested from `evil.com` with the path replaced by `/` and the composed path moved into
  the query (`https://evil.com/?x/v2/foo/manifests/latest`) or the fragment — the request path escapes its composition.
- `mailto:x`, `urn:x`, `user:pw`, `localhost:abc`, `localhost:99999`, `evil.com:5000:6000`: parsed as absolute URIs of
  another scheme; `SocketsHttpHandler` throws `NotSupportedException` ("The 'user' scheme is not supported").

Related defect found on the way, same function: **every registry of a host name with a port is broken.**
`localhost:5000`, `registry.example.com:5000`, `harbor.local:8443` pass line 459 as absolute URIs whose *scheme* is the
host name (`Uri.Scheme == "localhost"`, empty host), so every lookup throws `NotSupportedException`; `127.0.0.1:5000`
throws `UriFormatException`; `NormalizeRegistry` (line 513-523) turns `localhost:5000` into the empty string for the tag
listing. Only bracketed IPv6 with a port works today. The area document claims the opposite
(`docs/areas/registries.md:92-93`, "keep a non-default port"). The fix below repairs it by construction.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | Issue: a registered registry part `.`, `..` or another non-host value becomes the host of the OCI request URI built with `UriBuilder` | `ImageReferenceParser.cs:43-50,135-138`; `OciRegistryClient.cs:469`; `Parse("./app:1")` gives registry `.` by reading | confirmed |
| 2 | Issue: #360 does not cover the registry | `OciRegistryClient.cs:396-399,414-416,432` check repository, reference and digest only | confirmed |
| 3 | Issue (asked): `UriBuilder`/`Uri` with such a host gives an exception | measured: `UriFormatException` from `UriBuilder.Uri` for `.`, `..` and the forms listed under *Problem / root cause*; `a.`, `evil.com?x` and others do not throw | confirmed |
| 3a | Issue (asked): such a host leads to a relative resolution | measured: no form resolves relatively; every non-throwing form is requested from a host (or refused by the handler for its scheme) | refuted |
| 4 | The exception escapes three public lookups and fails the observed-image scan as "unexpected error" | `OciRegistryClient.cs:849,1279-1280` (no try); `ImageScanOrchestrator.cs:765-772`; `FailureReason` maps `UriFormatException` to `unexpected error` (not Reportable, HTTP, IO, JSON or cancellation) | confirmed |
| 5 | `?` and `#` in the registry move the composed path into the query or fragment of a request to the named host | measured: `https://evil.com/?x/v2/foo/manifests/latest`, `https://evil.com/#x/v2/foo/manifests/latest` | confirmed |
| 6 | A registry `host:port` with a host name is parsed as an absolute URI of scheme `host`; the real handler refuses it; IPv4 with port throws | measured: `localhost:5000` → scheme `localhost`, `NotSupportedException: The 'localhost' scheme is not supported.`; `127.0.0.1:5000` → `UriFormatException`; `NormalizeRegistry("localhost:5000")` → `""` | confirmed |
| 7 | For every grammar-valid registry, `Uri.TryCreate("https://" + registry + "/")` keeps host and port; it fails for a port above 65535 and for a bracketed non-IPv6 value | measured: `localhost:5000`, `registry.example.com:5000`, `127.0.0.1:5000`, `[::1]`, `[::1]:5000`, `localhost:65535` succeed; `localhost:99999`, `[a]`, `[::::::::::]`, `[1:2:3:4:5:6:7:8:9]` fail | confirmed |
| 8 | `new UriBuilder(baseUri) { Path = path, Query = query }.Uri` gives the same URI as today's `new UriBuilder("https", registry) { Path, Query }` for hosts without a port, so existing request URIs stay unchanged | measured on 5 paths incl. `%252E%252E`, `sha256%3Aabc`, `x%20y`, query `n=100&last=1.0%2B2`: identical `AbsoluteUri` and `Authority` | confirmed |
| 9 | `RegistryReferenceValidator.IsValidRegistry` is Docker's host grammar (DNS labels or bracketed hex/colons, optional digits port), ASCII only, at most 200 characters | `RegistryReferenceValidator.cs:71,106-111` | confirmed |
| 10 | No production source hands the OCI client a registry in absolute-URL form | parser splits at `/` (`ImageReferenceParser.cs:120`); remote references pass `ValidReferenceOrNull` (`BaseImageLabelReader.cs:42`, `RuntimeContainerScanOrchestrator.cs:590`); Docker Hub URLs go to `DockerHubClient` (`CanHandle`, line 1216-1220); no OCI test uses one (grep `Registry = "http` in `src/Tests`) | confirmed |
| 11a | `DockerHubClient.IsSupportedRegistry`, called by both clients' `CanHandle`, threw `ArgumentException` for a value that parses as an absolute URI without a host (`localhost:5000`, `user:pw`, `localhost:`, `localhost:abc`, `localhost:99999`, `evil.com:5000:6000`): it passed the empty `Uri.Host` to `IsSupportedDockerHubHost`, which calls `ThrowIfNullOrWhiteSpace` | found by the Dev in step 6, `DockerHubClient.cs:374,389`; fixed by taking the absolute-URI branch only for a non-empty host | confirmed |
| 11 | Only the OCI client puts a registered registry into a request URI | `DockerHubClient` serves fixed hosts; `DockerScoutVulnerabilityProvider.cs:439` docker.io only; Trivy passes it as a CLI argument (`TrivyVulnerabilityProvider.cs:218-231`) | confirmed |
| 12 | Record 0014 is unreleased | `git ls-remote --tags origin`: latest `v1.3.7`; `git tag --contains 4477ce7` (commit adding 0014) is empty | confirmed |

## Acceptance criteria

The refusal is decided in `CreateRegistryUri` (returns `null`) and reported by `CreateManifestUri`, `CreateBlobUri`
and `CreateTagsUri` through their `refusalMessage`; the registry is checked before the path.

- [ ] AC1: `OciRegistryClient.GetTagAsync` for a reference whose registry is any of the non-host forms below answers
  *not found* with the message `RegistryReferenceValidator.InvalidRegistryMessage` (exactly
  `Registry is not a valid host with an optional port`), sends no request (no token request either) and logs nothing:
  `.`, `..`, `...`, `.a`, `a..b`, `a.`, `-a.com`, `a_b.com`, `http:`, `http:evil.com`, `mailto:x`, `user:pw`,
  `localhost:`, `localhost:abc`, `localhost:99999`, `localhost:0`, `evil.com:5000:6000`, `evil.com?x`, `evil.com#x`, `evil.com\x`
  (one backslash), `evil.com%2Fx`, `a b.com`, `ex\u00e4mple.com` (the escape stands for U+00E4), `[::1`, `[a]`,
  `https://ghcr.io`.
- [ ] AC2: For registry `.`, `..`, `evil.com?x` and `localhost:99999`, `GetTagsAsync` and `GetImageConfigurationAsync`
  answer *not found* with `InvalidRegistryMessage`, and `ResolveBaseImagesAsync` succeeds with no base images; none of
  them sends a request or logs.
- [ ] AC3: A registry with a port is addressed with its host and port: `GetTagAsync` for registry `localhost:5000`,
  `registry.example.com:5000`, `127.0.0.1:5000` and `[::1]:5000`, repository `corp/api`, tag `1.0` requests
  exactly `https://<registry>/v2/corp/api/manifests/1.0` and succeeds; `GetTagsAsync("Registry.Example.com:5000",
  "corp/api")` requests `https://registry.example.com:5000/v2/corp/api/tags/list?n=100` and succeeds.

Registries without a port keep their request URIs: the existing `OciRegistryClientTests` (all on
`mcr.microsoft.com`, exact URIs) stay unchanged and green.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `OciRegistryClientGetTagAsyncInvalidRegistryReturnsNotFoundWithoutRequestAsync(string registry)` — one `DataRow` per form of AC1 (the `localhost:0` row added by the Tester), C# literals `"evil.com\\x"` and `"ex\u00e4mple.com"` | `src/Tests/DockerUpdateGuard.Tests/OciRegistryClientTests.cs` |
| AC2 | `OciRegistryClientGetTagsAsyncInvalidRegistryReturnsNotFoundWithoutRequestAsync(string registry)`, `OciRegistryClientGetImageConfigurationAsyncInvalidRegistryReturnsNotFoundWithoutRequestAsync(string registry)`, `OciRegistryClientResolveBaseImagesAsyncInvalidRegistryReturnsNoBaseImagesWithoutRequestAsync(string registry)` — `DataRow`s of AC2 | `src/Tests/DockerUpdateGuard.Tests/OciRegistryClientTests.cs` |
| AC3 | `OciRegistryClientGetTagAsyncRegistryWithPortRequestsHostAndPortAsync(string registry)` (4 `DataRow`s), `OciRegistryClientGetTagsAsyncRegistryWithPortKeepsPortAsync` (tag list `{"name":"corp/api","tags":[]}`) | `src/Tests/DockerUpdateGuard.Tests/OciRegistryClientTests.cs` |

Every test asserts on the recording `SequenceHttpMessageHandler` (`handler.Requests`) and `TestLogger` as the
dot-segment tests (`AssertDotSegmentRefusal`, `OciRegistryClientTests.cs:1380`) do. All fail on the current code: the
dot forms throw, `a.`/`evil.com?x`/`https://ghcr.io`/`localhost:99999` send a request, `localhost:5000` is requested as
`localhost:5000/v2/…`, `127.0.0.1:5000` throws, the tag listing of `registry.example.com:5000` loses its host;
`ResolveBaseImagesAsync` answers *failed*. Escape check (*Escapes in files*): if `"evil.com\\x"` lost its backslash it
would be `evil.comx`, a valid host that is requested, and the AC1 test fails for that row. The non-ASCII case is
written as the C# escape `\u00e4`, not as the letter.

## Approach

1. `RegistryReferenceValidator`: add the constant `InvalidRegistryMessage = "Registry is not a valid host with an
   optional port"` beside `DotSegmentMessage`.
2. `OciRegistryClient.CreateRegistryUri`: return `null` unless `RegistryReferenceValidator.IsValidRegistry(registry)`
   and `Uri.TryCreate($"https://{registry}/", UriKind.Absolute, out var baseUri)` both hold and `baseUri.Port` is not 0;
   otherwise return
   `new UriBuilder(baseUri) { Path = path, Query = query }.Uri` (path/query split unchanged). The absolute-URL branch
   (line 459-467) and `EnsureTrailingSlash` (line 483-493, then unused) are removed.
3. `CreateManifestUri`, `CreateBlobUri`, `CreateTagsUri` get `out string refusalMessage`: the existing dot-segment
   check stays first and sets `DotSegmentMessage`; then `CreateRegistryUri` returning `null` sets
   `InvalidRegistryMessage`; on success `string.Empty`. (A reference with both defects gets the dot-segment message;
   no criterion depends on the order.)
4. Callers use the message: `GetManifestMetadataAsync` (line 851-854) and `GetImageConfigurationAsync` (line
   1417-1420) return `NotFound(refusalMessage)`; `GetTagsAsync` (line 1282-1285) likewise; `ExtractBaseImageFromConfigAsync`
   (line 959-962) keeps returning `null`, so the base-image chain ends as for a dot segment.
5. `NormalizeRegistry`: drop the absolute-URI branch; return `registry.Trim().ToLowerInvariant()` (keeps a port).
6. `DockerHubClient.IsSupportedRegistry` (plan deviation, step 6): use the absolute-URI branch only when the parsed
   `Host` is non-empty, otherwise compare the value as a plain host, so `CanHandle` no longer throws for `localhost:5000`
   and the other host-less URI forms of fact 11a and the OCI guard is reached. Which hosts Docker Hub serves is unchanged.
7. No log entry, no change to the parser, registration or Docker Scout.

No new user-visible output beyond the fixed message, which appears where the dot-segment message does today (the scan
run's message of an observed image, the update assessment message).

## Options considered

Recorded in [0014](../../docs/decisions/0014-registry-tag-names-and-digests-are-validated-where-they-are-read.md),
options 20-24: check where the OCI URI is composed (chosen); reject at registration (leaves stored references, changes
what can be registered, form checks are #343); decline in `CanHandle` as *unsupported* (echoes the raw registry in the
dispatcher message, base-image chain bypasses it); keep the absolute-URL form for `http`/`https` (no source produces
it, kept a plain-HTTP path and caused the port defect); catch `UriFormatException` as *failed* (misses the forms that
do not throw and the port defect).

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Images/Helper/RegistryReferenceValidator.cs` | new constant `InvalidRegistryMessage` |
| DockerUpdateGuard | `Images/OciRegistryClient.cs` | `CreateRegistryUri` guard (port 0 refused), three builders report the refusal, callers use it, `NormalizeRegistry` keeps the port, `EnsureTrailingSlash` removed |
| DockerUpdateGuard | `DockerHub/DockerHubClient.cs` | `IsSupportedRegistry`: absolute-URI branch only for a non-empty `Host` (one condition) |
| DockerUpdateGuard.Tests | `OciRegistryClientTests.cs` | tests of AC1-AC3 |

## Signatures (for the Dev's skeleton)

```csharp
// RegistryReferenceValidator (Const fields region)
public const string InvalidRegistryMessage = "Registry is not a valid host with an optional port";

// OciRegistryClient (Static methods region, all private)
private static Uri? CreateRegistryUri(string registry, string relativePath);                                   // was Uri
private static Uri? CreateManifestUri(string registry, string repository, string reference, out string refusalMessage);
private static Uri? CreateBlobUri(string registry, string repository, string digest, out string refusalMessage);
private static Uri? CreateTagsUri(string registry, string repository, string? lastTag, out string refusalMessage);
// removed: private static Uri EnsureTrailingSlash(Uri uri)
```

The skeleton adds the constant to `RegistryReferenceValidator.cs` only (read: the const region is final apart from
it); the private members of `OciRegistryClient.cs` change in step 6.

## Test files

`src/Tests/DockerUpdateGuard.Tests/OciRegistryClientTests.cs` (existing class `OciRegistryClientTests`).

Existing test code that calls a changed signature: none — the changed members are private; the constant is new.
Existing tests kept unchanged: the dot-segment tests use registry `mcr.microsoft.com` (valid), so their message stays
`DotSegmentMessage`; no other test passes a registry with a port or an absolute URL.

Files no unit test can cover: none.

## Areas

- `docs/areas/registries.md` — *Choice of client per registry*: the OCI client addresses the registry as host and
  optional port, the absolute-URL sentence goes; *Request paths*: a bullet on the registry check (grammar, 200
  characters, ports 1-65535 reachable and port 0 refused, checked before the path, the message, base-image chain ends and succeeds);
  *Limits and errors*, *Not found*: add the invalid registry.
- `docs/areas/image-catalog.md` — *Image references*, last sentence on user-entered references: registry requests also
  refuse a registry that is not a host with an optional port.

## Documentation updates

Owner: Dev, after the code is green, against the code.

- `docs/areas/registries.md` lines 92-93, the *Request paths* section (lines 96-114) and line 279-280, as under *Areas*.
- `docs/areas/image-catalog.md` lines 33-34, as under *Areas*.
- `docs/ARCHITECTURE.md` line 430-432 (security posture bullet on remote names): the OCI request builder also refuses
  a registry that is not a host with an optional port, linking record 0014.
- Sweep done: `grep -n "absolute URL|dot segment|registry host|non-default port"` over `docs/ARCHITECTURE.md`,
  `docs/areas/*.md`, `README.md`, `SECURITY.md`, `.squad/project.md`; the hits above are the ones the change makes
  untrue. `registries.md:87` (Docker Hub matches an absolute URL by host) and `:306` (`DockerHub:Registry`) concern the
  Docker Hub client and stay; `registries.md` *Request paths* says the Docker Hub client matches an absolute URL by its host. `vulnerabilities.md:131` (Docker Scout, docker.io only) stays.

## Architecture check

The security posture in `docs/ARCHITECTURE.md` (remote names held to their grammars, request builders refusing dot
segments, record 0014) is extended to the registry: host and path of an OCI request are composed only from checked
parts. No guarantee of `.squad/project.md` is weakened. No startup, composition-root, data or pagination change; the
pagination origin of a tag listing now includes a port where the registry has one (`PaginationLinkGuard` compares the
origin of the first request, unchanged).

## Security considerations

- [ ] Limits: the registry is at most 200 characters (`MaxRegistryLength`), counted on the stored string, before any
  URI parsing; no decoding happens.
- [ ] Exceptions reaching a user-visible message: none new. The `UriFormatException` of today ("unexpected error") and
  `NotSupportedException` disappear for these inputs; the user sees the fixed `InvalidRegistryMessage`.
- [ ] Guard forms: the consumer is `System.Uri`; the forms it accepts or rejects for a host are the measured lists in
  *Problem / root cause* and *Verified facts* 6-7. Every form either fails the grammar or the `https://<registry>/`
  parse (refused) or is a DNS name, IPv4 or bracketed IPv6 with an optional port in 1-65535 (addressed as that host); port 0 is refused.
  Numeric shorthands such as `0x7f.1` match the grammar and reach `127.0.0.1` — the user names that host; unchanged
  by design.

## Decision records

- `docs/decisions/0014-registry-tag-names-and-digests-are-validated-where-they-are-read.md` (unreleased, extended in
  place: source #364, context paragraph (port 0 and the `CanHandle` exception included), options 20-24, decision
  sentence, one consequence; status and index row stay
  `Accepted`).

## Out of scope / follow-ups

- Follow-up issue for the orchestrator — title: "Trivy scan target can begin with a dash". Body: "`TrivyVulnerabilityProvider.GetScanTarget`
  puts the registered registry/repository first in the Trivy argument list (`BuildArguments`, no `--` before the
  target). A registered reference such as `--config=x.y/z` gives the argument `--config=x.y/z:latest`, which Trivy reads
  as an option, not as the image. Unverified what Trivy then does (likely an argument-count error); pass `--` before the
  target or refuse a target that begins with `-`. Found while planning #364." Security area: External process execution.
- Checking the registration form against the reference grammar: #343.
- Transport errors escaping as errors: #272.


## Challenge notes

1. **minor:** Port 0 passes both checks. The plan's own wording says "port at most 65535" in *Areas* and "0-65535" in *Security considerations*, which don't match. I measured `localhost:0`: it matches the grammar, `Uri.TryCreate("https://localhost:0/")` succeeds, and the request goes to `https://localhost:0/v2/…`. The connection then fails as a transport error (#272), not as *not found*. Leading zeros are normalized (`localhost:05000` becomes port 5000). Instruction for the Dev: either refuse port 0 in `CreateRegistryUri` (`baseUri.Port == 0`) and add `localhost:0` as an AC1 row, or say explicitly in `docs/areas/registries.md` that ports 0-65535 are accepted. Either way the *Areas* and *Security considerations* wording must agree. Why minor: it's a detail the Dev can settle without changing the plan, and it opens no path escape.

2. **minor:** No test row covers the 200-character limit, even though *Security considerations* claims it as a limit. Every AC1 form fails for another reason, so a broken length check would go unnoticed. Instruction for the Tester: add an AC1 `DataRow` with a registry that matches the grammar and is 201 characters long (for example 50 four-character labels such as `abcd.` repeated, trimmed so the total is exactly 201). It must answer *not found* with `InvalidRegistryMessage` and send no request. Why minor: it adds one test row and changes nothing in the approach.

## Security notes

1. Fixing the port bug means a registered reference can now reach any host:port over HTTPS. Before, only bracketed IPv6 with a port worked; host names and IPv4 with a port failed. This is intended, since the area document already promises non-default ports, and the risk is limited: every request is HTTPS and sends no stored credentials. Still, the planned bullet in `docs/areas/registries.md` under "Request paths" should say plainly that any port from 0 to 65535 on the named host is reachable. That way the area document records it as behavior, not just as a format rule.
2. The follow-up issue "Trivy scan target can begin with a dash" is a good catch and is correctly kept out of scope. The orchestrator should file it as planned.
