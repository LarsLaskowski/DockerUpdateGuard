# Plan: Discovery leaves out a listed container without an ID

Source: Issue #574
Tier: standard — discovery stops recording containers it recorded so far (a behavior change in the reader of the Engine's container list), with no security control created, changed or removed.
Security areas touched: none

## Problem / root cause

`ParseContainers` (`src/DockerUpdateGuard/Docker/DockerInstanceClient.cs:979–994`) skips an item only when it is no
JSON object or `HasStorableId` (`:509–514`) answers false, and `HasStorableId` answers true when `TryGetString(element,
"Id")` is null (`containerId is null || …`), and true for an empty or blank ID. `ParseContainer` (`:445–467`) then
records `ContainerId = TryGetString(…) ?? string.Empty`, and `GetPrimaryName` (`:521–538`) falls back to that ID, so
without `Names` the name is empty too. Resource sampling (`ReadContainerSamplesAsync`, `:1170–1175`) leaves out
`string.IsNullOrWhiteSpace(containerId) || containerId.Length > MaxContainerIdLength`. The two readers of one list apply
two rules.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | `HasStorableId` answers true for a missing ID | `DockerInstanceClient.cs:509–514` | confirmed |
| 2 | It answers true for an empty or blank ID and for an `Id` that is not a string (`TryGetString` answers null for a non-string kind); the issue names only the missing ID | `:513`; `TryGetString` `:546–560` | confirmed |
| 3 | Discovery records such a container with ID `""` (or the blank string) and, without a string in `Names`, an empty or blank name | `ParseContainer` `:453–454`, `GetPrimaryName` `:537` | confirmed |
| 4 | Sampling leaves out a missing, empty or blank ID | `:1170–1175` (the issue says `:1160`, line drift) | confirmed |
| 5 | A control character in the ID is replaced by a space before both checks, so an ID of control characters only is blank | `TryGetString` → `ResponseBodyExcerpt.ReplaceControlCharacters` `:556` | confirmed |
| 6 | Such a snapshot cannot be addressed: the detail route `/runtime-containers/{DockerInstanceId:guid}/{ContainerId}` has no empty segment; `CreateContainerKey` trims the ID, so empty and blank IDs of one instance merge | `RuntimeContainerDetail.razor:1`; `ApplicationViewService.cs:181–184` | confirmed |
| 7 | Nothing downstream rejects an empty ID: the orchestrator copies it into the snapshot, the column is required but accepts `""`, the index is not unique | `RuntimeContainerScanOrchestrator.cs:187`; `ContainerSnapshotConfiguration.cs:24–26, 59–64` | confirmed |
| 8 | No existing test feeds discovery or sampling a container object without a string `Id` | scan of every `{…}` block with `"Names"`, `"State"` or `"ImageID"` and no `"Id"` in `src/Tests/DockerUpdateGuard.Tests/*.cs`: no hit | confirmed |
| 9 | No existing test covers a missing or blank ID in sampling; the over-long ID has one test per reader (`…WhenContainerIdExceedsStoredLengthSkipsContainerAsync`, `DockerInstanceClientTests.cs:1518, 1612`) | grep of the test methods | confirmed |
| 10 | The real Engine always sends a string `Id`; the list item sample of the Engine API definition (v1.41) is in the repository as `ContainerSample` | `DockerInstanceClientListItemsTests.cs:20–35` | confirmed |
| 11 | Record 0069 is unreleased (added in `0c9a6c6`, after `v1.3.9`) | `git log --diff-filter=A`, `git ls-remote --tags origin` | confirmed |

## Acceptance criteria

- AC1: Discovery leaves out a container-list object whose `Id` is missing, `null`, a number, empty (`""`), blank
  (spaces) or only a control character (`"\u0001"`), and still returns the other containers of the list; the result
  is *succeeded*.
- AC2: Discovery of a list whose only object has no `Id` (but an `ImageID`) succeeds with no containers and sends no
  image inspect request.
- AC3: Resource sampling still leaves out the same IDs as AC1 without a stats request and samples the other containers
  (a pin of the refactored sampling filter).
- AC4: The ID length rule is unchanged in both readers: an ID of exactly 200 characters is kept, one of 201 left out
  (the existing tests of fact 9, unchanged).

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `DockerInstanceClientDiscoverContainersAsyncWhenContainerIdIsMissingOrBlankSkipsContainerAsync` (one `DataRow` per form of AC1) | `src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientListItemsTests.cs` |
| AC2 | `DockerInstanceClientDiscoverContainersAsyncWhenListHoldsOnlyContainersWithoutIdReturnsEmptySucceededAsync` | `src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientListItemsTests.cs` |
| AC3 | `DockerInstanceClientCollectContainerResourceUsageAsyncWhenContainerIdIsMissingOrBlankSkipsContainerAsync` (same rows as AC1; passes on today's code, so deferred to step 6) | `src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientListItemsTests.cs` |
| AC4 | `DockerInstanceClientDiscoverContainersAsyncWhenContainerIdExceedsStoredLengthSkipsContainerAsync`, `DockerInstanceClientCollectContainerResourceUsageAsyncWhenContainerIdExceedsStoredLengthSkipsContainerAsync` (existing, unchanged) | `src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientTests.cs` |

The tests of AC1–AC3 build the list from `ContainerSample` (fact 10): the kept container is the sample as it stands,
the left-out one the sample with its `Id` member removed or replaced by the row's value (and a distinct `Names` entry,
so an assertion can tell them apart). AC1 asserts the single returned container carries the sample's ID
`8dfafdbc3a40`; the control-character row's guard against a lost backslash is that same assertion — without the
backslash the ID `u0001` is kept and the list has two containers (`.squad/team.md`, *Escapes in files*). AC3 asserts
that no stats request other than `containers/8dfafdbc3a40/stats` is sent.

## Approach

Early draft PR: no

- Replace `HasStorableId(JsonElement)` by a private static `TryGetStorableId(JsonElement element)` returning `string?`:
  the cleaned `Id` when it is a non-blank string of at most `MaxContainerIdLength` characters, otherwise null (the
  rule of `:1171–1172`, moved). One helper, so the two readers cannot drift again (the quality gate's duplication
  condition).
- `ParseContainers` skips an object for which `TryGetStorableId` answers null; the JSON-object check stays first
  (record 0069). `ReadContainerSamplesAsync` takes its `containerId` from `TryGetStorableId` and skips on null; its
  inline check goes. `ParseContainer` and `GetPrimaryName` stay as they are: with the ID required, the name's fallback
  is never empty.
- A left-out container is logged nowhere, as the over-long ID and the non-object item (record 0070, option 5).
- No new user-visible output.

## Options considered

1. **Leave the container out of discovery by the sampling rule, one shared helper (chosen).**
2. **Keep recording it with an empty ID** (no change) — lost: the record is unaddressable and two merge (fact 6).
3. **Substitute an ID (name, generated)** — lost: may collide with a real container's ID; actions address by ID.
4. **Fail the discovery** — lost: hides every readable container, as record 0069 argued for non-object items.
5. **Leave it out with a warning per container** — lost: a new log event for input no real Engine sends.

Details in record 0070.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Docker/DockerInstanceClient.cs` | `HasStorableId` → `TryGetStorableId`; `ParseContainers` and `ReadContainerSamplesAsync` use it |
| DockerUpdateGuard.Tests | `DockerInstanceClientListItemsTests.cs` | three tests (AC1–AC3) |

## Signatures (for the Dev's skeleton)

None: the change adds no API member a test or another type calls. The private helper is described in *Approach*; no
skeleton (step 4 skipped). Its name and single parameter meet the naming rules of *Writing code* (`Try…` returning a
nullable value is the pattern of the neighboring `TryGetString`, `TryGetLabel`).

## Test files

- `src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientListItemsTests.cs` (existing class for the reading of list
  items; AC1–AC3).

Existing test code that calls a changed signature: none (the helper is private).

Files of the change no unit test can cover: none.

## Areas

- `docs/areas/docker-instances.md` (+35 words): *Container discovery*, first bullet — the left-out containers read
  "A container without an ID, with an empty or blank ID, or with an ID longer than 200 characters, and an item of the
  list that is not a JSON object, is left out." (the wording sampling already uses at `:161–163`); the decision list
  at the end gets the line for 0070 after 0069.

## Documentation updates

- `docs/areas/docker-instances.md` — as *Areas* (Dev, after the code is green).
- Sweep: `docs/areas/*.md`, `docs/ARCHITECTURE.md` and `README.md` for "left out" and "longer than 200" — only
  `docker-instances.md:94–95` (discovery), `:161–163` (sampling, already right), `:123` and `:252` (history and
  non-object items, unaffected). The `dockerupdateguard.runtime_containers` gauge (`telemetry.md:92`) counts the
  current set and needs no change. No changelog exists; the PR template asks for nothing else.

## Architecture check

No guarantee of `docs/ARCHITECTURE.md` or `.squad/project.md` changes: the Engine client still sends the same requests
over the same transports (*Docker Engine access* is not touched: what reaches the socket is unchanged, discovery only
inspects fewer images when a container is left out). The one-failing-instance rule and the current-set rule hold.

## Security considerations

- [x] No limits: the 200-character bound is unchanged and counts the cleaned ID, as today.
- [x] No exception reaches a user-visible message: a left-out container is skipped silently.

## Decision records

- `docs/decisions/0070-discovery-leaves-out-a-docker-engine-container-without-an-id.md` (Proposed), indexed; a decision
  of its own beside 0069 (unreleased, fact 11), which decided non-object items.

## Out of scope / follow-ups

- A warning per left-out container (record 0070, *Consequences*): no issue, revisit only on operator need.