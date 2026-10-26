# Plan: Reject configured URLs whose `@` is not their user-info separator

Source: Issue #325
Status: Draft
Tier: security — the change adds a validation guard that confines which host a configured URL, and the credentials sent with it (Portainer API key or password, Docker Hub PAT, Docker Scout login, client certificate), can reach.
Security areas touched: Secrets in configuration; Container actions (Portainer `BaseUrl`); Docker Engine access (instance `BaseUrl`); Registry and Docker Hub calls (`DockerHub:ApiBaseUrl`, absolute `DockerHub:Registry`, Docker Scout URLs); External process execution (`TrivyBaseUrl`); Logging and telemetry of external data (`Telemetry:OtlpEndpoint`, the export target)

## Problem / root cause

`DockerUpdateGuardOptionsValidator.ValidatePortainerOptions`
(`src/DockerUpdateGuard/Configuration/DockerUpdateGuardOptionsValidator.cs:320-325`) accepts any value that
`Uri.TryCreate(..., UriKind.Absolute)` parses with scheme `http`/`https`. The parser ends the authority at the first
`/`, `?` or `#`, so `https://admin#secret@portainer.example:9443` parses with host `admin`, port 443, empty user info
and the fragment `#secret@portainer.example:9443`. `PortainerClient` then sets `BaseAddress = new Uri(options.BaseUrl!)`
(`src/DockerUpdateGuard/Portainer/PortainerClient.cs:295`) and adds `X-API-Key` (`:307`): the API key goes to `admin`.
The same gap exists in `ValidateAbsoluteHttpUri` (`:126-141`: Docker Hub API, release feeds, Trivy, Docker Scout),
`TryValidateDockerUri` (`:351-363`: Docker instance `BaseUrl`), the non-blank-only check of `DockerHub:Registry`
(`:85-88`, used as base URI when absolute, `src/DockerUpdateGuard/DockerHub/DockerHubClient.cs:133-141`) and
`TelemetryOptionsValidator.TryCreateEndpoint` (`src/DockerUpdateGuard.Telemetry/TelemetryOptionsValidator.cs:27-38`).

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | `https://admin#secret@portainer.example:9443` passes Portainer validation | `DockerUpdateGuardOptionsValidator.cs:320-329`: only absolute, scheme and plaintext checks | confirmed |
| 2 | The parser reads host `admin`, port 443, no user info for the `#` and the `?` form | scratch program, .NET SDK 10.0.403, `Uri.TryCreate`: `#` → fragment `#secret@portainer.example:9443`; `?` → query `?secret@portainer.example:9443`; both `UserInfo` empty, `Host` `admin`, `Port` 443 | confirmed |
| 3 | The client connects to that host and sends the API key there | `PortainerClient.cs:295` (`BaseAddress` from the configured text), `:307` (`X-API-Key`); read, no request was run against `admin` | confirmed |
| 4 | Log entries 3306, 3310, 3104 exist and log the exception of a failed call | `PortainerClientLogging.cs:58`, `:114`; `DockerInstanceClientLogging.cs:65`; record 0009, *Consequences*. The exact runtime text `admin:443` is the issue's report; the plan does not rest on it | confirmed |
| 5 | Every other configured URL key is validated the same way | `ValidateAbsoluteHttpUri` `:135-140`, `TryValidateDockerUri` `:353-362`, `TelemetryOptionsValidator.cs:27-38` | confirmed |
| 6 | An absolute `DockerHub:Registry` replaces `ApiBaseUrl` as the Docker Hub base URI; validation checks it only for non-blank | `DockerHubClient.cs:133-141`; validator `:85-88` | confirmed |
| 7 | The parser forms in *Approach*, table *Forms*, behave as listed | same scratch program, .NET SDK 10.0.403 | confirmed |
| 8 | Records 0005 and 0009 are unreleased | `git ls-remote --tags origin`: latest `v1.3.7`; `git tag --contains` of the commits adding 0005 and 0009: none | confirmed |
| 9 | No existing test input, `appsettings*.json` or documented sample URL contains `@` | `grep` of `DockerUpdateGuardOptionsValidatorTests.cs`, `TelemetryServiceCollectionExtensionsTests.cs`, `src/DockerUpdateGuard/appsettings*.json`, `README.md`, `docs/*.yml`: no `://…@` value | confirmed |
| 10 | `IdnHost` of a host with a full-width at sign (`U+FF20`) throws, so such a value never reaches a different host | same scratch program: `new Uri("https://admin" + U+FF20 + "secret.example").IdnHost` throws in `DomainNameHelper.IdnEquivalent` | confirmed |

How Trivy's Go parser reads an accepted value is not relied on: the rule only rejects more, and record 0005 already
withholds Trivy's error output for a `TrivyBaseUrl` with `@`.

