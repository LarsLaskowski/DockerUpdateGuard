# Plan: Name Docker instance keys in validation messages relative to the instance

Source: Issue #330
Status: Draft
Tier: trivial — only the wording of startup validation messages changes (plus one private helper that formats them); no check, condition or control flow changes, and the choice of wording needs a decision record.
Security areas touched: Secrets in configuration (validation messages: they keep naming keys and instance names, never values), TLS and transport relaxations (wording of the `AllowInsecureHttp` opt-in failure only; the check is unchanged)

## Problem / root cause

`DockerUpdateGuardOptionsValidator` builds every message about a key inside a Docker instance as
`DockerUpdateGuard:DockerInstances:{instance.Name}:<key>`:
`src/DockerUpdateGuard/Configuration/DockerUpdateGuardOptionsValidator.cs` lines 301, 305, 309, 314
(`ValidateDockerInstance`), 333, 339, 343, 347 (twice), 356, 361 (`ValidatePortainerOptions`) and 462
(`ValidateCertificatePath`). `DockerUpdateGuardOptions.DockerInstances` is a list
(`src/DockerUpdateGuard/Configuration/DockerUpdateGuardOptions.cs:57`), so configuration addresses an entry by its
position, and a copied key adds a stray list entry. Related defect on the same path: line 425 reports a missing name
as `'DockerUpdateGuard:DockerInstances:Name'`, which is no key either (it would bind a list child named `Name`).

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | `DockerInstances` is a list | `DockerUpdateGuardOptions.cs:57`, `IList<DockerInstanceOptions>` | confirmed |
| 2 | Instance messages put the instance name where the list position belongs | validator lines 301–361 and 462 as listed above; the issue's range 318–342 is a subset: `ValidateDockerInstance` and `ValidateCertificatePath` have the same defect, so the defect is wider than the issue says | confirmed |
| 3 | The missing-name message names the non-key `DockerUpdateGuard:DockerInstances:Name` | validator line 425; a related defect on the same path | confirmed |
| 4 | The validator receives the bound options, not the configuration keys | `ServiceCollectionExtensions.cs:53-55` (`AddOptions().Bind(optionsSection)` plus the validator); `Validate(string?, DockerUpdateGuardOptions)` at validator line 470 | confirmed |
| 5 | With sparse child keys the bound list position differs from the key | scratch console with .NET SDK 10.0.403: keys `S:Items:1`, `:2`, `:10` bound to positions 0, 1, 2 (measured) | confirmed |
| 6 | The runtime Portainer message already names a key relative to the instance | `PortainerClient.cs:169` (`set Portainer:EndpointId of Docker instance '<name>'`), record 0010 option 7 | confirmed |
| 7 | Only `DockerUpdateGuardOptionsValidatorTests.cs` asserts these texts | `grep -rn "DockerInstances:" src` and `grep "Name' must be configured"` over `src/Tests` | confirmed |

## Acceptance criteria

`<Name>` is the instance's `Name` exactly as configured (as today); the rest of each text is literal.

- [ ] AC1: For a Docker instance with a name, the validator reports exactly these texts (each in the situation it
  reports today), and no failure contains `DockerInstances:` followed by the instance name:
  - `'BaseUrl' of Docker instance '<Name>' must be configured`
  - `'BaseUrl' of Docker instance '<Name>' must use http, https, tcp, unix or npipe`
  - `'BaseUrl' of Docker instance '<Name>' has an '@' that does not end its user info; percent-encode '@', '#', '?' and '/' in a user name or password`
  - `'RequestTimeoutSeconds' of Docker instance '<Name>' must be between 1 and 300`
  - `'CertificatePath' of Docker instance '<Name>' names an unusable client certificate '<path>': <reason>` (path and
    reason unchanged)
  - `'Portainer:BaseUrl' of Docker instance '<Name>' must be configured when Portainer is enabled`
  - `'Portainer:BaseUrl' of Docker instance '<Name>' must be an absolute http or https URI`
  - `'Portainer:BaseUrl' of Docker instance '<Name>' has an '@' that does not end its user info; percent-encode '@', '#', '?' and '/' in a user name or password`
  - `'Portainer' of Docker instance '<Name>' must have either ApiToken or Username+Password configured`
  - `'Portainer:RequestTimeoutSeconds' of Docker instance '<Name>' must be between 1 and 300`
