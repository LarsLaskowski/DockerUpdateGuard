# Notification Feature Set — Concept

This document describes the design for an optional notification subsystem in
DockerUpdateGuard. It covers the goals, the architectural decisions, the data
model, deduplication and lifecycle rules, the channel abstraction with a
webhook reference implementation, configuration, observability, the testing
strategy, and the staged delivery plan.

## Goals and requirements

1. **Strictly optional.** Notifications are disabled by default
   (`DockerUpdateGuard:Notifications:Enabled = false`). With the section absent
   from the configuration, the application behaves exactly as today.
2. **Trigger A — update available.** Notify when an update is available for
   * a currently running container, and
   * an observed (monitored) image.
3. **Trigger B — critical vulnerabilities.** Notify when a *new* critical CVE
   is detected in an image used by a currently running container.
4. **Channel type is not final.** The design provides a channel-agnostic
   abstraction (`INotificationChannel`) and one reference implementation: a
   generic HTTP webhook (JSON POST, with ntfy- and Gotify-compatible payload
   formats). Further channels (e.g. e-mail/SMTP) can be added later without
   touching the dispatch pipeline.

## Non-goals (v1)

* "Resolved"/all-clear notifications when a finding is deactivated.
* A settings UI — configuration stays file/environment based like every other
  option section.
* Per-container or per-image notification opt-outs.
* Guaranteed exactly-once delivery (see semantics below).

## Architecture decision: outbox/diff pattern instead of direct hooks

Two approaches were evaluated:

* **(a) Direct hooks:** the scan orchestrators
  (`RuntimeContainerScanOrchestrator`, `ImageScanOrchestrator`,
  `VulnerabilityEnrichmentService`) call a notification service whenever they
  create a finding.
* **(b) Outbox/diff (chosen):** a new scheduled background service
  periodically diffs the already persisted findings against a persisted
  notification ledger (`NotificationRecord`) and dispatches the delta.

| Concern | Direct hooks (a) | Outbox/diff (b) — chosen |
| --- | --- | --- |
| Writers | Requires touching three services inside their scan transactions | Zero changes; the finding tables *are* the event source (`IsActive`, `DetectedAtUtc` already exist) |
| Crash safety | Send before `SaveChangesAsync` → phantom notification; after → lost between commit and send | Detection only sees committed rows; record status is persisted → at-least-once, survives restarts |
| Duplicates | Runtime update findings are deactivated and re-created on every scan, so hooks would fire on every scan cycle | Content-based dedup key in the ledger absorbs finding row churn |
| Coupling | Scan latency includes webhook network I/O and retries | Scanning and delivery are fully decoupled; delivery failures never affect scans |
| Batching | Needs extra buffering | Natural: one digest per dispatch run |

The deciding detail: runtime update findings are row-churned on every scan
(`DeactivateSupersededRuntimeFindingsAsync` + `CreateRuntimeFindingAsync`), so
"one notification per finding row" is wrong regardless of the approach —
deduplication must be content based and persisted. Once a persisted dedup
ledger exists, the outbox is nearly free and direct hooks buy nothing.

**Delivery semantics: at-least-once.** A crash between a successful webhook
response and `SaveChangesAsync` re-sends the affected digest once. This is an
accepted trade-off for notifications.

## Data model (migration `Update8`)

### New entity `NotificationRecord`

`src/DockerUpdateGuard.Data/Entities/NotificationRecord.cs`

