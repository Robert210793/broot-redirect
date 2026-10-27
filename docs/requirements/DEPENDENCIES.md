# Abhängigkeiten — Epics und User Stories

## Epic-Abhängigkeitsdiagramm

```
Epic 11 — Admin-Zugang & Sicherheit
├─► Epic 05 — Regelverwaltung
│   ├─► Epic 03 — Regel-Matching (nutzt Epic 12)
│   │   ├─► Epic 04 — URL-Transformation (nutzt Epic 10)
│   │   │   ├─► Epic 06 — Globale Regeln
│   │   │   └─► Epic 08 — Validierung
│   │   └─► Epic 01 — Weiterleitung alter Links (nutzt Epic 04, 09, 10)
│   │       ├─► Epic 09 — Tracking & Statistiken (nutzt Epic 01)
│   │       └─► Epic 02 — Info-Seite (nutzt Epic 03, 05, 09, 10, 11)
│   ├─► Epic 07 — Import & Export
│   └─► Epic 13 — QM-Pro-Integration (nutzt Epic 01, 03)
└─► Epic 10 — Laufzeit-Einstellungen (nutzt Epic 12)

Epic 12 — Betrieb
├─► Epic 03 — Regel-Matching
└─► Epic 10 — Laufzeit-Einstellungen
```

Epic 01 und Epic 09 hängen gegenseitig voneinander ab: Die automatische Weiterleitung (US-01.001) schreibt Tracking-Einträge, das Erfassen von Aufrufen (US-09.001) setzt die Auflösung durch die Info-Seite (US-01.002) voraus. Auf Story-Ebene ist die Kette zyklenfrei.

### Wichtigste Epic-Vorbedingungen

| Epic | Benötigt zwingend | Nutzt optional |
|------|-------------------|----------------|
| 01 – Weiterleitung alter Links | 03, 04, 09, 10 | 05, 11, 12 |
| 02 – Info-Seite | 01, 03, 05, 09, 10, 11 | 04, 12 |
| 03 – Regel-Matching | 05, 12 | 11 |
| 04 – URL-Transformation | 03, 05, 10 | 11, 12 |
| 05 – Regelverwaltung | 11 | — |
| 06 – Globale Regeln | 04, 11 | 03, 05, 10, 12 |
| 07 – Import & Export | 05 | 11 |
| 08 – Validierung | 03, 04 | 05, 10, 11, 12 |
| 09 – Tracking & Statistiken | 01, 11 | 03, 04, 05, 10, 12 |
| 10 – Laufzeit-Einstellungen | 11, 12 | — |
| 11 – Admin-Zugang & Sicherheit | — | — |
| 12 – Betrieb | — | — |
| 13 – QM-Pro-Integration | 01, 03, 05, 11 | 07 |

---

## Vollständige US-Abhängigkeitstabelle

