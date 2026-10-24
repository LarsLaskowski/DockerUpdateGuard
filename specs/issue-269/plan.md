# Plan: CertificatePath accepts unusable non-PKCS#12 files and silently ignores a missing file

Source: Issue #269
Status: Draft
Tier: security — the change rewrites how the TLS client credential (certificate and private key) is accepted, loaded and rejected, a guard on credential handling for Docker Engine access.
Security areas touched: Docker Engine access (`DockerInstanceClient` client-certificate loading and HTTP-client pool); TLS and transport relaxations (the client-certificate half of the instance TLS options, next to `SkipCertificateValidation`, which stays unchanged); Secrets in configuration (the client private key is now read during options validation; messages must never carry file contents)

## Problem / root cause

- `src/DockerUpdateGuard/Docker/DockerInstanceClient.cs:297-304`: `CreateHttpClient` adds a client certificate only
  when `File.Exists(certificatePath)`; a missing file is skipped without log or error.
- `DockerInstanceClient.cs:485-497`: `LoadCertificate` loads every extension other than `.pfx`/`.p12` with
  `X509CertificateLoader.LoadCertificateFromFile`, which yields a certificate without private key.
- `src/DockerUpdateGuard/Configuration/DockerUpdateGuardOptionsValidator.cs:297-317` (`ValidateDockerInstance`)
  never looks at `CertificatePath`.
- Related defect found on the way: `DockerInstanceClient.cs:1272-1278` (`GetOrCreateHttpClient`) pools
  `new Lazy<HttpClient>(factory)` (mode `ExecutionAndPublication`), which caches a factory exception. A certificate
  that fails to load once (today: a corrupt or password-protected `.pfx`) fails every later operation of that
  instance until restart, even after the file is fixed. With the silent skip removed, a file missing for a moment
  would trigger this too, so the fix evicts a failed pool entry.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | A missing `CertificatePath` file is skipped silently | `DockerInstanceClient.cs:297-304` | confirmed |
| 2 | Non-`.pfx`/`.p12` files load via `LoadCertificateFromFile` (no private key) | `DockerInstanceClient.cs:485-497`; `LoadCertificateFromFile` reads a single X.509 certificate (PEM/DER), never a key | confirmed |
| 3 | The validator does not check `CertificatePath` | `DockerUpdateGuardOptionsValidator.cs:297-317` (issue said 296-316) | confirmed |
| 4 | A load failure at runtime already ends the operation *failed*, not unhandled | all five operations call `GetOrCreateHttpClient` inside `try` with `catch (Exception)` → `Failed(... {exception.Message})`: lines 994/1043, 1068/1131, 1157/1168, 1194/1205, 1228/1260 | confirmed |
| 5 | A failed client creation stays cached in the pool | `GetOrCreateHttpClient` line 1275, `Lazy<T>(Func<T>)` default `ExecutionAndPublication` caches exceptions (`Lazy<T>` docs, *Exception caching*) | confirmed |
| 6 | Validator is registered via DI with one `IConfiguration` constructor | `ServiceCollectionExtensions.cs:53` | confirmed |
| 7 | The pool key contains `CertificatePath` | `BuildHttpClientCacheKey`, `DockerInstanceClient.cs:249-258` | confirmed |
| 8 | Existing test generates its PFX at test time | `DockerInstanceClientTests.cs:572-616` | confirmed |
| 9 | README lines: configuration row and deployment note | `README.md:172` and `README.md:298` (issue said 168/294; moved) | refuted |
| 10 | The area document lists the gap under *Open questions* items 2 and 3 | `docs/areas/docker-instances.md:164-166` is one entry under *Known gaps*; there is no *Open questions* section | refuted |
| 11 | `docs/ARCHITECTURE.md` mentions `CertificatePath` | grep: no hit; §4 describes the validator | refuted |
| 12 | `DOCKER.md:123` promises PEM client certificates are loaded | `DOCKER.md:123` | confirmed |
| 13 | `X509CertificateLoader.LoadPkcs12(bytes, null)` loads a bundle exported with no password and one exported with the empty password `""` | to verify: Tester, step 5, tests `ClientCertificateLoaderTryLoadWithValidPfxReturnsLoaded` and `...WithEmptyPasswordPfxReturnsLoaded` | unverified |
| 14 | A password-protected or corrupt bundle makes `LoadPkcs12` throw `CryptographicException` (or a subclass) and nothing else | to verify: Tester, step 5, tests `...WithPasswordProtectedPfxReturnsUnreadable` and `...WithCorruptPfxReturnsUnreadable` | unverified |
| 15 | `LoadPkcs12` of a bundle without key returns a certificate with `HasPrivateKey == false` (does not throw) | to verify: Tester, step 5, test `...WithPfxWithoutPrivateKeyReturnsNoPrivateKey` | unverified |
| 16 | The README hint `openssl pkcs12 -export -in cert.pem -inkey key.pem -out client.pfx -passout pass:` (OpenSSL 3 defaults) produces a bundle the loader accepts | to verify: Dev, step 6, scratch directory outside the tree: generate a key/cert with openssl, convert with the exact hint, load with `ClientCertificateLoader`; report the OpenSSL version or "openssl not available" | unverified |
| 17 | MS DI picks the `(IConfiguration)` constructor of the validator when `ClientCertificateLoader` is not registered | to verify: Dev, step 6, existing `ServiceCollectionExtensionsTests` and a startup-path test stay green | unverified |

