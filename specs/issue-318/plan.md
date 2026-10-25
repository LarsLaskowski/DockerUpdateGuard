# Plan: Integration clients and orchestrators report fixed failure reasons instead of raw exception messages

Source: Issue #318
Status: Draft
Tier: security — the change extends the output-sanitizing allow-list of record 0009 (fixed reasons, one marker type whose message passes) from Portainer to every integration client and the scan orchestrators; #315 was raised to `security` for the same control.
Security areas touched: Secrets in configuration (exception text in result and scan-run messages); Docker Engine access (`DockerInstanceClient` results and logs); Container actions (`PortainerClient` moves to the shared mapping); External process execution (`TrivyVulnerabilityProvider` failure message only, no change to arguments or environment); Registry and Docker Hub calls (`DockerHubClient`, `OciRegistryClient`, `DockerScoutVulnerabilityProvider` results); Logging and telemetry of external data (five new log entries that carry the exception)

## Problem / root cause

Catch-all blocks interpolate `exception.Message` into the failure text (line numbers of the issue confirmed on
`origin/main` 7066811):

- `src/DockerUpdateGuard/Docker/DockerInstanceClient.cs:1041` (discovery), `:1127` (resource sampling), `:1164`
  (image inspect), `:1201` (image history), `:1256` (host info);
- `src/DockerUpdateGuard/Images/OciRegistryClient.cs:1347`; `src/DockerUpdateGuard/DockerHub/DockerHubClient.cs:1420`,
  `:1477`;
- `src/DockerUpdateGuard/Images/DotNetReleaseMetadataService.cs:208`, `NginxReleaseMetadataService.cs:165`;
- `src/DockerUpdateGuard/Vulnerabilities/TrivyVulnerabilityProvider.cs:355`, `DockerScoutVulnerabilityProvider.cs:469`;
- orchestrators: `src/DockerUpdateGuard/Images/RuntimeContainerScanOrchestrator.cs:517` (scan-run message), `:673`
  (`UpdateAssessmentMessage`), `:681` (container message), `VulnerabilityEnrichmentService.cs:489`,
  `ImageScanOrchestrator.cs:763`.

The orchestrator sites matter most: tag lookups and listings of both registry clients have no catch at all, so their
`HttpRequestException`, `HttpIOException`, `JsonException` and `HttpClient.Timeout` reach `:681`/`:763` unmapped.

Four Docker catch-alls (`:1127`, `:1164`, `:1201`, `:1256`) and `DockerHubClient.cs:1477` do not log the exception at
all, so mapping them without a new log entry would lose the only diagnosis.

Related defects found on the way (not fixed here, follow-up issues below):

- The UI catch blocks show `exception.Message` on the page and log nothing: `Components/Shared/VulnerabilityRescanControl.razor.cs:89`,
  `Components/Pages/RuntimeContainers.razor.cs:389`, `RuntimeContainerDetail.razor.cs:229`, `:263`,
  `ObservedImages.razor.cs:196`, `ScanHistory.razor.cs:206` (`docs/areas/web-ui.md:127` documents it).
- Remote response bodies go unbounded into results (`DockerInstanceClient.cs:1000`, `:875`, `:919`, `:1238`;
  `OciRegistryClient.cs:683`; `DockerHubClient.cs:230`; the release feeds `:158`/`:127`), and
  `ContainerSnapshot.UpdateAssessmentMessage` is limited to 2000 characters
  (`ContainerSnapshotConfiguration.cs:47-48`) while `RuntimeContainerScanOrchestrator.cs:765-768` stores a tag
  result's message there, so a long registry body can make the scan's save fail.
- A configured URL with `#` or `?` in its user info passes validation and connects to the wrong host (*Verified facts*
  13), carrying the Portainer API key to it; this is also what makes the 3306/3310 log entries name `admin:443`.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | The 17 sites of the issue put `exception.Message` into a result or status message | `grep -n "\.Message" src/DockerUpdateGuard`: the lines under *Problem* | confirmed |
