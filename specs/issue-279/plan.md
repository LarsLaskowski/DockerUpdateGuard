# Plan: Portainer without EndpointId uses an environment only when it is unambiguous

Source: Issue #279
Status: Draft
Tier: standard — a behavior change of `PortainerClient` (refuse instead of picking the first environment) that guards against misconfiguration, not against an attacker, so it creates no security control.
Security areas touched: Container actions (`PortainerClient`)

## Problem / root cause

`PortainerClient.ResolveEndpointIdAsync` (`src/DockerUpdateGuard/Portainer/PortainerClient.cs:82-94`) returns
`endpoints?.FirstOrDefault()?.Id` of Portainer's `/api/endpoints` list, and both callers use it whenever `EndpointId`
is empty: `GetCapabilityAsync` (`PortainerClient.cs:254-269`) reports "Portainer connected, endpoint <id>" and
`ExecuteActionAsync` (`PortainerClient.cs:323-339`) looks up and acts on the container in that environment. With
several environments the first one can be another host. A non-success list answer also returns `null`
(`PortainerClient.cs:86-89`) and is reported as "No Portainer endpoint found", hiding the status code.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | Without `EndpointId` the first listed environment is used, on every call | `PortainerClient.cs:93`, called at `:258` and `:327` | confirmed |
| 2 | The issue's line numbers (`74-86`, `237-252`, `306-320`) | Actual: `82-94`, `254-269`, `323-339` (file grew since); behavior as described, line numbers refuted | refuted |
| 3 | A non-success list answer is reported as "No Portainer endpoint found" | `PortainerClient.cs:86-89` returns `null`, `:261-268` / `:330-337` map it | confirmed |
| 4 | `PortainerEndpointItem` carries `Id` (int) and `Name` (nullable string) | `src/DockerUpdateGuard/Portainer/Data/PortainerEndpointItem.cs` | confirmed |
| 5 | README describes `EndpointId` as auto-discovered | `README.md:195` (the issue says `:191`) | confirmed |
| 6 | The area document lists this defect as *Open questions* item 5 | `docs/areas/container-actions.md` has no *Open questions*; the defect is the second bullet of *Known gaps* | refuted |
| 7 | `PortainerOptions.EndpointId` XML doc says "auto-resolved from the first available endpoint" | `src/DockerUpdateGuard/Configuration/PortainerOptions.cs:36` (related stale text, not named by the issue) | confirmed |
| 8 | Portainer returns the full list without paging parameters and always sets `X-Total-Count` | Portainer source, `develop`: `api/http/handler/endpoints/endpoint_list.go` — `paginateEndpoints` returns all when `limit == 0`; `w.Header().Set("X-Total-Count", strconv.Itoa(filteredEndpointCount))` | confirmed |
| 9 | Portainer lists only the environments the authenticating user may see, and all types (Docker, Kubernetes, Azure) | Same file: `security.FilterEndpoints(...)`; no type filter without `types` | confirmed |
| 10 | The JSON field names are `Id` and `Name` | Portainer `api/portainer.go`, `Endpoint` struct tags `json:"Id"`, `json:"Name"` | confirmed |
| 11 | With `EndpointId` configured the list is not requested today | `PortainerClient.cs:256`, `:325` guard the call | confirmed |
| 12 | No existing test relies on an unregistered `/api/endpoints` (404) mapping to "No Portainer endpoint found" | Every `PortainerClientTests` case without `EndpointId` registers `EndpointsUrl` (`:252`, `:292`, `:640`) or never reaches resolution (disabled, allow-list) | confirmed |
| 13 | Configuration error messages name keys as `DockerUpdateGuard:DockerInstances:<instance name>:Portainer:<key>` in quotes | `DockerUpdateGuardOptionsValidator.cs:318-342` | confirmed |
| 14 | `SequenceHttpMessageHandler` copies response headers into the returned clone, so a test can set `X-Total-Count` | `src/Tests/DockerUpdateGuard.Tests/Helper/SequenceHttpMessageHandler.cs`, `CloneResponse` copies `response.Headers` | confirmed |
| 15 | EventIds 3312 and 3313 are free | `grep "EventId = 331[2-9]" src` finds none | confirmed |

The response body reader is unchanged (same `PortainerEndpointItem[]` deserialization); the only new input read is
the integer header of fact 8, so no new sample of the format is needed.

## Acceptance criteria

The decision path for every message below is the new `ResolveEndpointAsync`; `GetCapabilityAsync` and
`ExecuteActionAsync` pass its `FailureMessage` through unchanged. *Count* is the larger of the number of listed
environments and a valid `X-Total-Count` header.

