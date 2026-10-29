# Broot.Redirect.SPFx

SPFx-Solution mit dem WebPart «Redirect Regeln» für die QM-Pro-Site (US-13.002). Das WebPart erfasst Bereiche mit einer Ziel-URL und beliebig vielen Quell-URLs in der SharePoint-Liste «Redirect Regeln» (`Lists/brtqmRedirectRules`). Schema, Identifier und normalisierter Schlüssel der Liste: [ADR-003](../docs/technical/adr/ADR-003-liste-redirect-regeln-als-schnittstelle.md).

## Aufbau

```
src/webparts/brtqmRedirectRules/
├── BrtqmRedirectRulesWebPart.ts     Einstieg: PnPjs-Instanz für die Site Collection, Service und Coordinator
├── components/                      React-Komponenten ohne Geschäftslogik (Fluent UI v8)
├── services/
│   ├── RedirectRulesService.ts      einzige Klasse mit PnPjs-Aufrufen (Lesen seitenweise, Batches à 100)
│   ├── AreaSaveCoordinator.ts       Ablauf beim Speichern: Eingaben, Recht, gleichzeitige Änderung, Dubletten, Ausführung
│   └── FileDownload.ts              Herunterladen einer erzeugten Datei im Browser
├── logic/                           reine Module: Schlüssel, URL-Prüfung, Bereichsname, Änderungsplanung, Konfliktprüfung, CSV-Export
├── types/                           gemeinsame Typen
└── loc/                             Texte (de-de primär, en-us als Fallback)
provisioning/
└── Initialize-BrtqmRedirectRulesList.ps1   idempotente Bereitstellung der Liste
tests/unit/                          Jest-Tests (Logik und Service gegen eine In-Memory-Liste)
```

## CSV-Export

Die Schaltfläche «Als CSV exportieren» lädt die ganze Liste «Redirect Regeln» als `Redirect-Regeln_JJJJ-MM-TT.csv` herunter, für alle Benutzer mit Lesezugriff auf die Liste. Die Einträge werden seitenweise gelesen (auch über 5'000) und nach Bereich, danach nach Listeneintrags-ID sortiert.

- Spalten: `Bereich`, `Quell-URL`, `Ziel-URL`, `Geändert am` (lokale Zeit, `TT.MM.JJJJ HH:MM`), `Geändert von`, `UniqueId` (Identifier für den Abgleich, US-13.003)
- Format für Excel mit Schweizer Einstellungen: Semikolon als Trennzeichen, UTF-8 mit BOM, Zeilenende CRLF; Felder mit `;`, `"` oder Zeilenumbruch in Anführungszeichen
- Werte, die mit `=`, `+`, `-` oder `@` beginnen, erhalten ein vorangestelltes `'`, damit Excel sie nicht als Formel ausführt (wie der Export von broot-redirect)

## Entwicklung

Voraussetzung: Node.js 22.

```bash
npm install
npm test
npx gulp serve --nobrowser
```

## Auslieferung

1. Liste bereitstellen (Besitzerrechte auf der QM-Pro-Site, PnP.PowerShell 2.x, PowerShell 7.2+):

   ```powershell
   ./provisioning/Initialize-BrtqmRedirectRulesList.ps1 -SiteUrl https://<tenant>.sharepoint.com/sites/<qm-pro> -ClientId <app-id>
   ```

   Das Skript legt Liste, Felder (feste Feld-GUIDs), Indizes und Standardansicht nur an, wenn sie fehlen. Es verändert keine Einträge und keine bestehenden Felder; Abweichungen wie ein anderer Feldtyp werden als Fehler gemeldet. Wiederholtes Ausführen ist unbedenklich.

2. Paket bauen:

   ```bash
   npx gulp bundle --ship
   npx gulp package-solution --ship
   ```

3. `sharepoint/solution/broot-redirect-spfx.sppkg` in den App Catalog der QM-Pro-Site hochladen und das WebPart auf einer Seite der Site einbinden.

Das WebPart verwendet immer die Liste der Site Collection, auf der es eingebunden ist. Fehlt die Liste, meldet es «Liste nicht bereitgestellt» und legt keine an.
