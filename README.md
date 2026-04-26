# DockerUpdateGuard

DockerUpdateGuard is a web application for tracking what is actually running in Docker, comparing it with registry metadata, and showing where updates, vulnerabilities, and shared base-image dependencies need attention.

It is designed for teams that want more than "a newer tag exists". The app keeps an inventory of observed images and runtime containers, correlates them with registry tags and digests, samples CPU, memory, and network usage, and keeps a scan history so update decisions can be reviewed instead of guessed.

## What the application helps with

- Discover configured Docker instances and the containers currently running on them
- Detect whether a running container is up to date, behind a newer version, or needs manual review
- Resolve alias tags such as `latest` to matching semantic version tags when they share the same digest
- Track resource usage over time for Docker instances and runtime containers
- Correlate runtime containers with observed images and shared base images
- Refresh vulnerability data for images
- Keep historical scan results for auditing and troubleshooting

## Main UI areas

- **Dashboard**: summary view of the current inventory
- **Observed Images**: repositories and tags discovered from Docker Hub account data
- **Runtime Containers**: live workload inventory with update and vulnerability state
- **Docker Instances**: configured engines plus usage history
- **Shared Base Images**: base-image reuse across observed images
- **Scan History**: recent background activity and outcomes

## Runtime requirements

DockerUpdateGuard expects:

- A PostgreSQL database
- Access to one or more Docker Engine endpoints
- Optional Docker Hub credentials for authenticated registry access
- Optional Portainer access per Docker instance
- Optional OTLP endpoint for telemetry export
- Optional Trivy server when Trivy-based vulnerability scanning is enabled

The application applies EF Core migrations automatically on startup.

## Running the application

### Local development

Use the solution file from the repository root:

```powershell
dotnet restore DockerUpdateGuard.slnx
reihitsu-format ./
dotnet build DockerUpdateGuard.slnx -c Release --no-restore
dotnet test src\Tests\**\*.csproj -c Release --no-build --logger trx --collect:"XPlat Code Coverage"
```

### Docker image

The container image listens on port `8080` by default.

Example for Linux with a local Docker socket:

```bash
docker run -d \
  --name dockerupdateguard \
  -p 8080:8080 \
  --mount type=bind,source=/var/run/docker.sock,target=/var/run/docker.sock \
  -e ConnectionStrings__DockerUpdateGuard="Host=postgres;Port=5432;Database=dockerupdateguard;Username=dockerupdateguard;Password=change-me" \
  -e DockerUpdateGuard__DockerInstances__0__Name="Local Docker" \
  -e DockerUpdateGuard__DockerInstances__0__BaseUrl="unix:///var/run/docker.sock" \
  -e DockerUpdateGuard__DockerInstances__0__Enabled="true" \
  networlddev/dockerupdateguard:latest
```

When DockerUpdateGuard runs inside a Linux container and should inspect the host Docker Engine through the Unix socket, two things must match:

1. `/var/run/docker.sock` must be mounted into the container
2. `DockerUpdateGuard__DockerInstances__0__BaseUrl` must be set to `unix:///var/run/docker.sock`

Without both settings, the application cannot reach the host engine.

## Configuration model

DockerUpdateGuard uses normal ASP.NET Core configuration binding. That means you can use:

- `appsettings.json`
- `appsettings.{Environment}.json`
- environment variables with `__` as the separator
- command-line arguments
- secret stores supported by ASP.NET Core

For example:

- JSON key: `DockerUpdateGuard:Scanning:RuntimeImageUpdateScanIntervalMinutes`
- Environment variable: `DockerUpdateGuard__Scanning__RuntimeImageUpdateScanIntervalMinutes`

## Configuration reference

### Connection and host settings

| Key | Default | Required | Description |
| --- | --- | --- | --- |
| `ConnectionStrings:DockerUpdateGuard` | none | Yes* | Named PostgreSQL connection string used when `DockerUpdateGuard:ConnectionString` is not set |
| `DockerUpdateGuard:ConnectionString` | none | Yes* | Inline PostgreSQL connection string |
| `DockerUpdateGuard:ConnectionStringName` | `DockerUpdateGuard` | No | Name of the `ConnectionStrings` entry to resolve |
| `DockerUpdateGuard:DisplayVersion` | assembly version | No | Version string shown in the UI footer; the Docker image sets this automatically |
| `ASPNETCORE_URLS` | `http://+:8080` in the image | No | ASP.NET Core bind address |

