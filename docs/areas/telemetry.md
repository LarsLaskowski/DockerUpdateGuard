# Telemetry

## Scope

What the application emits about itself as logs, traces and metrics, how each signal is switched on and exported,
which application-specific spans and instruments exist and when they change, and what may never appear in them.
Operators rely on it to monitor the application in an OpenTelemetry backend; dashboards and alerts rely on the
names and attributes stated here.

## Behavior

### Signals and switches

- There are three signals: logs, traces and metrics. Each is switched independently by `Telemetry:EnableLogging`,
  `Telemetry:EnableTracing` and `Telemetry:EnableMetrics`; all three default to `true`.
- When all three are `false`, no OpenTelemetry pipeline is set up at all and the other `Telemetry` keys are not
  validated.
- The platform's console logging is independent of these switches and always active. `Logging:LogLevel` filters apply
  to console output and to exported logs alike (shipped defaults: `Information`, and `Warning` for
  `Microsoft.AspNetCore`).
- The `Telemetry` section is read once at startup; changing it requires a restart.

### Export

- All enabled signals are exported over OTLP to one endpoint, `Telemetry:OtlpEndpoint`. It must be an absolute `http`
  or `https` URI. No transport protocol is configured by the application, so the OTLP exporter's default (gRPC, for
  example `http://collector:4317`) applies.
- Without `Telemetry:OtlpEndpoint`, enabled signals are collected in-process but nothing is exported. There is no
  fallback exporter (no console or Prometheus exporter).
- An unreachable or failing collector affects only the export; scanning, the UI and the database are unaffected.

### Resource

Every exported signal carries the same resource:

| Attribute | Value |
| --- | --- |
| `service.name` | `Telemetry:ServiceName` (default `DockerUpdateGuard`); must not be blank while any signal is enabled. |
| `service.version` | The environment variable `DockerUpdateGuard__DisplayVersion` (the container image sets it); otherwise the application's informational version, file version or assembly version, in that order; otherwise `unknown`. |
| `deployment.environment.name` | `Telemetry:Instance`, only when it is set. The shipped `appsettings.json` sets `Production`; there is no built-in default. |

### Traces

- Traces contain a span for every incoming HTTP request and every outgoing HTTP call, plus the application's own
  spans from the source `DockerUpdateGuard`.
- Application spans emitted today, all of kind client:

  | Span name | Wraps | Attribute |
  | --- | --- | --- |
  | `dockerhub.request` | A Docker Hub repository lookup, tag lookup or base-image resolution | `docker.image.reference` (the full image reference) |
  | `dockerengine.request` | Container discovery on one Docker instance | `docker.instance.name` (the configured instance name) |

- The span names `scan.run`, `cve.request`, `portainer.request`, `portainer.action` and `persistence.operation` are
  reserved for the application's source but not emitted (see *Open questions*).

### Logs

- Exported log records include the category, the formatted message, active scopes and the structured message
  parameters as individual attributes.
- Application log events have fixed event IDs and message templates; the startup and database events are
  `1000`–`1006` and the inventory refresh is `1010`. Message parameters are named in PascalCase
  (`{DockerInstanceName}`, `{ImageReference}`, `{StatusCode}`).

### Metrics

- Metrics contain the application's own instruments from the meter `DockerUpdateGuard`, plus the platform's
  instruments for incoming HTTP requests, outgoing HTTP calls and the .NET runtime.
- Scan instruments, recorded once per finished scan run after its final status is known (scan runs are defined in
  [Scan engine](scan-engine.md)):

  | Name | Type | Unit | Recorded |
  | --- | --- | --- | --- |
  | `dockerupdateguard.scans.total` | Counter | — | `+1` for every finished scan run |
  | `dockerupdateguard.scans.failed` | Counter | — | `+1` for every scan run that finished with status `Failed` |
  | `dockerupdateguard.scans.duration` | Histogram | `s` | The run's duration in seconds |

  All three carry `scan.type` (`ObservedImage`, `RuntimeContainer` or `Vulnerability`) and `result.status` (the
  final status, for example `Succeeded`, `Partial` or `Failed`). A run that ends `Partial` is counted in
  `scans.total` but not in `scans.failed`.
