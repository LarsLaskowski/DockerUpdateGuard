# Plan: Protect the UI against DNS rebinding with AllowedHosts

Source: Issue #473
Tier: security — the change offers every compose deployment the framework's host-name allow-list (a guard against DNS rebinding) and decides that filter's default, which is the network exposure of the UI; no production or test code changes.
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
| 3 | Probe (minimal `WebApplication.CreateBuilder` app on the installed .NET 11.0.0 runtime, curl with `Host`): unset → `evil.example:8080` 200. `AllowedHosts=dug.lan;192.168.1.5;*.example.com` → `evil.example:8080` 400, `dug.lan:8080` 200 (port ignored), `DUG.LAN` 200 (case-insensitive), `dug.lan.` 400, `192.168.1.5:8080` 200, `a.example.com` 200, `example.com` 400 (a wildcard covers subdomains only), `localhost`, `127.0.0.1`, `[::1]` 400 when not listed (IP addresses are not exempt, also measured by the Product Manager); `AllowedHosts=dug.lan;192.168.1.20;[::1]` → `[::1]` and `[::1]:8080` 200, `192.168.1.20` 200, `[fe80::1]` 400; a WebSocket upgrade to `/_blazor` with a foreign `Host` 400; an HTTP/1.0 request without `Host` 200; `*;dug.lan` → everything 200; a value of one space → everything 400 | probes in the Lead's scratchpad, step 2 | confirmed |
| 4 | The 400 body is the framework page titled "Bad Request" with "Bad Request - Invalid Hostname" | probe body; on the real image it is part of fact 8 | confirmed |
| 5 | `AllowedHosts` as an environment variable wins over `appsettings.json`; a change of `AllowedHosts` in `appsettings.json` applies without a restart | probe: env `dug.lan` over JSON `*` → 400; JSON `dug.lan` → 400, rewritten to `dug.lan;evil.example` → 200 after 3 s, same process | confirmed |
| 6 | A circuit needs component descriptors that are data-protected server-side, so a cross-origin page that cannot read the prerendered HTML gets no component | aspnetcore `ComponentHub.StartCircuit` (`TryDeserializeComponentDescriptorCollection`), `Circuits/ServerComponentDeserializer.cs` lines 85–87, 248 (`IDataProtector.Unprotect`), read on the main branch | confirmed |
| 7 | Compose interpolation of `AllowedHosts: "${ALLOWED_HOSTS:-*}"`: without `.env`, with the line commented out (`# ALLOWED_HOSTS=…`) and with `ALLOWED_HOSTS=` the service gets `*` (rendered `'*'`); `ALLOWED_HOSTS=dug.lan;192.168.1.20;[::1]` (unquoted) arrives as `dug.lan;192.168.1.20;[::1]`; a trailing ` # comment` after an unquoted value is dropped. The double quotes keep the YAML value a string: a plain scalar starting with `*` would be read as a YAML alias | `docker compose config` (YAML and `--format json`) with Docker Compose v5.7.1 in the Lead's scratchpad; the alias rule from the YAML specification | confirmed |
| 8 | On the real image, without `ALLOWED_HOSTS` the UI behaves as today; with it, an allowed host serves the UI with a working interactive circuit and a host or IP not listed gets the 400 page of fact 4 | to verify: the dry run (*Approach*), Product Manager, before step 8 | unverified |
| 9 | The interactive UI keeps working through a reverse proxy that forwards the original `Host` once that name is listed | to verify: the dry run, Product Manager, only where the operator's environment has such a proxy; otherwise the documents state only fact 3's rule | unverified |

## Acceptance criteria

The Product Manager decided "Optional in Compose" (*Challenge*, objection 1): nothing breaks, the key is offered and
documented prominently.

- AC1: Each of the three compose files sets `AllowedHosts: "${ALLOWED_HOSTS:-*}"` (double-quoted) in the
  `environment` of the `dockerupdateguard` service, and nowhere else; without an `ALLOWED_HOSTS` value the service
  gets `*`, as today.
- AC2: `docs/.env.example` has a section for the UI's host names whose `ALLOWED_HOSTS` line is commented out, with an
  example naming a host name and IP addresses (`# ALLOWED_HOSTS=dockerupdateguard.example.lan;192.168.10.20;localhost;127.0.0.1`),
  and comment lines saying: list every name and every IP address used in the browser's address bar, `;`-separated,
  without port (IPv6 in brackets); `*.example.lan` covers subdomains only; left unset every name is accepted, which
  leaves the UI open to DNS rebinding.
