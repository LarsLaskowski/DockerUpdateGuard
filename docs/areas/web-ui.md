# Web UI

## Scope

The browser interface of DockerUpdateGuard: its pages and routes, what each page shows, which actions a user can
start from it, how open pages pick up changes, the error and not-found pages, and light/dark mode. Users rely on it
to read the state the other areas compute and to start manual scans; it computes no findings of its own.

## Behavior

### General

- The UI is a server-rendered web application. Every page is rendered on the server first and then stays
  interactive over a persistent connection to the server; there is no client-side application logic beyond that
  connection. Data loaded for the first render is reused when the page becomes interactive, not loaded twice.
- There is no sign-in. Everyone who can reach the application can see every page and start every action
  ([Hosting and configuration](hosting.md) owns the security posture).
- All pages share one layout: a navigation drawer, a top bar and the page content. The drawer collapses into a menu
  button on narrow screens.
- The top bar shows the product name, the dark-mode toggle, the number of *protected assets* (manual observed images
  plus own images plus runtime containers) and *last activity* (the start time of the most recent scan run, or "No
  scans yet"). The top bar reloads these values on every navigation and on every dashboard refresh signal (see
  *Refresh without reload*). When its first load fails the top bar shows neither the values nor a placeholder; when
  a reload fails it keeps the last values.
- The navigation lists Dashboard, My Images, Observed Images, Runtime Containers, Docker Instances, Shared Base
  Images, Vulnerabilities and Scan History. *My Images* is listed only when both `DockerUpdateGuard:DockerHub:UserName`
  and `DockerUpdateGuard:DockerHub:Pat` are set; *Shared Base Images* only when at least one shared base image exists. Both
  conditions are evaluated when the session starts. The pages stay reachable by their routes either way. When the
  check for shared base images fails, *Shared Base Images* is hidden.
- When the first load of the Dashboard, My Images, Observed Images, Runtime Containers, Docker Instances, Shared Base
  Images, Vulnerabilities or Scan History page fails, the page shows an error alert in place of its loading skeleton
  (on Observed Images inside the *Manual images* section, the registration form stays usable) until a later load
  succeeds. A page whose load needs two answers (Docker Instances, Scan History) shows no part of its data when
  either fails. A reload that fails after a successful load keeps what is shown.
- The drawer shows the application version: `DockerUpdateGuard:DisplayVersion` when set, otherwise the version of the
  running build, otherwise "unknown".
- Timestamps are shown in UTC as `yyyy-MM-dd HH:mm UTC`, the same for every browser and independent of the time zone and
  culture of the server process. The resource chart labels are UTC too: the first and the last label read
  `MM-dd HH:mm UTC`, the visible labels between them `HH:mm`.
- While a page loads its data it shows placeholder skeletons; a list without data shows an explanatory empty state. A
  detail page (Docker instance, own image, observed image, runtime container) whose record does not exist shows a
  not-found note with a link back to its list instead, and keeps its route. When the page gets another route
  identifier, it clears its detail, its not-found note and its load error at once and shows the loading skeleton until
  the new load ends, also when the first render reused the prerendered record; a record, not-found or failure answer
  that arrives for an earlier route is dropped (also when the page has since returned to that route), so the current
  route's skeleton, detail, not-found note or alert stays; a failed load of an earlier route is still logged. The
  runtime container page also clears its action section and the outcome or error of an action, and drops an
  availability answer that arrives for an earlier route. A re-render with the same route identifier keeps the shown
  record until its reload completes.

### Pages and routes

