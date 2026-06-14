# Phase 6 — Host / Telemetry / Infrastructure

> **Start in neuem Chat:** „Lies und führe `docs/review/prompts/phase-6-host-telemetry.md` aus."

## Kontext

App-Bootstrap und Querschnitt: `Program.cs`, DI-Verkabelung, Host-Logging,
Application-Initialisierung (inkl. automatischer EF-Migrations beim Start),
OpenTelemetry-Setup und Infrastructure-Helfer.

**Umfang: 17 Dateien.** Module: `App` (Root: Program/Application*/Host*/
ServiceCollectionExtensions), `App/Infrastructure` (2), `Telemetry` (10).

## Deine Dateiliste (autoritativ aus der Matrix)

```powershell
Select-String -Path docs/review/file-inventory.md -Pattern '\| P6 \|' |
  ForEach-Object { ($_.Line -split '\|')[3].Trim().Trim('`') }
```

## Kriterien

Lies [../criteria.md](../criteria.md). Besonderer Fokus:

- **K2 Architektur:** DI-Lifetimes korrekt (Singleton/Scoped/Transient), keine
  Captive-Dependencies; Reihenfolge der Pipeline/Middleware sinnvoll.
- **K4 Resilienz:** Verhalten der automatischen Migration beim Start (Fehlerfall,
  Race bei mehreren Instanzen); Background-Service-Registrierung robust.
- **K11 Observability:** Telemetrie-Namenskonventionen (`Telemetry*Names.cs`)
  konsistent genutzt; Validatoren (`TelemetryOptionsValidator`) korrekt; sinnvolle
  Trace-/Metric-Abdeckung kritischer Pfade; Log-Level angemessen.
- **K3 Sicherheit:** keine Secrets im Startup-Log; OTLP-Endpoint-Validierung.

## Workflow

1. **Triage** → Ampeln + Status in [../file-inventory.md](../file-inventory.md).
2. **Deep-Dive** für 🔬 → Befunde `F-NNN` in [../findings.md](../findings.md)
   (Abschnitt „Phase 6"), verlinken, Status ✅.
3. [../progress.md](../progress.md) Zeile P6 aktualisieren.

## Abschlussbedingung

```powershell
(Select-String -Path docs/review/file-inventory.md -Pattern '\| P6 \|.*\| ⬜ \|').Count  # muss 0 sein
```
