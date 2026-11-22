# Plan: The registration form checks the stored length limits

Source: Issue #343
Tier: standard — a behavior change of the registration form and service (new validation messages, no database round trip for over-long input) that creates, changes or removes no security control: a length bound that keeps oversized input from failing at the database is input handling.
Security areas touched: Failure messages and remote text (new fixed `ReportableException` texts the `/observed-images` page shows)

## Problem / root cause

`ObservedImageRegistrationRequest` (`src/DockerUpdateGuard/Images/Data/ObservedImageRegistrationRequest.cs:15,26`)
carries only `[Required]`, so the form's `DataAnnotationsValidator` (`ObservedImages.razor`) never checks a length.

- **Reference parts:** `ImageRegistrationService.RegisterAsync` (`ImageRegistrationService.cs:119`) hands the parsed
  registry, repository, tag and digest to `IImageCatalogRepository.GetOrCreateImageVersionAsync` unchecked. On
  PostgreSQL an over-long part fails the insert with a `DbUpdateException`, which `ImageCatalogRepository`
  (`GetOrCreateRegistryRepositoryAsync`, `GetOrCreateImageVersionAsync`) takes for a lost race: it re-reads with
  `SingleAsync`, which throws `InvalidOperationException`, and `FailureReason.FromException` (`FailureReason.cs:85`)
  turns it into `unexpected error`. With only the tag or digest too long, the registry repository is saved first and
  stays.
- **Name:** the issue's claim is out of date. Since #502 (a79bea3) the service rejects a trimmed name over 200
  characters with `The name must not be longer than 200 characters` before anything reaches the catalog
  (`ImageRegistrationService.cs:96-106`); only the form's own check before sending is missing.

Related defect, not fixed here (follow-up below): the `catch (DbUpdateException)` of both get-or-create methods treats
every failed save as a lost race and loses the original error.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | A name over 200 characters fails at the database with `unexpected error` (issue) | `ImageRegistrationService.cs:96-106` throws `ReportableException(NameTooLongMessage)` before parsing; the page shows `Registration failed: The name must not be longer than 200 characters` (`docs/areas/image-catalog.md`, *Limits and errors*) | refuted |
| 2 | The form reports no length before sending | `ObservedImageRegistrationRequest.cs`: only `[Required]` on `Name` and `ImageReference`; no `IValidatableObject`, no length attribute | confirmed |
| 3 | Over-long reference parts reach the database and end as `unexpected error` (issue) | `ImageRegistrationService.cs:119-124` passes parts unchecked; `ImageCatalogRepository.cs` catches `DbUpdateException` and re-reads with `SingleAsync` (throws `InvalidOperationException`); `FailureReason.cs:85` → `UnexpectedError`; area document states the same | confirmed |
| 4 | Stored limits: name 200, registry 200, repository 300, tag 200, digest 255; description unlimited | `ObservedImageConfiguration.cs:25` (name), `:28` (`text`); `RegistryRepositoryConfiguration.cs:25,29`; `ImageVersionConfiguration.cs:28,32` | confirmed |
| 5 | The stored values are exactly the parsed parts | registry, repository and tag are passed as parsed (`ImageRegistrationService.cs:119-122`); the catalog's `NormalizeDigest` (trim, text after the last `@`) leaves the output of `ImageReferenceParser.NormalizeDigest` (already trimmed, cut after its last `@`) unchanged | confirmed |
| 6 | The limits cannot be checked on the raw text: parsing changes lengths | `ImageReferenceParser.Parse` adds `library/` to a single-segment Docker Hub repository, drops the registry segment and the `:tag`/`@digest` separators, trims the tag and digest | confirmed |
| 7 | `[Required]` rejects a whitespace-only string, so object-level validation never sees a blank name or reference | `RequiredAttribute.AllowEmptyStrings` defaults to `false`; `IsValid` then rejects `string.IsNullOrWhiteSpace` (.NET documentation) | confirmed |
| 8 | On submit, `DataAnnotationsValidator` calls `Validator.TryValidateObject(…, validateAllProperties: true)`, which runs `IValidatableObject.Validate` once every property attribute passed, and maps each result's member names to the form's fields; `<ValidationSummary />` lists them; `OnValidSubmit` is then not invoked | to verify: the AC1 and AC2 page tests in `ObservedImagesTests` (Tester, step 5; green in step 6, the Dev reports it as a plan deviation otherwise). Basis: the ASP.NET Core Forms source as recalled; no `AddValidation` in the host (`grep`), so no generated-validation path | unverified |
| 9 | The page is the only caller of `RegisterAsync`, through `EditForm OnValidSubmit` | `ObservedImages.razor` (`OnValidSubmit="RegisterAsync"`), `ObservedImages.razor.cs:190`; `grep RegisterAsync(` | confirmed |
| 10 | The SQLite test database does not enforce `varchar(n)` lengths, so a service test cannot observe the PostgreSQL failure | SQLite ignores the length of a declared type (sqlite.org, *Datatypes*); the new service tests assert the exception and the absence of rows instead | confirmed |
| 11 | No other code refers to the name constants the plan moves | `grep MaxNameLength\|EmptyNameMessage\|NameTooLongMessage`: only `ImageRegistrationService.cs` (`DockerInstanceClient` and the discovery service have private constants of their own); tests compare literal texts | confirmed |