- AC3: `INSTALL.md` step 3 recommends setting `ALLOWED_HOSTS` (names and IP addresses the UI is opened with);
  *Security notes* names the DNS rebinding risk with a link to *Security posture*; the enumeration of environment
  variables in *Beyond the quick start* includes `AllowedHosts`, and that paragraph says that with a mounted
  `appsettings.json` the compose file's entry still decides `AllowedHosts` (`*` when `ALLOWED_HOSTS` is unset), so a
  compose deployment sets it in `.env`.
- AC4: `docs/areas/hosting.md` *Security posture* replaces the sentence that links #473 by the rule (Approach), with
  an example for an operator's names and addresses and the statement that IP addresses must be listed too, and the
  document's other statements the change extends are updated (*Documentation updates*).
- AC5: `README.md` has a row `AllowedHosts` in *Connection and host settings* (default `*`, not required), the key in
  the JSON example, a bullet in *Notes for Docker-based deployments*, and the Portainer paragraph names the key.
- AC6: `DOCKER.md` *Troubleshooting* has an entry for "Bad Request - Invalid Hostname" with its cause and fix;
  *Required configuration* recommends `AllowedHosts` for `docker run` and mounted-`appsettings.json` deployments;
  *Common JSON paths* has an `AllowedHosts` row linking *Security posture*.
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
  from `docs/.env.example` (output `AllowedHosts: '*'`) and once more with the example line uncommented (output the
  example list), quoting both in its report; the Reviewer greps that `AllowedHosts` occurs once per file, quoted.
- AC2–AC6: check: the Reviewer reads each edit against its criterion and *Approach*.
- AC7: check: the Reviewer reads the edits; step 7's `config-check.py` covers the record index and links.
- AC8: manual: the Product Manager's dry run (*Approach*), on the branch head, before step 8; the result goes into
  `log.md` as a `7 Manual test` row. A row of *Verified facts* the run does not confirm narrows the documents to what
  it showed.

## Approach

Documentation and the deployment examples only. The application's own default stays the framework's (`*`), the
shipped `appsettings.json` keeps no `AllowedHosts` key, and the compose files pass `ALLOWED_HOSTS` through with `*`
as fallback, so no deployment changes behavior on upgrade and a compose file downloaded again starts as before.

