# ADR-002: Aktiv-Status und externe Dokument-ID auf der Weiterleitungsregel

## Status
Accepted — 2026-10-24

## Kontext
Regeln aus QM Pro müssen einem Dokument eindeutig zugeordnet sein, damit Publizieren eine bestehende Regel aktualisiert und Zurückziehen sie anhand der Dokument-ID findet. Zurückziehen deaktiviert die Regel, statt sie zu löschen. Das Regelmodell kennt bisher weder eine externe Kennung noch einen Aktiv-Status, und bestehende Zeilen in Table Storage haben diese Felder nicht.

Zu entscheiden ist, wo Zuordnung und Status gespeichert werden und wie deaktivierte Regeln aus Weiterleitung, Info-Seite und Validierung herausgehalten werden.

## Entscheidung
- Die Weiterleitungsregel erhält zwei zusätzliche Felder in der bestehenden Regeltabelle: eine optionale externe Dokument-ID und einen Aktiv-Status.
- Im Storage ist der Aktiv-Status nullable; ein fehlender Wert gilt als aktiv. Damit bleiben bestehende Zeilen ohne Migration gültig.
- Deaktivierte Regeln bleiben gespeichert und im Cache. Sie werden beim Aufbau der Matching-Indexe ausgelassen und wirken damit an einer einzigen Stelle auf Weiterleitung, Info-Seite und Validierung.
- Die Duplikat- und die Überlappungsprüfung berücksichtigen deaktivierte Regeln weiterhin.
- Eine separate Zuordnungstabelle wurde verworfen, weil sie ohne Transaktion zwischen zwei Tabellen eine zweite Konsistenzgrenze und einen zweiten Cache schafft.

## Konsequenzen
- Keine Migration und keine neue Tabelle.
- Kein Mehraufwand im Matching-Pfad.
- Eine deaktivierte Regel belegt ihren Matcher weiterhin.
- Ein Rollback auf eine ältere Version reaktiviert deaktivierte Regeln faktisch und zeigt die Herkunft «QM Pro» als «Unbekannt».
- Ein späteres manuelles Aktivieren und Deaktivieren kann auf dem Feld aufbauen.
