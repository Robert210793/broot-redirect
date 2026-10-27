# Domain-Glossar

Mapping zwischen deutschem Fachbegriff und dem Begriff im Code. Das Projekt verwendet kein Code-Präfix; die Code-Begriffe entsprechen den bestehenden Bezeichnern.

| Deutsch      | Code-Begriff                    | Beschreibung                        |
|--------------|---------------------------------|-------------------------------------|
| Admin-Session | `admin_session` | Session-Cookie nach erfolgreicher Anmeldung mit dem gemeinsamen Admin-Passwort |
| Aktiv-Status / deaktivierte Regel | `IsActive` | Ob eine Regel bei Weiterleitung, Info-Seite und Validierung berücksichtigt wird; eine deaktivierte Regel bleibt gespeichert und belegt ihren Matcher |
| Alte URL | `OldUrl` | Ursprüngliche On-Premises-URL eines Dokuments aus der Migration; ihr Pfad inklusive Query wird zum Matcher |
| Ampelstufe | `MatchQualityLevel` | Grün (ab 90), Gelb (ab 60) oder Rot — Darstellung der Match-Qualität |
| API-Key (QM Pro) | `QmProApiKey` | Geheimer Schlüssel aus der Startkonfiguration, den QM Pro im Header `X-Api-Key` sendet |
| Aufbewahrungsdauer | `TrackingRetentionDays` | Anzahl Tage, nach denen Tracking-Einträge automatisch gelöscht werden |
| Aufwärmen (Cache) | `CacheWarmupService` | Laden von Regeln, Einstellungen und globalen Regeln in den Arbeitsspeicher beim Start |
| Auto-Weiterleitung | `AutoRedirect` | Regelschalter: direkte HTTP-302-Weiterleitung statt Info-Seite |
| Beibehaltene Query-Parameter | `KeptQueryParam` | Positivliste von Query-Parametern, die trotz «Query verwerfen» übernommen werden |
| Bereich | `brtqmArea` | Fachliche Gruppe von Quell-URLs in der Liste «Redirect Regeln» mit genau einer gemeinsamen Ziel-URL; existiert nur über ihre Listeneinträge, Namen werden ohne Gross-/Kleinschreibung und Rand-Leerzeichen verglichen |
| Bereichsliste (Herkunft) | — (im Refinement festzulegen) | Herkunft von Regeln, die der Abgleich aus der Liste «Redirect Regeln» anlegt (US-13.003); getrennt von «QM Pro» |
| Blockierte IP | `BruteForceProtectionService` | IP-Adresse, die nach zu vielen Fehlversuchen oder manuell gesperrt ist |
| Dokument-ID | `ExternalDocumentId` | Kennung eines Dokuments in QM Pro (`brtqmSourceId`, Format `SiteUrl\|ListGuid\|ItemUniqueId`); verknüpft genau eine Regel mit einem Dokument |
| Domain (Regeltyp) | `RedirectType.Domain` | Regel, die nur den Hostnamen austauscht; in Import-Meldungen «Domain-Ersatz» |
| Feedback | `Feedback` | Rückmeldung zum Aufruf: `OK`, `NOK` oder `auto-redirect` |
| Globale Regel | `GlobalRule` | Literales Suchen/Ersetzen, das nach Priorität auf jede Ziel-URL angewendet wird |
| Herkunft | `RuleSource` | Woher eine Regel stammt: Manuell, Import, QM Pro (`RuleSource.QmPro`), Bereichsliste (US-13.003) oder Unbekannt |
| Hintergrundauftrag | `jobId` | Lang laufende Operation (Import, alle Regeln löschen) mit abfragbarem Fortschritt |
| Info-Seite | `InfoPageComponent` | Öffentliche Seite, die auf den veralteten Link hinweist und die neue URL zeigt |
| Info-Text | `InfoText` | Regelspezifischer Hinweistext auf der Info-Seite |
| Laufzeit-Einstellungen | `AppSettings` | Im Admin-Panel änderbare Einstellungen, gespeichert in Table Storage |
| Match-Qualität | `Quality` | Dem Benutzer angezeigte Güte der Zuordnung in Prozent (100, 75, 50, 0) |
| Matcher | `Matcher` | Muster der alten URL, gegen das eine Anfrage geprüft wird (Pfad, Domain oder Regex) |
| Matcher-Konflikt | `MATCHER_CONFLICT` | Abweisung einer Regel, deren Matcher hierarchisch mit einer bestehenden teilweisen Regel überlappt |
| Normalisierter Schlüssel | `brtqmSourceKey` | Vergleichswert einer Quell-URL ohne Host, dekodiert, ohne Gross-/Kleinschreibung, Query unabhängig von der Reihenfolge, gespeichert als `v1:` + SHA-256 (ADR-003); verhindert doppelte Quell-URLs in der Liste «Redirect Regeln» |
| Popup-Modus | `PopupMode` | Anzeige der Info-Seite: `inline` (Hinweisbanner) oder `active` (Dialog beim Laden) |
| Publikations-URL | `PublicationUrl` | URL der publizierten Kopie eines Dokuments in der Publikationsbibliothek; wird zur Ziel-URL der Regel |
| Publizieren (QM Pro) | `Publish` | Aufruf aus QM Pro, der die Regel eines Dokuments anlegt oder aktualisiert und aktiviert |
| Quell-URL | `brtqmSourceUrl` | Alte On-Premises-URL in der Liste «Redirect Regeln», gespeichert inklusive Host; der Timer-Trigger entfernt den Host, danach entspricht sie einer «Alten URL» |
| Redirect Regeln (Liste) | `Lists/brtqmRedirectRules` | SharePoint-Liste auf der QM-Pro-Site, in der das WebPart pro Quell-URL einen Eintrag mit Bereich und Ziel-URL führt; Identifier eines Eintrags ist seine SharePoint-GUID (`UniqueId`) |
| Regex (Regeltyp) | `RedirectType.Regex` | Regel mit regulärem Ausdruck, zuletzt geprüft |
| Score | `Score` | Interne Punktzahl zur Auswahl der besten Regel unter mehreren Kandidaten |
| Smart Search | `SmartSearchService` | Fallback ohne Treffer: Weiterleitung auf eine Suche mit einem aus dem Pfad abgeleiteten Suchbegriff |
| Standard-Zieldomain | `DefaultNewDomain` | Basis der neuen Ablage; Ziel ohne Treffer und Ergänzung relativer Ziel-URLs |
| Startkonfiguration | `BrootRedirectOptions` | Konfiguration aus `appsettings.json` bzw. Umgebungsvariablen; Änderungen erfordern einen Neustart |
| Statische Query-Parameter | `StaticQueryParam` | Feste Key/Value-Paare, die an jede Ziel-URL einer Regel angehängt werden |
| Suchen und Ersetzen | `SearchAndReplaceEntry` | Regelspezifische literale Ersetzung in der erzeugten Ziel-URL |
| Teilweise (Regeltyp) | `RedirectType.Partial` | Regel für einen Pfadabschnitt, die alle darunterliegenden URLs abdeckt; Standardtyp |
| Tracking-Eintrag | `TrackingEntry` | Datensatz pro Aufruf der Info-Seite oder automatischer Weiterleitung |
| Transformations-Trace | `UrlTraceStep` | Schrittweise Aufzeichnung, wie die Ziel-URL entstanden ist (Validierungswerkzeug) |
| Validierung (Trockenlauf) | `POST /api/rules/validate` | Prüfung einer URL-Liste gegen das Regelwerk, ohne Aufrufe zu erfassen |
| Verhalten ohne Treffer | `NoMatchBehavior` | `RedirectToDefault`, `SmartSearch` oder `Return404` |
| Vorgeschlagene URL | `UserProposedUrl` | Vom Benutzer im Feedback angegebene korrekte URL |
| Weiterleitungsregel | `RedirectRule` | Zentrale Entität: bildet einen Matcher auf eine Ziel-URL ab |
| Weiterleitungsstrategie | `RedirectStrategy` | Art der Auflösung im Tracking: `rule`, `smart-search`, `domain-fallback` |
| Weiterleitungstyp | `RedirectType` | Teilweise, Wildcard, Domain oder Regex |
| Wildcard (Regeltyp) | `RedirectType.Wildcard` | Regel für genau einen Pfad (exakter Nachschlag), optional mit Query; in Import-Meldungen «Vollständig» |
| Ziel-URL | `TargetUrl` | Ziel einer Regel; Interpretation hängt vom Weiterleitungstyp ab |
| Zurückziehen (QM Pro) | `Unpublish` | Aufruf aus QM Pro beim Löschen, Inaktivieren oder Archivieren eines Dokuments; deaktiviert die Regel des Dokuments |