**Rule for the area document** (*Security posture*, replacing the sentence "The application sets no `AllowedHosts` …
tracked in #473"), from facts 2–5:

- `AllowedHosts` (top-level key, environment variable `AllowedHosts`) lists the host names, `;`-separated, that
  requests may carry in `Host`; unset, empty or containing `*`, every name is accepted, which is the default.
- With a list, a request with any other host name is answered with 400 ("Bad Request - Invalid Hostname") before any
  page, asset or UI connection is served. The port is not part of the comparison, case does not matter, an entry
  `*.example.lan` covers its subdomains but not `example.lan`. IP addresses are not exempt: every address the browser
  uses must be listed like a name, `localhost` and `127.0.0.1` included, IPv6 in brackets (`[::1]`).
- It is the deployment's protection against DNS rebinding, recommended for every deployment: set it to every name and
  address the browser's address bar uses for the UI; behind a reverse proxy, to the `Host` the proxy forwards to the
  application (forwarded headers: #283). A change in `appsettings.json` applies to the next request without a restart.
- The documented compose files set it from `ALLOWED_HOSTS` in `.env`, `*` when that is unset. Because the compose
  entry is an environment variable, it decides also when an `appsettings.json` with its own `AllowedHosts` is mounted
  (fact 5).
- Example: `"AllowedHosts": "dockerupdateguard.example.lan;192.168.10.20"` in `appsettings.json`, or
  `ALLOWED_HOSTS=dockerupdateguard.example.lan;192.168.10.20` in the compose `.env`.

The area document states no framework names (*Normative and stable*). The wording of the `.env.example` comments,
the `INSTALL.md` and `DOCKER.md` text and the README row is the Dev's within AC2–AC6; no new shell lines are added to
`INSTALL.md` (the operator uncomments and edits the `ALLOWED_HOSTS` line of `.env`), so *Shell procedures* does not
apply.

**Dry run** (*Operator environments and documented procedures*), by the Product Manager on a Docker host with Compose
v2 and the published image `networlddev/dockerupdateguard:latest` (no image change), with the branch's
`docs/docker-compose.minimal.yml` and `docs/.env.example`, after step 6 and before step 8: (a) with the `.env` as
copied (line commented out), `docker compose up -d` starts and the UI answers on every name and address as today, a
manual scan starts; (b) with `ALLOWED_HOSTS=localhost;127.0.0.1`, `http://localhost:8080` and `http://127.0.0.1:8080`
(on the host or through an SSH tunnel) show the dashboard and a manual scan starts, and the host's LAN IP address or
name answers "Bad Request - Invalid Hostname"; (c) with that address or name added, it works too; (d) where available,
the same through the operator's reverse proxy (fact 9). Where the Product Manager cannot run it, the orchestrator
runs (a)–(c) in the session if its Docker daemon can pull the image, and logs which.

Pages the session cannot open: none — the change links no third-party page.

Early draft PR: no

## Options considered

1. **Document `AllowedHosts` only, compose files unchanged** — lost: compose operators, the way most installations
   come up, would have to add an environment entry themselves; passing `ALLOWED_HOSTS` through costs nothing.
2. **Require it in the compose files (`:?`)** — lost by the Product Manager's decision: every operator would have to
   list every name and internal IP used to open the UI (IPs are not exempt, fact 3), and a compose file downloaded
   again would not start, a breaking change.
3. **Chosen (Product Manager): optional in Compose** — `${ALLOWED_HOSTS:-*}`, the `.env.example` line commented out
   with an example, the key documented prominently; nothing breaks.
4. **A default other than `*` in the application** (the issue's question) — lost: every deployment reached by a
   name not in the default answers 400 after the upgrade; no default name fits.
5. **Fail startup while `AllowedHosts` is unset or `*`** — lost: stops every existing deployment at its upgrade.
6. **Check `Origin` on the Blazor hub** (the issue's second proposal; also `WebSocketOptions.AllowedOrigins`) — lost:
   against `Host` it stops nothing under rebinding (both name the attacker's host); against a list it duplicates
   `AllowedHosts`, covers only the hub and adds nothing, since the host filter already rejects the upgrade (fact 3);
   a cross-origin page cannot read the descriptors a circuit needs (fact 6).
7. **A startup warning while `AllowedHosts` is unset** — reaches every deployment form, but is production code beyond
   the issue's proposals; proposed as a follow-up issue (*Out of scope*).

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| — (deployment examples) | `docs/docker-compose.minimal.yml`, `docs/docker-compose.trivy.yml`, `docs/docker-compose.full.yml` | `AllowedHosts` from optional `ALLOWED_HOSTS`, `*` as fallback |
| — (deployment examples) | `docs/.env.example` | commented-out `ALLOWED_HOSTS` section |
| — (documentation) | `README.md`, `INSTALL.md`, `DOCKER.md`, `docs/areas/hosting.md`, `docs/ARCHITECTURE.md`, `.squad/project.md` | *Documentation updates* |

## Signatures (for the Dev's skeleton)

None: the change adds no API.

## Test files

None: no test code changes.

Existing test code that calls a changed signature: none.

Files of the change no unit test can cover: none (no code).

## Areas

- `docs/areas/hosting.md (+190 words)`: *Security posture* rule and example (Approach); *Configuration sources* names
  the top-level key; *Limits and errors* row; *Related decisions* entry for 0065.

## Documentation updates

All by the Dev; these edits are the change itself.

- `docs/docker-compose.minimal.yml`, `docs/docker-compose.trivy.yml`, `docs/docker-compose.full.yml`: the line of AC1
  as the first entry of the `dockerupdateguard` service's `environment`, value in double quotes.
- `docs/.env.example`: a section `# --- Host names of the UI (recommended) ---` after *PostgreSQL*, AC2.
- `INSTALL.md`: AC3 — step 3 (`ALLOWED_HOSTS`, recommended), *Beyond the quick start* (the enumeration
  `DockerUpdateGuard__...`, `ConnectionStrings__...`, `Telemetry__...` gains `AllowedHosts`, and the advice to mount an
  `appsettings.json` says the compose entry still decides the key, so it is set in `.env`), *Security notes*,
  *Troubleshooting* (link to the new `DOCKER.md` entry).
- `docs/areas/hosting.md`: *Configuration sources* — the sentence "The application reads four top-level sections …"
  also names the top-level key `AllowedHosts` with a link to *Security posture*; *Security posture* — the rule of
  *Approach*; *Limits and errors* — row "Request whose host name a restricted `AllowedHosts` does not list | 400 Bad
  Request ("Invalid Hostname"); nothing is served"; *Related decisions* — 0065 in one line.
- `README.md`: AC5 — *Connection and host settings* row (`AllowedHosts` | `*` | No | host names and IP addresses the
  UI answers, `;`-separated, without port; recommended; link *Security posture*);
  `"AllowedHosts": "dockerupdateguard.example.lan;192.168.10.20"` as the first key of the JSON example; a bullet in
  *Notes for Docker-based deployments*; in the Portainer paragraph "run the application behind a trusted perimeter"
  gains "and set `AllowedHosts`".
- `DOCKER.md`: AC6 — under *Required configuration* a sentence after the list recommending `AllowedHosts` for
  `docker run` and a mounted `appsettings.json`, linking *Security posture*; a row `AllowedHosts` | host names and IP
  addresses the UI answers, protection against DNS rebinding ([Security posture](docs/areas/hosting.md#security-posture))
  in *Common JSON paths*; `### "Bad Request - Invalid Hostname"` under *Troubleshooting*: the name or address in the
  address bar (or the `Host` a proxy forwards) is not in `AllowedHosts`/`ALLOWED_HOSTS`; add it, without port.
- `docs/ARCHITECTURE.md` §11: a bullet after *No authentication or authorization layer*: host names are accepted
  unrestricted unless the deployment sets `AllowedHosts`, its protection against DNS rebinding; the compose files
  pass it through from `.env`; links `areas/hosting.md#security-posture` and record 0065.
- `.squad/project.md (+30 words)`, *Security areas*, a new line after *Configured URLs*, reported by the Dev as exact
  text for the orchestrator: `- **Host names the UI answers:** `AllowedHosts`, passed through in `docs/docker-compose.*.yml`
  and `docs/.env.example` — [Hosting](../docs/areas/hosting.md#security-posture), [decision 0065](../docs/decisions/0065-allowed-host-names-stay-open-by-default-and-optional-in-the-compose-files.md)`.
- The PR carries the label `security`, no `breaking-change` (nothing changes without an `ALLOWED_HOSTS` value).

## Architecture check

The guarantee "no authentication or authorization in the app; it relies on a trusted network perimeter" stays: the
host filter is a deployment setting of the perimeter, not an application login. Layering is untouched (no code).
`docs/ARCHITECTURE.md` §11 gains the bullet above, which links record 0065.

## Security considerations

- [x] No limits: no input is parsed by new code; the host filter's matching rules are the framework's (fact 3).
- [x] Exceptions reaching a user-visible message: none; the only new user-visible output is the framework's 400 page
  (fact 4).
- The default stays open: a deployment without `ALLOWED_HOSTS`/`AllowedHosts` remains exposed to DNS rebinding, an
  accepted limitation (record 0065, the Product Manager's decision); the follow-up warning issue narrows it.
- A list containing `*` anywhere disables the filter (fact 3); `.env.example` and the area document say so.
- A request without `Host` (HTTP/1.0) passes the filter (fact 3); browsers always send `Host`, so rebinding does not
  use that path.
- A trailing-dot name (`dug.lan.`) is rejected unless listed in that form: fails closed (fact 3).

## Decision records

- `docs/decisions/0065-allowed-host-names-stay-open-by-default-and-optional-in-the-compose-files.md` (Proposed)

## Challenge

1. **(major) Requiring `ALLOWED_HOSTS` in the compose files is a breaking product decision the Lead may not take.**
   Accepted and escalated; resolved by the Product Manager's decision "Optional in Compose": the compose files pass
   `${ALLOWED_HOSTS:-*}` through, `.env.example` carries the line commented out with an example of names and IP
   addresses, and the key is documented prominently, so nothing breaks and no `breaking-change` label applies.
   Requiring it lost because operators would have to list every name and internal IP (fact 3). *Approach*, AC1–AC4,
   AC8, *Options considered* and record 0065 (renamed) follow the decision.
2. **(minor) Record 0065 option 2 overstated "upgrades keep working".** Accepted; with the Product Manager's decision
   the record now describes the required variant as the rejected option, naming that a compose file downloaded again
   would not start.
3. **(minor) `DOCKER.md` *Required configuration* and *Common JSON paths* were missing.** Accepted: AC6 and the
   `DOCKER.md` line of *Documentation updates* add both.
4. **(minor) A mounted `appsettings.json` in a compose deployment does not decide `AllowedHosts`.** Accepted: the
   *Security posture* rule (*Approach*), AC3 and the `INSTALL.md` line say that the compose entry keeps deciding (`*`
   when `ALLOWED_HOSTS` is unset), so a compose deployment sets it in `.env` (fact 5).

## Out of scope / follow-ups

- Proposed follow-up issue — title: `[Hosting] Warn at startup while AllowedHosts is unset`; body: "Deployments
  that do not set `AllowedHosts` accept every host name (decision 0065, #473), and nothing tells their operators. Log
  one warning at startup when the key is unset or empty (not when it is set to `*` on purpose), naming the key and
  linking *Security posture* in `docs/areas/hosting.md`." Note for that issue: the compose files pass `*` when
  `ALLOWED_HOSTS` is unset, so a compose deployment would look deliberate; the issue decides how to tell them apart.
- Forwarded headers behind a reverse proxy: #283.
