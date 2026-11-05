# Plan: Validate `DockerHub:UserName` as a Docker Hub namespace at startup

Source: Issue #363
Status: Draft
Tier: security — the check confines which path of the Docker Hub web API the account listing, with the account's bearer token, reaches (a guard on a URL, `.squad/routing.md`, *Tiers*).
Security areas touched: Registry and Docker Hub calls

## Problem / root cause

`DockerHubAccountImageDiscoveryService.SynchronizeAccountImagesAsync`
(`src/DockerUpdateGuard/Images/DockerHubAccountImageDiscoveryService.cs:273-277`) passes the trimmed
`DockerHub:UserName` to `DockerHubClient.GetRepositoriesAsync`, which builds
`new Uri(apiBaseUri, $"v2/repositories/{Uri.EscapeDataString(accountName)}/?page_size=100")`
(`src/DockerUpdateGuard/DockerHub/DockerHubClient.cs:1246`). `EscapeDataString` leaves `.` alone and `System.Uri`
removes `.` and resolves `..` segments, so `..` requests `v2/?page_size=100` and `.` requests
`v2/repositories/?page_size=100`, authenticated. `DockerUpdateGuardOptionsValidator.ValidateDockerHubOptions`
(`src/DockerUpdateGuard/Configuration/DockerUpdateGuardOptionsValidator.cs:93-119`) checks `Registry`, the ranges and
`ApiBaseUrl`, never `UserName`.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | The account listing puts the user name into the path with `Uri.EscapeDataString` only | `DockerHubClient.cs:1246`; caller `DockerHubAccountImageDiscoveryService.cs:273-277` (trimmed value), the only caller (`grep GetRepositoriesAsync src/DockerUpdateGuard`) | confirmed |
| 2 | `.`/`..` in that segment address another path (issue claim) | `EscapeDataString` leaves `.` unescaped and `System.Uri` removes/resolves dot segments, also written as `%2E` (measured for #360: decision 0014, options 14-15; `docs/areas/registries.md`, *Request paths*) | confirmed |
| 3 | `UserName` is not validated today | `DockerUpdateGuardOptionsValidator.cs:93-119` | confirmed |
| 4 | The validator runs at startup and on every reload before a value is read | `ServiceCollectionExtensions.cs:53-56` (`IValidateOptions` + `ValidateOnStart`); `OptionsFactory.Create` runs every `IValidateOptions` when `IOptionsMonitor` rebuilds the value; `docs/areas/hosting.md`, *Configuration reload* | confirmed |
| 5 | The account listing uses the value trimmed and treats blank as not set (the Docker Scout login, `DockerScoutVulnerabilityProvider.cs:252`, sends it untrimmed in a JSON body, not a path) | `DockerHubClient.cs:682-685`, `1053-1060`; `DockerHubAccountImageDiscoveryService.cs:266-273`; `DockerScoutVulnerabilityProvider.cs:252`, `432`; `NavMenu.razor.cs:66`, `Dashboard.razor.cs:117` | confirmed |
| 6 | The values in repository configuration and test data fit the new rule | `appsettings.json` (`""`), `README.md` sample and `TestDevelopmentConfiguration.cs:24` (`dockerupdateguard`); validator tests' `CreateValidOptions` sets no user name | confirmed |
| 7 | `RegexOptions.IgnoreCase` and `ToLowerInvariant` fold `\u212A` (Kelvin sign) to `k`, which is why the grammar uses explicit `[a-zA-Z0-9]` classes | to verify: the `\u212Acme` row of AC1 fails red before the fix and passes after it, owner Tester (step 5) and Dev (step 6) | unverified |

## Acceptance criteria

- [ ] AC1: A non-blank `DockerHub:UserName` whose trimmed value (`string.Trim()`) is not one component of the
  namespace grammar `\A[a-zA-Z0-9]+(?:(?:[._]|__|-+)[a-zA-Z0-9]+)*\z` of at most 255 characters fails
  `DockerUpdateGuardOptionsValidator.Validate` with exactly
  `'DockerUpdateGuard:DockerHub:UserName' must be a Docker Hub user name: letters and digits, optionally separated by '.', '_' or '-'`,
  and no other failure, so no failure carries the configured value (the text does not depend on it; no separate
  substring check, which the case `.` would fail against the text's own `'.'`). Cases: `.`, `..`, ` .. `, `...`, `.acme`, `acme.`, `acme/..`,
  `../acme`, `acme/x`, `%2e%2e`, `acme?x`, `acme#x`, `ac me`, `acme:pw`, `me@example.com`, `a___b`, `acme-`,
  `\u212Acme` (Kelvin sign), `ac\u200Bme` (zero-width space), and 256 `a`.
- [ ] AC2: These values pass validation: `null`, `""`, `"   "`, `acme`, `Acme`, `  acme  `, `\u00A0acme\u00A0`
  (no-break spaces, trimmed like the consumers trim), `my.org`, `my_org`, `my__org`, `my-org`, `my---org`, `a1b2`,
  `a`, and 255 `a`.

The C# escapes `\u212A`, `\u200B`, `\u00A0` are written as escapes in the test source. An AC1 row that lost its
backslash (`u212Acme`, `acu200Bme`) becomes a valid name, so the row fails red; the AC2 no-break-space case is its own test,
which asserts `char.IsWhiteSpace(userName[0])` unconditionally first, so a lost backslash there fails that guard.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `DockerUpdateGuardOptionsValidatorInvalidDockerHubUserNameReturnsFailure` (one `DataRow` per AC1 case; the failures are exactly the fixed text) | `src/Tests/DockerUpdateGuard.Tests/DockerUpdateGuardOptionsValidatorTests.cs` |
| AC2 | `DockerUpdateGuardOptionsValidatorValidDockerHubUserNameReturnsSuccess` (one `DataRow` per AC2 case except the no-break-space one); `DockerUpdateGuardOptionsValidatorNoBreakSpaceDockerHubUserNameReturnsSuccess` (`const string userName = "\u00A0acme\u00A0";`, unconditional `Assert.IsTrue(char.IsWhiteSpace(userName[0]))` before validating) | `src/Tests/DockerUpdateGuard.Tests/DockerUpdateGuardOptionsValidatorTests.cs` |

Both tests start from `CreateValidOptions()` with `CreateAtSignValidator()`, so AC1 asserts that the failures are
exactly the one fixed text (`Assert.AreSequenceEqual`), which also proves that no failure carries the value; the
test has no separate `Contains(value)` assertion. The
255/256-character values are built in the test (`new string('a', n)`), not typed.

## Approach

1. `RegistryReferenceValidator` (`src/DockerUpdateGuard/Images/Helper/RegistryReferenceValidator.cs`) gets a
   `[GeneratedRegex(@"\A[a-zA-Z0-9]+(?:(?:[._]|__|-+)[a-zA-Z0-9]+)*\z", RegexOptions.CultureInvariant, 100)]`
   `NamespaceExpression()` — explicit ASCII classes, no `IgnoreCase`, no lowercasing (fact 7) — and
   `IsValidNamespace`, which checks non-empty, length `<= MaxRepositoryLength` (255) **before** the regex, then the
   match.
2. `ValidateDockerHubOptions` adds, after the `Registry` rule: when `string.IsNullOrWhiteSpace(options.UserName) ==
   false` and `RegistryReferenceValidator.IsValidNamespace(options.UserName.Trim()) == false`, the AC1 failure. The
   validator checks exactly the value the consumers send (fact 5).
3. No change to `DockerHubClient` (options considered, 4).

Forms the consumer accepts (guard against bypasses): the value reaches `Uri.EscapeDataString` and then
`new Uri(base, relative)`. `EscapeDataString` escapes everything but `A-Za-z0-9-._~`, so `/`, `?`, `#`, `\`, `%`,
spaces and non-ASCII arrive encoded and stay inside the segment (`%2e` arrives as `%252e`). The only forms that leave
the segment are a segment exactly `.` or `..` (fact 2); `~` is not dangerous but outside the grammar. The grammar
rejects both dot forms (it starts and ends with `[a-zA-Z0-9]`), every character outside ASCII letters, digits, `.`,
`_`, `-`, and so every case of AC1. Surrounding white space is trimmed by validator and consumers alike, so ` .. `
is checked as `..`. A value of more than 255 characters fails on its length before the regex runs, so the regex
timeout (100 ms) cannot throw out of the validator.

## Options considered

1. Reject only `.` and `..` — lost: keeps `/`, `:` and other non-names; a `:` splits the registry token request's
   basic credentials (`DockerHubClient.cs:685`).
2. Namespace grammar, ASCII of either case, 255 bound — chosen.
3. Docker ID sign-up rule (4-30 lower-case letters and digits) — lost: stops configurations with upper-case letters
   or older names for no security gain.
4. Also refuse a dot segment in `GetRepositoriesAsync` — lost: the validator covers startup and reload (fact 4), and
   decision 0014 (option 19) already rejected the client check for this value.

Recorded in `docs/decisions/0018-the-docker-hub-user-name-is-validated-as-a-namespace-at-startup.md`.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| `DockerUpdateGuard` | `Images/Helper/RegistryReferenceValidator.cs` | `NamespaceExpression`, `IsValidNamespace` |
| `DockerUpdateGuard` | `Configuration/DockerUpdateGuardOptionsValidator.cs` | `UserName` rule in `ValidateDockerHubOptions` |

## Signatures (for the Dev's skeleton)

In `RegistryReferenceValidator` (internal static partial class), region *Regular expressions* and *Methods*:

```csharp
private static partial Regex NamespaceExpression();
public static bool IsValidNamespace([NotNullWhen(true)] string? namespaceName)
```

`ValidateDockerHubOptions(DockerHubOptions, List<string>)` keeps its signature.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/DockerUpdateGuardOptionsValidatorTests.cs`

Existing test call sites of a changed signature: none. Existing tests kept unchanged: none of them sets
`DockerHub:UserName` to a value outside the grammar (fact 6).

Files no unit test can cover: none.

## Areas

- `docs/areas/registries.md`: the validation bullet of *Configuration reload* gains the `UserName` rule; the
  *Known gaps* entry for #363 is removed.

## Documentation updates

Owner: Dev, once the code is green, against the code.

- `docs/areas/registries.md` — *Configuration reload*, bullet "Validation of these keys at startup": add that a
  non-blank `UserName`, trimmed, must be a Docker Hub user name (ASCII letters and digits, separated by `.`, `_`,
  `__` or any run of `-`, at most 255 characters), with the AC1 failure text, at startup and on reload; link 0018.
  *Known gaps*: delete the #363 entry. *Related decisions*: add 0018.
- `README.md` — `DockerUpdateGuard:DockerHub` table, `UserName` row: "Docker Hub user name (Docker ID) for
  authenticated API requests; letters, digits and `.`, `_`, `-` between them, validated at startup".
- `docs/ARCHITECTURE.md` — *Security posture*, the bullet ending in record 0014: add that the configured Docker Hub
  user name, the path segment of the account listing, must be a Docker Hub user name to pass validation, linking 0018.
- Sweep done: `docs/areas/hosting.md` (*Validation* delegates feature-key rules to their areas; the `@` rule list
  is about URL keys only — no change), `docs/areas/vulnerabilities.md:32`, `docs/areas/web-ui.md:25,98,117`
  (mention the key, no enumeration of rules — no change), `INSTALL.md:85`, `docs/.env.example`, compose files (no
  rule text — no change).

## Architecture check

No guarantee of `.squad/project.md` changes: validation stays one `IValidateOptions` step at startup and reload;
integration clients keep their outcomes; layering unchanged (the grammar stays in `Images/Helper`, used by the
validator in the same project). `docs/ARCHITECTURE.md` update as above, owner Dev.

## Security considerations

- [ ] Limits: the 255-character bound counts UTF-16 characters of the trimmed configuration value, before any
  escaping; no decoding happens.
- [ ] Exceptions reaching a message: none — the length bound precedes the regex, so `RegexMatchTimeoutException`
  cannot occur on a bounded ASCII-class pattern; the failure text is fixed and carries no value.

## Decision records

- `docs/decisions/0018-the-docker-hub-user-name-is-validated-as-a-namespace-at-startup.md` (Proposed)

## Out of scope / follow-ups

- A check in `DockerHubClient.GetRepositoriesAsync` itself (option 4).
- How Docker Hub treats upper-case letters or an e-mail address as login identifier: not known, and the rule does
  not rest on it (0018, *Consequences*).

## Challenge notes

Devil's Advocate, mode plan: OBJECTIONS 0 major, 2 minor (appended verbatim).

1. minor — Verified fact 5 is wrong for one consumer, so do not rely on it beyond the URL path. `DockerScoutVulnerabilityProvider.cs:252` sends `hubOptions.UserName!` untrimmed in the Docker Scout login body; it does not use the trimmed value. `MyImages.razor.cs:178` also shows the raw value. Instruction for the Dev and Reviewer: leave both unchanged, because they are not path-bearing and not part of #363. Do not write in the docs or in 0018 that every consumer trims the value. Neither the ARCHITECTURE bullet nor the registries.md bullet may claim this. Why minor: the security property only needs the account-listing path to be trimmed, and it is (`DockerHubAccountImageDiscoveryService.cs:273`).

2. minor — The test section names `CollectionAssert.AreEqual`, but the sibling at-sign tests in `src/Tests/DockerUpdateGuard.Tests/DockerUpdateGuardOptionsValidatorTests.cs` (around lines 862 and 900) use `Assert.AreSequenceEqual([...], ...)`. Instruction for the Tester: assert the exact single failure the way those tests do. Use `Assert.AreSequenceEqual` with the fixed AC1 text and an assertion message, so the new tests match their neighbours. Why minor: either assertion catches the defect; this is only about consistency in the file.

## Security notes

Security, mode plan: APPROVED, no blocking findings (non-blocking finding appended verbatim).

1. Fact 5 says every consumer trims the value, but `DockerScoutVulnerabilityProvider.AuthenticateAsync` (`src/DockerUpdateGuard/Vulnerabilities/DockerScoutVulnerabilityProvider.cs:252`) sends `hubOptions.UserName!` untrimmed. It goes into a JSON body, not a URL path, so it has no security effect and the guard is not weakened. The Lead should correct the wording of fact 5 (or the decision draft's Context), not the code. Leading or trailing spaces sent to the Scout login are existing behavior and out of scope here.

Fact 7 was measured by Security (a one-off .NET 10 app): `IgnoreCase` with `[a-z0-9]` matches the Kelvin sign and `ToLowerInvariant` folds it to `k`; the plan's pattern rejects it. The 255-character check precedes the regex, whose near-miss inputs ran in under 1 ms.
