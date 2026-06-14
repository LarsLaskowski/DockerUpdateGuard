# Review-Fortschritt

Phasen-Status auf einen Blick. Quelle der Wahrheit für *Dateien* bleibt die
[Review-Matrix](file-inventory.md); diese Tabelle ist die Phasen-Übersicht.

Status: ⬜ nicht begonnen · 🔬 in Arbeit · ✅ abgeschlossen

| Phase | Umfang | Dateien | Prompt | Status | Reviewt | Befunde |
|-------|--------|--------:|--------|--------|--------:|--------:|
| **P1** | Docker / DockerHub / Portainer / Configuration | 33 | [prompts/phase-1-docker-security.md](prompts/phase-1-docker-security.md) | ✅ | 33/33 | 12 |
| **P2** | Vulnerabilities | 18 | [prompts/phase-2-vulnerabilities.md](prompts/phase-2-vulnerabilities.md) | ✅ | 18/18 | 8 |
| **P3** | Images | 60 | [prompts/phase-3-images.md](prompts/phase-3-images.md) | ✅ | 60/60 | 11 |
| **P4** | Data (EF Core) | 65 | [prompts/phase-4-data.md](prompts/phase-4-data.md) | ✅ | 65/65 | 1 |
| **P5** | Components / UI / wwwroot | 59 | [prompts/phase-5-ui.md](prompts/phase-5-ui.md) | ✅ | 59/59 | 5 |
| **P6** | Host / Telemetry / Infrastructure | 17 | [prompts/phase-6-host-telemetry.md](prompts/phase-6-host-telemetry.md) | ✅ | 17/17 | 6 |
| **P7** | Tests | 52 | [prompts/phase-7-tests.md](prompts/phase-7-tests.md) | ✅ | 52/52 | 5 |
| **P8** | Root / Konfiguration / Doku | 28 | [prompts/phase-8-config-docs.md](prompts/phase-8-config-docs.md) | ✅ | 28/28 | 8 |
| | **Summe** | **332** | | | **332/332** | **56** |

> **P8-Hinweis:** Die ursprüngliche Liste umfasste 26 Dateien inkl. `azure-pipelines.yml`,
> die im Zuge der CI/CD-Migration (Commit fc81f4f) **gelöscht** wurde. Die real
> wirksamen Workflows `.github/workflows/ci.yml` + `release.yml` fehlten in der Matrix
> und wurden als Zeilen `4a`/`4b` ergänzt → P8 zählt jetzt 28 Matrix-Zeilen (27 reale
> Dateien + die als „entfernt" markierte Azure-Zeile). Details: F-036.
>
> **Gesamt-Review abgeschlossen:** alle Phasen P1–P8 sind ✅, keine ⬜-Zeile mehr in
> der [Matrix](file-inventory.md) (332/332 reviewt). **Schweregrad-Gesamtzusammenfassung
> über alle 56 Befunde: 0 🔴 · 21 🟠 · 28 🟡 · 7 🔵.** Kein 🔴-Befund — der Sicherheits-
> und Korrektheitskern ist solide; die 🟠-Befunde betreffen Resilienz/Härtung
> (Retry/Backoff fehlt, Scan-Batch-Abbruch, transienter Finding-Verlust),
> Korrektheits-Randfälle (SemVer-Pre-Release/Cross-Year, Scout-Severity/Registry) und
> Test-Lücken (Provider-/Parser-/Resilienz-Pfade, Test-DB-Treue).
>
> **P7-Hinweis:** Befund-IDs sind über die Phasen hinweg nicht global eindeutig
> (P3/P4 teilen sich `F-021`; P5/P8 teilen sich `F-032`–`F-036`). Phase 7 nutzt daher
> bewusst den kollisionsfreien Bereich **F-046–F-050**.

## Abschlussbedingung Gesamt-Review

Erledigt, wenn in der [Matrix](file-inventory.md) **keine ⬜-Zeile** mehr steht
(zählbar) und jede Phase oben auf ✅.

Schnellzählung (PowerShell):

```powershell
# offene Zeilen gesamt
(Select-String -Path docs/review/file-inventory.md -Pattern '\| ⬜ \|').Count
# offene Zeilen einer Phase, z. B. P1
(Select-String -Path docs/review/file-inventory.md -Pattern '\| P1 \|.*\| ⬜ \|').Count
```
