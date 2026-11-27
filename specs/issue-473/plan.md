# Plan: Protect the UI against DNS rebinding with AllowedHosts

Source: Issue #473
Tier: security — the change puts a host-name allow-list (the framework's host filter, a guard against DNS rebinding) in front of the UI of every compose deployment and decides that filter's default, a change to the network exposure; no production or test code changes.
Security areas touched: network exposure of the UI (the `Host` header allow-list, `AllowedHosts`); Docker configuration: the deployment examples `docs/docker-compose.minimal.yml`, `docs/docker-compose.trivy.yml`, `docs/docker-compose.full.yml` and `docs/.env.example`; `.squad/project.md` (*Security areas*, a new entry)

## Problem / root cause

`src/DockerUpdateGuard/appsettings.json` has no `AllowedHosts` key, no code configures `HostFilteringOptions`
(no hit for `HostFiltering` or `AllowedHosts` in `src/`), and `WebApplication.CreateBuilder` (`Program.cs`, `Main`)
then falls back to `*`: every `Host` header is accepted. A page an operator opens inside the perimeter can rebind its
own name (`evil.example`) to the application's address, read the prerendered page with its component descriptors as
same-origin and open the Blazor circuit (`App.razor`: `Routes` and `HeadOutlet` are `InteractiveServer`), reaching
manual scans, tag selection and the container actions of Portainer-enabled instances. Under rebinding `Host` and the
host of `Origin` both name `evil.example`, so only a list of the operator's real host names separates the two
requests; the application cannot know it. `docs/areas/hosting.md` (*Security posture*) states the gap and links
this issue.

Every claim of the issue holds (facts 1–4). Related: forwarded headers are not supported (#283), so behind a reverse
proxy the list must name the `Host` the proxy forwards.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | No `AllowedHosts` and no host-filter configuration exist | `appsettings.json` (only `ConnectionStrings`, `DockerUpdateGuard`, `Telemetry`, `Logging`); grep `HostFiltering\|AllowedHosts` in `src/`: no hit | confirmed |
| 2 | Unset or empty `AllowedHosts` means `*`; the key is split on `;`, empty entries dropped; the options follow configuration changes | aspnetcore `src/DefaultBuilder/src/WebHost.cs` lines 262–272 (main branch, sparse clone); probe below | confirmed |
| 3 | Probe (minimal `WebApplication.CreateBuilder` app on the installed .NET 11.0.0 runtime, curl with `Host`): unset → `evil.example:8080` 200. `AllowedHosts=dug.lan;192.168.1.5;*.example.com` → `evil.example:8080` 400, `dug.lan:8080` 200 (port ignored), `DUG.LAN` 200 (case-insensitive), `dug.lan.` 400, `192.168.1.5:8080` 200, `a.example.com` 200, `example.com` 400 (a wildcard covers subdomains only), `localhost`, `127.0.0.1`, `[::1]` 400 when not listed; a WebSocket upgrade to `/_blazor` with a foreign `Host` 400; an HTTP/1.0 request without `Host` 200; `*;dug.lan` → everything 200; a value of one space → everything 400 | probe in the Lead's scratchpad, step 2 | confirmed |
| 4 | The 400 body is the framework page titled "Bad Request" with "Bad Request - Invalid Hostname" | probe body; on the real image it is part of fact 8 | confirmed |
| 5 | `AllowedHosts` as an environment variable wins over `appsettings.json`; a change of `AllowedHosts` in `appsettings.json` applies without a restart | probe: env `dug.lan` over JSON `*` → 400; JSON `dug.lan` → 400, rewritten to `dug.lan;evil.example` → 200 after 3 s, same process | confirmed |
| 6 | A circuit needs component descriptors that are data-protected server-side, so a cross-origin page that cannot read the prerendered HTML gets no component | aspnetcore `ComponentHub.StartCircuit` (`TryDeserializeComponentDescriptorCollection`), `Circuits/ServerComponentDeserializer.cs` lines 85–87, 248 (`IDataProtector.Unprotect`), read on the main branch | confirmed |
| 7 | Compose interpolation: `${ALLOWED_HOSTS:?Set ALLOWED_HOSTS in .env}` refuses `config` when the variable is missing or empty, with `required variable ALLOWED_HOSTS is missing a value: Set ALLOWED_HOSTS in .env`; an unquoted `.env` value `localhost;dug.lan;*.example.com # names` arrives as `localhost;dug.lan;*.example.com`; of two lines for the variable the last wins | `docker compose config` with Docker Compose v5.7.1 in the Lead's scratchpad | confirmed |
| 8 | On the real image, an allowed host serves the UI with a working interactive circuit, a host not listed gets the 400 page of fact 4, and a compose file without the `.env` variable does not start | to verify: the dry run (*Approach*), Product Manager, before step 8 | unverified |
| 9 | The interactive UI keeps working through a reverse proxy that forwards the original `Host` once that name is listed | to verify: the dry run, Product Manager, only where the operator's environment has such a proxy; otherwise the documents state only fact 3's rule | unverified |

## Acceptance criteria

- AC1: Each of the three compose files sets `AllowedHosts: "${ALLOWED_HOSTS:?Set ALLOWED_HOSTS in .env}"` in the
  `environment` of the `dockerupdateguard` service, and nowhere else.
- AC2: `docs/.env.example` has a section for the UI's host names with `ALLOWED_HOSTS=localhost` and comment lines
  saying: the names (or IP addresses) used in the browser's address bar, separated by `;`, without port; `*.example.lan`
  covers subdomains only; `*` accepts every name and gives up the protection against DNS rebinding.
- AC3: `INSTALL.md` step 3 tells the operator to set `ALLOWED_HOSTS` to the names they open the UI with (keeping
  `localhost` for the URL of step 4); *Updating* says that a compose file downloaded again needs `ALLOWED_HOSTS` in
  `.env`; *Security notes* names the DNS rebinding risk with a link to *Security posture*; the enumeration of
  environment variables in *Beyond the quick start* includes `AllowedHosts`.
- AC4: `docs/areas/hosting.md` *Security posture* replaces the sentence that links #473 by the rule (Approach), with
  an example for an operator's names, and the document's other statements the change extends are updated
  (*Documentation updates*).
- AC5: `README.md` has a row `AllowedHosts` in *Connection and host settings* (default `*`, not required), the key in
  the JSON example, a bullet in *Notes for Docker-based deployments*, and the Portainer paragraph names the key.
- AC6: `DOCKER.md` *Troubleshooting* has an entry for "Bad Request - Invalid Hostname" with its cause and fix.
- AC7: `docs/ARCHITECTURE.md` §11 and `.squad/project.md` (*Security areas*) name the host filter and link the area
  document; decision record 0065 is linked from both the area document and §11.
- AC8: The documented compose procedure works as written on a real host (fact 8).

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | check: `docker compose config` per file, without and with `ALLOWED_HOSTS` | — |
| AC2 | check: read-only | — |
| AC3 | check: read-only | — |
| AC4 | check: read-only | — |
| AC5 | check: read-only | — |
| AC6 | check: read-only | — |
| AC7 | check: read-only, `decision-check.py` | — |
| AC8 | manual: Product Manager, dry run on the branch head | — |

## Verification without tests

Steps 4 (*Skeleton*), 5 (*Tests first*) and the *Coverage gate* are not applicable: no production or test code changes.

- AC1: check: the Dev runs `docker compose -f <file> config` for each of the three files with a scratch `.env` copied
  from `docs/.env.example` (succeeds, `AllowedHosts: localhost` in the output) and without `ALLOWED_HOSTS` (fails with
  the message of fact 7), quoting both in its report; the Reviewer greps that `AllowedHosts` occurs once per file.
- AC2–AC6: check: the Reviewer reads each edit against its criterion and *Approach*.
- AC7: check: the Reviewer reads the edits; step 7's `config-check.py` covers the record index and links.
- AC8: manual: the Product Manager's dry run (*Approach*), on the branch head, before step 8; the result goes into
  `log.md` as a `7 Manual test` row. A row of *Verified facts* the run does not confirm narrows the documents to what
  it showed.

## Approach

Documentation and the deployment examples only; the application's own default stays the framework's (`*`), and the
shipped `appsettings.json` keeps no `AllowedHosts` key, so no deployment changes behavior on upgrade.

**Rule for the area document** (*Security posture*, replacing the sentence "The application sets no `AllowedHosts` …
tracked in #473"), from facts 2–5:

- `AllowedHosts` (top-level key, environment variable `AllowedHosts`) lists the host names, `;`-separated, that
  requests may carry in `Host`; unset, empty or containing `*`, every name is accepted, which is the default.
- With a list, a request with any other host name is answered with 400 ("Bad Request - Invalid Hostname") before any
  page, asset or UI connection is served. The port is not part of the comparison, case does not matter, an entry
  `*.example.lan` covers its subdomains but not `example.lan`, and an IP address the browser uses must be listed like
  a name (`localhost` and `127.0.0.1` too).
- It is the deployment's protection against DNS rebinding: set it to every name and address the browser's address
  bar uses for the UI; behind a reverse proxy, to the `Host` the proxy forwards to the application (forwarded headers:
  #283). A change in `appsettings.json` applies to the next request without a restart.
- The documented compose files require it (`ALLOWED_HOSTS` in `.env`), the other deployment forms do not.
- Example: `"AllowedHosts": "dockerupdateguard.example.lan;192.168.10.20"` in `appsettings.json`, or
  `AllowedHosts=dockerupdateguard.example.lan;192.168.10.20` as an environment variable.

The area document states no framework names (*Normative and stable*). The wording of the `.env.example` comments,
the `INSTALL.md` and `DOCKER.md` text and the README row is the Dev's within AC2–AC6; no new shell lines are added to
`INSTALL.md` (the operator edits the `ALLOWED_HOSTS` line of `.env`), so *Shell procedures* does not apply.

**Dry run** (*Operator environments and documented procedures*), by the Product Manager on a Docker host with Compose
v2 and the published image `networlddev/dockerupdateguard:latest` (no image change), with the branch's
`docs/docker-compose.minimal.yml` and `docs/.env.example`, after step 6 and before step 8: (a) without the
`ALLOWED_HOSTS` line `docker compose up -d` refuses with the message of fact 7; (b) with `ALLOWED_HOSTS=localhost`,
`http://localhost:8080` (on the host, or through an SSH tunnel) shows the dashboard and a manual scan starts, and the
host's IP address or name answers "Bad Request - Invalid Hostname"; (c) with `localhost;<the host's name or IP>` both
work; (d) where available, the same through the operator's reverse proxy (fact 9). Where the Product Manager cannot
run it, the orchestrator runs (a)–(c) in the session if its Docker daemon can pull the image, and logs which.

Pages the session cannot open: none — the change links no third-party page.

Early draft PR: no

## Options considered

1. **Document `AllowedHosts` only, every default stays open** — lost: the compose walkthrough, the way most new
   installations come up, would keep leaving them open; the issue asks for protection, not only a note.
2. **Chosen:** document the key, require it in the compose files (`:?`, so a missing value stops `docker compose`
   with a message instead of opening silently), `.env.example` value `localhost` so the walkthrough's URL works.
3. **A default other than `*` in the application** (the issue's question) — lost: every deployment reached by a
   name not in the default answers 400 after the upgrade; no default name fits (the probe: even `127.0.0.1` must be
   listed beside `localhost`).
4. **Fail startup while `AllowedHosts` is unset or `*`** — lost: stops every existing deployment at its upgrade, a
   breaking change a Product Manager decision would have to take; option 2 gives new compose installations the same
   explicit choice without it.
5. **Check `Origin` on the Blazor hub** (the issue's second proposal; also `WebSocketOptions.AllowedOrigins`) — lost:
   against `Host` it stops nothing under rebinding (both name the attacker's host); against a list it duplicates
   `AllowedHosts`, covers only the hub and adds nothing, since the host filter already rejects the upgrade (fact 3);
   a cross-origin page cannot read the descriptors a circuit needs (fact 6).
6. **A startup warning while `AllowedHosts` is unset** — reaches `docker run` and `appsettings.json` deployments, but
   is production code beyond the issue's proposals; proposed as a follow-up issue (*Out of scope*).
7. **`${ALLOWED_HOSTS:-localhost}` instead of `:?`** — lost: a compose file downloaded again over an old `.env` would
   silently answer 400 to every remote browser instead of naming the missing variable.

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| — (deployment examples) | `docs/docker-compose.minimal.yml`, `docs/docker-compose.trivy.yml`, `docs/docker-compose.full.yml` | `AllowedHosts` from required `ALLOWED_HOSTS` |
| — (deployment examples) | `docs/.env.example` | `ALLOWED_HOSTS=localhost` section |
| — (documentation) | `README.md`, `INSTALL.md`, `DOCKER.md`, `docs/areas/hosting.md`, `docs/ARCHITECTURE.md`, `.squad/project.md` | *Documentation updates* |

## Signatures (for the Dev's skeleton)

None: the change adds no API.

## Test files

None: no test code changes.

Existing test code that calls a changed signature: none.

Files of the change no unit test can cover: none (no code).

## Areas

- `docs/areas/hosting.md (+170 words)`: *Security posture* rule and example (Approach); *Configuration sources* names
  the top-level key; *Limits and errors* row; *Related decisions* entry for 0065.

## Documentation updates

All by the Dev; these edits are the change itself.

- `docs/docker-compose.minimal.yml`, `docs/docker-compose.trivy.yml`, `docs/docker-compose.full.yml`: the line of AC1
  as the first entry of the `dockerupdateguard` service's `environment`.
- `docs/.env.example`: a section `# --- Host names of the UI ---` after *PostgreSQL*, AC2.
- `INSTALL.md`: AC3 — step 3 (`ALLOWED_HOSTS`), step 4 ("or a name you listed"), *Updating*, *Beyond the quick
  start* (the enumeration `DockerUpdateGuard__...`, `ConnectionStrings__...`, `Telemetry__...` gains `AllowedHosts`),
  *Security notes*, *Troubleshooting* (link to the new `DOCKER.md` entry).
- `docs/areas/hosting.md`: *Configuration sources* — the sentence "The application reads four top-level sections …"
  also names the top-level key `AllowedHosts` with a link to *Security posture*; *Security posture* — the rule of
  *Approach*; *Limits and errors* — row "Request whose host name a restricted `AllowedHosts` does not list | 400 Bad
  Request ("Invalid Hostname"); nothing is served"; *Related decisions* — 0065 in one line.
- `README.md`: AC5 — *Connection and host settings* row (`AllowedHosts` | `*` | No | host names the UI answers,
  `;`-separated, without port; link *Security posture*); `"AllowedHosts": "dockerupdateguard.example.lan"` as the
  first key of the JSON example; a bullet in *Notes for Docker-based deployments*; in the Portainer paragraph "run
  the application behind a trusted perimeter" gains "and set `AllowedHosts`".
- `DOCKER.md`: AC6 — `### "Bad Request - Invalid Hostname"` under *Troubleshooting*: the name in the address bar
  (or the `Host` a proxy forwards) is not in `AllowedHosts`/`ALLOWED_HOSTS`; add it, without port.
- `docs/ARCHITECTURE.md` §11: a bullet after *No authentication or authorization layer*: host names are accepted
  unrestricted unless the deployment sets `AllowedHosts`, its protection against DNS rebinding; the compose files
  require it; links `areas/hosting.md#security-posture` and record 0065.
- `.squad/project.md (+30 words)`, *Security areas*, a new line after *Configured URLs*, reported by the Dev as exact
  text for the orchestrator: `- **Host names the UI answers:** `AllowedHosts`, set in `docs/docker-compose.*.yml` and
  `docs/.env.example` — [Hosting](../docs/areas/hosting.md#security-posture), [decision 0065](../docs/decisions/0065-allowed-host-names-are-required-by-the-compose-files-and-open-by-default.md)`.
- The PR carries the labels `security` and `breaking-change` (a compose file downloaded again needs the new `.env`
  variable), saying so in its description (`.github/pull_request_template.md`).

## Architecture check

The guarantee "no authentication or authorization in the app; it relies on a trusted network perimeter" stays: the
host filter is a deployment setting of the perimeter, not an application login. Layering is untouched (no code).
`docs/ARCHITECTURE.md` §11 gains the bullet above, which links record 0065.

## Security considerations

- [x] No limits: no input is parsed by new code; the host filter's matching rules are the framework's (fact 3).
- [x] Exceptions reaching a user-visible message: none; the only new user-visible output is the framework's 400 page
  (fact 4) and Compose's interpolation error (fact 7).
- A list containing `*` anywhere disables the filter (fact 3); `.env.example` and the area document say so.
- A request without `Host` (HTTP/1.0) passes the filter (fact 3); browsers always send `Host`, so rebinding does not
  use that path.
- A trailing-dot name (`dug.lan.`) is rejected unless listed in that form: fails closed (fact 3).

## Decision records

- `docs/decisions/0065-allowed-host-names-are-required-by-the-compose-files-and-open-by-default.md` (Proposed)

## Out of scope / follow-ups

- Proposed follow-up issue — title: `[Hosting] Warn at startup while AllowedHosts is unset`; body: "Deployments
  started with `docker run` or a mounted `appsettings.json` accept every host name until the operator sets
  `AllowedHosts` (decision 0065, #473), and nothing tells them. Log one warning at startup when the key is unset or
  empty (not when it is set to `*` on purpose), naming the key and linking *Security posture* in
  `docs/areas/hosting.md`."
- Forwarded headers behind a reverse proxy: #283.
