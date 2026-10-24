# Anforderungen — broot-redirect

User Stories und Epics des Projekts. Die Stories der Bestandsaufnahme wurden am 2026-10-24 nachträglich aus dem Code abgeleitet und dokumentieren das Ist-Verhalten zu diesem Zeitpunkt.

- `[x]` — Akzeptanzkriterium umgesetzt und durch automatisierte Tests belegt
- `[ ]` — Akzeptanzkriterium umgesetzt, aber ohne automatisierten Test (Kandidat für neue Tests)
- Abweichungen zwischen Admin-Panel, Dokumentation und Code stehen als offene Fragen in den Stories (Übersicht unten).
- Die Stories der Bestandsaufnahme haben keine fachliche Freigabe und keine Abnahme. Neue Anforderungen entstehen über `/brt-ideate` bzw. `/brt-story`.

Weitere Dokumente: [DEPENDENCIES.md](DEPENDENCIES.md) · [DOMAIN-GLOSSARY.md](DOMAIN-GLOSSARY.md)

## Epics

| Nr | Epic | Beschreibung | Stories |
|----|------|--------------|---------|
| 01 | [Weiterleitung alter Links](01-weiterleitung-alter-links/README.md) | Entscheidet beim Aufruf einer alten URL, ob direkt weitergeleitet, die Info-Seite angezeigt oder ein Fallback (Standard-Domain, Smart Search) verwendet wird. | 5 |
| 02 | [Info-Seite](02-info-seite/README.md) | Öffentliche Seite, die auf den veralteten Link hinweist, die neue URL mit Qualitätsangabe zeigt und Feedback einsammelt. | 6 |
| 03 | [Regel-Matching](03-regel-matching/README.md) | Ermittelt zu einer alten URL die beste passende Regel samt Score und Match-Qualität. | 8 |
| 04 | [URL-Transformation](04-url-transformation/README.md) | Baut aus der getroffenen Regel und der alten URL die neue Ziel-URL. | 4 |
| 05 | [Regelverwaltung](05-regelverwaltung/README.md) | Anlegen, Bearbeiten, Validieren und Löschen von Weiterleitungsregeln im Admin-Panel. | 6 |
| 06 | [Globale Regeln](06-globale-regeln/README.md) | Such-und-Ersetzen-Regeln, die auf jede erzeugte Ziel-URL wirken. | 2 |
| 07 | [Import & Export](07-import-export/README.md) | Massenpflege des Regelwerks über CSV-, XLSX- und JSON-Dateien. | 5 |
| 08 | [Validierung](08-validierung/README.md) | Trockenlauf von URL-Listen gegen das Regelwerk mit Schritt-für-Schritt-Trace. | 2 |
| 09 | [Tracking & Statistiken](09-tracking-statistiken/README.md) | Erfassung der Aufrufe und Feedbacks sowie deren Auswertung. | 5 |
| 10 | [Laufzeit-Einstellungen](10-laufzeit-einstellungen/README.md) | Ohne Neustart änderbare Einstellungen für Verhalten und Texte. | 3 |
| 11 | [Admin-Zugang & Sicherheit](11-admin-zugang-sicherheit/README.md) | Anmeldung, Schutz der Admin-Endpunkte, IP-Sperren, Begrenzung der Anfragen und CSRF-Schutz. | 7 |
| 12 | [Betrieb](12-betrieb/README.md) | Health-Endpunkt, Aufwärmen des Caches und Versionsanzeige. | 3 |

## User Story Index

