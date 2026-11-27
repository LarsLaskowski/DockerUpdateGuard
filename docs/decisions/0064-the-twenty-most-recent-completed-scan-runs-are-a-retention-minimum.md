# 0064: The twenty most recent completed scan runs are a retention minimum

- **Status:** Accepted
- **Date:** 2026-11-27
- **Area:** Scan engine
- **Source:** Issue #290
- **Supersedes:** —

## Context

The cleanup pass removed every unreferenced completed scan run that was older than the `RetainScanRunsDays` cutoff
**or** not among the 20 most recently completed unreferenced runs. The 20 acted as a cap: with the default 30-day
retention, an instance that records hundreds of runs a day kept a few hours of unreferenced history instead of the
30 days the README promises, and an instance switched off for longer than the retention period lost its whole
unreferenced history in the first pass after restart.

## Options considered

1. **The 20 most recent completed runs are a minimum, counted over all completed runs** — a run is removed only when
   it completed before the cutoff, is not among the 20 most recently completed runs, and nothing refers to it.
   Retention keeps its documented meaning, and since referenced runs are kept anyway, the 20 most recent completed
   runs always exist after a pass. Chosen (the Product Manager's decision on #290).
2. **A minimum counted over the unreferenced runs only** (the set the old cap was counted over) — keeps 20
   unreferenced runs in addition to every referenced one, so more history survives a long pause than the history
   pages show, and the number kept depends on how many runs data still refers to.
3. **Keep the cap and only correct the README** — no code change, but the retention setting would still not mean
   what operators set it for.

## Decision

A completed scan run is removed by cleanup only when it completed before the retention cutoff, is not among the 20
most recently completed scan runs of all completed runs, and no data refers to it. The rules are in
[Scan engine](../areas/scan-engine.md#retention-and-cleanup).

## Consequences

- Unreferenced history now grows with the scan rate over the whole retention period (about 240 observed-image runs a
  day for ten observed images at the default interval); the cleanup pass still loads only the removable runs, never
  all runs (#81 tracks the loading of the pass as a whole).
- After a pause longer than the retention period the 20 most recent runs stay visible in the scan history.
- Revisiting the number 20 changes the constant `MinimumRetainedScanRuns` and the area document, not this rule.