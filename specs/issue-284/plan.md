# Plan: Manual image registration identifies images by their reference, not their name

Source: Issue #284
Tier: standard — changes how manual registration finds and writes observed images (behavior and control flow) without creating or changing a security control.
Security areas touched: Database and migrations (the registration query, built from the user's reference; no schema change); Failure messages and remote text (a new composed `ReportableException` message carrying a stored own-image name); Logging and telemetry of external data (that exception reaches the existing `UserActionFailed` log entry)

## Problem / root cause

`src/DockerUpdateGuard/Images/ImageRegistrationService.cs:92` looks the existing image up with
`SingleOrDefaultAsync(entity => entity.Name == request.Name)`: the raw, untrimmed name, case-sensitive, over all sources,
on a column without a unique index, while `:99` stores `request.Name.Trim()`. The update branch (`:107-113`) writes
whatever row it found, a discovered one included, and moves its `CurrentImageVersionId`. The name is never validated in
the service (the form's `[Required]` only), and `:90` marks the catalog version before anything is checked.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | Lookup by raw name, insert of the trimmed name | `ImageRegistrationService.cs:92`, `:99` (the issue's `:61-91`, `:70`, `:77` are shifted) | confirmed |
| 2 | `Name` has no unique index, max length 200 | `ObservedImageConfiguration.cs:24-26`; only `CurrentImageVersionId` is indexed (`:39`) | confirmed |
| 3 | PostgreSQL `=` on text is case-sensitive | default deterministic collation; no collation set in `ObservedImageConfiguration` | confirmed |
| 4 | Scenarios 1 and 4 (near-duplicates, one reference under two names) | `:92` finds no row for ` web ` or `Web`; nothing compares the version | confirmed |
| 5 | Scenario 2: registration throws on two rows of one name | `SingleOrDefaultAsync` at `:92` | confirmed |
| 6 | Scenario 2: "the form shows that message" | `ObservedImages.razor.cs` maps through `FailureReason.FromException`, an `InvalidOperationException` gives `Registration failed: unexpected error` (`ObservedImagesTests.cs:74`) | refuted |
| 7 | Scenario 3: discovered images are named after their repository and reachable from the form; the update keeps `Source` and moves the version | `DockerHubAccountImageDiscoveryService.cs:226`, `:240`; `ImageRegistrationService.cs:107-113` | confirmed |
| 8 | Scenario 3: the next discovery run moves the image back to the latest `myuser/app` tag | discovery loads only `Source == Discovery` (`:297`) and matches by the current version's repository (`:216`); the taken-over row now points at `library/nginx`, so it is disabled as obsolete (`:309`), stays disabled on nginx, and a second own image is created for `myuser/app` | refuted |
| 9 | `nginx:1.27` and ` docker.io/library/nginx:1.27 ` parse to the same parts today, without #275 | `ImageReferenceParser.cs:106` trims, `:130-139` give `docker.io` / `library/nginx` for both | confirmed |
| 10 | No "ignored" state exists for observed images (#282 is open); a discovered image is disabled only by discovery when its repository left the account | `grep IsIgnored` finds nothing; `DockerHubAccountImageDiscoveryService.cs:309` | confirmed |
| 11 | A discovered image's current version carries the tag's digest, so a manual reference without that digest is another image version | `DockerHubAccountImageDiscoveryService.cs:208-211` passes `selectedTag.Digest`; the catalog keys on registry repository, tag and digest (`image-catalog.md`, *The catalog*) | confirmed |
| 12 | Discovered names are grammar-checked repository paths (`[a-z0-9._/-]`), at most 200 characters | `DockerHubClient.cs:332-340` (`ReplaceControlCharacters`, `RegistryReferenceValidator.IsValidRepository`), discovery `:240` rewrites the name of every listed (enabled) image each run | confirmed |
| 13 | EF Core SQLite cannot translate `ORDER BY` on `DateTimeOffset` | known provider limitation; the host tests run on `SqliteTestDatabase` | confirmed |
| 14 | `TimeProvider` is registered; the service has one DI registration and two test call sites | `ServiceCollectionExtensions.cs:108`, `:133`; `ImageRegistrationServiceTests.cs:50`, `:107` | confirmed |
| 15 | The form allows one registration at a time per page | `_isBusy` disables the submit button (`ObservedImages.razor:53`) | confirmed |

## Acceptance criteria

- AC1: Registering a reference that a manual image already has as its current version — with another name, another
  case or surrounding whitespace in the name — updates that image: name the trimmed new name, description replaced
  (blank → none), enabled again, source *manual*, `UpdatedAtUtc` the injected clock's time; no second observed image.
- AC2: Registering two different references under the same name creates two manual images and does not throw.
- AC3: Registering a reference that is the current version of an **enabled** discovered image throws
  `OwnImageAlreadyTrackedException` with the message `This image is already tracked as own image '<name>'.` (`<name>`
  the discovered image's name) and its `ObservedImageId`; the discovered image (name, description, current version,
  source, enabled state, `UpdatedAtUtc`) and the image version's `Source` are unchanged and no observed image is added.
- AC4: Registering a discovered image's name with a different reference creates a new manual image and leaves the
  discovered image unchanged.
- AC5: Registering the current version of a **disabled** discovered image creates a new manual image and leaves the
  discovered image unchanged.
- AC6: A name blank after trimming fails with `ReportableException` `The name must not be empty`; a trimmed name longer
  than 200 characters with `The name must not be longer than 200 characters`; in both cases nothing is saved and no
  catalog entry is created. A name of 200 characters with surrounding whitespace is accepted.
- AC7: `nginx:1.27` and ` docker.io/library/nginx:1.27 ` (real catalog repository) end in one manual image.
- AC8: With two manual images of one version (from earlier versions), registration updates the one with the earlier
  `CreatedAtUtc`, leaves the other unchanged and does not throw.
- AC9: On the form, an `OwnImageAlreadyTrackedException` shows `Registration failed: This image is already tracked as own
  image '<name>'.` with a link `Open own image` to `/my-images/{id}` and starts no scan; any other failure shows no such
  link.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | ImageRegistrationServiceRegisterSameReferenceTwiceUpdatesExistingImageAsync, ImageRegistrationServiceRegisterTrimsNameAndMatchesByReferenceAsync, ImageRegistrationServiceRegisterDisabledManualImageEnablesItAsync | src/Tests/DockerUpdateGuard.Tests/ImageRegistrationServiceTests.cs |
| AC2 | ImageRegistrationServiceRegisterSameNameDifferentReferenceCreatesSecondImageAsync | src/Tests/DockerUpdateGuard.Tests/ImageRegistrationServiceTests.cs |
| AC3 | ImageRegistrationServiceRegisterReferenceOfDiscoveredImageIsRejectedAsync | src/Tests/DockerUpdateGuard.Tests/ImageRegistrationServiceTests.cs |
| AC4 | ImageRegistrationServiceRegisterDiscoveredImageNameDoesNotTakeOverAsync | src/Tests/DockerUpdateGuard.Tests/ImageRegistrationServiceTests.cs |
| AC5 | ImageRegistrationServiceRegisterReferenceOfDisabledDiscoveredImageCreatesManualImageAsync | src/Tests/DockerUpdateGuard.Tests/ImageRegistrationServiceTests.cs |
| AC6 | ImageRegistrationServiceRegisterBlankNameIsRejectedAsync, ImageRegistrationServiceRegisterTooLongNameIsRejectedAsync, ImageRegistrationServiceRegisterNameOfMaximumLengthAfterTrimmingIsAcceptedAsync | src/Tests/DockerUpdateGuard.Tests/ImageRegistrationServiceTests.cs |
| AC7 | ImageRegistrationServiceRegisterDockerHubSpellingVariantsMatchSameImageAsync | src/Tests/DockerUpdateGuard.Tests/ImageRegistrationServiceTests.cs |
| AC8 | ImageRegistrationServiceRegisterWithExistingDuplicatesUpdatesOldestAsync | src/Tests/DockerUpdateGuard.Tests/ImageRegistrationServiceTests.cs |
| AC9 | ObservedImagesRegistrationOfTrackedOwnImageLinksToOwnImageAsync; ObservedImagesFailedRegistrationShowsFixedReasonAndLogsAsync adapted (asserts no `/my-images/` link) | src/Tests/DockerUpdateGuard.Tests/ObservedImagesTests.cs |

Fixtures: `SqliteTestDatabase` with the rows seeded directly; AC6 and AC7 use the real `ImageCatalogRepository` (AC6
counts `ImageVersions`), the others may keep `TestImageCatalogRepository`. The clock is an NSubstitute `TimeProvider`.

## Approach

`ImageRegistrationService.RegisterAsync`, in this order:

1. Null check of `request`; `string.IsNullOrWhiteSpace(request.Name)` → `ReportableException(EmptyNameMessage)`;
   `name = request.Name.Trim()`; `name.Length > MaxNameLength` → `ReportableException(NameTooLongMessage)` — before
   parsing, so a rejected name creates no catalog entry.
2. Reference check, parse and `GetOrCreateImageVersionAsync` unchanged.
3. `candidates = ObservedImages.Where(entity => entity.CurrentImageVersionId == imageVersion.Id).ToListAsync()` —
   materialized, then ordered in memory by `CreatedAtUtc`, then `Id` (fact 13); the set is the images of one version.
4. The first candidate with `Source == Discovery && IsEnabled` → throw
   `new OwnImageAlreadyTrackedException($"This image is already tracked as own image '{ownImage.Name}'.", ownImage.Id)`.
   Nothing is modified before this point.
5. Then `imageVersion.Source = ImageVersionSource.ObservedImage` (moved from `:90`), and the first candidate with
   `Source == Manual` is updated (name, description, `IsEnabled = true`, `UpdatedAtUtc = now`; version unchanged), or a
   new manual image is added with `CreatedAtUtc` and `UpdatedAtUtc` = `now`; `now = _timeProvider.GetUtcNow()`.
   Candidates of other sources are ignored.
6. One `SaveChangesAsync`.

`ObservedImages.razor.cs`: a private `Guid? _ownImageId`, reset with `_errorMessage` at the start of `RegisterAsync`;
the catch sets it inside the same `InvokeAsync` as the message, from `(exception as OwnImageAlreadyTrackedException)?.ObservedImageId`.
Logging stays as today (`UserActionFailed`). Layout in `ObservedImages.razor`: inside the existing error `MudAlert`, after
the message text and a space, `<MudLink Href="@($"/my-images/{_ownImageId}")">Open own image</MudLink>` when
`_ownImageId` has a value; no other change.

## Options considered

- **Chosen:** identity by image version, discovered images never written, rejection for an enabled discovered image,
  rule in the service ([0048](../../docs/decisions/0048-manual-registration-identifies-an-observed-image-by-its-image-version.md)).
- Normalized name as identity, manual images only — still tracks one reference twice under two names (0048).
- Unique index on the current version of manual images (the issue's note) — a migration over existing duplicates and a
  column discovery moves (0048).
- A manual image beside a discovered one of the same reference — one image tracked twice (0048).
- Reject also for a disabled discovered image — it is no longer tracked (fact 10), and the user could then track that
  image in no way; the issue limits the rejection to tracked images.
- A message without the own image's name — the issue fixes the text; the name is a grammar-checked repository path
  (fact 12), already shown on `/my-images`.
- A result type instead of an exception for the rejection — changes `IImageRegistrationService` and both test files for
  no gain; the page already handles `ReportableException`.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Images/ImageRegistrationService.cs` | identity by version, name validation, `TimeProvider` |
| DockerUpdateGuard | `Images/OwnImageAlreadyTrackedException.cs` (new) | rejection carrying the own image's id |
| DockerUpdateGuard | `Components/Pages/ObservedImages.razor`, `.razor.cs` | link to the own image |

## Signatures (for the Dev's skeleton)

- `public ImageRegistrationService(DockerUpdateGuardDbContext dbContext, IImageCatalogRepository imageCatalogRepository, IImageReferenceParser imageReferenceParser, TimeProvider timeProvider)` (replaces the three-parameter constructor)
- `internal const int MaxNameLength = 200;`
- `internal const string EmptyNameMessage = "The name must not be empty";`
- `internal const string NameTooLongMessage = "The name must not be longer than 200 characters";`
- `public sealed class OwnImageAlreadyTrackedException : ReportableException` in `DockerUpdateGuard.Images`, with
  `public OwnImageAlreadyTrackedException()`, `public OwnImageAlreadyTrackedException(string message)`,
  `public OwnImageAlreadyTrackedException(string message, Exception innerException)`,
  `public OwnImageAlreadyTrackedException(string message, Guid observedImageId)` and
  `public Guid ObservedImageId { get; }` (the standard constructors as `PortainerAuthenticationException`)

## Test files

- `src/Tests/DockerUpdateGuard.Tests/ImageRegistrationServiceTests.cs`
- `src/Tests/DockerUpdateGuard.Tests/ObservedImagesTests.cs`

Existing call sites: the two constructor calls in `ImageRegistrationServiceTests.cs` (`:50`, `:107`) — the Dev adds
`TimeProvider.System` in step 4. `ObservedImagesFailedRegistrationShowsFixedReasonAndLogsAsync` gets the no-link
assertion from the Tester (AC9). `ObservedImagesPersistentStateTests` is unaffected.

Files no unit test can cover: none.

## Areas

- Image catalog: *Manual registration*, *Enabled state*, *Limits and errors*, *Known gaps*, *Related decisions*.
- Web UI: *Register and scan*, the page-error rule for composed messages.
- Integration failures: composed messages may carry a stored own-image name.

## Documentation updates

All by the Dev, against the code as built:

- `docs/areas/image-catalog.md`: rewrite *Manual registration* steps 2–3 (validation of the trimmed name, match by
  image version among manual images, oldest first, rejection for an enabled discovered image, ignored disabled
  discovered images, name and description replaced, version unchanged); *Enabled state*: "of an existing reference";
  *Limits and errors*: the two name messages, the own-image rejection text, delete the whitespace-name bullet, add that
  earlier duplicates stay (deletable once #282 lands), simultaneous registrations can create two, and a reference that
  differs from an own image's version in digest or tag registers as manual (fact 11); *Known gaps*: delete the #284
  entry; *Related decisions*: link 0048.
- `docs/areas/web-ui.md`: the *Register and scan* row (`:67`) and the bullet at `:79` (name trimmed, rejection with the
  `Open own image` link); `:166` names the own-image and name messages beside the rejected reference.
- `docs/areas/integration-failures.md:38`: composed messages may also carry the stored name of an own image.

## Architecture check

The catalog's single write path is untouched (`GetOrCreateImageVersionAsync` only). Observed images keep their owners:
discovery writes discovered images, registration now only manual ones. No schema, migration or layering change;
`docs/ARCHITECTURE.md` needs no update (its `ObservedImage` row stays true).

## Security considerations

- [x] Limits: the name limit of 200 applies to the trimmed .NET string (UTF-16 code units), the column's own unit; no
  decoding involved.
- [x] Exceptions reaching the message: the two name messages and the own-image message (fixed text plus a stored
  name that is a grammar-checked repository path, fact 12); everything else keeps its fixed reason. The query compares
  only a `Guid`, parameterized by EF Core.

## Decision records

- `docs/decisions/0048-manual-registration-identifies-an-observed-image-by-its-image-version.md` (Proposed)

## Out of scope / follow-ups

- Merging existing duplicates; disable and delete (#282); further Docker Hub spellings (#275).
- A manual reference of an own repository without the discovered digest is a separate image (fact 11); one identity
  across sources would need a repository-level rule, not proposed here.