| 2 | The messages reach scan-run errors and pages | `RuntimeContainerScanOrchestrator.cs:473`, `:524`; `ImageScanOrchestrator.cs:520`, `:770`; `VulnerabilityEnrichmentService.cs:310-318`, `:496`; `docs/areas/web-ui.md:128` | confirmed |
| 3 | The release-feed messages are not shown today (callers only test the status) | `ImageScanOrchestrator.cs:418`, `:467`; `RuntimeContainerScanOrchestrator.cs:997`, `:1039` | confirmed |
| 4 | Docker discovery logs its exception (3104); sampling, inspect, history, host info and the Docker Hub configuration lookup do not | `DockerInstanceClient.cs:1037-1042`, `:1125-1128`, `:1162-1165`, `:1199-1202`, `:1254-1257`; `DockerHubClient.cs:1475-1478` | confirmed |
| 5 | Docker catch-alls run after caller cancellation is rethrown; Docker Hub/OCI filter out every `OperationCanceledException`; release feeds and orchestrators catch caller cancellation too | `DockerInstanceClient.cs:1027`, `:1158`; `DockerHubClient.cs:1416`, `:1475`; `OciRegistryClient.cs:1341`; `DotNetReleaseMetadataService.cs:204`; `RuntimeContainerScanOrchestrator.cs:513` | confirmed |
| 6 | Two Docker messages are composed by the application and relied on by tests: the unusable client certificate and the npipe URI without pipe name, both thrown as `InvalidOperationException` | `DockerInstanceClient.cs:312`, `:487`; `DockerInstanceClientTests.cs:676`, `:923-929` | confirmed |
| 7 | A body that breaks off while read from `ReadAsStreamAsync` surfaces as `HttpIOException`, an `IOException` and not an `HttpRequestException`; with the default completion option it is `HttpRequestException` "Error while copying content to a stream." | Scratch probe (.NET 10.0.402, scratchpad): `Content-Length: 100`, connection closed early | confirmed |
| 8 | `ResponseHeadersRead` (streamed bodies) is used by the registry clients only | `OciRegistryClient.cs:1098`, `:1112`; `DockerHubClient.cs:919` | confirmed |
| 9 | A unix-socket connect failure in `ConnectCallback` is wrapped as `HttpRequestException` naming `localhost:80` | Same probe: `Cannot assign requested address (localhost:80)`, inner `SocketException` | confirmed |
| 10 | `Process.Start` of a missing executable throws `Win32Exception` naming the executable and the working directory | Same probe: `An error occurred trying to start process '/nonexistent/…' with working directory '…'. No such file or directory` | confirmed |
| 11 | `JsonSerializer.Deserialize` and `JsonDocument.ParseAsync` throw `JsonException` for malformed JSON | System.Text.Json documentation; #315 *Verified facts* 6 measured it for `ReadFromJsonAsync` | confirmed |
| 12 | `HttpClient.Timeout` surfaces as `TaskCanceledException` with inner `TimeoutException` while the caller's token is not cancelled | #315 *Verified facts* 4 (probe) | confirmed |
| 13 | `https://admin#secret@portainer.example:9443` parses with host `admin`, empty user info, the rest as fragment; the request fails with `Name or service not known (admin:443)`; `https://ad?min:secret@host` gives host `ad` | Same probe | confirmed |
| 14 | A proxy taken from `HTTPS_PROXY=http://user:pass@…` appears in the runtime's message without user info; an explicit `WebProxy` with user info would show it | Same probe: `The proxy tunnel request to proxy 'http://127.0.0.1:<port>/' failed …` vs `'http://puser:ppass@…'`; the app sets no `WebProxy` (`grep -rn "WebProxy\|Proxy =" src/DockerUpdateGuard`: none; Docker transports set `UseProxy = false`) | confirmed |
| 15 | No span records exception data | `grep -rn "SetStatus\|AddException\|RecordException" src`: none | confirmed |
| 16 | Record 0009 is unreleased and can be extended in place | Added by bc41e03; `git merge-base --is-ancestor bc41e03 v1.3.7` fails; latest tag on origin `v1.3.7` (`git ls-remote --tags origin`) | confirmed |
| 17 | `PortainerClient.GetFailureReason` has no `IOException` branch and no test calls it | `PortainerClient.cs:83-113`; `grep GetFailureReason src/Tests`: none | confirmed |

## Acceptance criteria

`<reason>` is one of the texts AC1 defines. Every test that injects an exception gives it a message containing the
marker `internal.test` and asserts `Assert.AreEqual` on the full message plus `Assert.DoesNotContain("internal.test", …)`.

