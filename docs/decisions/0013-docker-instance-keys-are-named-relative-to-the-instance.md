# 0013: Validation messages name Docker instance keys relative to the instance

- **Status:** Accepted
- **Date:** 2026-10-27
- **Area:** Hosting and configuration
- **Source:** Issue #330
- **Supersedes:** —

## Context

Configuration validation named every key inside a Docker instance as
`DockerUpdateGuard:DockerInstances:<instance name>:<key>`. `DockerInstances` is a list, so configuration addresses an
entry by its list position (`DockerUpdateGuard:DockerInstances:0:Portainer:BaseUrl`, or
`DockerUpdateGuard__DockerInstances__0__Portainer__BaseUrl` as an environment variable): an operator who copied the
key from the message added a stray list entry instead of fixing the instance (issue #330, found while planning #279).
The entry without a name was reported as `DockerUpdateGuard:DockerInstances:Name`, which is no key either. The
validator sees the bound options, not the configuration: the binder fills the list from the child keys in key order
and closes gaps, so keys `1`, `2` and `10` become list positions 0, 1 and 2 (measured with .NET SDK 10.0.403).
Record [0010](0010-portainer-environment-is-used-only-when-unambiguous.md) (option 7) already named a Portainer key
relative to its instance in a runtime message for the same reason.

## Options considered

1. **Name the key relative to the named instance** (`'Portainer:BaseUrl' of Docker instance 'Production'`) — instance
   names are required and unique, so the subject is unambiguous whatever the configuration source and key layout;
   the same form as the runtime message of record 0010. The operator still finds the entry's position. Chosen.
2. **Name the full key with the bound list position** — copyable when the keys are `0` … `n-1`, but with sparse keys
   (an environment variable that adds entry `5`, or entries removed from the middle) the position differs from the
   key, and a copied key then changes another instance or adds a stray entry again.
3. **Name the full key with the configuration's own child key** — exact, but the validator would have to pair the
   bound entries with the configuration's children by repeating the binder's rules (key order, gaps, entries that
   bind to nothing) and would name a wrong key without notice when the two disagree.
4. **Position and name together** — inherits the defect of option 2 in the one case where the position matters.

## Decision

Every validation message about a key inside a named Docker instance names the key relative to the instance together
with the instance's configured name; the message about an entry without a name names the list section. The message
texts are in [Hosting and configuration](../areas/hosting.md#validation).

## Consequences

- An operator copies the relative key and adds it to the entry of the named instance; no message proposes a key that
  creates a stray list entry or edits another instance.
- A message about an entry without a name cannot say which entry it is; the shipped configuration has a single entry,
  and the message repeats once per nameless entry.
- Messages for keys outside `DockerInstances` keep naming the full configuration path.