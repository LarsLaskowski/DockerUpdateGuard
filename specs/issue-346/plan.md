# Plan: Reading a streamed registry response body has no time limit

Source: Issue #346
Tier: standard — the change bounds how long a registry response body may be read, a resource bound like the excerpt's size bound (input handling), and creates, changes or removes no guard on what an attacker may reach.
Security areas touched: Registry and Docker Scout calls (`DockerHub/DockerHubClient.cs`, `Images/OciRegistryClient.cs` and their `HttpClient` pipelines in `ServiceCollectionExtensions.cs`); Failure messages and remote text (the bounded read of `ResponseBodyExcerpt` and its `timed out` reason, `docs/areas/integration-failures.md`)

## Problem / root cause

Both registry clients send with `HttpCompletionOption.ResponseHeadersRead` (`DockerHub/DockerHubClient.cs:951-953`
in `SendThrottledAsync`; `Images/OciRegistryClient.cs:1103-1105` in `SendCoreAsync` and `:1117-1119` in
`GetBearerTokenAsync`). In that mode `HttpClient.Timeout` (set from `DockerHub:RequestTimeoutSeconds` in
`ServiceCollectionExtensions.cs:76` and `:82`) ends when the headers arrive. Every later body read passes only the
caller's token: `ResponseBodyExcerpt.ReadAsync` (`Infrastructure/ResponseBodyExcerpt.cs:56-63`, from
`OciRegistryClient.cs:790` and `DockerHubClient.cs:195`) and `RemoteJsonDocument.ParseAsync` after
`ReadAsStreamAsync` (`OciRegistryClient.cs:817`, `:916`, `:1129`, `:1401`; `DockerHubClient.cs:629`, `:671`, `:740`,
`:786`, `:1085`, `:1174`, `:1247`, `:1320`, `:1409`, `:1485`). A remote side that trickles a body keeps the read alive
until the caller's token fires, contrary to [Registries](../../docs/areas/registries.md#request-timeout), which calls
`RequestTimeoutSeconds` the total time of one request.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | Both clients send with `ResponseHeadersRead`; no other client does | grep `ResponseHeadersRead` in `src/`: only `DockerHubClient.cs:952`, `OciRegistryClient.cs:1104`, `:1118` | confirmed |
| 2 | `HttpClient.Timeout` does not cover a body read after `ResponseHeadersRead` (issue) | Scratch console app (net10.0), loopback server sending 1 byte per 500 ms, `Timeout` 2 s: the read was still running after 5 s, with `Content-Length` and with chunked encoding | confirmed |
| 3 | A pending `ReadAsync` on the `SocketsHttpHandler` response stream ends with `TaskCanceledException` carrying the passed token when that token is cancelled | Same scratch app, read token cancelled after 3 s: `TaskCanceledException` after about 3.05 s, token equal, over plain HTTP/1.1 (Content-Length, chunked) and over TLS (HTTP/1.1, 3.18 s) | confirmed |
| 4 | The clients use HTTP/1.1 (no HTTP/2 request version is set) | grep `DefaultRequestVersion`, `HttpVersion.`, `Version20` in `src/DockerUpdateGuard`: no hit; `HttpClient` default is 1.1 | confirmed |
| 5 | With the default `ResponseContentRead`, `HttpClient.Timeout` covers buffering the body, so the release feeds (`NginxReleaseMetadataService.cs:119`, `DotNetReleaseMetadataService.cs:150`) and Docker Scout (`DockerScoutVulnerabilityProvider.cs:453`) are bounded | Scratch app: `GetAsync` on the trickling server ended with `TaskCanceledException` (inner `TimeoutException`) after 2.0 s | confirmed |
| 6 | Disposing a `CancellationTokenSource(TimeSpan, TimeProvider)` disposes the `ITimer` it created | Scratch app with a recording `TimeProvider`: timer created with 30 s, `Disposed` true after `cts.Dispose()` | confirmed |
| 7 | Every client call reads its bodies inside `ExecuteAsync`, which rethrows `OperationCanceledException` only while the caller's token is cancelled and otherwise answers `<message>: timed out after <n> seconds` with `_httpClient.Timeout` | `OciRegistryClient.cs:1207-1227`, `DockerHubClient.cs:1110-1130`, `Infrastructure/FailureReason.cs` (`FromException`); public calls at `DockerHubClient.cs:1229`, `1284`, `1383`, `1460`, `1525`, `1561` and `OciRegistryClient.cs:1440`, `1501`, `1518` | confirmed |
| 8 | The Docker Hub web API login is already bounded including its body | `DockerHubAccessTokenCache.cs:158-161` links a `loginTimeout` (`_httpClient.Timeout`, `DockerHubClient.cs:1017`) into the login; test `DockerHubClientLoginBodyStalledReleasesTokenCacheAfterTimeoutAsync` | confirmed |
| 9 | The issue's claim that a trickle stalls "until the caller's cancellation token fires" | follows from rows 2 and 7 | confirmed |
| 10 | The OCI manifest lookup reads `Content-Type` of the response content | `OciRegistryClient.cs:915` | confirmed |