| Route | Page | Shows |
| --- | --- | --- |
| `/` | Dashboard | Counts of my images, manual observed images, Docker instances, runtime containers and shared base images; active update findings; active derived-runtime warnings of own images; active vulnerability findings of currently running containers with a per-severity breakdown; a configuration hint for vulnerability scanning; the 10 most recent scan runs |
| `/my-images` | My Images | Images discovered from the configured Docker Hub account, with latest scan status and message, number of running containers, derived-runtime hint and vulnerability status |
| `/my-images/{id}` | My Image details | One observed image: reference, latest scan, derived-runtime hint, running containers, base image chain, update findings with tag candidates, and — when there are any — vulnerability findings and its 20 most recent scan runs |
| `/observed-images` | Observed Images | The registration form and the manually registered images with latest scan, active update findings, derived-runtime hint and vulnerability status |
| `/observed-images/{id}` | Observed Image detail | One observed image: reference, latest scan, whether it is an own or a manual image, derived-runtime hint, running containers (own images only), base image chain, vulnerability findings and update findings with tag candidates; no scan history |
| `/runtime-containers` | Runtime Containers | Every currently known container with instance, tag (and the version an alias resolves to), CPU, memory and network usage with a short trend of up to the 12 latest samples, update state, available update and vulnerability status; a link to the own image when the container runs one |
| `/runtime-containers/{instanceId}/{containerId}` | Runtime Container detail | One container: image and digest, Compose project, linked own image, update state and message, derived-runtime hint, base image chain, resource usage history, manual tag preference, available tag candidates, update findings, vulnerability findings and the scans of its 20 most recent snapshots; for a container of a Docker instance with Portainer enabled, the *Container actions* section with the 20 most recent action runs of the container |
| `/docker-instances` | Docker Instances | Configured Docker instances with endpoint, connection kind, latest runtime scan status and time, current resource usage and number of containers; with exactly one instance its current usage and usage history are shown on this page directly |
| `/docker-instances/{id}` | Docker Instance detail | The containers currently observed on one instance with linked own image and resource usage |
| `/base-images` | Shared Base Images | Base images used by two or more enabled images or containers, with the images built on them, the containers and number of enabled observed images that use them, and their vulnerability status |
| `/vulnerabilities` | Vulnerabilities | Active advisories across all images, one row per advisory, with severity and CVSS score, affected packages, fixed versions, number of affected images and running containers; a row expands to the affected images |
| `/scan-history` | Scan History | The 20 most recent scan runs with type, status, trigger, target, start time and error message |
| `/error` | Error | "Unexpected error" — the request could not be completed |
| `/not-found` | Not found | "Page not found" with a link back to the dashboard |

- Detail routes accept only identifiers in GUID format (the container identifier of the runtime container route is
  free text); any other path renders the not-found page.
- Resource usage histories on detail pages show up to the 48 most recent samples of the last 24 hours.
- The Vulnerabilities page filters by free text (advisory identifier, title or package, case-insensitive) and by
  severity (All, Critical, High, Medium, Low), shows 25 rows per page with page sizes 25, 50 and 100, and links an
  advisory to its reference only when the reference is an absolute `http` or `https` address; the link opens in a
  new tab.
- Which values the pages show and how they are computed (update state, recommended tag, vulnerability status,
  derived runtime, base image chain, resource values) is owned by the feature areas listed under *Not here*.

### Actions

| Action | Where | Effect |
| --- | --- | --- |
| Register and scan | Observed Images | Registers an image from name (trimmed), image reference and optional description ([Image catalog](image-catalog.md)), scans it immediately as a manual observed-image scan and opens its detail page; a reference already tracked as an enabled own image is rejected with an `Open own image` link to that image and starts no scan |
| Check image updates | Runtime Containers | Runs a manual runtime container scan of all enabled Docker instances |
| Refresh vulnerabilities | Scan History, shown only when vulnerability scanning is enabled | Runs a manual vulnerability refresh of all live images; the page also shows when the next scheduled refresh is expected |
| Rescan (per image) | Observed Images, My Images, Runtime Containers | Runs a manual vulnerability refresh of that one image version; next to it the time of the last check or "Never scanned" |
| Select / Clear selection | Runtime Container detail | Stores or removes a manual tag preference for that container ([Update detection](update-detection.md)); it does not change or redeploy the container |
| Start / Stop / Restart | Runtime Container detail, *Container actions* section | Starts, stops or restarts that container through Portainer after a confirmation, and records the action as an action run ([Container actions](container-actions.md)) |

