# Plan: Leave out a container list or image history item that is not a JSON object

Source: Issue #572
Tier: standard — a behavior change of how the Docker Engine client reads its list answers (a call that failed now succeeds without the item); it creates, changes or removes no security control.
Security areas touched: none

## Problem / root cause

Three loops in `src/DockerUpdateGuard/Docker/DockerInstanceClient.cs` read every item of a JSON array answer as a JSON
object, and `JsonElement.TryGetProperty` throws `InvalidOperationException` on any other kind:

- discovery, `ParseContainers` (`:974–989`): `HasStorableId` (`:509–514`) calls `TryGetString` (`:546–560`), whose
  only guard is `JsonValueKind.Undefined` (`:548`), then `TryGetProperty` (`:553`);
- resource sampling, `ReadContainerSamplesAsync` (`:1156–1158`): `TryGetString(containerElement, "Id")`;
- image history, the loop of `ReadImageHistoryAsync` (`:951–954`): `ParseImageHistoryEntry` (`:804–813`) first calls
  `TryGetHistoryCreatedAtUtc`, whose `element.TryGetProperty("Created", …)` (`:822`) throws before any `TryGetString`.

The catch of the public call (`DiscoverContainersAsync` `:1257–1262`, `CollectContainerResourceUsageAsync`
`:1317–1322`, `GetImageHistoryAsync` `:1395–1400`) maps the exception through `FailureReason.FromException` to
`unexpected error` (`FailureReason.cs:83`) and answers the whole call *failed*.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | A list item of kind String, Number, True, False, Null or Array throws in all three loops | the call chains above; `TryGetProperty` requires `ValueKind` Object (.NET `JsonElement` documentation); the existing test rows `RootNotObject` (`DockerInstanceClientTests.cs:2177`) show the same throw on an inspect root | confirmed |
| 2 | The issue's line numbers moved after #573 | `ParseContainers` now `:974`, sampling `:1156`, history `:951`, guard `:548` | confirmed |
| 3 | The history loop fails in `TryGetString` (issue) | it fails earlier, in `TryGetHistoryCreatedAtUtc` `:822`; a fix inside `TryGetString` alone would not reach it | refuted |
| 4 | An item without an ID is skipped (issue: "skipped like an item without an ID") | true for sampling only (`:1160`: null, blank or over 200 characters); discovery keeps it, `HasStorableId` answers true for a null ID (`:513`) and records an empty container ID (refuted for discovery) | refuted |
| 5 | A failed history keeps the derived runtime findings; a succeeded one is evaluated | `RuntimeContainerScanOrchestrator.cs:1354–1366` (`KeepFindings` on failure, `Detect` then `DeactivateFindings` when nothing is found) | confirmed |
| 6 | A non-object stats body and a non-object inspect root must keep failing | 0026 and 0068 (stats body left out with `unexpected error`); area *Container discovery* (inspect "holds a value of an unexpected kind"); pinned by `WrongShape` (`:1960`) and `RootNotObject` (`:2177`) | confirmed |
| 7 | A root that is not a JSON array fails the call in `EnumerateArray` | `:951`, `:978`, `:1156` | confirmed |
| 8 | Precedent: the Docker Hub client skips a platform image entry that is not an object | `DockerHubClient.cs:237–242` | confirmed |
| 9 | Real samples exist: the history example of the API definition (`HistorySample`, `DockerInstanceClientTests.cs:66`) and the `ContainerList` example of the Engine API v1.41 definition (first item `Id` `8dfafdbc3a40`, public example values) | `https://docs.docker.com/reference/api/engine/version/v1.41.yaml`, `/containers/json` → `examples` | confirmed |

## Acceptance criteria

The non-object kinds are String, Number, True, False, Null and Array; each test covers every one of them.

- AC1: Discovery whose list holds such an item next to a container object answers *succeeded* with exactly that
  container, and logs no failed discovery (event 3104).
- AC2: Resource sampling whose list holds such an item next to a container object answers *succeeded* with that
  container's sample and sends exactly one stats request; a list of only such items answers *succeeded* with no sample
  and sends no stats request.
- AC3: An image history whose array holds such an entry between the entries of the sample answers *succeeded* with
  the sample's entries, in order, and nothing for the left-out entry.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `DockerInstanceClientDiscoverContainersAsyncWhenListItemIsNoObjectSkipsItAsync` (one `DataRow` per kind) | `src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientListItemsTests.cs` |
