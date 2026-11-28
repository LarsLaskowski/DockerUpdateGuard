# Plan: Leave out Docker Engine string list items that are not JSON strings

Source: Issue #575
Tier: standard — a behavior change of input handling (an inspect or history that failed now succeeds without the item) that creates, changes or removes no security control.
Security areas touched: Docker Engine access (`Docker/DockerInstanceClient.cs`, how it reads Engine answers; no change to the socket, transport or TLS)

## Problem / root cause

`ReadStringArray` (`src/DockerUpdateGuard/Docker/DockerInstanceClient.cs:848–861`) maps every item of the array to
`item.GetString()`. `JsonElement.GetString` answers null for a JSON `null` (dropped by the `IsNullOrWhiteSpace` filter)
but throws `InvalidOperationException` for a number, `true`/`false`, an object or an array. The exception leaves
`ReadImageHistoryAsync` / `ReadImageInspectAsync` and is caught by the `catch (Exception)` of `GetImageHistoryAsync`
(`:1411`) or `InspectImageAsync` (`:1372`), which answers *failed* with `FailureReason.FromException` =
`unexpected error`. `ReadStringArray` serves the history entry's `Tags` (`:817`) and the inspect's `RepoTags` and
`RepoDigests` (`:795–796`).

Related defect, same cause, fixed here: `ParseImageInspect` reads `Config.Env` (`:775–778`) and `RootFS.Layers`
(`:786–789`) with the same inline `GetString` pipeline, so a non-string environment variable or layer fails the inspect
too. Related defect, not fixed here (other area): `OciRegistryClient.TryReadEnvironmentVariables` (`Images/OciRegistryClient.cs:450–465`)
and the image configuration read of `DockerHubClient` (`DockerHub/DockerHubClient.cs:719–728`) — see *Out of scope*.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | `ReadStringArray` calls `GetString` on every item without a kind check | `DockerInstanceClient.cs:856–860` | confirmed |
| 2 | `GetString` returns null for `Null`, throws `InvalidOperationException` for every kind but `String`/`Null` | .NET API docs of `JsonElement.GetString` ("This value's ValueKind is neither String nor Null"); a `null` tag list item is dropped today by the `IsNullOrWhiteSpace` filter (`:858`) | confirmed |
| 3 | The exception becomes *failed* `… failed for '<instance>': unexpected error` | `GetImageHistoryAsync` `:1411–1416`, `InspectImageAsync` `:1372–1377`; `FailureReason.FromException` falls through to `UnexpectedError` for `InvalidOperationException` (`Infrastructure/FailureReason.cs:83`) | confirmed |
| 4 | Env and Layers have the same defect | `:775–778`, `:786–789` | confirmed |
| 5 | Discovery's inspect failure today includes a number in `RepoDigests` (logged 3112 `unexpected error`) | test rows `NumberInRepoDigests` in `DockerInstanceClientTests.cs:2153`, `:2176`, input `:2408`; area `docker-instances.md` *Container discovery* ("holds a value of an unexpected kind") | confirmed |
| 6 | An inspect root that is not an object throws at the first `element.TryGetProperty` (`:770`), a history root that is not an array at `EnumerateArray` (`:957`); both stay failures | code; test rows `RootNotObject` (`DockerInstanceClientTests.cs:2154`, `:2177`) | confirmed |
| 7 | Discovery already keeps only string items of `Names` | `:530–534`, `.Where(nameElement => nameElement.ValueKind == JsonValueKind.String)` | confirmed |
| 8 | Undecodable strings are refused before parsing, independent of this change | `RemoteJsonDocument.ParseAsync` / `EnsureStringsDecode`, record 0019 | confirmed |
| 9 | Real samples: the history example of the Engine API definition (v1.41) is `HistorySample` in `DockerInstanceClientListItemsTests.cs:47–76`; the inspect field examples of the same definition (`api/docs/v1.41.yaml` in moby/moby, definition `ImageInspect`): `Id` `sha256:ec3f0931a6e6b6855d76b2d7b0be30e81860baccd891b2e243280bf1cd8ad710`, `RepoTags` `example:1.0`, `example:latest`, `example:stable`, `internal.registry.example.com:5000/example:1.0`, `RepoDigests` `example@sha256:afcc7f1ac1b49db317a7196c902e61c6c3c4607d63599ee1a82d702d249a0ccb`, `internal.registry.example.com:5000/example@sha256:b69959407d21e8a062e0416bf13405bb2b71ed7a84dde4158ebafacfa06f5578`, `Config.Env` (`ContainerConfig`) `PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin`, `RootFS.Layers` `sha256:1834950e52ce4d5a88a1bbd131c537f4d0e56d10ff0dd69e66be3b7dfa9df7e6`, `sha256:5f70bf18a086007016e948b04aed3b82103a36bea41755b6cddfaf10ace3c6ef` | read in the downloaded definition (Lead); public documentation examples, no private values | confirmed |
| 10 | No other caller relies on a failing inspect or history for such an item | `DockerImageInspectData`/`DockerImageHistoryEntryData` consumers read the lists only; base images keep findings only while a read fails (`base-images.md`, record 0042) | confirmed |

