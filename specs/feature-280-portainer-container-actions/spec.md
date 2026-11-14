# Spec: Trigger Portainer container actions from the runtime container page and record them as action runs

Source: Issue #280
Status: Approved by Lead

## Problem

An operator who sees a container that needs attention (an update, a hung process) on the runtime container detail page
has to leave DockerUpdateGuard and act in Portainer, although the Portainer integration can already authenticate and
execute container actions: nothing calls it, no action run is ever recorded, and the client's actions (`start`, `stop`,
`restart`, `kill`, `pause`, `unpause`) do not match the action types the action-run record stores. Because the app has
no sign-in ([Hosting](../../docs/areas/hosting.md#security-posture)), whoever reaches the UI could act; the feature
must therefore make every action deliberate (confirmed), traceable (recorded) and bounded to the container shown.

## Scenarios

- S1: Given a running container in the latest runtime scan of an enabled instance whose Portainer is enabled and
  supports actions, when the user opens its detail page, then a *Container actions* section offers **Stop** and
  **Restart**, a note `Runtime status as of the last runtime scan (<time>); an action does not update it. Run "Check
  image updates" on Runtime Containers to refresh it.` and the container's action history.
- S2: Given a container whose snapshot status is exited (or created, shown as not set), when the page opens, then the
  section offers **Start** only.
- S3: When the user clicks an action, then a dialog titled `<Action> container?` says `<Action> container '<container>'
  on Docker instance '<instance>' through Portainer environment <id>.`, `<id>` being the environment ID the capability
  check used, with **Cancel** and **<Action>**. Stop adds `The container stops and stays stopped until it is started
  again.`, Restart adds `The container is unavailable while it restarts.`; both confirm buttons are styled
  destructive. **Cancel** closes the dialog and nothing is recorded or sent.
- S4: Given the target is DockerUpdateGuard's own container (N8), the dialog also shows `This is DockerUpdateGuard's
  own container: the application <stops|restarts> and this page loses its connection.`
- S5: When the user confirms, then every action button of the container is disabled and the confirmed one shows
  progress; on completion the page shows the outcome message (success or error style) and reloads the history, whose
  newest row is this run.
- S6: The history lists the container's 20 most recent action runs, newest first (action, status, requested at,
  completed at, requested by, message); with none, `No container actions yet.`

## Errors and edge cases

Messages appear on the page after the action and as the run's message in the history. `<action>` is `start`, `stop`
or `restart`.

- E1: Success → *Succeeded*, `Action '<action>' on '<container>' executed successfully`.
- E2: The Engine answers 304 (already started or stopped; the snapshot was stale) → *Succeeded*, `Action '<action>' on
  '<container>' had no effect: the container was already in the requested state`.
- E3: Portainer answers non-success (401/403 included) to the action → *Failed*, `Action '<action>' failed: HTTP
  <status>`; a failing login keeps today's authentication error
  ([Container actions](../../docs/areas/container-actions.md#authentication)).
- E4: The action request itself fails after it may have been sent (HTTP 5xx, connection failed, timed out after its
  bound, N4) → *Failed* after one attempt, its message followed by `; the action may have been applied, check the
  container before repeating it`.
- E5: The Docker Engine reports no container with the snapshot's container ID (recreated or removed since the scan)
  → *Failed*, `Container changed since the last scan; run a runtime scan and try again`. No container is ever matched
  by name.
- E6: The container is not in the latest runtime scan of its instance — on page load: no buttons and `This container
  is not in the latest runtime scan of '<instance>'; no actions are available.`; at confirmation (a scan ran in
  between): *Failed*, `Container is not in the latest runtime scan; run a runtime scan and try again`.
- E7: Portainer disabled for the instance in the current configuration → no section. The capability check reports no
  support (several environments without `EndpointId`, login or connection failure) → the section shows its message
  instead of buttons. A configured `EndpointId` that Portainer does not know → no support, `Portainer environment <id>
  not found; check Portainer:EndpointId of Docker instance '<instance>'`. A confirmation that finds Portainer
  disabled or unsupported at that moment → *Failed* with the capability message, or `Portainer is not enabled for
  Docker instance '<instance>'`.
- E8: This application process is executing an action for the container (another tab, another browser) → the
  buttons are disabled with `An action is in progress for this container.` on page load; a confirmation that races
  it → *Failed*, `Another action is in progress for this container`.
- E9: The browser closes or the connection drops while an action runs → the action runs to its end and its run is
  finalized.
- E10: The application stops during an action → *Cancelled*, `Cancelled: the application stopped before the action
  was sent` when nothing was sent; otherwise the run stays non-terminal and at the next startup becomes *Failed*,
  `Interrupted: the application stopped before the action completed` (also the expected end of a Stop or Restart of
  the app's own container).
- E12: A paused or restarting container offers Stop and Restart; a dead one offers nothing.
- E13: A container with a long stop timeout (`stop_grace_period`) → Stop and Restart wait for it within the bound of
  N4 and end *Succeeded*; a stop that takes longer than 300 seconds ends as E4.
- E14: The Docker instance is disabled (`Enabled: false`) → no section; a confirmation → *Failed*, `Docker instance
  '<instance>' is disabled`.
- E15: The configuration fails validation at confirmation, or the *Requested* run cannot be written → no action is
  sent, no run beyond what was written, the page shows `Container action failed: unexpected error` (logged as a user
  action error, [Web UI](../../docs/areas/web-ui.md#limits-and-errors)). The terminal status cannot be written → the
  outcome is still shown and logged; startup reconciles the run (E10); the container is not blocked (E8).

## Non-functional requirements

- N1: One action contract: start, stop and restart of a container are the actions the client, the action-run record
  and the page know; `kill`, `pause` and `unpause` are no longer executable, `RedeployStack` and `UpdateService` are
  stored types only. Stored action types keep their numbers (`RestartContainer` = 1, `RedeployStack` = 2,
  `UpdateService` = 3); new ones are appended.
- N2: Actions start only from interactive page event handlers after confirmation: no route, query string, HTTP
  endpoint or prerender path starts one. The capability check runs on the interactive render only.
- N3: The target is checked on the server at confirmation, not only by the page, against the current configuration
  (not the stored endpoint flag): instance enabled, Portainer enabled and supporting actions, container in the latest
  runtime scan of the instance, no action of this process executing for the container (single application instance,
  [Scan engine](../../docs/areas/scan-engine.md#deployment-assumption)), the container addressed by the snapshot's
  container ID.
- N4: The run is written as *Requested* before Portainer is contacted, *Running* once Portainer is contacted,
  terminal with its completion time; `RequestedBy` is always `UI` (no sign-in; no IP address or header is recorded).
  An action's duration is the sum of its steps: login, environment resolution and the container lookup, each bounded
  by `RequestTimeoutSeconds`, then the action request, bounded by `RequestTimeoutSeconds` for Start and by 300
  seconds plus `RequestTimeoutSeconds` for Stop and Restart. Docker's own stop timeout is never overridden. Application shutdown
  ends it earlier (E10); the browser connection does not.
- N5: The Portainer client answers capability and actions in the shared outcome model with fixed failure reasons
  ([guarantee, decision 0009](../../docs/decisions/0009-integration-results-carry-fixed-failure-reasons.md)); an
  action request is sent at most once ([decision 0008](../../docs/decisions/0008-non-idempotent-requests-retry-only-when-not-processed.md)).
- N6: Credentials never appear in runs, page messages, logs or telemetry. Logs and a trace activity per action carry
  action, container name, instance name and HTTP status only; Portainer environment names reach only the page,
  HTML-encoded, and a run's stored message.
- N7: Action runs have no retention in this version: they are audit history, removed only with their instance. No
  schema change is needed for the container actions; no new configuration key.
- N8: Own container: the target is the app's own container only when `HOSTNAME` is exactly 12 lowercase hexadecimal
  characters (Docker's default) and the container ID starts with it; otherwise no warning is shown.

## Acceptance criteria

- AC1: The executable actions are start, stop and restart of a container; a request for any other action type is
  rejected before Portainer is contacted; stored action-type numbers are unchanged (N1).
- AC2: The *Container actions* section is shown only for an enabled instance with Portainer enabled, offers the
  actions of the snapshot's status (S1, S2, E12), and shows the capability message, the not-current message or the
  in-progress message instead of buttons (E6, E7, E8, E14); the capability check does not run during prerender (N2).
- AC3: Every action opens the confirmation dialog of S3, with the own-container warning of S4 when N8 applies;
  Cancel sends and records nothing (S3, S4, N8).
- AC4: A confirmed action writes exactly one run, *Requested* with instance, the instance's stored Portainer endpoint
  when one exists, snapshot, action type, resource type *Container*, container name, `RequestedBy` `UI` and request
  time before Portainer is contacted, then *Running* and a terminal status with completion time and message (S5, E1,
  N4).
- AC5: Outcomes and messages are those of E1–E4, after exactly one action request, with no credential in any run,
  message or log entry (E1, E2, E3, E4, N5, N6).
- AC6: The action addresses the snapshot's container ID; a container the Engine does not know by that ID, or one no
  longer in the latest runtime scan at confirmation, ends *Failed* with the messages of E5 and E6 and no action
  request (E5, E6, N3).
- AC7: Two confirmations for one container that overlap yield one executed action and one *Failed* run with the
  message of E8, also when the page did not disable the buttons (E8, N3).
- AC8: A confirmation that finds the instance disabled, or Portainer disabled, unsupported or its configured
  environment unknown, ends *Failed* without an action request (E7, E14, N3).
- AC9: Closing the browser does not cancel a running action; shutdown before sending records *Cancelled*; startup
  turns every *Requested* or *Running* run into *Failed* with the message of E10 (E9, E10).
- AC10: The page disables all action buttons of the container while one runs, shows the outcome message and reloads
  the history of S6 (S5, S6).
- AC11: Each action emits one trace activity and log entries carrying only action, container name, instance name and
  HTTP status (N6).
- AC12: A Stop or Restart that Docker answers after more than `RequestTimeoutSeconds` but within 300 seconds plus
  `RequestTimeoutSeconds` ends *Succeeded*; one that exceeds that bound ends as E4 (E13, N4).
- AC13: An invalid configuration or a failed write of the *Requested* run sends no action and shows the page error of
  E15; a failed terminal write still shows the outcome and leaves the container actionable (E15).

## Out of scope

- Redeploying stacks and updating Swarm services (`RedeployStack`, `UpdateService`) and redeploying to a selected
  tag: a follow-up issue linked to #291 (Open question 1).
- `kill`, `pause`, `unpause`; actions from list pages or background jobs; an automatic rescan after an action.
- Recording a user identity (no sign-in) and a retention for action runs (N7).
- Detecting the database container as a self-target (only the app's own container, N8).
- Removing an instance deletes its runs; disabling Portainer keeps them (unchanged).

## Open questions

1. Do the update actions (`RedeployStack`, `UpdateService`) ship in this feature? Answered: no (Product Manager,
   option A). Start, Stop and Restart ship now; `RedeployStack` and `UpdateService` stay as stored action types but
   are neither offered nor executable; redeploy and update become a follow-up issue linked to #291.
2. Which actions, on which status? Answered: the issue's recommendation (N1); status rules in E12.
3. `RequestedBy` and retention? Answered: `UI`, no retention (issue's recommendation, N4, N7).
4. Reconcile stale runs at startup or on read? Answered: at startup, a local database step
   ([Hosting](../../docs/areas/hosting.md#startup-sequence)).
5. Do the issue's claims about today's code hold? Answered: yes, at other lines (registration
   `ServiceCollectionExtensions.cs:110`; 304 fails, `PortainerClient.cs:514`); the detail page shows the latest snapshot
   even when not current (`ApplicationViewService.cs:1943`), hence E6; names are cut to 200 characters when scanned
   (`DockerInstanceClient.cs:34`), so issue edge case 13 cannot occur.

## Area documents

- `docs/areas/container-actions.md`: *Capability check* — the configured environment verified; *Actions* — the three
  actions by container ID, 304 as success, the action request's bound, the messages; *Action runs* — when a run is
  written, status flow, `RequestedBy`, the server-side checks, startup reconciliation, no retention; *Known gaps*
  entry removed.
- `docs/areas/web-ui.md`: *Pages and routes* and *Actions* — the *Container actions* section, dialog, history; the
  sentence "The UI offers no container actions" narrowed to redeploy and update; the error-prefix table gains
  `Container action failed`.
- `docs/areas/hosting.md`: *Startup sequence* gains the reconciliation step; *Security posture* states that enabling
  Portainer lets anyone who reaches the UI start, stop and restart that instance's containers.
- `docs/areas/scan-engine.md`: action runs are not cleaned up and keep their snapshots.
- `docs/areas/telemetry.md`: the action's trace activity.
- `README.md`, Portainer section: what enabling it allows and the no-sign-in consequence.
- `docs/ARCHITECTURE.md`: §6 *Integration clients* (the Portainer row: actions by ID), §9 *UI layer* (the actions
  section), §11 *Security posture* (as `hosting.md`), §3 if the reconciliation joins the startup sequence.

## Challenge

- M1 (Stop/Restart exceed `RequestTimeoutSeconds` and report *Failed*) — accepted. The action request of Stop and
  Restart is bounded by 300 s plus `RequestTimeoutSeconds` (planning: a fixed bound instead of the container's own stop
  timeout, which would need a new reader of Docker's container inspect); Start keeps `RequestTimeoutSeconds`. No `t` is
  passed (it would cut the operator's `stop_grace_period` short); no new key, so N7 stands. N4 states the sum of the
  steps; E13 and AC12 added.
- m1 (`<environment>` undefined) — accepted: the dialog names the environment ID the capability check used, never a
  name (S3).
- m2 (an unknown `EndpointId` shown as "container changed") — accepted: the capability check and the confirmation
  verify a configured environment and report `Portainer environment <id> not found; …` (E7, AC8); E5 now means only
  that the Engine knows no container with that ID.
- m3 (N8 matches an empty or short host name) — accepted: only a 12-character lowercase hexadecimal `HOSTNAME` counts
  (N8).
- m4 (database and configuration failures undefined) — accepted: E15 and AC13. "In progress" now means an action this
  process is executing (E8, N3), so a run whose terminal write failed blocks nothing until the startup reconciliation.
- m5 (disabled Docker instance) — accepted as recommended: no section, a confirmation ends *Failed* (E14, AC2, AC8, N3).
- m6 (README, `ARCHITECTURE.md`) — accepted: added to *Area documents*.
- m7 (stored endpoint may be missing or stale) — accepted: the run links the stored endpoint when one exists (AC4),
  and the decision to act follows the current configuration (N3).