| ID | Titel | Epic |
|----|-------|------|
| [US-01.001](01-weiterleitung-alter-links/US-01.001-automatische-weiterleitung.md) | Automatische Weiterleitung bei Regel mit Auto-Redirect | Weiterleitung alter Links |
| [US-01.002](01-weiterleitung-alter-links/US-01.002-info-seite-als-standard.md) | Info-Seite als Standard statt direkter Weiterleitung | Weiterleitung alter Links |
| [US-01.003](01-weiterleitung-alter-links/US-01.003-verhalten-ohne-treffer.md) | Verhalten bei fehlendem Treffer | Weiterleitung alter Links |
| [US-01.004](01-weiterleitung-alter-links/US-01.004-smart-search.md) | Smart Search bei fehlendem Treffer | Weiterleitung alter Links |
| [US-01.005](01-weiterleitung-alter-links/US-01.005-system-und-admin-pfade-ausnehmen.md) | System- und Admin-Pfade von der Weiterleitung ausnehmen | Weiterleitung alter Links |
| [US-02.001](02-info-seite/US-02.001-alte-und-neue-url-anzeigen.md) | Alte und neue URL auf der Info-Seite anzeigen | Info-Seite |
| [US-02.002](02-info-seite/US-02.002-neue-url-kopieren-oder-oeffnen.md) | Neue URL kopieren oder in neuem Tab öffnen | Info-Seite |
| [US-02.003](02-info-seite/US-02.003-match-qualitaet-anzeigen.md) | Match-Qualität auf der Info-Seite anzeigen | Info-Seite |
| [US-02.004](02-info-seite/US-02.004-hinweistext-mit-links.md) | Hinweistext mit klickbaren Links anzeigen | Info-Seite |
| [US-02.005](02-info-seite/US-02.005-feedback-zur-weiterleitung.md) | Feedback zur Weiterleitung geben | Info-Seite |
| [US-02.006](02-info-seite/US-02.006-zugang-zum-admin-panel.md) | Zugang zum Admin-Panel von der Info-Seite | Info-Seite |
| [US-03.001](03-regel-matching/US-03.001-partial-regeln.md) | Teilweise Regeln (Präfix-Abgleich) auf Pfadabschnitte anwenden | Regel-Matching |
| [US-03.002](03-regel-matching/US-03.002-vollstaendige-url-regeln.md) | Wildcard-Regeln (exakter Pfad) anwenden | Regel-Matching |
| [US-03.003](03-regel-matching/US-03.003-domain-regeln.md) | Domain-Regeln auf den Hostnamen anwenden | Regel-Matching |
| [US-03.004](03-regel-matching/US-03.004-regex-regeln.md) | Regex-Regeln für Sonderfälle anwenden | Regel-Matching |
| [US-03.005](03-regel-matching/US-03.005-query-parameter-abgleichen.md) | Query-Parameter beim Matching abgleichen | Regel-Matching |
| [US-03.006](03-regel-matching/US-03.006-beste-regel-auswaehlen.md) | Beste passende Regel auswählen | Regel-Matching |
| [US-03.007](03-regel-matching/US-03.007-match-qualitaet-berechnen.md) | Match-Qualität und Ampelstufe berechnen | Regel-Matching |
| [US-03.008](03-regel-matching/US-03.008-matching-konfigurieren.md) | Matching über die Startkonfiguration steuern | Regel-Matching |
| [US-04.001](04-url-transformation/US-04.001-ziel-url-nach-regeltyp.md) | Ziel-URL nach Regeltyp aufbauen | URL-Transformation |
| [US-04.002](04-url-transformation/US-04.002-suchen-und-ersetzen-pro-regel.md) | Suchen und Ersetzen pro Regel | URL-Transformation |
| [US-04.003](04-url-transformation/US-04.003-query-parameter-steuern.md) | Query-Parameter der alten URL weitergeben, verwerfen oder filtern | URL-Transformation |
| [US-04.004](04-url-transformation/US-04.004-statische-query-parameter.md) | Statische Query-Parameter anhängen | URL-Transformation |
| [US-05.001](05-regelverwaltung/US-05.001-regelliste-durchsuchen.md) | Regelliste anzeigen, durchsuchen und sortieren | Regelverwaltung |
| [US-05.002](05-regelverwaltung/US-05.002-regel-anlegen-und-bearbeiten.md) | Regel anlegen und bearbeiten | Regelverwaltung |
| [US-05.003](05-regelverwaltung/US-05.003-regeleingaben-validieren.md) | Regeleingaben validieren | Regelverwaltung |
| [US-05.004](05-regelverwaltung/US-05.004-ueberlappende-matcher-verhindern.md) | Überlappende Matcher verhindern | Regelverwaltung |
| [US-05.005](05-regelverwaltung/US-05.005-regeln-loeschen.md) | Einzelne oder mehrere Regeln löschen | Regelverwaltung |
| [US-05.006](05-regelverwaltung/US-05.006-alle-regeln-loeschen.md) | Alle Regeln löschen | Regelverwaltung |
| [US-06.001](06-globale-regeln/US-06.001-globale-regeln-verwalten.md) | Globale Such-und-Ersetzen-Regeln verwalten | Globale Regeln |
| [US-06.002](06-globale-regeln/US-06.002-globale-regeln-anwenden.md) | Globale Regeln auf jede Ziel-URL anwenden | Globale Regeln |
| [US-07.001](07-import-export/US-07.001-import-vorschau.md) | Import-Vorschau anzeigen | Import & Export |
| [US-07.002](07-import-export/US-07.002-import-ausfuehren.md) | Import mit Fortschrittsanzeige ausführen | Import & Export |
| [US-07.003](07-import-export/US-07.003-wiederholter-import.md) | Wiederholter Import aktualisiert statt dupliziert | Import & Export |
| [US-07.004](07-import-export/US-07.004-tolerante-spaltenzuordnung.md) | Tolerante Spaltenzuordnung beim Import | Import & Export |
| [US-07.005](07-import-export/US-07.005-regeln-exportieren.md) | Regeln als JSON, CSV oder XLSX exportieren | Import & Export |
| [US-08.001](08-validierung/US-08.001-url-liste-pruefen.md) | URL-Liste gegen das Regelwerk prüfen | Validierung |
| [US-08.002](08-validierung/US-08.002-transformations-trace.md) | Transformation Schritt für Schritt nachvollziehen | Validierung |
| [US-09.001](09-tracking-statistiken/US-09.001-aufrufe-und-feedback-erfassen.md) | Aufrufe und Feedback erfassen | Tracking & Statistiken |
| [US-09.002](09-tracking-statistiken/US-09.002-kennzahlen-anzeigen.md) | Kennzahlen und meistgenutzte Regeln anzeigen | Tracking & Statistiken |
| [US-09.003](09-tracking-statistiken/US-09.003-feedback-trend.md) | Feedback-Trend über die Zeit anzeigen | Tracking & Statistiken |
| [US-09.004](09-tracking-statistiken/US-09.004-eintraege-filtern-und-exportieren.md) | Tracking-Einträge filtern und exportieren | Tracking & Statistiken |
| [US-09.005](09-tracking-statistiken/US-09.005-tracking-daten-loeschen.md) | Tracking-Daten löschen und automatisch aufräumen | Tracking & Statistiken |
| [US-10.001](10-laufzeit-einstellungen/US-10.001-verhalten-und-schalter.md) | Weiterleitungsverhalten und Schalter zur Laufzeit einstellen | Laufzeit-Einstellungen |
| [US-10.002](10-laufzeit-einstellungen/US-10.002-texte-der-info-seite.md) | Texte und Anzeigemodus der Info-Seite anpassen | Laufzeit-Einstellungen |
| [US-10.003](10-laufzeit-einstellungen/US-10.003-smart-search-konfigurieren.md) | Smart Search konfigurieren | Laufzeit-Einstellungen |
| [US-11.001](11-admin-zugang-sicherheit/US-11.001-login-mit-admin-passwort.md) | Mit dem Admin-Passwort anmelden | Admin-Zugang & Sicherheit |
| [US-11.002](11-admin-zugang-sicherheit/US-11.002-abmelden-und-anmeldestatus.md) | Abmelden und Anmeldestatus prüfen | Admin-Zugang & Sicherheit |
| [US-11.003](11-admin-zugang-sicherheit/US-11.003-sperre-nach-fehlversuchen.md) | IP nach wiederholten Fehlversuchen sperren | Admin-Zugang & Sicherheit |
| [US-11.004](11-admin-zugang-sicherheit/US-11.004-blockierte-ips-verwalten.md) | Blockierte IPs verwalten | Admin-Zugang & Sicherheit |
| [US-11.005](11-admin-zugang-sicherheit/US-11.005-admin-endpunkte-schuetzen.md) | Admin-Endpunkte vor unberechtigtem Zugriff schützen | Admin-Zugang & Sicherheit |
| [US-11.006](11-admin-zugang-sicherheit/US-11.006-anfragen-begrenzen.md) | Anfragen pro IP begrenzen | Admin-Zugang & Sicherheit |
| [US-11.007](11-admin-zugang-sicherheit/US-11.007-csrf-schutz.md) | Schreibende API-Anfragen gegen CSRF schützen | Admin-Zugang & Sicherheit |
| [US-12.001](12-betrieb/US-12.001-health-endpunkt.md) | Betriebszustand über einen Health-Endpunkt melden | Betrieb |
| [US-12.002](12-betrieb/US-12.002-cache-beim-start-aufwaermen.md) | Daten beim Start in den Cache laden | Betrieb |
| [US-12.003](12-betrieb/US-12.003-version-anzeigen.md) | Installierte Version anzeigen | Betrieb |