## Acceptance criteria

The JSON kinds an array item can have (`JsonValueKind` without `Undefined`): object, array, string, number, true,
false, null. "Not a string" below means each of the other six.

- AC1: `GetImageHistoryAsync` with a history entry whose `Tags` holds an item that is not a JSON string between two
  strings answers *succeeded*; that entry's tags are the two strings in order, its other fields and the other entries
  are read as without the item.
- AC2: `InspectImageAsync` with an item that is not a JSON string in each of `RepoTags`, `RepoDigests`, `Config.Env`
  and `RootFS.Layers` answers *succeeded*; each list holds exactly its string items in order, the ID is read.
- AC3: Discovery of a container whose image's `RepoDigests` holds an item that is not a JSON string next to the
  matching digest resolves that digest and logs no warning 3112.
- AC4: An inspect answer that is not a JSON object still answers *failed* `Docker image inspect failed for 'Production': unexpected error`;
  a history answer that is not a JSON array still answers *failed* `Docker image history failed for 'Production': unexpected error`.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `DockerInstanceClientGetImageHistoryAsyncWhenTagIsNoStringSkipsItAsync(string itemJson)`, rows `7`, `1.5`, `true`, `false`, `null`, `{ "a": "b" }`, `["x"]`: replaces `"Tags": ["ubuntu:lucid", "ubuntu:10.04"]` of `HistorySample` by the list with the item between the two (asserting the replacement happened); asserts *succeeded*, 3 entries, first entry tags `["ubuntu:lucid", "ubuntu:10.04"]` and its `CreatedBy`, third entry tags `["scratch12:latest", "scratch:latest"]` | `src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientListItemsTests.cs` |
| AC2 | `DockerInstanceClientInspectImageAsyncWhenListItemIsNoStringSkipsItAsync(string itemJson)`, same rows: a new constant `ImageInspectSample`-like inspect answer built from the field examples of fact 9 (doc comment names the API definition), the item inserted after the first string of `RepoTags`, `RepoDigests`, `RootFS.Layers` and before the one `Config.Env` string; asserts *succeeded*, the ID and each list equal to the examples | same |
| AC3 | `DockerInstanceClientDiscoverContainersAsyncWhenRepoDigestIsNoStringResolvesDigestAsync(string itemJson)`, rows `7`, `true`, `{ "a": "b" }`: `ContainerSample` with `{ "RepoDigests": [<item>, "ubuntu@sha256:7777…"] }`; asserts digest `sha256:` + 64 × `7` and no entry 3112 | same |
| AC4 | `DockerInstanceClientInspectImageAsyncWhenBodyIsNoObjectReturnsUnexpectedErrorAsync(string bodyJson)`, rows `[]`, `"text"`, `7`; `DockerInstanceClientGetImageHistoryAsyncWhenBodyIsNoArrayReturnsUnexpectedErrorAsync(string bodyJson)`, rows `{}`, `"text"`, `7`; each asserts *failed* and the exact message | same |

## Approach

- `ReadStringArray`: filter `item.ValueKind == JsonValueKind.String` before the `Select`; the rest stays (control
  character replacement, blank filter). Its summary says that items that are not strings are left out.
- `ParseImageInspect`: read `Config.Env` and `RootFS.Layers` through `ReadStringArray(configElement, "Env")` and
  `ReadStringArray(rootFsElement, "Layers")` inside the existing object guards, removing the two inline pipelines
  (one shared reader, no duplication). The root access order stays: the first `TryGetProperty` on the root keeps
  throwing for a root that is not an object (AC4) — no guard on the root is added.
- No log entry for a left-out item (record 0071, option 5). No new user-visible output.

Early draft PR: no

## Options considered

