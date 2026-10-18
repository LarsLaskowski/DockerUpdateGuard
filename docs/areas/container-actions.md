# Container actions

## Scope

The optional Portainer integration of a Docker instance: how a Portainer server is configured and mapped to a Docker
instance, how DockerUpdateGuard authenticates against it, which container actions it can execute through it, and the
stored records reserved for action runs. Operators rely on it to configure Portainer access without weakening
transport security; the UI is the intended caller of the actions.

## Behavior

### Configuration and mapping

- Portainer is configured per Docker instance under `DockerUpdateGuard:DockerInstances:<n>:Portainer` with the keys
  `Enabled` (default `false`), `BaseUrl`, `Username`, `Password`, `ApiToken`, `EndpointId`, `AllowInsecureHttp`
  (default `false`) and `RequestTimeoutSeconds` (default `15`).
- A Docker instance has at most one Portainer endpoint. It is synchronized together with the instance
  ([Docker instances](docker-instances.md)):
  - when Portainer is enabled, the stored endpoint is created or updated with the name `<instance name> Portainer`,
    the trimmed `BaseUrl` and the configured `EndpointId`, and marked enabled;
  - when Portainer is disabled, an existing stored endpoint is kept but marked disabled;
  - when the Docker instance is removed from configuration, its Portainer endpoint and action runs are deleted with it.
- Credentials (`Username`, `Password`, `ApiToken`) are never stored in the database; they are read from configuration
  at the time of each call.
- `EndpointId` selects the Portainer environment that manages the Docker instance. When it is empty, the first
  environment returned by Portainer's environment list is used, resolved again on every call.

### Transport security

- `BaseUrl` must be an absolute `https` URL. A plaintext `http` URL is accepted only with `AllowInsecureHttp: true`;
  without it, startup fails with a message that names the key to set ([Hosting](hosting.md)).
- `AllowInsecureHttp` is opt-in only. It is a validation switch: no runtime warning is logged while it is active.
- Portainer server certificates are always validated; there is no option to skip validation for Portainer.

### Authentication

- When `ApiToken` is set, it is sent as an API key header on every request and takes precedence over username and
  password.
- Otherwise `Username` and `Password` are posted to Portainer's authentication endpoint and the returned JWT is used as
  a bearer token. The login is repeated for every operation; tokens are not cached.
- A login that answers non-success, or answers success without a token, ends the operation with an authentication
  error that names the Portainer base URL and the HTTP status code; credentials never appear in it or in the log.

### Capability check

- For a Docker instance, a capability check reports whether Portainer is configured, whether actions are supported, and
  a message:
  - Portainer disabled or without `BaseUrl` → not configured, no actions;
  - Portainer's status endpoint answers non-success → configured, no actions, message with the HTTP status code;
  - no environment configured and none found → configured, no actions, "No Portainer endpoint found";
  - otherwise → configured, actions supported, message naming the environment used;
  - any error (including authentication failure) → configured, no actions, message with the error text.

### Actions

- The executable actions are container actions only: `start`, `stop`, `restart`, `kill`, `pause` and `unpause`
  (case-insensitive). Any other action name is rejected before Portainer is contacted, with a message listing the
  allowed names.
- The target container is named, not identified by ID: Portainer's container list for the environment (including
  stopped containers) is searched for an exact, case-insensitive name match, with or without the leading `/`.
- The action is executed by Portainer on the Docker Engine of that environment. The result is either *succeeded* with
  a confirmation message or *failed* with a reason: Portainer not configured, no environment found, container not
  found, HTTP status code of a non-success answer, or the error text.
- Actions never throw to the caller; every outcome is a result. Executed, failed and not-found actions are logged with
  the action, the container name and the Docker instance name.
- Portainer requests go through the shared retry of transient HTTP failures described in
  [Registries](registries.md). This includes the login and the action request itself.
- `RequestTimeoutSeconds` (1–300, default 15) bounds each Portainer request including its retries.

### Action runs

- The data model holds an action-run record per requested action: the Docker instance, optionally the Portainer
  endpoint and the container snapshot, the action type (*restart container*, *redeploy stack*, *update service*), the
  resource type (*container*, *service*, *stack*), the resource name (up to 200 characters), status (*requested*,
  *running*, *succeeded*, *failed*, *cancelled*), requester, Portainer task ID, error message, and request and
  completion times.
- A container snapshot referenced by an action run is never removed by retention ([Scan engine](scan-engine.md)).
  Deleting the Portainer endpoint or the snapshot keeps the action run and clears the link.
- Today no component executes actions or writes action runs: neither a page nor a background job calls the capability
  check or an action (see *Open questions*).

## Limits and errors

- Validation, applied only when `Enabled` is `true`, fails startup when: `BaseUrl` is missing; `BaseUrl` is not an
  absolute `http`/`https` URI; `BaseUrl` is `http` without `AllowInsecureHttp: true`; neither `ApiToken` nor both
  `Username` and `Password` are set; `RequestTimeoutSeconds` is outside 1–300.
- A disabled Portainer section is not validated at all; its values may be incomplete.
- Error messages and log entries name the Docker instance, the Portainer base URL, the action, the container name and
  HTTP status codes. They never contain the password, the API token or the JWT.

## Related decisions

None yet.

## Not here

- Container discovery, snapshots and Docker Engine connectivity: [Docker instances](docker-instances.md).
- The retry policy and the shared failure model of outbound HTTP: [Registries](registries.md).
- Startup validation failure and configuration reload: [Hosting](hosting.md).
- Retention of snapshots and other history: [Scan engine](scan-engine.md).
- What the UI shows about Portainer availability: [Web UI](web-ui.md).

## Open questions

1. No page or job triggers Portainer actions, and no action run is ever written. Is the integration meant to be wired
   into the UI (the security area list names "the UI that triggers them"), or should the unused parts be removed?
2. The executable actions (`start`, `stop`, `restart`, `kill`, `pause`, `unpause` on containers) do not match the
   action types of the action-run record (*restart container*, *redeploy stack*, *update service* on containers,
   services and stacks). Which set is the intended contract? Redeploying stacks and updating services are not
   implemented.
3. The action request and the login are retried on HTTP 408, 429 and 5xx answers like any idempotent request, so one
   requested action can be executed more than once. Should non-idempotent Portainer requests be excluded from retry?
4. `AllowInsecureHttp` produces no runtime warning, while the documentation of the security posture says it is logged
   as a warning at runtime. Should a warning be added, as for `SkipCertificateValidation`?
5. Without `EndpointId`, the first environment Portainer lists is used, which can be an environment other than the one
   that manages the Docker instance. Should an explicit `EndpointId` be required when Portainer manages more than one
   environment?
