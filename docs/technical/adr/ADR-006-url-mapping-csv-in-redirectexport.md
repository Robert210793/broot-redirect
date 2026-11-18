# ADR-006: URL-Mapping-CSV in der Bibliothek «RedirectExport» — gemeinsamer Vertrag für Function App und WebPart

## Status
Accepted — 2026-11-18

## Kontext
US-13.006 verlangt ein URL-Mapping-CSV für das lokale IIS-Rewrite des Kunden im Format der Kundenvorlage (F1), unter dem festen Namen `url-mapping.csv` (F2), täglich erzeugt (F3) und zusätzlich manuell aus dem WebPart «Redirect Regeln» auslösbar. Bei jedem Fehler muss die bisherige Datei erhalten bleiben (F4). Die tägliche Erzeugung braucht eine zeitgesteuerte Komponente mit Zugriff auf die QM-Pro-Site; das ist nur die QM-Pro-Function-App, die bereits die Liste «Redirect Regeln» liest (ADR-003). Das WebPart läuft im Browser mit den Rechten des Benutzers; ein Aufruf der Function App aus dem Browser bräuchte ein neues Authentifizierungsverfahren. SharePoint kennt kein atomares Umbenennen mit Ersetzen.

## Entscheidung
1. **Erzeugung:** täglich durch die Timer-Funktion `brtqmRedirectUrlMappingExport` der QM-Pro-Function-App (Admin-Identität); manuell im SPFx-WebPart über PnPjs mit den Rechten des Benutzers. Beide setzen denselben Vertrag um; es gibt keinen Aufruf zwischen den Komponenten.
2. **Inhalt:** Kopfzeile `source-url`, je Zeile eine Quell-URL unverändert, nur Leerraum am Anfang und Ende entfernt; leere und mehrzeilige Werte werden übersprungen und gezählt; Filter nach dem exakten Statustext wie ADR-004 (leer, unbekannt und «vorbereitet» entfallen); Reihenfolge nach Listeneintrags-ID.
3. **Format:** UTF-8 ohne BOM, CRLF zwischen den Zeilen, kein abschliessendes CRLF; ohne Einträge nur die Kopfzeile.
4. **Ablage:** `{Site}/RedirectExport/url-mapping.csv`; die Bibliothek wird nicht angelegt, ihr Fehlen ist ein Fehler; der Ordner «Archiv» wird bei Bedarf angelegt.
5. **Ablauf beim Ersetzen:** Liste lesen und CSV bilden; Bibliothek prüfen; Inhalt der bestehenden `url-mapping.csv` als `Archiv/url-mapping_JJJJ-MM-TT_HHMM[_n].csv` ablegen (Schweizer Ortszeit aus `TimeLastModified`, ohne Überschreiben); danach `url-mapping.csv` an Ort und Stelle überschreiben, bei Fehler die Archivkopie wieder entfernen; erst danach die übrigen `url-mapping*.csv` des Stammverzeichnisses ohne Überschreiben ins Archiv verschieben (404 gilt als erledigt, andere Fehler ergeben einen Teilerfolg).
6. **Dateizugriffe:** in beiden Implementierungen über dieselben REST-Endpunkte (`AddUsingPath` mit bzw. ohne Überschreiben, `MoveFileByPath` ohne Überschreiben).
7. **Sperre:** kein Distributed Lock; der Ablauf überschreibt nie Archivdateien und verliert bei parallelen Läufen keine Daten.
8. **Konfiguration:** Bibliothekspfad `RedirectExport` als Konstante auf beiden Seiten; Function App mit `RedirectUrlMappingEnabled` und `RedirectUrlMappingSchedule` (NCRONTAB in `WEBSITE_TIME_ZONE`, Standard 06:00).

## Verworfene Alternativen
- HTTP-Funktion, vom WebPart über `AadHttpClient` aufgerufen: verlangt Easy Auth, App-Registrierung und mandantenweite API-Freigabe — ein neues Authentifizierungsverfahren der Function App; ein Function Key im Browser wäre ein offengelegtes Secret.
- Auslöser-Eintrag in SharePoint, von der Function App abgeholt: neue Liste als Vertrag, Polling, nur asynchrone Rückmeldung an den Benutzer.
- Erzeugung in broot-redirect: kein SharePoint-Zugriff, neue Zugangsdaten nötig.
- Erst verschieben, dann hochladen: zeitweise keine Datei, Verlust bei Fehler (widerspricht F4).
- Temporäre Datei mit Move-Overwrite: Atomarität nicht dokumentiert, neue Element-ID, Versionsverlauf geht verloren.
- Datum und Uhrzeit in jedem Dateinamen: in F2 verworfen.
- Sortierung nach Bereich: kulturabhängiger Vergleich, in TypeScript und PowerShell nicht sicher gleich.

## Konsequenzen
- Der Vertrag existiert zweimal (TypeScript und PowerShell); jede Änderung braucht einen abgestimmten Release beider Repos und gespiegelte Testfälle in Jest und Pester.
- Manuell erzeugen kann nur, wer in «RedirectExport» Mitwirken-Rechte hat; ohne diese Rechte meldet das WebPart einen Fehler.
- Der Versionsverlauf von `url-mapping.csv` wächst täglich.
- Restrisiken: doppelte Archivkopie bei Abbruch zwischen Archivieren und Ersetzen oder bei parallelen Läufen; verschobener Zeitstempel nach einer Bearbeitung der Metadaten von Hand; auf Linux- und Flex-Consumption-Plänen gilt der Zeitplan in UTC.
- Eine Umbenennung von Statuswerten (ADR-004) betrifft auch beide Implementierungen dieses Vertrags.
- broot-redirect selbst ändert sich nicht.