## Acceptance criteria

- [ ] AC1: For an enabled instance whose `CertificatePath` is not blank and whose extension is not `.pfx`/`.p12`
  (ordinal ignore-case; `.pem`, `.crt`, `.cer`, `.key`, no extension), validation fails with exactly
  `'DockerUpdateGuard:DockerInstances:<Name>:CertificatePath' names an unusable client certificate '<path>': only password-less PKCS#12 files with the extension .pfx or .p12 are supported`,
  and the file is not read.
- [ ] AC2: A `.pfx`/`.p12` path whose file or directory does not exist fails validation with reason
  `the file does not exist` (same message frame as AC1).
- [ ] AC3: A file that cannot be read (access denied, other I/O error) or is not a password-less PKCS#12 bundle
  (corrupt bytes, password-protected) fails with reason `the file cannot be read or is not a password-less PKCS#12 bundle`.
- [ ] AC4: A bundle whose loaded certificate has no private key fails with reason
  `the bundle contains no certificate with a private key`.
- [ ] AC5: Validation passes for a password-less bundle with private key (no password and empty password, `.pfx` and
  `.P12` casing), and for a `null`, empty or whitespace `CertificatePath` (no read).
- [ ] AC6: A disabled instance (`Enabled: false`) is not checked for `CertificatePath`; an enabled instance is checked
  whatever its transport (`http`, `tcp`, `unix`, `npipe` included).
- [ ] AC7: At runtime, on an `https` engine URI (`https://`, or `tcp://` with `UseTls`), an unusable certificate ends
  the operation *failed* with `The client certificate of Docker instance '<Name>' could not be loaded: <reason>` inside
  the operation's existing frame (for example `Docker image inspect failed for 'Production': The client certificate of Docker instance 'Production' could not be loaded: the file does not exist`);
  `LoadCertificateFromFile` and the `File.Exists` skip are gone.
- [ ] AC8: After an HTTP-client creation failed, the next operation for the same instance creates the client again
  (the failure is not cached in the pool).
- [ ] AC9: On a plain `http` engine URI (`http://`, `tcp://` without `UseTls`) the certificate is not loaded at runtime:
  client creation succeeds with a nonexistent `CertificatePath`.