Related defects found: none.

## Acceptance criteria

- AC1: A Docker Hub client call whose response body stops arriving after the headers — a JSON body and a non-success
  body read for its excerpt — answers *failed* with `<call message>: timed out after 30 seconds` once the deadline of
  30 seconds armed when the request was sent elapses, without the caller's token being cancelled. For
  `GetTagsAsync("docker.io", "library/nginx")` the message is `Tag lookup failed for 'library/nginx': timed out after
  30 seconds`; for `GetTagAsync` it is `Tag metadata lookup failed for '<imageReference.FullReference>': timed out
  after 30 seconds`.
- AC2: The same for the OCI client: a stalled `tags/list` body gives, for `GetTagsAsync("ghcr.io", "acme/api")`,
  `Tag lookup failed for 'ghcr.io/acme/api': timed out after 30 seconds`; a stalled non-success manifest body (excerpt)
  and a stalled bearer token body each give `Registry tag lookup failed for '<imageReference.FullReference>': timed out
  after 30 seconds` from `GetTagAsync`.
- AC3: The handler arms exactly one deadline per request with its configured timeout on the injected `TimeProvider`
  before it passes the request on; a body read that ends in time returns the body's bytes unchanged, and the
  response content keeps every content header of the original (`Content-Type` with its parameters, `Content-Length`).
- AC4: A cancellation of the caller's token during a body read still ends the call with `OperationCanceledException`,
  not with a *failed* result.
- AC5: Disposing the response releases the deadline: the timer the handler armed is disposed.
- AC6: The `HttpClient`s of `DockerHubClient` and `OciRegistryClient` send through `RequestDeadlineHandler` with
  `DockerHub:RequestTimeoutSeconds`, placed before (outside) `TransientHttpRetryHandler`.

## Tests per criterion

The client tests build `new HttpClient(new RequestDeadlineHandler(clock, TimeSpan.FromSeconds(30)) { InnerHandler =
<DelegateHttpMessageHandler> }) { Timeout = TimeSpan.FromSeconds(30) }`; a stalled body is `new StreamContent(new
Pipe().Reader.AsStream())`, and `clock` is the substituted `TimeProvider` whose captured timer callback the test
fires, as `DockerHubClientLoginBodyStalledReleasesTokenCacheAfterTimeoutAsync` does.

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `DockerHubClientTagListingBodyStalledReturnsTimedOutAsync`, `DockerHubClientErrorBodyStalledReturnsTimedOutAsync` | `src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs` |
| AC2 | `OciRegistryClientTagListingBodyStalledReturnsTimedOutAsync`, `OciRegistryClientErrorBodyStalledReturnsTimedOutAsync`, `OciRegistryClientTokenBodyStalledReturnsTimedOutAsync` | `src/Tests/DockerUpdateGuard.Tests/OciRegistryClientTests.cs` |
| AC3 | `RequestDeadlineHandlerSendArmsDeadlineBeforeSendingAsync`, `RequestDeadlineHandlerResponseKeepsBodyAndContentHeadersAsync` | `src/Tests/DockerUpdateGuard.Tests/RequestDeadlineHandlerTests.cs` |
| AC4 | `OciRegistryClientCallerCancelledDuringBodyReadThrowsAsync` | `src/Tests/DockerUpdateGuard.Tests/OciRegistryClientTests.cs` |
| AC5 | `RequestDeadlineHandlerResponseDisposedReleasesDeadlineTimerAsync` | `src/Tests/DockerUpdateGuard.Tests/RequestDeadlineHandlerTests.cs` |
| AC6 | `ServiceCollectionExtensionsRegistryClientsSendThroughRequestDeadlineHandler` (one `DataRow` per client name, `nameof(DockerHubClient)` and `nameof(OciRegistryClient)`, configured timeout 42 s) | `src/Tests/DockerUpdateGuard.Tests/ServiceCollectionExtensionsTests.cs` |