| Property | Type | Notes |
| --- | --- | --- |
| `Id` | `Guid` | Primary key, `Guid.NewGuid()` |
| `Kind` | `NotificationKind` | `NotSet = 0`, `ContainerUpdateAvailable = 1`, `ObservedImageUpdateAvailable = 2`, `CriticalVulnerability = 3` |
| `DedupKey` | `string` (max 512) | Content-based identity (see below); unique index on `(ChannelName, DedupKey)` |
| `ChannelName` | `string` (max 64) | e.g. `"Webhook"`; one record per event × channel (multi-channel ready) |
| `Status` | `NotificationRecordStatus` | `NotSet = 0`, `Pending = 1`, `Sent = 2`, `Retrying = 3`, `DeadLettered = 4`, `Suppressed = 5` (`Suppressed` = baseline capture, never sent) |
| `UpdateFindingId` | `Guid?` | FK → `UpdateFindings`, `OnDelete(SetNull)` so records survive scan cleanup |
| `VulnerabilityFindingId` | `Guid?` | FK → `VulnerabilityFindings`, `OnDelete(SetNull)` |
| `Subject` | `string` (max 512) | Denormalized display text (e.g. `prod / web-1: nginx:1.27.1 → 1.29.0`) so history survives finding cleanup |
| `Details` | `string?` | Optional denormalized detail line |
| `AttemptCount` | `int` | Default 0 |
| `NextAttemptAtUtc` | `DateTimeOffset?` | Backoff scheduling |
| `LastError` | `string?` (max 1024) | Truncated channel error message |
| `DispatchBatchId` | `Guid?` | Groups records delivered in the same digest |
| `CreatedAtUtc` | `DateTimeOffset` | |
| `SentAtUtc` | `DateTimeOffset?` | |

Supporting pieces follow the existing conventions:

* `src/DockerUpdateGuard.Data/Configurations/NotificationRecordConfiguration.cs`
  (`IEntityTypeConfiguration<NotificationRecord>`; unique index on
  `(ChannelName, DedupKey)`, index on `(Status, NextAttemptAtUtc)`).
* `DbSet<NotificationRecord> NotificationRecords` on
  `DockerUpdateGuardDbContext`.
* Migration `Update8` following the established naming pattern: file renamed to
  drop the timestamp prefix, generated `[Migration("yyyyMMddHHmmss_Update8")]`
  attribute kept unchanged.

### Retention

`ScanCleanupBackgroundService` additionally deletes `NotificationRecords` in a
final state (`Sent`, `Suppressed`, `DeadLettered`) older than the existing
`Scanning:RetainScanRunsDays` cutoff — **except** records whose linked finding
is still active, which prevents an old-but-active finding from being
re-notified after its record was purged.

## Deduplication and lifecycle rules

All rules are evaluated **per channel**: "a record exists" always means "a
record with this `ChannelName` exists". A channel enabled later automatically
gets its own baseline.

### Trigger A — updates

Detection scope per dispatch run (only when the respective toggle is on):

* **Containers:** `UpdateFindings` where `IsActive`,
  `ContainerSnapshotId != null`, `RecommendedImageVersionId != null` and
  `Type` ∈ { `RuntimeImageUpdate`, `DerivedBaseRuntimeUpdate` }.
  `TagRecommendation` findings ("needs review", no concrete update) are
  deliberately excluded.
* **Observed images:** `UpdateFindings` where `IsActive`,
  `ObservedImageId != null`, `RecommendedImageVersionId != null` and the
  observed image `IsEnabled` (types `BaseImageUpdate`, `RuntimeImageUpdate`).

Dedup keys (content based, immune to finding row churn):

```text
update:container:{DockerInstanceId}:{ContainerId}:{RecommendedImageVersionId}
update:observed:{ObservedImageId}:{RecommendedImageVersionId}
```

Rules:

* Notify **once per (subject, recommended version)**. A later scan re-creating
  the same finding with the same recommendation matches the existing `Sent`
  record and is not re-sent.
* A **changed recommendation** (e.g. `1.29.0` → `1.29.1` appears) produces a
  new dedup key → a new record → a new notification.
* A finding deactivated without a successor (the user updated the container)
  sends nothing in v1; "resolved" notifications are a possible future `Kind`.

### Trigger B — critical CVEs

Detection: `VulnerabilityFindings` where `IsActive` and
`Severity >= MinimumVulnerabilitySeverity` (default `Critical`) **and** the
finding's `ImageVersionId` belongs to a currently running container. Because
container snapshots are per-scan rows, "running" means the *latest snapshot per
(DockerInstanceId, ContainerId)* — group by instance/container, take the row
with the newest `RecordedAtUtc`, filter `IsRunning`. This is the same grouping
`ApplicationTelemetry.RefreshInventoryMetricsAsync` already uses.

Dedup key — exactly the upsert identity used by
`VulnerabilityEnrichmentService` (`AdvisoryId` + `AffectedPackage` per image
version):

