# Phase 8 — Root / Configuration / Docs

> **Start in a new chat:** "Read and execute `docs/review/prompts/phase-8-config-docs.md`."

## Context

Everything outside the application code: build & solution configuration, CI pipeline,
container build instructions, runtime config, and documentation. What matters here is
consistency between docs/config and the actual code.

**Scope: 26 files.** Among others `.github/` (2), `.serena/` (6), `rules/` (2),
`Directory.Build.props`, `Directory.Packages.props`, `DockerUpdateGuard.slnx`,
`SharedAssemblyInfo.cs`, `README.md`, `DOCKER.md`, `LICENSE.md`, `azure-pipelines.yml`,
`.editorconfig`, `.gitignore`, `.dockerignore`, as well as app config
(`appsettings.json`, `Properties/launchSettings.json`, `entrypoint.sh`,
`Dockerfile`, `DockerUpdateGuard.csproj`).

## Your file list (authoritative from the matrix)

```powershell
Select-String -Path docs/review/file-inventory.md -Pattern '\| P8 \|' |
  ForEach-Object { ($_.Line -split '\|')[3].Trim().Trim('`') }
```

## Criteria

Read [../criteria.md](../criteria.md). Particular focus:

- **K10 Configuration:** Does the README config reference (keys, defaults,
  required fields) match `appsettings.json` and the options code (P1/P6)?
- **K3 Security:** no real secrets checked into `appsettings.json`/`launchSettings.json`/
  the pipeline; `Dockerfile`/`entrypoint.sh` without root/permission weaknesses;
  `.dockerignore` protects against leaking sensitive files into the image.
- **K12 Docs:** README.md / DOCKER.md current against code & pipeline; version/
  build arguments consistent (`DisplayVersion`).
- **Build/CI:** `Directory.Packages.props` (central versions) consistent;
  ruleset/analyzer config (`rules/`, `.editorconfig`) plausible; pipeline steps
  complete (build/test/format).

## Workflow

1. **Triage** of all 26 files → indicators + status in [../file-inventory.md](../file-inventory.md).
2. **Deep-dive** for 🔬 → findings `F-NNN` in [../findings.md](../findings.md)
   (section "Phase 8"), link, status ✅.
3. Update [../progress.md](../progress.md) row P8.

## Completion condition

```powershell
(Select-String -Path docs/review/file-inventory.md -Pattern '\| P8 \|.*\| ⬜ \|').Count  # must be 0
```

## After Phase 8: finalize the overall review

When all phases are ✅: the total number of open rows must be 0:

```powershell
(Select-String -Path docs/review/file-inventory.md -Pattern '\| ⬜ \|').Count  # must be 0
```

Then finalize the totals row in [../progress.md](../progress.md) and add an
overall summary by severity to [../findings.md](../findings.md).