- Every action runs while the user waits: its button is disabled and shows progress until the action has finished,
  and the page then reloads its data. Starting the same action twice from one page is not possible while it runs.
- Manual scans are recorded as scan runs with trigger `Manual` ([Scan engine](scan-engine.md)).
- The per-image rescan is offered even when vulnerability scanning is disabled; it then completes without doing
  anything and records no run.
- Name and image reference are required for a registration; the form reports a missing value, a name longer than
  200 characters or a reference part longer than its stored limit before anything is sent, at the field and in the
  form's validation summary. A registration rejected because the reference is already tracked as an own image shows
  `Registration failed: This image is already tracked as own image '<name>'.` with an `Open own image` link to
  `/my-images/{id}`; no other failure shows that link.
- The expected time of the next scheduled vulnerability refresh is an estimate: the start of the most recent
  vulnerability run of any trigger plus `VulnerabilityRefreshIntervalMinutes`, or "shortly (no run recorded yet)".
- The UI offers no redeploy of stacks or update of services
  ([#474](https://github.com/LarsLaskowski/DockerUpdateGuard/issues/474)) and no way to edit configuration, delete
  images or enable and disable them.

### Container actions section

- The runtime container detail page shows a *Container actions* section directly after the container summary when
  [Container actions](container-actions.md#container-availability) says it is shown. The page asks only once it is
  interactive, never while the server renders it first (the check contacts Portainer), and again when the route
  changes to another container; until the answer arrives, and for a Docker instance that is disabled or has Portainer
  disabled, no section appears. If the question itself fails, the section stays hidden and the failure is logged as
  user action `LoadContainerActions`.
- The section holds, in this order:
  1. the heading `Container actions` with the description `Start, stop or restart this container through Portainer.
     Every action asks for confirmation and is recorded.`;
  2. the note `Runtime status as of the last runtime scan (<time>); an action does not update it. Run "Check image
     updates" on Runtime Containers to refresh it.`;
  3. either one alert with the availability message (informational for a container that is not in the latest runtime
     scan or has an action in progress, a warning when Portainer cannot be used), `No actions are available for this
     container.` for a container without offered actions, or the offered buttons: Start (filled), Stop and Restart
     (outlined, in the error color);
  4. after an action, an alert with its outcome message: green for *succeeded*, red otherwise;
  5. the heading `Action history` and the 20 most recent action runs of the container in this Docker instance, newest
     first, with Action (`Start`, `Stop`, `Restart`, `Redeploy stack` or `Update service`), Status (a colored chip:
     *succeeded* green, *failed* red, *running* blue, *requested* orange, *cancelled* neutral), Requested, Completed
     (`—` while not completed), Requested by and Message; with none, `No container actions yet.`.
- A click on an action button opens a confirmation dialog titled `<Action> container?` that says `<Action> container
  '<container name>' on Docker instance '<instance name>' through Portainer environment <id>.`, `<id>` being the
  environment the capability check used. Stop adds `The container stops and stays stopped until it is started again.`
  and Restart `The container is unavailable while it restarts.`; for the application's own container a warning adds
  `This is DockerUpdateGuard's own container: the application <stops|restarts> and this page loses its connection.`
  The buttons are `Cancel` and the action; the confirm button of Stop and Restart is styled as destructive. Cancel and
  closing the dialog send and record nothing; no route, query string or page load starts an action.
- After the confirmation every action button of the container is disabled and the confirmed one shows progress. When
  the action has ended, the page shows the outcome, reloads its data (so the history's newest row is this run, though
  the runtime status stays that of the last runtime scan) and asks for the availability again. Closing the browser
  does not stop the action.
- A confirmation given after the page moved to another container sends and records nothing. While an action of the
  earlier container still runs, the new container's buttons are disabled and none shows progress. The outcome or
  failure of an action, confirmation or tag selection that ends after such a move is not shown (an action's run is in
  its container's history).
- Messages of Portainer or the action are rendered HTML-encoded.

### Refresh without reload

- After a manual scan, registration or rescan finishes, every open dashboard and every open top bar in every browser
  session connected to the same application process reloads its data without a page reload.
- Scheduled background runs do not send this signal: their results become visible when a page is opened or
  navigated to again (the top bar also refreshes on every navigation). No page polls the server.

### Vulnerability configuration hint

The dashboard shows at most one hint about vulnerability scanning:

- scanning disabled: how to enable it (`DockerUpdateGuard:Vulnerabilities:Enabled` and a provider);
- Docker Scout not configured: set `DockerUpdateGuard:DockerHub:UserName` and `DockerUpdateGuard:DockerHub:Pat`.

When the hint applies is owned by [Vulnerabilities](vulnerabilities.md). The hint names configuration keys only,
never their values.

### Light and dark mode

- The UI has a light and a dark theme, switched with the toggle in the top bar.
- The choice is remembered per browser in encrypted browser storage. Without a stored choice the theme follows the
  operating system's color-scheme preference, determined after the first render; the server-rendered first paint
  is always light.
- If the browser storage cannot be read or written, the toggle still works for the session and the choice is not
  remembered.
- Users who ask their system for reduced motion get (practically) no transitions or animations.

### Secrets

- The UI never shows configured secrets: the Docker Hub token, Portainer credentials and token and the database
  connection string are neither displayed nor sent to the browser. Pages show only whether an integration is
  configured, and the Docker Hub user name as the title of the My Images list.
- Docker instance endpoints are shown with the user info of their URL replaced by `***`
  (`https://***@docker.example:2376/`), also for rows stored before the masking existed.

## Limits and errors

- An unhandled error during a request outside the Development environment renders the `/error` page; an unknown
  route or an HTTP error status renders the `/not-found` page. The HTTP-level handling is owned by
  [Hosting and configuration](hosting.md).
- An error in a user action is shown as an error message on the page that started it (for the per-image rescan, next
  to its button); the page stays usable and the action can be started again. The message names the action and a
  fixed reason (`Vulnerability rescan failed: connection failed`), never the error's own text. The reasons are those
  of [Integration failures](integration-failures.md#fixed-failure-reasons); an application-composed message (the rejected image
  reference, name or already tracked own image of [Image catalog](image-catalog.md)) is passed on. The prefix names the user action, not the step that
  failed: a later step of the same action (the scan after a registration, the reload after a save) can fail after the
  earlier step succeeded. When the list reload after a failed registration fails as well, the registration's message stays, the
  list keeps what it showed, and the reload error is logged as a second `RegisterObservedImage` entry. The error is logged as an error with event ID 4000 (`User action <action> failed`) and the
  exception.

  | Action | Prefix | Logged action |
  | --- | --- | --- |
  | Rescan (per image) | `Vulnerability rescan failed` | `VulnerabilityRescan` |
  | Check image updates | `Update check failed` | `UpdateCheck` |
  | Select | `Saving the tag selection failed` | `SaveTagSelection` |
  | Clear selection | `Clearing the tag selection failed` | `ClearTagSelection` |
  | Start / Stop / Restart | `Container action failed` | `ContainerAction` |
  | Register and scan | `Registration failed` | `RegisterObservedImage` |
  | Refresh vulnerabilities | `Vulnerability refresh failed` | `VulnerabilityRefresh` |

  An action that Portainer or a check rejects is not a page error but an outcome: its message is shown in the outcome
  alert and stored in the run ([Container actions](container-actions.md#actions)). `Container action failed` appears
  when the action service itself raises an error, for example an invalid configuration or a run that cannot be
  written, and then no action was sent; or when the page's reload after an action fails, and then the outcome alert
  shows what became of the action.

- A failed first load of a list page or the dashboard shows one error alert `<prefix>: <reason>` with the fixed
  reason in place of the skeleton, no empty-state note and no rows, and is logged like a user action (event ID
  4000). A failed reload after a successful load is logged and shows nothing. The layout's loads are logged the same
  way and show no text: `LoadDashboardSummary` (top bar) and `LoadNavigation` (the *Shared Base Images* entry).

  | Page load | Prefix | Logged action |
  | --- | --- | --- |
  | Dashboard | `Loading the dashboard failed` | `LoadDashboard` |
  | My Images | `Loading the images failed` | `LoadMyImages` |
  | Observed Images | `Loading the observed images failed` | `LoadObservedImages` |
  | Runtime Containers | `Loading the containers failed` | `LoadRuntimeContainers` |
  | Docker Instances | `Loading the Docker instances failed` | `LoadDockerInstances` |
  | Shared Base Images | `Loading the shared base images failed` | `LoadSharedBaseImages` |
  | Vulnerabilities | `Loading the vulnerabilities failed` | `LoadVulnerabilities` |
  | Scan History | `Loading the scan history failed` | `LoadScanHistory` |

- A failed load of a detail page shows one error alert `<prefix>: <reason>` with the fixed reason, no detail data and
  no not-found note, and is logged like a user action (event ID 4000). The reload after a runtime container action
  keeps the action's prefix; a reload without a record shows the not-found note.

  | Page load | Prefix | Logged action |
  | --- | --- | --- |
  | Docker instance detail | `Loading the Docker instance failed` | `LoadDockerInstanceDetail` |
  | Own image detail | `Loading the image failed` | `LoadMyImageDetail` |
  | Observed image detail | `Loading the observed image failed` | `LoadObservedImageDetail` |
  | Runtime container detail | `Loading the container failed` | `LoadRuntimeContainerDetail` |

- Error messages of scan runs are shown as stored ([Scan engine](scan-engine.md)) in the dashboard, the scan history,
  the latest-scan columns and the detail pages.
- If the connection to the server is lost, the page stops being interactive until the browser reconnects or the page
  is reloaded.
- Lists are not paged except the Vulnerabilities page; all other lists show every item or the fixed number stated
  above.

### Known gaps

- The scan status chips (dashboard, scan history, observed image and My Images lists) color only `Failed`, `Running`
  and `Pending`; `Succeeded` and `Partial` are shown without color, so a partial run is not marked as a warning.
  Tracked in [#274](https://github.com/LarsLaskowski/DockerUpdateGuard/issues/274).
- The Scan History and dashboard descriptions mention discovery and cleanup runs, although those jobs record no scan
  runs ([Scan engine](scan-engine.md)).
  Tracked in [#278](https://github.com/LarsLaskowski/DockerUpdateGuard/issues/278).

## Related decisions

- [0005](../decisions/0005-urls-are-output-with-user-info-masked.md) — URLs are output with their user info masked.
- [0009](../decisions/0009-integration-results-carry-fixed-failure-reasons.md) — Failure messages carry fixed reasons,
  also on the pages.
- [0058](../decisions/0058-timestamps-are-shown-in-utc-with-an-explicit-suffix.md) — Timestamps are shown in UTC with an
  explicit suffix.
- [0059](../decisions/0059-a-detail-page-shows-a-missing-record-or-a-failed-load-inline.md) — A detail page shows a
  missing record or a failed load inline.
- [0060](../decisions/0060-a-detail-page-acts-only-on-the-state-of-its-current-route.md) — A detail page
  acts only on the state of its current route.

## Not here

- How update states, recommended tags, tag candidates and manual tag preferences are evaluated in
  [Update detection](update-detection.md).
- Image registration rules and reference formats in [Image catalog](image-catalog.md).
- Base image chains, shared base images and derived runtime hints in [Base images](base-images.md).
- Vulnerability status, severities, fix status and the configuration hint rules in
  [Vulnerabilities](vulnerabilities.md).
- Container discovery and resource values in [Docker instances](docker-instances.md).
- Scan runs, schedules and retention in [Scan engine](scan-engine.md).
- Container actions through Portainer in [Container actions](container-actions.md).
- Error and status-code middleware, HTTPS and the missing authentication in
  [Hosting and configuration](hosting.md).
