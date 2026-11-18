# ADR-005: Aktiv-Status im Abgleich der Liste «Redirect Regeln»

## Status
Accepted — 2026-11-18 (ergänzt ADR-001, ADR-002 und ADR-004)

## Kontext
Jeder Eintrag der Liste «Redirect Regeln» führt einen Status (ADR-004, US-13.004). US-13.005 verlangt, dass nur Einträge mit Status «aktiv» aktive Regeln in broot-redirect ergeben; die Regeln aller übrigen Einträge werden deaktiviert statt gelöscht, damit Regel-ID und Aufrufstatistik beim Statuswechsel erhalten bleiben (F2) — nach dem Muster des Zurückziehens in QM Pro (ADR-002). Der Abgleich besteht aus zwei unabhängig ausgelieferten Komponenten: dem Timer-Trigger `brtqmRedirectRulesSync` in der QM-Pro-Function-App und dem Endpunkt `PUT /api/qm-pro/area-rules` in broot-redirect (ADR-001, US-13.003). Der Vertrag zwischen beiden muss so erweitert werden, dass jede Kombination alter und neuer Versionen sicher bleibt.

## Entscheidung
- Der Timer-Trigger liest zusätzlich `brtqmStatus` und übermittelt pro Eintrag `active` als Boolean: `true` nur, wenn der getrimmte Statustext «aktiv» ist; leer, unbekannt und alle anderen Status ergeben `false`. Das Status-Vokabular existiert nur in QM Pro.
- `PUT /api/qm-pro/area-rules` nimmt `active` optional an; fehlend oder `null` gilt als aktiv.
- Regeln der Herkunft «Bereichsliste» werden für nicht aktive Einträge deaktiviert bzw. deaktiviert angelegt und für aktive Einträge aktiviert; gelöscht werden sie nur, wenn ihr Identifier im Stand fehlt.
- Die Antwort erhält additiv die Zähler `activated` und `deactivated` (Teilmengen von `updated`).
- Fehlt das Feld `brtqmStatus` in der Liste, gilt das Lesen als fehlgeschlagen; der Timer-Trigger sendet keinen Stand.

## Verworfene Alternativen
- Statustext im Vertrag, Übersetzung in broot-redirect: koppelt broot-redirect an die deutschen Choice-Werte; ein Umbenennen verlangt einen koordinierten Release beider Repos.
- Filtern im Timer-Trigger: nicht aktive Einträge fehlen im Stand und ihre Regeln werden gelöscht — widerspricht F2.
- Pflichtfeld `active`, fehlend gleich inaktiv: eine alte Function App gegen eine neue API würde alle Bereichsregeln deaktivieren.
- Eigener Endpunkt für Statuswechsel: zweiter Aufruf pro Lauf, kein atomarer Stand.

## Konsequenzen
- Additive, rückwärtskompatible Vertragserweiterung: alte Function App gegen neue API → alle Einträge aktiv; neue Function App gegen alte API → `active` wird ignoriert, alle Einträge aktiv wie bisher.
- Der Lesevertrag des Timer-Triggers aus ADR-003 umfasst `brtqmStatus`, wie von ADR-004 angekündigt.
- Deaktivierte Bereichsregeln belegen ihren Matcher gegenüber anderen Herkünften (ADR-002).
- Altbestand ohne Status wird nach dem Deployment des neuen Timer-Triggers deaktiviert; der Status ist vorher zu pflegen.
- Ein Rollback von broot-redirect auf eine Version vor ADR-002 würde deaktivierte Bereichsregeln faktisch reaktivieren (wie in ADR-002 beschrieben).
