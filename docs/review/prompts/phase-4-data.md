# Phase 4 — Data (EF Core: Entities, Configurations, Migrations, Queries, Repos)

> **Start in neuem Chat:** „Lies und führe `docs/review/prompts/phase-4-data.md` aus."

## Kontext

Die Datenschicht (`src/DockerUpdateGuard.Data`, EF Core / PostgreSQL):
Entities, Fluent-Configurations, Migrations, der DbContext, Repositories und
Query-Services. Fokus auf Modell-/Migrations-Konsistenz und Query-Effizienz.

**Umfang: 65 Dateien.** Module: `Data/Entities` (31), `Data/Configurations` (15),
`Data/Migrations` (9), `Data/Queries` (4), `Data/Repositories` (2) + Data-Root
(DbContext, ServiceCollectionExtensions, Design-Factory, csproj).

## Deine Dateiliste (autoritativ aus der Matrix)

```powershell
Select-String -Path docs/review/file-inventory.md -Pattern '\| P4 \|' |
  ForEach-Object { ($_.Line -split '\|')[3].Trim().Trim('`') }
```

## Kriterien

Lies [../criteria.md](../criteria.md). Besonderer Fokus:

- **K6 Datenzugriff:** Migrations konsistent zum aktuellen Modell (Snapshot vs.
  Entities/Configurations)? Indizes/Unique-Constraints für Lookup-Felder
  (Digests, Tags, Endpoints) vorhanden? Nullable/Required korrekt gemappt?
  Cascade-/Delete-Verhalten sinnvoll?
- **K1 Korrektheit:** Query-Services (`SharedBaseImageQueryService`) – liefern sie
  korrekte Aggregate? Randfälle (keine Treffer, Mehrfachnutzung).
- **K7 Performance:** `AsNoTracking` für Lesepfade, Projektionen statt
  Voll-Materialisierung, keine N+1.
- **K2 Architektur:** Data-Schicht ohne UI-/Host-Abhängigkeiten; DI-Registrierung
  (`ServiceCollectionExtensions`) mit korrekten Lifetimes.

## Workflow

1. **Triage** → Ampeln + Status in [../file-inventory.md](../file-inventory.md).
   Entities/Enums sind oft schnell ✅; Configurations/Queries/Migrations genauer.
2. **Deep-Dive** für 🔬 → Befunde `F-NNN` in [../findings.md](../findings.md)
   (Abschnitt „Phase 4"), verlinken, Status ✅.
3. [../progress.md](../progress.md) Zeile P4 aktualisieren.

## Abschlussbedingung

```powershell
(Select-String -Path docs/review/file-inventory.md -Pattern '\| P4 \|.*\| ⬜ \|').Count  # muss 0 sein
```
