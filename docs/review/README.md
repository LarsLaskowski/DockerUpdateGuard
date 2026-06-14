# DockerUpdateGuard – Code-Review-Workspace

Selbsttragender Arbeitsbereich für das vollständige Repo-Review. Ziel: **alle 330
getrackten Dateien** entlang fester Kriterien bewerten, lückenlos nachverfolgbar.

## Dateien in diesem Ordner

| Datei | Zweck |
|-------|-------|
| [README.md](README.md) | Diese Übersicht / Einstieg |
| [criteria.md](criteria.md) | Bewertungskriterien K1–K12 + Schweregrade (Methodik) |
| [file-inventory.md](file-inventory.md) | **Review-Matrix** – alle 330 Dateien, Status & Ampeln je Schwerpunkt. Single Source of Truth. |
| [findings.md](findings.md) | Befundliste `F-NNN`, gruppiert nach Phase, mit Schweregrad |
| [progress.md](progress.md) | Phasen-Fortschritt auf einen Blick |
| [prompts/](prompts/) | Ein eigenständiger Start-Prompt je Phase (P1–P8) |

## So startest du eine Phase in einem **neuen Chat**

1. Öffne den passenden Prompt unter `prompts/phase-N-*.md`.
2. Schreib im neuen Chat: **„Lies und führe `docs/review/prompts/phase-1-docker-security.md` aus."**
   (oder kopier den Prompt-Inhalt direkt hinein).
3. Der Prompt ist self-contained: er verweist auf Kriterien, Matrix und Befundliste
   und enthält Dateiauswahl, Workflow und Abschlussbedingung. Ein kalter Start
   genügt – kein Wissen aus vorherigen Chats nötig.

## Phasen (Reihenfolge nach Risiko)

| Phase | Umfang | Dateien |
|-------|--------|--------:|
| P1 | Docker / DockerHub / Portainer / Configuration (Sicherheitskern) | 33 |
| P2 | Vulnerabilities (externe Provider) | 18 |
| P3 | Images (Tag/Digest-Logik, Base-Image-Ketten) | 60 |
| P4 | Data – EF Core (Entities, Configurations, Migrations, Queries, Repos) | 65 |
| P5 | Components / UI / wwwroot (Blazor) | 59 |
| P6 | Host / Telemetry / Infrastructure | 17 |
| P7 | Tests | 52 |
| P8 | Root / Konfiguration / Doku | 26 |

Phasen sind unabhängig und können in beliebiger Reihenfolge / parallel in
getrennten Chats laufen, da jede Datei zu **genau einer** Phase gehört.

## Arbeitsweise pro Datei (Triage → Deep-Dive)

1. **Triage:** Datei öffnen, Ampeln (Sicher./Korrekt./Arch./Tests) setzen, Status
   in der Matrix auf ✅ – oder bei 🟡/🔴 zunächst 🔬.
2. **Deep-Dive** nur für 🔬-Dateien: zeilengenaue Analyse; Befund als `F-NNN` in
   [findings.md](findings.md) anlegen und in der Matrix-Spalte *Befunde* verlinken.
3. Status nach Deep-Dive auf ✅. **Keine Datei bleibt ⬜.**

## Abschluss

Gesamt-Review fertig, wenn in der Matrix keine ⬜-Zeile mehr steht und in
[progress.md](progress.md) alle Phasen ✅ sind. Zählbefehle stehen in progress.md.
