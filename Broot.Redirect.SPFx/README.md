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

## Status und Kommentare

Jeder Eintrag führt einen Status (`brtqmStatus`, [ADR-004](../docs/technical/adr/ADR-004-status-pro-eintrag-liste-redirect-regeln.md), US-13.004): «vorbereitet», «IIS-Umleitung offen», «Testing», «Fehlerhaft» oder «aktiv».

- Standardwert der Liste ist «vorbereitet»; er greift für Einträge aus dem WebPart, direkt aus der Liste und aus dem Import-Skript. Ein leerer oder unbekannter Wert gilt überall als «vorbereitet».
- Im WebPart hat jede Quell-URL ein Auswahlfeld für den Status; «Status für alle Quell-URLs» oberhalb setzt ihn für alle Zeilen des Bereichs. Statusänderungen werden mit «Speichern» geschrieben, mit denselben Prüfungen wie die übrigen Änderungen. Unberührte Einträge ohne Status bleiben leer.
- Kommentare sind die SharePoint-Kommentare des Listeneintrags. Jede gespeicherte Quell-URL zeigt ein Kommentar-Symbol mit der Anzahl; ein Klick öffnet ein Seitenpanel zum Lesen und Schreiben. Kommentare werden sofort gesendet, unabhängig von «Speichern», und als Klartext ohne Erwähnungen geschrieben.
- Sind Kommentare für die Liste oder den Mandanten deaktiviert, meldet das Panel dies; Status, Speichern und Export bleiben nutzbar.

## CSV-Export

Die Schaltfläche «Als CSV exportieren» lädt die Liste «Redirect Regeln» als `Redirect-Regeln_JJJJ-MM-TT.csv` herunter, für alle Benutzer mit Lesezugriff auf die Liste. Die Einträge werden seitenweise gelesen (auch über 5'000) und nach Bereich, danach nach Listeneintrags-ID sortiert. Einträge mit Status «vorbereitet» oder ohne Status werden nicht exportiert.

- Variante «Vollständig»: Spalten `Bereich`, `Quell-URL`, `Ziel-URL`, `Status`, `Geändert am` (lokale Zeit, `TT.MM.JJJJ HH:MM`), `Geändert von`, `UniqueId` (Identifier für den Abgleich, US-13.003)
- Variante «Nur Quell-URLs»: eine Spalte `Quell-URL`, Datei `Redirect-Regeln_Quell-URLs_JJJJ-MM-TT.csv`
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

   Das Skript legt Liste, Felder (feste Feld-GUIDs), Indizes und Standardansicht nur an, wenn sie fehlen. Es verändert keine Einträge und keine bestehenden Felder; Abweichungen wie ein anderer Feldtyp, eine abweichende Wertemenge oder ein anderer Standardwert des Status werden als Fehler gemeldet. Wiederholtes Ausführen ist unbedenklich. Bei einem Update auf eine neue Version (z. B. für das Feld `brtqmStatus`) das Skript vor dem Hochladen des Pakets erneut ausführen; neu angelegte Felder werden der Standardansicht hinzugefügt.

2. Paket bauen:

   ```bash
   npx gulp bundle --ship
   npx gulp package-solution --ship
   ```

3. `sharepoint/solution/broot-redirect-spfx.sppkg` in den App Catalog der QM-Pro-Site hochladen und das WebPart auf einer Seite der Site einbinden.

Das WebPart verwendet immer die Liste der Site Collection, auf der es eingebunden ist. Fehlt die Liste, meldet es «Liste nicht bereitgestellt» und legt keine an.
