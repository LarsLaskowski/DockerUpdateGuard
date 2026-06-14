# Phase 7 — Tests

> **Start in neuem Chat:** „Lies und führe `docs/review/prompts/phase-7-tests.md` aus."

## Kontext

Die beiden MSTest-Projekte. Diese Phase bewertet Testabdeckung und -qualität und
spiegelt sie gegen die in P1–P6 gefundenen Risiken (sind die kritischen Pfade
getestet?).

**Umfang: 52 Dateien.** Module: `Tests/DockerUpdateGuard.Tests` (46),
`Tests/DockerUpdateGuard.Data.Tests` (6).

## Deine Dateiliste (autoritativ aus der Matrix)

```powershell
Select-String -Path docs/review/file-inventory.md -Pattern '\| P7 \|' |
  ForEach-Object { ($_.Line -split '\|')[3].Trim().Trim('`') }
```

## Kriterien

Lies [../criteria.md](../criteria.md). Hauptachse **K8 Tests**:

- **Aussagekraft:** echte Assertions statt Smoke; testet der Test wirklich das
  Verhalten oder nur „läuft durch"?
- **Abdeckung:** sind Korrektheits-Kernpfade (Tag/Digest-Logik aus P3, Provider aus
  P2, Query-Services aus P4) abgedeckt? Fehlende Negativ-/Randfälle benennen.
- **Determinismus:** keine Flakiness über Zeit/Netz/Reihenfolge; Test-Doubles
  (HTTP-Handler-Helper) korrekt eingesetzt.
- **K9 Naming:** `{Feature}Tests` / `{Class}{Scenario}{ExpectedResult}`.

> Optional: SonarQube-Coverage-Skill (`sonar-coverage`) heranziehen, um
> Abdeckungslücken objektiv zu untermauern – Befunde dann ganz normal als `F-NNN`.

## Workflow

1. **Triage** aller 52 Dateien → Ampeln (v. a. Tests-Spalte) + Status in
   [../file-inventory.md](../file-inventory.md).
2. **Deep-Dive** für 🔬 → Befunde `F-NNN` in [../findings.md](../findings.md)
   (Abschnitt „Phase 7"), verlinken, Status ✅. Fehlende-Test-Lücken sind ebenfalls
   Befunde (Schweregrad i. d. R. 🟠).
3. [../progress.md](../progress.md) Zeile P7 aktualisieren.

## Abschlussbedingung

```powershell
(Select-String -Path docs/review/file-inventory.md -Pattern '\| P7 \|.*\| ⬜ \|').Count  # muss 0 sein
```
