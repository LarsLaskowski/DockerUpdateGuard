# Scan engine

## Scope

When and how often the application refreshes its data in the background, how each refresh is recorded as a scan
run, how runs that never finished are repaired and how old scan data is removed. Operators rely on it through the
`DockerUpdateGuard:Scanning` interval and retention keys; the [Web UI](web-ui.md) relies on the scan-run records
for its scan history and status chips. What a single scan evaluates belongs to the feature areas listed under
*Not here*.

## Behavior

### Background jobs

The host runs exactly seven recurring background jobs, all in the application process:

| Job | What it triggers | Interval key (`DockerUpdateGuard:Scanning:…`) | Default | Allowed range |
| --- | --- | --- | --- | --- |
| Docker instance synchronization | Reconcile the configured Docker instances with the stored ones ([Docker instances](docker-instances.md)) | `DiscoveryIntervalMinutes` | 15 min | 1–1440 |
| Docker Hub account discovery | Refresh the images of the configured Docker Hub account ([Registries](registries.md)) | `DockerHubAccountDiscoveryIntervalMinutes` | 60 min | 1–10080 |
| Observed-image refresh | Scan every enabled observed image (tag metadata, base images, derived runtime) | dynamic, see below (`OwnImageBaseScanIntervalMinutes` is the floor) | 60 min | 1–10080 |
| Runtime container refresh | Scan the running containers of every enabled Docker instance | `RuntimeImageUpdateScanIntervalMinutes` | 30 min | 1–1440 |
| Resource statistics | Sample CPU, memory and network usage ([Docker instances](docker-instances.md)) | `ResourceStatisticsIntervalMinutes` | 5 min | 1–1440 |
| Vulnerability refresh | Re-assess the vulnerabilities of all live images ([Vulnerabilities](vulnerabilities.md)) | `VulnerabilityRefreshIntervalMinutes` | 180 min | 1–10080 |
| Scan cleanup | Repair unfinished runs and purge old data (see *Retention and cleanup*) | `CleanupIntervalMinutes` | 720 min | 1–10080 |

- The jobs start after the startup sequence of [Hosting and configuration](hosting.md) has completed (database
  reachable, migrations applied).
- Every job except scan cleanup runs once immediately when it starts and then waits one interval between runs.
  Scan cleanup first performs its stale-run repair (see *Unfinished runs*) and runs its first full pass only after
  one `CleanupIntervalMinutes` interval.
- The wait is measured from the end of one run to the start of the next, so runs of one job never overlap each
  other and a long run delays the next one.
- The jobs are independent of each other: they are neither ordered nor coordinated, and two different jobs may run
  at the same time.
- The interval is read again before every wait. A changed interval key takes effect from the next wait without a
  restart; a wait already in progress is not shortened or extended.
- Each run works in its own unit of work (its own database context), so no state carries over between runs.

### Dynamic observed-image interval

The observed-image refresh stretches its interval with the number of enabled observed images so that scheduled
Docker Hub requests stay within a request budget:

```
budget   = DockerHubRequestLimitPerWindow − DockerHubReservedManualRequestsPerWindow
interval = max(OwnImageBaseScanIntervalMinutes, ⌈DockerHubRequestLimitWindowHours × 60 × enabledImages / budget⌉) minutes
```

- With no enabled observed image the interval is `OwnImageBaseScanIntervalMinutes`.
- The count of enabled observed images is read again before every wait.
- Defaults and ranges: `DockerHubRequestLimitWindowHours` 6 (1–168), `DockerHubRequestLimitPerWindow` 200
  (1–100000), `DockerHubReservedManualRequestsPerWindow` 40 (0–100000). The reserved share must be smaller than
  the limit per window, so the budget is always at least 1.
- With the defaults, up to 26 enabled images are scanned every 60 minutes; from 27 images on the interval grows by
  about 2.25 minutes per image (e.g. 100 images → 225 minutes).
- The formula only spaces out scheduled runs. Manual scans are not counted against the budget, and the job does not
  count the requests a run actually makes. Request parallelism and HTTP 429 handling belong to
  [Registries](registries.md).

### Scan runs

Three kinds of work are recorded as scan runs; the other jobs (instance synchronization, account discovery,
resource statistics, cleanup) leave no scan-run record and report only through logs.

