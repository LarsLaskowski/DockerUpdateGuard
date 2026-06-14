# Phase 3 — Images (Tag/Digest-Logik, Base-Image-Ketten)

> **Start in neuem Chat:** „Lies und führe `docs/review/prompts/phase-3-images.md` aus."

## Kontext

Größte Logik-Fläche der App: Auflösung von Tags/Digests, SemVer-Vergleich,
Alias-Tags (`latest`), abgeleitete Base-Runtime-Erkennung, Update-Bewertung und
geteilte Base-Image-Abhängigkeiten. Hier entstehen die Kern-Aussagen „aktuell /
veraltet / manuelle Prüfung".

**Umfang: 60 Dateien.** Modul: `App/Images` (inkl. Unterordner wie `Enums`).

## Deine Dateiliste (autoritativ aus der Matrix)

```powershell
Select-String -Path docs/review/file-inventory.md -Pattern '\| P3 \|' |
  ForEach-Object { ($_.Line -split '\|')[3].Trim().Trim('`') }
```

## Kriterien

Lies [../criteria.md](../criteria.md). Besonderer Fokus:

- **K1 Korrektheit:** SemVer-Parsing & -Ordnung, Digest-Vergleich, Alias-Auflösung
  (`latest` → konkrete Version bei gleichem Digest), `UpdateEvaluationStatus`-Logik,
  Randfälle (pre-release, fehlende Tags, mehrdeutige Digests).
- **K5 Async/Concurrency:** Parallelität bei Registry-Abfragen, Quota-/Request-Budget,
  `CancellationToken`.
- **K6 Datenzugriff:** wie `ObservedImage`/`ImageVersion`/`TagCandidate` geladen
  werden (N+1?), Tracking-Verhalten.
- **K7 Performance:** Heißpfade beim Massen-Scan, Allokationen, Caching.

> Wegen Größe (60 Dateien) ggf. in Teil-Sessions splitten – die Matrix hält den
> Fortschritt, du kannst jederzeit an der ersten ⬜-P3-Zeile weitermachen.

## Workflow

1. **Triage** → Ampeln + Status in [../file-inventory.md](../file-inventory.md).
2. **Deep-Dive** für 🔬 → Befunde `F-NNN` in [../findings.md](../findings.md)
   (Abschnitt „Phase 3"), verlinken, Status ✅.
3. [../progress.md](../progress.md) Zeile P3 aktualisieren.

## Abschlussbedingung

```powershell
(Select-String -Path docs/review/file-inventory.md -Pattern '\| P3 \|.*\| ⬜ \|').Count  # muss 0 sein
```
