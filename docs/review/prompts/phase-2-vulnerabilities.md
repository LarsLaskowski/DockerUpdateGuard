# Phase 2 — Vulnerabilities (externe Scan-Provider)

> **Start in neuem Chat:** „Lies und führe `docs/review/prompts/phase-2-vulnerabilities.md` aus."

## Kontext

Diese Phase prüft die Vulnerability-Integration: Anbindung externer Provider
(`None`, `DockerScout`, `Trivy`), Abruf, Parsing und Persistenz von
Schwachstellen-Daten. Externer Netzwerk-Input + Konfig-gesteuerte Provider-Wahl.

**Umfang: 18 Dateien.** Modul: `App/Vulnerabilities`.

## Deine Dateiliste (autoritativ aus der Matrix)

```powershell
Select-String -Path docs/review/file-inventory.md -Pattern '\| P2 \|' |
  ForEach-Object { ($_.Line -split '\|')[3].Trim().Trim('`') }
```

## Kriterien

Lies [../criteria.md](../criteria.md). Besonderer Fokus:

- **K3/K4 Sicherheit & Resilienz:** `TrivyBaseUrl` als externer Endpunkt (SSRF,
  Timeout, TLS), robustes Verhalten bei Provider-Ausfall/Teilantwort, kein
  Credential-/Token-Leak in Logs.
- **K1 Korrektheit:** Parsing der Provider-Antworten (Severity-Mapping, fehlende
  Felder, Versions-/CVE-Zuordnung). Verhalten bei `Provider=None`/`Enabled=false`.
- **K5 Async:** `CancellationToken` durchgereicht, Parallelität begrenzt.
- **K8 Tests:** Sind alle drei Provider-Pfade getestet (auch Fehler-/Leerfälle)?

## Workflow

1. **Triage** aller 18 Dateien → Ampeln + Status in [../file-inventory.md](../file-inventory.md).
2. **Deep-Dive** für 🔬 → Befunde `F-NNN` in [../findings.md](../findings.md)
   (Abschnitt „Phase 2"), in Matrix verlinken, Status ✅.
3. [../progress.md](../progress.md) Zeile P2 aktualisieren.

## Projektkonventionen

Siehe [../criteria.md](../criteria.md) (K9). Nur Review/Doku, keine unrelated
Code-Änderungen ohne Freigabe.

## Abschlussbedingung

```powershell
(Select-String -Path docs/review/file-inventory.md -Pattern '\| P2 \|.*\| ⬜ \|').Count  # muss 0 sein
```
