# 0026: A failed stats request drops only its container from a resource round

- **Status:** Accepted
- **Date:** 2026-11-13
- **Area:** Docker instances
- **Source:** Issue #270
- **Supersedes:** —

## Context

Resource sampling reads one stats request per running container of an instance, one at a time, inside a single
error handler. A non-success answer or a malformed stats body only left that container out, but a stats request
that timed out, broke at the transport level or returned JSON of the wrong shape ended the whole round: no container
sample and no instance sample. One hung container, frequent under heavy load or in a broken state, thereby blanked the
resource charts of every container on its host for as long as it stayed hung. The Product Manager decided on the
issue that a timeout drops only the affected container, and that a round in which no container could be sampled is
treated as failed.

## Options considered

1. **Handle each stats request in its own error handler inside `DockerInstanceClient`, and answer *failed* when stats
   were requested for at least one container and none could be read (chosen)** — the client keeps deciding what a
   usable round is; `IDockerInstanceClient` and `ResourceStatisticsCollector` stay unchanged.
2. **Return the listed and the sampled count with the samples and let the collector decide** — changes the
   interface, its result type and every substitute of it, for a rule the client can decide alone.
3. **Record the instance sample even when no container could be sampled** — an Engine that answers the list but
   hangs on every stats call would record container count 0, CPU 0 and memory 0, which reads as an idle host.
4. **Sample the containers in parallel, or skip a container that timed out in the previous round** — bounds the
   round's length, but changes the load the Engine sees and adds state across rounds; beyond the issue.
5. **Keep the instance network rate as the delta of summed totals against the previous instance sample** — with
   partial rounds, a container that returns after a dropped round adds its whole counter to that delta and the chart
   spikes (as it already does whenever a new container appears).
6. **Take the instance rate's delta only over the containers sampled in both rounds** — needs the previous round's
   container set and leaves a returning container's traffic out of its round.

## Decision

Option 1: every reason a single stats request fails (non-success, timeout, transport error, unreadable or
wrongly shaped body) leaves only that container out of the round and is logged once per round as a warning; the
instance sample is computed from the containers that were sampled, its network rates being the sums of their
per-container rates, and a round that requested stats but read none fails for the instance. The behavior is described in
[Docker instances](../areas/docker-instances.md#resource-statistics).

## Consequences

- A hung container no longer blanks the charts of its host; its own chart gets a gap for each round it stays hung.
- The instance sample of a round with a dropped container understates the host's CPU, memory and network totals and
  its container count; the warning per dropped container says why.
- Each container rate is computed against that container's own last sample, so summing them keeps the instance rate
  free of spikes when a dropped container returns or a new one appears; the collector no longer reads the previous
  instance sample.
- A round can now take up to one `RequestTimeoutSeconds` per hung container instead of ending at the first timeout.
  Rounds of the job never overlap, so this only delays the next round; option 4 would be the way to bound it.
- A non-success stats answer, dropped silently before, is now logged like the other reasons.
