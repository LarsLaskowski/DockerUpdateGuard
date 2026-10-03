# Project

What the squad needs to know about this project that is not stack-specific. Read by the Lead, the Devil's
Advocate, Security, the Tester and the Reviewer. Not template-managed: `adopt-template` creates it once
and never overwrites it. A product PR updates it when the change makes an entry untrue
(`.squad/routing.md`, *Scope of a product PR*).

## Security areas

A change that touches one of these is tier `security` (`.squad/routing.md`). Details in
`docs/ARCHITECTURE.md`, *Security posture and operating assumptions*.

- **Secrets in configuration:** Docker Hub PAT, Portainer credentials/API token, database password — never
  logged, never shown in the UI, never in exception messages or telemetry attributes.
- **TLS and transport relaxations:** `SkipCertificateValidation` (Docker instances) and `AllowInsecureHttp`
  (Portainer) — must stay opt-in and keep their runtime warnings.
- **Docker Engine access:** `DockerInstanceClient` (HTTP, TCP, Unix socket, named pipe transports); the
  mounted Docker socket is root-equivalent on the host.
- **Container actions:** `PortainerClient` restart/redeploy/update actions and the UI that triggers them.
- **External process execution:** `TrivyVulnerabilityProvider` via `IProcessRunner` (argument construction,
  untrusted image references, JSON parsing of stdout).
- **Registry and Docker Hub calls:** `DockerHubClient`, `OciRegistryClient`, `DockerScoutVulnerabilityProvider`
  (untrusted JSON, pagination caps, token caching), `TransientHttpRetryHandler`.
- **Database and migrations:** EF Core queries built from user input, the PostgreSQL advisory lock around
  migrations.
- **Container image and runtime user:** `src/DockerUpdateGuard/Dockerfile` (non-root UID 64000, GID 0).
- **Logging and telemetry of external data:** OpenTelemetry attributes and log messages that carry
  registry, container or credential data.

## Guarantees

Deliberate behavior that must not change without the Product Manager. Each one is described in
`docs/ARCHITECTURE.md` (*Key design decisions* and the sections it points to).

- No authentication or authorization in the app itself: it assumes a trusted network perimeter (reverse
  proxy, VPN, private network).
- Integration clients never throw for expected failures; they return `ExternalOperationResult<T>`
  (`NotConfigured`, `Unsupported`, `NotFound`, `Failed`, `Unknown`) and orchestrators branch on the status.
- Update detection (`IUpdateDetectionService`) and tag comparison (`VersionTagResolutionHelper`) stay pure
  and I/O-free, including the conservative major-version-upgrade gating.
- `IImageCatalogRepository` is the single entry point for the registry/image-version catalog (get-or-create
  with dedup and race retry); other entities are written by the orchestrator that owns them.
- Migrations are serialized by a PostgreSQL advisory lock at startup; the scan engine assumes a single
  instance (duplicate scans are wasteful, never incorrect).
- Layering: web startup and DI wiring in `src/DockerUpdateGuard`, persistence in `.Data`, observability in
  `.Telemetry`.

## Integration surface

What the Reviewer checks when the diff introduces or changes a thing of this kind: every place that must
change with it.

**A new or changed configuration option** touches:
- its options class and the binding/validation in the host's composition root
- `appsettings*.json` and the configuration reference in `README.md` (key, environment variable, default)
- the deployment tiers in `docs/docker-compose.*.yml` and `INSTALL.md` when operators must set it
- `docs/ARCHITECTURE.md`, *Configuration model*

**A new or changed service, background job or orchestrator** touches:
- its interface and its registration *and lifetime* in the composition root
- the background job engine section of `docs/ARCHITECTURE.md` (schedule, overlap rules) for a job
- the tests in `src/Tests/DockerUpdateGuard.Tests`

**A new integration client or external call** touches:
- the client behind a narrow interface, returning `ExternalOperationResult<T>`
- `TransientHttpRetryHandler` attached to its `HttpClient`
- `RegistryMetadataService` dispatch (`CanHandle`) for a new registry type
- the client table in `docs/ARCHITECTURE.md`, *Integration clients*

**A change to the entity model** touches:
- `DockerUpdateGuardDbContext`, a migration named per the convention in `CLAUDE.md` (*Project configuration*)
- `src/Tests/DockerUpdateGuard.Data.Tests`
- `docs/ARCHITECTURE.md`, *Data layer*

**A new Blazor page or component** touches the UI tests (`…RenderTests`, `…PersistentStateTests`) and the
UI section of `docs/ARCHITECTURE.md`.

**Every review** also compares the PR description with the diff and reads the SonarQube Cloud result of the
PR; a description that overstates or misses part of the change, or a failing quality gate, is a finding.

**Async code in service and data-access code** uses `.ConfigureAwait(false)`; EF Core navigation and query
assumptions (included navigations, tracking) are checked against the query that loads the entity.

## Test doubles

MSTest with **NSubstitute** (`Substitute.For<T>()`) for collaborators that cross an infrastructure boundary
(HTTP, process, clock); real objects otherwise. Data tests use EF Core InMemory (host tests) or SQLite
(`DockerUpdateGuard.Data.Tests`). Details and helpers in `docs/UNIT_TESTS.md`.