- [ ] AC1: `FailureReason.FromException(exception, timeout, cancellationToken)` returns, first match wins:
  the message of a `ReportableException` (also of a subtype); `cancelled` for an `OperationCanceledException` while
  `cancellationToken.IsCancellationRequested`; for any other `OperationCanceledException` or a `TimeoutException`
  `timed out after <n> seconds` when `timeout` is positive and finite (`n` = whole seconds, invariant culture), else
  `timed out`; `connection failed` for `HttpRequestException` or `IOException` (`HttpIOException` included);
  `invalid response` for `JsonException`; `unexpected error` otherwise.
- [ ] AC2: When a transport failure (`HttpRequestException`) breaks a Docker Engine operation, the result is failed
  with `Docker container discovery failed for '<Name>': connection failed`, `Docker container resource sampling
  failed for '<Name>': connection failed`, `Docker image inspect failed for '<Name>': connection failed`, `Docker
  image history failed for '<Name>': connection failed` and `Docker host info request failed for '<Name>': connection
  failed`, and the failure is logged with the exception (EventIds 3104, 3107, 3108, 3109, 3110).
- [ ] AC3: The composed Docker messages pass unchanged (client certificate, npipe URI without pipe name, *Verified
  facts* 6); any other exception from creating the HTTP client gives `Docker image inspect failed for '<Name>':
  unexpected error`.
- [ ] AC4: A transport failure during base-image resolution gives `Base image chain resolution failed for
  '<reference>': connection failed` from `OciRegistryClient` and from `DockerHubClient`; during a Docker Hub image
  configuration lookup `Image configuration lookup failed for '<reference>': connection failed`, logged with the
  exception (EventId 3212).
- [ ] AC5: A release feed that cannot be reached gives `Loading .NET release metadata failed: connection failed` /
  `Loading NGINX release metadata failed: connection failed`; a malformed .NET index gives `Loading .NET release
  metadata failed: invalid response`; a cancelled caller token gives `…: cancelled` (no throw, as today).
- [ ] AC6: Trivy output that is not JSON gives `Trivy scan failed: invalid response`; a `Win32Exception` from starting
  the process gives `Trivy scan failed: the Trivy executable could not be started`; both still logged with the
  exception (EventId 3423).
- [ ] AC7: A Docker Scout transport failure gives `Docker Scout scan failed: connection failed`; an `HttpClient`
  timeout (*Verified facts* 12) gives `Docker Scout scan failed: timed out after <n> seconds` with `n` =
  `Vulnerabilities:RequestTimeoutSeconds`.
- [ ] AC8: An exception in a runtime container scan outside a container gives the scan-run message `Runtime container
  scan failed: <reason>`; an exception while processing one container gives `Container '<container>' could not be
  processed: <reason>` in the scan-run message and in that snapshot's `UpdateAssessmentMessage`.
- [ ] AC9: An exception in an observed-image scan gives the scan-run message `Observed image scan failed: <reason>`;
  in a vulnerability refresh `Vulnerability refresh failed: <reason>`.