- [ ] AC10: README, `DOCKER.md`, the area documents and `docs/ARCHITECTURE.md` describe the behavior as listed under
  *Documentation updates* and *Areas*; the #269 entry under *Known gaps* is removed.

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | `ClientCertificateLoaderTryLoadWithUnsupportedExtensionReturnsUnsupportedFormatWithoutReading` (DataRows `.pem`, `.crt`, `.cer`, `.key`, no extension; reader fails the test if called); `DockerUpdateGuardOptionsValidatorValidateWithPemCertificatePathFails` (exact message) | `ClientCertificateLoaderTests.cs`; `DockerUpdateGuardOptionsValidatorTests.cs` |
| AC2 | `ClientCertificateLoaderTryLoadWithMissingFileReturnsFileNotFound` (DataRow: reader throws `FileNotFoundException`, `DirectoryNotFoundException`); `DockerUpdateGuardOptionsValidatorValidateWithMissingCertificateFileFails` | same two files |
| AC3 | `ClientCertificateLoaderTryLoadWithUnreadableFileReturnsUnreadable` (reader throws `UnauthorizedAccessException`, `IOException`); `ClientCertificateLoaderTryLoadWithPasswordProtectedPfxReturnsUnreadable`; `ClientCertificateLoaderTryLoadWithCorruptPfxReturnsUnreadable`; `DockerUpdateGuardOptionsValidatorValidateWithPasswordProtectedPfxFails` | same two files |
| AC4 | `ClientCertificateLoaderTryLoadWithPfxWithoutPrivateKeyReturnsNoPrivateKey`; `DockerUpdateGuardOptionsValidatorValidateWithPfxWithoutPrivateKeyFails` | same two files |
| AC5 | `ClientCertificateLoaderTryLoadWithValidPfxReturnsLoaded` (asserts `HasPrivateKey`); `ClientCertificateLoaderTryLoadWithEmptyPasswordPfxReturnsLoaded`; `ClientCertificateLoaderTryLoadWithBlankPathReturnsNotSet` (`null`, `""`, `"   "`, reader not called); `DockerUpdateGuardOptionsValidatorValidateWithValidPfxSucceeds` (path `client.P12`); `DockerUpdateGuardOptionsValidatorValidateWithBlankCertificatePathSucceeds` | same two files |
| AC6 | `DockerUpdateGuardOptionsValidatorValidateWithDisabledInstanceSkipsCertificatePath`; `DockerUpdateGuardOptionsValidatorValidateWithUnusableCertificateOnUnixInstanceFails` | `DockerUpdateGuardOptionsValidatorTests.cs` |
| AC7 | `DockerInstanceClientInspectImageAsyncWithMissingClientCertificateReturnsFailedAsync` (default constructor, `https://docker.example.test`, `CertificatePath` = `<temp>/<new guid>/client.pfx`, never created; exact message) | `DockerInstanceClientTests.cs` |
| AC8 | `DockerInstanceClientInspectImageAsyncAfterClientCreationFailureCreatesClientAgainAsync` (factory throws `InvalidOperationException` on the first call, returns a stub-backed client on the second; first result *failed*, second *succeeded*, factory called twice) | `DockerInstanceClientTests.cs` |
| AC9 | `DockerInstanceClientCreateHttpClientWithPlainHttpEndpointIgnoresCertificatePath` (reflection like the existing `CreateHttpClient` tests; `http://docker.example.test`, nonexistent `.pfx`; returns a client) | `DockerInstanceClientTests.cs` |
| AC10 | no unit test: the Reviewer reads the documentation diff against *Documentation updates* and *Areas* in step 8 | — |

Test inputs: certificates and bundles are generated in the test (`CertificateRequest` + `CreateSelfSigned`, then
`Export(X509ContentType.Pkcs12)` with no password, `""`, or `"secret"`; a bundle without key from
`X509CertificateLoader.LoadCertificate(certificate.RawData).Export(X509ContentType.Pkcs12)`; corrupt = a few fixed
bytes), never checked in. Loader and validator tests feed bytes and exceptions through the injected reader, so they
touch no file system (`docs/UNIT_TESTS.md`). Only AC7 reads a path, one that does not exist.

## Approach

1. New `ClientCertificateLoader` (namespace `DockerUpdateGuard.Configuration`, used by the validator and by
   `DockerInstanceClient`, which already imports that namespace) and enum `ClientCertificateLoadStatus`.
   `TryLoad(path, out certificate)`:
   - blank path → `NotSet`;
   - `Path.GetExtension(path)` not `.pfx`/`.p12` (`StringComparison.OrdinalIgnoreCase`) → `UnsupportedFormat`,
     before any read;
   - read bytes through the injected reader (default `File.ReadAllBytes`): `FileNotFoundException` or
     `DirectoryNotFoundException` → `FileNotFound`; any other `IOException`, `UnauthorizedAccessException`,
     `ArgumentException`, `NotSupportedException` → `Unreadable`;
   - `X509CertificateLoader.LoadPkcs12(bytes, password: null)`; `CryptographicException` → `Unreadable`;
   - `HasPrivateKey == false` → dispose, `NoPrivateKey`; otherwise `Loaded` with the certificate.
   - The path is used as configured (no trimming). No exception message, byte or certificate field leaves the loader.
   - `DescribeFailure(status)` returns the four reason texts of AC1–AC4 (exact strings there); `NotSet` and `Loaded`
     throw `ArgumentOutOfRangeException`.
