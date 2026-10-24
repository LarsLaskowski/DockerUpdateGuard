# 0006: Insecure Portainer HTTP warns at the first request per instance

- **Status:** Proposed
- **Date:** 2026-10-24
- **Area:** Container actions
- **Source:** Issue #276
- **Supersedes:** —

## Context

`AllowInsecureHttp: true` lets the Portainer integration send the API token, or the username, password and the
returned JWT, over plaintext `http`. `.squad/project.md` requires the transport relaxations `SkipCertificateValidation`
and `AllowInsecureHttp` to stay opt-in and keep their runtime warnings, but `AllowInsecureHttp` was only a
validation switch. `SkipCertificateValidation` already warns once per Docker instance name per process lifetime when
the first connection for that instance is created. Today no page or job calls `PortainerClient` (#280), so no
credential is sent yet; the warning must be in place before a caller exists.

## Options considered

1. **Warn when the first Portainer request for an instance is about to be made, once per instance name per process
   lifetime** — mirrors `SkipCertificateValidation`, and warns exactly when credentials actually travel in plaintext.
   Until a caller exists (#280) the warning never appears.
2. **Warn once at startup for every enabled instance with an `http` Portainer URL** — visible even if Portainer is
   never contacted, but it reports a transmission that may never happen, needs a second component (startup or
   options validation) to own a log, and does not cover a URL changed by a configuration reload.
3. **Both startup and first request** — two warnings for one condition, and two places to keep consistent.
4. **Warn on every request** — floods the log under repeated capability checks, which `SkipCertificateValidation`
   deliberately avoids.

## Decision

`PortainerClient` logs a warning naming the Docker instance, never a credential, the first time it is about to send
a request for an instance whose Portainer `BaseUrl` parses to the `http` scheme; it remembers the instance names it
has warned for in an instance field of the singleton client. The behavior is described in
[Container actions](../areas/container-actions.md), *Transport security*.

## Consequences

- The warning appears once a caller of `PortainerClient` exists (#280); before that, the operator sees only the
  startup validation and the documentation of `AllowInsecureHttp`.
- An instance whose `BaseUrl` changes from `https` to `http` by a configuration reload warns at its next request,
  since the check reads the URL of each request; a name that has warned once never warns again in that process.
- Revisit when Portainer is contacted at startup or by a job, or if operators ask for the warning without any
  Portainer traffic (option 2).
