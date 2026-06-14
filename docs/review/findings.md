# Review Findings

Central findings list. Each finding receives a stable ID `F-NNN` and is referenced
from the [Review Matrix](file-inventory.md) (column *Findings*).

Severity levels see [criteria.md](criteria.md#severity-levels-for-findingsmd):
🔴 High · 🟠 Medium · 🟡 Low · 🔵 Info.

Status per finding: 🆕 open · 🛠️ in progress · ✅ fixed · 🚫 wontfix/accepted.

---

## Template (copy per finding)

```
### F-NNN — <Short title>
- **Severity:** 🔴/🟠/🟡/🔵
- **Criterion:** K?  (e.g., K3 Security)
- **Phase:** P?
- **File(s):** `path/to/file.cs:Line`
- **Status:** 🆕
- **Finding:** What the issue is.
- **Impact:** Why it matters.
- **Recommendation:** Concrete proposed fix.
```

---

## Phase 1 — Docker / DockerHub / Portainer / Configuration

Summary: **0 🔴 · 3 🟠 · 7 🟡 · 2 🔵**. The security core is solidly built
(DockerHub credentials only via HTTPS auth endpoints, no secrets in logs,
allow-listed Portainer actions, read-only Docker Engine access). Findings relate to
hardening/resilience rather than acute vulnerabilities.

### F-001 — DockerInstanceClient creates a new HttpClient per call
- **Severity:** 🟡
- **Criterion:** K7 (Performance/Resources)
- **Phase:** P1
- **File(s):** `src/DockerUpdateGuard/Docker/DockerInstanceClient.cs:237-274` (and usages at `:959,:1033,:1122,:1159,:1193`)
- **Status:** 🆕
- **Finding:** The default factory `CreateHttpClient` instantiates a new `HttpClientHandler`/`SocketsHttpHandler` + `HttpClient` per engine call, which is disposed immediately via `using`. For `tcp`/`http(s)` endpoints this is the known "new HttpClient per request" anti-pattern (no connection pooling, TLS handshake churn). `PortainerClient` correctly uses `IHttpClientFactory`.
- **Impact:** At higher frequency/multiple instances there's a risk of socket/TIME_WAIT accumulation and repeated TLS handshakes. Given the configured scan intervals (minutes) it's practically limited; nevertheless it's a K7 violation (within a single operation the client is reused correctly).
- **Recommendation:** Reuse a pooled `SocketsHttpHandler` per instance or use named `IHttpClientFactory` clients with `ConfigurePrimaryHttpMessageHandler`; the Unix/NPipe `ConnectCallback` remains representable there.

### F-002 — Silent TLS bypass when SkipCertificateValidation is set
- **Severity:** 🟠
- **Criterion:** K3 (Security)
- **Phase:** P1
- **File(s):** `src/DockerUpdateGuard/Docker/DockerInstanceClient.cs:256-259`
- **Status:** 🆕
- **Finding:** If `SkipCertificateValidation = true`, `HttpClientHandler.DangerousAcceptAnyServerCertificateValidator` is set — without any log output. The certificate bypass is not visible/auditable at runtime.
- **Impact:** MITM risk for `tcp`+TLS engines; an accidentally enabled bypass remains unnoticed. It's opt-in via admin configuration, thus not 🔴.
- **Recommendation:** Log a `Warning` during client construction (instance name + "certificate validation disabled"); strengthen the README note (line 147).

### F-003 — Portainer credentials may be sent over plaintext HTTP
- **Severity:** 🟡
- **Criterion:** K3
- **Phase:** P1
- **File(s):** `src/DockerUpdateGuard/Configuration/DockerUpdateGuardOptionsValidator.cs:191-196`; `src/DockerUpdateGuard/Portainer/PortainerClient.cs:304-318`
- **Status:** 🆕
- **Finding:** The validator accepts both `http` and `https` for `Portainer:BaseUrl`. `CreateAuthenticatedClientAsync` sends `X-API-Key`, username/password (`POST /api/auth`) or the bearer JWT schema-agnostic — over `http://` this is plaintext. No warning is emitted. The README (line 166) even documents `http` as acceptable.
- **Impact:** API tokens/passwords/JWTs can be intercepted on non-TLS connections. Often used behind VPN/localhost, hence 🟡.
- **Recommendation:** At minimum warn if credentials are configured with `http` scheme; ideally enforce `https` (or provide an explicit opt-in flag for plaintext).

### F-004 — Secret fields declared as `record` → plaintext in generated `ToString()`
- **Severity:** 🟡
- **Criterion:** K3
- **Phase:** P1
- **File(s):** `src/DockerUpdateGuard/Portainer/Data/PortainerLoginRequest.cs:10-11`; `src/DockerUpdateGuard/Portainer/Data/PortainerAuthResponse.cs:9`
- **Status:** 🆕
- **Finding:** `PortainerLoginRequest` (record) contains `Password`, and `PortainerAuthResponse` (record) contains `Jwt`. The compiler-generated `ToString()` exposes all members in plaintext. Currently these objects are not logged — the risk is latent (e.g., future structured logging of `{Request}`/`{Response}`).
- **Impact:** Latent leakage of password/JWT in logs/exceptions. Similar pattern: anonymous `new { username, password }` in `DockerHubClient.RequestAccessTokenAsync` (`DockerHubClient.cs:1250-1254`).
- **Recommendation:** Override `ToString()` or avoid modeling credentials as `record`; consider an analyzer rule to detect secrets in `ToString()`.

### F-005 — `MaxParallelRequests` is a dead configuration field
- **Severity:** 🟡
- **Criterion:** K7 / K10
- **Phase:** P1
- **File(s):** `src/DockerUpdateGuard/Configuration/DockerHubOptions.cs:36-37`
- **Status:** 🆕
- **Finding:** `DockerHub:MaxParallelRequests` is declared (`[Range(1,32)]`), validated in the options validator (`<=0`) and documented in README (line 109)/appsettings — but not read by any production code (repo-wide only declaration/validator/doc/tests). Registry parallelism is effectively unthrottled.
- **Impact:** A misleading throttle setting; real parallelism is unlimited, which may conflict with the Docker Hub quota budget managed in `Scanning`.
- **Recommendation:** Enforce it (e.g., apply a `SemaphoreSlim` throttle around outgoing Docker Hub/registry calls) or remove the setting and its README entry.

### F-006 — `RetryCount` has no effect; no retry/backoff implemented
- **Severity:** 🟠
- **Criterion:** K4 / K10
- **Phase:** P1
- **File(s):** `src/DockerUpdateGuard/Configuration/ScanningOptions.cs:73-76`
- **Status:** 🆕
- **Finding:** `Scanning:RetryCount` is documented as "Retry count for transient failures" (README line 134) but is not consumed by any production code. There is no retry/backoff logic for transient errors against Engine/Registry/Portainer — errors are logged and discarded until the next interval.
- **Impact:** Pretended resilience; transient network errors abort the scan immediately. For a tool that continuously contacts flaky external registries, this is a real resilience gap.
- **Recommendation:** Implement bounded retry with backoff honoring `RetryCount` (e.g., resilience handler on `IHttpClientFactory`) or remove the setting and documentation.

### F-007 — `[Range]` DataAnnotations are not enforced (`ValidateDataAnnotations` missing)
- **Severity:** 🟡
- **Criterion:** K10
- **Phase:** P1
- **File(s):** `src/DockerUpdateGuard/ServiceCollectionExtensions.cs:43-45`; `src/DockerUpdateGuard/Configuration/*Options.cs`
- **Status:** 🆕
- **Finding:** `AddOptions<DockerUpdateGuardOptions>().Bind(...).ValidateOnStart()` does not call `.ValidateDataAnnotations()`. Only the custom `DockerUpdateGuardOptionsValidator` runs, which checks lower bounds (`<=0`/`<0`) and required fields. All `[Range]` upper bounds (timeouts ≤300, `MaxParallelRequests` ≤32, interval caps) are not enforced.
- **Impact:** Out-of-range values can pass start validation; `[Range]` attributes misleadingly suggest enforcement that does not exist. Real impact is small (admin-controlled values).
- **Recommendation:** Add `.ValidateDataAnnotations()` (including recursive validation of nested options) or incorporate upper bounds into the custom validator and remove the attributes.

### F-008 — DockerHubClient: unused os/arch parameters; manifest list selection ignores platform
- **Severity:** 🟡
- **Criterion:** K1 (Correctness)
- **Phase:** P1
- **File(s):** `src/DockerUpdateGuard/DockerHub/DockerHubClient.cs:217-227,939-960`
- **Status:** 🆕
- **Finding:** `ParseTag(element, operatingSystem, architecture)` does not evaluate `operatingSystem`/`architecture`; they are passed through from `GetTagAsync`/`GetTagsAsync` but never used. `GetConfigDigestFromManifestListAsync` picks the **first** manifest with a digest from a multi-arch manifest list (no platform matching).
- **Impact:** For multi-arch images, config/base image extraction may reflect the wrong platform; the os/arch API signature suggests filtering that does not occur (dead path).
- **Recommendation:** Implement platform selection properly (match os/arch against the manifest list's `platform` block) or remove the parameters.

### F-009 — DockerHub access token cache tied to scope lifetime
- **Severity:** 🔵
- **Criterion:** K4 / K2
- **Phase:** P1
- **File(s):** `src/DockerUpdateGuard/DockerHub/DockerHubClient.cs:62-73,1199-1237`; `src/DockerUpdateGuard/ServiceCollectionExtensions.cs:54-59,109-110`
- **Status:** 🆕
- **Finding:** `DockerHubClient` is registered via `AddHttpClient<DockerHubClient>` (transient); `IDockerHubClient`/`IRegistryMetadataClient` are `AddScoped(... GetRequiredService<DockerHubClient>())`. The token cache (`_accessToken`, `_accessTokenExpiresAtUtc`, `_tokenRefreshLock`) therefore exists per scope and is rebuilt for each new scope.
- **Impact:** Caching applies within a scope; across scopes each cycle re-authenticates with Docker Hub → extra login requests against the quota budget. Not a correctness error, but worth reviewing.
- **Recommendation:** Confirm intended reuse; if cross-scope caching is desired, move the token cache into a singleton service.

### F-010 — Portainer filter built via string interpolation as JSON
- **Severity:** 🟡
- **Criterion:** K1 / K3
- **Phase:** P1
- **File(s):** `src/DockerUpdateGuard/Portainer/PortainerClient.cs:94-95`
- **Status:** 🆕
- **Finding:** `FindContainerIdAsync` builds the Docker filter using `Uri.EscapeDataString($"{{\"name\":[\"{containerName}\"]}}")`. `containerName` is interpolated into the JSON string without JSON escaping; `Uri.EscapeDataString` only protects URL transport, not the JSON structure.
- **Impact:** Special characters (`"`, `\`) break the filter or could alter the JSON structure. Docker name rules restrict container names — practical injection risk is low but the pattern is fragile.
- **Recommendation:** Produce the filter via `JsonSerializer.Serialize` of a typed object (correct escaping) and then URL-encode.

### F-011 — PortainerClient lacks automated tests (critical action path)
- **Severity:** 🟠
- **Criterion:** K8
- **Phase:** P1
- **File(s):** `src/DockerUpdateGuard/Portainer/PortainerClient.cs` (entire)
- **Status:** 🆕
- **Finding:** There are no tests for `PortainerClient` (no `PortainerClientTests`). Untested are: auth selection (PAT vs username/password JWT), action allow-list, endpoint/container resolution and error paths — despite destructive container actions (stop/kill/restart).
- **Impact:** Regression risk on a security-/impact-critical path (container lifecycle + credential handling) without a safety net.
- **Recommendation:** Add tests with stubbed `IHttpClientFactory`/`HttpMessageHandler`: allow-list rejection, PAT vs login path, "missing JWT" error, endpoint auto-resolve, container-not-found.

### F-012 — Image environment variables (potential secrets) are read
- **Severity:** 🔵
- **Criterion:** K3
- **Phase:** P1
- **File(s):** `src/DockerUpdateGuard/Docker/DockerInstanceClient.cs:741-779`; `src/DockerUpdateGuard/DockerHub/DockerHubClient.cs:1097-1120`
- **Status:** 🆕
- **Finding:** From image inspect/config blob `config.Env` is copied into `EnvironmentVariables`. Image env vars often contain secrets (DB passwords, API keys). They are consumed downstream in `DerivedBaseRuntimeDetector` and `ImageScanOrchestrator` (Phase 3).
- **Impact:** The capture itself is not a leak; it becomes relevant if raw values are logged, persisted, or shown in the UI downstream.
- **Recommendation:** In P3/P4/P5 verify that `EnvironmentVariables` are not logged/stored/displayed in plaintext (only use for detection, then discard). This is noted here as a handoff hint.

## Phase 2 — Vulnerabilities

Zusammenfassung: **0 🔴 · 4 🟠 · 3 🟡 · 1 🔵**. Die Provider sind sauber über
`IHttpClientFactory` (gepoolte Handler, kein Socket-Churn) und `IOptionsMonitor`
aufgebaut; Scout authentifiziert ausschließlich über HTTPS-Endpunkte (`hub.docker.com`,
`api.scout.docker.com`), Fehler werden geloggt und zu `ExternalOperationResult`
degradiert statt den Background-Service zu crashen; `CancellationToken` wird
durchgereicht; die Enrichment-Schleife ist sequentiell (keine unbegrenzte
Parallelität). Schwerpunkt der Befunde: **Docker-Scout-Korrektheit** (Severity-Mapping
case-sensitiv, Registry ignoriert), **fehlende Paket-Granularität** der Findings
(toter Unique-Index), **Testabdeckung** (Scout-/Default-Pfad ungetestet) sowie
kleinere Härtungs-/Resilienz-Punkte.

### F-013 — Docker-Scout-Severity-Mapping ist case-sensitiv → stilles `NotSet`
- **Schweregrad:** 🟠
- **Kriterium:** K1 (Korrektheit)
- **Phase:** P2
- **Datei(en):** `src/DockerUpdateGuard/Vulnerabilities/DockerScoutVulnerabilityProvider.cs:122-132`
- **Status:** 🆕
- **Befund:** `MapSeverity` schaltet direkt auf exakt großgeschriebene Strings
  (`"LOW"`/`"MEDIUM"`/`"HIGH"`/`"CRITICAL"`) ohne Normalisierung; alles andere →
  `NotSet`. Der Trivy-Provider normalisiert dagegen (`ToUpperInvariant()` + Strippen
  des `SEVERITY_`-Präfixes, `:108-122`). Liefert Docker Scout die Severity in
  Klein-/Mischschreibung (in der Scout-Welt üblich, z. B. `critical`/`high`), fällt
  jede Severity still auf `NotSet`.
- **Auswirkung:** Die Severity ist das zentrale Triage-Feld. Ein stiller Verlust
  lässt sämtliche Scout-Findings als `NotSet` erscheinen → Unterpriorisierung, ohne
  Fehlersignal.
- **Empfehlung:** Wie bei Trivy case-insensitiv mappen (vor dem `switch`
  `ToUpperInvariant()`), unbekannte Werte protokollieren; Test mit gemischter
  Schreibweise ergänzen.

### F-014 — Docker Scout ignoriert die Registry → falsche/kollidierende Lookups
- **Schweregrad:** 🟠
- **Kriterium:** K1 (Korrektheit)
- **Phase:** P2
- **Datei(en):** `src/DockerUpdateGuard/Vulnerabilities/DockerScoutVulnerabilityProvider.cs:76-86,226-228`
- **Status:** 🆕
- **Befund:** `FetchVulnerabilitiesAsync` baut die URL allein aus
  `ParseNamespaceAndRepo(imageReference.Repository)` (`…/v1/repositories/{ns}/{repo}/tags/{tag}/vulnerabilities`)
  und verwirft `imageReference.Registry` vollständig. Ein Image `ghcr.io/acme/api`
  wird damit gegen den Docker-Hub-/Scout-Pfad `acme/api` abgefragt. Der
  Trivy-Provider macht es korrekt: `GetArtifactRepository` stellt für Nicht-`docker.io`
  die Registry voran (`:129-138`) — dafür existiert sogar ein expliziter Test.
- **Auswirkung:** Für Nicht-Docker-Hub-Images entweder `NotFound` oder – schlimmer –
  Schwachstellen eines **fremden, gleichnamigen** Docker-Hub-Images, die dem falschen
  Image zugeordnet werden.
- **Empfehlung:** Scout auf `docker.io`-Images beschränken (sonst `Unsupported`/
  `NotConfigured` zurückgeben) oder die Registry in den Lookup einbeziehen; Tests
  für Nicht-Hub-Referenzen ergänzen.

### F-015 — Findings verlieren Paket-Granularität; geplanter Unique-Index ist wirkungslos
- **Schweregrad:** 🟠
- **Kriterium:** K1 / K6
- **Phase:** P2 (Persistenz-Auswirkung in P3/P4)
- **Datei(en):** `src/DockerUpdateGuard/Vulnerabilities/VulnerabilityAdvisoryData.cs:8-37`; `src/DockerUpdateGuard/Vulnerabilities/Data/TrivyVulnerability.cs:8-42`; `src/DockerUpdateGuard/Vulnerabilities/Data/ScoutVulnerabilityItem.cs:8-36`; Querverweis: `src/DockerUpdateGuard/Images/VulnerabilityEnrichmentService.cs:172-195` (P3), `src/DockerUpdateGuard.Data/Configurations/VulnerabilityFindingConfiguration.cs:60-67` (P4)
- **Status:** 🆕
- **Befund:** Weder das Advisory-Modell (`VulnerabilityAdvisoryData`) noch die
  Provider-DTOs erfassen das betroffene Paket bzw. die Fix-Version
  (`PkgName`/`InstalledVersion`/`FixedVersion`). Die persistierte
  `VulnerabilityFinding` führt `AffectedPackage`/`FixedVersion`, der Schreibpfad
  setzt sie aber nie. Der Unique-Index `UNIQUE(ImageVersionId, AdvisoryId,
  AffectedPackage, FixedVersion)` war erkennbar als Dedup pro (Advisory, Paket, Fix)
  gedacht — da die beiden letzten Spalten **immer NULL** sind, greift er unter
  PostgreSQL (Default `NULLS DISTINCT`) wie unter SQLite **nie**. Folge: dieselbe CVE
  über mehrere Pakete wird zu mehreren identischen Findings; die Meldung „provider
  reported N finding(s)" (`:193-195`) überzählt; der Index erzwingt nichts. Zusätzlich
  legt der „deactivate + neue Zeile"-Verlauf (`:170,:174`) pro Zyklus neue, nie
  kollidierende Inaktiv-Zeilen an, die mangels Cascade (`ScanRun`-Löschung =
  `SetNull`, P4) nie aufgeräumt werden → unbegrenztes Tabellenwachstum.
- **Auswirkung:** In der UI doppelt gelistete CVEs und überzählte Finding-Counts; ein
  Unique-Index, der nichts garantiert; langfristiges Wachstum der
  `VulnerabilityFindings`-Tabelle. Kein Crash (NULLs gelten als distinct).
- **Empfehlung:** `PkgName`/`InstalledVersion`/`FixedVersion` in DTOs und
  `VulnerabilityAdvisoryData` aufnehmen und beim Persistieren auf
  `AffectedPackage`/`FixedVersion` abbilden; alternativ vor dem Persistieren auf
  `AdvisoryId` deduplizieren. Index ggf. `AreNullsDistinct(false)` oder gefiltert auf
  `IsActive`. Cleanup für inaktive Findings vorsehen. **Für P3/P4 vormerken.**

### F-016 — Provider-Pfade unzureichend getestet (Scout/Default gar nicht, Trivy nur Happy Path)
- **Schweregrad:** 🟠
- **Kriterium:** K8 (Tests)
- **Phase:** P2
- **Datei(en):** `src/DockerUpdateGuard/Vulnerabilities/DockerScoutVulnerabilityProvider.cs` (gesamt, keine Tests); `src/DockerUpdateGuard/Vulnerabilities/DefaultVulnerabilityProvider.cs` (keine Tests); `src/Tests/DockerUpdateGuard.Tests/TrivyVulnerabilityProviderTests.cs` (nur Erfolg + Leerantwort)
- **Status:** 🆕
- **Befund:** Antwort auf die K8-Leitfrage „sind alle drei Provider-Pfade getestet?":
  **nein**. Für `DockerScoutVulnerabilityProvider` gibt es keinerlei Test
  (repo-weite Suche nach `Scout` in `src/Tests` → leer) — ungetestet bleiben
  Zwei-Schritt-Auth, `NotFound`-/Auth-Fehlerpfad, Credential-Gate und das
  (fehlerhafte) Severity-Mapping. `DefaultVulnerabilityProvider` (Pfad `Provider=None`/
  disabled) ist nicht direkt getestet. Trivy testet nur 2xx + `{"results":[]}`; nicht
  abgedeckt: Non-2xx → `Failed`, fehlende `TrivyBaseUrl` → `NotConfigured`,
  Exception/Timeout, Severity-Normalisierung. Zudem sind die Wire-Contracts nur an
  selbst geschriebene Stubs (`StubHttpMessageHandler`) gepinnt — eine Abweichung vom
  echten Trivy-Twirp- bzw. Scout-REST-API würde die Tests bestehen, in Produktion
  aber fehlschlagen.
- **Auswirkung:** Regressionen und Contract-Drift auf den fehleranfälligen
  Parsing-/Auth-Pfaden externer Eingaben bleiben unentdeckt.
- **Empfehlung:** Provider-Tests je Status (`Succeeded`/`NotConfigured`/`NotFound`/
  `Failed`) + Severity-Normalisierung + Leer-/Teilantworten ergänzen; für Trivy
  zusätzlich einen Contract-/Integrationscheck gegen einen echten Trivy-Server.

### F-017 — Abbruch (Cancellation) wird als Scan-Fehler verschluckt
- **Schweregrad:** 🟡
- **Kriterium:** K4 / K5
- **Phase:** P2
- **Datei(en):** `src/DockerUpdateGuard/Vulnerabilities/TrivyVulnerabilityProvider.cs:201-206`; `src/DockerUpdateGuard/Vulnerabilities/DockerScoutVulnerabilityProvider.cs:171-176`; Querverweis: `src/DockerUpdateGuard/Images/VulnerabilityEnrichmentService.cs:215-222` (P3)
- **Status:** 🆕
- **Befund:** Das breite `catch (Exception)` wandelt auch
  `OperationCanceledException`/`TaskCanceledException` aus echter Token-Cancellation
  (Graceful Shutdown) in ein `Failed`-Ergebnis samt `Error`-Log und Failed-/Partial-
  ScanRun um. (Timeouts erscheinen ebenfalls als `TaskCanceledException` — die
  wollen wir als `Failed`; echte Cancellation sollte aber propagieren.)
- **Auswirkung:** Beim Herunterfahren entstehen irreführende Failed-/Partial-Scans und
  Error-Rauschen; echte Fehler sind schwerer von Shutdown zu unterscheiden.
- **Empfehlung:** Vor dem allgemeinen `catch` ein
  `catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }`
  ergänzen.

### F-018 — Scout-Credential-DTOs als `record` → Secrets im generierten `ToString()`
- **Schweregrad:** 🟡
- **Kriterium:** K3 (Sicherheit)
- **Phase:** P2
- **Datei(en):** `src/DockerUpdateGuard/Vulnerabilities/Data/HubLoginRequest.cs:8-23`; `src/DockerUpdateGuard/Vulnerabilities/Data/HubLoginResponse.cs:8-18`
- **Status:** 🆕
- **Befund:** Gleiches Muster wie [F-004](#f-004--secret-felder-als-record--klartext-im-generierten-tostring):
  `HubLoginRequest` (record) enthält `Password` (Docker-Hub-PAT), `HubLoginResponse`
  (record) das `Token` (JWT). Der compiler-generierte `ToString()` gibt beide im
  Klartext aus. Aktuell werden die Objekte nicht geloggt — die Gefahr ist latent
  (künftiges `{Request}`/`{Response}`-Structured-Logging).
- **Auswirkung:** Latentes Leck von Docker-Hub-PAT/JWT in Logs/Exceptions.
- **Empfehlung:** `ToString()` redagieren oder Credentials nicht als `record`
  modellieren; ggf. Analyzer-Regel gegen Secret-in-`ToString`. (Querverweis F-004.)

### F-019 — `TrivyBaseUrl` nur auf Vorhandensein geprüft, nicht als absolute http/https-URI
- **Schweregrad:** 🟡
- **Kriterium:** K3 / K10
- **Phase:** P2
- **Datei(en):** `src/DockerUpdateGuard/Configuration/DockerUpdateGuardOptionsValidator.cs:69-72`; `src/DockerUpdateGuard/Vulnerabilities/TrivyVulnerabilityProvider.cs:150-162`; `src/DockerUpdateGuard/Configuration/VulnerabilityOptions.cs:25`
- **Status:** 🆕
- **Befund:** Der Validator erzwingt `TrivyBaseUrl` bei `Provider=Trivy`, prüft aber –
  anders als bei Portainer-/Docker-`BaseUrl` (`:191-196,:220-232`) – weder absolute
  URI noch Schema. Ein fehlerhafter Wert (z. B. ohne Schema) scheitert erst zur
  Laufzeit (gefangen → `Failed`) statt beim Start; `http://` (das in README Zeile 214
  dokumentierte Beispiel `http://trivy:4954`) überträgt Image-Koordinaten im Klartext;
  es gibt kein Schema-/SSRF-Guardrail (admin-konfiguriert, daher gering).
- **Auswirkung:** Späte/opake Fehlkonfiguration; Klartext-Transport als
  Default-Beispiel; kein Schema-Guardrail.
- **Empfehlung:** Beim Start `Uri.TryCreate(..., UriKind.Absolute)` + http/https-Schema
  prüfen (analog Portainer), bei `http` warnen und TLS dokumentieren.

### F-020 — Aktiver Provider wird beim Start fixiert, Konfig aber live gelesen
- **Schweregrad:** 🔵
- **Kriterium:** K2 / K10
- **Phase:** P2
- **Datei(en):** `src/DockerUpdateGuard/ServiceCollectionExtensions.cs:81-107` (P6)
- **Status:** 🆕
- **Befund:** Der DI-`switch` wählt die `IVulnerabilityProvider`-Implementierung
  **einmalig beim Start** aus `Enabled`/`Provider`. `VulnerabilityRefreshBackgroundService`
  und `VulnerabilityEnrichmentService` lesen `Enabled` jedoch live, und die Provider
  lesen `TrivyBaseUrl`/Credentials/Timeout live über `IOptionsMonitor`. Ein
  Laufzeitwechsel `Enabled` false→true (Start = disabled) tauscht die Implementierung
  nicht: Es bleibt der `DefaultVulnerabilityProvider`, der dann für jedes Image
  `NotConfigured` meldet (ScanRun = Partial). Gemischtes statisch/dynamisch-Modell.
- **Auswirkung:** Überraschendes Reconfig-Verhalten (Neustart nötig); meist kosmetisch,
  aber ein Footgun.
- **Empfehlung:** Dokumentieren, dass Provider-/Enable-Wechsel einen Neustart
  erfordern, oder alle Provider registrieren und zur Aufrufzeit anhand der aktuellen
  Optionen auflösen.

## Phase 3 — Images

Zusammenfassung: **0 🔴 · 5 🟠 · 4 🟡 · 2 🔵**. Die Kern-Logik ist insgesamt
sorgfältig gebaut: Tag-Parsing folgt der Docker-Heuristik, der Digest-Vergleich
nutzt durchgängig die Manifest-List-Digest (plattformunabhängig, passend zur
gespeicherten RepoDigest), Cancellation wird durchgereicht, die Hintergrund-Basis
(`ScheduledBackgroundService`) fängt Fehler sauber ab (Service überlebt), und
**Image-Env-Variablen werden nirgends geloggt** (positiver Abschluss zu
[F-012](#f-012--image-umgebungsvariablen-potenzielle-secrets-werden-eingelesen):
`DerivedBaseRuntimeDetector` wertet sie nur zur Detektion aus, `ImageHostLoggingExtensions`
loggt keine Secrets). Schwerpunkt der Befunde: **SemVer-Randfälle** (Pre-Release-/
Variant-Suffix-Ordnung), **Registry-Performance** (kein Token-Caching im OCI-Pfad,
sequentielle Tag-Fan-outs – es existiert repo-weit keinerlei Parallelität, womit
auch [F-005](#f-005--maxparallelrequests-ist-totes-konfigurationsfeld) ins Leere
greift), **Scan-Resilienz** (Batch-Abbruch bei Einzelfehlern; Findings werden vor
dem Scan gelöscht und bei Fehlern nicht wiederhergestellt) sowie
**Daten-Materialisierung in Heißpfaden**.

### F-021 — Pre-Release-/Variant-Suffix-Zahlen werden verworfen → gleiche Version vergleicht „gleich"; SemVer-Pre-Release-Ordnung fehlt
- **Schweregrad:** 🟠
- **Kriterium:** K1 (Korrektheit)
- **Phase:** P3
- **Datei(en):** `src/DockerUpdateGuard/Images/Helper/VersionTagResolutionHelper.cs:388-401` (`NormalizeVariantSegment`), `:334-356` (`TryParseVersionTagComponents`), `:167-186` (`TryCompareVersionTags`)
- **Status:** 🆕
- **Befund:** Das Suffix eines Tags (`-rc1`, `-alpine3.19`) wird in eine „Variant-Family"
  normalisiert, indem `NormalizeVariantSegment` je Segment **nur die führenden Buchstaben**
  behält (`rc1`→`rc`, `alpine3.19`→`alpine`). Der Versionsvergleich (`TryCompareVersionTags`)
  vergleicht ausschließlich `Major.Minor.Patch`. Folge: `1.2.3-rc1`, `1.2.3-rc2`,
  `1.2.3-rc10` sind **gleich** (Family `rc`, Version `(1,2,3)`); ein Pre-Release wird nie
  auf ein höheres Pre-Release derselben Version angehoben. Zusätzlich liegt ein Pre-Release
  (`1.2.3-rc1`, Family `rc`) in einer **anderen** Family als der GA-Release (`1.2.3`/`1.2.4`,
  Family `""`) — der Sprung Pre-Release→GA wird nie als Update angeboten. Gleiches gilt für
  Sub-Versionen einer Basis (`1.2.3-alpine3.18` vs `1.2.3-alpine3.19` → gleich). SemVer-
  Pre-Release-Präzedenz (`1.2.3-rc1 < 1.2.3`) ist nicht implementiert.
- **Auswirkung:** Nutzer auf RC-/Beta-/Pre-Release- oder sub-versionierten Variant-Tags
  verpassen Updates (RC-Inkremente, Basis-OS-Bumps bei gleicher App-Version, und den
  GA-Release). Da es sich um unterschiedliche Tag-Strings handelt, greift auch der
  Digest-Change-Pfad nicht. Die Kriterien führen Pre-Release-Randfälle explizit auf.
- **Empfehlung:** Suffix als geordnetes Pre-Release behandeln (numerischen Teil erhalten,
  z. B. `rc.1` < `rc.2`) und SemVer-konforme Pre-Release-vs-GA-Präzedenz abbilden; alternativ
  eine etablierte SemVer-Bibliothek nutzen. Tests mit `-rcN`/`-betaN` und Pre-Release→GA ergänzen.

### F-022 — Unbehandelte `OverflowException` bei überlangen Versions-Komponenten
- **Schweregrad:** 🟡
- **Kriterium:** K1 / K4
- **Phase:** P3
- **Datei(en):** `src/DockerUpdateGuard/Images/Helper/VersionTagResolutionHelper.cs:350-352,320-321,264`
- **Status:** 🆕
- **Befund:** `TryParseVersionTagComponents`/`TryParseVersionLineTag` parsen die Regex-Gruppen
  (`\d+`, unbegrenzt) per `int.Parse`. Ein Registry-Tag mit ≥10-stelliger Komponente (z. B.
  `99999999999.0.0`) wirft `OverflowException` — entgegen dem `Try…`-Kontrakt, der `false`
  liefern sollte. Die Methoden werden aus `UpdateDetectionService.Evaluate` und den
  Tag-Filtern in einer LINQ-`Where`-Kette über **registry-gelieferte** Tags aufgerufen.
- **Auswirkung:** Ein einzelner ungewöhnlicher Tag bricht die Auswertung des gesamten Images
  ab (höher gefangen → Snapshot `Failed`/ScanRun `Partial`). Geringe Eintrittswahrscheinlichkeit
  (10-stellige `X.Y.Z`-Komponente), aber externer Input + `Try`-Kontraktbruch.
- **Empfehlung:** `int.TryParse` verwenden und bei Fehlschlag `false` zurückgeben (analog
  `DerivedBaseRuntimeDetector`, der durchgängig `Version.TryParse` nutzt).

### F-023 — OciRegistryClient ohne Token-Caching: jede Anfrage durchläuft 401→Token→Retry, multipliziert über den Per-Tag-Fan-out
- **Schweregrad:** 🟠
- **Kriterium:** K7 / K5
- **Phase:** P3
- **Datei(en):** `src/DockerUpdateGuard/Images/OciRegistryClient.cs:1093-1127` (`SendRegistryRequestAsync`), `:1165-1189` (`GetBearerTokenAsync`), `:647-752` (`GetTagsAsync`), `:720` (Per-Tag `GetTagAsync`)
- **Status:** 🆕
- **Befund:** `SendRegistryRequestAsync` sendet **jede** Anfrage zuerst unauthentifiziert,
  erhält 401, holt einen Bearer-Token und wiederholt — ohne jedes Caching (kein Feld, keine
  Wiederverwendung über den Scan). `GetTagsAsync` inspiziert bis zu `MaximumTags`
  (250 Runtime / 150 Base-Image) Tags, **jeden einzeln** via `GetTagAsync`→Manifest; Multi-Arch-
  Manifeste lösen einen zweiten Manifest-Abruf (Plattform-Digest) aus. Ein Repository-Scan
  gegen eine Nicht-Docker-Hub-Registry (ghcr/quay/mcr/eigene) erzeugt damit mehrere Hundert
  bis Tausende HTTP-Requests, ~3-fach aufgebläht durch die Re-Challenge. Repo-weit existiert
  zudem **keine Parallelität/Drosselung** (kein `Task.WhenAll`/`SemaphoreSlim`), d. h. die
  Last ist sequentiell (quota-schonend), aber langsam – und [F-005](#f-005--maxparallelrequests-ist-totes-konfigurationsfeld)
  hat hier keinen Angriffspunkt.
- **Auswirkung:** Langsame Scans und Gefahr von Registry-Rate-Limiting (ghcr/quay erzwingen
  Limits) bei Massen-Scans. Docker-Hub-Images sind nicht betroffen (eigener Client mit Tag-API,
  die Digests bereits in der Liste liefert).
- **Empfehlung:** Bearer-Token je Registry (und je Scan/Instanz) cachen und wiederverwenden;
  für reine Digest-Probes `HEAD` statt `GET` erwägen; ein Request-/Parallelitäts-Budget einführen
  (vgl. F-005). Nebenbefund: `GetContentDigest` (`:429-434`) nutzt `values.SingleOrDefault()` —
  mehrere `Docker-Content-Digest`-Header (fehlkonfigurierter Proxy) würfen `InvalidOperationException`;
  `FirstOrDefault` ist robuster.

### F-024 — `ScanAllAsync` bricht den gesamten Batch ab, wenn die Vor-/Nachbereitung eines Items wirft
- **Schweregrad:** 🟠
- **Kriterium:** K4 / K5
- **Phase:** P3
- **Datei(en):** `src/DockerUpdateGuard/Images/ImageScanOrchestrator.cs:185-190,207-231,357-365`; `src/DockerUpdateGuard/Images/RuntimeContainerScanOrchestrator.cs:433-457,478-497,685-693`
- **Status:** 🆕
- **Befund:** In `ImageScanOrchestrator.ScanAsync` liegen `SingleAsync` (Image zwischen
  Listing und Scan gelöscht → wirft `InvalidOperationException`), das initiale `ScanRun`-`SaveChanges`,
  `DeleteSupersededObservedFindingsAsync` sowie das finale `SaveChanges`/Telemetry **außerhalb**
  des inneren `try` (`:233-355`). `ScanAllAsync` ruft `ScanAsync` in einer `foreach` **ohne**
  Per-Item-`catch` auf — eine Ausnahme propagiert heraus und überspringt alle restlichen Images
  dieses Zyklus. Identisches Muster in `RuntimeContainerScanOrchestrator.ScanInstanceAsync`
  (`ScanRun`-Save + `DeactivateRuntimeFindingsAsync` vor dem `try`; finales Save danach). Die
  Hintergrund-Basis fängt die Ausnahme zwar ab (Service überlebt), aber ein dauerhaft fehlschlagendes
  früh einsortiertes Item **verhungert** alle nachfolgenden Items dauerhaft (Reihenfolge = PK-Einfügung).
- **Auswirkung:** Ein gelöschtes Image / ein transienter DB-Fehler bei einem Eintrag verhindert
  das Scannen aller folgenden Einträge — pro Zyklus und, bei persistentem Fehler, dauerhaft.
- **Empfehlung:** Den Per-Item-Aufruf in `ScanAllAsync` in `try/catch` kapseln (oder `ScanAsync`/
  `ScanInstanceAsync` selbst exception-frei machen); `SingleOrDefaultAsync` + Null-Prüfung statt `SingleAsync`.

### F-025 — Findings werden vor dem Scan gelöscht/deaktiviert und bei Fehlern nicht wiederhergestellt → Alarme verschwinden transient
- **Schweregrad:** 🟠
- **Kriterium:** K1 / K6
- **Phase:** P3
- **Datei(en):** `src/DockerUpdateGuard/Images/ImageScanOrchestrator.cs:231,445-450`; `src/DockerUpdateGuard/Images/RuntimeContainerScanOrchestrator.cs:497,712-729`; Gegenbeispiel: `src/DockerUpdateGuard/Images/VulnerabilityEnrichmentService.cs:168-170`
- **Status:** 🆕
- **Befund:** `ImageScanOrchestrator.DeleteSupersededObservedFindingsAsync` nutzt
  `ExecuteDeleteAsync` (sofort, nicht rückrollbar) **zu Beginn** des Scans, vor jeglicher
  Registry-Arbeit. `RuntimeContainerScanOrchestrator.DeactivateRuntimeFindingsAsync` läuft vor
  dem `try` und wird mit dem finalen `SaveChanges` committet — auch wenn die Container-Discovery
  fehlschlägt. Ein transienter Registry-/Docker-Ausfall löscht bzw. deaktiviert damit **alle**
  aktiven Update-Findings eines Images/einer Instanz, ohne neue zu erzeugen. Kontrast:
  `VulnerabilityEnrichmentService` deaktiviert korrekt **nur** im Erfolgszweig pro Image (`:168-170`).
- **Auswirkung:** Bei kurzem Docker-/Registry-Ausfall verliert das Dashboard alle „Update verfügbar"-
  Marker der betroffenen Instanz/des Images, bis ein nächster erfolgreicher Scan sie neu anlegt
  (flackernde Alarme).
- **Empfehlung:** Findings erst **nach** erfolgreicher Neubewertung löschen/deaktivieren (analog
  Vulnerability-Pfad) oder Löschen+Neuanlegen in einer Transaktion kapseln.

### F-026 — `ResourceStatisticsCollector` materialisiert je Zyklus die gesamte Sample-Historie der Instanz
- **Schweregrad:** 🟠
- **Kriterium:** K7 / K6
- **Phase:** P3
- **Datei(en):** `src/DockerUpdateGuard/Images/ResourceStatisticsCollector.cs:167-176`
- **Status:** 🆕
- **Befund:** `CollectForInstanceAsync` lädt **alle** `RuntimeContainerResourceSamples` der Instanz
  (`Where(instance).OrderByDescending(RecordedAtUtc).AsNoTracking().ToListAsync()`), nur um per
  `GroupBy(ContainerId).First()` das jeweils jüngste Sample je Container für die Delta-Berechnung
  zu ermitteln. Die Tabelle wächst zwischen den Cleanups; bei kurzem Sampling-Intervall werden je
  Lauf immer mehr Zeilen geladen. (Das Instanz-Sample nutzt korrekt `FirstOrDefaultAsync`.)
- **Auswirkung:** Wachsender Speicher-/CPU-Aufwand in einem häufigen periodischen Pfad, skalierend
  mit Retention × Frequenz × Container-Anzahl; vermeidbar.
- **Empfehlung:** Nur das jüngste Sample je Container laden (gruppierte/Window-Query oder Filter auf
  ein enges Zeitfenster).

### F-027 — `ScanCleanupBackgroundService` lädt alle zu löschenden Zeilen in den Speicher vor `RemoveRange`
- **Schweregrad:** 🟡
- **Kriterium:** K7 / K6
- **Phase:** P3
- **Datei(en):** `src/DockerUpdateGuard/Images/ScanCleanupBackgroundService.cs:103-167`
- **Status:** 🆕
- **Befund:** Jede Kategorie (alte Samples, Snapshots, Findings, Scan-Runs, Tag-Candidates) wird
  per `ToListAsync()` materialisiert und dann `RemoveRange`'d. Cleanup zielt gerade auf große Rückstände
  ab, sodass sehr große Mengen in den Speicher geladen werden. `ExecuteDeleteAsync` (in Abhängigkeits-
  reihenfolge bzw. mit DB-Cascade) vermeidet die Materialisierung. (Die Materialisierung kann bewusst
  sein, um EF die abhängigen Löschungen über den Graphen abwickeln zu lassen.)
- **Auswirkung:** Unnötige Materialisierung; bei Skalierung untergräbt es den Zweck des Cleanups.
  Selten ausgeführt (`CleanupIntervalMinutes`), daher 🟡.
- **Empfehlung:** Auf `ExecuteDeleteAsync` in Abhängigkeitsreihenfolge umstellen bzw. die FK-Cascade-
  Regeln (P4) nutzen.

### F-028 — Year-Prefixed-Updates über Jahresgrenzen werden auf `NeedsReview` herabgestuft
- **Schweregrad:** 🟡
- **Kriterium:** K1 (Korrektheit)
- **Phase:** P3
- **Datei(en):** `src/DockerUpdateGuard/Images/UpdateDetectionService.cs:211-229` (insb. `:217`)
- **Status:** 🆕
- **Befund:** `GetHigherYearPrefixedCandidates` filtert `tagYear == currentYear` — ein `2025-*`-
  Nachfolger zu `2024-*` wird damit nie als `UpdateAvailable` erkannt; der Fall fällt in den
  Digest-/`NeedsReview`-Pfad. Der Methodenname („higher year-prefixed candidates") suggeriert das
  Gegenteil.
- **Auswirkung:** Signal-Herabstufung über Jahresgrenzen (echte Updates erscheinen nur als „manuelle
  Prüfung"). Erholt sich als Review, kein Datenverlust.
- **Empfehlung:** Intention klären; bei gewünschtem Cross-Year-Update `tagYear >= currentYear`
  (bzw. Vergleich über `CompareYearPrefixedTags`) zulassen und testen.

### F-029 — `ImageRegistrationService` sucht mit ungetrimmtem Namen, legt aber getrimmt an
- **Schweregrad:** 🟡
- **Kriterium:** K1 (Korrektheit)
- **Phase:** P3
- **Datei(en):** `src/DockerUpdateGuard/Images/ImageRegistrationService.cs:70,77`
- **Status:** 🆕
- **Befund:** `RegisterAsync` sucht via `SingleOrDefaultAsync(e => e.Name == request.Name)`
  (ungetrimmt), legt aber `Name = request.Name.Trim()` an. Ein Name mit umgebenden Leerzeichen
  matcht den getrimmt gespeicherten Wert nicht → Duplikat-Insert bzw. Verletzung eines Unique-Index
  auf `Name`.
- **Auswirkung:** Inkonsistente Upsert-Semantik; je nach Constraint ein Doppeleintrag oder ein
  `SaveChanges`-Fehler.
- **Empfehlung:** Vor dem Lookup trimmen (`var name = request.Name.Trim();`) und denselben Wert für
  Suche und Anlage nutzen.

### F-030 — `_observedImageScanLocks` wächst unbegrenzt
- **Schweregrad:** 🔵
- **Kriterium:** K7 / K5
- **Phase:** P3
- **Datei(en):** `src/DockerUpdateGuard/Images/ImageScanOrchestrator.cs:39,163-166`
- **Status:** 🆕
- **Befund:** Die statische `ConcurrentDictionary<Guid, SemaphoreSlim>` der Per-Image-Scan-Locks
  entfernt nie Einträge. Für gelöschte/neu entdeckte Observed-Images bleiben Stale-Einträge zurück.
- **Auswirkung:** Geringes, gebundenes Wachstum (eine `SemaphoreSlim` je je gescanntem Image-Guid).
  Praktisch unkritisch, da Observed-Images wenige/stabile IDs haben.
- **Empfehlung:** Optional: Locks beim Entfernen eines Observed-Image bereinigen oder einen
  zeit-/größenbegrenzten Cache verwenden.

### F-031 — `DockerHubBaseImageResolver` ist toter Code
- **Schweregrad:** 🔵
- **Kriterium:** K2 (Architektur)
- **Phase:** P3
- **Datei(en):** `src/DockerUpdateGuard/Images/DockerHubBaseImageResolver.cs`; Querverweis `src/DockerUpdateGuard/ServiceCollectionExtensions.cs:118`
- **Status:** 🆕
- **Befund:** `DockerHubBaseImageResolver` (delegiert nur an `IDockerHubClient.ResolveBaseImagesAsync`)
  wird nirgends registriert oder referenziert (repo-weite Suche → nur die Datei selbst). `IBaseImageResolver`
  ist auf `RegistryBaseImageResolver` registriert (`ServiceCollectionExtensions.cs:118`).
- **Auswirkung:** Verwaister Code; Verwirrung über den aktiven Resolver-Pfad. Kein Laufzeitfehler.
- **Empfehlung:** Entfernen oder, falls als alternativer Adapter gedacht, dokumentieren/registrieren.

## Phase 4 — Data (EF Core)

Zusammenfassung: **0 🔴 · 0 🟠 · 1 🟡 · 0 🔵**. Die Datenschicht ist durchgängig
sauber und konsistent. Das Modell-Snapshot ist mit Entities/Configurations in Sync
(per `dotnet ef migrations has-pending-model-changes` bestätigt: „No changes have been
made to the model since the last migration"); Lookup-/Zeitreihen-Felder sind indiziert,
Unique-Constraints (Registry+Repository, DockerInstance-`Name`, Portainer-Endpoint je
Instanz, Digest-Tripel) sind vorhanden und dank `null→""`-`ValueConverter` auch bei
fehlenden Digests wirksam; Cascade-/SetNull-/Restrict-Verhalten ist durchdacht; Enums
sind mit expliziten Werten (`NotSet = 0`) versioniert. Lesepfade nutzen `AsNoTracking`
+ serverseitige Projektionen (kein N+1); `SharedBaseImageQueryService` liefert korrekte
Aggregate (Randfälle „keine Treffer"/„Mehrfachnutzung" geprüft); Get-or-Create behandelt
Races über die Unique-Indizes (`DbUpdateException` → Re-Query). DI-Lifetimes (Scoped)
passen, keine UI-/Host-Abhängigkeit (kein `ProjectReference` in der `.csproj`).

**Übergaben aufgelöst:** _F-012_ (P1) — die Datenschicht persistiert **keine**
Image-Env-Variablen; `ImageVersion.MetadataJson` enthält nur Tag-Metadaten
(`JsonSerializer.Serialize(tagResult.Data)` in `ImageScanOrchestrator.cs:241,296`),
nicht `config.Env`. _F-015_ (P2) — Datenschicht-Ausprägung als **F-021** erfasst; die
dort befürchtete „unbegrenzte Tabellenwachstum" ist **durch das Retention-Fenster
begrenzt** (Deaktivieren setzt `ResolvedAtUtc`, `ScanCleanupBackgroundService` räumt auf).

Hinweis (kein Befund): Snapshot-`ProductVersion` ist `10.0.7`, das EF-Paket
(`Directory.Packages.props`) `10.0.8` — rein kosmetisch, wird bei der nächsten Migration
neu geschrieben. Design-Time-Factory mit hartkodierten `postgres/postgres`-Credentials
ist akzeptabel (nur `dotnet ef`-Tooling, nie Laufzeit).

### F-021 — VulnerabilityFindings: Unique-Index über nullbare Spalten ist wirkungslos
- **Schweregrad:** 🟡
- **Kriterium:** K6 (Datenzugriff)
- **Phase:** P4
- **Datei(en):** `src/DockerUpdateGuard.Data/Configurations/VulnerabilityFindingConfiguration.cs:32-36,60-67`; `src/DockerUpdateGuard.Data/Entities/VulnerabilityFinding.cs:38,43`; Migration `src/DockerUpdateGuard.Data/Migrations/InitialCreate.cs:223-224,456-459`; Snapshot `…/DockerUpdateGuardDbContextModelSnapshot.cs:707-720,759-760`
- **Status:** 🆕
- **Befund:** Der als Dedup gedachte Unique-Index `UNIQUE(ImageVersionId, AdvisoryId,
  AffectedPackage, FixedVersion)` schließt die beiden **nullbaren** Spalten
  `AffectedPackage`/`FixedVersion` ein. Beide werden vom Schreibpfad
  (`VulnerabilityEnrichmentService`, P3) nie gesetzt → immer `NULL`. Unter PostgreSQL
  (Default `NULLS DISTINCT`) wie unter SQLite gelten `NULL`-Werte als verschieden, der
  Index greift damit **nie**. Bemerkenswert: Dieselbe Fachschicht löst das Problem an
  anderer Stelle bereits korrekt — `ImageVersionConfiguration`, `TagCandidateConfiguration`
  und `RuntimeContainerTagSelectionConfiguration` mappen ihren `Digest` über einen
  `ValueConverter<string?,string>` (`null→""`), damit der jeweilige Unique-Index trotz
  „fehlender" Werte trägt. Dieses Idiom wurde hier nicht angewandt. (Datenschicht-
  Ausprägung von **F-015**; der dortige Korrektheitskern — DTOs/Advisory-Modell erfassen
  Paket/Fix-Version nicht — bleibt die eigentliche Ursache.)
- **Auswirkung:** Der Unique-Index garantiert nichts; liefert ein Provider dieselbe CVE
  für mehrere Pakete, entstehen mehrere identische aktive Findings (Overcount in
  „provider reported N finding(s)" und in der UI). Reiner Schreib-/Index-Overhead ohne
  Nutzen. _Kein_ unbegrenztes Wachstum (siehe Korrektur zu F-015 oben).
- **Empfehlung:** Entweder den vorhandenen `null→""`-Converter auf `AffectedPackage`/
  `FixedVersion` anwenden (Index wird wirksam), oder den Index per Npgsql
  `HasIndex(...).AreNullsDistinct(false)` auf `NULLS NOT DISTINCT` stellen, oder den
  Index auf `IsActive` filtern. Voraussetzung für echten Dedup-Nutzen bleibt, dass die
  DTOs Paket + Fix-Version führen (F-015) und der Schreibpfad sie befüllt.

## Phase 5 — Components / UI / wwwroot

Zusammenfassung: **0 🔴 · 3 🟠 · 2 🟡 · 0 🔵**. Die Präsentationsschicht ist
größtenteils sauber geschnitten: Components greifen **nie** direkt auf den
`DbContext` oder HTTP-Clients zu, sondern ausschließlich über `IApplicationViewService`
bzw. die Command-/Orchestrator-Services; Markup und Logik sind über
`*.razor`/`*.razor.cs`-Paare getrennt (keine fetten Inline-`@code`-Blöcke), alle
Lesepfade nutzen `AsNoTracking` + serverseitige Projektionen. **Kein XSS über
`MarkupString`** (repo-weit keine Verwendung) — sämtliche Registry-/Container-/
Advisory-Strings werden über `@expression` HTML-encodiert gerendert, auch die
numerisch (Invariant-Kultur) erzeugten SVG-Sparkline-Pfade; die SVG-Assets enthalten
kein Skript, der einzige `behavior:`-Treffer in `bootstrap.min.css` ist
`scroll-behavior` (Falsch-Positiv im vendored Drittanbieter-File). Event-Subscriptions
(`NavigationManager.LocationChanged`, `DashboardRefreshState.Changed`) werden in
`MainLayout`/`Dashboard` korrekt per `IDisposable` abgemeldet; Detail-Seiten nutzen
`OnParametersSetAsync` (Reload bei Routenwechsel), die Schreib-Pfade haben sauberes
`try/catch/finally` mit `_errorMessage`/`_isBusy`. Schwerpunkt der Befunde: **ein
einziger ungeprüfter External-URL-`href`-Sink** (Advisory-Link), **DbContext-Sharing/
Concurrency** über den langlebigen Blazor-Circuit und **N+1-Synchron-Queries** in den
Listen-Projektionen sowie zwei UX-Korrektheitspunkte (Server-Zeitzone, Lade-vs.-
Not-Found).

**Cross-Ref (kein eigener Befund):** `ApplicationViewService.HasSameOrNewerComparableVersion`
(`src/DockerUpdateGuard/UI/ApplicationViewService.cs:423-428`) trägt dieselbe
Cross-Year-Einschränkung (`candidateYear == currentYear`) wie [F-028](#f-028--year-prefixed-updates-über-jahresgrenzen-werden-auf-needsreview-herabgestuft)
(P3) — ein `2025-*`-Kandidat wird für ein `2024-*`-Image nicht als „verfügbar"
gefiltert. Ursache und Fix siehe F-028.

### F-032 — Geteilter scoped DbContext über den Blazor-Circuit; View-Service-Lock deckt Schreib-/Scan-Pfade nicht ab
- **Schweregrad:** 🟠
- **Kriterium:** K2 / K5
- **Phase:** P5
- **Datei(en):** `src/DockerUpdateGuard/ServiceCollectionExtensions.cs:127-128`; `src/DockerUpdateGuard.Data/ServiceCollectionExtensions.cs:27`; `src/DockerUpdateGuard/UI/ApplicationViewService.cs:37,42,1078-1090`; `src/DockerUpdateGuard/UI/RuntimeContainerTagSelectionService.cs:28,60-83`; `src/DockerUpdateGuard/Components/Pages/ObservedImages.razor.cs:158-162`; `src/DockerUpdateGuard/Components/Pages/RuntimeContainers.razor.cs:361`
- **Status:** 🆕
- **Befund:** Der `DockerUpdateGuardDbContext` wird per `AddDbContext` (Scoped) registriert; in Blazor Server entspricht der Scope dem SignalR-**Circuit** (u. U. stundenlang offen), d. h. **ein einziger DbContext** wird über die gesamte Circuit-Lebensdauer von allen scoped Services geteilt (`ApplicationViewService`, `RuntimeContainerTagSelectionService`, `IImageScanOrchestrator`, `IRuntimeContainerScanOrchestrator`, `IImageRegistrationService` …). `ApplicationViewService` serialisiert nur seine **eigenen** Lesezugriffe über ein privates `SemaphoreSlim _dbContextLock` (`:1078-1090`); die übrigen Services nehmen an diesem Lock **nicht** teil. EF Core wirft `InvalidOperationException` („A second operation was started on this context instance…"), sobald eine zweite Operation auf demselben Kontext startet, während eine andere läuft. Da die Continuations durchgängig `ConfigureAwait(false)` nutzen (laufen auf dem Threadpool, nicht auf dem Circuit-Dispatcher), ist eine Überlappung real: z. B. wenn der Topbar in `MainLayout` (Abo auf `LocationChanged`/`DashboardRefreshState.Changed`, `_ = InvokeAsync(LoadSummaryAsync)`) eine Dashboard-Lesung startet, während ein UI-ausgelöster Schreib-/Scan-Pfad (`RegisterAsync`→`ScanAsync`, `TriggerScanAsync`→`ScanAllAsync`, `SaveSelectionAsync`/`ClearSelectionAsync`) auf demselben Kontext schreibt (alle nutzen den **Circuit**-Scope, nicht einen frischen wie die Hintergrunddienste).
- **Auswirkung:** Sporadische `InvalidOperationException` (abgebrochene Render-/Scan-Aktion, ggf. Circuit-Teardown) bei zeitlicher Überlappung von Lesung und Schreibung/Scan; das private Lock vermittelt eine trügerische Thread-Sicherheit, die nur Leser-gegen-Leser greift. Zusätzlich: langlebiger, geteilter DbContext = dauerhaft offene DB-Verbindung pro Circuit (Change-Tracker-Wachstum durch `AsNoTracking` entschärft).
- **Empfehlung:** Für die Blazor-UI auf `IDbContextFactory<DockerUpdateGuardDbContext>` (`AddDbContextFactory`/`AddPooledDbContextFactory`) umstellen und je Operation einen kurzlebigen `await using var db = factory.CreateDbContext()` verwenden — damit entfallen Lock und Sharing-Risiko vollständig. Mindestens das `_dbContextLock` auf alle UI-DbContext-Nutzer ausdehnen oder UI-Scans nicht inline auf dem Circuit-Kontext, sondern über die Hintergrund-Pipeline auslösen.

### F-033 — N+1-Synchron-Queries in den Listen-Projektionen des View-Service
- **Schweregrad:** 🟠
- **Kriterium:** K7 / K6
- **Phase:** P5
- **Datei(en):** `src/DockerUpdateGuard/UI/ApplicationViewService.cs:665-668` (4×/Observed-Image, via `GetLatestObservedScanStatus` `:1711-1717`, `GetLatestObservedScanMessage` `:1724-1730`); `:1175` (1×/Runtime-Container); `:971-977` (2×/Docker-Instanz, via `GetLatestRuntimeScanStatus` `:1737-1743`)
- **Status:** 🆕
- **Befund:** In den Listen-Projektionen werden pro Zeile **synchrone** EF-Core-Queries gegen den geteilten DbContext abgesetzt. `GetObservedImagesCoreAsync` führt im `.Select` über die bereits materialisierte `observedImages`-Liste vier Synchron-Queries je Bild aus (`UpdateFindings.Count(...)` + `VulnerabilityFindings.Count(...)` + zwei `FirstOrDefault()`-Status/Message-Reads) → **4·N** Round-Trips. `GetRuntimeContainersCoreAsync` (auch vom Dashboard und der Instanz-Detailseite konsumiert) ruft je Snapshot `VulnerabilityFindings.Count(...)` synchron auf (1·N). `GetDockerInstancesAsync` setzt je Instanz zwei Synchron-Queries ab (2·N). Die Synchron-Aufrufe blockieren während des `_dbContextLock`-Besitzes (F-032) je einen Threadpool-Thread (sync-over-async). Gegenbeispiel im selben File: `LoadBaseImageRelationshipsByChildVersionAsync`/`GetBaseImagesCoreAsync` (`:1219-1229,:1437-1447`) laden die Finding-Counts korrekt in **einer** gruppierten `async`-Query.
- **Auswirkung:** Latenz/Last der Hauptseiten (Observed Images, Runtime Containers, Dashboard, Docker Instances) skaliert linear mit der Zeilenzahl; bei einem Monitoring-Tool mit vielen Containern/Images spürbar. Kein Korrektheitsfehler, aber vermeidbare Skalierungsschwäche plus blockierter Thread je Aufruf.
- **Empfehlung:** Die Per-Zeilen-Kennzahlen vorab in je **einer** gruppierten `async`-Query laden (`GroupBy(...).Select(g => g.Count())` bzw. Window-Query für den jüngsten ScanRun-Status) und per Dictionary zuordnen — analog zum bereits vorhandenen Muster für Base-Image-Findings; durchgängig `*Async`-Varianten verwenden.

### F-034 — Ungeprüfte externe Advisory-URL als `href` gerendert (javascript:-XSS-Sink)
- **Schweregrad:** 🟠
- **Kriterium:** K3 (Sicherheit)
- **Phase:** P5
- **Datei(en):** `src/DockerUpdateGuard/Components/Pages/MyImageDetail.razor:223-226`; Datenpfad `src/DockerUpdateGuard/UI/VulnerabilityFindingViewData.cs:53` ← `ApplicationViewService.MapVulnerabilityFinding` (`:94`) ← `VulnerabilityFinding.ReferenceUrl` (Provider Trivy/Docker Scout, P2)
- **Status:** 🆕
- **Befund:** `<MudLink Href="@context.ReferenceUrl" Target="_blank">@context.AdvisoryId</MudLink>` rendert die **externe, ungeprüfte** Advisory-URL direkt als `href`. Blazor HTML-encodiert zwar den Attributwert (kein Ausbrechen aus dem Attribut), **validiert aber das URL-Schema nicht** — eine `javascript:`-URL aus den Advisory-Daten bliebe als `href` erhalten und würde beim Klick im Admin-Kontext ausgeführt (DOM-/Stored-XSS). Die `ReferenceUrl` stammt aus den Vulnerability-Providern und wird unverändert persistiert und gerendert; es existiert nirgends eine `http(s)`-Schema-Allow-List. Dies ist die **einzige** Stelle der UI, an der ein externer String als `href` landet (alle übrigen Werte werden als encodierter Text gerendert). Nebenbefund: `Target="_blank"` ohne `rel="noopener noreferrer"` (Reverse-Tabnabbing; von modernen Browsern weitgehend entschärft).
- **Auswirkung:** Stored-/DOM-XSS-Sink, falls ein Provider/Feed eine bösartige `ReferenceUrl` liefert — erleichtert, wenn der Trivy-Endpunkt über `http://` läuft (cf. [F-019](#f-019--trivybaseurl-nur-auf-vorhandensein-geprüft-nicht-als-absolute-httphttps-uri)). Zielgruppe sind authentifizierte Admins und die Provider-APIs sind semi-vertrauenswürdig, daher nicht 🔴.
- **Empfehlung:** Vor dem Rendern auf absolutes `http`/`https`-Schema prüfen (`Uri.TryCreate(..., UriKind.Absolute)` + Schema-Allow-List); andernfalls als reinen Text statt Link darstellen. `rel="noopener noreferrer"` ergänzen. Idealerweise zentral beim Persistieren der Advisory validieren.

### F-035 — Zeitstempel mit `ToLocalTime()` in Blazor Server → Server-Zeitzone statt Nutzer-Zeitzone
- **Schweregrad:** 🟡
- **Kriterium:** K1 (Korrektheit)
- **Phase:** P5
- **Datei(en):** `src/DockerUpdateGuard/Components/Pages/Dashboard.razor:196`; `DockerInstances.razor:128`; `MyImageDetail.razor:112,280,284`; `ObservedImageDetail.razor:119`; `RuntimeContainers.razor:85`; `ScanHistory.razor:49`; `RuntimeContainerDetail.razor:54,113,255,317,437`; `src/DockerUpdateGuard/Components/Layout/MainLayout.razor.cs:164-167`; `src/DockerUpdateGuard/UI/ResourceUsageChartBuilder.cs:135`
- **Status:** 🆕
- **Befund:** Zeitstempel werden durchgängig mit `DateTimeOffset.ToLocalTime().ToString("g")` gerendert. In **Blazor Server** läuft dieser Code auf dem **Server**; `ToLocalTime()` konvertiert daher in die Zeitzone des Servers, nicht in die des Browsers/Nutzers. Für entfernte Nutzer bzw. einen Server in UTC/abweichender TZ werden alle Zeiten falsch — und ohne TZ-Kennzeichnung — angezeigt.
- **Auswirkung:** Irreführende „Started/Published/Recorded/Saved/Checked"-Zeiten für jeden Nutzer außerhalb der Server-Zeitzone; betrifft praktisch alle Listen- und Detailseiten sowie die Resource-Charts. Kein Datenfehler, reines Anzeigeproblem.
- **Empfehlung:** UTC mit explizitem Suffix anzeigen oder client-seitig in die Browser-TZ konvertieren (JS-Interop/`TimeProvider`/relative Zeiten) und in einem gemeinsamen Formatter zentralisieren — passend zum bereits durchgängig UTC-geführten Datenmodell (`*Utc`).

### F-036 — Detail-Seiten verwechseln Lade- und Not-Found-Zustand (Dauerspinner für fehlende Ressource)
- **Schweregrad:** 🟡
- **Kriterium:** K1 (Korrektheit)
- **Phase:** P5
- **Datei(en):** `src/DockerUpdateGuard/Components/Pages/DockerInstanceDetail.razor:14-18` (+ `.razor.cs:76-85`); `MyImageDetail.razor:16-20` (+ `.razor.cs:100-109`); `ObservedImageDetail.razor:14-18` (+ `.razor.cs:110-119`); `RuntimeContainerDetail.razor:19-24` (+ `.razor.cs:201-210`)
- **Status:** 🆕
- **Befund:** Alle vier Detailseiten unterscheiden Lade- und Not-Found-Zustand nicht: `@if (_detail is null) { "Loading…" }`. Die View-Service-Methoden (`GetObservedImageDetailAsync`/`GetRuntimeContainerDetailAsync`/`GetDockerInstanceDetailAsync`) liefern für eine **nicht existierende** ID ebenfalls `null` → `_detail` bleibt `null` → die Seite zeigt **dauerhaft** „Loading…" für gelöschte/ungültige IDs (z. B. veralteter Bookmark/Link nach Cleanup). Zusätzlich kein `try/catch` in `OnParametersSetAsync` der Lese-Seiten — eine Service-Exception schlägt auf die Blazor-Error-Boundary durch, statt inline gemeldet zu werden (anders als die Schreib-Pfade, die das sauber tun).
- **Auswirkung:** Verwirrender Dauerspinner statt „nicht gefunden" bei fehlender Ressource; Ladefehler sind für den Nutzer nicht inline sichtbar. Reines UX-/Zustands-Thema (fällt in den K1-Fokus „Lade-/Fehlerzustände in der UI").
- **Empfehlung:** Drei-Zustands-Rendering wie in `ObservedImages.razor` (laden → leer/not-found → Daten): separates Lade-Flag und eine „nicht gefunden"-Ansicht; optional `try/catch` mit `_errorMessage`-Alert.

## Phase 6 — Host / Telemetry / Infrastructure

Zusammenfassung: **0 🔴 · 1 🟠 · 4 🟡 · 1 🔵**. Das Host-/Querschnitts-Gerüst ist
überwiegend sauber: **DI-Lifetimes sind korrekt, keine Captive-Dependencies** —
sämtliche Singletons (`ApplicationTelemetry`, `DockerInstanceClient`, `PortainerClient`,
alle drei `IVulnerabilityProvider`-Implementierungen) hängen ausschließlich an
Singletons (`IHttpClientFactory`, `IOptionsMonitor`, `ILogger`) bzw. an einer
statischen `Func`-Factory; die Provider nutzen korrekt `IOptionsMonitor` statt
`IOptionsSnapshot`. Die **Middleware-Reihenfolge** in `Program.cs` ist konventionskonform
(ExceptionHandler→HSTS→HTTPS nur außerhalb Development, danach StatusCodePages→
Antiforgery→StaticAssets→RazorComponents). Die **Initialisierung läuft vor `app.Run()`**,
d. h. die Hosted-Services starten erst nach abgeschlossener Migration (kein Request-Serving
während der Migration). **OTLP-Endpoint wird validiert** (absolute http/https-URI,
`TelemetryOptionsValidator.TryCreateEndpoint`), Telemetrie wird per Enable-Flags sauber
abgeschaltet, und **es werden keine Secrets beim Start geloggt** (nur Zähler/Account-Name;
Connection-String wird nicht protokolliert). `ApplicationTelemetry` reicht den `DbContext`
als Parameter durch (kein captive DbContext im Singleton) und die Observable-Gauges werden
nach jedem Scan/Cleanup über `RefreshInventoryMetricsAsync` aktualisiert (nicht eingefroren).
Schwerpunkt der Befunde: **Start-Resilienz der Auto-Migration** (kein Retry/keine
Instanz-Koordination), **Telemetrie-Abdeckung & tote Namenskonstanten** (zentrale
`scan.run`-/Portainer-/CVE-Spans nie gestartet, `TelemetryLogPropertyNames` ungenutzt)
sowie kleinere Resilienz-/DI-Punkte.

**Hinweis (kein eigener Befund):** Die `.Telemetry.csproj` setzt — wie alle Projektdateien —
`Deterministic=False` (Folge des Wildcard-`AssemblyVersion("1.0.*")`, dokumentiert unter
[F-039](#f-039--dockerupdateguardcsproj-doppelte-property--nicht-deterministischer-release-build)).
Kein doppeltes `GenerateDocumentationFile` hier; ansonsten konsistente Projektkonventionen.

### F-040 — Kritische Pfade ohne eigene Trace-Spans: `scan.run`/Portainer/CVE/Persistenz nie gestartet
- **Schweregrad:** 🟡
- **Kriterium:** K11 (Observability)
- **Phase:** P6
- **Datei(en):** `src/DockerUpdateGuard.Telemetry/TelemetryActivityNames.cs:13,28,33,38,43`; `src/DockerUpdateGuard.Telemetry/TelemetryTagNames.cs:33,38,43`; genutzte Spans: `src/DockerUpdateGuard/Docker/DockerInstanceClient.cs:953`, `src/DockerUpdateGuard/DockerHub/DockerHubClient.cs:506,566,714`; Querverweis Metriken `src/DockerUpdateGuard/ApplicationTelemetry.cs:111-127`
- **Status:** 🆕
- **Befund:** Von den sieben deklarierten `TelemetryActivityNames` werden nur **zwei** je
  als Custom-Span gestartet: `DockerHubRequest` (`DockerHubClient.cs:506,566,714`) und
  `DockerEngineRequest` (`DockerInstanceClient.cs:953`). Repo-weite Suche bestätigt **null**
  Verwendungen für `ScanRun` (`scan.run`), `CveProviderRequest`, `PortainerRequest`,
  `PortainerAction` und `PersistenceOperation`. Damit existiert kein Eltern-Span für den
  zentralen Scan-Lebenszyklus: Die Orchestratoren erfassen zwar Metriken
  (`ApplicationTelemetry.RecordScanRun`), spannen aber **kein** `scan.run`-Activity auf, unter
  dem sich die DockerHub-/Engine-Child-Spans und die DB-Arbeit eines Scans korrelieren ließen.
  Auch der **sicherheitskritische Portainer-Aktionspfad** (stop/kill/restart) und die
  CVE-Provider-Aufrufe haben — trotz reservierter Namen — keine Spans. Parallel sind die
  Tag-Konstanten `ActionType`, `ErrorClass` und `ScanId` (`TelemetryTagNames.cs:33,38,43`)
  nirgends gesetzt. (Auto-Instrumentierung für ASP.NET Core/HttpClient ist über
  `AddAspNetCoreInstrumentation`/`AddHttpClientInstrumentation` aktiv; Portainer-Aktionen
  werden zudem über `PortainerClientLogging` geloggt — der Verlust betrifft *Traces*, nicht *Logs*.)
- **Auswirkung:** Ohne `scan.run`-Wurzelspan lassen sich Scans nicht end-to-end tracen; die
  vorhandenen Child-Spans hängen unkorreliert unter dem ASP.NET-/Background-Kontext. Für ein
  Observability-zentriertes Tool eine spürbare Lücke in der Trace-Abdeckung kritischer Pfade
  (K11-Leitfrage), insbesondere für den destruktiven Portainer-Pfad.
- **Empfehlung:** In den drei Orchestratoren einen `scan.run`-Span (`ActivityKind.Internal`/
  `Server`) um den Scan-Durchlauf legen (mit `ScanId`/`ScanType`/`ResultStatus`-Tags) und die
  Portainer-Aktionen mit `PortainerAction`-Spans (inkl. `ActionType`) instrumentieren; sonst
  die ungenutzten Konstanten entfernen (vgl. F-041).

### F-041 — Tote Telemetrie-Namenskonstanten: `TelemetryLogPropertyNames` komplett ungenutzt
- **Schweregrad:** 🟡
- **Kriterium:** K11 / K9
- **Phase:** P6
- **Datei(en):** `src/DockerUpdateGuard.Telemetry/TelemetryLogPropertyNames.cs` (gesamt); Querverweis tatsächliche Log-Templates `src/DockerUpdateGuard/HostLoggingExtensions.cs:47-56` und übrige `*LoggingExtensions`
- **Status:** 🆕
- **Befund:** Die Klasse `TelemetryLogPropertyNames` (acht Konstanten, teils Aliase auf
  `TelemetryTagNames`) wird **nirgends** im Produktivcode referenziert (repo-weite Suche nach
  `TelemetryLogPropertyNames.` → leer). Das strukturierte Logging läuft durchgängig über
  source-generierte `[LoggerMessage]`-Templates mit **ad-hoc** benannten Platzhaltern
  (z. B. `{ObservedImages}`, `{RuntimeContainers}` in `HostLoggingExtensions.cs:49`), nicht
  über diese zentralen Property-Namen. Die beabsichtigte „gemeinsame, zentral gepflegte
  Property-Namenskonvention" ist damit nicht realisiert. (Sibling zu
  [F-040](#f-040--kritische-pfade-ohne-eigene-trace-spans-scanrunportainercve-persistenz-nie-gestartet):
  auch dort sind Aktivitäts-/Tag-Namen nur teilweise verdrahtet.)
- **Auswirkung:** Toter Code, der eine Konsistenz suggeriert, die nicht existiert; Log-Property-
  Namen driften zwischen den `*LoggingExtensions` auseinander (kein Single Source of Truth).
  Rein wartbarkeits-/konventionsbezogen, kein Laufzeitfehler.
- **Empfehlung:** Entweder die `[LoggerMessage]`-Templates auf die zentralen Namen umstellen
  (für stabile, korrelierbare Log-Attribute über die OTLP-Logging-Pipeline) oder die ungenutzte
  Klasse + ungenutzte `TelemetryTagNames`-/`TelemetryActivityNames`-Member entfernen.

### F-042 — Auto-Migration beim Start: kein Retry, keine Instanz-Koordination, keine Fehlerbehandlung
- **Schweregrad:** 🟠
- **Kriterium:** K4 (Resilienz)
- **Phase:** P6
- **Datei(en):** `src/DockerUpdateGuard/ApplicationInitializationExtensions.cs:36-37`; `src/DockerUpdateGuard/ServiceCollectionExtensions.cs:49-52`
- **Status:** 🆕
- **Befund:** `InitializeDockerUpdateGuardAsync` ruft `dbContext.Database.MigrateAsync()` **ohne
  try/catch** auf; eine Ausnahme propagiert ungefangen aus `Main` → der Prozess beendet sich.
  Die Npgsql-Registrierung (`UseNpgsql`, `:51`) setzt **kein** `EnableRetryOnFailure`/keine
  Execution-Strategy. Im dokumentierten Container-Szenario (App startet neben ihrer
  PostgreSQL-Instanz) ist die DB beim Boot oft noch nicht verbindungsbereit → `MigrateAsync`
  wirft → Crash; eine Erholung hängt allein an der Container-Restart-Policy (Crash-Loop bis die
  DB bereit ist). Zusätzlich gibt es **keine Migrations-Koordination über mehrere Instanzen**:
  EF Core nimmt für `MigrateAsync` keinen instanzübergreifenden Lock; bei horizontaler Skalierung
  können zwei gleichzeitig startende Instanzen beide „Migration X fehlt" lesen und anwenden —
  eine scheitert (Duplikat in `__EFMigrationsHistory` bzw. „relation already exists") und crasht.
  PostgreSQLs transaktionales DDL verhindert Datenkorruption (fehlgeschlagene Migration rollt
  zurück), nicht aber den Absturz der unterlegenen Instanz.
- **Auswirkung:** Crash-Loop bei verzögerter DB-Verfügbarkeit am Boot; Start-Race bei
  Mehr-Instanz-Betrieb. Kein Datenverlust (transaktionales DDL), aber eine echte
  Start-Resilienz-Lücke genau im vom Prompt adressierten Bereich.
- **Empfehlung:** (1) DB-Verfügbarkeit am Start abwarten/retryen (`EnableRetryOnFailure` und/oder
  ein bounded Wait-for-DB vor `MigrateAsync`); (2) Migration über mehrere Instanzen serialisieren
  (PostgreSQL-Advisory-Lock um `MigrateAsync`, oder Migration aus dem App-Start in einen
  dedizierten Init-Job/Leader auslagern); (3) Migrationsfehler gezielt fangen und mit klarer
  Diagnose loggen statt als rohe Startup-Exception.

### F-043 — Eager-Startup-Discovery mit `CancellationToken.None`, redundant zu den Hosted-Services
- **Schweregrad:** 🟡
- **Kriterium:** K4 / K5
- **Phase:** P6
- **Datei(en):** `src/DockerUpdateGuard/ApplicationInitializationExtensions.cs:39-44`; Querverweis Hosted-Services `src/DockerUpdateGuard/ServiceCollectionExtensions.cs:129-135`
- **Status:** 🆕
- **Befund:** Nach der Migration führt die Initialisierung synchron
  `SynchronizeConfiguredInstancesAsync`, `SynchronizeAccountImagesAsync` und
  `RefreshInventoryMetricsAsync` aus — jeweils mit **`CancellationToken.None`**. Dieselben
  Discovery-Arbeiten erledigen ohnehin die registrierten Hosted-Services
  (`DockerInstanceDiscoveryBackgroundService`, `DockerHubAccountImageDiscoveryBackgroundService`)
  periodisch; der Eager-Aufruf ist damit weitgehend redundant. `CancellationToken.None` bedeutet:
  Selbst ein SIGTERM während eines langsamen Startup-Syncs (Docker-Hub-Netz) bricht die Operation
  nicht ab — die Readiness wird verzögert, bis alle drei Schritte (durch den HttpClient-Timeout
  begrenzt) durchlaufen. Positiv: die Netzfehler selbst werden **degradiert** statt geworfen
  (`SynchronizeAccountImagesAsync` prüft `ExternalOperationResult.Status`, `:188-194`;
  `SynchronizeConfiguredInstancesAsync` ist reine config-/DB-Arbeit) — ein unerreichbarer
  Docker-Hub/Instance crasht den Start also **nicht**.
- **Auswirkung:** Verzögerte/blockierte Readiness am Boot, nicht abbrechbar beim Herunterfahren;
  doppelte Erstausführung der Discovery. Keine Korrektheits-/Sicherheitsfolge.
- **Empfehlung:** Den App-Lifetime-Token (`IHostApplicationLifetime.ApplicationStopping`) statt
  `CancellationToken.None` durchreichen; die Erst-Discovery den Hosted-Services überlassen
  (z. B. „RunOnStartup" im `ScheduledBackgroundService`) oder zeitlich begrenzen, damit der Start
  nicht an externen Diensten hängt.

### F-044 — `Telemetry:Instance` wird auf `deployment.environment.name` gemappt (Semantik-Mismatch)
- **Schweregrad:** 🟡
- **Kriterium:** K11 / K12
- **Phase:** P6
- **Datei(en):** `src/DockerUpdateGuard.Telemetry/TelemetryServiceCollectionExtensions.cs:221-225`; `src/DockerUpdateGuard.Telemetry/TelemetryResourceAttributeNames.cs:13`; `src/DockerUpdateGuard.Telemetry/TelemetryOptions.cs:30-32`
- **Status:** 🆕
- **Befund:** Die Option `TelemetryOptions.Instance` ist als „Logical deployment **instance**
  name" dokumentiert, wird aber als Resource-Attribut **`deployment.environment.name`** gesetzt
  (`ConfigureResource`, `:221-225`). Nach den OpenTelemetry-Semantic-Conventions bezeichnet
  `deployment.environment.name` die *Umgebung* (z. B. `production`/`staging`), während eine
  *Instanz* über `service.instance.id` ausgedrückt wird. Setzt ein Betreiber gemäß Doku
  `Instance="node-1"`, erscheint dieser Wert im Backend als Deployment-Umgebung „node-1" —
  irreführend. Entweder ist die Option falsch benannt (gemeint: Environment) oder das Attribut
  falsch gewählt (gemeint: Instanz-ID).
- **Auswirkung:** Falsch attribuierte Telemetrie-Ressourcen; Dashboards/Filter nach „environment"
  bzw. „instance" greifen ins Leere bzw. vermischen die Dimensionen. Reines Konventions-/
  Korrektheits-Thema der Observability-Metadaten.
- **Empfehlung:** Intention klären und angleichen: entweder Option in `Environment` umbenennen und
  bei `deployment.environment.name` belassen, oder das Attribut auf `service.instance.id`
  umstellen (ggf. beide Konzepte getrennt anbieten). Nebenpunkt: `GetServiceVersion` (`:249`)
  liest die Version nur aus der Umgebungsvariable `DockerUpdateGuard__DisplayVersion` (nicht aus
  `IConfiguration`); via `appsettings` gesetztes `DisplayVersion` wird für Telemetrie ignoriert
  (Fallback auf `InformationalVersion` ist korrekt) — Konsistenz prüfen.

### F-045 — Pro Scope zwei `DockerHubClient`-Instanzen → doppelter Token-Cache (verstärkt F-009)
- **Schweregrad:** 🔵
- **Kriterium:** K2 (Architektur)
- **Phase:** P6
- **Datei(en):** `src/DockerUpdateGuard/ServiceCollectionExtensions.cs:54-59,109-111`
- **Status:** 🆕
- **Befund:** `DockerHubClient` wird via `AddHttpClient<DockerHubClient>` als **transient**
  registriert. Sowohl `IDockerHubClient` (`:109`) als auch eine der `IRegistryMetadataClient`-
  Registrierungen (`:110`) lösen ihn über `serviceProvider.GetRequiredService<DockerHubClient>()`
  in **separaten** scoped Factories auf — jede scoped Registrierung cached ihre eigene Instanz,
  und da der Typ transient ist, entstehen pro Scope **zwei** verschiedene `DockerHubClient`-Objekte
  (eines hinter `IDockerHubClient`, eines im `IRegistryMetadataClient`-Enumerable). Jedes hält
  einen eigenen Token-Cache + `SemaphoreSlim`, sodass sich pro Scope die in
  [F-009](#f-009--dockerhub-access-token-cache-an-scope-lebensdauer-gebunden) beschriebene
  doppelte Docker-Hub-Authentifizierung noch einmal verdoppelt.
- **Auswirkung:** Zusätzliche Login-Requests gegen das Docker-Hub-Quota-Budget; geringfügig mehr
  Allokation. Kein Korrektheitsfehler.
- **Empfehlung:** `DockerHubClient` einmal je Scope auflösen (z. B. `AddScoped<DockerHubClient>`
  als Basis und die Facaden darauf zeigen lassen) oder — gemeinsam mit F-009 — den Token-Cache in
  einen Singleton-Dienst auslagern, sodass Instanzanzahl und Scope-Bindung irrelevant werden.

## Phase 7 — Tests

Zusammenfassung: **0 🔴 · 4 🟠 · 1 🟡 · 0 🔵**. Die Testsuite ist überwiegend
**hochwertig**: durchgängig echte Assertions statt Smoke (inkl. präziser
NSubstitute-`Received`-Verifikationen und Log-EventId-Prüfungen), saubere
**deterministische** HTTP-Test-Doubles (`SequenceHttpMessageHandler`/`StubHttpMessageHandler`
mit URI-gemappten, geklonten Antworten; `TimeoutHttpMessageHandler` über echte
Cancellation statt Wall-Clock), und die **Kernpfade der Clients sind breit abgedeckt**:
`DockerInstanceClient` testet die Fehlerpfade (disabled→`NotConfigured`,
ssh→`Unsupported`, Timeout→`Failed` mit dediziertem Log, fehlender RepoDigest),
`DockerHubClient`/`OciRegistryClient` testen Paginierung, 401→Token→Retry,
Plattform-Matching (OCI) und Base-Image-Rekursion, die Orchestratoren decken
Erfolg/Partial/derived-Findings und **Per-Container-Resilienz** ab, und
`InstanceDiscoveryService`/`DockerHubAccountImageDiscoveryService`/`ScanCleanupBackgroundService`/
`*ReleaseMetadataService` sind solide (inkl. Cascade-Delete, Skip-Pfade,
`NotFound`-Pfade). Der Options-Validator und die DI-Registrierung sind gründlich
geprüft (letztere bestätigt nebenbei **F-031**: `RegistryBaseImageResolver` ist der
aktive `IBaseImageResolver`). **K9-Naming** ist durchgängig konform
(`{Class}Tests` / `{Class}{Scenario}{ExpectedResult}`).

Schwerpunkt der Befunde: **Lücken genau auf den in P1–P3 als riskant markierten
Korrektheits-Kernpfaden** — der Image-Referenz-Parser ist mit einem einzigen Test
praktisch ungetestet (F-046), die SemVer-/Pre-Release-/Overflow-Randfälle aus
[F-021](#f-021--pre-release-variant-suffix-zahlen-werden-verworfen--gleiche-version-vergleicht-gleich-semver-pre-release-ordnung-fehlt)/[F-022](#f-022--unbehandelte-overflowexception-bei-überlangen-versions-komponenten)
fehlen und die Cross-Year-Einschränkung aus [F-028](#f-028--year-prefixed-updates-über-jahresgrenzen-werden-auf-needsreview-herabgestuft)
wird von zwei Tests sogar als Soll-Verhalten festgeschrieben (F-047), die
Scan-Resilienz-Defekte [F-024](#f-024--scanallasync-bricht-den-gesamten-batch-ab-wenn-die-vor-nachbereitung-eines-items-wirft)/[F-025](#f-025--findings-werden-vor-dem-scan-gelöschtdeaktiviert-und-bei-fehlern-nicht-wiederhergestellt--alarme-verschwinden-transient)
sind nicht durch Regressionstests abgesichert (F-048), die Blazor-UI wird nur über
Reflection-Helfer-Tests statt gerenderte Komponenten geprüft (F-049), und die
**Test-DB-Treue** (SQLite/InMemory statt PostgreSQL, `EnsureCreated` statt
Migrationen) verdeckt PostgreSQL-spezifische Defekte und liefert beim
Concurrency-Test eine trügerische Sicherheit gegen [F-032](#f-032--geteilter-scoped-dbcontext-über-den-blazor-circuit-view-service-lock-deckt-schreib-scan-pfade-nicht-ab) (F-050).

**Bereits anderswo erfasste Test-Lücken (Querverweis, kein neuer Befund):**
[F-011](#f-011--portainerclient-ohne-automatisierte-tests-kritischer-aktionspfad) —
`PortainerClient` (destruktiver Container-Aktionspfad) hat **kein** Test-Pendant;
[F-016](#f-016--provider-pfade-unzureichend-getestet-scoutdefault-gar-nicht-trivy-nur-happy-path) —
`DockerScoutVulnerabilityProvider`/`DefaultVulnerabilityProvider` ungetestet, Trivy
nur Happy-Path. Beide bleiben in P7 gültig und werden in der Matrix an den
betroffenen Test-/Quelldateien referenziert.

### F-046 — `ImageReferenceParser` ist mit einem einzigen Testfall praktisch ungetestet
- **Schweregrad:** 🟠
- **Kriterium:** K8 (Tests)
- **Phase:** P7
- **Datei(en):** `src/Tests/DockerUpdateGuard.Tests/ImageReferenceParserTests.cs` (1 Test); Querverweis Quelle `src/DockerUpdateGuard/Images/ImageReferenceParser.cs`
- **Status:** 🆕
- **Befund:** `ImageReferenceParserTests` enthält **genau einen** Testfall
  (`…ParseWrappedMicrosoftRegistryReferenceNormalizesRegistry`) für den eng begrenzten
  „docker.io/mcr.microsoft.com/…"-Entwrap-Fall. `ImageReferenceParser` ist der
  Eingangspunkt nahezu jeder Image-Verarbeitung (Tag-/Digest-Logik P3, Provider-Lookups
  P2, Orchestratoren) und behandelt eine Vielzahl von Docker-Referenz-Formen. Ungetestet
  bleiben u. a.: implizite Registry/Repository-Defaults (`nginx` → `docker.io/library/nginx`,
  implizites `:latest`), Digest-Referenzen (`repo@sha256:…`), kombiniertes Tag+Digest
  (`repo:tag@sha256:…`), Registry mit Port (`localhost:5000/app`), mehrteilige
  Repository-Pfade (`ghcr.io/org/team/app`), Groß-/Kleinschreibung und Fehl-/Leereingaben.
- **Auswirkung:** Eine Regression in der zentralen Referenz-Normalisierung würde sich
  über sämtliche nachgelagerten Pfade (falsche Registry/Repo/Tag/Digest → falsche
  Lookups, falsche Update-/CVE-Zuordnung) auswirken und bliebe vom Testnetz unentdeckt.
  Das ist der K8-Leitfrage „sind die Korrektheits-Kernpfade abgedeckt?" direkt
  zuwiderlaufend.
- **Empfehlung:** Datengetriebene Tests (`[DataRow]`) über die typischen Referenzformen
  ergänzen — implizite Defaults, Digest-/Tag+Digest-Referenzen, Registry-mit-Port,
  mehrteilige Repos, sowie Negativ-/Randfälle (leere/ungültige Eingabe).

### F-047 — Tag-/Digest-Korrektheitskern: Pre-Release-/Overflow-Randfälle ungetestet; Cross-Year-Einschränkung (F-028) als Soll festgeschrieben
- **Schweregrad:** 🟠
- **Kriterium:** K8 (Tests)
- **Phase:** P7
- **Datei(en):** `src/Tests/DockerUpdateGuard.Tests/VersionTagResolutionHelperTests.cs` (3 Tests); `src/Tests/DockerUpdateGuard.Tests/UpdateDetectionServiceTests.cs` (`…YearCuTagOnlyUsesSameYearSuccessors` :155-190, `…YearPrefixedTagUsesSameYearSuccessors` :196-227)
- **Status:** 🆕
- **Befund:** Der in P3 als höchstes Korrektheitsrisiko markierte Tag-/Digest-Bereich ist
  zwar im Happy-Path solide getestet (Digest-Change, SemVer-Successor, MCR-Variant-Family,
  Latest-Alias-up-to-date, 50er-Cap), lässt aber genau die in P3 gefundenen **Randfälle
  ungetestet**: kein Test für Pre-Release-Ordnung (`-rc1` < `-rc2`, Pre-Release→GA) oder
  Variant-Sub-Versionen (`-alpine3.18` vs `-alpine3.19`) aus
  [F-021](#f-021--pre-release-variant-suffix-zahlen-werden-verworfen--gleiche-version-vergleicht-gleich-semver-pre-release-ordnung-fehlt),
  und kein Test für den `OverflowException`-`Try`-Kontraktbruch bei ≥10-stelligen
  Versionskomponenten aus
  [F-022](#f-022--unbehandelte-overflowexception-bei-überlangen-versions-komponenten).
  Gravierender: `UpdateDetectionServiceYearCuTagOnlyUsesSameYearSuccessors` und
  `…YearPrefixedTagUsesSameYearSuccessors` **schreiben die fehlerhafte
  Cross-Year-Einschränkung aus
  [F-028](#f-028--year-prefixed-updates-über-jahresgrenzen-werden-auf-needsreview-herabgestuft)
  als korrektes Soll-Verhalten fest** (sie assertieren, dass ein `2022-*`-Nachfolger zu
  einem `2019-*`-Image gerade **nicht** empfohlen wird).
- **Auswirkung:** Die fehleranfälligsten Korrektheitspfade (von P3 mit fünf 🟠-Befunden)
  haben kein Regressionsnetz für ihre Randfälle; ein Fix für F-028 würde zudem an den
  bestehenden Tests **scheitern**, was die Behebung aktiv erschwert und das Fehlverhalten
  zementiert.
- **Empfehlung:** Negativ-/Randfalltests für F-021 (`-rcN`/`-betaN`-Ordnung, Pre-Release→GA,
  Variant-Sub-Versionen) und F-022 (überlange Komponente → `false`/keine Exception)
  ergänzen; die beiden Year-Line-Tests beim F-028-Fix auf das gewünschte
  Cross-Year-Verhalten umstellen (statt das Ist zu fixieren).

### F-048 — Scan-Resilienz-Defekte (F-024 Batch-Abbruch, F-025 transienter Finding-Verlust) ohne Regressionstest
- **Schweregrad:** 🟠
- **Kriterium:** K8 (Tests)
- **Phase:** P7
- **Datei(en):** `src/Tests/DockerUpdateGuard.Tests/ImageScanOrchestratorTests.cs`; `src/Tests/DockerUpdateGuard.Tests/RuntimeContainerScanOrchestratorTests.cs`
- **Status:** 🆕
- **Befund:** Die Orchestrator-Suiten sind insgesamt stark und decken die
  **Per-Container**-Resilienz vorbildlich ab (`…ContinuesAfterContainerProcessingFailureAsync`:
  ein kaputter Container → `Partial`, übrige laufen weiter). Nicht abgedeckt bleiben aber
  genau die beiden in P3 gefundenen Resilienz-Defekte: (a)
  [F-024](#f-024--scanallasync-bricht-den-gesamten-batch-ab-wenn-die-vor-nachbereitung-eines-items-wirft)
  — eine Ausnahme **außerhalb** des inneren `try` (z. B. `SingleAsync` auf ein zwischen
  Listing und Scan gelöschtes Image, oder ein DB-Fehler beim initialen `ScanRun`-Save)
  bricht in `ScanAllAsync`/`ScanInstanceAsync` **alle nachfolgenden Items** ab; alle
  Batch-Tests nutzen jedoch nur **eine** Instanz bzw. überspringen den Pre-`try`-Wurf. (b)
  [F-025](#f-025--findings-werden-vor-dem-scan-gelöschtdeaktiviert-und-bei-fehlern-nicht-wiederhergestellt--alarme-verschwinden-transient)
  — kein Test seedet **aktive** Findings und lässt anschließend die Discovery/Registry
  fehlschlagen, um zu prüfen, ob bestehende Alarme den transienten Fehler überleben
  (sie tun es laut F-025 nicht).
- **Auswirkung:** Zwei Defekte mit Nutzerauswirkung (verhungernde Folge-Items;
  flackernde „Update verfügbar"-Alarme) können unbemerkt regredieren oder bei einem Fix
  ungeprüft bleiben — genau auf den Pfaden, die der Review als 🟠 markiert hat.
- **Empfehlung:** (1) Multi-Instanz-/Multi-Item-Batchtest, bei dem das **erste** Item im
  Pre-`try`-Bereich wirft, und Verifikation, dass die restlichen Items dennoch gescannt
  werden. (2) Test mit vorab aktiven Findings + fehlschlagender Discovery, der belegt,
  dass aktive Findings erst nach erfolgreicher Neubewertung gelöscht/deaktiviert werden.

### F-049 — Blazor-UI nur über Reflection-Helfer getestet; gerenderte Komponenten (inkl. F-034-Sink) ungetestet
- **Schweregrad:** 🟡
- **Kriterium:** K8 (Tests)
- **Phase:** P7
- **Datei(en):** `src/Tests/DockerUpdateGuard.Tests/DashboardTests.cs`, `MainLayoutTests.cs`, `NavMenuTests.cs`, `MyImagesTests.cs`, `MyImageDetailTests.cs`, `RuntimeContainersTests.cs`; Querverweis `src/Tests/DockerUpdateGuard.Tests/DockerUpdateGuard.Tests.csproj` (kein bUnit-Paket)
- **Status:** 🆕
- **Befund:** Sämtliche Komponenten-Tests sind **Reflection-Unit-Tests privater
  `.razor.cs`-Helfer** (Farb-Mapper, Routen-Mapping, Section-Titel,
  `GetProtectedAssetCount`, `BuildSparklinePath`). Die Helfer sind gut datengetrieben
  abgedeckt (Case-Insensitivity, null/blank), aber **keine** einzige Komponente wird
  tatsächlich gerendert (kein bUnit im Projekt). Damit haben die in P5 gefundenen
  Render-Risiken **null Testabdeckung**: der ungeprüfte Advisory-`href`-`javascript:`-Sink
  [F-034](#f-034--ungeprüfte-externe-advisory-url-als-href-gerendert-javascript-xss-sink),
  die `ToLocalTime()`-Server-Zeitzone
  [F-035](#f-035--zeitstempel-mit-tolocaltime-in-blazor-server--server-zeitzone-statt-nutzer-zeitzone)
  und die Lade-vs-Not-Found-Verwechslung
  [F-036](#f-036--detail-seiten-verwechseln-lade--und-not-found-zustand-dauerspinner-für-fehlende-ressource).
  Der Reflection-Zugriff koppelt die Tests zudem an private Methodennamen (brüchig).
- **Auswirkung:** Render-/Markup-Regressionen — einschließlich des einzigen
  sicherheitsrelevanten XSS-Sinks der UI — bleiben unentdeckt. Geringer als die
  Korrektheits-Kernpfade, da die zugrunde liegenden Defekte bereits als P5-Befunde erfasst
  sind und es sich um die Präsentationsschicht handelt — daher 🟡.
- **Empfehlung:** bUnit (oder Playwright-Komponententests) ergänzen, mindestens für den
  Advisory-Link-Sink (F-034: `javascript:`-URL wird **nicht** als `href` gerendert) und die
  Drei-Zustands-Detailseiten (F-036); danach die Reflection-Helfer-Tests durch echte
  Render-Assertions ablösen.

### F-050 — Test-DB-Treue: SQLite/InMemory statt PostgreSQL verdeckt F-021 und liefert trügerische F-032-Sicherheit
- **Schweregrad:** 🟠
- **Kriterium:** K8 (Tests — Determinismus/Aussagekraft)
- **Phase:** P7
- **Datei(en):** `src/Tests/DockerUpdateGuard.Tests/Data/SqliteTestDatabase.cs`; `src/Tests/DockerUpdateGuard.Data.Tests/Data/SqliteTestDatabase.cs` (`EnsureCreated`); `src/Tests/DockerUpdateGuard.Tests/ApplicationViewServiceTests.cs` (`UseInMemoryDatabase`, insb. `…ConcurrentReadsCompleteWithoutDbContextOverlapAsync` :1234-1281); `src/Tests/DockerUpdateGuard.Tests/ScanCleanupBackgroundServiceTests.cs` (`UseInMemoryDatabase`)
- **Status:** 🆕
- **Befund:** Die Daten-/Query-/Orchestrator-Tests laufen gegen **In-Memory-SQLite**
  (Schema via `EnsureCreated`, **nicht** über die Migrationen) bzw. den **EF-InMemory**-Provider —
  nie gegen das Produktiv-PostgreSQL. Folgen: (a) PostgreSQL-spezifische Semantik wie
  `NULLS DISTINCT` — Ursache des wirkungslosen Unique-Index in
  [F-021](#f-021--vulnerabilityfindings-unique-index-über-nullbare-spalten-ist-wirkungslos)
  — ist **nicht reproduzierbar**; (b) die **Migrationen selbst** werden von keinem Test
  ausgeführt (nur das Modell via `EnsureCreated`), Drift Migration↔Modell bliebe
  testseitig unentdeckt; (c) `ApplicationViewServiceConcurrentReadsCompleteWithoutDbContextOverlapAsync`
  läuft auf dem InMemory-Provider, der EF-Cores „a second operation was started on this
  context"-Wächter **nicht** erzwingt — der Test kann den realen Shared-DbContext-Defekt
  [F-032](#f-032--geteilter-scoped-dbcontext-über-den-blazor-circuit-view-service-lock-deckt-schreib-scan-pfade-nicht-ab),
  den sein Name suggeriert, **prinzipiell nicht** nachweisen (trügerische Sicherheit); (d)
  die N+1-Synchron-Queries aus
  [F-033](#f-033--n1-synchron-queries-in-den-listen-projektionen-des-view-service)
  „funktionieren" gegen InMemory und werden so nicht sichtbar.
- **Auswirkung:** Mehrere bestätigte Defekte (F-021, F-032) sind durch die gewählte
  Test-Infrastruktur **prinzipiell unfindbar**; ein Test trägt sogar einen Namen, der
  Schutz gegen F-032 verspricht, ohne ihn leisten zu können. Das untergräbt die
  Aussagekraft der Datenschicht-Tests.
- **Empfehlung:** Für die kritischen Datenschicht-/Concurrency-Pfade gegen ein echtes
  PostgreSQL testen (Testcontainers o. ä.), das Schema über `Database.MigrateAsync()`
  statt `EnsureCreated()` aufbauen (testet zugleich die Migrationen), und den
  Concurrency-Test entweder gegen einen relationalen Provider mit aktivem
  Single-Operation-Wächter führen oder ehrlich als nicht-aussagekräftig kennzeichnen/entfernen.

## Phase 8 — Root / Konfiguration / Doku

Zusammenfassung: **0 🔴 · 1 🟠 · 6 🟡 · 1 🔵**. **Keine Secrets eingecheckt** —
`appsettings.json`/`launchSettings.json` enthalten ausschließlich Leerstrings, die
Release-Pipeline nutzt korrekt GitHub-`secrets.*` (kein Klartext). Die
**README-Konfig-Referenz ist konsistent** mit `appsettings.json` und dem Options-Code
(P1/P6): alle Keys, Defaults und `[Range]`-/`Required`-Marker stimmen überein
(`DisplayVersion` wird via `IConfiguration`/Env in `NavMenu.razor.cs:97,104` real
konsumiert; `Telemetry:ServiceName`-Pflicht durch `TelemetryOptionsValidator:58`
gedeckt). Die zentralen Paketversionen (`Directory.Packages.props`) sind konsistent
(EF Core durchgängig 10.0.8), die Rulesets sind plausibel (Debug lenient,
Release `RHxxxx`→Error). Schwerpunkt der Befunde: **CI-/Container-Drift nach der
Azure-DevOps→GitHub-Actions-Migration** (fc81f4f) — die Release-Pipeline zieht den
Digest des **falschen** Basis-Images, `.slnx` zeigt auf die gelöschte
`azure-pipelines.yml`, die neuen Workflows fehlen in Solution & Review-Matrix —
sowie **Doku-Widersprüche** (Lizenz, Copilot-Instructions) und **Container-Härtung**
(Root, `.dockerignore`).

> **Hinweis zur Matrix-Pflege:** `azure-pipelines.yml` (P8-Zeile 19) wurde in
> fc81f4f gelöscht und durch `.github/workflows/ci.yml` + `release.yml` ersetzt.
> Diese beiden real existierenden Dateien fehlten in der Matrix; sie wurden als
> Zeilen `4a`/`4b` ergänzt und anstelle der Azure-Pipeline reviewt (siehe **F-036**).

### F-032 — Container-Image läuft als root (kein `USER` im Dockerfile)
- **Schweregrad:** 🟡
- **Kriterium:** K3 (Sicherheit)
- **Phase:** P8
- **Datei(en):** `src/DockerUpdateGuard/Dockerfile:27-51` (Runtime-Stage ohne `USER`); `src/DockerUpdateGuard/entrypoint.sh:17`; Querverweis Doku `DOCKER.md:100,154`
- **Status:** 🆕
- **Befund:** Die Runtime-Stage setzt kein `USER`; der Prozess läuft als **root**. `entrypoint.sh` importiert CA-Zertifikate gezielt nur, wenn `id -u = 0` bzw. der Trust-Store beschreibbar ist (`:17`) — die Funktion ist also auf den Root-Lauf ausgelegt; `DOCKER.md:100,154` dokumentiert das ebenfalls als „typically when run as root". Anschließend wird die App per `exec` **ohne Privilegienabgabe** im selben Root-Kontext gestartet.
- **Auswirkung:** Best-Practice-/CIS-Verstoß (Container nicht als root). Ehrliche Einordnung: Der dominante Eskalationspfad ist die gemountete `docker.sock` (README-Hauptszenario) — Socket-Zugriff allein erlaubt unabhängig von der Container-UID Host-Übernahme, ein Non-Root-User behebt das **nicht** vollständig. Root erweitert aber die Blast-Radius bei Nicht-Socket-Angriffsflächen (Dateisystem, Trust-Store-Persistenz) unnötig. Daher 🟡.
- **Empfehlung:** Non-Root-`USER` (numerische UID) setzen; CA-Import in die Build-Stage verlagern oder als optionalen, dokumentierten Root-Init-Schritt führen; Socket-Zugriff über Gruppen-GID statt Root dokumentieren.

### F-033 — Release-Pipeline ermittelt den Digest des falschen Basis-Images → inkonsistentes OCI-Label
- **Schweregrad:** 🟠
- **Kriterium:** K1 (Korrektheit) / K12
- **Phase:** P8
- **Datei(en):** `.github/workflows/release.yml:52-57,68-70`; `src/DockerUpdateGuard/Dockerfile:2,29-33`
- **Status:** 🆕
- **Befund:** Der Schritt „Extract base runtime digest" zieht und inspiziert `mcr.microsoft.com/dotnet/runtime:10.0-alpine` (`release.yml:55-56`) und reicht dessen Digest als `BASE_RUNTIME_DIGEST` ein (`:68-70`). Der Dockerfile baut die Runtime-Stage aber auf `mcr.microsoft.com/dotnet/aspnet:10.0-alpine` (`Dockerfile:2`, nicht überschrieben) und schreibt die Labels `org.opencontainers.image.base.name="…/aspnet:10.0-alpine"` und `org.opencontainers.image.base.digest="${BASE_RUNTIME_DIGEST}"` (`:32-33`). Name (`aspnet`) und Digest (`runtime`) gehören damit zu **verschiedenen** Images; der referenzierte Digest steckt nicht einmal im finalen Image.
- **Auswirkung:** Das veröffentlichte Image trägt eine **falsche Provenance-Angabe**. Das ist besonders relevant, weil DockerUpdateGuard selbst Basis-Image-Digests trackt: Scannt es sein eigenes Image, liest es widersprüchliche Labels. SBOM-/Supply-Chain-Auswertungen werden in die Irre geführt. Kein Laufzeitfehler (App läuft auf aspnet), daher 🟠 statt 🔴.
- **Empfehlung:** Im Digest-Schritt `mcr.microsoft.com/dotnet/aspnet:10.0-alpine` pullen/inspizieren (identisch zum Dockerfile-`BASE_RUNTIME`); idealerweise das Image-Tag aus einer gemeinsamen Quelle (Build-Arg) ableiten, damit Name und Digest nicht auseinanderlaufen können.

### F-034 — Lizenz-Widerspruch: README nennt „proprietary", LICENSE.md ist MIT
- **Schweregrad:** 🟡
- **Kriterium:** K12 (Dokumentation)
- **Phase:** P8
- **Datei(en):** `README.md:277`; `LICENSE.md:1-3`
- **Status:** 🆕
- **Befund:** `README.md:277` schreibt: „This repository is distributed under the **proprietary terms** described in LICENSE.md." `LICENSE.md` enthält jedoch die **MIT-Lizenz** (permissiv, „Permission is hereby granted, free of charge…", Copyright (c) 2026 e-networld).
- **Auswirkung:** Direkter Widerspruch zwischen README und tatsächlicher Lizenzdatei. Für Nutzer/Dritte ist rechtlich unklar, ob das Werk MIT-lizenziert oder proprietär ist — materiell relevant für Verwendung/Weitergabe, auch wenn es kein Code-Defekt ist.
- **Empfehlung:** Den vom Projektinhaber gewollten Stand festlegen und beide Stellen angleichen (entweder README auf MIT korrigieren oder `LICENSE.md` durch den proprietären Text ersetzen).

### F-035 — `.github/copilot-instructions.md` veraltet: beschreibt das Repo als leeres Gerüst
- **Schweregrad:** 🟡
- **Kriterium:** K12 (Dokumentation)
- **Phase:** P8
- **Datei(en):** `.github/copilot-instructions.md:36,59-76` (insb. `:36,:70`)
- **Status:** 🆕
- **Befund:** `:36` behauptet: „The repository is currently a solution skeleton with project wiring in place, but **almost no implementation files yet**." Das Repo enthält tatsächlich eine vollständige Implementierung (~330 Dateien: Docker-/DockerHub-/Portainer-Clients, EF-Core-Datenschicht inkl. Migrationen, Blazor-UI, Telemetrie). Der „Current state note"-Block (`:59-76`) formuliert durchweg im Gründungs-/Template-Modus („when the solution is created", „If the project adds EF Core migrations…") und verweist auf ein fremdes Projekt **SeriesOverwatch** (`:70`), obwohl Migrationen längst existieren (`InitialCreate`). Auch `csharp.instructions.md` nutzt Template-Beispiele (`F1Server`, `PacketAnalyzer`) — als Stil-Illustration vertretbar, aber Indiz für ungepflegte Copy-Paste-Herkunft.
- **Auswirkung:** KI-Assistenten und neue Mitwirkende werden über den Projektstand fehlinformiert; die Migrations-Anweisung verweist auf ein nicht zugehöriges Projekt.
- **Empfehlung:** Den „Current state"-/Skeleton-Abschnitt auf den realen Stand aktualisieren, den SeriesOverwatch-Verweis durch die projekteigene Migrations-Konvention ersetzen.

### F-036 — `.slnx`/CI-Drift nach Azure→GitHub-Actions-Migration
- **Schweregrad:** 🟡
- **Kriterium:** K12 / Build-CI
- **Phase:** P8
- **Datei(en):** `DockerUpdateGuard.slnx:6`; `.github/workflows/ci.yml`, `.github/workflows/release.yml` (nicht in `.slnx`); gelöscht: `azure-pipelines.yml` (Commit fc81f4f)
- **Status:** 🆕
- **Befund:** Die CI/CD-Migration (fc81f4f) hat `azure-pipelines.yml` entfernt und durch GitHub-Actions-Workflows ersetzt, aber die Solution nicht nachgezogen: `DockerUpdateGuard.slnx:6` referenziert weiterhin `<File Path="azure-pipelines.yml" />` (in Visual Studio/`dotnet sln` ein fehlender Eintrag). Die neuen Workflows `ci.yml`/`release.yml` sind **nicht** in der `.slnx` gelistet (der Ordner „Copilot" führt nur die beiden Instruction-Dateien). Zusätzlich führte die Review-Matrix die gelöschte `azure-pipelines.yml` und ließ die Workflows aus (siehe Matrix-Hinweis oben).
- **Auswirkung:** Dangling-Verweis in der Solution; die real wirksame Pipeline ist nicht Teil der Solution-Sicht. Rein strukturell, kein Laufzeitfehler.
- **Empfehlung:** In `.slnx` den `azure-pipelines.yml`-Eintrag entfernen und `.github/workflows/ci.yml` + `release.yml` (z. B. unter „Solution items") aufnehmen.

### F-037 — CI verifiziert die Formatierung nicht (dokumentierter `reihitsu-format`-Schritt fehlt in der Pipeline)
- **Schweregrad:** 🟡
- **Kriterium:** Build-CI / K12
- **Phase:** P8
- **Datei(en):** `.github/workflows/ci.yml:27-39`; `.github/workflows/release.yml:33-44`; Soll-Doku `README.md:48`, `.github/copilot-instructions.md:24,30`
- **Status:** 🆕
- **Befund:** README, Copilot-Instructions und die Serena-Memories führen `reihitsu-format ./` als **verpflichtenden** Schritt vor jedem Build. Beide Workflows führen jedoch nur `restore` → `build` → `test` aus; es gibt **keinen** Format-/Lint-Verifikationsschritt (kein `reihitsu-format --check`/`dotnet format --verify-no-changes`). Formatierungsdrift wird in CI nicht erkannt. (Die `RHxxxx`-Analyzer-Regeln greifen zwar beim Build als Error im Release-Ruleset — reine Formatierung deckt das aber nicht vollständig ab.)
- **Auswirkung:** Die als Pflicht dokumentierte Formatierung ist nicht durchgesetzt; Stil-/Format-Regressionen können unbemerkt nach `main` gelangen. Geringe funktionale Auswirkung.
- **Empfehlung:** Einen Verifikations-Schritt ergänzen (z. B. `reihitsu-format` im Check-Modus, falls verfügbar, sonst `dotnet format --verify-no-changes`), der den Build bei Drift rot macht.

### F-038 — `.dockerignore` minimal: kein Schutz vor Leak von `appsettings.*.json`/Zertifikaten beim lokalen Build
- **Schweregrad:** 🟡
- **Kriterium:** K3 (Sicherheit)
- **Phase:** P8
- **Datei(en):** `.dockerignore:1-8`; Querverweis `src/DockerUpdateGuard/Dockerfile:21-22`
- **Status:** 🆕
- **Befund:** `.dockerignore` schließt nur `.vs/.git/bin/obj/node_modules/.idea`, `*.md` und `.gitignore` aus. Es schließt **nicht** `appsettings.*.json` (z. B. `appsettings.Development.json`), `*.pfx`/`*.crt`/`*.pem` oder ein `certs/`-Verzeichnis aus. Der Dockerfile kopiert den gesamten Kontext (`COPY . .`, `:21`) und publiziert (`:22`); das Web-SDK nimmt `appsettings.*.json` standardmäßig als Content in die Publish-Ausgabe (`/app/publish`) auf. Bei einem **lokalen** `docker build` mit vorhandener, befüllter `appsettings.Development.json` oder lokal abgelegten Zertifikaten landen diese im finalen Image.
- **Auswirkung:** Potenzielles Einbacken von Entwickler-Secrets/Schlüsseln ins Image bei lokalen Builds. CI-Builds sind nicht betroffen (frischer Checkout; `appsettings.Development.json` ist via `.gitignore:363` ausgeschlossen). Daher 🟡.
- **Empfehlung:** `.dockerignore` härten: `**/appsettings.*.json` (außer der base `appsettings.json`, falls bewusst gewünscht), `*.pfx`, `*.crt`, `*.pem`, `**/certs/` sowie `docs/` ergänzen.

### F-039 — `DockerUpdateGuard.csproj`: doppelte Property & nicht-deterministischer Release-Build
- **Schweregrad:** 🔵
- **Kriterium:** K9 / K12
- **Phase:** P8
- **Datei(en):** `src/DockerUpdateGuard/DockerUpdateGuard.csproj:8,11,18,24`; `SharedAssemblyInfo.cs:13`
- **Status:** 🆕
- **Befund:** `<GenerateDocumentationFile>True` ist doppelt deklariert (`:8` und `:11`). `<Deterministic>False` ist für Debug **und** Release gesetzt (`:18,:24`) — nötig, weil `SharedAssemblyInfo.cs:13` `AssemblyVersion("1.0.*")` (Wildcard) verwendet, was deterministische Builds ausschließt. Folge: Release-Builds sind nicht reproduzierbar, und `AssemblyVersion` ist stets `1.0.<auto>` (die echte Version steckt korrekt in `InformationalVersion`/`FileVersion`/`DisplayVersion`).
- **Auswirkung:** Kosmetische Redundanz; nicht reproduzierbare Release-Assemblys. Praktisch unkritisch, da Anzeige/Diagnose über `InformationalVersion`/`DisplayVersion` laufen.
- **Empfehlung:** Doppelte `GenerateDocumentationFile`-Zeile entfernen. Falls reproduzierbare Builds gewünscht sind: feste `AssemblyVersion` (z. B. aus `$(Version)`) statt `1.0.*` und `Deterministic` (Default `true`) belassen.