\* At least one of `DockerUpdateGuard:ConnectionString` or `ConnectionStrings:{ConnectionStringName}` must be configured.

### `DockerUpdateGuard:DockerHub`

| Key | Default | Required | Description |
| --- | --- | --- | --- |
| `Registry` | `docker.io` | Yes | Registry host handled by the Docker Hub integration |
| `UserName` | none | No | Docker Hub username for authenticated API requests |
| `Pat` | none | No | Docker Hub personal access token |
| `RequestTimeoutSeconds` | `30` | No | Timeout for outbound Docker Hub and OCI registry requests |
| `MaxParallelRequests` | `4` | No | Maximum logical request parallelism for registry operations |

### `DockerUpdateGuard:Vulnerabilities`

| Key | Default | Required | Description |
| --- | --- | --- | --- |
| `Enabled` | `false` | No | Enables vulnerability refresh |
| `Provider` | `None` | Required when enabled | Supported values: `None`, `DockerScout`, `Trivy` |
| `TrivyBaseUrl` | none | Required for `Trivy` | Base URL of the Trivy server |
| `RequestTimeoutSeconds` | `30` | No | Timeout for vulnerability provider requests |

### `DockerUpdateGuard:Scanning`

| Key | Default | Description |
| --- | --- | --- |
| `DiscoveryIntervalMinutes` | `15` | Synchronization interval for configured Docker instances |
| `DockerHubAccountDiscoveryIntervalMinutes` | `60` | Interval for refreshing the Docker Hub account image inventory |
| `OwnImageBaseScanIntervalMinutes` | `60` | Interval for resolving base-image chains of observed images |
| `DockerHubRequestLimitWindowHours` | `6` | Quota window size for scheduled Docker Hub refreshes |
| `DockerHubRequestLimitPerWindow` | `200` | Scheduled Docker Hub request budget per quota window |
| `DockerHubReservedManualRequestsPerWindow` | `40` | Reserved request budget for manual scans and ad-hoc activity |
| `RuntimeImageUpdateScanIntervalMinutes` | `30` | Interval for refreshing runtime container state and update status |
| `ResourceStatisticsIntervalMinutes` | `5` | Interval for sampling CPU, memory, and network usage |
| `VulnerabilityRefreshIntervalMinutes` | `180` | Interval for refreshing vulnerability information |
| `CleanupIntervalMinutes` | `720` | Interval for cleaning old scan data |
| `RetryCount` | `2` | Retry count for transient background failures |
| `RetainScanRunsDays` | `30` | Retention period for completed scan history |

### `DockerUpdateGuard:DockerInstances[]`

Each entry describes one Docker Engine endpoint.

| Key | Default | Required | Description |
| --- | --- | --- | --- |
| `Name` | none | Yes | Display name of the Docker instance |
| `BaseUrl` | none | Yes | Docker endpoint URI; validation allows `http`, `https`, `tcp`, `unix`, and `npipe` |
| `Enabled` | `true` | No | Enables or disables this Docker instance |
| `UseTls` | `false` | No | For `tcp://` endpoints, upgrade the connection to HTTPS |
| `SkipCertificateValidation` | `false` | No | Skip server certificate validation for TLS endpoints |
| `CertificatePath` | none | No | Optional client certificate path for TLS-secured engine access |
| `RequestTimeoutSeconds` | `15` | No | Timeout for Docker Engine requests |

Recommended Linux socket configuration:

| Key | Example value |
| --- | --- |
| `DockerUpdateGuard__DockerInstances__0__Name` | `Local Docker` |
| `DockerUpdateGuard__DockerInstances__0__BaseUrl` | `unix:///var/run/docker.sock` |
| `DockerUpdateGuard__DockerInstances__0__Enabled` | `true` |

### `DockerUpdateGuard:DockerInstances[].Portainer`

Portainer settings are optional and are only needed when Portainer-backed actions should be available.

| Key | Default | Required | Description |
| --- | --- | --- | --- |
| `Enabled` | `false` | No | Enables Portainer integration for the Docker instance |
| `BaseUrl` | none | Required when enabled | Absolute `http` or `https` Portainer URL |
| `Username` | none | Conditional | Username for Portainer login |
| `Password` | none | Conditional | Password for Portainer login |
| `ApiToken` | none | Conditional | Portainer API token; takes precedence over username/password |
| `EndpointId` | none | No | Explicit Portainer endpoint ID; auto-discovered when omitted |
| `RequestTimeoutSeconds` | `15` | No | Timeout for Portainer requests |