## Approach

1. New `Infrastructure/RequestDeadlineHandler.cs`, a `DelegatingHandler`. `SendAsync` creates `deadlineSource = new
   CancellationTokenSource(RequestTimeout, _timeProvider)` before anything else, then, under a `using`
   `CreateLinkedTokenSource(cancellationToken, deadlineSource.Token)`, awaits `base.SendAsync` and
   `response.Content.ReadAsStreamAsync` with the linked token. It replaces `response.Content` by a `StreamContent` over
   a `DeadlineReadStream(innerStream, originalContent, deadlineSource)` and copies every header of the original
   content with `TryAddWithoutValidation`. On any exception it disposes the response (when it has one) and
   `deadlineSource`, then rethrows. The linked source is disposed when `SendAsync` returns; reads do not rely on it
   (fact 3: the stream observes only the read's token).
2. New `Infrastructure/DeadlineReadStream.cs`, a read-only `Stream`. `ReadAsync(Memory<byte>, CancellationToken)`
   reads the inner stream with the deadline token when the caller's token cannot be cancelled, otherwise with a
   per-read `CreateLinkedTokenSource(cancellationToken, deadline)`; `ReadAsync(byte[], int, int, CancellationToken)`
   forwards to it. The synchronous `Read`, `Seek`, `SetLength`, `Write` and the `Length`/`Position` accessors throw
   `NotSupportedException` (a synchronous read would bypass the deadline: fails closed); `CanRead` is the inner
   stream's, `CanSeek` and `CanWrite` are `false`, `Flush` does nothing. `Dispose(bool)` disposes the inner stream, the
   original content and `deadlineSource` (fact 6: its timer goes with it).
3. `ServiceCollectionExtensions.cs`: for `AddHttpClient<DockerHubClient>` and `AddHttpClient<OciRegistryClient>` add
   `.AddHttpMessageHandler(serviceProvider => new RequestDeadlineHandler(serviceProvider.GetRequiredService<TimeProvider>(),
   TimeSpan.FromSeconds(applicationOptions.DockerHub.RequestTimeoutSeconds)))` before
   `.AddHttpMessageHandler<TransientHttpRetryHandler>()`, so the deadline spans the retries like `HttpClient.Timeout`.
   `HttpClient.Timeout` stays: it is the value `ExecuteAsync` names in the reason.
4. The clients themselves do not change: the deadline's `OperationCanceledException` reaches `ExecuteAsync` while the
   caller's token is not cancelled and becomes `timed out after <n> seconds` (fact 7).

Resource bound and its seam: the deadline is the one timer `TimeProvider.CreateTimer` creates per request (due time
`RequestTimeout`, period infinite); tests observe and fire it through the substituted `TimeProvider`. No new
user-visible output: the reason text exists already.

## Options considered

1. Deadline handler outside the retry handler, linked into every body read — chosen.
2. Per-request bound at each call site, as the Portainer client does (0037) — needs a `TimeProvider` in both client
   constructors (about 70 test call sites) and about fifteen restructured read sites; a future read site that forgets
   the token is unbounded again.
3. `ResponseContentRead` so `HttpClient.Timeout` covers buffering — buffers every body whole (error bodies too, losing
   the excerpt's size bound) and holds the Docker Hub request slot during the body read (0030).
4. One bound per call in `ExecuteAsync` — a listing of up to 1,000 pages would have to fit one request timeout.
5. Dispose the response at the deadline — the reader sees `ObjectDisposedException`/`IOException`, reported as
   `unexpected error`/`connection failed`, not as a timeout.
6. A second body timeout starting at the headers — doubles the worst case and changes the meaning of
   `RequestTimeoutSeconds`.

Details in [0039](../../docs/decisions/0039-a-registry-request-and-the-read-of-its-response-body-share-one-deadline.md).

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Infrastructure/RequestDeadlineHandler.cs` | new |
| DockerUpdateGuard | `Infrastructure/DeadlineReadStream.cs` | new |
| DockerUpdateGuard | `ServiceCollectionExtensions.cs` | handler on the two registry clients |

## Signatures (for the Dev's skeleton)

- `internal sealed class RequestDeadlineHandler : DelegatingHandler`
- `public RequestDeadlineHandler(TimeProvider timeProvider, TimeSpan requestTimeout)`
- `public TimeSpan RequestTimeout { get; }`
- `protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)`
- `internal sealed class DeadlineReadStream : Stream`
- `public DeadlineReadStream(Stream innerStream, HttpContent innerContent, CancellationTokenSource deadlineSource)`
- `public override bool CanRead { get; }`, `public override bool CanSeek { get; }`, `public override bool CanWrite { get; }`, `public override long Length { get; }`, `public override long Position { get; set; }`
- `public override void Flush()`
- `public override int Read(byte[] buffer, int offset, int count)`
- `public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)`
- `public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)`
- `public override long Seek(long offset, SeekOrigin origin)`
- `public override void SetLength(long value)`
- `public override void Write(byte[] buffer, int offset, int count)`
- `protected override void Dispose(bool disposing)`

Names and parameter counts checked against *Writing code* in `.squad/stack.md`: no nested type, at most three
parameters, no keyword names. Existing files the skeleton rewrites: none (`ServiceCollectionExtensions.cs` changes in
step 6).

## Test files

- `src/Tests/DockerUpdateGuard.Tests/RequestDeadlineHandlerTests.cs` (new)
- `src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs`
- `src/Tests/DockerUpdateGuard.Tests/OciRegistryClientTests.cs`
- `src/Tests/DockerUpdateGuard.Tests/ServiceCollectionExtensionsTests.cs`
- Coverage pass (Tester): `src/Tests/DockerUpdateGuard.Tests/DeadlineReadStreamTests.cs` for the members no criterion
  test reaches (the `NotSupportedException` members, `Flush`, the read without a cancellable token).

Existing test code that calls a changed signature: none (no signature changes). Existing tests stay unchanged: they
build their `HttpClient`s without the new handler, so no input of theirs is handled differently.

Files no unit test can cover: none.

## Areas

- [Registries](../../docs/areas/registries.md): *Request timeout* — `RequestTimeoutSeconds` runs from sending a
  request until its response body is read, retries and waits included; a body still arriving then ends the call
  *failed* with `timed out after <n> seconds`. *Limits and errors* — each request including its body is bounded,
  the duration of a whole listing is not. *Related decisions* — 0039.
- [Integration failures](../../docs/areas/integration-failures.md): *Excerpts of remote text* (the bullet on the
  bounded read) — the streamed OCI and Docker Hub reads are bounded in time by the request timeout as well;
  *Retry of transient HTTP failures* (the bullet on a request's timeout) — for the registry clients the timeout also
  covers reading the response body.

## Documentation updates

All by the Dev, once the code is green, against the code as built:

- `docs/areas/registries.md` and `docs/areas/integration-failures.md` as under *Areas*.
- `README.md`, configuration table row `RequestTimeoutSeconds` (Docker Hub section, line 127): the timeout also
  covers reading a Docker Hub or OCI registry response.
- `docs/ARCHITECTURE.md` §3 step 4 (the `HttpClient`s with `TransientHttpRetryHandler`): the Docker Hub and OCI
  registry clients additionally send through `RequestDeadlineHandler`, outside the retry handler; §6, the paragraph on
  `TransientHttpRetryHandler`: one sentence on `RequestDeadlineHandler` with links to 0039 and
  [Registries](../../docs/areas/registries.md#request-timeout).
- `.squad/project.md`, *Security areas*, line *Registry and Docker Scout calls*: the Dev reports this text for the
  orchestrator to write in — add `Infrastructure/RequestDeadlineHandler.cs`, `Infrastructure/DeadlineReadStream.cs`
  after `Infrastructure/RemoteJsonDocument.cs`.
- Sweep done: "RequestTimeoutSeconds", "timeout" and the handler names in `docs/areas/*.md`, `docs/ARCHITECTURE.md`,
  `README.md`, `DOCKER.md`, `INSTALL.md`, `SECURITY.md`; the hits not listed (Portainer, Docker Scout, Docker Engine,
  database timeouts, the login in *Authentication*, the slot sentence in *Docker Hub request budget and parallelism*)
  stay true. No changelog exists.

## Architecture check

The guarantee "integration calls answer expected failures with an outcome and a fixed reason" holds: the deadline's
cancellation becomes the existing fixed reason. Layering holds (the handler is wiring in the host project). The
retry handler, request slot and pagination guard are unchanged. `docs/ARCHITECTURE.md` changes as listed above.

## Security considerations

- [x] Limits: a time limit only, not on bytes; the excerpt's size bound (`ResponseBodyExcerpt`) is unchanged.
- [x] Exceptions reaching a message: the deadline's `OperationCanceledException` → `timed out after <n> seconds`; a
  `NotSupportedException` from a synchronous read → `unexpected error`. No exception text reaches a message.

## Decision records

- `docs/decisions/0039-a-registry-request-and-the-read-of-its-response-body-share-one-deadline.md` (Proposed)

## Out of scope / follow-ups

- The total duration of a listing (per-request bound only, 0029 and 0024 unchanged).
- Release feeds, Docker Scout, Docker Engine and Portainer: bounded already (facts 5 and 8; 0037).

## Challenge notes

Plan challenge for issue #346 (mode plan), /home/user/DockerUpdateGuard/specs/issue-346/plan.md

The plan holds up. I checked its claims in the code:
- Only three places send with `ResponseHeadersRead`: `DockerHub/DockerHubClient.cs:952`, `Images/OciRegistryClient.cs:1104` and `:1118`. No `GetStreamAsync` or other streaming read exists anywhere else in `src/DockerUpdateGuard`.
- The Docker Hub request slot is released in the `finally` of `SendThrottledAsync` (`DockerHubClient.cs:946-960`), before any body read. So the handler does not change the slot semantics, and the slot wait stays outside the deadline, as `docs/areas/registries.md:220-223` says.
- `ExecuteAsync` (`OciRegistryClient.cs:1215-1227`, `DockerHubClient.cs:1118-1130`) and `FailureReason.FromException` (`Infrastructure/FailureReason.cs:58-65`) turn an `OperationCanceledException` raised while the caller's token is not cancelled into `timed out after <n> seconds`, using `_httpClient.Timeout`. Neither client has a `catch` that would swallow the deadline's exception before it reaches `ExecuteAsync`. The only other `catch` is at `OciRegistryClient.cs:564`, and it catches `ArgumentOutOfRangeException`.
- `TransientHttpRetryHandler` does not retry once its token is cancelled (`Infrastructure/TransientHttpRetryHandler.cs:159`, `:360-361`), and its backoff `Task.Delay` observes the token. With the deadline handler outside it, the deadline therefore covers retries and waits, as the plan says.
- Every body read goes through an async read: `ResponseBodyExcerpt` uses `StreamReader.ReadBlockAsync(Memory, ct)` (`Infrastructure/ResponseBodyExcerpt.cs:56-63`), and `RemoteJsonDocument.ParseAsync` is async. The `NotSupportedException` thrown by a synchronous `Read` is never hit in production.
- Portainer and Docker Engine are already covered by 0037. The release feeds and Docker Scout use `ResponseContentRead`, so `HttpClient.Timeout` covers them. The out-of-scope list is correct.

Objections:

1. minor: the OCI token-body test can fire the wrong timer. The template test `DockerHubClientLoginBodyStalledReleasesTokenCacheAfterTimeoutAsync` keeps only the first timer `CreateTimer` creates (`timerCallback ??= ...`, `src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs:3145-3152`). With `RequestDeadlineHandler` in the test's `HttpClient`, every request arms its own timer. Any test whose stalled body is not on the first request will fire the deadline of an earlier, already disposed response, and the stalled read never ends. That covers `OciRegistryClientTokenBodyStalledReturnsTimedOutAsync`, where the token request follows the registry request that answered with the challenge, and any test that sends a token or login request first. Tester: in these AC1 and AC2 tests, keep the timer callback of the request whose body stalls (for example the last timer created, or the one created while the stalled request is being sent), not the first, and fire that one.

## Security notes

## Security plan review: issue #346 (specs/issue-346/plan.md)

I approve the plan, with one non-blocking finding about how it lists security areas.

**Tier.** `standard` is correct. A time bound on reading a response body is a resource bound, which counts as input handling. It does not create, change or remove a guard that decides what an attacker may reach. Auth, bearer-token handling, `PaginationLinkGuard`, redirect behaviour and URL handling are untouched. The deadline creates no new message text: a stalled read maps to the existing `timed out after <n> seconds` reason.

**What I read against the plan**
- `TransientHttpRetryHandler.SendAsync`. A `TaskCanceledException` from the deadline is not caught as transient, because `IsTransientException` only matches `HttpRequestException` and `TimeoutException`. So the deadline does not trigger retries. Retry-After waits use the linked token, so they stay inside the deadline, as the plan says.
- `FailureReason.FromException`, lines 58–66. An `OperationCanceledException` while the caller's token is not cancelled becomes `timed out after <n> seconds`, so fact 7 holds.
- Every body read in the two clients and in `DockerHubAccessTokenCache` is asynchronous:
  - `ResponseBodyExcerpt.ReadAsync` uses `StreamReader.ReadBlockAsync`.
  - `RemoteJsonDocument.ParseAsync` uses `JsonDocument.ParseAsync`.
  - Lines `DockerHubClient.cs:670` and `OciRegistryClient.cs:915` read only content headers.
  - So the synchronous `Read` that throws `NotSupportedException` fails closed and breaks no existing reader.
- Response disposal. Every response is disposed by a `using var response` or an explicit `Dispose`, including the 401 → token → retry path in `OciRegistryClient.SendRegistryRequestAsync` (lines 1047–1076). Each request there gets its own deadline, and the deadline goes with the response. A response that were never disposed would still let go of its timer within one `RequestTimeoutSeconds`, so the bound holds.
- The excerpt's size bound (`MaxBodyLength`) and the JSON validation are unchanged.

**Measurement (run before approving).** Facts 2, 3 and 6 and the whole design depend on library behaviour, so I checked them myself.
- I built a scratch console app (net10.0) at `/tmp/claude-0/-home-user-DockerUpdateGuard/108fb879-950f-5379-ad51-d799bf96a1bc/scratchpad/dlprobe/Program.cs`. It contains:
  - a loopback HTTP/1.1 server that sends headers (`Content-Length: 100000`, `Content-Type: application/json; charset=utf-8`) and then 2 bytes every 300 ms;
  - a prototype `DeadlineHandler` and `DeadlineStream` written as plan steps 1–2 describe;
  - a recording `TimeProvider`.
- Command: `cd /tmp/claude-0/-home-user-DockerUpdateGuard/108fb879-950f-5379-ad51-d799bf96a1bc/scratchpad/dlprobe && timeout 300 dotnet run -c Release`
- Output:
  ```
  timers created=1 due=00:00:30 period=-00:00:00.0010000
  after Fire IsCancellationRequested=True
  after Dispose timer disposed=True
  json: headers CT=application/json; charset=utf-8 CL=100000
  json: TaskCanceledException after 2.02s
  excerpt: headers CT=application/json; charset=utf-8 CL=100000
  excerpt: TaskCanceledException after 2.00s
  ```
- What this confirms:
  - A `CancellationTokenSource(TimeSpan, TimeProvider)` creates one timer through `CreateTimer` with the configured due time. Firing that timer cancels the source, and disposing the source disposes the timer. This supports the test double the plan's tests use (a captured callback that the test fires) and AC5.
  - With a 2 s deadline and the caller's token not cancellable, both the `JsonDocument.ParseAsync` path and the excerpt path (`StreamReader.ReadBlockAsync` with a cancellable caller token, which goes through the per-read linked source) end in `TaskCanceledException` at the deadline.
  - Content headers survive the swap to `StreamContent`, including the `charset` parameter (AC3).
- `DeadlineStream.Dispose` ran 3–4 times per response: the `ReadOnlyStream` wrapper of `StreamContent`, the `StreamReader`, `await using`, and the response all dispose it. Disposing again did no harm, because `CancellationTokenSource` and `HttpContent` both tolerate it. The Dev should keep `Dispose(bool)` idempotent; no change to the plan is needed for this.

**Findings**

1. [NON-BLOCKING] *Security areas touched* leaves out **Failure messages and remote text** (`.squad/project.md`, the line listing `ResponseBodyExcerpt.cs`, `FailureReason.cs`, `TransientHttpRetryHandler.cs` → Integration failures). The plan changes what a stalled excerpt read produces: it is now a `timed out` reason instead of waiting until the caller's token fires (AC1/AC2 error-body cases, plan *Security considerations*, second bullet). It also edits that area's document, `docs/areas/integration-failures.md` (*Excerpts of remote text*, *Retry of transient HTTP failures*). The tier and the reviews stay the same. Smallest fix, verbatim, replacing the plan's *Security areas touched* line:
   `Security areas touched: Registry and Docker Scout calls (`DockerHub/DockerHubClient.cs`, `Images/OciRegistryClient.cs` and their `HttpClient` pipelines in `ServiceCollectionExtensions.cs`); Failure messages and remote text (the bounded read of `ResponseBodyExcerpt` and its `timed out` reason, `docs/areas/integration-failures.md`)`
   Recommendation: fix it now, in the Lead's revision or as a plan edit before step 4.

No blocking findings.
