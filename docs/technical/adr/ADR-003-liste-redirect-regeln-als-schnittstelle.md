# ADR-003: Liste «Redirect Regeln» als Schnittstelle zwischen QM-Pro-WebPart und Abgleich

## Status
Accepted — 2026-10-27

## Kontext
Ein SPFx-WebPart in der Legacy-QM-Pro-Solution erfasst Bereiche mit vielen alten On-Premises-URLs und einer gemeinsamen Ziel-URL (US-13.002). Ein Timer-Trigger in QM Pro liest diese Einträge periodisch und gleicht sie mit den Regeln in broot-redirect ab (US-13.003). Die SharePoint-Liste ist damit der Vertrag zwischen zwei Komponenten in zwei Repositories.

Alte URLs mit Parametern wie `RootFolder=…&FolderCTID=…&View=…` überschreiten 255 Zeichen. Einzeilige Text- und Hyperlink-Felder sind auf 255 Zeichen begrenzt, mehrzeilige Textfelder lassen sich nicht indizieren. Die Liste muss mehr als 5'000 Einträge aufnehmen, und doppelte Quell-URLs sollen schon bei der Erfassung nach demselben Massstab erkannt werden, den broot-redirect für gleichwertige Matcher anwendet.

Zu entscheiden ist das Schema der Liste, der Identifier eines Eintrags, die Form des normalisierten Schlüssels und die Art der Bereitstellung.

## Entscheidung
- **Liste:** `Lists/brtqmRedirectRules` auf der QM-Pro-Site, Anzeigename «Redirect Regeln», ein Eintrag pro Quell-URL. Adressiert wird die Liste über ihre URL, nicht über Titel oder GUID.
- **Felder:**

  | Anzeigename | Interner Name | Typ | Pflicht | Indiziert |
  |---|---|---|---|---|
  | Bereich | `brtqmArea` | Einzeiliger Text, max. 255 | Ja | Ja |
  | Quell-URL | `brtqmSourceUrl` | Mehrzeiliger Text, nur Text | Ja | Nein |
  | Ziel-URL | `brtqmTargetUrl` | Mehrzeiliger Text, nur Text | Ja | Nein |
  | Quell-Schlüssel | `brtqmSourceKey` | Einzeiliger Text, max. 255, in Formularen ausgeblendet | Nein | Ja |

  Das Standardfeld `Title` wird nicht verwendet. Die Quell-URL wird inklusive Host gespeichert, die Ziel-URL immer absolut.
- **Identifier:** die von SharePoint vergebene GUID `UniqueId` des Eintrags; kein eigenes Feld.
- **Normalisierter Schlüssel:** `v1:` + SHA-256 (Hex, klein) der kanonischen Form der Quell-URL. Die kanonische Form folgt dem Gleichwertigkeitsmassstab von broot-redirect (`RuleCacheService.FindEquivalentMatcher`): Schema, Host und Fragment verworfen, Pfadsegmente einzeln dekodiert und kleingeschrieben, ohne abschliessenden `/`, Query als sortierte Menge dekodierter, kleingeschriebener Schlüssel/Wert-Paare. Der Hash hält den Schlüssel unabhängig von der URL-Länge indizierbar; das Präfix erlaubt eine spätere Neuberechnung.
- **Schreiber und Leser:** Das WebPart ist der einzige vorgesehene Schreiber und pflegt den Schlüssel. Direkte Bearbeitung in SharePoint ist zulässig, aber ohne Schlüsselpflege. Der Timer-Trigger aus US-13.003 liest seitenweise nur `UniqueId`, `brtqmSourceUrl` und `brtqmTargetUrl` und schreibt nie in die Liste.
- **Bereitstellung:** idempotentes PnP.PowerShell-Skript gegen die QM-Pro-Site: legt Liste, Felder (mit festen Feld-GUIDs), Indizes und Standardansicht nur an, wenn sie fehlen. Es verändert weder Einträge noch bestehende Felder und meldet Abweichungen, statt sie zu korrigieren.

## Verworfene Alternativen
- Quell- und Ziel-URL als einzeilige Text- oder Hyperlink-Felder: 255-Zeichen-Grenze.
- Normalisierter Schlüssel im Klartext: nicht indizierbar, sobald er länger als 255 Zeichen ist.
- Eigenes GUID-Feld als Identifier: in US-13.002, F9, verworfen.
- Zweite Liste der Bereiche mit Nachschlagefeld: in US-13.002, F10, verworfen.
- Eindeutigkeit über `EnforceUniqueValues`: kollidiert mit direkt erfassten Einträgen ohne Schlüssel.
- Bereitstellung zur Laufzeit im WebPart: verlangt Rechte zum Verwalten von Listen beim Benutzer und könnte auf anderen Sites ungewollt Listen erzeugen.
- Bereitstellung über das SPFx Feature Framework: Felder bestehender Listen lassen sich nur über Upgrade-Actions ergänzen, schwer idempotent.

## Konsequenzen
- Interne Namen und Feldtypen sind nach der Bereitstellung praktisch unveränderlich. Eine Änderung braucht eine neue Liste und damit neue `UniqueId`s; der Abgleich würde alle Regeln löschen und neu anlegen.
- Ändert sich der Gleichwertigkeitsmassstab in broot-redirect, muss das WebPart folgen: neue Schlüsselversion `v2:` und Neuberechnung aller Einträge.
- Direkt in SharePoint erfasste oder geänderte Einträge haben keinen oder einen veralteten Schlüssel und entgehen der Dublettenprüfung; die Konfliktprüfung des Abgleichs (US-13.003) fängt sie ab.
- Zwei Personen, die im selben Moment dieselbe Quell-URL speichern, können eine Dublette erzeugen; auch hier greift die Konfliktprüfung des Abgleichs.
- US-13.003 braucht keinen Zugriff auf `brtqmArea` und `brtqmSourceKey`.
- broot-redirect selbst ändert sich durch diese Entscheidung nicht.