When Portainer is enabled, either:

- `ApiToken`, or
- `Username` and `Password`

must be configured.

### `Telemetry`

| Key | Default | Required | Description |
| --- | --- | --- | --- |
| `ServiceName` | `DockerUpdateGuard` | Required when telemetry is enabled | Service name used in telemetry resources |
| `OtlpEndpoint` | none | No | Absolute `http` or `https` OTLP endpoint |
| `Instance` | none | No | Logical deployment instance name |
| `EnableLogging` | `true` | No | Enables OpenTelemetry logging export |
| `EnableMetrics` | `true` | No | Enables OpenTelemetry metrics export |
| `EnableTracing` | `true` | No | Enables OpenTelemetry tracing export |

If all three telemetry switches are `false`, telemetry is effectively disabled.

## Example configuration

### JSON

```json
{
  "ConnectionStrings": {
    "DockerUpdateGuard": "Host=postgres;Port=5432;Database=dockerupdateguard;Username=dockerupdateguard;Password=change-me"
  },
  "DockerUpdateGuard": {
    "ConnectionStringName": "DockerUpdateGuard",
    "DockerHub": {
      "Registry": "docker.io",
      "UserName": "dockerupdateguard",
      "Pat": "change-me",
      "RequestTimeoutSeconds": 30,
      "MaxParallelRequests": 4
    },
    "Vulnerabilities": {
      "Enabled": true,
      "Provider": "Trivy",
      "TrivyBaseUrl": "http://trivy:4954",
      "RequestTimeoutSeconds": 30
    },
    "Scanning": {
      "DiscoveryIntervalMinutes": 5,
      "DockerHubAccountDiscoveryIntervalMinutes": 15,
      "OwnImageBaseScanIntervalMinutes": 30,
      "DockerHubRequestLimitWindowHours": 6,
      "DockerHubRequestLimitPerWindow": 200,
      "DockerHubReservedManualRequestsPerWindow": 40,
      "RuntimeImageUpdateScanIntervalMinutes": 10,
      "ResourceStatisticsIntervalMinutes": 5,
      "VulnerabilityRefreshIntervalMinutes": 60,
      "CleanupIntervalMinutes": 720,
      "RetryCount": 1,
      "RetainScanRunsDays": 14
    },
    "DockerInstances": [
      {
        "Name": "Local Docker",
        "BaseUrl": "unix:///var/run/docker.sock",
        "Enabled": true,
        "UseTls": false,
        "SkipCertificateValidation": false,
        "RequestTimeoutSeconds": 15,
        "Portainer": {
          "Enabled": false,
          "BaseUrl": "https://portainer.local",
          "EndpointId": "1",
          "RequestTimeoutSeconds": 15
        }
      }
    ]
  },
  "Telemetry": {
    "ServiceName": "DockerUpdateGuard",
    "Instance": "Production",
    "OtlpEndpoint": "http://otel-collector:4317",
    "EnableLogging": true,
    "EnableMetrics": true,
    "EnableTracing": true
  }
}
```

### Environment variables for container deployment

```text
ConnectionStrings__DockerUpdateGuard=Host=postgres;Port=5432;Database=dockerupdateguard;Username=dockerupdateguard;Password=change-me
DockerUpdateGuard__DockerHub__Registry=docker.io
DockerUpdateGuard__DockerHub__UserName=dockerupdateguard
DockerUpdateGuard__DockerHub__Pat=change-me
DockerUpdateGuard__Vulnerabilities__Enabled=true
DockerUpdateGuard__Vulnerabilities__Provider=Trivy
DockerUpdateGuard__Vulnerabilities__TrivyBaseUrl=http://trivy:4954
DockerUpdateGuard__DockerInstances__0__Name=Local Docker
DockerUpdateGuard__DockerInstances__0__BaseUrl=unix:///var/run/docker.sock
Telemetry__OtlpEndpoint=http://otel-collector:4317
```

## Notes for Docker-based deployments

- The published image already sets `ASPNETCORE_URLS=http://+:8080`
- The image also sets `DockerUpdateGuard__DisplayVersion` from the image build argument
- For Linux host monitoring, bind-mount `/var/run/docker.sock`
- For TLS-secured remote engines, also mount the client certificate file if `CertificatePath` is used
- The application needs outbound network access to the configured registry, optional Portainer endpoint, optional Trivy server, and optional OTLP collector

## License

No license information is currently declared in this repository.