| AC2 | `DockerInstanceClientCollectContainerResourceUsageAsyncWhenListItemIsNoObjectSkipsItAsync`, `DockerInstanceClientCollectContainerResourceUsageAsyncWhenListHoldsOnlyNoObjectItemsReturnsEmptySucceededAsync` (one `DataRow` per kind each) | same |
| AC3 | `DockerInstanceClientGetImageHistoryAsyncWhenEntryIsNoObjectSkipsItAsync` (one `DataRow` per kind) | same |

The container object of AC1 and AC2 is the first item of the API definition's `ContainerList` example (fact 9), its
`ImageID`'s inspect answered by the test; the history of AC3 is `HistorySample` with the item inserted after its first
entry. Each test fails today with *failed* / `unexpected error`.

## Approach

Each of the three loops checks the item's kind first and goes on with the next item when it is not
`JsonValueKind.Object`: in `ParseContainers` before `HasStorableId`, in `ReadContainerSamplesAsync` before the ID is
read (so a skipped item does not count as requested and sends no request), in the history loop before
`ParseImageHistoryEntry`. No log entry for a skipped item, as for an over-long ID. `TryGetString`, `HasStorableId`,
`ParseResourceSample`, `ParseImageInspect` and every other reader stay unchanged, so facts 6 and 7 keep their behavior.
No new output.

Early draft PR: no

## Options considered

The full list with pros and cons is in decision record 0069.

- Leave the item out (chosen): the over-long-ID rule and the Docker Hub precedent (fact 8).
- Answer the call *failed* with a fixed message: one item still discards every readable container — the reported defect.
- Leave it out and log a warning per item: a new log event for an input no real Engine sends.
- Guard non-objects inside `TryGetString`: fixes only sampling — discovery reads `Labels` (`:447`) and the history `Created` (fact 3) directly — and would weaken what fact 6 rests on.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Docker/DockerInstanceClient.cs` | kind check in `ParseContainers`, `ReadContainerSamplesAsync`, `ReadImageHistoryAsync` |
| DockerUpdateGuard.Tests | `DockerInstanceClientListItemsTests.cs` (new) | the tests above |

## Signatures (for the Dev's skeleton)

None: only private method bodies change, no skeleton (step 4 skipped).

## Test files

- `src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientListItemsTests.cs` (new class
  `DockerInstanceClientListItemsTests`, `{TypeUnderTest}{Concern}Tests`; it declares its own samples, helpers and
  `TestContext`; `DockerInstanceClientTests.cs` already has 2,651 lines).

Existing test code that calls a changed signature: none. Existing tests kept unchanged: none feeds a non-object list
item; `WrongShape` and `RootNotObject` (fact 6) stay green because no reader they reach changes.

Files of the change no unit test can cover: none.

## Areas

- `docs/areas/docker-instances.md` (+55 words):
  - *Container discovery*, first bullet: an item of the list that is not a JSON object is left out, like a container
    whose ID is longer than 200 characters.
  - *Resource statistics*, second bullet: an item of the list that is not a JSON object, a container without an ID
    (fact 4, today undocumented) or with an ID longer than 200 characters is left out without a request.
  - *Image inspection and history*, first bullet: a history entry that is not a JSON object is left out, the other
    entries are still read.
  - *Related decisions*: a line for 0069.

## Documentation updates

- `docs/areas/docker-instances.md`: as *Areas* (Dev).
- `docs/ARCHITECTURE.md`, `README.md`: none — the sweep for "left out", "longer than 200", "not a JSON" and "unexpected
  kind" over `docs/ARCHITECTURE.md` and `docs/areas/` found only the three bullets above (and the inspect rule, which
  this change keeps). No changelog exists.

## Architecture check

The guarantee that every Engine operation reports a status instead of throwing (area *Limits and errors*) is kept;
fewer calls end *failed*. One failing instance still never stops the others. No guarantee of `.squad/project.md` is
weakened.

## Security considerations

- [x] No limits: no byte or length bound changes; the 200-character ID bound stays.
- [x] Exceptions reaching a user-visible reason: none new; the change removes one source of `unexpected error`.

A broken or hostile Engine gains nothing: a list of only skipped items answers like the empty list it can send today
(decision record 0069, *Consequences*).

## Decision records

- `docs/decisions/0069-a-docker-engine-list-item-that-is-not-a-json-object-is-left-out.md` (Proposed)

## Out of scope / follow-ups

- A root that is not a JSON array keeps failing the call (fact 7): the answer as a whole is unreadable.
- Proposed issue 1 — title: `[Docker] Discovery records a listed container without an ID with an empty container ID`;
  body: `HasStorableId` (`DockerInstanceClient.cs:509–514`) answers true for a missing ID, so discovery records the item
  with an empty ID (and an empty name without `Names`), while resource sampling leaves it out (`:1160`). Decide
  whether discovery leaves it out too, and cover it with tests. Found while planning #572.
- Proposed issue 2 — title: `[Docker] A non-string item of an image history entry's Tags fails the whole history`;
  body: `ReadStringArray` (`DockerInstanceClient.cs:842–855`) calls `GetString` on each item, which throws for a number
  or a boolean, so `GetImageHistoryAsync` answers *failed* with `unexpected error`. Image inspect documents such a value
  as a failed inspect (area *Container discovery*); decide whether history and inspect skip such items or keep
  failing, and cover it with tests. Found while planning #572.

