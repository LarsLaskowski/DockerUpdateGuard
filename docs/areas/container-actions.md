# Container actions

## Scope

The optional Portainer integration of a Docker instance: how a Portainer server is configured and mapped to a Docker
instance, how DockerUpdateGuard authenticates against it, which container actions it executes through it (start, stop
and restart), when the runtime container page offers them, and the action runs it records. Operators rely on it to
configure Portainer access without weakening transport security and to know what enabling it allows: **anyone who
can reach the UI can start, stop and restart the containers of that Docker instance** ([Hosting](hosting.md), *Security
posture*).

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
- `EndpointId` selects the Portainer environment that manages the Docker instance. A configured value must consist
  of ASCII digits only to be used (it becomes part of a request path); any other value counts as an unknown
  environment (*Capability check*). When it is empty, the environment
  is resolved again on every call from Portainer's environment list and used only when it is unambiguous: Portainer
  lists exactly one environment, counting the `X-Total-Count` response header when it is present (the larger of the
  listed number and the header). A header that is present but not a single non-negative integer makes the count
  unknown and the environment unusable; an absent header is ignored.
  - When more than one environment is counted, or the count is unknown and at least one environment is listed, nothing is used and the capability check and every action fail with one message: `Portainer manages
    <n> environments; set Portainer:EndpointId of Docker instance '<instance name>' to one of: <id> (<name>), …`, or
    `Portainer reported no valid environment count; …` when the count is unknown; when none is listed and none is counted, they fail with `No Portainer endpoint found`. The key is named relative to the
    instance because configuration keys address instances by list position. The message lists at most 10
    environments by ascending ID, followed by `, …` when more exist or the count is unknown; with none listed it ends
    after the instance name.
  - Names are trimmed, every control, format, line-separator and paragraph-separator character becomes `?`, a name
    longer than 64 UTF-16 units is cut (never inside a surrogate pair) and followed by `…`, and an empty name lists
    the ID alone. Environment URLs are never listed.
  - Portainer lists only the environments the authenticating user may see, of every type (Docker, Kubernetes, Azure).
    A single listed environment is trusted and not checked against the Docker instance, so `EndpointId` is the only
    way to bind an instance to a specific host.
  - Reasoning: [0010](../decisions/0010-portainer-environment-is-used-only-when-unambiguous.md).

### Transport security

- `BaseUrl` must be an absolute `https` URL. A plaintext `http` URL is accepted only with `AllowInsecureHttp: true`;
  without it, startup fails with a message that names the key to set ([Hosting](hosting.md)).
- `AllowInsecureHttp` is opt-in only. While a Docker instance's `BaseUrl` is `http`, the first request to Portainer
  for that instance logs a warning (EventId 3311) that names the instance only, never the URL or a credential. It is
  logged once per instance name for the lifetime of the process, before the request is sent, whatever its outcome.
  No warning appears when Portainer is disabled, has no `BaseUrl`, an action is rejected before Portainer is contacted
  (an unsupported action type, a malformed container ID), or the URL is `https`. The warning appears with the first
  Portainer request of the instance: the capability check the runtime container page runs, or an action.
- Portainer server certificates are always validated; there is no option to skip validation for Portainer.

### Authentication

- When `ApiToken` is set, it is sent as an API key header on every request and takes precedence over username and
  password.
