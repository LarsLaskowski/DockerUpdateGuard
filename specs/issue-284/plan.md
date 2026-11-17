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

## Challenge notes

Devil's Advocate, plan mode, issue #284 (`/home/user/DockerUpdateGuard/specs/issue-284/plan.md`, record `/home/user/DockerUpdateGuard/docs/decisions/0048-manual-registration-identifies-an-observed-image-by-its-image-version.md`).

The plan holds up. I found no major objections: no refuted claim, no missed sibling path, no wrong tier. Two minor objections are about test fixtures.

What I checked and found correct:
- **Root cause.** The service looks images up by the raw name at `ImageRegistrationService.cs:92` and stores the trimmed name at `:99`. No other code looks an observed image up by `Name`: the only `.Name ==` match on observed images in `src/DockerUpdateGuard` is `:92`.
- **Sources.** Observed images are only ever created as `Manual` or `Discovery`. `ConfigurationFile` is used only by `DockerInstance` and `InstanceDiscoveryService.cs:102`, so "candidates of other sources are ignored" is safe.
- **Rejection message reaches the form.** `FailureReason.cs:53-56` passes `ReportableException` messages through, and it uses `is`, so the subclass `OwnImageAlreadyTrackedException` gets the same treatment. AC9 as written works.
- **Link target.** The route `/my-images/{ObservedImageId:guid}` exists (`MyImageDetail.razor:1`).
- **Catalog writes on rejection.** `GetOrCreateImageVersionAsync` saves only when it creates a new version (`ImageCatalogRepository.cs:184-187`, `:205-210`). On the rejection path (AC3) the version already exists, so "nothing saved" holds. Validating the name before parsing makes AC6's "no catalog entry" hold.
- **Spelling variants.** The parser trims and canonicalises `nginx:1.27` to `docker.io` / `library/nginx` (`ImageReferenceParser.cs:106`, `:130-134`), so AC7 does not depend on #275. Fact 9 is confirmed.
- **Digest gap.** Discovered images carry the tag's digest (`DockerHubAccountImageDiscoveryService.cs:208-211`), and versions are matched exactly on digest (`ImageCatalogRepository.cs:108-111`). So the rejection only fires for a reference that includes that digest. The plan already says this in fact 11, record 0048's Consequences and the *Limits and errors* doc update, so it is a known limit, not an objection.