## Offene Fragen der Bestandsaufnahme

| US | Frage |
|----|-------|
| [US-01.003](01-weiterleitung-alter-links/US-01.003-verhalten-ohne-treffer.md) | F1 · Option «404 zurückgeben» liefert keinen HTTP-Status 404 |
| [US-02.003](02-info-seite/US-02.003-match-qualitaet-anzeigen.md) | F1 · Schalter «Link-Qualitätsanzeige» ohne Wirkung |
| [US-02.005](02-info-seite/US-02.005-feedback-zur-weiterleitung.md) | F1 · Schalter «Feedback-Kommentar» ohne Wirkung |
| [US-02.005](02-info-seite/US-02.005-feedback-zur-weiterleitung.md) | F2 · Anrede im Feedback-Dialog |
| [US-03.003](03-regel-matching/US-03.003-domain-regeln.md) | F1 · Domain-Regeln im Live-Betrieb wirkungslos |
| [US-03.004](03-regel-matching/US-03.004-regex-regeln.md) | F1 · Ziel-URL von Regex-Regeln wird nicht verwendet |
| [US-03.008](03-regel-matching/US-03.008-matching-konfigurieren.md) | F1 · Laufzeit-Schalter «Case-sensitive Link-Erkennung» ohne Wirkung |
| [US-05.003](05-regelverwaltung/US-05.003-regeleingaben-validieren.md) | F1 · Keine serverseitige Validierung beim manuellen Anlegen und Bearbeiten |
| [US-09.001](09-tracking-statistiken/US-09.001-aufrufe-und-feedback-erfassen.md) | F1 · Schalter «Referrer-Tracking» ohne Wirkung |
| [US-11.006](11-admin-zugang-sicherheit/US-11.006-anfragen-begrenzen.md) | F1 · Client-IP hinter dem Azure-Ingress |