- Otherwise `Username` and `Password` are posted to Portainer's authentication endpoint and the returned JWT is used as
  a bearer token. The login is repeated for every operation; tokens are not cached. After a login answered 429 with a
  positive `Retry-After`, no instance whose login goes to the same URL with the same user name sends a login before the
  window ends: the capability check or action sends no request and fails with the authentication error `Portainer
  authentication for '<masked BaseUrl>' not sent: Portainer answered HTTP 429 and asked to wait until <until>` (UTC
  `yyyy-MM-ddTHH:mm:ssZ`), as `Portainer connection error: <that text>` for the capability check and `Action
  '<action>' failed: <that text>` for an action
  ([Integration failures](integration-failures.md#logins-inside-a-retry-after-window)). An instance with an `ApiToken`
  never logs in and is unaffected.
- A login that answers non-success, or answers success without a token, ends the operation with an authentication
  error that names the Portainer base URL (user info masked as `***`) and the HTTP status code; credentials never appear in it or in the log.

### Capability check

- For a Docker instance, a capability check reports whether Portainer is configured, whether actions are supported, the
  environment it used (`EndpointId` of the answer) and a message. The runtime container page runs it for a container
  it shows actions for ([Container availability](#container-availability)), and an action repeats it before it looks
  the container up:
  - Portainer disabled or without `BaseUrl` → not configured, no actions;
  - Portainer's status endpoint answers non-success → configured, no actions, message with the HTTP status code;
  - no environment configured and none found → configured, no actions, "No Portainer endpoint found";
  - no environment configured and several listed (or an unusable count) → configured, no actions, the message of
    *Configuration and mapping*;
  - no environment configured and the environment list answers non-success → configured, no actions,
    `Portainer environment list failed: HTTP <status>`;
  - otherwise the environment, configured or resolved from the list, is verified with `GET /api/endpoints/<id>`:
    - an `EndpointId` that is not all ASCII digits, or an environment Portainer answers 404 for → configured, no
      actions, `Portainer environment <id> not found; check Portainer:EndpointId of Docker instance '<instance
      name>'` (a non-digit value sends no environment request);
    - any other non-success answer → configured, no actions, `Portainer environment <id> check failed: HTTP <status>`;
    - success → configured, actions supported, message naming the environment used;
  - any error → configured, no actions, message `Portainer connection error: ` with the reason (see *Limits and errors*).

### Container availability

The runtime container page offers actions from the container's latest stored snapshot and the current configuration
(not the stored endpoint flag); the service answers it as follows ([Web UI](web-ui.md) for the layout):

- Nothing is shown when the Docker instance or the container ID's snapshot is unknown, the instance is missing from
  the configuration or disabled, or Portainer is disabled for it.
- Otherwise a container that is not in the latest runtime scan of its instance shows `This container is not in the
  latest runtime scan of '<instance name>'; no actions are available.` and no action, and Portainer is not contacted.
  A container is in that scan when its latest snapshot belongs to the instance's latest runtime scan run that recorded
  any snapshot.
- Otherwise the capability check runs. A result that is not *succeeded* shows its message and no action. A *succeeded*
  one offers the actions of the snapshot's runtime status:

  | Runtime status | Offered actions |
  | --- | --- |
  | running, paused, restarting | stop, restart |
  | exited, not set (a created container) | start |
  | dead | none |

- While an action of this process executes for the container, no action is offered and the message is `An action is in
  progress for this container.` ([Action runs](#action-runs)).
- The container is the application's own container when `HOSTNAME` is exactly 12 lowercase hexadecimal characters
  (Docker's default host name) and the container ID starts with it; with any other host name, including an empty or
  unset one, it is not, and the confirmation shows no own-container warning. The database container is never detected.

### Actions

- The executable actions are `start`, `stop` and `restart` of a container, requested as the action types *start
  container*, *stop container* and *restart container*. Any other type (*redeploy stack*, *update service*, not set)
  is rejected before Portainer is contacted, with `Action '<type>' is not supported; allowed: start, stop, restart`;
  `kill`, `pause` and `unpause` are not executable. Reasoning:
  [0034](../decisions/0034-container-actions-are-start-stop-and-restart-of-a-container.md).
- The target container is identified by the container ID of its snapshot, never by name. An ID that is not exactly 64
  lowercase hexadecimal characters is answered *not found* with the message below, without any request.
- An action runs these steps; each failure ends it:
  1. login and the environment resolution and verification of the *Capability check*, with the same outcomes
     (*not configured*, *unsupported*, *failed*; an error ends it *failed* with `Action '<action>' failed: <reason>`);
  2. the lookup `GET /api/endpoints/<id>/docker/containers/json?all=true&filters={"id":["<container ID>"]}`: a
     non-success answer ends it *failed*, `Action '<action>' failed: HTTP <status>`; when no listed container has
     exactly that ID it ends *not found*, `Container changed since the last scan; run a runtime scan and try again`
     (recreated or removed since the scan);
  3. `POST /api/endpoints/<id>/docker/containers/<container ID>/<action>` with an empty body and no Docker `t`
     parameter, so a container keeps its own stop grace period.
- The answer to the request of step 3:

  | Answer | Outcome and message |
  | --- | --- |
  | 2xx | *succeeded*, `Action '<action>' on '<container name>' executed successfully` |
  | 304 (already started or stopped) | *succeeded*, `Action '<action>' on '<container name>' had no effect: the container was already in the requested state` |
  | 5xx | *unknown*, `Action '<action>' failed: HTTP <status>` and the suffix below |
  | any other non-success, 401 and 403 included | *failed*, `Action '<action>' failed: HTTP <status>` |
  | an error of this request (connection failed, timed out, cancelled) | *unknown*, `Action '<action>' failed: <reason>` and the suffix below |

  The suffix is `; the action may have been applied, check the container before repeating it`: the request may have
  reached Docker.
- Actions never throw to the caller; every outcome is a result. Executed, failed and not-found actions are logged with
  the action, the container name and the Docker instance name.
- Portainer requests go through the shared retry of transient HTTP failures
  ([Integration failures](integration-failures.md#retry-of-transient-http-failures)). The login and the action request
  are not idempotent and are retried only when they cannot have been processed, so a requested action reaches
  Portainer's processing at most once; every other transient failure ends it after one attempt, and the operator
  repeats it deliberately.
- `RequestTimeoutSeconds` (1-300, default 15) bounds each Portainer request on its own, retries included: the login,
  the status request, the environment list, the environment check, the container lookup and the start request. The
  stop and restart request is bounded by 300 seconds plus `RequestTimeoutSeconds`, because Docker answers it only once
  the container has stopped; one that takes longer ends *unknown* with `timed out after <n> seconds` (`<n>` the bound
  that fired). An action can therefore last as long as the sum of its steps' bounds. Application shutdown ends it
  earlier (*Action runs*). Reasoning:
  [0037](../decisions/0037-stop-and-restart-requests-wait-up-to-300-seconds-beyond-the-request-timeout.md).

### Action runs

- The data model holds an action-run record per requested action: the Docker instance, optionally the Portainer
  endpoint and the container snapshot, the action type (*restart container*, *redeploy stack*, *update service*,
  *start container*, *stop container*; stored numbers `1`, `2`, `3`, `4`, `5`, never changed), the resource type
  (*container*, *service*, *stack*), the resource name (up to 200 characters), status (*requested*, *running*,
  *succeeded*, *failed*, *cancelled*), requester, Portainer task ID, error message, and request and completion times.
  *Redeploy stack* and *update service* are stored types only: nothing offers or executes them
  ([#474](https://github.com/LarsLaskowski/DockerUpdateGuard/issues/474)).
- Only the runtime container page writes runs, one per confirmed action. A run holds the Docker instance, its stored
  Portainer endpoint when one exists, the container's snapshot (the latest of the container ID), the action type,
  resource type *container*, the container name as resource name, the request time and the requester `UI`: the
  application has no sign-in, and no IP address or header is recorded.
- **Status flow.** *Requested* is written before Portainer is contacted, *running* immediately afterwards, just before
  the Portainer client is called, then one terminal status with the completion time:
  - *succeeded* for a *succeeded* outcome;
  - when application shutdown was requested and the outcome is *unknown*, the request may have reached Docker: the run
    stays *running* and the next startup reconciles it;
  - *cancelled* with `Cancelled: the application stopped before the action was sent` when shutdown was requested and
    the outcome is any other that is not *succeeded*;
  - otherwise *failed* with the outcome's message.
  The message of every terminal run, a success included, is stored in its `ErrorMessage` column, and the terminal
  status is written without the caller's or shutdown's cancellation. A terminal write that fails is logged
  (EventId 3323) and the outcome is still returned; the run stays open until startup reconciles it.
- **Checks at confirmation.** The service checks the target again on the server against the current configuration,
  whatever the page showed. A confirmation for an instance or container ID with no snapshot fails with `Container is
  not in the latest runtime scan; run a runtime scan and try again` and records no run. Otherwise each check below
  ends a rejected confirmation with one *failed* run (completed at its request time, logged as EventId 3322 with the
  check's name) and no Portainer request, in this order:

  | Check | Run message |
  | --- | --- |
  | the instance is missing from the configuration or disabled | `Docker instance '<instance name>' is disabled` |
  | Portainer is disabled for the instance | `Portainer is not enabled for Docker instance '<instance name>'` |
  | the container is not in the latest runtime scan of its instance | `Container is not in the latest runtime scan; run a runtime scan and try again` |
  | an action of this process executes for the container | `Another action is in progress for this container` |

  The client's own checks follow (an environment that is not usable, an unknown container ID, *Actions*) and end the
  run *failed* with their messages. An invalid configuration at confirmation, or a *requested* or *running* run that
  cannot be written, sends no action and writes no further run; the caller gets an error, which the page shows as
  `Container action failed: unexpected error` ([Web UI](web-ui.md#limits-and-errors)).
- **One action per container.** The containers an action of this process executes for are held in a process-wide set
  by Docker instance and container ID; a confirmation adds its container atomically before it writes the run and the
  entry is removed whatever the outcome, so a run whose terminal write failed does not block the container. Overlapping
  confirmations execute one action and record one *failed* run. The set holds for one application process
  ([Scan engine](scan-engine.md#deployment-assumption)). Reasoning:
  [0036](../decisions/0036-one-container-action-at-a-time-per-container-guarded-in-the-process.md).
- **Lifetime.** The action runs to its end when the browser closes or the connection drops: it uses the application's
  shutdown signal, not the page's. Shutdown before the request was sent records *cancelled* as above.
- **Startup reconciliation.** After the configured instances are synchronized ([Hosting](hosting.md#startup-sequence))
  every run still *requested* or *running* becomes *failed* with `Interrupted: the application stopped before the
  action completed` and the current time; EventId 3324 logs the count when any changed. A stop or restart of the
  application's own container ends this way, although Docker may have completed it.
- **Retention.** Action runs are audit history: they are never cleaned up and are removed only with their Docker
  instance. A container snapshot an action run references is never removed by retention
  ([Scan engine](scan-engine.md)); deleting the Portainer endpoint or the snapshot keeps the action run and clears the
  link. Disabling Portainer keeps the runs.
- The service logs a request (EventId 3320) and a finished action (3321) with the action, container name, Docker
  instance name and status, and emits one trace activity ([Telemetry](telemetry.md#traces)); neither carries a message
  or a credential. Reasoning for the run history:
  [0035](../decisions/0035-action-runs-are-an-audit-history-written-before-portainer-is-contacted.md).

## Limits and errors

- Validation, applied only when `Enabled` is `true`, fails startup when: `BaseUrl` is missing; `BaseUrl` is not an
  absolute `http`/`https` URI; `BaseUrl` has an `@` outside its user info ([Hosting](hosting.md), *Validation*); `BaseUrl` is `http` without
  `AllowInsecureHttp: true`; neither `ApiToken` nor both
  `Username` and `Password` are set; `RequestTimeoutSeconds` is outside 1–300.
- A disabled Portainer section is not validated at all; its values may be incomplete.
- An error during a capability check or an action is reported with one fixed reason, never with the error's own text:
  the authentication error as described in *Authentication*; `cancelled` when the caller cancelled; `timed out after
  <n> seconds` (the bound of the request that ended it, *Actions*); `connection failed` for any transport failure; `invalid response` for an
  answer that is not the expected JSON; `unexpected error` otherwise. The error itself is logged with the failure
  (EventIds 3306, 3310). These are the shared reasons of
  [Integration failures](integration-failures.md#fixed-failure-reasons).
- The capability and action `Message` can carry Portainer environment names (external data). They are never written to
  logs or telemetry and are rendered HTML-encoded by the UI ([Web UI](web-ui.md)); the message of an action whose
  environment resolution failed with several environments is also stored in the run, so a run can hold them.
- Two warnings cover the environment resolution, neither with an environment name or URL: EventId 3312 (several or
  unusable environments, no `EndpointId`) carries the instance name, the listed count and the valid reported total;
  EventId 3313 (environment list answered non-success) carries the instance name and the status code.
- Error messages and log entries name the Docker instance, the Portainer base URL (user info masked as `***`), the action, the container name and
  HTTP status codes. They never contain the password, the API token or the JWT.

### Known gaps

- Redeploying Portainer stacks and updating Swarm services are not implemented. Tracked in
  [#474](https://github.com/LarsLaskowski/DockerUpdateGuard/issues/474).

## Related decisions

- [0006 Insecure Portainer HTTP warns at the first request per instance](../decisions/0006-insecure-portainer-http-warns-at-first-request.md)
- [0008 Non-idempotent requests are retried only when they cannot have been processed](../decisions/0008-non-idempotent-requests-retry-only-when-not-processed.md)
- [0009 Integration results carry fixed failure reasons](../decisions/0009-integration-results-carry-fixed-failure-reasons.md)
- [0010 A Portainer environment is used only when unambiguous](../decisions/0010-portainer-environment-is-used-only-when-unambiguous.md)
- [0034 Container actions are start, stop and restart of a container](../decisions/0034-container-actions-are-start-stop-and-restart-of-a-container.md)
- [0035 Action runs are an audit history written before Portainer is contacted](../decisions/0035-action-runs-are-an-audit-history-written-before-portainer-is-contacted.md)
- [0036 One container action at a time per container, guarded in the process](../decisions/0036-one-container-action-at-a-time-per-container-guarded-in-the-process.md)
- [0037 Stop and restart requests wait up to 300 seconds beyond the request timeout](../decisions/0037-stop-and-restart-requests-wait-up-to-300-seconds-beyond-the-request-timeout.md)

## Not here

- Container discovery, snapshots and Docker Engine connectivity: [Docker instances](docker-instances.md).
- The retry policy and the shared failure model of outbound HTTP: [Integration failures](integration-failures.md).
- Startup validation failure and configuration reload: [Hosting](hosting.md).
- Retention of snapshots and other history: [Scan engine](scan-engine.md).
- The page layout, confirmation dialog and history of container actions: [Web UI](web-ui.md).