- Inventory gauges:

  | Name | Value |
  | --- | --- |
  | `dockerupdateguard.observed_images` | Number of observed images, enabled or not |
  | `dockerupdateguard.runtime_containers` | Number of distinct containers (per Docker instance and container ID) that have a stored snapshot |
  | `dockerupdateguard.base_images.deduplicated` | Number of distinct image versions used as a base image by at least one image |
  | `dockerupdateguard.findings.update.active` | Number of active update findings |
  | `dockerupdateguard.findings.cve.active` | Number of active vulnerability findings |
  | `dockerupdateguard.findings.needs_review.active` | Number of active update findings that are tag recommendations |

- Gauges report the values of their last refresh; observing them never queries the database. They are refreshed:
  - once during startup, before requests are served (see [Hosting](hosting.md));
  - after every finished observed-image scan, runtime-container scan and vulnerability refresh;
  - after every cleanup pass.
- Between refreshes a gauge can lag behind the database, for example after a manual registration that triggers no
  scan.

### Credentials and external data

- Credential values never appear in log messages, log attributes or span attributes: not the Docker Hub PAT, the
  Portainer password or API token, the bearer tokens obtained from Docker Hub, registries, Docker Scout or Portainer,
  nor the database connection string. Credentials travel only in request headers, and headers are not recorded.
- Database statements are logged without parameter values.
- What logs and spans may carry about external systems: configured instance names, image references, configured base
  URLs, the Docker Hub account name, HTTP methods and status codes, counts, durations and error messages from
  external tools. Retry log lines carry the request URL without its query string.

## Limits and errors

- `Telemetry:OtlpEndpoint` set but not an absolute `http`/`https` URI: startup fails with
  `'Telemetry:OtlpEndpoint' must be an absolute http or https URI`.
- `Telemetry:ServiceName` blank while any signal is enabled: startup fails with
  `'Telemetry:ServiceName' must be configured when telemetry is enabled`.
- Both checks run while services are registered, before the host is built; a failure aborts the process before the
  database is contacted.
- Export failures are handled inside the exporter and are never surfaced to users or scans.

## Related decisions

None yet.

## Not here

- Deploying the collector and the Grafana/LGTM stack: `INSTALL.md` and `docs/docker-compose.full.yml`.
- What a scan run is and when scans run: [Scan engine](scan-engine.md).
- What counts as a finding, a tag recommendation or a base image: [Update detection](update-detection.md),
  [Vulnerabilities](vulnerabilities.md), [Base images](base-images.md).
- Secrets in the UI: [Web UI](web-ui.md); secrets in configuration: [Hosting](hosting.md).
- Runtime warnings for `SkipCertificateValidation` and `AllowInsecureHttp`: [Docker instances](docker-instances.md)
  and [Container actions](container-actions.md).

## Open questions

1. The span names `scan.run`, `cve.request`, `portainer.request`, `portainer.action` and `persistence.operation` are
   defined but no code starts such spans, so scans, vulnerability provider calls, Portainer calls and persistence
   have no application span. Should they be emitted, or the names dropped?
2. `dockerupdateguard.runtime_containers` counts every container that still has a stored snapshot, including
   containers that have since stopped or been removed, until cleanup deletes their snapshots. Is "containers seen
   within the retention period" the intended meaning, or should it count only currently known containers?
3. Configured base URLs (Docker instance, Portainer) are logged verbatim, and the retry log keeps the user-info part of
   a URL. A base URL with embedded credentials (`https://user:secret@host`) would therefore reach the logs. Should
   URLs be stripped of user info before logging?
4. A scan run that ends `Partial` is not counted in `dockerupdateguard.scans.failed`. Is that the intended alerting
   signal, or should partial runs be counted separately?
