# Docker instances

## Scope

How DockerUpdateGuard connects to the Docker Engines it watches: the configured instances and their transports, the
read-only Engine calls it makes, the discovery of containers and their runtime status, the container snapshots that
record what ran where, and the CPU, memory and network samples collected per container and per instance. Operators
rely on it to configure engine access safely; the scan engine, update detection, base-image resolution and the UI rely
on the snapshots and samples it produces.

## Behavior

### Configured instances

- Docker instances exist only through configuration: each entry of `DockerUpdateGuard:DockerInstances` is one
  instance. There is no way to add an instance from the UI.
- Per instance the keys are `Name` (required), `BaseUrl` (required), `Enabled` (default `true`), `UseTls` (default
  `false`), `SkipCertificateValidation` (default `false`), `CertificatePath` (optional), `RequestTimeoutSeconds`
  (default `15`) and the optional `Portainer` section (see [Container actions](container-actions.md)).
- An instance is identified by its trimmed `Name`, compared case-insensitively. Renaming an instance in configuration
  is the removal of the old instance and the creation of a new one.
- The configured instances are synchronized into the database at startup, on every
  `DockerUpdateGuard:Scanning:DiscoveryIntervalMinutes` tick, and at the start of every runtime container scan and
  every resource-statistics collection (schedules: [Scan engine](scan-engine.md)). A synchronization:
  - creates a record for each new name, with registration source *configuration file*;
  - updates endpoint URI (trimmed `BaseUrl`, its user info masked as `***`), connection kind, enabled flag and `SkipCertificateValidation` of each
    existing one;
  - **deletes** every stored instance whose name is no longer configured, immediately and without waiting for
    retention, together with its scan runs, container snapshots, resource samples, Portainer endpoint and action
    runs. A removed instance is not kept as disabled. Update findings that pointed at a deleted snapshot lose that
    link but are kept.
- A disabled instance (`Enabled: false`) keeps its stored history but is not contacted: no discovery, no inspection,
  no resource sampling. Every Engine call for it answers *not configured*.
- The stored connection kind is derived from `BaseUrl`: `http` → Http, `https` → Https, `tcp` → Https when `UseTls`
  is `true`, otherwise Http, `unix` → UnixSocket, `npipe` → NamedPipe. Any other value is stored as *not set* and an
  enabled instance with it is logged as a warning.

### Transports

- `http://` and `https://` URLs are used as the Engine base address as given.
- `tcp://host:port` is turned into `https://host:port` when `UseTls` is `true`, otherwise `http://host:port`.
  `UseTls` has no effect on any other scheme.
- `unix:///path/to/socket` connects to that Unix domain socket (for example `unix:///var/run/docker.sock`).
- `npipe://server/pipe/name` connects to a Windows named pipe; an empty host or `.` means the local machine, a leading
  `pipe/` segment is optional. A named-pipe URL without a pipe name fails every call for that instance.
- Engine connections never use an HTTP proxy. Connections are pooled per instance and per connection-relevant
  setting (name, `BaseUrl`, TLS, certificate validation, certificate path); pooled TCP connections are renewed after
  at most 5 minutes.
- A connection whose creation failed (for example because the client certificate could not be loaded) is not kept in
  the pool; the next operation for that instance tries to create it again. A working pooled connection keeps the
  certificate it loaded: a file renewed under the same path is read only when that pool entry is rebuilt.
- Engine requests use the Docker Engine API version `v1.41` path prefix.
- Engine requests are not retried; a failed request fails that operation (the shared retry of outbound HTTP described
  in [Registries](registries.md) does not apply to Engine connections).

### TLS options

- `SkipCertificateValidation: true` accepts any server certificate on `https` and TLS `tcp` connections. It is opt-in
  only. Whenever it is active, a warning saying that the connection is unauthenticated and open to man-in-the-middle
  attacks is logged once per instance name per process lifetime, when the first connection for that instance is
  created.
- `CertificatePath` names the client certificate presented on `https` and TLS `tcp` connections. It must be a
  password-less PKCS#12 bundle (no password or the empty password) with the extension `.pfx` or `.p12` (any case)
  whose certificate has a private key. A PEM, DER or any other file is not accepted; the path is used exactly as
  configured. A blank `CertificatePath` means no client certificate.
- At runtime the certificate is loaded only for `https` and TLS `tcp` connections, when the connection is created; on
  `http`, non-TLS `tcp`, `unix` and `npipe` connections it is never loaded.
