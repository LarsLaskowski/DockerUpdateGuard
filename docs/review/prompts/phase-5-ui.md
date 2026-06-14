# Phase 5 — Components / UI / wwwroot (Blazor)

> **Start in neuem Chat:** „Lies und führe `docs/review/prompts/phase-5-ui.md` aus."

## Kontext

Die Präsentationsschicht: Razor-Components, UI-Hilfslogik und statische Assets.
Dashboard, Observed Images, Runtime Containers, Docker Instances, Shared Base
Images, Scan History.

**Umfang: 59 Dateien.** Module: `App/Components` (31), `App/UI` (24),
`App/wwwroot` (4).

## Deine Dateiliste (autoritativ aus der Matrix)

```powershell
Select-String -Path docs/review/file-inventory.md -Pattern '\| P5 \|' |
  ForEach-Object { ($_.Line -split '\|')[3].Trim().Trim('`') }
```

## Kriterien

Lies [../criteria.md](../criteria.md). Besonderer Fokus:

- **K3 Sicherheit:** XSS – ungeprüfte `MarkupString`/`@((MarkupString))`-Nutzung,
  Rendern von Registry-/Container-Strings ohne Encoding.
- **K2 Architektur:** Greifen Components direkt auf den DbContext/HTTP-Clients zu
  statt über Services? Trennung Markup ↔ Logik. Sinnvolle Component-Lebensdauer,
  `IDisposable`/`await using` bei Subscriptions.
- **K1 Korrektheit:** Render-Logik, Null-/Leerzustände, Lade-/Fehlerzustände in der
  UI, korrekte Status-/Ampel-Darstellung.
- **K9 Konventionen:** Razor-Stil konsistent; `@code`-Blöcke schlank.

## Workflow

1. **Triage** aller 59 Dateien → Ampeln + Status in [../file-inventory.md](../file-inventory.md).
   CSS/SVG unter `wwwroot` meist schnell ✅ (Sicher./Korrekt. = —).
2. **Deep-Dive** für 🔬 → Befunde `F-NNN` in [../findings.md](../findings.md)
   (Abschnitt „Phase 5"), verlinken, Status ✅.
3. [../progress.md](../progress.md) Zeile P5 aktualisieren.

## Abschlussbedingung

```powershell
(Select-String -Path docs/review/file-inventory.md -Pattern '\| P5 \|.*\| ⬜ \|').Count  # muss 0 sein
```
