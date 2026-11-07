# 0018: The Docker Hub user name is validated as a Docker Hub namespace at startup

- **Status:** Accepted
- **Date:** 2026-11-05
- **Area:** Registries
- **Source:** Issue #363
- **Supersedes:** —

## Context

`DockerUpdateGuard:DockerHub:UserName` is the namespace of the Docker Hub account listing:
`DockerHubClient.GetRepositoriesAsync` builds `v2/repositories/{name}/` with `Uri.EscapeDataString`, which leaves `.`
alone, and `System.Uri` removes a `.` segment and resolves a `..` segment, so `.` or `..` sent the listing, with the
account's bearer token, to another path of the Docker Hub web API. [0014](0014-registry-tag-names-and-digests-are-validated-where-they-are-read.md)
(option 19) left this configured value to the startup validation instead of a check in the request builder. The same
value is also the login identifier of the Docker Hub web API, the user part of the registry token request's basic
credentials and the Docker Scout login name.

## Options considered

1. **Reject only `.` and `..`** — closes the defect, but keeps values that are no Docker Hub name (`a/b`, `me:x`,
   `a b`) and fail later at Docker Hub with a less clear message, and a `:` splits the basic credentials of the
   registry token request.
2. **Hold the trimmed value to one path component of the Docker reference grammar, ASCII letters of either case,
   bounded like a repository path** — every Docker Hub namespace fits it, no form of it is a dot segment, and a
   configuration with an upper-case letter keeps starting. Chosen.
3. **Hold it to Docker's sign-up rule for a Docker ID (4 to 30 lower-case letters and digits)** — stricter than any
   check the security property needs, and would stop deployments whose value has an upper-case letter or belongs to an
   older account outside today's sign-up rule.
4. **Refuse a dot segment in `GetRepositoriesAsync` as well** — the validator also runs on every configuration reload
   before the value is read, so the request builder sees no other value; [0014](0014-registry-tag-names-and-digests-are-validated-where-they-are-read.md)
   rejected it for the same reason.

## Decision

Option 2: a configured Docker Hub user name must have the form of a Docker Hub namespace, with letters of either case,
and any other value fails the configuration validation. The rule, its bound and its failure text are in
[Registries](../areas/registries.md#configuration-reload).

## Consequences

- A configuration whose user name is an e-mail address or carries other characters stops starting. Whether Docker Hub accepted such a value for login is not known; the
  account listing never worked with it.
- Upper-case letters stay accepted although Docker Hub names are lower-case; how Docker Hub treats them is unchanged.