2. Validator: field `_clientCertificateLoader`; the existing `(IConfiguration)` constructor chains to a new
   `(IConfiguration, ClientCertificateLoader)` constructor with `new ClientCertificateLoader()`.
   `ValidateDockerInstances` becomes an instance method (moves to the `Methods` region) and calls the new
   `ValidateCertificatePath` for every named instance after `ValidateDockerInstance`. `ValidateCertificatePath`
   returns at once when `Enabled == false` or the path is blank; otherwise it calls `TryLoad`, disposes a loaded
   certificate, and on failure adds the AC1 message frame with `DescribeFailure`.
3. `DockerInstanceClient.CreateHttpClient`: replace lines 297-304 with: when the path is not blank **and**
   `engineUri.Scheme == Uri.UriSchemeHttps`, call the shared loader (a `private static readonly` field
   `_clientCertificateLoader`); on any status other than `Loaded` throw
   `InvalidOperationException($"The client certificate of Docker instance '{instanceOptions.Name}' could not be loaded: {ClientCertificateLoader.DescribeFailure(status)}")`,
   otherwise add the certificate. Delete `LoadCertificate`. The `SkipCertificateValidation` block and its warning stay
   as they are.
4. `GetOrCreateHttpClient`: keep the `Lazy` pool; read `pooledClient.Value` in a `try`, and on an exception remove
   exactly that entry (`_httpClients.TryRemove(KeyValuePair.Create(cacheKey, pooledClient))`) and rethrow, so the
   operations' existing `catch (Exception)` builds the *failed* result.
5. `BuildHttpClientCacheKey` stays unchanged.

New user-visible output: the validation message and the runtime message above, plain text, exact strings in AC1–AC4
and AC7. No layout beyond that.

## Options considered

1. **Shared loader with an injected read delegate (chosen)** — one rule set for startup and runtime, tests without
   file system.
2. **A file-system abstraction registered in DI** — same testability, more wiring for one read.
3. **Validate only where the transport presents the certificate** — hides a forgotten `UseTls`; departs from the
   Product Manager's wording "set for an enabled instance".
4. **Validate disabled instances too** — blocks startup for a parked instance whose certificate is not mounted.
5. **Keep loading on plain `http` at runtime** — a vanished file would fail operations on a transport that never
   presents the certificate.
6. **Keep the cached `Lazy` failure** — a certificate missing for a moment disables the instance until restart.
7. **PEM certificate plus key file** — out of scope by the Product Manager's decision.

Reasoning in `docs/decisions/0007-client-certificates-are-validated-as-pkcs12-at-startup.md`.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| DockerUpdateGuard | `Configuration/ClientCertificateLoader.cs` | new |
| DockerUpdateGuard | `Configuration/ClientCertificateLoadStatus.cs` | new |
| DockerUpdateGuard | `Configuration/DockerUpdateGuardOptionsValidator.cs` | field, constructor, `ValidateDockerInstances` to instance, new `ValidateCertificatePath` |
| DockerUpdateGuard | `Docker/DockerInstanceClient.cs` | `CreateHttpClient` certificate block, `LoadCertificate` removed, `GetOrCreateHttpClient` eviction, new static field |
| DockerUpdateGuard.Tests | `ClientCertificateLoaderTests.cs` | new |
| DockerUpdateGuard.Tests | `DockerUpdateGuardOptionsValidatorTests.cs`, `DockerInstanceClientTests.cs` | new tests |

## Signatures (for the Dev's skeleton)