From record [0071](../../docs/decisions/0071-a-docker-engine-string-list-item-that-is-not-a-json-string-is-left-out.md):
1. Leave out the item in all five lists (chosen).
2. Skip in history tags only, keep failing inspect (the issue's alternative) — two rules for one kind of list; the area
   text described the code, decided nothing.
3. Fail with a fixed message — still discards every readable value.
4. Read the whole list as empty — discards readable values for no gain.
5. Skip and log a warning per item — new log event for input no real Engine sends (as 0069 rejected).

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Docker/DockerInstanceClient.cs` (`ReadStringArray`, `ParseImageInspect`) | kind filter; Env/Layers through the shared reader |
| DockerUpdateGuard.Tests | `DockerInstanceClientListItemsTests.cs`, `DockerInstanceClientTests.cs` | new tests; adapted rows |

## Signatures (for the Dev's skeleton)

None: only private static methods change, their signatures stay. No skeleton pieces; the Tester's tests fail on the
unfixed head (AC1–AC3), AC4 passes there and guards the fix.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientListItemsTests.cs` (new tests of AC1–AC4; class summary
  extended to string list items).

Existing test code whose input a criterion handles differently: the `NumberInRepoDigests` rows of
`DockerInstanceClientDiscoverContainersAsyncWhenImageInspectThrowsSucceedsWithoutThatImagesDigestAsync` and
`…WhenImageInspectThrowsLogsSkippedImageAsync` (`DockerInstanceClientTests.cs:2153`, `:2176`), its case in
`CreateFailingInspectResponseAsync` (`:2408`) and the two `failureKind` param docs (`:2146`, `:2168`): the inspect
now succeeds, so the Tester removes the rows, the case and the name from the param docs (AC3 covers the new behavior;
`RootNotObject` keeps the `unexpected error` case). Owner: Tester. No other test has a non-string item in these lists
(grep of the five member names over `src/Tests`).

Files no unit test can cover: none.

## Areas

- `docs/areas/docker-instances.md (+40 words)` (3325 words now):
  - *Container discovery*, the failed-inspect sentence: "holds a value of an unexpected kind" becomes "is not a JSON
    object".
  - *Image inspection and history*, after the sentence on history entries that are not objects: an item of the repo
    tags, repo digests, environment variables, root file-system layers or a history entry's tags that is not a JSON
    string (a number, `true`/`false`, `null`, an object, an array) is left out and the other items are still read;
    linked to record 0071.

## Documentation updates

Dev, once the code is green:
- `docs/areas/docker-instances.md`: as *Areas*.
- `docs/areas/integration-failures.md`: sweep checked, the Docker Engine field list (`:77–81`) is unchanged (no edit).
- `docs/areas/base-images.md`: no edit (its rule on a failing read stays; fewer reads fail).
- `docs/ARCHITECTURE.md`: no edit (no guarantee or flow changes; sweep for inspect/history found no enumeration).
- No changelog file exists; the PR checklist asks only for area documents and records.

## Architecture check

No guarantee of `.squad/project.md` changes: the client still answers every non-cancellation error as a result, never
throws to callers; remote text is still cleaned of control characters; undecodable strings still fail as invalid response.

## Security considerations

- [x] No limits.
- [x] Exceptions reaching a user-visible reason: unchanged — an inspect root that is not an object or a history root
  that is not an array reaches `unexpected error` (fixed text, AC4); no new exception path.
- A hostile Engine gains nothing: it could already send the lists without the item. A digest left as the only one
  after skipping is chosen by the existing "exactly one" rule, as if the Engine had sent it alone.

## Decision records

- `docs/decisions/0071-a-docker-engine-string-list-item-that-is-not-a-json-string-is-left-out.md` (Proposed), index row added.
- `docs/decisions/0069-…` (unreleased, checked with `git ls-remote --tags origin`: not in `v1.3.10`): its consequence
  that left this case open now points to 0071 (edited in place).

## Out of scope / follow-ups

- Proposed issue, title "[Registries] A non-string environment variable of an image configuration fails the whole
  read": body — `OciRegistryClient.TryReadEnvironmentVariables` (`Images/OciRegistryClient.cs:450–465`) and the image
  configuration read of `DockerHubClient` (`DockerHub/DockerHubClient.cs:719–728`) call `GetString` on every `Env` item,
  so a number or boolean throws and the configuration read fails with `unexpected error`; decide as record 0071 did
  for the Docker Engine. Found while planning #575.