- Startup validation checks `CertificatePath` for every enabled instance that sets it, whatever its transport, so a
  certificate set on an instance that never presents it (for example a forgotten `UseTls: true`) still has to be
  usable. Disabled instances are not checked. A configuration reload checks it again (see
  [Hosting](hosting.md), *Validation*).
- The file is read whole, without a size limit: a path to a device or a named pipe would make startup or a reload hang
  or run out of memory. The path is operator configuration and trusted like `BaseUrl`.
- `SkipCertificateValidation` has no effect on `unix` and `npipe` connections either.

### Timeouts

- `RequestTimeoutSeconds` (1–300, default 15) bounds each single Engine request (container list, each image inspect,
  each image history, each stats call, host info) separately; it is not a budget for a whole scan.
- A request that exceeds it ends as *failed* with a message naming the instance and the timeout. Cancellation of the
  calling job (for example at shutdown) is not reported as a failure; it stops the operation.

### Container discovery

- Discovery lists **all** containers of an instance, including stopped ones.
- Per container it records: the container ID; the name (the first listed name without its leading `/`, or the
  container ID when it has no name); the image reference as the Engine reports it; the local image ID; the compose
  project (`com.docker.compose.project` label), the stack name (`com.docker.stack.namespace`) and the swarm service
  name (`com.docker.swarm.service.name`).
- The runtime status is one of Running, Paused, Restarting, Exited, Dead; any other Engine state (for example
  `created`) is *not set*. A container counts as running only in state `running`.
- The image digest of each container is taken from the repository digests of its local image (one image inspect per
  distinct local image): the digest whose registry and repository match the container's image reference; if none
  matches, the only digest when there is exactly one; otherwise none. When the image reference cannot be parsed, the
  first repository digest is used. Digests are stored lower-case. A failed inspect leaves the digest empty and does
  not fail discovery.
- Discovery emits one `dockerengine.request` trace activity per instance (see [Telemetry](telemetry.md)).

### Image inspection and history

- Other areas can read the inspect data of a local image (ID, repo tags, repo digests, environment variables, root
  file-system layers, creation time, operating system, architecture) and its history (creation time, created-by
  command, comment, tags per layer) from an instance, addressed by image ID or reference.
- An image the Engine does not know answers *not found*; any other non-success answer *failed*. How the data is used
  belongs to [Base images](base-images.md) and [Update detection](update-detection.md).

### Container snapshots

- Each runtime container scan of an enabled instance records one scan run for the instance and one container snapshot
  per discovered container, linked to that scan run. Snapshots are append-only history: every scan adds new rows,
  earlier ones stay until retention removes them ([Scan engine](scan-engine.md)).
- A snapshot holds the instance, container ID, name, compose project, stack name, service name, runtime status,
  running flag, the catalogued image version (registry, repository, tag and the discovered digest, see
  [Image catalog](image-catalog.md)) and its recording time. It starts with update assessment *not evaluated*; the
  assessment fields are filled by [Update detection](update-detection.md).
- The **current** containers of an instance are the snapshots of its most recent runtime scan run that recorded
  snapshots. A later run that recorded none (for example because the Engine was unreachable) does not empty the
  current set; the previous set stays current.
- A container whose image reference cannot be parsed produces no snapshot; the scan run then ends *partial* with a
  message naming the container.
- Outcome of the discovery step for the scan run: a *failed* discovery ends the run *failed*; *not configured* or
  *unsupported* ends it *partial*; an unexpected error ends it *failed*. The run's error message joins the distinct
  messages of all problems. One failing instance never stops the scans of the other instances.
- Configured instances that are disabled, or stored instances that are missing from the current configuration, are
  skipped by the scan without a scan run.

### Resource statistics

- Every `DockerUpdateGuard:Scanning:ResourceStatisticsIntervalMinutes` (1–1440, default 5) each enabled, configured
  instance is sampled ([Scan engine](scan-engine.md) owns the schedule).
- Only **running** containers are sampled, one at a time, each with a single non-streaming stats request. A container
  whose stats request answers non-success is left out of that round.
- Per container sample:
  - CPU percent = (container CPU time delta ÷ system CPU time delta) × online CPUs × 100, rounded to 4 decimals; the
    CPU count falls back to the per-CPU usage list length and then to 1; 0 when either delta is not positive. A
    multi-core container can exceed 100.
  - Memory usage and memory limit are the Engine's reported usage and limit in bytes.
  - Network receive and transmit totals are the sums over all of the container's network interfaces, in bytes.
  - The recording time is the Engine's stats timestamp, or the current time when it is missing.
  - Receive and transmit rates in bytes per second are computed against the previous sample of the same container ID
    on the same instance, rounded to 4 decimals. The rate is 0 for the first sample, when no time has passed, or when
    the total did not grow (for example after a container restart reset the counter).