## Acceptance criteria

The rule: a value has an *`@` outside its user info* when it contains `@` and either contains more than one `@`, or
does not parse as an absolute URI, or parses with an empty `Uri.UserInfo`. The failure text is exactly
`'<key>' has an '@' that does not end its user info; percent-encode '@', '#', '?' and '/' in a user name or password`,
with `<key>` the full configuration path as in the existing messages.

- [ ] AC1: An enabled Portainer section whose `BaseUrl` has an `@` outside its user info fails validation with the
  failure text for `DockerUpdateGuard:DockerInstances:<Name>:Portainer:BaseUrl`, as the only failure naming that key
  (no plaintext-HTTP failure for an `http` value with `AllowInsecureHttp: false`), and no failure contains `secret`.
- [ ] AC2: A Docker instance `BaseUrl` with an `@` outside its user info fails with the failure text for
  `DockerUpdateGuard:DockerInstances:<Name>:BaseUrl`, as the only failure naming that key, for `https`, `tcp`, `npipe`
  and an opaque `tcp:` value.
- [ ] AC3: `DockerHub:ApiBaseUrl`, `ReleaseMetadata:DotNetBaseUrl`, `ReleaseMetadata:NginxBaseUrl`,
  `Vulnerabilities:TrivyBaseUrl` (Trivy enabled), `Vulnerabilities:DockerScoutLoginUrl`,
  `Vulnerabilities:DockerScoutBaseUrl` and an absolute `DockerHub:Registry`, each with an `@` outside its user info,
  each fail with the failure text for their key.
- [ ] AC4: Values whose only `@` ends a non-empty parsed user info, and values without `@`, still pass:
  `https://user:secret@portainer.example:9443`, `https://token@portainer.example`,
  `https://user%23x:se%40cret@portainer.example`, `https://portainer.example@other.example` for the Portainer
  `BaseUrl`, `DockerHub:ApiBaseUrl` and `Vulnerabilities:DockerScoutBaseUrl`; `tcp://user:secret@docker.example:2376`
  and `unix:///var/run/docker.sock` for a Docker instance `BaseUrl`; `docker.io` for `DockerHub:Registry`.
- [ ] AC5: `Telemetry:OtlpEndpoint` with an `@` outside its user info fails `TelemetryOptionsValidator` with the
  failure text for `Telemetry:OtlpEndpoint` (only failure for that key); `http://user:secret@collector.example:4317`
  and `http://collector.example:4317` pass.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `DockerUpdateGuardOptionsValidatorPortainerBaseUrlWithAtSignOutsideUserInfoReturnsFailure` — `[DataRow]`s: `https://admin#secret@portainer.example:9443`, `https://admin?secret@portainer.example:9443`, `https://admin/secret@portainer.example:9443`, `https://admin:#secret@portainer.example:9443`, `https://user:secret@portainer.example:9443#x@y`, `https://portainer.example/@scope`, `https://@portainer.example`, `http://admin#secret@portainer.local` (`AllowInsecureHttp: false`) | `src/Tests/DockerUpdateGuard.Tests/DockerUpdateGuardOptionsValidatorTests.cs` |
| AC2 | `DockerUpdateGuardOptionsValidatorDockerInstanceBaseUrlWithAtSignOutsideUserInfoReturnsFailure` — `[DataRow]`s: `https://admin#secret@docker.example:2376`, `tcp://admin#secret@docker.example:2376`, `npipe://admin#secret@./pipe/docker_engine`, `tcp:admin@docker.example:2376` | same file |
| AC3 | `DockerUpdateGuardOptionsValidatorHttpUrlsWithAtSignOutsideUserInfoReturnFailures` — all seven keys set to `https://admin#secret@<host>` in one options object, seven exact failure texts asserted | same file |
| AC4 | `DockerUpdateGuardOptionsValidatorUrlsWithUserInfoReturnSuccess` (`[DataRow]` per value, set on Portainer `BaseUrl`, `ApiBaseUrl`, `DockerScoutBaseUrl`); `DockerUpdateGuardOptionsValidatorDockerInstanceBaseUrlWithUserInfoReturnsSuccess` (`tcp`, `unix` rows) — pass on today's code too, so they are deferred to step 6 | same file |
| AC5 | `TelemetryOptionsValidatorOtlpEndpointWithAtSignOutsideUserInfoReturnsFailure` (`http://admin#secret@collector.example:4317`, `http://collector.example:4317/@x`); `TelemetryOptionsValidatorOtlpEndpointWithUserInfoReturnsSuccess` (the two passing values; deferred to step 6) | `src/Tests/DockerUpdateGuard.Tests/TelemetryOptionsValidatorTests.cs` (new) |