- [ ] AC2: A Portainer `BaseUrl` with scheme `http` and `AllowInsecureHttp` false is reported as exactly
  `'Portainer:BaseUrl' of Docker instance '<Name>' uses plaintext HTTP; API keys, passwords and tokens will be transmitted unencrypted — set 'Portainer:AllowInsecureHttp' of Docker instance '<Name>' to true to explicitly allow it`.
- [ ] AC3: An entry whose `Name` is blank is reported as exactly
  `'Name' must be configured for every entry of 'DockerUpdateGuard:DockerInstances'` (once per such entry, as
  today), and no failure contains `'DockerUpdateGuard:DockerInstances:Name'`.
- [ ] AC4: Messages for keys outside `DockerInstances` are unchanged, the `@` failure of a top-level URL key included
  (`'DockerUpdateGuard:DockerHub:ApiBaseUrl' has an '@' …`), and so is the duplicate-name message.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | new `DockerUpdateGuardOptionsValidatorInvalidDockerInstanceReturnsFailuresRelativeToInstance` (instance `Production`: blank `BaseUrl`, `RequestTimeoutSeconds` 0, Portainer enabled with blank `BaseUrl`, no credentials, `RequestTimeoutSeconds` 0 — the five texts exactly, and no failure contains `DockerInstances:Production`); adapted to the exact new texts: `DockerUpdateGuardOptionsValidatorInvalidConfigurationReturnsRelevantFailures` (scheme, Portainer absolute URI, credentials), `DockerUpdateGuardOptionsValidatorOutOfRangeUpperBoundsReturnsFailures` (both timeouts), `DockerUpdateGuardOptionsValidatorDockerInstanceBaseUrlWithAtSignOutsideUserInfoReturnsFailure`, `DockerUpdateGuardOptionsValidatorPortainerBaseUrlWithAtSignOutsideUserInfoReturnsFailure`, `DockerUpdateGuardOptionsValidatorValidateWithPemCertificatePathFails`, `…WithMissingCertificateFileFails`, `…WithPasswordProtectedPfxFails`, `…WithPfxWithoutPrivateKeyFails`, `…WithUnusableCertificateOnUnixInstanceFails` | `src/Tests/DockerUpdateGuard.Tests/DockerUpdateGuardOptionsValidatorTests.cs` |
| AC2 | adapted `DockerUpdateGuardOptionsValidatorPortainerHttpWithoutAllowInsecureHttpReturnsFailure`: asserts the exact text instead of the substring `AllowInsecureHttp` | same file |
| AC3 | new `DockerUpdateGuardOptionsValidatorDockerInstanceWithoutNameReturnsFailureNamingSection` (entries with `Name` empty and whitespace: the exact text, and no failure contains `'DockerUpdateGuard:DockerInstances:Name'`) | same file |
| AC4 | existing `DockerUpdateGuardOptionsValidatorHttpUrlsWithAtSignOutsideUserInfoReturnFailures` and `DockerUpdateGuardOptionsValidatorInvalidDatabaseOptionsReturnsFailures`, unchanged; the `duplicate instance name` assertion in `…InvalidConfigurationReturnsRelevantFailures` stays | same file |

Tier `trivial`: no tests first. The Dev changes the production code; the adapted tests then fail on the old texts
until the Tester, in step 6, adapts them and adds the two new tests (assertion changes, not signature call sites).

## Approach

In `DockerUpdateGuardOptionsValidator`:

1. Add `DescribeInstanceKey(instanceName, relativeKey)` returning `'<relativeKey>' of Docker instance '<instanceName>'`.
2. Change `CreateAtSignFailure` to take the quoted subject instead of the bare path: it returns
   `<subject> has an '@' that does not end its user info; percent-encode …` (rest unchanged). The two top-level
   callers (lines 94 and 150) pass `$"'{propertyPath}'"`, so AC4's texts stay byte-identical; lines 309 and 343
   pass `DescribeInstanceKey(…)`.
3. Rewrite the messages at lines 301, 305, 314, 333, 339, 347, 356, 361 and 462 to `DescribeInstanceKey(…)` plus
   the unchanged predicate, with the texts of AC1 and AC2; line 425 to the text of AC3.

No condition, `else if` order or failure count changes. Error texts are fixed by the criteria; nothing else is
user-visible.

## Options considered

1. **Relative to the named instance** — chosen; unambiguous for every key layout and configuration source.
2. **Full key with the bound list position** — wrong for sparse keys (fact 5): a copied key then edits another
   instance or adds a stray entry again.