- [ ] AC1: `GetCapabilityAsync` without `EndpointId`, list `[{"Id":5,"Name":"edge"},{"Id":1,"Name":"local"},{"Id":2,"Name":"nas"}]` → `IsConfigured = true`, `SupportsActions = false`, `Message` exactly `Portainer manages 3 environments; set 'DockerUpdateGuard:DockerInstances:Production:Portainer:EndpointId' to one of: 1 (local), 2 (nas), 5 (edge)`; one warning with EventId 3312 naming the instance and the count, not containing any environment name; no 3304 entry.
- [ ] AC2: `ExecuteActionAsync` (restart) without `EndpointId` and two listed environments → `Succeeded = false`, `Message` equal to the AC1 format for those two; the only requests are the list request (no container list, no `POST`).
- [ ] AC3: exactly one listed environment (Count 1) → it is used: capability "Portainer connected, endpoint 7", and an action runs against environment 7.
- [ ] AC4: an empty list (`[]`), or a body `null`, → "No Portainer endpoint found", as today, for both calls.
- [ ] AC5: the list request answering 500 → capability `IsConfigured = true`, `SupportsActions = false`, `Message` exactly `Portainer environment list failed: HTTP 500`; the action fails with the same message and sends no container-list request; one warning with EventId 3313 naming the instance and the status code.
- [ ] AC6: with `EndpointId` configured, the list is never requested, for both calls.
- [ ] AC7: Count above 10 → the message lists the first 10 environments by ascending `Id`, followed by `, …`; and a list with one environment plus `X-Total-Count: 3` → ambiguous, not used: `Message` exactly `Portainer manages 3 environments; set 'DockerUpdateGuard:DockerInstances:Production:Portainer:EndpointId' to one of: 7 (local), …`.
- [ ] AC8: environment names are trimmed; every character of Unicode category Control, Format, LineSeparator or ParagraphSeparator becomes `?`; a name longer than 64 UTF-16 units is cut to 64 (63 when the 64th is a high surrogate) plus `…`; an empty or missing name lists the ID alone (`3` instead of `3 ()`).

## Tests per criterion

All in `PortainerClientTests`. Tests that pass on today's code are marked *deferred* (step 6, `.squad/routing.md`
step 5); "existing" tests stay unchanged.

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `PortainerClientGetCapabilityAsyncWithMultipleEnvironmentsAndNoEndpointIdReportsAmbiguousAsync` | `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs` |
| AC2 | `PortainerClientExecuteActionAsyncWithMultipleEnvironmentsAndNoEndpointIdFailsWithoutActionAsync` | `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs` |
| AC3 | existing `PortainerClientGetCapabilityAsyncResolvesEndpointAutomaticallyAndLogsResolutionAsync`; new `PortainerClientExecuteActionAsyncWithSingleEnvironmentAndNoEndpointIdUsesItAsync` (deferred) | `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs` |
| AC4 | existing `PortainerClientGetCapabilityAsyncWhenNoEndpointFoundReturnsUnsupportedResultAsync`, `PortainerClientExecuteActionAsyncWhenNoEndpointFoundReturnsFailureResultAsync`; new `PortainerClientGetCapabilityAsyncWhenEnvironmentListIsNullReturnsNoEndpointFoundAsync` (deferred) | `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs` |
| AC5 | `PortainerClientGetCapabilityAsyncWhenEnvironmentListFailsReportsStatusCodeAsync`, `PortainerClientExecuteActionAsyncWhenEnvironmentListFailsReportsStatusCodeWithoutActionAsync` | `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs` |
| AC6 | `PortainerClientGetCapabilityAsyncWithEndpointIdDoesNotRequestEnvironmentListAsync`, `PortainerClientExecuteActionAsyncWithEndpointIdDoesNotRequestEnvironmentListAsync` (both deferred) | `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs` |
| AC7 | `PortainerClientGetCapabilityAsyncWithMoreThanTenEnvironmentsListsFirstTenAsync`, `PortainerClientGetCapabilityAsyncWithTotalCountAboveListedEnvironmentsReportsAmbiguousAsync` | `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs` |
| AC8 | `PortainerClientGetCapabilityAsyncWithAmbiguousEnvironmentsSanitizesEnvironmentNamesAsync` (cases in one list: a name with U+202E, one with U+2028, one with a tab, a 100-character name, a 63-character prefix followed by a surrogate pair, `"Name":null`, `"Name":"  "`) | `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs` |

AC8 input: the JSON body carries each special character as a JSON escape (`\u202E`, `\u2028`, `\t`, a surrogate
pair as `\uD83D\uDE00`) inside a C# raw string literal, so neither the C# compiler nor `reihitsu-format` turns it
into the character; the test asserts that its JSON input contains the backslash sequence `\u202E`, so a lost escape
fails instead of testing a plain name (`.squad/team.md`, *Escapes in files*). The guard that fails when the escape is
lost is that assertion; with the escape present, the category replacement is what turns the decoded U+202E into `?`.