No test input contains a backslash or a control character.

## Approach

One private helper per validator implements the rule of *Acceptance criteria*; it is called only after the value
passed its existing rules (configured, absolute, scheme), as the next `else if` of the same chain, so a value gets
at most one failure from these rules and the `@` failure replaces the Portainer plaintext-HTTP failure:

- `ValidateAbsoluteHttpUri`: after the scheme check (covers six keys).
- `ValidateDockerInstance`: after `TryValidateDockerUri`.
- `ValidatePortainerOptions`: between the scheme check and the plaintext check.
- `ValidateDockerHubOptions`: when `Registry` is not blank and `Uri.TryCreate(Registry, UriKind.Absolute, …)`
  succeeds; a non-absolute registry name stays unchecked (it is never used as a base URI, fact 6).
- `TelemetryOptionsValidator.Validate`: after `TryCreateEndpoint` succeeded; `TryCreateEndpoint` stays unchanged.

The failure text never contains the value. New user-visible output is only the failure text, fixed above.

### Forms

Measured with .NET SDK 10.0.403 (`Uri.TryCreate`, absolute); guard result after this change.

| Value | Parser | Guard |
| ----- | ------ | ----- |
| `https://admin#secret@h:9443`, `…:#secret@…`, `?`, `/` instead of `#` | host `admin`, user info empty | reject |
| `https://user:secret@h:9443`, `https://token@h`, `https://:@h` | user info non-empty, host `h` | accept |
| `https://user%23x:se%40cret@h` | user info `user%23x:se%40cret` | accept |
| `https://@h` | user info empty, host `h` | reject (fail closed) |
| `https://h/@scope`, `https://h/?a=b@c`, `https://h/#a@c` | `@` in path, query, fragment | reject (fail closed) |
| `https://u:p@h#x@y` | user info `u:p`, second `@` in fragment | reject |
| `https://h@other.example` | user info `h`, host `other.example` | accept (parser and text agree) |
| `https://user:p@ss@h`, `https://a@b@c`, `https://user:pw@@h`, a backslash in the user info, `unix://u@/path` | parse fails | existing scheme failure |
| `https:` + two backslashes + `u:p@h` | user info `u:p`, host `h` | accept |
| `tcp:u@h:2376`, `unix:u@x`, `npipe:u@x` (no `//`) | opaque, user info empty | reject |
| `tcp://admin#x@h:2376`, `npipe://u#x@./pipe/docker_engine` | host `admin` / `u` | reject |
| `tcp://u:p@h:2376` | user info `u:p` | accept |
| user info with `;`, `[`, `%`, space, `U+FF03`, `U+202E`, `U+0000` | parsed as (escaped) user info, host `h` | accept |
| full-width at sign `U+FF20` in the host, no ASCII `@` | host with `U+FF20`; `IdnHost` throws (fact 10) | not checked (no `@`) |
| `%40` in the authority instead of `@` | parse fails | existing scheme failure |

## Options considered

Weighed in record 0012: (1) reject an `@` that is not the one separator of a non-empty user info — chosen; (2) reject
only an empty user info with `@` — accepts a second `@` after a correct user info; (3) reject `#`, `?`, `/` before the
last `@` — reimplements the parser; (4) repair the value — guesses intent and diverges from Trivy; (5) reject all user
info — breaks working configurations (record 0005, option 3); (6) runtime warning — the credential still leaves;
(7) Portainer and Docker `BaseUrl` only — leaves the PAT, Docker Scout and telemetry keys open. For the telemetry key,
a helper shared from `DockerUpdateGuard.Telemetry` lost to a private copy: the host's configuration rules do not
belong in the observability layer, and the rule is a few lines.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| `DockerUpdateGuard` | `Configuration/DockerUpdateGuardOptionsValidator.cs` | helper, failure text, five call sites |
| `DockerUpdateGuard.Telemetry` | `TelemetryOptionsValidator.cs` | helper, one call site |

## Signatures (for the Dev's skeleton)

In `DockerUpdateGuardOptionsValidator`, region `Static methods`:

- `private static bool HasAtSignOutsideUserInfo(string value, Uri uri)` — `true` per the rule in *Acceptance
  criteria*, on the URI the call site already parsed.
- `private static string CreateAtSignFailure(string propertyPath)` — returns the failure text for that key.
- `private static bool TryValidateDockerUri(string value, [NotNullWhen(true)] out Uri? uri)` — changed so the parsed
  URI reaches the helper (*Challenge*).

In `TelemetryOptionsValidator`, region `Methods`, after `TryCreateEndpoint` (private static after public static):