```text
cve:{ImageVersionId}:{AdvisoryId}:{AffectedPackage ?? "-"}
```

Rules:

* **Reactivation** of a previously resolved finding (same identity) matches
  the existing record → not re-notified (same known CVE, no news).
* **Severity escalation** into the threshold (e.g. `High` → `Critical`): the
  finding never matched before, so no record exists → notified. Correct by
  construction because the key contains no severity.
* **A container starts using an already vulnerable image:** its
  `ImageVersionId` newly enters the running set → notified.

### Baseline capture (anti-storm)

On a dispatch run where a channel has **zero** notification records, all
currently matching findings are recorded with `Status = Suppressed` and nothing
is sent ("baseline captured"). Enabling notifications on an installation with
hundreds of historical findings therefore sends nothing; only subsequent
changes notify. Setting `NotifyPreExistingFindings = true` sends the backlog
instead. Fresh installations have an empty baseline, so the first real scan
results do notify.

## Channel abstraction

New folder `src/DockerUpdateGuard/Notifications/` (peer of
`Vulnerabilities/`), one type per file:

```text
Notifications/
    Interfaces/INotificationChannel.cs
    Interfaces/INotificationChannelResolver.cs
    Interfaces/INotificationDispatchService.cs
    NotificationChannelResolver.cs
    NotificationDispatchService.cs
    NotificationDispatchBackgroundService.cs
    NotificationLoggingExtensions.cs
    Channels/WebhookNotificationChannel.cs
    Data/NotificationMessageData.cs
    Data/NotificationItemData.cs
    Data/NotificationDeliveryData.cs
```

```csharp
public interface INotificationChannel
{
    string Name { get; }

    bool IsEnabled { get; }

    Task<ExternalOperationResult<NotificationDeliveryData>> SendAsync(NotificationMessageData message,
                                                                      CancellationToken cancellationToken);
}
```

* `NotificationMessageData`: `Title`, `Summary`, `GeneratedAtUtc`,
  `UpdateItems` / `VulnerabilityItems` (`IReadOnlyList<NotificationItemData>`),
  `TruncatedItemCount`.
* `NotificationItemData`: `Kind`, `Subject`, `Details`, `Severity?`,
  `AdvisoryId?`, `CurrentVersion?`, `RecommendedVersion?`.
* `NotificationDeliveryData`: `DeliveredAtUtc`, `HttpStatusCode?` — the payload
  of the `ExternalOperationResult`; channels never throw across the interface,
  matching the repository-wide convention.
* `INotificationChannelResolver` mirrors `IVulnerabilityProviderResolver`
  (singleton, options monitor, concrete channels injected) but returns **all
  enabled channels**: `IReadOnlyList<INotificationChannel> ResolveEnabledChannels()`.
  With `Notifications:Enabled = false` it returns an empty list. Adding e-mail
  later means one new channel singleton, one resolver line, and one options
  subsection — no dispatcher changes; the per-channel ledger and baseline
  already handle it.

Dependency injection additions in `AddDockerUpdateGuardHost`:

* named `HttpClient` for the webhook channel with `TransientHttpRetryHandler`
  (the `PortainerClient.HttpClientName` pattern, because the channel is a
  singleton with hot-reloadable options),
* `WebhookNotificationChannel` and `INotificationChannelResolver` as
  singletons, `INotificationDispatchService` as scoped,
* `NotificationDispatchBackgroundService` as hosted service.

## Webhook reference channel

`WebhookNotificationChannel` (`Name = "Webhook"`) builds each request from
`IOptionsMonitor` at send time, POSTs the digest, and maps the outcome to
`ExternalOperationResult`: any 2xx → `Succeeded`; non-2xx → `Failed` with the
status code; missing configuration → `NotConfigured`; exceptions are caught and
returned as `Failed`. A per-request timeout comes from
`RequestTimeoutSeconds`.

### Payload formats (`WebhookPayloadFormat`)

`Generic` (default) — full JSON document:

```json
{
  "source": "DockerUpdateGuard",
  "version": 1,
  "generatedAtUtc": "2026-07-27T18:00:00Z",
  "title": "DockerUpdateGuard: 2 updates, 1 critical vulnerability",
  "summary": "Plain-text digest of all items",
  "updates": [
    {
      "kind": "ContainerUpdateAvailable",
      "subject": "prod / web-1",
      "currentVersion": "nginx:1.27.1",
      "recommendedVersion": "1.29.0"
    }
  ],
  "vulnerabilities": [
    {
      "advisoryId": "CVE-2026-1234",
      "severity": "Critical",
      "package": "openssl",
      "subject": "nginx:1.27.1"
    }
  ],
  "truncatedItemCount": 0
}
```

`Ntfy` — `title`, `message`, `priority`, `tags` posted to the configured topic
URL (priority 5 when vulnerabilities are present, otherwise 3).

`Gotify` — `title`, `message`, `priority` (the token goes into the URL or the
configured header).

### Retry layering

1. **In-request (HTTP-transient):** `TransientHttpRetryHandler` on the named
   client handles 5xx/408/timeouts like every other outbound client.
2. **Cross-run (outbox):** when a digest send fails, all included records move
   to `Retrying` with `AttemptCount + 1` and
   `NextAttemptAtUtc = now + RetryBaseDelayMinutes × 2^(AttemptCount − 1)`
   (5, 10, 20, 40, 80 minutes by default). When `AttemptCount` reaches
   `MaxDeliveryAttempts`, the record becomes `DeadLettered` with `LastError`
   persisted, a warning log, and a failure metric. Dead-lettered records are
   never retried automatically; re-notification only happens naturally when
   the dedup key changes.

## Configuration

New nested options on `DockerUpdateGuardOptions`
(`public NotificationOptions Notifications { get; set; } = new();`), new files
`Configuration/NotificationOptions.cs`, `Configuration/WebhookNotificationOptions.cs`,
`Configuration/WebhookPayloadFormat.cs`.

### `DockerUpdateGuard:Notifications`

| Key | Default | Description |
| --- | --- | --- |
| `Enabled` | `false` | Master switch; everything below is inert without it |
| `DispatchIntervalMinutes` | `5` | Dispatch cadence (1–1440) |
| `NotifyContainerUpdates` | `true` | Trigger A, container scope |
| `NotifyObservedImageUpdates` | `true` | Trigger A, observed-image scope |
| `NotifyVulnerabilities` | `true` | Trigger B |
| `MinimumVulnerabilitySeverity` | `Critical` | `VulnerabilitySeverity` threshold (allows `High`) |
| `NotifyPreExistingFindings` | `false` | Send the historical backlog instead of capturing a silent baseline |
| `MaxItemsPerNotification` | `50` | Digest item cap (1–500) |
| `MaxDeliveryAttempts` | `5` | Attempts before dead-lettering (1–20) |
| `RetryBaseDelayMinutes` | `5` | Base for exponential backoff (1–1440) |

### `DockerUpdateGuard:Notifications:Webhook`

| Key | Default | Description |
| --- | --- | --- |
| `Enabled` | `false` | Enables the webhook channel |
| `Url` | — | Required when enabled; absolute http(s) URL |
| `AuthorizationHeaderName` | `Authorization` | Header used for the auth value |
| `AuthorizationHeaderValue` | `null` | e.g. `Bearer …`; optional |
| `PayloadFormat` | `Generic` | `Generic` \| `Ntfy` \| `Gotify` |
| `RequestTimeoutSeconds` | `30` | Per-request timeout (1–300) |
| `AllowInsecureHttp` | `false` | Required to permit plaintext-HTTP endpoints |

Validation extends `DockerUpdateGuardOptionsValidator` in the established
style (full configuration-path messages, shared URI helper): when `Enabled`,
at least one channel must be enabled; when `Webhook:Enabled`, `Url` must be an
absolute http(s) URI and plaintext HTTP requires `AllowInsecureHttp`; range
checks always apply. The README configuration reference gains matching tables
and an extended JSON example.

## Dispatch pipeline

`NotificationDispatchBackgroundService` derives from
`ScheduledBackgroundService` (template: `VulnerabilityRefreshBackgroundService`):
interval from `Notifications:DispatchIntervalMinutes`, immediate first run
(cheap no-op when disabled, drains due retries after a restart), each run
creates a scope and calls `INotificationDispatchService.DispatchAsync`.