```csharp
// Configuration/ClientCertificateLoadStatus.cs
public enum ClientCertificateLoadStatus
{
    NotSet = 0,        // path null, empty or whitespace
    Loaded,
    UnsupportedFormat,
    FileNotFound,
    Unreadable,
    NoPrivateKey,
}

// Configuration/ClientCertificateLoader.cs
public sealed class ClientCertificateLoader
{
    private readonly Func<string, byte[]> _readAllBytes;
    public ClientCertificateLoader();                                   // : this(File.ReadAllBytes)
    public ClientCertificateLoader(Func<string, byte[]> readAllBytes);
    public static string DescribeFailure(ClientCertificateLoadStatus status);
    public ClientCertificateLoadStatus TryLoad(string? certificatePath, out X509Certificate2? certificate);
}

// Configuration/DockerUpdateGuardOptionsValidator.cs
private readonly ClientCertificateLoader _clientCertificateLoader;
public DockerUpdateGuardOptionsValidator(IConfiguration configuration);  // : this(configuration, new ClientCertificateLoader())
public DockerUpdateGuardOptionsValidator(IConfiguration configuration, ClientCertificateLoader clientCertificateLoader);
private void ValidateDockerInstances(IEnumerable<DockerInstanceOptions> instances, List<string> failures); // was static
private void ValidateCertificatePath(DockerInstanceOptions instance, List<string> failures);

// Docker/DockerInstanceClient.cs
private static readonly ClientCertificateLoader _clientCertificateLoader = new ClientCertificateLoader();
// removed: private static X509Certificate2 LoadCertificate(string certificatePath)
// unchanged signatures: CreateHttpClient(DockerInstanceOptions, Uri, ILogger), GetOrCreateHttpClient(DockerInstanceOptions, Uri)
```

Files the skeleton rewrites: the four production files above. `DockerInstanceOptions.cs` stays as it is (read: final).

## Test files

- `src/Tests/DockerUpdateGuard.Tests/ClientCertificateLoaderTests.cs` (new)
- `src/Tests/DockerUpdateGuard.Tests/DockerUpdateGuardOptionsValidatorTests.cs`
- `src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientTests.cs`

Existing test code that calls a changed signature: none. The `(IConfiguration)` constructor stays; `CreateHttpClient`
keeps its signature, so the reflection call sites (`DockerInstanceClientTests.cs:572` and the other
`CreateHttpClient` tests) and `DockerInstanceClientCreateHttpClientWithClientCertificateReturnsConfiguredClient` stay
valid unchanged.

Files of the change no unit test can cover: none.

## Areas

- `docs/areas/docker-instances.md` (owner Lead):
  - *TLS options*: `CertificatePath` must name a password-less PKCS#12 bundle (`.pfx`/`.p12`, any case) with a
    certificate that has a private key; presented on `https` and TLS `tcp`; not loaded on `http`, non-TLS `tcp`,
    `unix`, `npipe`; checked at startup for every enabled instance that sets it, whatever the transport; disabled
    instances are not checked.
  - *Transports*: a connection whose creation failed is not pooled; the next operation tries again (the pool key
    still includes the path, so a renewed file under the same path is read only when the pool entry is rebuilt).
  - *Limits and errors*: add the four validation failures with the message frame and the runtime *failed* message;
    messages name the path, never file contents.
  - *Known gaps*: remove the #269 entry. *Related decisions*: link 0007.
- `docs/areas/hosting.md` (owner Lead): *Validation* — messages may also name a configured certificate path, and
  validation reads that one file; *Configuration reload* / *Known gaps* #281 entry — a reload validates the
  certificate file again, so a certificate deleted while running makes the reloaded configuration invalid.

## Documentation updates

- `README.md:172` (owner Dev): `CertificatePath` row — password-less PKCS#12 (`.pfx`/`.p12`) with certificate and
  private key, readable by the container user (UID 64000); no effect on `http`, non-TLS `tcp`, `unix` and `npipe`
  instances but still validated at startup when set on an enabled instance; conversion hint
  `openssl pkcs12 -export -in cert.pem -inkey key.pem -out client.pfx -passout pass:`; a PEM or DER file now fails
  startup.
- `README.md:298` (owner Dev): deployment note — mount the `.pfx`/`.p12` readable by UID 64000.
- `DOCKER.md:99-123` (owner Dev): replace "PFX/P12 or PEM" and "will load ... PEM client certificates" with the
  PKCS#12-only rule and the conversion hint; advise to keep `cert.pem`/`key.pem` out of `/app/certs`, whose `*.pem`
  files the entrypoint treats as CA certificates (`DOCKER.md:113-115`).
- `docs/ARCHITECTURE.md` §4 *Configuration model* (owner Lead): one sentence that the `DockerUpdateGuard` validator
  also loads each configured client certificate through `ClientCertificateLoader`, the same loader
  `DockerInstanceClient` uses, linking record 0007.