| Type | One run per | Started by |
| --- | --- | --- |
| `ObservedImage` | observed image scanned | the observed-image refresh (one run per enabled image), or registering an image in the UI |
| `RuntimeContainer` | enabled Docker instance scanned | the runtime container refresh, or the manual update check in the UI |
| `Vulnerability` | vulnerability refresh | the vulnerability refresh (one run for all live images), or a manual refresh or per-image rescan in the UI |

- A run records its type, status, trigger, start time, completion time, an optional error message and the observed
  image or Docker instance it concerns.
- Trigger is `Scheduled` for runs started by a background job and `Manual` for runs started from the UI. The stored
  values `Startup` and `Discovery` are reserved and not written today.
- A run is stored with status `Running` before any work begins and is updated once at the end to one of:
  - `Succeeded` — every step completed;
  - `Partial` — at least one step returned an incomplete result (for example an unsupported registry or one
    container that could not be processed) while the run as a whole went on;
  - `Failed` — a step reported a failure, or an unexpected error ended the run.
- The error message joins the distinct messages of all incomplete or failed steps, one per line; it is empty for a
  `Succeeded` run.
- The stored values `NotSet` and `Pending` are reserved and not written today.
- A vulnerability refresh that is disabled by configuration, a refresh for an image version that does not exist and
  an observed-image scan for an image that no longer exists create no run at all.
- A runtime container refresh first synchronizes the configured Docker instances; instances that are enabled in the
  database but missing from or disabled in configuration are skipped without a run.
- After every recorded run the inventory gauges are refreshed and the run's duration and status are recorded as
  metrics ([Telemetry](telemetry.md)).

### Overlapping scans

- Scans of the same observed image never run at the same time within one application process. A second request
  for an image that is being scanned waits until the first scan has finished and then runs in full; it is not
  skipped or merged.
- Scans of different observed images may run at the same time (for example a manual registration during the
  scheduled refresh).
- Runtime container scans and vulnerability refreshes have no such guard: a manual run and a scheduled run of the
  same kind may overlap.
- Within the observed-image refresh the images are scanned one after another; within the runtime container refresh
  the instances are scanned one after another. Parallelism of vulnerability provider calls inside one refresh is
  owned by [Vulnerabilities](vulnerabilities.md).

### Failure isolation

- A run of a background job that fails with an error is logged with the job name and its duration and does not stop
  the job, the other jobs or the host; the job waits its interval and runs again.
- Inside a batch, an error while scanning one observed image or one Docker instance is logged and the batch goes on
  with the next one.
- A failed run is not retried before the next scheduled run. `DockerUpdateGuard:Scanning:RetryCount` (default 2,
  range 0–10) applies to individual outbound HTTP requests, not to runs ([Registries](registries.md)).
- Host shutdown cancels the run in progress; it is logged neither as completed nor as failed.

### Unfinished runs

A run can stay `Running` when the process stops or crashes during a scan. Such runs are repaired:

- once when the cleanup job starts, and again at the beginning of every cleanup pass;
- a `Running` run counts as stale once it started longer ago than its threshold, which is twice the configured
  interval of its job, but at least 24 hours:
  - `ObservedImage`: 2 × `OwnImageBaseScanIntervalMinutes` (the configured floor, not the dynamic interval);
  - `RuntimeContainer`: 2 × `RuntimeImageUpdateScanIntervalMinutes`;
  - `Vulnerability`: 2 × `VulnerabilityRefreshIntervalMinutes`;
- a stale run is set to `Failed`, gets the repair time as completion time and the error message
  "Marked as failed by cleanup: the run never completed (process restart or crash)".

With the default intervals every threshold is 24 hours, so a run interrupted by a restart shows as `Running` for up
to a day.

### Retention and cleanup

Each cleanup pass computes a cutoff of now minus `DockerUpdateGuard:Scanning:RetainScanRunsDays` (default 30, range
1–3650) and removes, in one transaction:

- **Scan runs:** only completed runs that no update finding, vulnerability finding, container snapshot or image
  relationship refers to. Of these, a run is kept only while it is both among the 20 most recently completed and
  not completed before the cutoff; all others are removed. Runs still referenced by data are kept regardless of age
  and count, and `Running` runs are never removed.