## Acceptance criteria

- AC1: Submitting the form with a name longer than 200 characters after trimming (UTF-16 code units, as today) shows
  `The name must not be longer than 200 characters` in the form's validation messages and calls
  `IImageRegistrationService.RegisterAsync` not at all; a name of 200 characters with surrounding whitespace passes.
- AC2: Submitting the form with an image reference whose parsed part exceeds its stored limit shows that part's text in
  the form's validation messages and calls `RegisterAsync` not at all: `The registry must not be longer than 200
  characters`, `The repository must not be longer than 300 characters`, `The tag must not be longer than 200
  characters`, `The digest must not be longer than 255 characters`. With several parts too long only the first of
  that order is reported; a part at exactly its limit passes.
- AC3: The limits count the parsed parts as stored, not the typed text: a Docker Hub single-segment repository of 293
  characters (301 with `library/`) is rejected, one of 292 passes; surrounding whitespace of tag and digest and the
  `@` separator do not count.
- AC4: The service rejects the same over-long parts with a `ReportableException` carrying the AC2 text before the
  catalog is reached: no registry repository, image version or observed image is saved, and the page would show
  `Registration failed: <text>`. The name checks keep their texts.
- AC5: The limits of the rules equal the maximum lengths of the EF Core model (`ObservedImage.Name`,
  `RegistryRepository.Registry` and `.Repository`, `ImageVersion.Tag` and `.Digest`).
- AC6: A reference the parser rejects (no repository name) gets no form message from the length check; the service
  still fails it with `Registration failed: The image reference does not contain a repository name`.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `ObservedImagesTooLongNameIsReportedWithoutRegistrationAsync`; `ObservedImageRegistrationRequestValidateTooLongNameReturnsNameMessage`, `ObservedImageRegistrationRequestValidateMaximumNameWithWhitespaceReturnsNoResult` | `ObservedImagesTests.cs`, `ObservedImageRegistrationRequestTests.cs` |
| AC2 | `ObservedImagesTooLongTagIsReportedWithoutRegistrationAsync`; `ObservedImageRegistrationRulesGetPartsErrorRegistryOverLimitReturnsRegistryMessage`, `…RepositoryOverLimitReturnsRepositoryMessage`, `…TagOverLimitReturnsTagMessage`, `…DigestOverLimitReturnsDigestMessage`, `…PartsAtLimitReturnNull`, `…SeveralPartsOverLimitReturnsFirstMessage` (each `ObservedImageRegistrationRulesGetPartsError…`); `ObservedImageRegistrationRequestValidateTooLongReferencePartReturnsReferenceMessage` | `ObservedImagesTests.cs`, `ObservedImageRegistrationRulesTests.cs`, `ObservedImageRegistrationRequestTests.cs` |
| AC3 | `ObservedImageRegistrationRulesGetReferenceErrorDockerHubRepositoryWithLibraryPrefixOverLimitReturnsRepositoryMessage`, `ObservedImageRegistrationRulesGetReferenceErrorDockerHubRepositoryWithLibraryPrefixAtLimitReturnsNull`, `ObservedImageRegistrationRulesGetReferenceErrorPaddedTagAndDigestAtLimitReturnsNull` | `ObservedImageRegistrationRulesTests.cs` |
| AC4 | `ImageRegistrationServiceRegisterTooLongTagIsRejectedBeforeCatalogAsync`, `ImageRegistrationServiceRegisterTooLongRepositoryIsRejectedBeforeCatalogAsync` | `ImageRegistrationServiceTests.cs` |
| AC5 | `ObservedImageRegistrationRulesLimitsMatchModelMaxLengths` | `ObservedImageRegistrationRulesTests.cs` |
| AC6 | `ObservedImageRegistrationRulesGetReferenceErrorReferenceWithoutRepositoryReturnsNull`; existing `ImageRegistrationServiceRegisterAsyncReferenceWithoutRepositoryThrowsReportableExceptionAsync` (unchanged) | `ObservedImageRegistrationRulesTests.cs`, `ImageRegistrationServiceTests.cs` |

## Approach

1. **One rule set, two callers.** A new `internal static class ObservedImageRegistrationRules`
   (`src/DockerUpdateGuard/Images/`) holds the limits, the texts and three checks (*Signatures*): the trimmed name
   (blank, too long), the parts of a parsed `ImageReference` in the order registry, repository, tag, digest (`Length`
   of the string, UTF-16 code units, never more than the column's character count), and a raw reference, which it
   parses with a new `ImageReferenceParser` (stateless; the composition root registers the same type) and then checks
   as parts. A blank reference, or one the parser rejects with `ArgumentException`, gives `null`: `[Required]` and the
   service report those as today.
2. **Form.** `ObservedImageRegistrationRequest` implements `IValidatableObject`: `Validate` yields one
   `ValidationResult` with member `Name` for a name error and one with member `ImageReference` for a reference error
   (both when both apply). It needs no service from the `ValidationContext`. It runs on submit only, after `[Required]`
   passed (*Verified facts* 7, 8).
   Layout: no new markup; the texts appear where the `[Required]` messages appear today, at the field (MudBlazor's
   error text through `For`) and as an item of the form's existing `<ValidationSummary />` — no `Registration failed:`
   prefix, no error alert, no log entry, since nothing failed. `ObservedImages.razor` and its code-behind stay as they are.
3. **Service.** `ImageRegistrationService.RegisterAsync` replaces its inline name checks by `GetNameError` (same
   texts) and, after a successful parse and before `GetOrCreateImageVersionAsync`, throws
   `ReportableException(GetPartsError(...))` when it returns a text. The three name constants move to the rules class.
4. Tests: the rules and request tests need no database except AC5, which reads the model of a `SqliteTestDatabase`
   context (`Model.FindEntityType(...).FindProperty(...).GetMaxLength()`). The AC4 tests assert, besides the
   exception text, that `RegistryRepositories`, `ImageVersions` and `ObservedImages` are empty afterwards.

Early draft PR: no

## Options considered

1. **Chosen:** `IValidatableObject` on the request plus one shared static rule set the service also enforces, on the
   parsed parts.
2. **`[StringLength]`/`[MaxLength]` on the request's properties** — lost: the name limit holds for the trimmed name (a
   200-character name with whitespace is documented as accepted), and the stored parts differ in length from the
   typed reference (*Verified facts* 6).
3. **Custom `ValidationAttribute` types per property** — would report on field change, not only on submit; lost: two
   more types, the reference attribute still has to parse, and submit-time feedback meets the issue.
4. **Length checks in `ImageCatalogRepository`** — would protect every writer; lost: the form would still report
   nothing before sending, the other writers already bound remote parts ([0014](../../docs/decisions/0014-registry-tag-names-and-digests-are-validated-where-they-are-read.md)),
   and it changes the data layer's single write path for a failure only manual registration can produce.
5. **Service check only** — fixes `unexpected error` but not what the issue asks: the form reporting before sending.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Images/ObservedImageRegistrationRules.cs` | new |
| DockerUpdateGuard | `Images/Data/ObservedImageRegistrationRequest.cs` | implements `IValidatableObject` |
| DockerUpdateGuard | `Images/ImageRegistrationService.cs` | uses the rules; name constants removed |
| DockerUpdateGuard.Tests | the four test files below | new tests |

