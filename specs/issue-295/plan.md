# Plan: Credentials embedded in configured URLs leak into logs, error messages and the UI

Source: Issue #295
Status: Draft
Tier: security — the change adds a redaction control for secrets in configuration (user info of configured URLs) to logs, messages, stored values and the UI.
Security areas touched: Secrets in configuration; Logging and telemetry of external data; Registry and Docker Hub calls (`TransientHttpRetryHandler`); Container actions (`PortainerClient`); Docker Engine access (`DockerInstanceClient`)

## Problem / root cause

Every output site passes the configured URL string (or a URI that still holds its user info) straight into the text
(all paths under `src/DockerUpdateGuard/`):

- `Infrastructure/TransientHttpRetryHandler.cs:131` — `GetLeftPart(UriPartial.Path)` keeps the user info; used by
  events `3500`/`3501`.
- `Docker/DockerInstanceClient.cs:983` (event `3101`) and `:985`, `:1063`, `:1152`, `:1189`, `:1223` (`Unsupported`
  messages) use `instanceOptions.BaseUrl`.
- `Docker/DockerInstanceClient.cs:474` — `GetNamedPipeName` throws with `'{pipeUri}'`; **not in the issue** (fact 9).
- `Images/InstanceDiscoveryService.cs:109` (stored `EndpointUri`), `:118` (event `2091`), `:155` (stored Portainer
  `BaseUrl`); the disabled-Portainer branch `:131-140` keeps an earlier stored `BaseUrl` (fact 11).
- `Portainer/PortainerClient.cs:166`, `:173` (event `3303`), `:168`, `:175` (exception messages, which become the
  capability/action `Message` at `:271`, `:364` and the exception of events `3306`/`3310`).