Minor objections (for the plan's *Challenge notes*, to the Tester):

1. **minor — AC2 and AC4 cannot be tested with `TestImageCatalogRepository`.** Its `GetOrCreateImageVersionAsync` returns the same version for every reference (`src/Tests/DockerUpdateGuard.Tests/Helper/TestImageCatalogRepository.cs`). Two different references would both resolve to one version and register as one image. Instruction: in the AC2 and AC4 tests, use the real `ImageCatalogRepository` (as AC6 and AC7 do) or seed two distinct versions, so each reference resolves to its own `ImageVersion`.
2. **minor — AC3's "image version's `Source` unchanged" check would pass on a wrong build.** Discovery already sets a discovered image's version to `ImageVersionSource.ObservedImage` (`DockerHubAccountImageDiscoveryService.cs:217`). A fixture seeded that way would not notice if the code set `Source` before the rejection check. Instruction: seed the discovered image's version with a different `Source` (for example `ImageVersionSource.RuntimeContainer`) and assert it still has that value after the rejection.

## Security notes

Security plan review: specs/issue-284/plan.md and docs/decisions/0048-manual-registration-identifies-an-observed-image-by-its-image-version.md (mode plan, round 1)

I found no blocking findings. The plan is approved, with two non-blocking findings for the Lead.

**Tier.** `standard` is right. The new name checks (blank, more than 200 characters) only keep bad input from breaking the program. `.squad/routing.md` (`security` row, line 89) calls that input handling, not a guard. The rule "registration never writes a discovered image" decides which entity the single user's action changes. It does not decide what an attacker may reach. Nothing in authentication, secrets, allow-lists, URL or path confinement, output escaping or network exposure changes.

**Security areas touched.** The three listed areas are complete. I checked for others: the new `MudLink` targets an existing route (`MyImageDetail.razor:1`, `@page "/my-images/{ObservedImageId:guid}"`) and interpolates only a `Guid`. There is no new dependency, no CI, Docker or build change, and no schema or migration change.

**What I checked:**
- **Database.** Today `ImageRegistrationService.cs:92` filters on the user's name string. The new query filters on `CurrentImageVersionId == imageVersion.Id`, a `Guid` that EF Core parameterizes, so user text no longer reaches a query predicate. The version is still created through the single write path, `GetOrCreateImageVersionAsync`. Moving `imageVersion.Source` after the rejection is correct: AC3 then leaves the version's `Source` unchanged.
- **Name limit.** The 200 limit matches `ObservedImageConfiguration` (`HasMaxLength(200)`). The name is checked before parsing, so a rejected name creates no catalog row (AC6 counts `ImageVersions`).
- **Failure message.** `FailureReason.FromException` passes a `ReportableException`'s message through unchanged (`FailureReason.cs:53-55`), so the new message reaches the page as composed. The page renders `_errorMessage` with Razor, which HTML-encodes it (`ObservedImages.razor:21`).
- **Fact 12 (discovered names are checked repository paths).** Confirmed in the current code:
  - `DockerHubClient.cs:330-340` replaces control characters, lowercases, and blanks any name that fails `RegistryReferenceValidator.IsValidRepository`.
  - `DockerHubAccountImageDiscoveryService.cs:240` rewrites the name of every listed image on each run.
  - Discovery is the only place that sets `Name` on a discovery-sourced row; today's registration update branch does not set it.
- **Logging.** `UserActionFailed` (`ComponentLoggingExtensions.cs:19`) already logs the exception with its message. `docs/areas/telemetry.md` (*Credentials and external data*) allows repository names from remote answers in logs, with control characters replaced. No logger arguments change.
- **No measurement needed.** The plan claims no security property that rests on measured or library behavior. Fact 13 (SQLite cannot order by `DateTimeOffset`) is a test-fixture fact, not a security claim. I ran no probes.

**Findings:**

1. [NON-BLOCKING] Plan, *Security considerations*, second bullet, and *Options considered* ("the name is a grammar-checked repository path (fact 12)"). That guarantee only covers names written since the control-character replacement and the validation were added: 1437e01 (#359) and 2bee289 (#362). An installation upgraded from an older release can hold an enabled discovered row whose name was stored before then. If discovery no longer runs there (PAT or user name removed), the row stays enabled and is never rewritten. Its name would then appear unescaped in the composed exception message, and through `UserActionFailed` in the log. The page itself is safe because Razor encodes the output. The risk is low: it needs an old, malicious Docker Hub answer for the user's own account.
   - Smallest fix: compose the message from `ResponseBodyExcerpt.ReplaceControlCharacters(ownImage.Name)`. That is one call, and AC3's expected text stays the same for valid names.
   - Recommendation: fix now in this change, and word the plan's claim as "a stored name with its control characters replaced".

2. [NON-BLOCKING] Plan, *Security considerations*, first bullet: "UTF-16 code units, the column's own unit" is inaccurate for PostgreSQL. `varchar(200)` counts characters (code points), not UTF-16 code units. The check is still safe: a string with at most 200 UTF-16 units never has more than 200 code points. Its only side effect is that a name of supplementary-plane characters is rejected below 200 visible characters.
   - Smallest fix: reword it to "the limit counts UTF-16 code units of the trimmed .NET string, which is never more than the column's character count". The Dev should not carry the original wording into `docs/areas/image-catalog.md`, *Limits and errors*.
   - Recommendation: fix now; it is a wording change only.
