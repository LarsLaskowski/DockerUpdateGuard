# 0025: A failing interval lookup keeps the job's last known interval

- **Status:** Proposed
- **Date:** 2026-11-13
- **Area:** Scan engine
- **Source:** Issue #281
- **Supersedes:** —

## Context

Every background job reads its interval before every wait, outside the error handling that protects its runs. The
read fails while a reloaded configuration fails validation (`IOptionsMonitor<T>.CurrentValue` validates on every
re-creation and caches nothing that failed) and, for the observed-image refresh, while the database cannot count the
enabled images. The exception then ends the job, and with the platform's default `StopHost` the whole host: one bad
edit of the mounted settings file, or a short database outage, takes the application down, against the scan engine's
promise that a failure never stops the host. The Product Manager decided on the issue that a job keeps its last
valid interval, logs the failure and never stops the host.

## Options considered

1. **Catch the failure once in `ScheduledBackgroundService`, remember the last interval read successfully, and seed
   it with an initial interval each job reads from its configuration when it is created at host start (chosen)** —
   one implementation for all seven jobs; the fallback exists before the first read and needs no further options
   access when the configuration is already invalid.
2. **Seed the fallback from a virtual `GetFallbackInterval()` reading `IOptions<T>.Value`** (the issue's
   recommendation) — `IOptions<T>.Value` is created on its first read, not at startup, and validates then; read for
   the first time after an invalid reload, it throws as well (measured with an in-memory configuration and the options validator), so the fallback would fail exactly
   when it is needed, unless the job read it in its constructor, which is option 1 with a second options interface.
3. **Set `HostOptions.BackgroundServiceExceptionBehavior` to `Ignore`** — the host survives, but the failed job's
   loop has ended and the job never runs again, silently.
4. **Handle the failure in each job's `GetInterval()`** — seven copies of the same handling, and the next job
   would have to remember it.
5. **Keep the whole last valid configuration for every consumer** — would also shield runs and pages from an
   invalid reload, but changes how every consumer reads its options; beyond the issue, which keeps those reads
   as they are.

## Decision

Option 1: `ScheduledBackgroundService` catches every exception of `GetInterval()` except a cancellation during
shutdown, logs it as a warning with the job name and waits the last interval it read successfully — before the first
successful read, the initial interval the job passed to its constructor from the configuration it read at host
start. The behavior is described in [Scan engine](../areas/scan-engine.md#background-jobs).

## Consequences

- An invalid reload or a database outage no longer stops the host; each job keeps its rhythm and picks up a valid
  configuration again from its next wait.
- While the configuration stays invalid, every job logs a warning for each failed lookup (before each wait, and
  once more at its start), and its runs, which read the configuration themselves, fail and are logged as before.
- The jobs read their configuration once more, in their constructors: an invalid configuration at that moment fails
  the host's start, as startup validation does.
- The jobs take a `TimeProvider` for their wait, so a test drives the loop without the real clock.