- **Tag candidates:** those whose update finding was resolved before the cutoff, and those without a digest
  regardless of age.
- **Update findings:** inactive findings resolved before the cutoff.
- **Vulnerability findings:** inactive findings of image versions that are no longer live, regardless of age, and
  inactive findings of live image versions resolved before the cutoff.
- **Container snapshots:** snapshots recorded before the cutoff that have no active update finding and no recorded
  container action.
- **Resource samples:** container and Docker instance samples recorded before the cutoff.

After the removal the cleanup pass refreshes the inventory gauges ([Telemetry](telemetry.md)) and logs how many
tag candidates, update findings, vulnerability findings, snapshots and scan runs it removed. Nothing else (images,
observed images, Docker instances, relationships, tag selections) is removed by cleanup.

### Live images

An image version is *live* when it is:

- the current version of any observed image (enabled or disabled), or
- running according to the most recent runtime container scan of any Docker instance (for an instance without a
  runtime scan, the most recent snapshot of each of its containers counts), or
- a direct base image of one of the above.

The vulnerability refresh assesses exactly the live image versions, and cleanup uses the same set to purge findings
of image versions that left the fleet.

### Deployment assumption

The engine assumes a single application instance per database. Its scan guard is local to one process and the jobs
are not coordinated through the database: several instances against one database duplicate the scheduled work and
may scan the same image at the same time. This is wasteful but does not corrupt the shared image catalog
([Image catalog](image-catalog.md)).

## Limits and errors

- Interval, budget and retention keys outside their ranges, or a reserved request share not smaller than the limit
  per window, fail configuration validation with a message naming the key and the allowed range
  ([Hosting and configuration](hosting.md)).
- The smallest possible interval of every job is one minute; there is no sub-minute schedule and no cron-style
  schedule.
- A step that reports a failure or an unexpected error turns the run `Failed`; the error message of the run carries
  the step's message as returned by the integration, or the error's own message. Integrations report expected
  failures as results, not errors ([Registries](registries.md)).
- A run that cannot be saved at its end (for example because the database became unreachable) stays `Running` until
  the stale-run repair marks it `Failed`. A vulnerability refresh retries this final save once on a fresh state
  before giving up.
- If the database cannot be reached during a cleanup pass, the whole pass fails, nothing is removed, and the next
  pass starts after one interval.

### Known gaps

- Reading a job's interval is outside the job's error handling. When it fails — a configuration reload with an
  invalid value, or an unreachable database while the observed-image refresh counts the enabled images — the job
  ends with an error and the host stops, instead of the job keeping its last known interval.
  Tracked in [#281](https://github.com/LarsLaskowski/DockerUpdateGuard/issues/281).
- The 20 most recently completed scan runs are a cap, not a minimum: an unreferenced run completed before the
  `RetainScanRunsDays` cutoff is removed even when it is among the 20 most recent, so the scan history can be
  empty after a long pause. Tracked in [#290](https://github.com/LarsLaskowski/DockerUpdateGuard/issues/290).
- Runtime container scans and vulnerability refreshes have no per-subject guard: a manual and a scheduled run of the
  same kind can run at the same time (see *Overlapping scans*).
  Tracked in [#292](https://github.com/LarsLaskowski/DockerUpdateGuard/issues/292).

## Related decisions

None yet.

## Not here

- What an observed-image scan resolves: tag metadata and image identity in [Image catalog](image-catalog.md) and
  [Registries](registries.md), base images and derived runtimes in [Base images](base-images.md), recommended
  updates in [Update detection](update-detection.md).
- Container discovery, snapshots and resource sampling in [Docker instances](docker-instances.md).
- Vulnerability providers, assessment status and the provider parallelism in
  [Vulnerabilities](vulnerabilities.md).
- Docker Hub request parallelism, HTTP retry and the failure model of integrations in [Registries](registries.md).
- How scan runs and manual triggers are shown in the UI in [Web UI](web-ui.md).
- Metrics recorded per run and the inventory gauges in [Telemetry](telemetry.md).
- Configuration loading, validation and the startup sequence in [Hosting and configuration](hosting.md).
