# Epic 13: QM-Pro-Integration

## Übersicht

Schnittstelle für QM Pro 365: Beim Publizieren eines Dokuments schreibt QM Pro die Weiterleitungsregel von der alten On-Premises-URL auf die Publikations-URL, beim Zurückziehen deaktiviert QM Pro die Regel. Zusätzlich erfasst ein SPFx-WebPart auf der QM-Pro-Site Bereiche mit vielen alten URLs und einer gemeinsamen Ziel-URL in der Liste «Redirect Regeln»; ein Timer-Trigger gleicht diese Liste periodisch mit den Regeln in broot-redirect ab.

## User Stories

| ID | Titel |
|----|-------|
| [US-13.001](US-13.001-qm-pro-regeln-schreiben-und-deaktivieren.md) | Regeln aus QM Pro beim Publizieren schreiben und beim Zurückziehen deaktivieren |
| [US-13.002](US-13.002-regeln-pro-bereich-erfassen.md) | Regeln pro Bereich in einem SPFx-WebPart erfassen |
| [US-13.003](US-13.003-bereichsregeln-abgleichen.md) | Regeln aus der Liste «Redirect Regeln» periodisch abgleichen |
