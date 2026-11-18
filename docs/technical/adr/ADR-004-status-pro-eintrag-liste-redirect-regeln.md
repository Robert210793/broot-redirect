# ADR-004: Status pro Eintrag der Liste «Redirect Regeln»

## Status
Accepted — 2026-11-18 (ergänzt ADR-003)

## Kontext
Die Liste «Redirect Regeln» (ADR-003) ist der Vertrag zwischen dem SPFx-WebPart, dem Timer-Trigger in QM Pro und weiteren Lesern. US-13.004 führt pro Eintrag einen Status ein, der den Fortschritt einer Umleitung vom Erfassen bis zum Livegang abbildet, und Kommentare pro Eintrag. Der Status steuert künftig, welche Einträge nach broot-redirect abgeglichen werden (US-13.005) und welche ins URL-Mapping-CSV gelangen (US-13.006). Jeder neue Eintrag soll den Status «vorbereitet» erhalten, unabhängig davon, ob er im WebPart, direkt in der Liste oder über das Import-Skript entsteht (US-13.004, F1). Bestehende Einträge haben keinen Status.

Zu entscheiden sind Feldname, Feldtyp, gespeicherte Werte, Standardwert, Umgang mit leeren Werten und die Ablage der Kommentare.

## Entscheidung
- **Feld:** Anzeigename «Status», interner Name `brtqmStatus`, Typ Auswahl (Choice, Dropdown, keine Freitextwerte, `FillInChoice=FALSE`), feste Feld-GUID, kein Pflichtfeld, indiziert.
- **Werte:** exakt «vorbereitet», «IIS-Umleitung offen», «Testing», «Fehlerhaft», «aktiv», in dieser Reihenfolge. Der gespeicherte Text ist der Vertrag für alle Leser (WebPart, Export, Timer-Trigger aus US-13.005, URL-Mapping-CSV aus US-13.006).
- **Standardwert:** «vorbereitet», serverseitig als Standardwert des Felds; er greift für jeden neuen Eintrag ohne Statuswert.
- **Leere oder unbekannte Werte:** gelten für alle Leser als «vorbereitet».
- **Bereitstellung:** `Initialize-BrtqmRedirectRulesList.ps1` ergänzt das Feld als fehlendes Feld, analog zu ADR-003; bestehende Einträge bleiben unverändert, Abweichungen (Typ, Wertemenge, Standardwert, Freitext) werden gemeldet, nicht korrigiert.
- **Kommentare:** die nativen SharePoint-Kommentare des Listeneintrags; es gibt kein Kommentarfeld.

## Verworfene Alternativen
- Pflichtfeld: blockiert die direkte Bearbeitung von Altbestand und widerspricht «leer = vorbereitet».
- Englische Codewerte in einem Textfeld mit Anzeige-Mapping: ein Choice-Feld speichert nur Text; das Mapping müsste in jedem Leser wiederholt werden.
- Eigene Statusliste mit Nachschlagefeld: analog zur Ablehnung einer zweiten Liste in ADR-003 (US-13.002, F10).
- Ja/Nein-Feld «aktiv»: bildet den Ablauf nicht ab.
- Standardwert nur im WebPart: verfehlt F1 für direkt und per Skript angelegte Einträge.
- Eigenes mehrzeiliges Kommentarfeld: von US-13.004 ausgeschlossen, ohne Verfasser und Zeitpunkt pro Kommentar.

## Konsequenzen
- Name, Typ und Werte sind nach der Bereitstellung praktisch unveränderlich. Das Umbenennen eines Werts verlangt eine Datenmigration aller Einträge und die gleichzeitige Anpassung von US-13.005 und US-13.006.
- Der Timer-Trigger liest künftig zusätzlich `brtqmStatus`; der Lesevertrag aus ADR-003 wird mit US-13.005 erweitert.
- Altbestand bleibt leer, bis der Administrator ihn setzt; bis dahin gilt er als «vorbereitet».
- Kommentare gehen mit dem Eintrag in den Papierkorb und lassen sich mit ihm wiederherstellen.
- broot-redirect selbst ändert sich durch diese Entscheidung nicht.