## Challenge notes

Devil's Advocate, mode `plan`, on /home/user/DockerUpdateGuard/specs/issue-572/plan.md (decision record /home/user/DockerUpdateGuard/docs/decisions/0069-a-docker-engine-list-item-that-is-not-a-json-object-is-left-out.md).

The plan holds up. I found one minor wording issue in the area text and nothing that changes the plan.

**Checks that confirmed the plan** (all in /home/user/DockerUpdateGuard/src/DockerUpdateGuard/Docker/DockerInstanceClient.cs):
- **The three loops and their throw points:**
  - `ParseContainers`: line 978 loops over the items, line 980 calls `HasStorableId`, which calls `TryGetString`.
  - `ReadContainerSamplesAsync`: line 1156 loops, line 1158 calls `TryGetString`.
  - The history loop: line 951 loops, then `ParseImageHistoryEntry` calls `TryGetHistoryCreatedAtUtc`, whose `TryGetProperty` at line 822 throws before any `TryGetString`. So the plan is right that the issue's root cause is wrong for history (fact 3).
- **`TryGetString`** only guards `Undefined` (line 548), so its `TryGetProperty` throws on every other non-object kind, `Null` included.
- **Fact 4 (an item without an ID is not skipped everywhere):** `HasStorableId` answers true for a null ID (line 513), so discovery keeps such an item. Sampling skips it (line 1160).
- **Why fixing `TryGetString` alone is not enough:** discovery would still throw on `Labels` (line 447). The rejected option is argued correctly.
- **Failure logging:** discovery's catch logs `DockerInstanceDiscoveryFailed` (the plan's event 3104, defined at `DockerInstanceClientLogging.cs:65`) and maps the error through `FailureReason`. AC1's "no event 3104" check therefore detects the defect.
- **Precedent:** `DockerHubClient.cs:239–242` already skips an item that is not an object.
- **Sibling paths:**
  - In this client, every other place that loops over items reads them through a kind guard, or reads inspect values the area document already describes as a failed inspect (`docker-instances.md`, *Container discovery*). The plan's proposed issue 2 covers history `Tags`. The network values in a stats body (line 686) fail only that one container's sample, not the round.
  - Registry and Docker Hub loops (`ManifestPlatformSelector.cs:41`, `DockerHubClient.cs:467/512`, `OciRegistryClient.cs:705`) are outside this Engine issue.
- **Issue text:** fetched through the REST API. It says "Found while planning #570", which matches record 0069's *Context*.
- **Test layout:** `DockerInstanceClientStatsValuesTests.cs` already sets the precedent of a split class that declares its own sample and helpers. `HistorySample` (`DockerInstanceClientTests.cs:66`) is a private constant, so the new class has to copy it, as the plan says.

**Objection:**
- `minor`: the *Areas* bullet for *Resource statistics* says "a container without an ID". The code skips more than that: `string.IsNullOrWhiteSpace(containerId)` (line 1160) also skips an empty or whitespace-only ID. Instruction for the Dev: write "a container without an ID or with an empty or blank ID, or with an ID longer than 200 characters, is left out without a request". Otherwise the newly documented rule says less than the code does.