| US | Titel (Kurzform) | Benötigt |
|----|-----------------|----------|
| US-01.001 | Automatische Weiterleitung bei Regel mit Auto-Redirect | US-03.006, US-04.001, US-09.001 |
| US-01.002 | Info-Seite als Standard statt direkter Weiterleitung | US-03.006 |
| US-01.003 | Verhalten bei fehlendem Treffer | US-10.001 |
| US-01.004 | Smart Search bei fehlendem Treffer | US-01.003, US-10.003 |
| US-01.005 | System- und Admin-Pfade von der Weiterleitung ausnehmen | — |
| US-02.001 | Alte und neue URL auf der Info-Seite anzeigen | US-01.002, US-10.002 |
| US-02.002 | Neue URL kopieren oder in neuem Tab öffnen | US-02.001 |
| US-02.003 | Match-Qualität auf der Info-Seite anzeigen | US-03.007, US-10.002 |
| US-02.004 | Hinweistext mit klickbaren Links anzeigen | US-02.001, US-05.002, US-10.002 |
| US-02.005 | Feedback zur Weiterleitung geben | US-02.002, US-09.001, US-10.001 |
| US-02.006 | Zugang zum Admin-Panel von der Info-Seite | US-11.001, US-11.002 |
| US-03.001 | Teilweise Regeln (Präfix-Abgleich) auf Pfadabschnitte anwenden | US-05.002 |
| US-03.002 | Wildcard-Regeln (exakter Pfad) anwenden | US-05.002, US-03.005 |
| US-03.003 | Domain-Regeln auf den Hostnamen anwenden | US-05.002 |
| US-03.004 | Regex-Regeln für Sonderfälle anwenden | US-05.002 |
| US-03.005 | Query-Parameter beim Matching abgleichen | — |
| US-03.006 | Beste passende Regel auswählen | US-03.001, US-03.002, US-03.003, US-03.004, US-12.002 |
| US-03.007 | Match-Qualität und Ampelstufe berechnen | US-03.006 |
| US-03.008 | Matching über die Startkonfiguration steuern | — |
| US-04.001 | Ziel-URL nach Regeltyp aufbauen | US-03.006, US-10.001 |
| US-04.002 | Suchen und Ersetzen pro Regel | US-04.001, US-05.002 |
| US-04.003 | Query-Parameter der alten URL weitergeben, verwerfen oder filtern | US-04.001, US-05.002 |
| US-04.004 | Statische Query-Parameter anhängen | US-04.003, US-05.002 |
| US-05.001 | Regelliste anzeigen, durchsuchen und sortieren | US-11.005 |
| US-05.002 | Regel anlegen und bearbeiten | US-05.001 |
| US-05.003 | Regeleingaben validieren | US-05.002 |
| US-05.004 | Überlappende Matcher verhindern | US-05.002 |
| US-05.005 | Einzelne oder mehrere Regeln löschen | US-05.001 |
| US-05.006 | Alle Regeln löschen | US-05.001 |
| US-06.001 | Globale Such-und-Ersetzen-Regeln verwalten | US-11.005 |
| US-06.002 | Globale Regeln auf jede Ziel-URL anwenden | US-06.001, US-04.002 |
| US-07.001 | Import-Vorschau anzeigen | US-07.004, US-05.003 |
| US-07.002 | Import mit Fortschrittsanzeige ausführen | US-07.001, US-05.003 |
| US-07.003 | Wiederholter Import aktualisiert statt dupliziert | US-07.002, US-07.005 |
| US-07.004 | Tolerante Spaltenzuordnung beim Import | — |
| US-07.005 | Regeln als JSON, CSV oder XLSX exportieren | US-05.001 |
| US-08.001 | URL-Liste gegen das Regelwerk prüfen | US-03.006, US-04.001 |
| US-08.002 | Transformation Schritt für Schritt nachvollziehen | US-08.001 |
| US-09.001 | Aufrufe und Feedback erfassen | US-01.002 |
| US-09.002 | Kennzahlen und meistgenutzte Regeln anzeigen | US-09.001, US-11.005 |
| US-09.003 | Feedback-Trend über die Zeit anzeigen | US-09.001 |
| US-09.004 | Tracking-Einträge filtern und exportieren | US-09.001 |
| US-09.005 | Tracking-Daten löschen und automatisch aufräumen | US-09.001 |
| US-10.001 | Weiterleitungsverhalten und Schalter zur Laufzeit einstellen | US-11.005, US-12.002 |
| US-10.002 | Texte und Anzeigemodus der Info-Seite anpassen | US-10.001 |
| US-10.003 | Smart Search konfigurieren | US-10.001 |
| US-11.001 | Mit dem Admin-Passwort anmelden | US-11.003 |
| US-11.002 | Abmelden und Anmeldestatus prüfen | US-11.001 |
| US-11.003 | IP nach wiederholten Fehlversuchen sperren | — |
| US-11.004 | Blockierte IPs verwalten | US-11.003, US-11.005 |
| US-11.005 | Admin-Endpunkte vor unberechtigtem Zugriff schützen | US-11.001 |
| US-11.006 | Anfragen pro IP begrenzen | — |
| US-11.007 | Schreibende API-Anfragen gegen CSRF schützen | US-11.005 |
| US-12.001 | Betriebszustand über einen Health-Endpunkt melden | US-12.002 |
| US-12.002 | Daten beim Start in den Cache laden | — |
| US-12.003 | Installierte Version anzeigen | US-12.001 |
| US-13.001 | Regeln aus QM Pro schreiben und deaktivieren | US-01.002, US-03.002, US-05.001, US-05.002, US-05.003, US-05.004, US-05.005, US-05.006, US-07.002, US-07.003, US-07.005, US-08.001, US-11.005, US-11.006, US-11.007 |
| US-13.002 | Regeln pro Bereich in einem SPFx-WebPart erfassen | — |
| US-13.003 | Regeln aus der Liste «Redirect Regeln» periodisch abgleichen | US-13.002, US-13.001, US-05.004, US-03.001, US-03.002, US-03.006 |

---

## Empfohlene Implementierungsreihenfolge

Die Stories der Bestandsaufnahme sind bereits umgesetzt. Die Reihenfolge zeigt, in welcher Abfolge das System aufeinander aufbaut — hilfreich für die Einarbeitung und für die Reihenfolge, in der fehlende Tests ergänzt werden.