- `UI/ApplicationViewService.cs:2101`, `:2148` map the stored `EndpointUri` unmasked to the view data.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | `GetLeftPart(UriPartial.Path)` keeps user info; `Uri.UserInfo` of `tcp://user:secret@docker.example:2376` is `user:secret` | Scratch probe (.NET 10.0.402): `https://user:secret@portainer.example:9443/api/auth?x=1` → `https://user:secret@portainer.example:9443/api/auth`; tcp `UserInfo=[user:secret]` | confirmed |
| 2 | Validation accepts URLs with user info, and only parseable absolute URIs | `Configuration/DockerUpdateGuardOptionsValidator.cs:299-305`, `:327-339`, `:362-374` (`Uri.TryCreate` + scheme check only) | confirmed |
| 3 | The unsupported-endpoint branches are practically unreachable in production: the validator admits only `http`, `https`, `tcp`, `unix`, `npipe`, exactly the schemes `TryCreateEngineUri` accepts (`DockerInstanceClient.cs:399-436`); options are validated on start and on every rebuild | `ServiceCollectionExtensions.cs:53-56` (`IValidateOptions` + `ValidateOnStart`); the issue's "the unsupported-endpoint paths log exactly such [unparseable] strings" is therefore refuted for production, the masking there is defense in depth | refuted |
| 4 | Issue reproduction "a transient 5xx from [a Docker instance] logs `… GET https://user:S3cret@docker.example:2376/…`" | The Docker Engine `HttpClient` is built without the retry handler (`DockerInstanceClient.cs:281-311`, `new HttpClient(handler)`); the handler is attached only to Docker Hub, OCI registry, release metadata and Portainer clients (`ServiceCollectionExtensions.cs:72-101`). The retry leak is real for those clients' URLs (Portainer `BaseUrl`, `DockerHub:ApiBaseUrl`, release metadata URLs) | refuted |
| 5 | `PortainerClient` has no production caller today, so the Portainer leak is latent | `grep IPortainerClient` finds only the registration `ServiceCollectionExtensions.cs:104`; `docs/areas/container-actions.md` *Known gaps* (#280) | confirmed |
| 6 | Issue: "rows of instances no longer in configuration, which synchronization does not update" | Synchronization **deletes** those rows (`InstanceDiscoveryService.cs:192-197`) and runs at startup before requests are served (`ApplicationInitializationExtensions.cs:47`); unmasked rows survive only until the first synchronization after the upgrade, or a failed one. Display masking stays as defense in depth | refuted |
| 7 | Neither `DockerInstance.EndpointUri` nor `PortainerEndpoint.BaseUrl` is used for connecting | `grep EndpointUri` / `.BaseUrl`: written only at `InstanceDiscoveryService.cs:109,155`, read only at `ApplicationViewService.cs:2101,2148`; clients use the options | confirmed |
| 8 | `url.full` of HTTP client spans carries no user info: on .NET 9+ OpenTelemetry.Instrumentation.Http 1.20.1 takes the runtime's `System.Net.Http` activity, whose `url.full` is `UriRedactionHelper.GetRedactedUriString` = scheme, host, port, path, query values as `*` | Source read: contrib tag `Instrumentation.Http-1.20.1` `HttpHandlerDiagnosticListener.cs:141-150` (sets tags only below .NET 9); dotnet/runtime `release/10.0` `DiagnosticsHandler.cs:132`, `UriRedactionHelper.cs` (switch `System.Net.Http.DisableUriRedaction` / `DOTNET_SYSTEM_NET_HTTP_DISABLEURIREDACTION` turns it off). The HTTP client factory's log lines use the same helper (`Microsoft.Extensions.Http/src/Logging/LogHelper.cs:73,107`). This settles the issue's `url.full` criterion without a test | confirmed |
| 9 | `npipe://user:secret@pipehost/` passes validation, and discovery fails with a message that contains the user info | Probe: parses, `UserInfo=[user:secret]`, `AbsolutePath=/`; `CreateNamedPipeHttpClient` calls `GetNamedPipeName` eagerly (`DockerInstanceClient.cs:355-360`), which throws at `:474`; caught at the discovery `catch` → `Failed("… failed for '<name>': <message>")` and event `3104` with the exception | confirmed |
| 10 | Runtime HTTP exception messages never contain the request URI | `System.Net.Http` `Strings.resx` (release/10.0): the only `{0}` near a URI is the scheme in `net_http_unsupported_requesturi_scheme`; connect failures use `host:port` (`ConnectHelper.cs:143`) | confirmed |
| 11 | With Portainer disabled, an existing stored endpoint keeps its earlier `BaseUrl` | `InstanceDiscoveryService.cs:131-140` sets only `IsEnabled`/`UpdatedAtUtc` | confirmed |
| 12 | Parser behavior the helper relies on (probe, .NET 10.0.402): two `@` in the authority (`https://user:p@ss@host/path`), a `/`, `?` or `#` in the password, `npipe://user:secret@./pipe/x`, `unix://user:secret@/var/run/docker.sock` and backslash forms do **not** parse; `user:secret@host:2376` parses as scheme `user` with empty `UserInfo` and path `secret@host:2376`; percent-escaped, IPv6 (`[::1]`), upper-case and whitespace-padded forms parse with `UserInfo` set; `GetComponents(Host \| Port \| PathAndQuery \| Fragment, UriEscaped)` omits default ports, keeps `[::1]:2376`, and gives `unix:///var/run/docker.sock` as `/var/run/docker.sock` | Scratch probe in the session scratchpad | confirmed |
| 13 | The test logger records the formatted message only, not the structured `{BaseUrl}` attribute; the attribute carries the same argument | `src/Tests/DockerUpdateGuard.Tests/Data/TestLogger{TCategoryName}.cs:54-68`; source-generated `LoggerMessage` passes one value to both | confirmed |

## Acceptance criteria

The masked form `***` is the constant `UrlRedaction.Mask`. "Contains no secret" means the test's password and user
name strings do not occur in the text (`Assert.DoesNotContain` with a message).

- [ ] AC1: `UrlRedaction.RedactUserInfo(string?)` returns, for each input, exactly:

  | Input | Output | Rule |
  | ----- | ------ | ---- |
  | `null`, `""` | `""` | empty |
  | `unix:///var/run/docker.sock`, `npipe://./pipe/docker_engine`, `https://host.example/path?token=x` | unchanged | no `@` |
  | `https://host.example/@scope/pkg` | unchanged | parsed with authority, no user info |
  | `https://user:secret@portainer.example:9443/api/auth?x=1` | `https://***@portainer.example:9443/api/auth?x=1` | parsed with user info |
  | `https://user@host.example` | `https://***@host.example/` | parsed with user info |
  | `https://:secret@host.example/` | `https://***@host.example/` | parsed with user info |
  | `tcp://user:secret@docker.example:2376` | `tcp://***@docker.example:2376/` | parsed with user info |
  | `https://user:secret@[::1]:2376/` | `https://***@[::1]:2376/` | parsed with user info |
  | `https://user%40x:se%3Acret@host.example` | `https://***@host.example/` | parsed with user info |
  | `https://user:secret@host.example/p#frag` | `https://***@host.example/p#frag` | parsed with user info |
  | `https://user:p@ss@host.example/path` | `https://***@host.example/path` | fallback |
  | `https://user:pa/ss@host.example` | `https://***@host.example` | fallback |
  | `npipe://user:secret@./pipe/docker_engine` | `npipe://***@./pipe/docker_engine` | fallback |
  | `user:secret@host:2376` | `***@host:2376` | fallback (parsed as scheme `user`, no authority) |
  | `user:secret@host?x=http://a` | `***@host?x=http://a` | fallback (`://` only after the `@`) |

- [ ] AC2: `UrlRedaction.RedactUserInfo(Uri?)` returns `""` for `null`, the AC1 result of `AbsoluteUri` for an
  absolute URI (`new Uri("https://user:secret@host.example/a")` → `https://***@host.example/a`) and of
  `OriginalString` for a relative one.
- [ ] AC3: A request to `https://user:secret@registry.example.test/v2/path?token=x` that the retry handler retries
  logs event `3500` (transient status) and event `3501` (transient exception) with
  `https://***@registry.example.test/v2/path` and without `user`, `secret` or `token=x`; the request the inner
  handler receives still carries `user:secret` (connecting is unchanged).
- [ ] AC4: With Portainer `BaseUrl` `https://admin:S3cret@portainer.example.test:9443`, user name/password login and a
  login answer `401`, `GetCapabilityAsync` returns a `Message` and logs events `3303` and `3306` (message and
  exception message) that contain `https://***@portainer.example.test:9443/` and not `S3cret`; the same holds for a
  `200` login answer without JWT.
- [ ] AC5: With `BaseUrl` `ssh://user:secret@docker.example.test` and `Enabled = true`, each of
  `DiscoverContainersAsync`, `CollectContainerResourceUsageAsync`, `InspectImageAsync`, `GetImageHistoryAsync` and
  `GetHostMemoryTotalAsync` returns `Unsupported` with a message containing `ssh://***@docker.example.test/` and no
  `secret`; discovery's event `3101` likewise.
- [ ] AC6: With `BaseUrl` `npipe://user:secret@pipehost/`, `DiscoverContainersAsync` returns `Failed` with a message
  containing `npipe://***@pipehost/` and no `secret`, and event `3104`'s exception message contains no `secret`.
- [ ] AC7: Synchronization stores `EndpointUri` `https://***@docker.example.test:2376/` for a configured
  `https://user:secret@docker.example.test:2376` (connection kind still `Https`, the options' `BaseUrl` unchanged),
  stores the Portainer `BaseUrl` masked, logs event `2091` for an enabled `ssh://user:secret@docker.example.test`
  masked, and rewrites a stored Portainer `BaseUrl` with user info masked when Portainer is disabled.
- [ ] AC8: `GetDockerInstancesAsync` and `GetDockerInstanceDetailAsync` return `EndpointUri`
  `https://***@docker.example.test/` for a stored row whose `EndpointUri` is `https://user:secret@docker.example.test`
  (a row written before the change), and `unix:///var/run/docker.sock` unchanged.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `UrlRedactionRedactUserInfoNullOrEmptyReturnsEmpty`, `UrlRedactionRedactUserInfoWithoutAtSignReturnsInputUnchanged` (data rows: unix, npipe, query), `UrlRedactionRedactUserInfoAtSignInPathReturnsInputUnchanged`, `UrlRedactionRedactUserInfoUserAndPasswordMasked` (data rows: the seven "parsed with user info" rows), `UrlRedactionRedactUserInfoUnparsableWithUserInfoMasked` (data rows: the five fallback rows); every masked row also asserts that neither the user name nor the password of its input occurs in the result | `src/Tests/DockerUpdateGuard.Tests/UrlRedactionTests.cs` |
| AC2 | `UrlRedactionRedactUserInfoNullUriReturnsEmpty`, `UrlRedactionRedactUserInfoAbsoluteUriMasked`, `UrlRedactionRedactUserInfoRelativeUriUsesOriginalString` | `src/Tests/DockerUpdateGuard.Tests/UrlRedactionTests.cs` |
| AC3 | `TransientHttpRetryHandlerTransientStatusLogOmitsUserInfoAndQueryAsync`, `TransientHttpRetryHandlerTransientExceptionLogOmitsUserInfoAndQueryAsync` | `src/Tests/DockerUpdateGuard.Tests/TransientHttpRetryHandlerTests.cs` |
| AC4 | `PortainerClientGetCapabilityAsyncWithUserInfoAndFailedLoginMasksUserInfoAsync`, `PortainerClientGetCapabilityAsyncWithUserInfoAndMissingJwtMasksUserInfoAsync` | `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs` |
| AC5 | `DockerInstanceClientDiscoverContainersAsyncWithUnsupportedEndpointUserInfoMasksUserInfoAsync`, `DockerInstanceClientOperationsWithUnsupportedEndpointUserInfoMaskUserInfoAsync` (the four other operations) | `src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientTests.cs` |
| AC6 | `DockerInstanceClientDiscoverContainersAsyncWithNamedPipeWithoutPipeNameMasksUserInfoAsync` | `src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientTests.cs` |
| AC7 | `InstanceDiscoveryServiceSynchronizeConfiguredInstancesAsyncStoresEndpointWithUserInfoMaskedAsync`, `InstanceDiscoveryServiceSynchronizeConfiguredInstancesAsyncStoresPortainerBaseUrlMaskedAsync`, `InstanceDiscoveryServiceSynchronizeConfiguredInstancesAsyncUnsupportedEndpointLogMasksUserInfoAsync`, `InstanceDiscoveryServiceSynchronizeConfiguredInstancesAsyncDisabledPortainerMasksStoredBaseUrlAsync` | `src/Tests/DockerUpdateGuard.Tests/InstanceDiscoveryServiceTests.cs` |
| AC8 | `ApplicationViewServiceDockerInstancesMaskStoredEndpointUserInfoAsync`, `ApplicationViewServiceDockerInstanceDetailMasksStoredEndpointUserInfoAsync` | `src/Tests/DockerUpdateGuard.Tests/ApplicationViewServiceTests.cs` |

`RedactUserInfo(null)` is ambiguous between the two overloads: the tests write `RedactUserInfo((string?)null)` and
`RedactUserInfo((Uri?)null)`.

## Approach

1. **Helper** `Infrastructure/UrlRedaction.cs`, `internal static class` (pattern of `PaginationLinkGuard`). String
   overload, in this order:
   1. `null` or empty → `""`.
   2. No `@` in the input → the input unchanged.
   3. `Uri.TryCreate(input, UriKind.Absolute, …)` succeeds and the trimmed input starts with `{uri.Scheme}://`
      (ordinal, case-insensitive), i.e. the parsed URI has an authority:
      - `UserInfo` not empty → `$"{uri.Scheme}://{Mask}@{uri.GetComponents(UriComponents.Host | UriComponents.Port | UriComponents.PathAndQuery | UriComponents.Fragment, UriFormat.UriEscaped)}"`;
      - `UserInfo` empty → the input unchanged (the `@` lies in path, query or fragment for the parser the clients
        connect with).
   4. Otherwise (unparseable, or parsed without an authority): `last` = last index of `@`; `start` = index after the first `://` when
      that occurs before `last`, else `0`; return `input[..start] + Mask + input[last..]`. Fail-closed: everything
      before the last `@` after the scheme separator is masked. String operations only, no regular expression.
   Uri overload: `null` → `""`; absolute → string overload of `AbsoluteUri`; relative → of `OriginalString`.
2. **Retry handler** `GetRequestTarget`: absolute → `UrlRedaction.RedactUserInfo(requestUri.GetLeftPart(UriPartial.Path))`
   (query still dropped first); relative → `UrlRedaction.RedactUserInfo(requestUri.OriginalString)`. XML summary
   names both. The request itself is untouched.
3. **Output sites**: pass `UrlRedaction.RedactUserInfo(…)` instead of the raw value at `DockerInstanceClient.cs:474`
   (Uri overload), `:983`, `:985`, `:1063`, `:1152`, `:1189`, `:1223`; `InstanceDiscoveryService.cs:118`;
   `PortainerClient.cs:166`, `:168`, `:173`, `:175` (compute the masked value once per call). Message texts, event
   IDs, levels and parameter names (`{BaseUrl}`) stay as they are.
4. **Stored values**: `InstanceDiscoveryService.cs:109` stores `UrlRedaction.RedactUserInfo(configuredInstance.BaseUrl.Trim())`;
   `:155` stores `UrlRedaction.RedactUserInfo(configuredInstance.Portainer.BaseUrl?.Trim())`; in the disabled branch
   (`:133-137`) also `PortainerEndpoint.BaseUrl = UrlRedaction.RedactUserInfo(PortainerEndpoint.BaseUrl)`.
   `MapConnectionKind` keeps reading the options. No migration.
5. **Display**: `ApplicationViewService.cs:2101`, `:2148` map `UrlRedaction.RedactUserInfo(entity.EndpointUri)`. The
   pages render the view data as before (no `.razor` change), so the prerendered persistent state is masked too.
6. Layout of output: unchanged except the URL text; no new user-visible element.

## Options considered

Weighed in [0005](../../docs/decisions/0005-urls-are-output-with-user-info-masked.md): masking with `***` (chosen),
dropping user info, rejecting it in validation (product decision, breaks working configurations), a log processor
(misses console, messages, database and UI), the issue's anchored regular expression (leaks the part after a second
`@`, misses a `/` in the password), display-only masking (secret stays at rest). One further choice: masking inside
the `LoggerMessage` methods instead of at the call sites — rejected, the exception and result messages need the
masked value too, and one rule per call site is easier to review.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Infrastructure/UrlRedaction.cs` | new helper |
| DockerUpdateGuard | `Infrastructure/TransientHttpRetryHandler.cs` | `GetRequestTarget` masks user info |
| DockerUpdateGuard | `Docker/DockerInstanceClient.cs` | seven output sites |
| DockerUpdateGuard | `Images/InstanceDiscoveryService.cs` | stored values and event `2091` |
| DockerUpdateGuard | `Portainer/PortainerClient.cs` | event `3303` and two exception messages |
| DockerUpdateGuard | `UI/ApplicationViewService.cs` | two mappings |

## Signatures (for the Dev's skeleton)

```csharp
namespace DockerUpdateGuard.Infrastructure;

internal static class UrlRedaction
{
    public const string Mask = "***";
    public static string RedactUserInfo(string? url);
    public static string RedactUserInfo(Uri? uri);
}
```

No other signature changes; every other file listed above changes only method bodies.

## Test files

- New: `src/Tests/DockerUpdateGuard.Tests/UrlRedactionTests.cs`.
- Extended: `TransientHttpRetryHandlerTests.cs`, `PortainerClientTests.cs`, `DockerInstanceClientTests.cs`,
  `InstanceDiscoveryServiceTests.cs`, `ApplicationViewServiceTests.cs` (all in `src/Tests/DockerUpdateGuard.Tests/`).
  The retry tests need the log entries: `SendThroughRetryHandlerAsync` gains an optional `TestLogger` parameter (the
  Tester's change, test code only).
- Existing test call sites of a changed signature: none. `DockerInstanceClientDiscoverContainersAsyncWithUnsupportedEndpointReturnsUnsupportedAndLogsWarningAsync`
  asserts `ssh://docker.example.test` in the log; that value has no `@` and stays unchanged.
- Files no unit test can cover: none.

## Areas

- Telemetry: *Credentials and external data* — configured URLs and retry lines carry no user info (masked `***`);
  HTTP client spans and client-factory log lines carry none by the platform; known gap #295 removed; 0005 linked.
- Web UI: *Secrets* — endpoints shown with user info masked, also for rows stored before; known gap #295 removed.
- Docker instances: stored endpoint URI masked; messages naming an endpoint mask its user info.
- Container actions: stored base URL masked (also when Portainer is disabled); error messages and logs name the base
  URL with user info masked.
- Registries: retry log line carries the URL without user info and query.

## Documentation updates

All edits by the **Dev**, after the code, checked against it:

- `docs/areas/telemetry.md` — *Credentials and external data* bullet 3 ("configured base URLs" → with user info
  replaced by `***`; "Retry log lines carry the request URL without its user info and query string"); a bullet that
  HTTP client spans (`url.full`) and the HTTP client log lines carry scheme, host, port and path only, with query
  values as `*`, unless the platform's URI redaction is switched off (`DOTNET_SYSTEM_NET_HTTP_DISABLEURIREDACTION`);
  remove the #295 *Known gaps* entry; *Related decisions* → 0005.
- `docs/areas/web-ui.md` — *Secrets*: replace "Docker instance endpoints are shown as configured (see *Known gaps*)"
  with the masked rule and an example (`https://***@docker.example:2376/`); remove the #295 *Known gaps* entry;
  *Related decisions* → 0005.
- `docs/areas/docker-instances.md` — line 26 "(trimmed `BaseUrl`, its user info masked)"; *Limits and errors*: a
  message or log entry that names an endpoint shows it with user info masked.
- `docs/areas/container-actions.md` — line 20 masked `BaseUrl`; line 21 disabled endpoint: base URL rewritten masked;
  lines 42 and 88: the base URL is named with user info masked.
- `docs/areas/registries.md` — line 166 "the URL without its user info and query".
- `docs/ARCHITECTURE.md` — *Security posture*, *Secrets in configuration* bullet: one sentence that URLs with user info
  are logged, stored and shown with it masked, linking [Telemetry](areas/telemetry.md) and record 0005.
- Sweep done: `grep -i "base url|verbatim|as configured|query string|user info"` over `docs/areas`, `ARCHITECTURE.md`,
  `README.md`; the hits above are all that the change makes untrue. `README.md` needs no edit.

## Architecture check

No guarantee of `.squad/project.md` is weakened. Clients keep returning `ExternalOperationResult<T>` (only message
text changes); layering stays (helper in the host project, next to `PaginationLinkGuard`); no entity or migration
change. `ARCHITECTURE.md` gets the sentence above because the secrets posture gains a rule.

## Security considerations

- [x] No limits on parsed input. Masking can lengthen a value by at most 2 characters (user info shorter than `***`)
  plus a trailing `/`; `EndpointUri`/`BaseUrl` columns are 512 characters and configuration has no length check
  today, so a configured URL of 509–512 characters with one- or two-character user info would now fail the save that
  a 513-character URL fails today. Accepted, not worth a new limit.
- [x] Exceptions reaching a user-visible message: the two Portainer `InvalidOperationException`s (text as today with
  the masked URL, e.g. `Portainer authentication failed with HTTP 401 for 'https://***@portainer.example.test:9443/'`)
  and the named-pipe `InvalidOperationException` (`The Docker named pipe URI 'npipe://***@pipehost/' does not contain
  a pipe name`); runtime `HttpRequestException`s carry no URI (fact 10).
- The helper is the guard: the forms the parser accepts or rejects are enumerated in fact 12, and AC1 has a row for
  each class (parsed with user info, parsed without, every rejected form seen, no `://`). Any input with `@` that the
  parser does not resolve to user info with an authority is masked fail-closed up to its last `@`.
- Scan-run error messages stored before the change are not rewritten (fact 3: the unsupported branches are not
  reachable in production; the named-pipe message is, and old rows age out with scan-run retention).

## Decision records

- `docs/decisions/0005-urls-are-output-with-user-info-masked.md` (Proposed), indexed.

## Out of scope / follow-ups

- Trivy's error excerpt (`TrivyVulnerabilityProvider.cs:305-311`) is passed on as Trivy writes it; whether Trivy echoes
  a `TrivyBaseUrl` with user info is unknown. Follow-up issue for the orchestrator: title "Trivy error output may echo
  credentials embedded in TrivyBaseUrl", body: "`TrivyVulnerabilityProvider` passes the last line of Trivy's stderr
  into logs (`TrivyCommandFailed`) and scan-run messages. If `Vulnerabilities:TrivyBaseUrl` carries user info
  (`https://user:secret@trivy:4954`), Trivy may repeat it there. Measure Trivy's output for a failing server call with
  such a URL, then decide whether to mask user info in free text with the helper of #295 (`UrlRedaction`), or to
  reject user info in `TrivyBaseUrl`."
- `OciRegistryClient.cs:1119,1134` name the token realm URI a registry sends; it is not configured and carries no
  configured secret — unchanged.
- Rejecting user info in configuration validation: a product decision (0005, option 3), not proposed here.
