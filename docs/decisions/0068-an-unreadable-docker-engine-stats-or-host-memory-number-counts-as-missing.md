# 0068: An unreadable Docker Engine stats or host memory number counts as missing

- **Status:** Proposed
- **Date:** 2026-11-28
- **Area:** Docker instances
- **Source:** Issue #570
- **Supersedes:** 0026 (amends it: a stats body that is a JSON object is no longer left out for a value in it the client cannot read)

## Context

Resource sampling reads CPU, memory and network numbers from each container's stats body, and the host's total memory
from the Engine's info answer. A missing number already counted as 0, but a number sent as another JSON kind (a string,
`null`, an object), or a member on the path to it that is not a JSON object, made the reader throw. The error handler of
the single stats request ([0026](0026-a-failed-stats-request-drops-only-its-container-from-a-resource-round.md)) then
left the whole container out of the round with the reason `unexpected error`, and the info request answered *failed*.
One unreadable optional counter, a network interface's byte count say, thereby blanked every value of that container's
chart for the round; with one running container the round failed for the instance. The Engine sends these values as
unsigned integers, so only a broken or hostile Engine (or a proxy in front of it) sends another kind. The plan challenge
of #566 found the defect.

## Options considered

1. **Check the kind of every member on the read path and of the value itself; anything unreadable counts as missing, 0,
   the way a missing value already does (chosen)** — one rule for missing and unreadable values, the container keeps
   its other values, no exception is used for an expected input.
2. **Keep leaving the container out, but by an explicit check instead of an exception** — one unreadable counter would
   still discard the readable CPU, memory and network values of the container and could fail the instance's round.
3. **Leave the container out only when a value is unreadable, but keep it when the value is missing** — two outcomes
   for inputs that carry the same information; a missing value is already sent by real Engines (no `networks` in host
   network mode, no `percpu_usage` on cgroup v2) and must keep counting as 0.
4. **Answer the info request *failed* when its answer is not a JSON object, and 0 only for an unreadable `MemTotal`** —
   the collector takes a failed and a zero host memory alike as 0, so the extra outcome changes only a log entry, for
   one more code path.

## Decision

A stats or info number is read only when every member on its path is a JSON object and the value is a JSON number
that fits a 64-bit integer (a 32-bit one for the online CPU count); otherwise it counts as missing. A stats body that is
not a JSON object is still left out as [0026](0026-a-failed-stats-request-drops-only-its-container-from-a-resource-round.md)
decided. The values and their fallbacks are in [Docker instances](../areas/docker-instances.md#resource-statistics).

## Consequences

- A container with one unreadable value keeps its readable values in the round; the unreadable one is 0 there, which a
  chart cannot tell from a real 0, and no warning names it.
- An unreadable network value lowers that round's total: its rate is 0 there (the total did not grow), and the next
  readable sample's rate is computed against the lowered total, so it spikes the way it does after a counter reset.
  Leaving the container out (option 2) would avoid the spike at the cost of all its other values.
- An info answer without a readable total memory is *succeeded* with 0 and no longer logged as a failure.
- Revisit, with a warning per container and value, if operators need to find an Engine that sends such values.
