# ADR-001: API-Key-geschützte Integrationsschnittstelle für QM Pro

## Status
Accepted — 2026-10-24

## Kontext
QM Pro 365 schreibt beim Publizieren eines Dokuments eine Weiterleitungsregel und deaktiviert sie beim Zurückziehen. Die Aufrufe kommen aus Azure Functions, also von Server zu Server, ohne Browser, ohne Session-Cookie und ohne Origin-Header. Die bestehende Admin-API ist über eine Session und den CSRF-Schutz abgesichert und für diesen Aufrufer nicht nutzbar.

Zu entscheiden ist, über welche Schnittstelle und mit welchem Authentifizierungsverfahren QM Pro Regeln schreibt.

## Entscheidung
- Eigene Maschinen-Endpunkte unter einem separaten API-Präfix, getrennt von der Admin-API: `POST publish` und `POST unpublish`.
- Die Dokument-ID steht im JSON-Body, nie im Pfad.
- Authentifizierung über einen statischen API-Key im Request-Header. Der Key kommt aus der Startkonfiguration und wird laufzeitkonstant über Hashes verglichen.
- Die Prüfung sitzt als Filter am Controller, nicht in einer Pfadliste.
- Das Präfix ist vom CSRF-Schutz ausgenommen und gehört nicht zur Session-Autorisierung.
- Eine eigene Stufe der Anfragebegrenzung trennt die Integrationsaufrufe vom Kontingent der Info-Seite.
- Antwortvertrag: `{ ruleId, result }` bei Erfolg, `{ error, code }` bei Fehlern, mit den Statuscodes 200, 201, 400, 401, 409, 429 und 503.
- Entra ID mit Client Credentials bzw. Managed Identity wurde verworfen, weil es eine App-Registrierung und Tenant-Abhängigkeiten verlangt, die für einen einzelnen Aufrufer unverhältnismässig sind.

## Konsequenzen
- Neben der Admin-Session besteht ein zweites Authentifizierungsverfahren.
- Der API-Key ist ein Secret (Container-App-Secret oder Key Vault) und muss mit QM Pro abgestimmt rotiert werden.
- Ohne konfigurierten Key sind die Endpunkte gesperrt.
- Ein Massen-Publizieren blockiert die Info-Seite nicht über die Anfragebegrenzung.
- Der Vertrag ist repository-übergreifend; Änderungen sind mit `broot-spo-qms-backend-ps` abgestimmte Breaking Changes.
- Eine Admin-Session ersetzt den API-Key nicht.