`DispatchAsync` end to end:

1. Resolve enabled channels; none → return.
2. Per channel: baseline check (zero records → capture `Suppressed` baseline
   unless `NotifyPreExistingFindings`).
3. **Detection:** run the trigger queries, anti-join against
   `NotificationRecords` on `(ChannelName, DedupKey)`, insert `Pending`
   records with denormalized subject/details, save. Detection is committed
   *before* delivery so a delivery crash cannot lose events.
4. **Delivery:** load `Pending` plus due `Retrying` records for the channel;
   build the digest (cap items, note the truncated count); `SendAsync`; on
   success mark all records `Sent` with a shared `DispatchBatchId`; on failure
   apply the retry/dead-letter transitions; save.
5. Record metrics and a summary log line.

## Observability

* **Logging:** `NotificationLoggingExtensions` with source-generated
  `[LoggerMessage]` methods in the free **EventId block 3600–3699**
  (existing blocks end at 3501): dispatch skipped, baseline captured, events
  detected, digest sent, digest send failed, record dead-lettered, channel not
  configured, run summary.
* **Metrics:** new constants in `TelemetryMetricNames` —
  `dockerupdateguard.notifications.sent`,
  `dockerupdateguard.notifications.failed` (counters, tagged with channel and
  kind) and `dockerupdateguard.notifications.pending` (observable gauge) —
  wired through `ApplicationTelemetry` alongside the existing inventory
  gauges.

## Testing strategy

MSTest with `{Class}{Scenario}{ExpectedResult}` naming and assertion messages,
using the existing helpers (`SqliteTestDatabase`, `TestOptionsMonitor<T>`,
`TestLogger<T>`, `StubHttpMessageHandler` and friends):

* `Update8MigrationTests` (Data.Tests) — table, unique index, `SetNull` FK
  behavior; extend `MappingTests`.
* `NotificationDispatchServiceTests` — the core suite with a fake channel:
  disabled no-op, baseline suppression and override, dedup across finding row
  churn, re-notify on recommendation change, running-container CVE join
  (latest-snapshot semantics, stopped containers excluded), severity threshold
  and escalation, reactivation suppression, retry/backoff/dead-letter
  transitions, digest cap, per-channel independence.
* `WebhookNotificationChannelTests` — payload schema per format, auth header,
  timeout, non-2xx → `Failed`, missing URL → `NotConfigured`.
* `NotificationChannelResolverTests`, `NotificationDispatchBackgroundServiceTests`
  (mirroring the existing resolver/background-service tests).
* Extend `DockerUpdateGuardOptionsValidatorTests` and
  `ScanCleanupBackgroundServiceTests` (record retention including
  active-finding protection).

## Delivery stages

The feature set is split into sequential, individually shippable stages, each
tracked as its own GitHub issue:

1. **Notification foundation** — data model (`NotificationRecord`, enums,
   migration `Update8`), options and validation, README configuration tables,
   channel abstraction and resolver, dispatch background service skeleton with
   baseline capture, logging block 3600, metric name constants.
2. **Webhook reference channel** — `WebhookNotificationChannel` with the three
   payload formats, auth header, timeout, named client with retry handler, DI
   wiring.
3. **Trigger A: update-availability notifications** — detection queries for
   both scopes, dedup keys, per-trigger toggles, digest builder, `Sent`
   bookkeeping.
4. **Trigger B: critical-CVE notifications** — CVE detection with the
   latest-running-snapshot join, severity threshold, reactivation and
   escalation rules.
5. **Delivery hardening** — outbox retry with exponential backoff,
   dead-lettering, cleanup-service retention with active-finding protection,
   telemetry counters and gauge.
6. **Notification history UI (optional)** — read-only history page following
   the `*ViewData` and skeleton-component patterns, nav entry shown only when
   notifications are enabled.

## Later channel candidates

The abstraction is designed so that each of these is a self-contained addition
(one channel class, one options subsection, one resolver line):

* E-mail (SMTP, e.g. via MailKit)
* ntfy/Gotify native clients beyond the payload-format support in the webhook
  channel
* Messenger webhooks with dedicated formats (Slack, Discord, Teams)
