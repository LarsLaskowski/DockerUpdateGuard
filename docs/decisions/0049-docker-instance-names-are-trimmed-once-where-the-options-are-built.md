# 0049: Docker instance names are trimmed once where the options are built

- **Status:** Proposed
- **Date:** 2026-11-17
- **Area:** Docker instances
- **Source:** Issue #266
- **Supersedes:** —

## Context

An instance is identified by its trimmed `Name`, compared case-insensitively. The synchronization and the validator
trimmed the configured name, but the runtime container scan, the resource collection and the container actions looked
the configured instance up by its untrimmed name, so an instance configured as `"Production "` was stored and shown
but never scanned, never sampled and offered no actions. The Engine and Portainer clients, their messages and log
entries received the untrimmed name as well. Every consumer reads the options through `IOptionsMonitor`, which builds
them on startup and on every reload.

## Options considered

1. **Trim every instance name in a post-configure step of the options pipeline** — one place, applied before
   validation and on every reload; every consumer, message and log entry sees the same name. Consumers constructed
   with hand-built options (tests) no longer get the trimming themselves. Chosen.
2. **Trim at each lookup** (both dictionaries, the container-action lookup) — the smallest diff, but the clients,
   messages and log entries keep the untrimmed spelling, and each new lookup has to repeat the rule.
3. **Both** — lookups stay correct for hand-built options too, but the rule lives in four places for one behavior.

## Decision

A post-configure step of `DockerUpdateGuardOptions` trims the `Name` of every configured Docker instance before the
options are validated or handed to a consumer, on startup and on every reload; no consumer trims the name for lookup.
The behavior is stated in [Docker instances](../areas/docker-instances.md#configured-instances).

## Consequences

- Validation messages, Engine and Portainer messages and log entries name the instance as the UI shows it.
- The existing `Trim()` calls in the synchronization stay as harmless repetitions.
- Tests that feed a consumer untrimmed names must build the options through the options pipeline with the
  post-configure step registered.