- Per instance, one sample per round: container count, CPU percent and memory usage as the sums over the container
  samples, memory limit as the host's total memory reported by the Engine (0 when that request fails), network totals
  as sums, rates against the previous instance sample by the same rule, and the latest container recording time (the
  current time when no container was sampled). An instance with no running containers still gets a sample with
  count 0.
- Samples are kept until retention removes them ([Scan engine](scan-engine.md)); how they are charted belongs to
  [Web UI](web-ui.md).

## Limits and errors

- Invalid instance configuration fails startup ([Hosting](hosting.md)): a missing `Name`, a duplicate name (after
  trimming, case-insensitive), a missing `BaseUrl`, a `BaseUrl` that is not an absolute URI with scheme `http`,
  `https`, `tcp`, `unix` or `npipe`, or a `RequestTimeoutSeconds` outside 1–300. The shipped `appsettings.json`
  contains one instance with an empty `Name`, so an operator must set a name before the application starts.
- An unusable `CertificatePath` of an enabled instance fails startup with
  `'DockerUpdateGuard:DockerInstances:<Name>:CertificatePath' names an unusable client certificate '<path>': <reason>`,
  where the reason is one of:
  - `only password-less PKCS#12 files with the extension .pfx or .p12 are supported` — any other extension (or none);
    the file is not read;
  - `the file does not exist` — the file or its directory is missing;
  - `the file cannot be read or is not a password-less PKCS#12 bundle` — access denied or another read error, corrupt
    content, or a password-protected bundle;
  - `the bundle contains no certificate with a private key`.
- At runtime, an `https` or TLS `tcp` connection whose client certificate cannot be loaded (for example because the
  file was removed after startup) ends the operation *failed* with
  `The client certificate of Docker instance '<Name>' could not be loaded: <reason>` inside the operation's message,
  for example
  `Docker image inspect failed for 'Production': The client certificate of Docker instance 'Production' could not be loaded: the file does not exist`.
- These messages name the configured path (validation only) and the instance name; they never contain file contents,
  certificate fields or the underlying error text.
- Every Engine operation reports its outcome as a status (*succeeded*, *not configured*, *unsupported*, *not found*,
  *failed*) instead of throwing; the shared failure model is described in [Registries](registries.md).
- A non-success Engine answer is reported as *failed* with the instance name, the HTTP status code and the bounded excerpt of the
  response body ([Registries](registries.md#failure-model-of-integrations)). Unexpected errors are reported with the instance name and a fixed reason
  ([Registries](registries.md#failure-model-of-integrations)), except the composed certificate and named-pipe
  messages; every failure is logged with its error. These messages can end up in a scan run's error message and are
  shown in the UI.
- A message or log entry that names an endpoint shows its URL with the user info replaced by `***`; connecting still
  uses the configured URL.
- A resource collection that fails for an instance (list request failed or timed out, or any stats request timed out
  or broke) records **no** samples for that instance in that round and logs a warning; other instances are not
  affected.
- `SkipCertificateValidation` and a mounted Docker socket are trust relaxations the operator chooses; the socket is
  root-equivalent on the host ([Hosting](hosting.md)).

### Known gaps

- Instance lookups during a runtime scan or a resource collection compare the configured `Name` untrimmed with the
  trimmed stored name, so an instance whose configured name has leading or trailing spaces is stored but always
  skipped as missing from configuration. Tracked in
  [#266](https://github.com/LarsLaskowski/DockerUpdateGuard/issues/266).
- A single timed-out or broken stats request discards the whole round of samples for its instance, while a
  non-success stats answer only drops that container. Tracked in
  [#270](https://github.com/LarsLaskowski/DockerUpdateGuard/issues/270).

## Related decisions

- [0007](../decisions/0007-client-certificates-are-validated-as-pkcs12-at-startup.md): client certificates are
  validated as PKCS#12 at startup.

## Not here

- When scans and sampling run, retention of snapshots, samples and scan runs: [Scan engine](scan-engine.md).
- How a container's image is evaluated for updates and the assessment statuses: [Update detection](update-detection.md).
- Base-image and derived-runtime detection from inspect and history data: [Base images](base-images.md).
- Image reference parsing and the image catalog: [Image catalog](image-catalog.md).
- Restart and other actions through Portainer: [Container actions](container-actions.md).
- Pages that show instances, containers and resource charts: [Web UI](web-ui.md).
- Startup, validation failure and the container image's socket access: [Hosting](hosting.md).