## Signatures (for the Dev's skeleton)

New file `src/DockerUpdateGuard/Images/ObservedImageRegistrationRules.cs`, namespace `DockerUpdateGuard.Images`:

- `internal static class ObservedImageRegistrationRules`
- `internal const int MaxNameLength = 200;`, `internal const int MaxRegistryLength = 200;`,
  `internal const int MaxRepositoryLength = 300;`, `internal const int MaxTagLength = 200;`,
  `internal const int MaxDigestLength = 255;`
- `internal const string EmptyNameMessage = "The name must not be empty";`,
  `internal const string NameTooLongMessage = "The name must not be longer than 200 characters";`,
  `internal const string RegistryTooLongMessage = "The registry must not be longer than 200 characters";`,
  `internal const string RepositoryTooLongMessage = "The repository must not be longer than 300 characters";`,
  `internal const string TagTooLongMessage = "The tag must not be longer than 200 characters";`,
  `internal const string DigestTooLongMessage = "The digest must not be longer than 255 characters";`
- `internal static string? GetNameError(string? name)` — `EmptyNameMessage`, `NameTooLongMessage` or `null`
- `internal static string? GetPartsError(ImageReference reference)` — the first part's text or `null`
- `internal static string? GetReferenceError(string? imageReference)` — parses, then as `GetPartsError`; `null` for a
  blank or unparsable reference