## Approach

`ResolveEndpointAsync` replaces `ResolveEndpointIdAsync` and decides in this order:

1. `EndpointId` configured (not whitespace) → `EndpointId` as configured, no request.
2. `GET /api/endpoints` (unchanged URL, no paging parameters). Non-success → log 3313, failure
   `Portainer environment list failed: HTTP <status>`.
3. Body read as `PortainerEndpointItem[]` (a `null` body counts as empty). `X-Total-Count`: read only when exactly one
   value parses as a non-negative `int` (`CultureInfo.InvariantCulture`); otherwise ignored. Count = max(listed, header).
4. Count greater than 1 → log 3312, failure message (format below). Listed exactly 1 and Count 1 → its `Id`
   (`ToString(CultureInfo.InvariantCulture)`). Otherwise → failure `No Portainer endpoint found`.

Message layout (one line, plain text): `Portainer manages <Count> environments; set
'DockerUpdateGuard:DockerInstances:<instance name>:Portainer:EndpointId' to one of: <entries>`. The section name comes
from `DockerUpdateGuardOptions.SectionName`. Entries: the listed environments ordered by `Id` ascending, at most 10,
each `<Id> (<name>)` or `<Id>` when the cleaned name is empty, joined by `, `; when Count exceeds the number of entries
shown, `, …` is appended (`…` alone when none is shown). Name cleaning as AC8, applied in this order: trim, replace,
cut. IDs and names only — never an environment URL.

Callers: both map a failure to their existing result shape (`IsConfigured = true, SupportsActions = false` /
`Succeeded = false`) with `FailureMessage` as `Message`; the success path is unchanged (3304 log, "Portainer connected,
endpoint <id>", container lookup). Exceptions keep flowing into the existing `catch` blocks (decision 0009).

Logs: 3312 Warning `Portainer for Docker instance {DockerInstanceName} manages {EnvironmentCount} environments and no
EndpointId is configured; no environment is used`; 3313 Warning `Portainer environment list failed for Docker instance
{DockerInstanceName}: HTTP {StatusCode}`. Neither carries environment names or URLs.

## Options considered

The product options (heuristic matching, mandatory `EndpointId` at startup, persisting the resolved ID, warn only)
are weighed in [0010](../../docs/decisions/0010-portainer-environment-is-used-only-when-unambiguous.md); the Product
Manager decided "no silent pick" on the issue. Implementation choices:

1. **Ignore `X-Total-Count`, count the array only** — Portainer returns the full list without paging (fact 8), but a
   proxy or a future default page size would make a truncated list of one look unambiguous; reading the header fails
   closed at the cost of one parse. Lost.
2. **Request `?limit=…` paging or `excludeSnapshots=true`** — changes the request every existing test registers, and
   is not needed for correctness. Lost; see *Out of scope*.
3. **List IDs only, no names** — no external text in the message, but the issue asks for names so the operator can
   choose. Lost; names are cleaned and cut instead (AC8).
4. **Return a tuple instead of a record** — a named record reads better at two call sites and is documentable. Lost.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Portainer/PortainerClient.cs` | `ResolveEndpointIdAsync` replaced by `ResolveEndpointAsync`; three static helpers and three constants; both call sites map the resolution |
| DockerUpdateGuard | `Portainer/Data/PortainerEndpointResolution.cs` (new) | result of the resolution |
| DockerUpdateGuard | `Portainer/PortainerClientLogging.cs` | EventIds 3312, 3313 |
| DockerUpdateGuard | `Configuration/PortainerOptions.cs` | XML doc of `EndpointId` (fact 7): "Optional Portainer environment identifier — required when Portainer lists more than one environment" |

## Signatures (for the Dev's skeleton)

New file `src/DockerUpdateGuard/Portainer/Data/PortainerEndpointResolution.cs` (positional record, documented like
`PortainerEndpointItem`; exactly one of the two values is set):

- `internal sealed record PortainerEndpointResolution(string? EndpointId, string? FailureMessage);`

`PortainerClient` — removed: `private static async Task<string?> ResolveEndpointIdAsync(HttpClient client, CancellationToken cancellationToken)`. New, in *Constants*:

- `private const int MaxListedEnvironments = 10;`
- `private const int MaxEnvironmentNameLength = 64;`
- `private const string TotalCountHeaderName = "X-Total-Count";`

New, in *Static methods*:

- `private static int? ReadTotalCount(HttpResponseMessage response)`
- `private static string FormatEnvironmentName(string? name)`
- `private static string FormatAmbiguousEnvironmentsMessage(string dockerInstanceName, PortainerEndpointItem[] endpoints, int environmentCount)`

New, in *Methods*:

- `private async Task<PortainerEndpointResolution> ResolveEndpointAsync(HttpClient client, DockerInstanceOptions instanceOptions, CancellationToken cancellationToken)`

`PortainerClientLogging`, in *Methods*:

- `public static partial void PortainerEnvironmentAmbiguous(this ILogger logger, string dockerInstanceName, int environmentCount);` (EventId 3312, Warning)
- `public static partial void PortainerEnvironmentListFailed(this ILogger logger, string dockerInstanceName, int statusCode);` (EventId 3313, Warning)

Skeleton note: `GetCapabilityAsync` and `ExecuteActionAsync` keep their public signatures and already call
`ResolveEndpointAsync` in the skeleton. A throwing body would break existing tests that pass today (every
`EndpointId` test, and the three without it at `PortainerClientTests.cs:243`, `:283`, `:632`), so its skeleton body
is today's logic in the new shape: configured `EndpointId` returned, else the first listed `Id`, else
`No Portainer endpoint found` (also for a non-success answer). The three static helpers throw the *Skeleton*
exception. The Dev reports this as the allowed deviation (`.squad/stack.md`, *Skeleton*); the new AC1, AC2, AC5, AC7
and AC8 tests still fail on it.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/PortainerClientTests.cs` — the existing class; new constants for the extra
  environment JSON go into its *Constants* region.

Existing test code that calls a changed signature: none (only private members change; `IPortainerClient` is
unchanged). Existing tests kept unchanged: no existing input is handled differently (fact 12; single-environment and
empty lists keep their results).

Files no unit test can cover: none.

## Areas

`docs/areas/container-actions.md`:

- *Configuration and mapping*, last bullet: replace "When it is empty, the first environment … resolved again on every
  call" with the rule — without `EndpointId` the environment is used only when Portainer lists exactly one (counting
  `X-Total-Count`), resolved on every call; the ambiguous message format, the 10-entry limit and the name cleaning;
  that Portainer lists only environments the user may see, of every type; link record 0010.