- [ ] AC10: Portainer results are unchanged by the move to the shared mapping (the #315 tests stay green), except that an `IOException`/`HttpIOException` now reads `connection failed`, as `container-actions.md` already documents.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `FailureReasonFromExceptionWithReportableExceptionReturnsItsMessage` (also with a `PortainerAuthenticationException`); `FailureReasonFromExceptionWithCallerCancellationReturnsCancelled`; `FailureReasonFromExceptionWithTimeoutReturnsTimedOut` (`TaskCanceledException` with inner `TimeoutException` and 15 s → `timed out after 15 seconds`; `TimeoutException` with `null`, `TimeSpan.Zero`, `Timeout.InfiniteTimeSpan` → `timed out`); `FailureReasonFromExceptionWithTransportFailureReturnsConnectionFailed` (`HttpRequestException`, `HttpIOException(HttpRequestError.ResponseEnded, …)`, `IOException`); `FailureReasonFromExceptionWithJsonExceptionReturnsInvalidResponse`; `FailureReasonFromExceptionWithOtherExceptionReturnsUnexpectedError` (`InvalidOperationException`, `Win32Exception`) | `src/Tests/DockerUpdateGuard.Tests/FailureReasonTests.cs` |
| AC2 | `DockerInstanceClientOperationWhenTransportFailsReportsConnectionFailedAsync` (`[DataRow]` per operation with expected message and EventId; client built with the factory constructor over `RetryProbeHttpMessageHandler`, outcome `[null]`, `FailureFactory` = `new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused (docker.internal.test:2376)")`) | `src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientTests.cs` |
| AC3 | `DockerInstanceClientInspectImageAsyncWhenClientCreationThrowsReportsUnexpectedErrorAsync` (factory throws `InvalidOperationException("Client creation failed for docker.internal.test")`); pins, not written in step 5: `DockerInstanceClientInspectImageAsyncWithMissingClientCertificateReturnsFailedAsync`, `DockerInstanceClientDiscoverContainersAsyncWithNamedPipeWithoutPipeNameMasksUserInfoAsync` | `src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientTests.cs` |
| AC4 | `OciRegistryClientResolveBaseImagesAsyncWhenTransportFailsReportsConnectionFailedAsync` | `src/Tests/DockerUpdateGuard.Tests/OciRegistryClientTests.cs` |
| AC4 | `DockerHubClientResolveBaseImagesAsyncWhenTransportFailsReportsConnectionFailedAsync`; `DockerHubClientGetImageConfigurationAsyncWhenTransportFailsReportsConnectionFailedAsync` (asserts the 3212 entry carries the exception) | `src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs` |
| AC5 | `DotNetReleaseMetadataServiceGetChannelReleaseAsyncWhenTransportFailsReportsConnectionFailedAsync`; `DotNetReleaseMetadataServiceGetChannelReleaseAsyncWhenIndexIsMalformedReportsInvalidResponseAsync`; `DotNetReleaseMetadataServiceGetChannelReleaseAsyncWhenCallerCancelsReportsCancelledAsync` (cancellation injected through `FailureFactory` with the cancelled token, #315 *Challenge notes*) | `src/Tests/DockerUpdateGuard.Tests/DotNetReleaseMetadataServiceTests.cs` |
| AC5 | `NginxReleaseMetadataServiceGetChannelReleaseAsyncWhenTransportFailsReportsConnectionFailedAsync` | `src/Tests/DockerUpdateGuard.Tests/NginxReleaseMetadataServiceTests.cs` |
| AC6 | `TrivyVulnerabilityProviderGetVulnerabilitiesAsyncReturnsFailedForInvalidJsonAsync` (existing, tightened to `Assert.AreEqual("Trivy scan failed: invalid response", …)`); `TrivyVulnerabilityProviderGetVulnerabilitiesAsyncWhenProcessCannotStartReportsStartFailureAsync` (`IProcessRunner` substitute throws `new Win32Exception(2, "An error occurred trying to start process 'trivy' with working directory '/app.internal.test'")`; asserts 3423 with the exception) | `src/Tests/DockerUpdateGuard.Tests/TrivyVulnerabilityProviderTests.cs` |
| AC7 | `DockerScoutVulnerabilityProviderGetVulnerabilitiesAsyncWhenTransportFailsReportsConnectionFailedAsync`; `DockerScoutVulnerabilityProviderGetVulnerabilitiesAsyncWhenRequestTimesOutReportsTimedOutAsync` | `src/Tests/DockerUpdateGuard.Tests/DockerScoutVulnerabilityProviderTests.cs` |
| AC8 | `RuntimeContainerScanOrchestratorScanAllAsyncWhenDiscoveryThrowsReportsFixedReasonAsync` (`DiscoverContainersAsync` throws `HttpRequestException` → `Runtime container scan failed: connection failed`); `RuntimeContainerScanOrchestratorScanAllAsyncWhenContainerProcessingThrowsReportsFixedReasonAsync` (`GetTagsAsync` throws `HttpRequestException` → both texts `Container '<name>' could not be processed: connection failed`) | `src/Tests/DockerUpdateGuard.Tests/RuntimeContainerScanOrchestratorTests.cs` |
| AC9 | `ImageScanOrchestratorScanAsyncWhenRegistryThrowsReportsFixedReasonAsync` (`GetTagAsync` throws `HttpRequestException` → `Observed image scan failed: connection failed`) | `src/Tests/DockerUpdateGuard.Tests/ImageScanOrchestratorTests.cs` |
| AC9 | `VulnerabilityEnrichmentServiceRefreshAsyncWhenProviderThrowsReportsFixedReasonAsync` (provider throws `InvalidOperationException("provider boom internal.test")` → `Vulnerability refresh failed: unexpected error`) | `src/Tests/DockerUpdateGuard.Tests/VulnerabilityEnrichmentServiceTests.cs` |
| AC10 | pins, not written in step 5: the nine `PortainerClient…Reports…Async` tests of #315 (`PortainerClientTests.cs:1300-1444`) and the login-failure tests | `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs` |

Expected to fail on today's code: AC1 (type missing, fails against the skeleton stub), AC2, AC3 (new test), AC4–AC9.

## Approach

1. **`ReportableException`** (new, `Infrastructure`): an exception whose message the application composed from fixed
   text, configuration names and masked URLs only. `PortainerAuthenticationException` derives from it instead of
   `Exception`. `DockerInstanceClient.cs:312` and `:487` throw `ReportableException` instead of
   `InvalidOperationException`, texts unchanged.
2. **`FailureReason`** (new, `Infrastructure`): constants and `FromException` exactly as AC1.
   `PortainerClient.GetFailureReason` is removed; both call sites use
   `FailureReason.FromException(ex, TimeSpan.FromSeconds(instanceOptions.Portainer.RequestTimeoutSeconds), cancellationToken)`.
3. **Clients**, each catch-all keeps its prefix and appends the reason; timeouts passed:
   - Docker (5 sites): `TimeSpan.FromSeconds(instanceOptions.RequestTimeoutSeconds)`; sampling, inspect, history and host
     info log first with new entries 3107–3110 (*Signatures*).
   - OCI, Docker Hub (3 sites): `null` (cancellation is filtered out there). `DockerHubClient.cs:1475` logs 3212 first.
   - Release feeds: `_httpClient.Timeout`. Trivy: `ex is Win32Exception` → `the Trivy executable could not be started`,
     else `FailureReason.FromException(ex, null, cancellationToken)`. Docker Scout:
     `TimeSpan.FromSeconds(options.Vulnerabilities.RequestTimeoutSeconds)`.
4. **Orchestrators** (`timeout` `null`, existing log calls unchanged, statuses unchanged): `RuntimeContainerScanOrchestrator.cs:517`
   → `Runtime container scan failed: <reason>`; `:673` and `:681` → one local `Container '<container.Name>' could not
   be processed: <reason>` used for both; `ImageScanOrchestrator.cs:763` → `Observed image scan failed: <reason>`;
   `VulnerabilityEnrichmentService.cs:489` → `Vulnerability refresh failed: <reason>`.
5. No control flow changes: which calls throw, rethrow or catch stays as it is; messages composed inside the `try`
   (status codes, bodies, timeouts) stay.

No new layout: every message stays one line in its existing place.

## Options considered

1. Shared mapping plus a marker type for composed messages — chosen (record 0009).
2. A private mapping per client, as #315 built it — eight copies that drift.
3. Sanitizing the message text — deny-list over runtime text (0009).
4. Passing selected types' messages, or keeping `InvalidOperationException` as the marker — runtime text again; the
   runtime throws `InvalidOperationException` too.
5. Mapping only in the orchestrators — misses client results that reach logs and other callers, and cannot name the
   client's timeout.
6. Trivy's process-start failure as `unexpected error` — lost: the most common Trivy misconfiguration would become
   unreadable; one Trivy-local reason keeps it actionable without runtime text.
7. For the 3306/3310 log concern of the issue: (a) drop the exception from these entries — lost: removes the only
   diagnosis and contradicts "keeping the exception in the log"; (b) reject URLs whose user info contains `#`/`?` at
   validation here — split: it touches every URL option, the Hosting area and its tests, and is a separate defect
   (wrong request target, *Problem*); (c) keep the entries — chosen for this change, with *Verified facts* 13–14
   (environment proxies carry no user info; the would-be host disappears with the follow-up).
8. A dedicated `database error` reason for EF/Npgsql exceptions — not chosen: no criterion needs it; they read
   `unexpected error` with the exception logged.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Infrastructure/ReportableException.cs` | new |
| DockerUpdateGuard | `Infrastructure/FailureReason.cs` | new |
| DockerUpdateGuard | `Portainer/PortainerAuthenticationException.cs` | base type `ReportableException` |
| DockerUpdateGuard | `Portainer/PortainerClient.cs` | `GetFailureReason` removed, calls use `FailureReason` |
| DockerUpdateGuard | `Docker/DockerInstanceClient.cs`, `Docker/DockerInstanceClientLogging.cs` | two throws, five catch-alls, four log entries |
| DockerUpdateGuard | `DockerHub/DockerHubClient.cs`, `DockerHub/DockerHubClientLogging.cs` | two catch-alls, one log entry |
| DockerUpdateGuard | `Images/OciRegistryClient.cs`, `Images/DotNetReleaseMetadataService.cs`, `Images/NginxReleaseMetadataService.cs` | catch-alls |
| DockerUpdateGuard | `Vulnerabilities/TrivyVulnerabilityProvider.cs`, `Vulnerabilities/DockerScoutVulnerabilityProvider.cs` | catch-alls |
| DockerUpdateGuard | `Images/RuntimeContainerScanOrchestrator.cs`, `Images/ImageScanOrchestrator.cs`, `Images/VulnerabilityEnrichmentService.cs` | orchestrator messages |

## Signatures (for the Dev's skeleton)

`src/DockerUpdateGuard/Infrastructure/ReportableException.cs`, namespace `DockerUpdateGuard.Infrastructure`,
`#region Constructors` (not sealed: `PortainerAuthenticationException` derives from it):

```csharp
public class ReportableException : Exception
{
    public ReportableException()
    public ReportableException(string message)
    public ReportableException(string message, Exception innerException)
}
```

`src/DockerUpdateGuard/Infrastructure/FailureReason.cs`, namespace `DockerUpdateGuard.Infrastructure`:

```csharp
internal static class FailureReason
{
    public const string Cancelled = "cancelled";
    public const string TimedOut = "timed out";
    public const string ConnectionFailed = "connection failed";
    public const string InvalidResponse = "invalid response";
    public const string UnexpectedError = "unexpected error";

    public static string FromException(Exception exception,
                                       TimeSpan? timeout,
                                       CancellationToken cancellationToken)
}
```

`src/DockerUpdateGuard/Portainer/PortainerAuthenticationException.cs`:
`public sealed class PortainerAuthenticationException : ReportableException` (constructors unchanged).

`DockerInstanceClientLogging.cs` (Level `Warning`, parameter order as `DockerInstanceDiscoveryFailed`):

```csharp
[LoggerMessage(EventId = 3107, Level = LogLevel.Warning, Message = "Docker container resource sampling failed for {DockerInstanceName}")]
public static partial void DockerInstanceResourceSamplingFailed(this ILogger logger, Exception exception, string dockerInstanceName);
[LoggerMessage(EventId = 3108, Level = LogLevel.Warning, Message = "Docker image inspect failed for {DockerInstanceName}")]
public static partial void DockerInstanceImageInspectFailed(this ILogger logger, Exception exception, string dockerInstanceName);
[LoggerMessage(EventId = 3109, Level = LogLevel.Warning, Message = "Docker image history failed for {DockerInstanceName}")]
public static partial void DockerInstanceImageHistoryFailed(this ILogger logger, Exception exception, string dockerInstanceName);
[LoggerMessage(EventId = 3110, Level = LogLevel.Warning, Message = "Docker host info request failed for {DockerInstanceName}")]
public static partial void DockerInstanceHostInfoFailed(this ILogger logger, Exception exception, string dockerInstanceName);
```

`DockerHubClientLogging.cs`:

```csharp
[LoggerMessage(EventId = 3212, Level = LogLevel.Warning, Message = "Docker Hub image configuration lookup failed for {ImageReference}")]
public static partial void DockerHubImageConfigurationLookupFailed(this ILogger logger, Exception exception, string imageReference);
```

`TrivyVulnerabilityProvider`: private const `ProcessStartFailedReason = "the Trivy executable could not be started"`.
Removed: `PortainerClient.GetFailureReason` (private). Skeleton: the two new files (`FromException` body as *Skeleton*
in `.squad/stack.md` prescribes), the base-type change and the five log methods; every other file is the Dev's step-6
edit.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/FailureReasonTests.cs` (new); existing: `DockerInstanceClientTests.cs`,
  `OciRegistryClientTests.cs`, `DockerHubClientTests.cs`, `DotNetReleaseMetadataServiceTests.cs`,
  `NginxReleaseMetadataServiceTests.cs`, `TrivyVulnerabilityProviderTests.cs`,
  `DockerScoutVulnerabilityProviderTests.cs`, `RuntimeContainerScanOrchestratorTests.cs`, `ImageScanOrchestratorTests.cs`,
  `VulnerabilityEnrichmentServiceTests.cs`, `PortainerClientTests.cs` (pins only).

Existing test code that calls a changed signature: none (no public signature changes; the tests assert messages, not
the thrown type of the Docker composed messages).

Files of the change no unit test can cover: none. The parameterless and inner-exception constructors of
`ReportableException` are unused by production code; if the *Coverage gate* reports them, the Tester's coverage pass
adds `src/Tests/DockerUpdateGuard.Tests/ReportableExceptionTests.cs`.

## Areas

- `docs/areas/registries.md` — the failure model gains the fixed reasons, the marker rule and the logging (the home of
  the reasons).
- `docs/areas/docker-instances.md`, `docs/areas/vulnerabilities.md`, `docs/areas/scan-engine.md` — unexpected errors
  read a fixed reason.
- `docs/areas/container-actions.md` — points to the shared reasons; record link renamed (done by the Lead).

## Documentation updates

Sweep: `grep -rn -i "error text\|error message\|error's own\|exception message\|message with the"` over `docs/areas`,
`docs/ARCHITECTURE.md` and `README.md`. Hits that stay: `docker-instances.md:122`, `:165-172` (composed certificate
message, still true), `image-catalog.md:87` (form validation), `web-ui.md:127` (UI catch blocks, follow-up),
`telemetry.md:106` (external tools' output), `base-images.md:22` (no text named), `container-actions.md:55`, `:85`;
`README.md`: none.

All owned by the **Dev**, step 6, except the first item:

- Done by the Lead: record 0009 renamed to `0009-integration-results-carry-fixed-failure-reasons.md`, extended,
  `Proposed`; index row and the link in `container-actions.md` *Related decisions* updated.
- `registries.md`, *Failure model of integrations*, new last bullet: an outcome caused by an error rather than by an
  answer carries a fixed reason, never the error's own text — `cancelled`, `timed out after <n> seconds` (or `timed
  out` when the call has no timeout of its own), `connection failed` (transport and broken responses), `invalid
  response` (malformed JSON), `unexpected error`; a message the application composed itself is passed on; the error is
  logged with the failure. Link record 0009 under *Related decisions*.
- `registries.md`, *Limits and errors*: "A Docker Hub configuration lookup turns them into *failed*." → "… into
  *failed* with a fixed reason ([failure model](#failure-model-of-integrations))."; add that such errors reach the
  scan run as `Container '<name>' could not be processed: <reason>` or `Observed image scan failed: <reason>`.
- `docker-instances.md:176`: "Unexpected errors are reported with the instance name and the error message." →
  "Unexpected errors are reported with the instance name and a fixed reason
  ([Registries](registries.md#failure-model-of-integrations)), except the composed certificate and named-pipe
  messages; every failure is logged with its error."
- `scan-engine.md:178-179`: "or the error's own message" → "or, for an unexpected error, `<scan> failed: <reason>`
  (`Runtime container scan failed`, `Observed image scan failed`, `Vulnerability refresh failed`) with a fixed reason
  ([Registries](registries.md#failure-model-of-integrations))".
- `vulnerabilities.md:157-159`: the provider texts become "`Trivy scan failed: <reason>`" / "`Docker Scout scan
  failed: <reason>`", plus `Trivy scan failed: the Trivy executable could not be started`; "with the error message" →
  "with `Vulnerability refresh failed: <reason>`". `:164`: "the provider's error text" → "the provider's error output
  (Trivy's excerpt) or a fixed reason".
- `container-actions.md:98-102`: append "These are the shared reasons of
  [Registries](registries.md#failure-model-of-integrations)."
- `docs/ARCHITECTURE.md` §11, new bullet after *Secrets in configuration*: "**Failure messages carry fixed reasons.**
  Integration clients and the scan orchestrators never put an exception's message into a result or a scan run; they
  report a fixed reason and log the exception ([Registries](areas/registries.md#failure-model-of-integrations),
  [record 0009](decisions/0009-integration-results-carry-fixed-failure-reasons.md))."

## Architecture check

- *Integration clients never throw for expected failures*: preserved, no throw/catch changes (Approach 5).
- Record 0005: only composed messages with masked URLs pass. Layering: both new types in the host's `Infrastructure`.
- No new dependency, configuration, entity or job.

## Security considerations

- [x] No limits: no parsed input gains or loses a limit.
- [x] Every exception that can reach a user-visible message: `ReportableException` (Portainer login failure, Docker
  client certificate, npipe without pipe name) → its composed text; everything else → a reason of AC1, or Trivy's
  start-failure reason. No raw runtime exception message reaches a result, an assessment or a scan run from the sites
  of this change; the UI catch blocks remain (follow-up 1).
- Logs keep the exception; five new entries add it where none was logged. Environment proxies appear without user
  info (*Verified facts* 14); a would-be host from `#`/`?` in user info remains in log text until follow-up 3.

## Decision records

- `docs/decisions/0009-integration-results-carry-fixed-failure-reasons.md` (Proposed; extended in place from the
  Portainer record, renamed, index row updated).

## Out of scope / follow-ups

Follow-up issues for the orchestrator to create:

1. **`[BUG] Pages show raw exception messages of failed user actions`** — Body: The catch blocks of
   `VulnerabilityRescanControl.razor.cs:89`, `RuntimeContainers.razor.cs:389`, `RuntimeContainerDetail.razor.cs:229`,
   `:263`, `ObservedImages.razor.cs:196` and `ScanHistory.razor.cs:206` show `exception.Message` on the page and log
   nothing. Show a fixed reason (`FailureReason`, record 0009) and log the exception. Found while fixing #318.
2. **`[BUG] Remote response bodies go unbounded into results and can exceed stored message lengths`** — Body: Docker
   Engine, registry and release-feed failures append the full response body to the result message
   (`DockerInstanceClient.cs:875`, `:919`, `:1000`, `:1238`; `OciRegistryClient.cs:683`; `DockerHubClient.cs:230`;
   `DotNetReleaseMetadataService.cs:158`; `NginxReleaseMetadataService.cs:127`). `RuntimeContainerScanOrchestrator.cs:765-768`
   stores such a message in `ContainerSnapshot.UpdateAssessmentMessage`, limited to 2000 characters, so a long body
   can make the scan's save fail. Bound the excerpt. Found while fixing #318.
3. **`[BUG] Configuration validation accepts URLs whose user info is parsed as host`** — Body:
   `https://admin#secret@portainer.example:9443` (or `?` instead of `#`) passes validation; the client connects to
   host `admin`, sends the Portainer API key there, and log entries (3306, 3310, 3104, …) name `admin:443`. Reject a
   configured URL whose text contains `@` that its parsed user info does not account for. Found while fixing #318.


## Challenge notes

- minor: AC10 says Portainer results are "unchanged", but AC1 adds an `IOException` -> `connection failed` branch that `PortainerClient.GetFailureReason` does not have (`PortainerClient.cs:83-113`, plan *Verified facts* 17). A Portainer `HttpIOException` from a response that breaks off while being read changes from `unexpected error` to `connection failed`. Instruction: reword AC10 to "Portainer results are unchanged except that an `IOException`/`HttpIOException` now reads `connection failed`, as `container-actions.md` already documents", and leave the #315 pins as they are.

## Security notes

1. AC10 overstates "unchanged". The shared mapping adds an `IOException` branch. A Portainer `HttpIOException` that read "unexpected error" under `PortainerClient.GetFailureReason` will now read "connection failed". This is safe, but AC10 and the Portainer text in `container-actions.md` should say so. Alternatively, add one Portainer pin test that shows the new reason.
2. The marker contract has no guard against misuse. `ReportableException` is public and not sealed, and its whole guarantee is that its message is composed only from fixed text, configuration names and masked URLs. Put that rule in the type's XML summary and in the `registries.md` failure-model bullet. Have the Reviewer treat as blocking any future `new ReportableException(...)` (or subtype) whose message interpolates `.Message`, a response body or an unmasked URL.
3. Residual exposure stays in results, already accepted by record 0009 and tracked as follow-ups 1-3 (remote response bodies, Trivy's stderr excerpt, the UI catch blocks). All three follow-up issues should be created in the same run.
