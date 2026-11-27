# Project

What the squad needs to know about this project that is not stack-specific. Read by the Lead, the Devil's
Advocate, Security and the Reviewer; the test doubles are in `docs/UNIT_TESTS.md` (*Test doubles*). Not
template-managed: `adopt-template` creates it once and never overwrites it. A product PR updates it when the change
makes an entry untrue (`.squad/routing.md`, *Scope of a product PR*).

Every entry points: it names files, types or endpoints and links the document that says more. Behavior, values,
limits and sequences are written once, in the area documents (`docs/areas/README.md`), and the reasons in the
decision records; a copy here goes stale with the next change. `config-check.py` warns about an entry of more than
`PROJECT_ENTRY_MAX_PROSE` prose words (code spans and links do not count) and about a change that adds more than
`PROJECT_MAX_GROWTH` words to this file (`.squad/tools/squad_settings.py`).

## Security areas

A change that touches one of these lists it under *Security areas touched* in its plan, so Security reviews the
plan (`.squad/routing.md`, *Tiers*, which also says when a change is tier `security`). One line per area: what is
protected, the concrete types, files or endpoints, and a link to the area document that states its rules
(`- Session tokens: issued in src/Auth/, checked by every /api route — [Sessions](../docs/areas/sessions.md)`); no
limits, patterns or sequences. Paths below are under `src/DockerUpdateGuard/` unless they say otherwise.

