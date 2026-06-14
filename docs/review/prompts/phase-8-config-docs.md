# Phase 8 — Root / Konfiguration / Doku

> **Start in neuem Chat:** „Lies und führe `docs/review/prompts/phase-8-config-docs.md` aus."

## Kontext

Alles außerhalb des Anwendungscodes: Build- & Solution-Konfiguration, CI-Pipeline,
Container-Bauanleitung, Laufzeit-Konfig und Dokumentation. Hier zählt Konsistenz
zwischen Doku/Konfig und tatsächlichem Code.

**Umfang: 26 Dateien.** U. a. `.github/` (2), `.serena/` (6), `rules/` (2),
`Directory.Build.props`, `Directory.Packages.props`, `DockerUpdateGuard.slnx`,
`SharedAssemblyInfo.cs`, `README.md`, `DOCKER.md`, `LICENSE.md`, `azure-pipelines.yml`,
`.editorconfig`, `.gitignore`, `.dockerignore`, sowie App-Konfig
(`appsettings.json`, `Properties/launchSettings.json`, `entrypoint.sh`,
`Dockerfile`, `DockerUpdateGuard.csproj`).

## Deine Dateiliste (autoritativ aus der Matrix)

```powershell
Select-String -Path docs/review/file-inventory.md -Pattern '\| P8 \|' |
  ForEach-Object { ($_.Line -split '\|')[3].Trim().Trim('`') }
```

## Kriterien

Lies [../criteria.md](../criteria.md). Besonderer Fokus:

- **K10 Konfiguration:** Stimmt die README-Konfig-Referenz (Keys, Defaults,
  Pflichtfelder) mit `appsettings.json` und dem Options-Code (P1/P6) überein?
- **K3 Sicherheit:** keine echten Secrets in `appsettings.json`/`launchSettings.json`/
  Pipeline eingecheckt; `Dockerfile`/`entrypoint.sh` ohne Root-/Berechtigungs-Schwächen;
  `.dockerignore` schützt vor Leak sensibler Dateien ins Image.
- **K12 Doku:** README.md / DOCKER.md aktuell gegen Code & Pipeline; Versions-/
  Build-Argumente konsistent (`DisplayVersion`).
- **Build/CI:** `Directory.Packages.props` (zentrale Versionen) konsistent;
  Ruleset/Analyzer-Konfig (`rules/`, `.editorconfig`) plausibel; Pipeline-Schritte
  vollständig (Build/Test/Format).

## Workflow

1. **Triage** aller 26 Dateien → Ampeln + Status in [../file-inventory.md](../file-inventory.md).
2. **Deep-Dive** für 🔬 → Befunde `F-NNN` in [../findings.md](../findings.md)
   (Abschnitt „Phase 8"), verlinken, Status ✅.
3. [../progress.md](../progress.md) Zeile P8 aktualisieren.

## Abschlussbedingung

```powershell
(Select-String -Path docs/review/file-inventory.md -Pattern '\| P8 \|.*\| ⬜ \|').Count  # muss 0 sein
```

## Nach Phase 8: Gesamt-Review abschließen

Wenn alle Phasen ✅: Gesamtzahl offener Zeilen muss 0 sein:

```powershell
(Select-String -Path docs/review/file-inventory.md -Pattern '\| ⬜ \|').Count  # muss 0 sein
```

Dann in [../progress.md](../progress.md) die Summenzeile finalisieren und in
[../findings.md](../findings.md) eine Gesamt-Zusammenfassung nach Schweregrad ergänzen.
