# Spec: Trigger Portainer container actions from the runtime container page and record them as action runs

Source: Issue #280
Status: Draft

## Problem

An operator who sees a container that needs attention (an update, a hung process) on the runtime container detail page
has to leave DockerUpdateGuard and act in Portainer, although the Portainer integration can already authenticate and
execute container actions: nothing calls it, no action run is ever recorded, and the client's actions (`start`, `stop`,
`restart`, `kill`, `pause`, `unpause`) do not match the action types the action-run record stores. Because the app has
no sign-in ([Hosting](../../docs/areas/hosting.md#security-posture)), whoever reaches the UI could act; the feature
must therefore make every action deliberate (confirmed), traceable (recorded) and bounded to the container shown.

## Scenarios

- S1: Given a running container in the latest runtime scan of an instance whose Portainer is enabled and supports
  actions, when the user opens its detail page, then a *Container actions* section offers **Stop** and **Restart**,
  a note `Runtime status as of the last runtime scan (<time>); an action does not update it. Run "Check image updates"
  on Runtime Containers to refresh it.` and the container's action history.
- S2: Given a container whose snapshot status is exited (or created, shown as not set), when the page opens, then the
  section offers **Start** only.
- S3: When the user clicks an action, then a dialog titled `<Action> container?` says `<Action> container '<container>'
  on Docker instance '<instance>' through Portainer environment <environment>.`, with **Cancel** and **<Action>**. Stop
  adds `The container stops and stays stopped until it is started again.`, Restart adds `The container is unavailable
  while it restarts.`; both confirm buttons are styled destructive. **Cancel** closes the dialog and nothing is
  recorded or sent.
- S4: Given the target is DockerUpdateGuard's own container, the dialog also shows `This is DockerUpdateGuard's own
  container: the application <stops|restarts> and this page loses its connection.`
- S5: When the user confirms, then every action button of the container is disabled and the confirmed one shows
  progress; on completion the page shows the outcome message (success or error style) and reloads the history, whose
  newest row is this run.
- S6: The history lists the container's 20 most recent action runs, newest first, with action, status, requested at,
  completed at, requested by and message; with none it shows `No container actions yet.`

## Errors and edge cases

Messages appear on the page after the action and as the run's message in the history. `<action>` is `start`, `stop`
or `restart`.

- E1: Success → *Succeeded*, `Action '<action>' on '<container>' executed successfully`.
- E2: The Engine answers 304 (already started or stopped; the snapshot was stale) → *Succeeded*, `Action '<action>' on
  '<container>' had no effect: the container was already in the requested state`.
- E3: Portainer answers non-success (401/403 included) to the action → *Failed*, `Action '<action>' failed: HTTP
  <status>`; a failing login keeps today's authentication error
  ([Container actions](../../docs/areas/container-actions.md#authentication)).
- E4: The action request itself fails after it may have been sent (HTTP 5xx, connection failed, timed out after
  `RequestTimeoutSeconds`) → *Failed* after one attempt, its message followed by `; the action may have been applied,
  check the container before repeating it`.
- E5: Portainer does not know the snapshot's container ID (container recreated or removed since the scan) → *Failed*,
  `Container changed since the last scan; run a runtime scan and try again`. No container is ever matched by name.
- E6: The container is not in the latest runtime scan of its instance — on page load: no buttons and `This container
  is not in the latest runtime scan of '<instance>'; no actions are available.`; at confirmation (a scan ran in
  between): *Failed*, `Container is not in the latest runtime scan; run a runtime scan and try again`.
- E7: Portainer disabled for the instance in the current configuration → no section. The capability check reports no
  support (several environments without `EndpointId`, login or connection failure) → the section shows its message
  instead of buttons. A confirmation that finds Portainer disabled or unsupported at that moment → *Failed* with the
  capability message, or `Portainer is not enabled for Docker instance '<instance>'`.
- E8: A non-terminal run exists for the container (another tab, another browser) → the buttons are disabled with `An
  action is in progress for this container.` on page load; a confirmation that races it → *Failed*, `Another action
  is in progress for this container`.
- E9: The browser closes or the connection drops while an action runs → the action runs to its end and its run is
  finalized.
- E10: The application stops during an action → *Cancelled*, `Cancelled: the application stopped before the action
  was sent` when nothing was sent; otherwise the run stays non-terminal and at the next startup becomes *Failed*,
  `Interrupted: the application stopped before the action completed` (also the expected end of a Stop or Restart of
  the app's own container).
- E11: The instance is removed from configuration → its runs are deleted with it (unchanged); Portainer disabled later
  → runs stay.
- E12: A paused or restarting container offers Stop and Restart; a dead one offers nothing.

## Non-functional requirements

- N1: One action contract: start, stop and restart of a container are the actions the client, the action-run record
  and the page know; `kill`, `pause` and `unpause` are no longer executable. Stored action types keep their numbers
  (`RestartContainer` = 1, `RedeployStack` = 2, `UpdateService` = 3); new ones are appended.
- N2: Actions start only from interactive page event handlers after confirmation: no route, query string, HTTP
  endpoint or prerender path starts one. The capability check runs on the interactive render only.
- N3: The target is checked on the server at confirmation, not only by the page: Portainer enabled and supporting
  actions, container in the latest runtime scan of the instance, at most one non-terminal run per container (single
  application instance, [Scan engine](../../docs/areas/scan-engine.md#deployment-assumption)), the container addressed
  by the snapshot's container ID.
- N4: The run is written as *Requested* before Portainer is contacted, *Running* when the action request is sent,
  terminal with its completion time; `RequestedBy` is always `UI` (no sign-in; no IP address or header is recorded).
  An action's lifetime is bounded by `RequestTimeoutSeconds` and application shutdown, not by the browser connection.
- N5: The Portainer client answers capability and actions in the shared outcome model with fixed failure reasons
  ([guarantee, decision 0009](../../docs/decisions/0009-integration-results-carry-fixed-failure-reasons.md)); an
  action request is sent at most once ([decision 0008](../../docs/decisions/0008-non-idempotent-requests-retry-only-when-not-processed.md)).
- N6: Credentials never appear in runs, page messages, logs or telemetry. Logs and a trace activity per action carry
  action, container name, instance name and HTTP status only; Portainer environment names reach only the page,
  HTML-encoded.
- N7: Action runs have no retention in this version: they are audit history, removed only with their instance. No
  schema change is needed for the container actions; no new configuration key.
- N8: Own container: the target is the app's own container when its ID starts with the `HOSTNAME` the app runs with
  (Docker's short container ID); a custom host name means no warning, never a wrong one.

## Acceptance criteria

- AC1: The executable actions are start, stop and restart of a container; a request for any other action type is
  rejected before Portainer is contacted; stored action-type numbers are unchanged (N1).
- AC2: The *Container actions* section is shown only for an instance with Portainer enabled, offers the actions of
  the snapshot's status (S1, S2, E12), and shows the capability message, the not-current message or the in-progress
  message instead of buttons (E6, E7, E8); the capability check does not run during prerender (N2).
- AC3: Every action opens the confirmation dialog of S3, with the own-container warning of S4 when N8 applies;
  Cancel sends and records nothing (S3, S4, N8).
- AC4: A confirmed action writes exactly one run, *Requested* with instance, Portainer endpoint, snapshot, action
  type, resource type *Container*, container name, `RequestedBy` `UI` and request time before Portainer is contacted,
  then *Running* and a terminal status with completion time and message (S5, E1, N4).
- AC5: Outcomes and messages are those of E1–E4, after exactly one action request, with no credential in any run,
  message or log entry (E1, E2, E3, E4, N5, N6).
- AC6: The action addresses the snapshot's container ID; a container Portainer does not know by that ID, or one no
  longer in the latest runtime scan at confirmation, ends *Failed* with the messages of E5 and E6 and no action
  request (E5, E6, N3).
- AC7: Two confirmations for one container that overlap yield one executed action and one *Failed* run with the
  message of E8, also when the page did not disable the buttons (E8, N3).
- AC8: A confirmation that finds Portainer disabled or unsupported ends *Failed* without an action request (E7, N3).
- AC9: Closing the browser does not cancel a running action; shutdown before sending records *Cancelled*; startup
  turns every *Requested* or *Running* run into *Failed* with the message of E10 (E9, E10).
- AC10: The page disables all action buttons of the container while one runs, shows the outcome message and reloads
  the history of S6 (S5, S6).
- AC11: Each action emits one trace activity and log entries carrying only action, container name, instance name and
  HTTP status (N6).

## Out of scope

- Redeploying stacks and updating Swarm services (`RedeployStack`, `UpdateService`) and redeploying to a selected
  tag: a follow-up issue linked to #291 (Open question 1).
- `kill`, `pause`, `unpause`; actions from list pages or background jobs; an automatic rescan after an action.
- Recording a user identity (no sign-in) and a retention for action runs (N7).
- Detecting the database container as a self-target (only the app's own container, N8).
- E11: unchanged behavior.

## Open questions

1. Do the update actions (`RedeployStack`, `UpdateService`) ship in this feature? Answered: no (Product Manager,
   option A). Start, Stop and Restart ship now; `RedeployStack` and `UpdateService` stay as stored action types but
   are neither offered nor executable; redeploy and update become a follow-up issue linked to #291.
2. Which actions, and on which status? Answered: issue's recommendation (Start / Stop / Restart; kill removed as
   destructive, pause/unpause not needed for update management); paused and restarting containers count as running,
   dead ones get none (E12).
3. `RequestedBy` and retention? Answered: `UI`, no retention (issue's recommendation, N4, N7).
4. Reconcile stale runs at startup or on read? Answered: at startup, a local database step like the instance
   synchronization ([Hosting](../../docs/areas/hosting.md#startup-sequence)).
5. Do the issue's claims about today's code hold? Answered: yes, with corrected locations: `IPortainerClient` is registered (`ServiceCollectionExtensions.cs:110`, not
   104) and never injected; 304 fails today (`PortainerClient.cs:514`, success only on 2xx); the target is looked up by
   name; the detail page shows the latest snapshot of the container ID even when it is not current
   (`ApplicationViewService.cs:1943`), hence E6; container names are cut to 200 characters when scanned
   (`DockerInstanceClient.cs:34`), so the 200-character resource name always fits (issue edge case 13). The *Known
   gaps* entry to remove is one bullet in `container-actions.md`, not "Open questions items 1 and 2".

## Area documents

- `docs/areas/container-actions.md`: *Actions* — the three actions by container ID, 304 as success, the messages;
  *Action runs* — when a run is written, status flow, `RequestedBy`, the server-side checks, startup reconciliation,
  no retention; *Known gaps* entry removed.
- `docs/areas/web-ui.md`: *Pages and routes* and *Actions* — the *Container actions* section, dialog, history; the
  sentence "The UI offers no container actions" narrowed to redeploy and update; the error-prefix table if the action
  joins it.
- `docs/areas/hosting.md`: *Startup sequence* gains the reconciliation step; *Security posture* states that enabling
  Portainer lets anyone who reaches the UI start, stop and restart that instance's containers.
- `docs/areas/scan-engine.md`: action runs are not cleaned up and keep their snapshots.
- `docs/areas/telemetry.md`: the action's trace activity and its attributes.

## Challenge notes

Devil's Advocate, mode `spec`: feature #280, `/home/user/DockerUpdateGuard/specs/feature-280-portainer-container-actions/spec.md`

I left Option A (Start/Stop/Restart only) alone. The spec's corrected code claims hold: the client succeeds only on 2xx (`PortainerClient.cs:514`), looks the container up by name (`PortainerClient.cs:215-240, 495`), and cuts names to 200 (`DockerInstanceClient.cs:34, 521`). My objections are below.

## Major

**M1. Stop and Restart will report *Failed* for actions that actually succeed (N4 and E4 cannot be met as written).**
- Evidence: every Portainer request runs under `client.Timeout = RequestTimeoutSeconds` (`PortainerClient.cs:312`, default 15, `container-actions.md` *Actions*). The action is sent with no `t` parameter (`PortainerClient.cs:508`).
- Docker's `POST /containers/{id}/stop` and `/restart` only answer once the container has stopped. That takes up to the container's stop timeout: 10 s by default, longer with compose `stop_grace_period`. Restart then adds the start time on top.
- So a container that ignores SIGTERM, or has a grace period of about 15 s or more, makes Stop or Restart hit the timeout. Under E4 that becomes *Failed* "…timed out after 15 seconds; the action may have been applied", even though Docker completes the action. The operator is told to check, and may well repeat a destructive action that already worked.
- N4 also says an action's lifetime is bounded by `RequestTimeoutSeconds`. In fact login, environment list, the ID lookup (needed for AC6) and the action each get their own timeout.
- Instruction: the spec must say how long a Stop or Restart request may take relative to the container's stop timeout. Options:
  - give the action request its own bound that is at least the stop timeout plus a margin, or
  - pass a stated `t`, or
  - state as a documented constraint that `RequestTimeoutSeconds` must exceed the longest stop grace period. N7's "no new configuration key" may then need revisiting.

  Add a criterion: a Stop that Docker answers after more than the default timeout but within the stop timeout ends *Succeeded*. Restate N4's lifetime as the sum of the steps.

## Minor

**m1. `<environment>` in the S3 dialog is undefined.**
When `EndpointId` is configured, the client never fetches the environment's name (`PortainerClient.cs:257-260`). The capability message only carries the ID (`"Portainer connected, endpoint {endpointId}"`, `PortainerClient.cs:437`). Instruction: state that `<environment>` is the endpoint ID, or the ID plus the name when Portainer reports one, and where the name comes from. N6's "environment names reach only the page" then covers that name.

**m2. E5 will be shown for an unknown environment, not just an unknown container.**
With `EndpointId` configured, the capability check reports "supported" without checking that the environment exists (`PortainerClient.cs:257-260, 433-438`). A wrong or deleted `EndpointId` makes Portainer answer 404 on `/api/endpoints/{id}/docker/...`, and E5 tells the operator to rescan, which never helps. Instruction: E5 applies only when the Docker Engine reports no such container for that ID. A 404 for an unknown environment ends *Failed* with a message naming `Portainer:EndpointId`.

**m3. N8 can show a wrong own-container warning.**
"ID starts with `HOSTNAME`" matches every container when `HOSTNAME` is empty or unset, for example outside Docker. It also matches some containers when a custom host name happens to be short hex, such as `cafe` or `a1`. That contradicts "never a wrong one". Instruction: treat the target as the app's own container only when `HOSTNAME` is exactly 12 lowercase hex characters (Docker's default) and the container ID starts with it.

**m4. No behavior is defined for a failing database or configuration read.**
Cases the spec leaves open:
- The *Requested* run cannot be written.
- The terminal status cannot be written after the action was sent. The run stays non-terminal, and E8 then blocks the container until a restart.
- A reloaded configuration fails validation at confirmation. Reads then fail where they are made (`hosting.md` *Configuration reload*, lines 122-127).

Instruction: add edge cases.
- No run written → no action sent, and an error message on the page.
- Terminal write fails → the outcome is still shown and logged, and the run is reconciled at startup.
- Invalid configuration → no action and a page error. The service never throws for these.

**m5. A disabled Docker instance (`Enabled: false`) with Portainer enabled is not covered.**
The client only checks `Portainer.Enabled` (`PortainerClient.cs:379, 461`). A disabled instance keeps its last scan as its current set (`docker-instances.md`, lines 121-123 and 31-33). So the spec as written would offer actions on a host the app has been told not to contact. Instruction: state whether the section is shown for a disabled instance. Recommended: not shown, and a confirmation for one ends *Failed*.

**m6. The documentation scope is narrower than the issue asks.**
The issue's criteria require README (Portainer section: what enabling it allows, plus the no-auth consequence) and `docs/ARCHITECTURE.md` (*Integration clients*, *UI*, *Security posture*). Two passages will be stale:
- `README.md:204`, which talks of "Portainer-backed actions"
- `docs/ARCHITECTURE.md:235`, which says "endpoint and container listing"

Instruction: add `README.md` and `docs/ARCHITECTURE.md` §6, §9 and §11 to *Area documents*.

**m7. AC4 requires a "Portainer endpoint" that may not be stored yet.**
The stored endpoint is only synchronized at startup, on discovery ticks and at the start of scans (`docker-instances.md`, lines 22-24). If Portainer is enabled by a configuration reload, an action before the next sync finds no endpoint record, or one still marked disabled. The run's endpoint link is optional (`container-actions.md` *Action runs*). Instruction: AC4 records the instance's stored Portainer endpoint when one exists, and the decision to act follows the current configuration, not the stored flag.
