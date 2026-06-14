# Phase 1 — Docker / DockerHub / Portainer / Configuration (Security Core)

> **Start in a new chat:** "Read and execute `docs/review/prompts/phase-1-docker-security.md`."

## Context

DockerUpdateGuard is an ASP.NET Core Razor Components app that monitors the Docker
runtime and compares it against registry metadata. This phase reviews the
**security core**: connectivity to Docker engines, Docker Hub / OCI registries,
Portainer, and the associated configuration. This is where credentials, TLS options,
and socket access live — the highest risk, hence Phase 1.

**Scope: 33 files.** Modules: `App/Docker` (7), `App/DockerHub` (7),
`App/Portainer` (10), `App/Configuration` (9).

## Your file list (authoritative from the matrix)

```powershell
Select-String -Path docs/review/file-inventory.md -Pattern '\| P1 \|' |
  ForEach-Object { ($_.Line -split '\|')[3].Trim().Trim('`') }
```

This list is binding — **every** one of these files must end up with status ✅.

## Criteria

Read [../criteria.md](../criteria.md). For each file, assess the four focus areas
(Sec./Correct./Arch./Tests). In this phase, with particular focus on:

- **K3 Security:** DockerHub `Pat` & Portainer `Password`/`ApiToken` — never logged,
  not in exceptions/telemetry. Review `SkipCertificateValidation`/`UseTls` paths
  (no silent TLS bypass). Docker socket access (`unix://`, `npipe://`). SSRF
  via configurable `BaseUrl`. Auth header construction for registry/Portainer.
- **K4 Resilience:** Is `RequestTimeoutSeconds` actually applied? `HttpClient` lifetime
  (IHttpClientFactory vs. `new HttpClient`)? Error paths for an unreachable
  engine/registry/Portainer — handled cleanly rather than swallowed?
- **K10 Configuration:** Are options validators present & correct (required fields when
  Portainer is enabled: `ApiToken` OR `Username`+`Password`)? Check defaults against
  the README.

## Workflow

1. **Triage:** Open each file in the list, set the indicators in
   [../file-inventory.md](../file-inventory.md), status → ✅ (or 🔬 for 🟡/🔴).
2. **Deep-dive** for 🔬 files: line by line. For each finding, create an entry
   `F-NNN` in [../findings.md](../findings.md) (section "Phase 1") following the
   template there; record the ID in the *Findings* column of the matrix. Then status ✅.
3. **Update progress** in [../progress.md](../progress.md) (row P1: status, reviewed count,
   finding count).

## Project conventions (consider when assessing K9)

file-scoped namespaces, one top-level type per file, `#region` blocks, XML docs on
all members, Allman braces, `var` preferred, no `this.`, `== false` instead of `!`,
Reihitsu analyzer, net10.0. **No** unrelated changes to production code in
this phase — review/docs only. If fixes are desired: separately, after approval.

## Completion condition Phase 1

```powershell
(Select-String -Path docs/review/file-inventory.md -Pattern '\| P1 \|.*\| ⬜ \|').Count  # must be 0
```

If 0: set P1 to ✅ in [../progress.md](../progress.md) and summarize the
findings.