Changed: `public class ObservedImageRegistrationRequest : IValidatableObject` with
`public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)` in a region `IValidatableObject`.

Deleted: `ImageRegistrationService.MaxNameLength`, `ImageRegistrationService.EmptyNameMessage` and
`ImageRegistrationService.NameTooLongMessage` are removed (moved to the rules class);
`ReferenceWithoutRepositoryMessage` stays.

All names checked against *Writing code*: no nested type, at most one parameter, no keyword-named parameter. The
skeleton holds nothing for real; the page needs no change, so every new test fails on the skeleton.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/ObservedImageRegistrationRulesTests.cs` (new)
- `src/Tests/DockerUpdateGuard.Tests/ObservedImageRegistrationRequestTests.cs` (new)
- `src/Tests/DockerUpdateGuard.Tests/ImageRegistrationServiceTests.cs` (added tests)
- `src/Tests/DockerUpdateGuard.Tests/ObservedImagesTests.cs` (added tests)

Existing test code that calls a changed signature: none (the tests compare the name texts as literals, *Verified
facts* 11). The existing tests keep their inputs, all within the limits; the name tests of `ImageRegistrationServiceTests`
keep their texts through `GetNameError`.

Files of the change no unit test can cover: none.

## Areas

- `docs/areas/image-catalog.md (+70 words)` (2005 now): *Manual registration* step 2 also checks the parsed parts
  against the catalog limits before anything reaches the catalog; *Limits and errors*: the form reports an over-long
  name or reference part before sending, the limits count the parsed parts as stored (`library/` included), the
  service rejects them with the four texts as `Registration failed: <text>`; the `unexpected error` sentence and the
  parenthesis about a remaining image version go for these two cases.
- `docs/areas/web-ui.md (+30 words)` (2876 now): the sentence "the form reports a missing value before anything is
  sent" names the too-long name and the over-long reference part too, shown at the field and in the form's summary.

## Documentation updates

- `docs/areas/image-catalog.md` and `docs/areas/web-ui.md` as under *Areas* (Dev, after the code is green).
- Sweep: `docs/ARCHITECTURE.md`, `README.md` and the other area documents hold no statement of the registration's
  validation or of `unexpected error` for it (`grep -i "unexpected error|stored limits|200 characters"`); no
  changelog exists. Nothing else.

## Architecture check

No guarantee of `.squad/project.md` is touched: the catalog keeps its one get-or-create write path (the check runs
before it), failure texts stay fixed (`ReportableException` with constant texts, record 0009), layering unchanged.

## Security considerations

- [x] Limits count UTF-16 code units of the parsed .NET strings after trimming, lowercasing and normalization — no
  decoding involved; a code-unit count is never below the column's character count, so the check never admits a value
  the column refuses.
- [x] User-visible texts: the four new constant texts and the existing name and reference texts, through
  `ReportableException` and `FailureReason.FromException`; none contains input. The form messages are the same
  constants. No other exception changes its path.

## Decision records

- none: no decision beyond the obvious fix the issue prescribes; the options are recorded above.

## Out of scope / follow-ups

- Proposed issue: **"Image catalog get-or-create takes every failed save for a lost race"** — `ImageCatalogRepository.GetOrCreateRegistryRepositoryAsync` and `GetOrCreateImageVersionAsync` catch every `DbUpdateException`, detach the entity and re-read with `SingleAsync`; a failure that is not a unique-key collision (a length overflow, a lost connection) surfaces as `InvalidOperationException` ("Sequence contains no elements") and the original error is neither rethrown nor logged. Catch only the unique-violation and rethrow otherwise.
- The form's handling of a reference without a repository name (AC6 keeps it in the service).
