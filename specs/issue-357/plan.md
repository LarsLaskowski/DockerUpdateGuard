# Plan: Image configuration labels whose names differ only in case fail the read

Source: Issue #357
Tier: standard — a behavior change in both registry clients' configuration reader (a configuration that failed is now read) that creates, changes or removes no security control.
Security areas touched: Registry and Docker Hub calls

## Problem / root cause

Both clients fold the `config.Labels` object of an image configuration blob into
`ToDictionary(name, value, StringComparer.OrdinalIgnoreCase)`:

- `OciRegistryClient.TryReadLabels`, `src/DockerUpdateGuard/Images/OciRegistryClient.cs:630-634` (the issue's lines
  599-603 are stale);
- `DockerHubClient.ExtractImageConfigurationAsync`, `src/DockerUpdateGuard/DockerHub/DockerHubClient.cs:933-937`
  (issue: 856-861, stale — those lines are the base-image read).

`ToDictionary` throws `ArgumentException` on the second of two names equal under the comparer. The exception
reaches the catch-all of `OciRegistryClient.ExecuteAsync` / `DockerHubClient.GetImageConfigurationAsync`
(`DockerHubClient.cs:1680`) and becomes *failed* with `FailureReason.UnexpectedError`
(`Infrastructure/FailureReason.cs:83`). Effects: `RegistryBaseImageResolver` (the registered `IBaseImageResolver`,
`ServiceCollectionExtensions.cs:121`) cannot read the observed image's configuration, so resolution is unavailable
and the scan becomes *failed* or *partial* ([Base images](../../docs/areas/base-images.md#resolving-the-chain));
`ImageScanOrchestrator.CreateDerivedBaseRuntimeFindingAsync` (`ImageScanOrchestrator.cs:348-354`) silently skips
runtime detection because the environment variables are lost with the rest.

Related defect, fixed by the same change: a label name that repeats **exactly** throws the same way (fact 3).

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | Both clients build the label dictionary with `ToDictionary(..., OrdinalIgnoreCase)` | `OciRegistryClient.cs:630-634`, `DockerHubClient.cs:933-937`; issue's line numbers are stale | confirmed |
| 2 | Two names differing only in case make the read throw `ArgumentException` | `Enumerable.ToDictionary` throws on a duplicate key under the given comparer; scratch run (fact 3) | confirmed |
| 3 | `JsonDocument.Parse` (default options, as `RemoteJsonDocument.ParseAsync` uses) accepts exact duplicate names, `EnumerateObject` yields all of them, `TryGetProperty` returns the last, and an ordinal `ToDictionary` throws | scratch console app on net10.0 with `{"a":"1","A":"2","a":"3"}`: `TryGetProperty a -> 3`, `a=1,A=2,a=3`, `Ordinal ToDictionary: ArgumentException` | confirmed |
| 4 | The exception surfaces as *failed* / `unexpected error`, the scan as *failed*/*partial* | `FailureReason.cs:49-83`; `DockerHubClient.cs:1680-1685`; `OciRegistryClient.GetImageConfigurationAsync` → `ExecuteAsync`; base-images.md *Resolving the chain* | confirmed |
| 5 | Labels of the configuration are consumed only by `TryGetValue` on the two fixed OCI names | `RegistryBaseImageResolver.cs:87,93`; repo-wide grep of `.Labels` | confirmed |
| 6 | The documented lookup is case-insensitive | `docs/areas/registries.md:76` "labels (keys compared case-insensitively)" | confirmed |
| 7 | The clients' own `ResolveBaseImagesAsync` chains read the base-image labels with case-sensitive `TryGetProperty` (`OciRegistryClient.cs:1094,1101`, `DockerHubClient.cs:869,876`), and nothing in production calls them (`DockerHubBaseImageResolver` is not registered; `RegistryMetadataService.ResolveBaseImagesAsync` has no caller) | grep of `.ResolveBaseImagesAsync(` and registrations | confirmed |
| 8 | Go's `encoding/json` writes map keys sorted, so of names differing only in case the all-lowercase one comes last in a Docker/BuildKit configuration | Go `encoding/json.Marshal` documentation ("The map keys are sorted"); moby image config `Labels map[string]string`; an uppercase ASCII letter sorts before its lowercase; only the rationale of record 0021 rests on it, no test or security property | confirmed |
| 9 | Label names and values are author-controlled free text; names are not cleaned | `integration-failures.md:77-78`; record 0009 option 28 | confirmed |

## Acceptance criteria

- AC1: `ImageConfigurationLabelReader.ReadLabels` given labels whose names differ only in case returns one entry for
  them, carrying the name and the value of the one that comes last in the object; a lookup by any case of the name
  finds it, and the other labels are kept.
- AC2: Given a label name that repeats exactly, `ReadLabels` keeps the value of the last occurrence.
- AC3: A label whose value is not a JSON string is skipped as today and does not replace an earlier string label of a
  colliding name; a value still has its control characters replaced (`ResponseBodyExcerpt.ReplaceControlCharacters`,
  `null` → empty string) as today.
- AC4: `OciRegistryClient.GetImageConfigurationAsync` on a configuration whose labels are
  `Org.Opencontainers.Image.Base.Name` followed by `org.opencontainers.image.base.name` (different values), plus an
  `Env` entry, answers *succeeded*; `Labels["org.opencontainers.image.base.name"]` is the second value and the
  environment variable is kept.
- AC5: The same as AC4 for `DockerHubClient.GetImageConfigurationAsync`.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `ImageConfigurationLabelReaderReadLabelsWhenNamesDifferOnlyInCaseKeepsTheLast` | `src/Tests/DockerUpdateGuard.Tests/ImageConfigurationLabelReaderTests.cs` |
| AC2 | `ImageConfigurationLabelReaderReadLabelsWhenNameRepeatsKeepsTheLast` | `src/Tests/DockerUpdateGuard.Tests/ImageConfigurationLabelReaderTests.cs` |
| AC3 | `ImageConfigurationLabelReaderReadLabelsWhenLaterValueIsNotStringKeepsTheEarlier`, `ImageConfigurationLabelReaderReadLabelsWhenValueHasControlCharactersReplacesThem` | `src/Tests/DockerUpdateGuard.Tests/ImageConfigurationLabelReaderTests.cs` |
| AC4 | `OciRegistryClientGetImageConfigurationAsyncWhenLabelNamesDifferOnlyInCaseKeepsTheLastAsync` | `src/Tests/DockerUpdateGuard.Tests/OciRegistryClientTests.cs` |
| AC5 | `DockerHubClientGetImageConfigurationAsyncWhenLabelNamesDifferOnlyInCaseKeepsTheLastAsync` | `src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs` |

The reader tests build their input with `JsonDocument.Parse` on inline JSON and pass `RootElement`; AC4/AC5 follow
the existing `...GetImageConfigurationAsyncWhenFieldsHaveControlCharactersReplacesThemAsync` arrangement of each
class (`SequenceHttpMessageHandler` / `CreateBaseImageChainResponse`). The AC1 test asserts the kept key name via
`Keys` (it must be the last label's name, which a plain indexer assignment would not give).

## Approach

1. New `internal static class ImageConfigurationLabelReader` in `src/DockerUpdateGuard/Images/Helper/` (next to
   `BaseImageLabelReader`). `ReadLabels` returns a new `Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)`;
   for an element that is not an object it returns it empty. It walks `EnumerateObject()` in order, skips a property
   whose value kind is not `String`, and for each other one does `labels.Remove(name)` then
   `labels.Add(name, ReplaceControlCharacters(value) ?? string.Empty)` — no `ToDictionary`, no indexer, so the last
   label keeps its own name (AC1).
2. `OciRegistryClient.TryReadLabels`: keep its guard; replace the `EnumerateObject()…ToDictionary(...)` return with
   `return ImageConfigurationLabelReader.ReadLabels(labelsElement);`.
3. `DockerHubClient.ExtractImageConfigurationAsync`: replace the `labelsElement.EnumerateObject()…ToDictionary(...)`
   branch of the `labels` conditional with `ImageConfigurationLabelReader.ReadLabels(labelsElement)`.

No user-visible output is added. No change to the base-image `TryGetProperty` reads (fact 7).

## Options considered

The winner rule — last wins (chosen), first wins, case-sensitive keys, exact-name preference, rejecting the
configuration, dropping colliding labels — is weighed in
[0021](../../docs/decisions/0021-of-image-configuration-labels-with-colliding-names-the-last-one-wins.md).
Structure: fixing both call sites in place with the same loop lost to one shared reader, because the two copies are
already identical and the rule must not drift between clients.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Images/Helper/ImageConfigurationLabelReader.cs` | new |
| DockerUpdateGuard | `Images/OciRegistryClient.cs` (`TryReadLabels`) | call the reader |
| DockerUpdateGuard | `DockerHub/DockerHubClient.cs` (`ExtractImageConfigurationAsync`) | call the reader |
| DockerUpdateGuard.Tests | the three test files below | new / extended |

## Signatures (for the Dev's skeleton)

```csharp
namespace DockerUpdateGuard.Images.Helper;

/// Reads the labels of an image configuration
internal static class ImageConfigurationLabelReader
{
    /// Read the string-valued labels; of names equal case-insensitively the last one wins
    public static Dictionary<string, string> ReadLabels(JsonElement labelsElement);
}
```

Usings: `System.Text.Json`, `DockerUpdateGuard.Infrastructure` (for `ResponseBodyExcerpt`). Existing signatures
unchanged.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/ImageConfigurationLabelReaderTests.cs` (new)
- `src/Tests/DockerUpdateGuard.Tests/OciRegistryClientTests.cs` (extended)
- `src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs` (extended)

Existing test call sites of a changed signature: none. Existing label tests (`…WhenFieldsHaveControlCharactersReplacesThemAsync`,
the base-image chain tests) have no colliding names, so their inputs behave the same: kept unchanged.

Code no unit test can cover: none.

## Areas

- [Registries](../../docs/areas/registries.md): *What can be read*, the image configuration bullet gains the
  collision rule; *Related decisions* lists 0021.

## Documentation updates

Owner: Dev, once green.

- `docs/areas/registries.md` line 76: after "labels (keys compared case-insensitively" add that of labels whose names
  are equal case-insensitively, or repeat, the one that comes last in the configuration is kept, with its name and
  value; add a *Related decisions* bullet for
  [0021](../../docs/decisions/0021-of-image-configuration-labels-with-colliding-names-the-last-one-wins.md).
- Sweep: `docs/ARCHITECTURE.md` (lines 268, 296-300, 475) and `base-images.md` (*Resolving the chain*) name the labels
  but state no lookup rule — no change; `integration-failures.md:77-78` (label names stay as received) still holds;
  no changelog in the repository; `README.md` no change.

## Architecture check

No guarantee of `.squad/project.md` or flow of `docs/ARCHITECTURE.md` changes: the clients still return
`RegistryImageConfigurationData` with a case-insensitive `Labels` map, the composition root is untouched, and fixed
failure reasons ([0009](../../docs/decisions/0009-integration-results-carry-fixed-failure-reasons.md)) still apply —
one source of `unexpected error` disappears.

## Security considerations

- [x] No limits: no length or count bound on labels is added or changed (none exists today).
- [x] Exceptions reaching a user-visible reason: none new; the `ArgumentException` that produced `unexpected error`
  is removed. Which colliding label wins is author-controlled either way (fact 9), so it decides nothing an attacker
  could not set directly; the base-image value keeps passing through `BaseImageLabelReader`'s grammar checks
  ([0014](../../docs/decisions/0014-registry-tag-names-and-digests-are-validated-where-they-are-read.md)).

## Decision records

- `docs/decisions/0021-of-image-configuration-labels-with-colliding-names-the-last-one-wins.md` (Proposed, indexed)

## Out of scope / follow-ups

- Proposed issue — title: "Remove or align the unused client-side base-image chain resolution". Body: "`OciRegistryClient.ResolveBaseImagesAsync`
  and `DockerHubClient.ResolveBaseImagesAsync` read the base-image labels with case-sensitive `TryGetProperty`, while the
  registered `RegistryBaseImageResolver` reads them through the case-insensitive configuration labels (#357, record
  0021). Nothing in production calls the client-side chains (`DockerHubBaseImageResolver` is not registered,
  `RegistryMetadataService.ResolveBaseImagesAsync` has no caller). Remove them, or make them use the configuration
  labels, so the two paths cannot disagree."
- Real sample: the change does not alter which fields of the configuration format are read, only how colliding names
  in an already parsed JSON object are folded; the variants (case-differing names, exact repeats) are JSON-structure
  variants, written as inline JSON like the existing configuration tests. No sample escalation.

## Challenge notes

1. minor: the end-to-end tests cannot tell "last wins" from "lowercase wins". AC4 and AC5 put the lowercase name second, so a build that always prefers the lowercase or exact-case name would also pass them. Instruction for the Tester: in the AC1 reader test, put the mixed-case name (for example `Org.Opencontainers.Image.Base.Name`) after the lowercase one. Then assert that `Labels["org.opencontainers.image.base.name"]` returns the mixed-case label's value and that `Keys` holds only the mixed-case name. This proves the record 0021 rule (position in the document, not case) and not just the Go-sorted ordering.
2. minor: the plan names the wrong test helper for Docker Hub. The plan says AC4 and AC5 follow "`SequenceHttpMessageHandler` / `CreateBaseImageChainResponse`". In fact `OciRegistryClientTests.cs:1612` uses `SequenceHttpMessageHandler`, while `DockerHubClientTests.cs:1644` uses `DelegateHttpMessageHandler` with `CreateBaseImageChainResponse` (helper at line 2404). Instruction for the Tester: copy each class's own `...WhenFieldsHaveControlCharactersReplacesThemAsync` arrangement as it stands.

## Security notes

1. Non-blocking: the plan names the security area wrongly. *Security areas touched* says "Registry and Docker Hub calls", but `.squad/project.md:38` lists it as "Registry and Docker Scout calls" (the entry covers `DockerHubClient.cs` and `OciRegistryClient.cs`). Smallest fix, verbatim: in `specs/issue-357/plan.md`, replace `Security areas touched: Registry and Docker Hub calls` with `Security areas touched: Registry and Docker Scout calls`.