- Sweep for enumerations the change extends: `docs/areas/docker-instances.md` *Limits and errors* (list of
  validation rules, line 141) and *Transports* (pool, line 46); `docs/areas/hosting.md` *Validation* ("Messages name
  keys and, at most, configured instance names"); `docs/ARCHITECTURE.md` §4 and §11 (no enumeration of TLS options
  that includes `CertificatePath`; §11 unchanged); `INSTALL.md:130` (generic mention, unchanged); `appsettings.json`
  (`""` stays valid). All listed above.
- Pull request description: call out the breaking configuration change for PEM users (orchestrator, step 10).

## Architecture check

- *Integration clients never throw for expected failures*: preserved — the runtime failure is thrown inside
  `CreateHttpClient`, which every operation already calls within its `catch (Exception)`; AC7 pins the *failed*
  result.
- *Layering*: the loader lives in the host project's `Configuration` namespace, used by the validator and the Docker
  client of the same project; no new project dependency.
- *Misconfiguration is a startup failure* (§4): extended to the certificate file; the validator gains its first file
  read, recorded in 0007 and §4.
- `SkipCertificateValidation` stays opt-in with its once-per-instance warning; no code path of it changes.

## Security considerations

- [ ] No byte or length limit is added: the file is operator-controlled configuration and is read whole;
  `X509CertificateLoader`'s default `Pkcs12LoaderLimits` apply to the bundle.
- [ ] Exceptions that can reach a user-visible message: `CryptographicException`, `IOException` and subclasses,
  `UnauthorizedAccessException`, `ArgumentException`, `NotSupportedException` from the read or load are caught in the
  loader and turned into a status; only the fixed reason texts of AC1–AC4 reach the validation message (with the
  configured path) and the runtime message (with the instance name, no path). No exception message, file content or
  certificate field is logged or shown. The `InvalidOperationException` thrown by `CreateHttpClient` carries only
  that fixed text.
- [ ] The validation-time certificate (with private key) is disposed right after the check.
- [ ] The configured path is operator configuration (trusted, like `BaseUrl`); it is echoed in the validation
  message only, which hosting already allows for key names and instance names.

## Decision records

- `docs/decisions/0007-client-certificates-are-validated-as-pkcs12-at-startup.md` (Proposed), indexed.

## Out of scope / follow-ups

- A password option for encrypted bundles, and a PEM certificate plus key file pair (record 0007).
- The client certificate added to a pooled handler is not disposed when the pool is disposed (existing, minor; no
  issue proposed).
- The reload gap itself (#281) and the `/app/certs` trust-store import (#285) stay with their issues.

## Challenge notes

1. `minor`: No test checks that a valid certificate is actually attached on `https`. The existing `DockerInstanceClientCreateHttpClientWithClientCertificateReturnsConfiguredClient` (`DockerInstanceClientTests.cs:571-614`) checks only `BaseAddress`. A build that loads the certificate but never adds it to `SslOptions.ClientCertificates` would pass AC5, AC7 and AC9. Instruction for the Tester: extend that test, or add `DockerInstanceClientCreateHttpClientWithValidPfxOnHttpsAddsClientCertificate`, to read the `SocketsHttpHandler` through reflection and assert that `SslOptions.ClientCertificates` holds exactly one certificate with `HasPrivateKey == true`. Also assert that the collection is null or empty in the AC9 plain-`http` test.
2. `minor`: The plan's *Areas* text for `docs/areas/hosting.md` *Validation* says validation "reads that one file". It reads one file per enabled instance that sets `CertificatePath`, at startup and again on every configuration reload. Instruction for the Lead or Dev: write it as "validation reads the configured client-certificate file of each enabled instance, at startup and on every reload".

## Security notes

1. Startup now reads one operator-chosen file with no size limit. If the path points at a device or a FIFO (`/dev/zero`, a named pipe), startup or a reload hangs or runs out of memory. The plan accepts this on purpose (operator-trusted configuration, *Security considerations* item 1), and Security agrees it is not a defect of this change. One sentence about it in the area document's *TLS options* would be enough; no code is needed.
2. Facts 13-16 can be marked confirmed in the plan, citing Security's measurement (.NET SDK 10.0.402, OpenSSL 3.0.13).