- `private static bool HasAtSignOutsideUserInfo(string value, Uri uri)` — same rule.

No public or internal API changes, so step 4 (*Skeleton*) does not apply; the tests call `Validate`.

## Test files

- `src/Tests/DockerUpdateGuard.Tests/DockerUpdateGuardOptionsValidatorTests.cs` (existing)
- `src/Tests/DockerUpdateGuard.Tests/TelemetryOptionsValidatorTests.cs` (new, class `TelemetryOptionsValidatorTests`)

Existing test code that calls a changed signature: none (only private members are added). Existing tests stay
unchanged: none has an input with `@` (fact 9).

Files no unit test can cover: none.

## Areas

- [Hosting and configuration](../../docs/areas/hosting.md): *Validation* gains the rule, the keys it applies to, the
  failure text and its order — the one place the rule is written.
- [Container actions](../../docs/areas/container-actions.md), [Docker instances](../../docs/areas/docker-instances.md),
  [Registries](../../docs/areas/registries.md), [Base images](../../docs/areas/base-images.md),
  [Vulnerabilities](../../docs/areas/vulnerabilities.md): their validation lists link the rule.
- [Telemetry](../../docs/areas/telemetry.md): the OTLP rule and its failure text.

## Documentation updates

Sweep: every validation enumeration for a URL key in `docs/areas/`, `docs/ARCHITECTURE.md` and `README.md`. All by
the Dev, against the code once green:

- `docs/areas/hosting.md`, *Validation*: a bullet with the rule, the keys (the URL keys of the `DockerUpdateGuard`
  section, `DockerHub:Registry` when absolute; `Telemetry:OtlpEndpoint` in its own validator), the failure text, that
  it is checked after the scheme rule and replaces the Portainer plaintext failure, and the fix (percent-encoding);
  link record 0012 under the document's decision links.
- `docs/areas/container-actions.md` `:115-117`: add "`BaseUrl` has an `@` outside its user info" to the list.
- `docs/areas/docker-instances.md` `:152-154`: same for the instance `BaseUrl`.
- `docs/areas/registries.md` `:200-201`: `ApiBaseUrl` and an absolute `Registry`.
- `docs/areas/base-images.md` `:104`: the release-feed URLs.
- `docs/areas/vulnerabilities.md` table rows `:21`, `:23`, `:24`, and `:106` (the `@` there is now always user info).
- `docs/areas/telemetry.md` *Export* `:25-26` and *Limits and errors* `:117-118`: the rule and its exact text.
- `README.md`: the *Configuration reference* intro gets one sentence on the rule (percent-encode the user info);
  row `:168` (Docker `BaseUrl`, names its validation) and row `:190` (Portainer `BaseUrl`) mention it.
- `docs/ARCHITECTURE.md` `:413`, *Secrets in configuration*: one sentence that URLs whose `@` is not their user-info
  separator fail validation, linking record 0012. Section 4 delegates the rules to `hosting.md`: no change.
- Records 0005 and 0009 (unreleased): already updated by the Lead in this step.

## Architecture check

No guarantee of `.squad/project.md` changes: validation still runs as one unit with `.ValidateOnStart()`, the
validator still reads only the certificate file, integration clients are untouched and keep returning
`ExternalOperationResult<T>`. Layering holds: the telemetry rule stays in `DockerUpdateGuard.Telemetry`.

## Security considerations

- [ ] No limits: the rule counts characters of a configuration value; no decoding.
- [ ] Exceptions reaching a user-visible message: none — `Uri.TryCreate` does not throw; the failure text is fixed
  and carries only the key.
- [ ] Bypass forms: the *Forms* table enumerates what the parser accepts; any value the parser reads with a host other
  than the text after its one `@` has an empty user info or a second `@`, and is rejected.

## Decision records

- `docs/decisions/0012-configured-urls-are-rejected-when-an-at-sign-is-not-their-user-info-separator.md` (Proposed,
  indexed).
- `0005` (Context) and `0009` (Consequences) edited in place: the follow-up they name is this rule.

## Out of scope / follow-ups

- Rejecting user info altogether (record 0005, option 3).
- How Trivy reads an accepted `TrivyBaseUrl` (record 0005 covers its output).

## Challenge

- **minor** (Devil's Advocate), dead parse clause in the helper — accepted: the helper takes the URI the call site
  parsed (`HasAtSignOutsideUserInfo(string value, Uri uri)`, both validators), and `TryValidateDockerUri` puts out its
  parsed URI for that. A value that does not parse still gets the existing scheme failure, so the rule's outcome is
  unchanged; `hosting.md` words it without the parse clause. Confirmed against the built code in step 9.

## Security notes

- Security plan review: APPROVED, no blocking or non-blocking findings.