- **Secrets in configuration:** the Docker Hub PAT, Portainer password and API token, database password — bound in
  `Configuration/`, used in `DockerHub/`, `Portainer/`, `Vulnerabilities/` — [Hosting](../docs/areas/hosting.md#security-posture),
  [Telemetry](../docs/areas/telemetry.md#credentials-and-external-data)
- **Configured URLs and their user info:** where a URL sends credentials and how it is output — `Configuration/DockerUpdateGuardOptionsValidator.cs`,
  `Infrastructure/UrlRedaction.cs` — [Hosting](../docs/areas/hosting.md#validation),
  [Telemetry](../docs/areas/telemetry.md#credentials-and-external-data)
- **TLS relaxation of Docker instances:** `SkipCertificateValidation` and client certificates — `Docker/DockerInstanceClient.cs`,
  `Configuration/ClientCertificateLoader.cs` — [Docker instances](../docs/areas/docker-instances.md#tls-options)
- **Insecure Portainer HTTP:** the `AllowInsecureHttp` opt-in and its warning — `Portainer/PortainerClient.cs` —
  [Container actions](../docs/areas/container-actions.md#transport-security)
- **Docker Engine access:** the mounted socket is root-equivalent on the host — `Docker/DockerInstanceClient.cs` —
  [Docker instances](../docs/areas/docker-instances.md#transports)
- **Container actions:** the start, stop and restart of containers executed through Portainer and recorded as action runs —
  `Portainer/PortainerClient.cs`, `Portainer/ContainerActionService.cs`, `Components/Pages/RuntimeContainerDetail.razor` —
  [Container actions](../docs/areas/container-actions.md#actions)
- **External process execution:** Trivy's arguments, environment, the untrusted image reference and its JSON output —
  `Vulnerabilities/TrivyVulnerabilityProvider.cs`, `Infrastructure/ProcessRunner.cs` —
  [Vulnerabilities](../docs/areas/vulnerabilities.md#trivy)
- **Registry and Docker Scout calls:** untrusted JSON, request paths, pagination origins, bearer tokens —
  `DockerHub/DockerHubClient.cs`, `DockerHub/DockerHubAccessTokenCache.cs`, `Images/OciRegistryClient.cs`, `Images/Helper/ManifestPlatformSelector.cs`, `Vulnerabilities/DockerScoutVulnerabilityProvider.cs`,
  `Infrastructure/PaginationLinkGuard.cs`, `Infrastructure/RemoteJsonDocument.cs`, `Infrastructure/RequestDeadlineHandler.cs`, `Infrastructure/DeadlineReadStream.cs` — [Registries](../docs/areas/registries.md),
  [Vulnerabilities](../docs/areas/vulnerabilities.md#docker-scout)
- **Failure messages and remote text:** what a result, a scan run or a page may carry of an error or a remote answer —
  `Infrastructure/FailureReason.cs`, `Infrastructure/ResponseBodyExcerpt.cs`, `Infrastructure/ReportableException.cs`,
  `Infrastructure/ExternalJson.cs`, `Infrastructure/TransientHttpRetryHandler.cs` — [Integration failures](../docs/areas/integration-failures.md)
- **Database and migrations:** queries built from user input, the lock around migrations — `src/DockerUpdateGuard.Data/`,
  `DatabaseMigrator.cs` — [Hosting](../docs/areas/hosting.md#startup-sequence)
- **Container image and runtime user:** the non-root user and the CA import — `Dockerfile`, `entrypoint.sh` —
  [Hosting](../docs/areas/hosting.md#container-image)
- **Logging and telemetry of external data:** log messages and telemetry attributes carrying registry, container or
  credential data — the `*Logging.cs` and `*LoggingExtensions.cs` files, every other log call and span tag,
  `src/DockerUpdateGuard.Telemetry/` — [Telemetry](../docs/areas/telemetry.md#credentials-and-external-data)

## Guarantees

Deliberate behavior that must not change without the Product Manager. One line each: the guarantee in a few words,
a link to its decision record where it was a real choice, and a link to the area document that states it.

- No authentication or authorization in the app; it relies on a trusted network perimeter —
  [Hosting](../docs/areas/hosting.md#security-posture)
- Integration calls answer expected failures with an outcome and a fixed reason, never an error's own text —
  [decision 0009](../docs/decisions/0009-integration-results-carry-fixed-failure-reasons.md),
  [Integration failures](../docs/areas/integration-failures.md)
- Update evaluation and tag comparison perform no I/O, including the conservative major-upgrade gating —
  [Update detection](../docs/areas/update-detection.md#limits-and-errors)
- The registry/image-version catalog has one write path, get-or-create without duplicates under races; other records
  are written by the scan that owns them — [Image catalog](../docs/areas/image-catalog.md#the-catalog)
- Migrations are serialized at startup — [Hosting](../docs/areas/hosting.md#startup-sequence); the scan engine assumes
  one instance, and duplicate scans are wasteful, never incorrect — [Scan engine](../docs/areas/scan-engine.md#deployment-assumption)
- Layering: web startup and dependency wiring in `src/DockerUpdateGuard`, persistence in `.Data`, observability in
  `.Telemetry` — [`ARCHITECTURE.md`](../docs/ARCHITECTURE.md), *Solution layout*

## Integration surface

What the Reviewer checks when the diff introduces or changes a thing of this kind: every place that must
change with it. The area document of the feature (`docs/areas/README.md`) changes with every behavior change.

**A new or changed configuration option** touches:
- its options class in `src/DockerUpdateGuard/Configuration/` and `DockerUpdateGuardOptionsValidator.cs`
- its binding in `src/DockerUpdateGuard/ServiceCollectionExtensions.cs`
- `src/DockerUpdateGuard/appsettings.json`
- its row in the `README.md` configuration reference (key, default, short description; the section links its area)
- the test that pins the binding and the validation (`DockerUpdateGuardOptionsValidatorTests`)
- its meaning, range and reload behavior in the feature's area document; validation rules in
  [Hosting](../docs/areas/hosting.md#validation)
- `docs/docker-compose.*.yml` and `INSTALL.md` when operators must set it

**A new or changed service, background job or orchestrator** touches:
- its interface (`Images/Interfaces/` for the scan services and orchestrators)
- its registration and lifetime in the composition root (`src/DockerUpdateGuard/ServiceCollectionExtensions.cs`)
- the test double in the test project, and its tests in `src/Tests/DockerUpdateGuard.Tests`
- a job's interval, overlap and retention rules in [Scan engine](../docs/areas/scan-engine.md)
- the orchestration and background job sections of `docs/ARCHITECTURE.md`

**A new integration client or external call** touches:
- the client and its DTOs — the external shape must not leak past it; it answers `ExternalOperationResult<T>`
  ([Integration failures](../docs/areas/integration-failures.md#outcomes))
- `TransientHttpRetryHandler` on its `HttpClient`, or the reason it has none in its area document
  ([retry](../docs/areas/integration-failures.md#retry-of-transient-http-failures))
- messages built only from fixed text, configuration names and masked URLs (`ReportableException`); one that
  interpolates an error's message, a response body or an unmasked URL is blocking
  ([fixed reasons](../docs/areas/integration-failures.md#fixed-failure-reasons))
- remote text through `ResponseBodyExcerpt` and next-page links through `PaginationLinkGuard` and remote JSON through `ExternalJson` (`RemoteJsonDocument` where strings must decode)
- `RegistryMetadataService` dispatch (`CanHandle`) for a new registry type — [Registries](../docs/areas/registries.md)
- the fake/stub in the tests (`docs/UNIT_TESTS.md`, *Test doubles*)
- the client table in `docs/ARCHITECTURE.md`, *Integration clients*

**A change to the entity model** touches:
- `DockerUpdateGuardDbContext`, a migration named per the convention in `AGENTS.md` (*Project configuration*)
- `src/Tests/DockerUpdateGuard.Data.Tests`
- stored lengths and retention in the area document of the data
- `docs/ARCHITECTURE.md`, *Data layer*

**A new Blazor page or component** touches the UI tests (`…RenderTests`, `…PersistentStateTests`),
[Web UI](../docs/areas/web-ui.md#pages-and-routes) and the UI section of `docs/ARCHITECTURE.md`.

**A review of an open pull request** (`review-pr`, squad step 11) also compares the PR description with the
diff and reads the SonarQube Cloud result of the PR; a description that overstates or misses part of the change, or a failing quality gate, is a finding.

**Async code in service and data-access code** uses `.ConfigureAwait(false)`; EF Core navigation and query
assumptions (included navigations, tracking) are checked against the query that loads the entity.
