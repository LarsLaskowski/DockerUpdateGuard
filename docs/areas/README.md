# Areas

What the software does, area by area. Each area has one document here that states the behavior a user, an
operator or another component can rely on — formats, limits, error behavior, guarantees. The
[decision records](../decisions/README.md) explain *why* individual choices were made; the area documents say
*what holds today*. `ARCHITECTURE.md` shows how the parts fit together and links the areas instead of repeating them.

## Rules

- **One document per area**, `docs/areas/<slug>.md`, created from [`_template.md`](_template.md) and listed in the
  index below. The squad Lead keeps it in step with the code: a change in behavior updates the area document in the
  same pull request.
- **Normative and stable.** Write what must hold, not how one language or library does it: the test is whether
  the document would still be true if the component were rewritten in another language. No class, package or
  function names as a contract (mention them only as a pointer to where it is implemented).
- **One home per behavior.** A cross-cutting topic (security, offline behavior) lives in the area that owns it; the
  others link there.
- **Size.** One readable document per area: split one that grows past a few thousand words, fold one that
  says only a few sentences into a neighbor. Development and release process are not areas (see
  `CONTRIBUTING.md`).
- **A new area is a Lead decision** made in the plan (`plan.md`, *Areas*), with its name and scope in the index.
- Every decision record names its area in its `Area:` field (`—` for a record about the process). Area documents are
  not frozen by a release; their history is Git.

## Index

<!-- project:begin area-index -->
| Area | Scope | Not here |
| ---- | ----- | -------- |
| [Hosting and configuration](hosting.md) | Loading and validating configuration, startup sequence and database migrations, HTTP pipeline and error pages, container image and runtime user, security posture | Meaning of feature-specific keys (their areas); full key reference (`README.md`); build, CI and release (`CONTRIBUTING.md`) |
| [Docker instances](docker-instances.md) | Docker Engine connectivity and transports, certificate relaxation, discovery of running containers and their runtime status, resource statistics | Update evaluation of containers (Update detection); Portainer actions (Container actions); schedules (Scan engine) |
| [Registries](registries.md) | Read access to Docker Hub and OCI registries, registry dispatch, Docker Hub account discovery and request budget, retry of transient HTTP failures, failure model of all integrations | When scans run (Scan engine); Docker Scout (Vulnerabilities); base-image resolution (Base images) |
| [Image catalog](image-catalog.md) | Image reference parsing and normalization, the deduplicated registry/repository/tag/digest catalog, observed images and how they are registered | Base images (Base images); tag comparison (Update detection); CVEs (Vulnerabilities) |
| [Base images](base-images.md) | Base-image resolution, the base-image relationship graph, shared base images, derived .NET/nginx runtime detection against release feeds | Tag ranking (Update detection); registry access (Registries) |
| [Update detection](update-detection.md) | Tag formats and variant families, alias resolution, pre-release ranking, major-upgrade gating, update assessment, findings and tag candidates, manual tag selection | Fetching tags (Registries); retention (Scan engine) |
| [Vulnerabilities](vulnerabilities.md) | Provider choice (Trivy, Docker Scout, none), assessment status, severities, fix status and CVSS, manual rescan, configuration hints | Schedule and cleanup (Scan engine); display (Web UI) |
| [Container actions](container-actions.md) | Portainer endpoints and authentication, insecure HTTP opt-in, restart/redeploy/update actions and their recorded runs | Container discovery (Docker instances) |
| [Scan engine](scan-engine.md) | Background jobs and their intervals, dynamic own-image interval, overlap rules, scan runs, crash recovery, retention and cleanup, single-instance assumption | What a scan evaluates (the feature areas) |
| [Web UI](web-ui.md) | Pages and routes, what each page shows and triggers, dashboard refresh, error and not-found pages, theme | How the shown values are computed (the feature areas) |
| [Telemetry](telemetry.md) | Logs, traces and metrics, OTLP export, activity and instrument names, inventory gauges, what never reaches telemetry | Deployment of the observability stack (`INSTALL.md`) |
<!-- project:end area-index -->