### Phase 1 — Grundlagen, Betrieb und Sicherheit
1. US-01.005 System- und Admin-Pfade von der Weiterleitung ausnehmen
2. US-03.005 Query-Parameter beim Matching abgleichen
3. US-03.008 Matching über die Startkonfiguration steuern
4. US-07.004 Tolerante Spaltenzuordnung beim Import
5. US-11.003 IP nach wiederholten Fehlversuchen sperren
6. US-11.006 Anfragen pro IP begrenzen
7. US-12.002 Daten beim Start in den Cache laden
8. US-11.001 Mit dem Admin-Passwort anmelden
9. US-12.001 Betriebszustand über einen Health-Endpunkt melden
10. US-11.002 Abmelden und Anmeldestatus prüfen
11. US-11.005 Admin-Endpunkte vor unberechtigtem Zugriff schützen
12. US-12.003 Installierte Version anzeigen

### Phase 2 — Admin-Grundfunktionen und Einstellungen
1. US-02.006 Zugang zum Admin-Panel von der Info-Seite
2. US-05.001 Regelliste anzeigen, durchsuchen und sortieren
3. US-06.001 Globale Such-und-Ersetzen-Regeln verwalten
4. US-10.001 Weiterleitungsverhalten und Schalter zur Laufzeit einstellen
5. US-11.004 Blockierte IPs verwalten
6. US-11.007 Schreibende API-Anfragen gegen CSRF schützen
7. US-01.003 Verhalten bei fehlendem Treffer
8. US-05.002 Regel anlegen und bearbeiten
9. US-05.005 Einzelne oder mehrere Regeln löschen
10. US-05.006 Alle Regeln löschen
11. US-07.005 Regeln als JSON, CSV oder XLSX exportieren
12. US-10.002 Texte und Anzeigemodus der Info-Seite anpassen
13. US-10.003 Smart Search konfigurieren

### Phase 3 — Regeltypen und Matching
1. US-01.004 Smart Search bei fehlendem Treffer
2. US-03.001 Teilweise Regeln (Präfix-Abgleich) auf Pfadabschnitte anwenden
3. US-03.002 Wildcard-Regeln (exakter Pfad) anwenden
4. US-03.003 Domain-Regeln auf den Hostnamen anwenden
5. US-03.004 Regex-Regeln für Sonderfälle anwenden
6. US-05.003 Regeleingaben validieren
7. US-05.004 Überlappende Matcher verhindern
8. US-03.006 Beste passende Regel auswählen
9. US-07.001 Import-Vorschau anzeigen

### Phase 4 — Auflösung, Transformation und Import
1. US-01.002 Info-Seite als Standard statt direkter Weiterleitung
2. US-03.007 Match-Qualität und Ampelstufe berechnen
3. US-04.001 Ziel-URL nach Regeltyp aufbauen
4. US-07.002 Import mit Fortschrittsanzeige ausführen
5. US-02.001 Alte und neue URL auf der Info-Seite anzeigen
6. US-02.003 Match-Qualität auf der Info-Seite anzeigen
7. US-04.002 Suchen und Ersetzen pro Regel
8. US-04.003 Query-Parameter der alten URL weitergeben, verwerfen oder filtern
9. US-07.003 Wiederholter Import aktualisiert statt dupliziert
10. US-08.001 URL-Liste gegen das Regelwerk prüfen
11. US-09.001 Aufrufe und Feedback erfassen

### Phase 5 — Info-Seite, Weiterleitung und Auswertung
1. US-01.001 Automatische Weiterleitung bei Regel mit Auto-Redirect
2. US-02.002 Neue URL kopieren oder in neuem Tab öffnen
3. US-02.004 Hinweistext mit klickbaren Links anzeigen
4. US-04.004 Statische Query-Parameter anhängen
5. US-06.002 Globale Regeln auf jede Ziel-URL anwenden
6. US-08.002 Transformation Schritt für Schritt nachvollziehen
7. US-09.002 Kennzahlen und meistgenutzte Regeln anzeigen
8. US-09.003 Feedback-Trend über die Zeit anzeigen
9. US-09.004 Tracking-Einträge filtern und exportieren
10. US-09.005 Tracking-Daten löschen und automatisch aufräumen
11. US-02.005 Feedback zur Weiterleitung geben

### Phase 6 — Integrationen
1. US-13.001 Regeln aus QM Pro beim Publizieren schreiben und beim Zurückziehen deaktivieren
2. US-13.002 Regeln pro Bereich in einem SPFx-WebPart erfassen
3. US-13.003 Regeln aus der Liste «Redirect Regeln» periodisch abgleichen
