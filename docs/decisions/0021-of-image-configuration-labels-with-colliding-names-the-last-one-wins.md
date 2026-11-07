# 0021: Of image configuration labels with colliding names, the last one wins

- **Status:** Accepted
- **Date:** 2026-11-07
- **Area:** Registries
- **Source:** Issue #357
- **Supersedes:** —

## Context

Both registry clients read the `Labels` object of an image configuration into a dictionary whose keys are compared
case-insensitively ([Registries](../areas/registries.md#what-can-be-read)), built with `ToDictionary`. Two labels
whose names differ only in case — legal: label and annotation keys are case-sensitive strings, and Docker keeps
`Foo` and `foo` apart — made `ToDictionary` throw, and so does a name that repeats exactly, which `JsonDocument`
accepts by default. The whole configuration lookup then failed with `unexpected error`: the base-image chain of an
observed image became unavailable (scan *failed* or *partial*) and its environment variables were lost for runtime
detection, although only one label was ambiguous. The labels are author-controlled text looked up by two fixed
names ([0009](0009-integration-results-carry-fixed-failure-reasons.md), option 28), so which one wins is not a
trust question, only one of predictability.

## Options considered

1. **The label that comes last in the configuration wins, compared case-insensitively.** Chosen. One rule for both
   collisions: `JsonElement.TryGetProperty`, which reads the base-image labels elsewhere in both clients, also
   returns the last of exact duplicates. Configurations written by Go tooling (Docker, BuildKit) serialize the label
   map with sorted keys, so of names that differ only in case the all-lowercase one — the form of the OCI names
   looked up — comes last.
2. **The first label wins.** Equally simple, but the opposite of `System.Text.Json` for exact duplicates, and in a
   Go-written configuration it picks the mixed-case variant over the conventional lowercase name.
3. **Compare label names case-sensitively (ordinal dictionary).** Matches the label semantics of Docker, but changes
   the documented case-insensitive lookup, stops finding a base-image label written in another case, and still needs
   a rule for exact duplicates.
4. **Prefer the name that equals the looked-up name exactly, otherwise any case.** Needs both entries kept and a
   lookup type of its own in place of the dictionary the resolver reads; on Go-written configurations it gives the
   same result as option 1.
5. **Answer a configuration with colliding names as an invalid response.** Rejects legal configurations and throws
   away every other field of the configuration for one ambiguous label.
6. **Drop every label of a colliding name.** Loses the label for no gain; the author controls all of them anyway.

## Decision

When an image configuration holds several labels whose names are equal case-insensitively, the label that comes last
in the configuration's `Labels` object is kept, with its own name and value; the lookup stays case-insensitive. Both
registry clients apply this rule through one shared reader.

## Consequences

- A configuration with colliding label names is read successfully; the rule is stated in
  [Registries](../areas/registries.md#what-can-be-read).
- An author who sets both `Org.Opencontainers.Image.Base.Name` and `org.opencontainers.image.base.name` gets the one
  later in the document; for a Go-written configuration that is the lowercase one.
- Revisit together with option 3 if label lookups ever need Docker's case-sensitive semantics.