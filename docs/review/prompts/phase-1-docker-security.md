# Phase 1 — Docker / DockerHub / Portainer / Configuration (Sicherheitskern)

> **Start in neuem Chat:** „Lies und führe `docs/review/prompts/phase-1-docker-security.md` aus."

## Kontext

DockerUpdateGuard ist eine ASP.NET-Core-Razor-Components-App, die Docker-Laufzeit
überwacht und mit Registry-Metadaten vergleicht. Diese Phase prüft den
**Sicherheitskern**: Anbindung an Docker-Engines, Docker Hub / OCI-Registries,
Portainer und die zugehörige Konfiguration. Hier liegen Credentials, TLS-Optionen
und Socket-Zugriffe – höchstes Risiko, daher Phase 1.

**Umfang: 33 Dateien.** Module: `App/Docker` (7), `App/DockerHub` (7),
`App/Portainer` (10), `App/Configuration` (9).

## Deine Dateiliste (autoritativ aus der Matrix)

```powershell
Select-String -Path docs/review/file-inventory.md -Pattern '\| P1 \|' |
  ForEach-Object { ($_.Line -split '\|')[3].Trim().Trim('`') }
```

Diese Liste ist verbindlich – **jede** dieser Dateien muss am Ende Status ✅ haben.

## Kriterien

Lies [../criteria.md](../criteria.md). Bewerte je Datei die vier Schwerpunkte
(Sicher./Korrekt./Arch./Tests). In dieser Phase mit besonderem Fokus auf:

- **K3 Sicherheit:** DockerHub `Pat` & Portainer `Password`/`ApiToken` – nie ins Log,
  nicht in Exceptions/Telemetrie. `SkipCertificateValidation`/`UseTls`-Pfade prüfen
  (kein stiller TLS-Bypass). Docker-Socket-Zugriff (`unix://`, `npipe://`). SSRF
  über konfigurierbare `BaseUrl`. Auth-Header-Aufbau für Registry/Portainer.
- **K4 Resilienz:** `RequestTimeoutSeconds` real angewandt? `HttpClient`-Lebensdauer
  (IHttpClientFactory vs. `new HttpClient`)? Fehlerpfade bei nicht erreichbarer
  Engine/Registry/Portainer – sauber behandelt statt geschluckt?
- **K10 Konfiguration:** Options-Validatoren vorhanden & korrekt (Pflichtfelder bei
  aktiviertem Portainer: `ApiToken` ODER `Username`+`Password`). Defaults gegen
  README prüfen.

## Workflow

1. **Triage:** Jede Datei der Liste öffnen, in [../file-inventory.md](../file-inventory.md)
   die Ampeln setzen, Status → ✅ (oder 🔬 bei 🟡/🔴).
2. **Deep-Dive** für 🔬-Dateien: zeilengenau. Für jeden Befund einen Eintrag
   `F-NNN` in [../findings.md](../findings.md) (Abschnitt „Phase 1") nach der dortigen
   Vorlage anlegen; ID in der Matrix-Spalte *Befunde* eintragen. Danach Status ✅.
3. **Fortschritt** in [../progress.md](../progress.md) (Zeile P1: Status, Reviewt-Zähler,
   Befundzahl) aktualisieren.

## Projektkonventionen (beim Bewerten von K9 beachten)

file-scoped Namespaces, ein Top-Level-Typ je Datei, `#region`-Blöcke, XML-Docs auf
allen Membern, Allman-Braces, `var` bevorzugt, kein `this.`, `== false` statt `!`,
Reihitsu-Analyzer, net10.0. **Keine** unrelated Änderungen am Produktivcode in
dieser Phase – nur Review/Doku. Falls Fixes gewünscht: separat nach Freigabe.

## Abschlussbedingung Phase 1

```powershell
(Select-String -Path docs/review/file-inventory.md -Pattern '\| P1 \|.*\| ⬜ \|').Count  # muss 0 sein
```

Wenn 0: P1 in [../progress.md](../progress.md) auf ✅ setzen und Befunde
zusammenfassen.