3. **Full key with the configuration's child key** — exact, but re-implements the binder's pairing of entries and
   children and fails silently when they disagree; the validator validates bound options (fact 4).
4. **Position and name together** — inherits option 2's defect.

Details in record 0013.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| `src/DockerUpdateGuard` | `Configuration/DockerUpdateGuardOptionsValidator.cs` | message texts, new helper, `CreateAtSignFailure` parameter meaning |
| `src/Tests/DockerUpdateGuard.Tests` | `DockerUpdateGuardOptionsValidatorTests.cs` | tests of *Tests per criterion* |

## Signatures (for the Dev's skeleton)

No public API changes (step 4 is skipped on this tier). Private members of `DockerUpdateGuardOptionsValidator`, in
the region *Static methods*:

- new `private static string DescribeInstanceKey(string instanceName, string relativeKey)`
- changed `private static string CreateAtSignFailure(string subject)` (was `string propertyPath`; XML doc: the quoted
  subject the failure names)

## Test files

- `src/Tests/DockerUpdateGuard.Tests/DockerUpdateGuardOptionsValidatorTests.cs`

Existing test code that calls a changed signature: none (both members are private). The test helper
`CreateAtSignFailure(string key)` stays for AC4; the instance `@` tests build their expectation with a new test
helper or inline (Tester's choice).

Files of the change no unit test can cover: none.

## Areas

- Hosting and configuration (`docs/areas/hosting.md`, *Validation*): how keys inside a Docker instance are named.
- Docker instances (`docs/areas/docker-instances.md`, *Limits and errors*): the `CertificatePath` message text.

## Documentation updates

All by the Dev, against the code once green. Sweep: `grep -rn "DockerInstances" docs README.md` and the message
texts of AC1–AC3 over `docs/` and `README.md`.

- `docs/areas/hosting.md`, *Validation*: after the first bullet, state that a key inside a Docker instance entry is
  named relative to the entry with the instance's configured name (`'<key>' of Docker instance '<Name>'`, for
  example `'Portainer:BaseUrl' of Docker instance 'Production' must be configured when Portainer is enabled`),
  because configuration addresses the entries by list position, and give the AC3 text for an entry without a name;
  link record 0013. Line 48: "with the full configuration path as key" becomes "naming the key as the other
  validation failures do (a Docker instance key relative to its instance)".
- `docs/areas/docker-instances.md` line 159: the `CertificatePath` message becomes
  `'CertificatePath' of Docker instance '<Name>' names an unusable client certificate '<path>': <reason>`.
- `README.md` lines 183–185 (related defect found in the sweep): the example keys
  `DockerUpdateGuard:DockerInstances[0]:Name`, `…[0]:BaseUrl`, `…[0]:Enabled` are not configuration keys either;
  write them as `DockerUpdateGuard:DockerInstances:0:Name` etc. The section headings `DockerInstances[]` stay (notation
  for a list).
- Checked, no change: `docs/areas/container-actions.md` lines 14 (`<n>` already) and 47–48 ("names the key to set" still
  holds); `docs/ARCHITECTURE.md` (no message texts); record 0007 line 11 (`<n>` already).

## Architecture check

No guarantee of `.squad/project.md` or `docs/ARCHITECTURE.md` is touched: validation stays one unit at startup with
the same rules and failure count; messages still name keys and at most instance names and a certificate path, never
values (`docs/areas/hosting.md`, *Validation*).

## Security considerations

- [ ] No limits: no input is parsed or bounded differently.
- [ ] Exceptions reaching a message: none new; the texts are built from the configured instance name and constant
  key names as before, and `CertificatePath`'s path and fixed reason are unchanged.

## Decision records

- `docs/decisions/0013-docker-instance-keys-are-named-relative-to-the-instance.md` (Proposed), indexed.

## Out of scope / follow-ups

- Naming which nameless entry failed (its position in key order): the shipped configuration has one entry; record
  0013, *Consequences*.
- Cleaning control characters from the echoed instance name: unchanged behavior, not part of this issue.

## Security notes

- `CreateAtSignFailure` changes from taking a bare path to taking an already-quoted subject. If the Dev misses the quotes at either top-level caller (lines 94 and 150), the top-level `@` messages would silently change. The AC4 tests as planned (`DockerUpdateGuardOptionsValidatorHttpUrlsWithAtSignOutsideUserInfoReturnFailures`, unchanged) catch this; they just need to stay exact-match.
- The instance name is still echoed without cleaning out control characters. That is today's behavior and correctly listed as out of scope.