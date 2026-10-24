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
  *Refresh without reload*).
- The navigation lists Dashboard, My Images, Observed Images, Runtime Containers, Docker Instances, Base Images,
  Vulnerabilities and Scan History. *My Images* is listed only when both `DockerUpdateGuard:DockerHub:UserName` and
  `DockerUpdateGuard:DockerHub:Pat` are set; *Base Images* only when at least one base image has been resolved. Both
  conditions are evaluated when the session starts. The pages stay reachable by their routes either way.
- The drawer shows the application version: `DockerUpdateGuard:DisplayVersion` when set, otherwise the version of the
  running build, otherwise "unknown".
- Timestamps are shown as short date and short time in the time zone and culture of the server process, not of the
  browser.
- While a page loads its data it shows placeholder skeletons; a list without data shows an explanatory empty state.

### Pages and routes

| Route | Page | Shows |
| --- | --- | --- |
| `/` | Dashboard | Counts of my images, manual observed images, Docker instances, runtime containers and resolved base images; active update findings; active derived-runtime warnings of own images; active vulnerability findings of currently running containers with a per-severity breakdown; a configuration hint for vulnerability scanning; the 10 most recent scan runs |
| `/my-images` | My Images | Images discovered from the configured Docker Hub account, with latest scan status and message, number of running containers, derived-runtime hint and vulnerability status |
| `/my-images/{id}` | My Image details | One observed image: reference, latest scan, derived-runtime hint, running containers, base image chain, update findings with tag candidates, and — when there are any — vulnerability findings and its 20 most recent scan runs |
| `/observed-images` | Observed Images | The registration form and the manually registered images with latest scan, active update findings, derived-runtime hint and vulnerability status |
| `/observed-images/{id}` | Observed Image detail | One observed image: reference, latest scan, whether it is an own or a manual image, derived-runtime hint, running containers (own images only), base image chain, vulnerability findings and update findings with tag candidates; no scan history |
| `/runtime-containers` | Runtime Containers | Every currently known container with instance, tag (and the version an alias resolves to), CPU, memory and network usage with a short trend of up to the 12 latest samples, update state, available update and vulnerability status; a link to the own image when the container runs one |
| `/runtime-containers/{instanceId}/{containerId}` | Runtime Container detail | One container: image and digest, Compose project, linked own image, update state and message, derived-runtime hint, base image chain, resource usage history, manual tag preference, available tag candidates, update findings, vulnerability findings and the scans of its 20 most recent snapshots |
| `/docker-instances` | Docker Instances | Configured Docker instances with endpoint, connection kind, latest runtime scan status and time, current resource usage and number of containers; with exactly one instance its current usage and usage history are shown on this page directly |
| `/docker-instances/{id}` | Docker Instance detail | The containers currently observed on one instance with linked own image and resource usage |
| `/base-images` | Base Images | Resolved base images with the images built on them, the runtime containers and number of observed images that use them, and their vulnerability status |
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
| Register and scan | Observed Images | Registers an image from name, image reference and optional description ([Image catalog](image-catalog.md)), scans it immediately as a manual observed-image scan and opens its detail page |
| Check image updates | Runtime Containers | Runs a manual runtime container scan of all enabled Docker instances |
| Refresh vulnerabilities | Scan History, shown only when vulnerability scanning is enabled | Runs a manual vulnerability refresh of all live images; the page also shows when the next scheduled refresh is expected |
| Rescan (per image) | Observed Images, My Images, Runtime Containers | Runs a manual vulnerability refresh of that one image version; next to it the time of the last check or "Never scanned" |
| Select / Clear selection | Runtime Container detail | Stores or removes a manual tag preference for that container ([Update detection](update-detection.md)); it does not change or redeploy the container |

- Every action runs while the user waits: its button is disabled and shows progress until the action has finished,
  and the page then reloads its data. Starting the same action twice from one page is not possible while it runs.
- Manual scans are recorded as scan runs with trigger `Manual` ([Scan engine](scan-engine.md)).
- The per-image rescan is offered even when vulnerability scanning is disabled; it then completes without doing
  anything and records no run.
- Name and image reference are required for a registration; the form reports a missing value before anything is
  sent.
- The expected time of the next scheduled vulnerability refresh is an estimate: the start of the most recent
  vulnerability run of any trigger plus `VulnerabilityRefreshIntervalMinutes`, or "shortly (no run recorded yet)".
- The UI offers no container actions (restart, redeploy, update) and no way to edit configuration, delete images or
  enable and disable them.

### Refresh without reload

- After a manual scan, registration or rescan finishes, every open dashboard and every open top bar in every browser
  session connected to the same application process reloads its data without a page reload.
- Scheduled background runs do not send this signal: their results become visible when a page is opened or
  navigated to again (the top bar also refreshes on every navigation). No page polls the server.

### Vulnerability configuration hint

The dashboard shows at most one hint about vulnerability scanning:

- scanning disabled: how to enable it (`DockerUpdateGuard:Vulnerabilities:Enabled` and a provider);
- Trivy not configured: set `DockerUpdateGuard:Vulnerabilities:TrivyBaseUrl`;
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
  to its button); the page stays usable and the action can be started again. The message is the error's own message.
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
- A detail route with a well-formed identifier that does not exist (for example a deleted observed image) keeps
  showing the loading skeleton instead of a not-found state.
  Tracked in [#40](https://github.com/LarsLaskowski/DockerUpdateGuard/issues/40).
- The Scan History and dashboard descriptions mention discovery and cleanup runs, although those jobs record no scan
  runs ([Scan engine](scan-engine.md)).
  Tracked in [#278](https://github.com/LarsLaskowski/DockerUpdateGuard/issues/278).

## Related decisions

- [0005](../decisions/0005-urls-are-output-with-user-info-masked.md) — URLs are output with their user info masked.

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