- *Capability check*: the ambiguous case and the list-failure case (both configured, no actions, with their messages).
- *Actions*: the failure reasons gain "several environments without `EndpointId`" and "environment list failed with
  HTTP status", both before any container lookup.
- *Limits and errors*: EventIds 3312 and 3313 and what they carry.
- *Known gaps*: remove the #279 bullet.
- *Related decisions*: add 0010.

## Documentation updates

Sweep: `EndpointId`, "first", "auto-discover", "auto-resolv" across `README.md`, `INSTALL.md`, `docs/`; hits:
`README.md:195`, `docs/areas/container-actions.md` (two places), `PortainerOptions.cs:36`. `docs/ARCHITECTURE.md:229`
("endpoint and container listing") stays true; `appsettings.json` keeps `"EndpointId": ""`; no event-ID table elsewhere.

| File | Edit | Owner |
| ---- | ---- | ----- |
| `README.md:195` | Description: "Portainer environment ID; required when Portainer manages more than one environment — without it the only listed environment is used, and actions are refused when there are several" | Dev |
| `docs/areas/container-actions.md` | as *Areas* | Dev |
| `docs/decisions/0010-…md`, index row | written (Proposed) | Lead |

## Architecture check

Guarantee "integration clients never throw for expected failures": preserved — every new outcome is a result, the
existing `catch` blocks are unchanged. Layering unchanged (host project only). No entity, migration or startup
validation change; `PortainerEndpoint.ExternalEndpointId` still stores the configured value only. No
`docs/ARCHITECTURE.md` update.

## Security considerations

- [ ] Limits: at most 10 environments in a message and 64 UTF-16 units per name, both counted after JSON decoding and
  after the replacement of AC8 (replacement is one-for-one, so it cannot grow a name). The list body itself is not
  size-capped (unchanged from today; bounded by `RequestTimeoutSeconds`).
- [ ] Exceptions reaching a message: none new — JSON or transport failures of the list request end in the existing
  `catch` with the fixed reasons of 0009 (`invalid response`, `connection failed`, …).
- [ ] The message is external data (environment names) shown in the UI; it is never logged. Logs carry only the
  instance name, the count and the status code.
- [ ] No action request can be sent without a resolved environment: the action path returns before
  `FindContainerIdAsync` on every failure (AC2, AC5).

## Decision records

- `docs/decisions/0010-portainer-environment-is-used-only-when-unambiguous.md` (Proposed), indexed.

## Out of scope / follow-ups

- Requesting the list with `excludeSnapshots=true` and a size cap on its body — a separate improvement, not needed to
  stop the wrong-host action.
- Wiring the capability check and actions into the UI: #280.
