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
    the trimmed `BaseUrl` (its user info masked as `***`) and the configured `EndpointId`, and marked enabled;
  - when Portainer is disabled, an existing stored endpoint is kept but marked disabled, and its stored base URL is rewritten with the user info
    masked;
  - when the Docker instance is removed from configuration, its Portainer endpoint and action runs are deleted with it.
- Credentials (`Username`, `Password`, `ApiToken`) are never stored in the database; they are read from configuration
  at the time of each call.
- `EndpointId` selects the Portainer environment that manages the Docker instance. When it is empty, the first
  environment returned by Portainer's environment list is used, resolved again on every call.

### Transport security

- `BaseUrl` must be an absolute `https` URL. A plaintext `http` URL is accepted only with `AllowInsecureHttp: true`;
  without it, startup fails with a message that names the key to set ([Hosting](hosting.md)).
- `AllowInsecureHttp` is opt-in only. While a Docker instance's `BaseUrl` is `http`, the first request to Portainer
  for that instance logs a warning (EventId 3311) that names the instance only, never the URL or a credential. It is
  logged once per instance name for the lifetime of the process, before the request is sent, whatever its outcome.
  No warning appears when Portainer is disabled, has no `BaseUrl`, an action is rejected by the allow-list, or the
  URL is `https`. The warning only appears once something calls the Portainer client, which no component does today
  (see the note on action runs and *Known gaps* below).
- Portainer server certificates are always validated; there is no option to skip validation for Portainer.

### Authentication

- When `ApiToken` is set, it is sent as an API key header on every request and takes precedence over username and
  password.
- Otherwise `Username` and `Password` are posted to Portainer's authentication endpoint and the returned JWT is used as
  a bearer token. The login is repeated for every operation; tokens are not cached.
- A login that answers non-success, or answers success without a token, ends the operation with an authentication
  error that names the Portainer base URL (user info masked as `***`) and the HTTP status code; credentials never appear in it or in the log.

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
  [Registries](registries.md). The login and the action request are `POST`s, which are retried only when they cannot
  have been processed: after a connection error before anything was sent, and on 429. The action request carries an
  empty body so the runtime does not resend it on its own either. A requested action therefore reaches Portainer's
  processing at most once; a 5xx, 408, timeout or broken connection ends it *failed* after one attempt, and the
  operator repeats it deliberately.
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
  check or an action (see *Known gaps*).

## Limits and errors

- Validation, applied only when `Enabled` is `true`, fails startup when: `BaseUrl` is missing; `BaseUrl` is not an
  absolute `http`/`https` URI; `BaseUrl` is `http` without `AllowInsecureHttp: true`; neither `ApiToken` nor both
  `Username` and `Password` are set; `RequestTimeoutSeconds` is outside 1–300.
- A disabled Portainer section is not validated at all; its values may be incomplete.
- Error messages and log entries name the Docker instance, the Portainer base URL (user info masked as `***`), the action, the container name and
  HTTP status codes. They never contain the password, the API token or the JWT.

### Known gaps

- Container actions cannot be triggered from the UI or a job, and no action run is ever recorded. The executable
  actions (`start`, `stop`, `restart`, `kill`, `pause`, `unpause` on containers) do not match the action types of the
  action-run record, and redeploying stacks and updating services are not implemented. Tracked in
  [#280](https://github.com/LarsLaskowski/DockerUpdateGuard/issues/280).
- Without `EndpointId` the first environment Portainer lists is used, which can be an environment other than the one
  that manages the Docker instance; an explicit `EndpointId` is not required when Portainer manages several
  environments. Tracked in [#279](https://github.com/LarsLaskowski/DockerUpdateGuard/issues/279).

## Related decisions

- [0006 Insecure Portainer HTTP warns at the first request per instance](../decisions/0006-insecure-portainer-http-warns-at-first-request.md)
- [0008 Non-idempotent requests are retried only when they cannot have been processed](../decisions/0008-non-idempotent-requests-retry-only-when-not-processed.md)

## Not here

- Container discovery, snapshots and Docker Engine connectivity: [Docker instances](docker-instances.md).
- The retry policy and the shared failure model of outbound HTTP: [Registries](registries.md).
- Startup validation failure and configuration reload: [Hosting](hosting.md).
- Retention of snapshots and other history: [Scan engine](scan-engine.md).
- What the UI shows about Portainer availability: [Web UI](web-ui.md).
