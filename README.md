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
  -v /path/to/appsettings.json:/app/appsettings.json:ro \
  networlddev/dockerupdateguard:latest
```

When DockerUpdateGuard runs inside a Linux container and should inspect the host Docker Engine through the Unix socket, two things must be in place:

1. `/var/run/docker.sock` must be mounted into the container
2. The corresponding Docker instance BaseUrl must be set to `unix:///var/run/docker.sock` in appsettings.json

Without both settings, the application cannot reach the host engine.

## Configuration model

DockerUpdateGuard uses normal ASP.NET Core configuration binding and prefers configuration files for container deployments:

- `appsettings.json`
- `appsettings.{Environment}.json`
- command-line arguments
- secret stores supported by ASP.NET Core

For example:

- JSON key: `DockerUpdateGuard:Scanning:RuntimeImageUpdateScanIntervalMinutes`

## Configuration reference

### Connection and host settings

| Key | Default | Required | Description |
| --- | --- | --- | --- |
| `ConnectionStrings:DockerUpdateGuard` | none | Yes* | Named PostgreSQL connection string used when `DockerUpdateGuard:ConnectionString` is not set |
| `DockerUpdateGuard:ConnectionString` | none | Yes* | Inline PostgreSQL connection string |
| `DockerUpdateGuard:ConnectionStringName` | `DockerUpdateGuard` | No | Name of the `ConnectionStrings` entry to resolve |
| `DockerUpdateGuard:DisplayVersion` | assembly version | No | Version string shown in the UI footer; the Docker image sets this automatically |


\* At least one of `DockerUpdateGuard:ConnectionString` or `ConnectionStrings:{ConnectionStringName}` must be configured.

### `DockerUpdateGuard:DockerHub`

| Key | Default | Required | Description |
| --- | --- | --- | --- |
| `Registry` | `docker.io` | Yes | Registry host handled by the Docker Hub integration |
| `ApiBaseUrl` | `https://hub.docker.com/` | No | Docker Hub API base address used for registries served by Docker Hub |
| `UserName` | none | No | Docker Hub username for authenticated API requests |
| `Pat` | none | No | Docker Hub personal access token |
| `RequestTimeoutSeconds` | `30` | No | Timeout for outbound Docker Hub and OCI registry requests |
| `MaxParallelRequests` | `4` | No | Maximum logical request parallelism for registry operations |

### `DockerUpdateGuard:ReleaseMetadata`

Base addresses of the upstream feeds used to resolve .NET and nginx release
versions. Override them to route the lookups through an internal mirror or proxy.

| Key | Default | Required | Description |
| --- | --- | --- | --- |
| `DotNetBaseUrl` | `https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/` | No | Base address of the .NET release metadata feed |
| `NginxBaseUrl` | `https://nginx.org/` | No | Base address of the nginx release feed |

### `DockerUpdateGuard:Vulnerabilities`

| Key | Default | Required | Description |
| --- | --- | --- | --- |
| `Enabled` | `false` | No | Enables vulnerability refresh |
| `Provider` | `None` | Required when enabled | Supported values: `None`, `DockerScout`, `Trivy` |
| `TrivyBaseUrl` | none | Required for `Trivy` | Base URL of the Trivy server |
| `DockerScoutLoginUrl` | `https://hub.docker.com/v2/users/login` | No | Docker Hub login endpoint used by the `DockerScout` provider |
| `DockerScoutBaseUrl` | `https://api.scout.docker.com` | No | Base address of the Docker Scout API |
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
| `SkipCertificateValidation` | `false` | No | Skip server certificate validation for TLS endpoints. **Insecure:** disables TLS authentication and exposes the connection to man-in-the-middle attacks; a warning is logged at runtime whenever it is active. Use only for trusted endpoints with self-signed certificates. |
| `CertificatePath` | none | No | Optional client certificate path for TLS-secured engine access |
| `RequestTimeoutSeconds` | `15` | No | Timeout for Docker Engine requests |

Recommended Linux socket configuration:

| Key | Example value |
| --- | --- |
| `DockerUpdateGuard:DockerInstances[0]:Name` | `Local Docker` |
| `DockerUpdateGuard:DockerInstances[0]:BaseUrl` | `unix:///var/run/docker.sock` |
| `DockerUpdateGuard:DockerInstances[0]:Enabled` | `true` |

### `DockerUpdateGuard:DockerInstances[].Portainer`

Portainer settings are optional and are only needed when Portainer-backed actions should be available.

| Key | Default | Required | Description |
| --- | --- | --- | --- |
| `Enabled` | `false` | No | Enables Portainer integration for the Docker instance |
| `BaseUrl` | none | Required when enabled | Absolute `https` Portainer URL (strongly recommended); see `AllowInsecureHttp` |
| `AllowInsecureHttp` | `false` | No | Allow plaintext `http` for `BaseUrl`; credentials are transmitted unencrypted — only use on localhost or trusted private networks |
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

### Container deployment configuration

Prefer mounting an appsettings.json into the container rather than using environment variables. Example:

- Mount your configuration file into the container's content root, for example: `-v /path/to/appsettings.json:/app/appsettings.json:ro`

See the JSON example above for the exact keys and structure.

## Notes for Docker-based deployments

- The container listens on port 8080 by default
- The image sets `DockerUpdateGuard:DisplayVersion` from the image build argument
- For Linux host monitoring, bind-mount `/var/run/docker.sock`
- For TLS-secured remote engines, also mount the client certificate file if `CertificatePath` is used
- The application needs outbound network access to the configured registry, optional Portainer endpoint, optional Trivy server, and optional OTLP collector

## License

This repository is distributed under the proprietary terms described in LICENSE.md. See LICENSE.md for the full license text.
