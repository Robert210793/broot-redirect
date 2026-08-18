# Broot.Redirect

Ein System zur Verwaltung von URL-Weiterleitungen für migrierte SharePoint-Dokumente. Umgesetzt mit .NET 8 und Angular 19, als Datenablage dient Azure Table Storage.

Wird eine alte URL aufgerufen, gleicht die Applikation sie gegen eine Menge von administrierbaren Regeln ab, transformiert sie in die neue URL und leitet den Benutzer entweder automatisch weiter oder zeigt eine **Info-Seite** an, die erklärt, dass der Link veraltet ist und wohin er neu zeigt. Alles — Regeln, Texte, Verhalten — ist zur Laufzeit über ein Admin-Panel bearbeitbar.

---

## Inhaltsverzeichnis

1. [Was das System macht](#1-was-das-system-macht)
2. [Fachliche Konzepte](#2-fachliche-konzepte)
3. [Architektur](#3-architektur)
4. [Ablauf eines Requests](#4-ablauf-eines-requests)
5. [Die Matching-Engine](#5-die-matching-engine)
6. [Die URL-Transformations-Pipeline](#6-die-url-transformations-pipeline)
7. [Caching](#7-caching)
8. [Datenmodell und Storage](#8-datenmodell-und-storage)
9. [Sicherheit](#9-sicherheit)
10. [API-Referenz](#10-api-referenz)
11. [Frontend](#11-frontend)
12. [Import / Export](#12-import--export)
13. [Lokale Entwicklung](#13-lokale-entwicklung)
14. [Tests](#14-tests)
15. [Konfigurationsreferenz](#15-konfigurationsreferenz)
16. [Deployment](#16-deployment)
17. [CI/CD und Versionierung](#17-cicd-und-versionierung)

---

## 1. Was das System macht

Das Szenario: Ein Unternehmen migriert seine Dokumente in SharePoint, wodurch sich sämtliche Dokument-URLs ändern. Tausende von Links auf diese Dokumente stecken weiterhin in E-Mails, anderen Dokumenten, Bookmarks und Wiki-Seiten — und laufen nach der Migration ins Leere. Broot.Redirect wird unter der **alten Adresse** betrieben und erledigt drei Dinge:

1. **Matching** der eingehenden alten URL gegen die konfigurierten Regeln.
2. **Transformation** in die entsprechende neue URL.
3. Entweder **direkte Weiterleitung** (HTTP 302) oder Anzeige einer **Info-Seite** — einer gebrandeten Seite, welche die neue URL anzeigt, sie kopieren oder in einem neuen Tab öffnen lässt, erklärt wie zuverlässig der Treffer war und optional Feedback einsammelt.

Die Info-Seite ist der Standardfall, weil sie den Benutzer *darauf aufmerksam macht*, dass sein Link veraltet ist — statt ihn auf ewig still weiterzuleiten.

> **Abgrenzung:** Der Anwendungsfall sind ausschliesslich Links auf Dokumente in SharePoint. Die Matching- und Transformationslogik selbst arbeitet jedoch rein auf URLs und kennt weder SharePoint noch Microsoft 365 — es gibt keine SharePoint-spezifischen Abhängigkeiten im Code (kein PnP, kein CSOM, kein Graph). Alles Fachliche steckt in den Regeln und Einstellungen, nicht in der Implementierung.

Zusätzlich bietet das System:

- Ein **Admin-Panel** (Angular SPA) für die Verwaltung von Regeln, globalen Such-und-Ersetzen-Regeln, Laufzeit-Einstellungen, Auswertungen und blockierten IPs.
- **Massen-Import/-Export** in CSV, XLSX und JSON.
- Ein **Validierungs-/Dry-Run-Werkzeug**, das eine Liste von URLs entgegennimmt und Schritt für Schritt zeigt, welche Regel greift und wie die Ziel-URL aufgebaut wird.
- **Tracking und Statistiken** — jeder Aufruf der Info-Seite und jede Auto-Weiterleitung wird erfasst, inklusive Match-Qualität, Feedback und Trend-Diagrammen.

---

## 2. Fachliche Konzepte

### Redirect Rule (Weiterleitungsregel)

Die zentrale Entität ([`RedirectRule`](Broot.Redirect.Core/Models/RedirectRule.cs)). Eine Regel bildet einen **Matcher** (das alte URL-Muster) auf eine **Ziel-URL** ab.

| Feld | Bedeutung |
|---|---|
| `Matcher` | Das Muster, gegen das die eingehende URL geprüft wird. Entweder ein Pfad (beginnt mit `/`), eine reine Domain (`old.example.com`) oder ein Regex. |
| `TargetUrl` | Wohin die URL zeigen soll. Die Interpretation hängt vom `RedirectType` ab. |
| `RedirectType` | `Partial`, `Wildcard`, `Domain` oder `Regex` — siehe unten. |
| `Source` | `Manual`, `Import` oder `Unknown` — woher die Regel stammt. Wird im Admin-UI angezeigt. |
| `InfoText` | Regelspezifischer Hinweis auf der Info-Seite. Überschreibt den globalen Hinweistext. |
| `AutoRedirect` | Wenn `true`, wird die Info-Seite übersprungen und direkt ein 302 gesendet. |
| `DiscardQueryParams` | Verwirft die ursprüngliche Query. |
| `ForwardQueryParams` | Hängt die ursprüngliche Query an das Ziel an. |
| `KeptQueryParams` | Regex-basierte Positivliste von Query-Parametern, die übernommen werden (zusammen mit `DiscardQueryParams` verwendet). |
| `StaticQueryParams` | Feste Key/Value-Paare, die an jede Ziel-URL angehängt werden. |
| `SearchAndReplace` | Regelspezifisches literales Suchen/Ersetzen auf der aufgelösten URL. |

### Redirect-Typen

| Typ | Beispiel-Matcher | Verhalten |
|---|---|---|
| **`Partial`** | `/old-section` | Der Matcher wird *irgendwo* im Pfad gefunden und durch `TargetUrl` ersetzt; alles Nachfolgende bleibt erhalten. `/old-section/page/1` → `/new-section/page/1`. Standardtyp und der am häufigsten genutzte. |
| **`Wildcard`** | `/reports/*` | Wird über einen O(1)-Index auf den exakten Pfad aufgelöst (der ganze normalisierte Pfad ist der Schlüssel). Der Suffix nach dem Matcher-Präfix wird an das Ziel angehängt. |
| **`Domain`** | `old.example.com` | Trifft nur auf den Hostnamen zu. Der gesamte Pfad bleibt erhalten, nur die Domain wird ausgetauscht. |
| **`Regex`** | `^/legacy/\d+$` | Vollwertiges .NET-Regex gegen die komplette Request-URL. Vorkompiliert mit konfigurierbarem Timeout. Wird zuletzt ausgewertet und nimmt nie an der Überlappungsprüfung teil. |

> **Hinweis zur Benennung:** Das deutsche Admin-UI bezeichnet diese Typen als *Teilweise* (Partial), *Vollständig* (Wildcard), *Domain-Ersatz* (Domain) und *Regex*. `Wildcard` wird gegenüber Benutzern als «vollständige/exakte URL» beschrieben — das trifft das tatsächliche Verhalten besser als der Name selbst.

### Match-Qualität

Jeder Treffer erzeugt einen **Qualitäts-Prozentwert** und eine Ampel-**Stufe**, die dem Benutzer auf der Info-Seite als Anzeige dargestellt wird:

| Qualität | Stufe | Bedeutung |
|---|---|---|
| `100` | Grün | Exakter Treffer — Pfad und Query vollständig abgedeckt. |
| `75` | Gelb | Treffer, aber der Request enthielt zusätzliche Query-Parameter, die der Regel unbekannt sind. |
| `50` | Rot | Nur ein Teil der URL wurde getroffen (Präfix-/Teilsegment-Treffer). |

Schwellwerte: `>= 90` grün, `>= 60` gelb, sonst rot ([`QualityToLevel`](Broot.Redirect.Core/Services/RuleMatchingService.cs)). Jede Stufe hat einen eigenen, im Admin-UI editierbaren Erklärungstext.

Die Qualität ist nicht dasselbe wie der **Score** — der Score ist die interne Zahl, mit der unter mehreren Kandidaten die *beste* Regel bestimmt wird; die Qualität ist das, was der Benutzer sieht.

### Global Rules (globale Regeln)

[`GlobalRule`](Broot.Redirect.Core/Models/GlobalRule.cs) — literale Such-und-Ersetzen-Paare, die auf **jede** aufgelöste URL angewendet werden, sortiert nach `Priority` (aufsteigend, tiefer läuft zuerst). Nützlich für flächendeckende Ersetzungen, etwa das Umbenennen eines Pfadsegments über die ganze Site hinweg, ohne Hunderte von Regeln anzufassen.

### App-Einstellungen

[`AppSettings`](Broot.Redirect.Core/Models/AppSettings.cs) — zur Laufzeit editierbare Konfiguration, gespeichert im Table Storage und im Memory gecacht. Bewusst getrennt von `appsettings.json`:

- **`AppSettings`** (Table Storage, im Admin-UI editierbar): Standard-Zieldomain, Verhalten ohne Treffer, sämtliche Texte der Info-Seite, Smart-Search-Konfiguration, Feature-Schalter.
- **`BrootRedirectOptions`** (`appsettings.json` / Umgebungsvariablen, benötigt Neustart): Admin-Passwort, Scoring-Gewichte, Rate Limits, Session-Timeout, Aufbewahrungsdauer.

### Verhalten ohne Treffer

Greift keine Regel, entscheidet `AppSettings.NoMatchBehavior`:

| Wert | Wirkung |
|---|---|
| `RedirectToDefault` | 302 auf `DefaultNewDomain`. |
| `SmartSearch` | Baut aus dem Request-Pfad eine Such-URL und leitet dorthin weiter. |
| `Return404` | Fällt durch zur SPA — in der Praxis wird die Info-Seite gerendert. |

### Smart Search

[`SmartSearchService`](Broot.Redirect.Core/Services/SmartSearchService.cs) verwandelt einen Pfad ohne Treffer in eine Suchanfrage auf der neuen SharePoint-Site. Der Suchbegriff wird entweder über ein im Admin-UI konfiguriertes Regex mit Capture-Group extrahiert oder als Fallback aus dem letzten Pfadsegment abgeleitet (Dateiendung wird entfernt, `-`/`_` werden zu Leerzeichen). Aus `/docs/annual-report-2023.pdf` wird der Suchbegriff `annual report 2023`, angehängt an `SmartSearchUrl`.

### Tracking

[`TrackingEntry`](Broot.Redirect.Core/Models/TrackingEntry.cs) — ein Datensatz pro Aufruf der Info-Seite bzw. pro Auto-Weiterleitung. Erfasst werden alte/neue URL, die getroffene Regel, die Match-Qualität, User Agent, Referrer, die verwendete Strategie (`rule`, `smart-search`, `domain-fallback`, `auto-redirect`) sowie optionales Benutzer-Feedback (`OK`/`NOK` plus eine vom Benutzer vorgeschlagene URL). Wird nach `TrackingRetentionDays` automatisch gelöscht.

---

## 3. Architektur

### Systemüberblick

Produktiv läuft alles in **einem einzigen Container**: Die Angular-SPA wird nach `wwwroot/` gebaut und vom selben ASP.NET-Core-Prozess ausgeliefert wie die API. Einzige externe Abhängigkeit ist Azure Table Storage — optional kommt Application Insights dazu.

```
  Benutzer ─┐
            ├─► Middleware-Pipeline ─┬─► RedirectMiddleware ─┬─► 302 auf neue SharePoint-URL
  Admin ────┘  RateLimit · CSRF ·    │                       │
               Session · Auth        │                       └─► Angular-SPA aus wwwroot
                                     │                                    │
                                     └─► REST-Controller /api/* ◄─────────┘
                                         │  resolve · track
                                         ▼
                                         Core-Services · Matching · Transformation
                                         │
                                         ▼
                                         In-Memory-Cache · Rules · Settings · GlobalRules
                                         │  Warmup beim Start, danach nur Schreibzugriffe
                                         ▼
                                         Azure Table Storage
                                         RedirectRules · AppSettings · GlobalRules · Tracking
```

Entscheidend für das Verständnis: **Lesezugriffe enden im Cache, nicht im Storage.** Table Storage wird nur beim Start und bei Schreibvorgängen berührt (Details in [Kapitel 7](#7-caching)).

### Projekte und Schichten

Vier Projekte plus Tests, in einer an Clean Architecture angelehnten Schichtung:

```
Broot.Redirect.API/            ASP.NET Core 8 — Controller, Middleware, DTOs, Auth, Import/Export
Broot.Redirect.Core/           Domain-Modelle, Service-Interfaces, Matching- und Transformationslogik (kein I/O)
Broot.Redirect.Infrastructure/ Azure-Table-Storage-Repositories, In-Memory-Caches, Background Services
Broot.Redirect.Client/         Angular-19-SPA (Admin-Panel und öffentliche Info-Seite)
Broot.Redirect.Tests/          xUnit — Unit-Tests und Azurite-gestützte Integrationstests
```

```
  Client  ──ng build──►  wwwroot/  ──wird ausgeliefert von──►  API

  API ──► Infrastructure ──► Core
   └───────────────────────────►     (API referenziert Core auch direkt)

  Core hat keine Projektreferenzen. Tests referenzieren alle drei.
```

Die Abhängigkeitsrichtung ist `API → Infrastructure → Core`, wobei Core von nichts abhängt. Die API referenziert Core zusätzlich direkt, weil Controller und Middleware gegen dessen Interfaces arbeiten. In Core liegen die beiden algorithmisch anspruchsvollen Teile — [`RuleMatchingService`](Broot.Redirect.Core/Services/RuleMatchingService.cs) und [`UrlTransformService`](Broot.Redirect.Core/Services/UrlTransformService.cs) — und beide sind seiteneffektfrei und vollständig unit-testbar.

Das Client-Projekt ist zur Laufzeit keine eigene Komponente: Es wird zur Buildzeit nach `wwwroot/` kompiliert und danach als statische Dateien ausgeliefert.

### Zentrale Services

| Service | Projekt | Verantwortung |
|---|---|---|
| `RuleMatchingService` | Core | Wählt die beste passende Regel für eine URL; liefert Score und Qualität. |
| `UrlTransformService` | Core | Baut aus einer getroffenen Regel die finale Ziel-URL; kann einen Schritt-für-Schritt-Trace erzeugen. |
| `SmartSearchService` | Core | Baut eine Such-URL als Fallback, wenn nichts greift. |
| `RuleCacheService` | Infrastructure | In-Memory-Indexe, nach Regeltyp partitioniert. Alle Lesezugriffe gehen hierhin, nie in den Storage. |
| `AppSettingsCacheService` | Infrastructure | Einzelnes gecachtes Settings-Objekt. |
| `GlobalRuleCacheService` | Infrastructure | Globale Regeln, permanent nach Priorität sortiert. |
| `CacheWarmupService` | Infrastructure | `IHostedService` — legt beim Start die Tabellen an und füllt alle Caches. |
| `TrackingCleanupService` | Infrastructure | `BackgroundService` — löscht alle 24 h abgelaufene Tracking-Einträge. |
| `RuleValidationService` | API | Validiert Regel-Eingaben. Fehlermeldungen sind deutsch (benutzersichtbar). |
| `RuleImportExportService` | API | CSV-/XLSX-/JSON-Parsing und -Erzeugung, Matcher-Normalisierung. |
| `BruteForceProtectionService` | API | Erfasst fehlgeschlagene Logins pro IP und sperrt sie. |

Sämtliche Services und Repositories sind als **Singleton** registriert — die Caches müssen es sein, der Rest ist zustandslos.

---

## 4. Ablauf eines Requests

Middleware-Reihenfolge gemäss [`Program.cs`](Broot.Redirect.API/Program.cs):

```
UseForwardedHeaders          X-Forwarded-For / -Proto (nötig hinter Azure Ingress)
Swagger                      nur in Development
UseHttpsRedirection
SPA-Fallback                 Liefert index.html bei 200 und 404 ausserhalb von /api
UseStaticFiles
UseRouting
no-store Cache-Header        Wird auf /api/*-Responses gesetzt
RateLimitMiddleware          Pro IP, drei Stufen
CsrfProtectionMiddleware     Origin-/Referer-Prüfung bei unsicheren /api-Methoden
UseSession
AdminSessionMiddleware       Pfadbasierte Auth-Durchsetzung
MapControllers
RedirectMiddleware           Terminal — die eigentliche Weiterleitungslogik
```

### Wie ein Weiterleitungs-Request durchläuft

Ein eingehender Request auf einen alten Dokument-Link, z. B. `GET /sites/alt/Dokumente/bericht.pdf`:

1. Er ist weder `/api/*` noch eine bekannte SPA-Route, also übernimmt die `RedirectMiddleware`.
2. `RuleMatchingService.ResolveMatch` ermittelt die beste Regel.
3. **Regel getroffen und `AutoRedirect` ist true** → `UrlTransformService` baut das Ziel, ein Tracking-Eintrag mit `Feedback = "auto-redirect"` wird geschrieben, ein `302` geht zurück.
4. **Regel getroffen und `AutoRedirect` ist false** → die Middleware fällt durch, der SPA-Fallback liefert `index.html`, die Angular-Info-Seite lädt. Diese ruft anschliessend `GET /api/redirect/resolve?path=…` auf, um dasselbe Match-Resultat zu holen und darzustellen, und sendet selbst `POST /api/track`.
5. **Kein Treffer** → `NoMatchBehavior` entscheidet (Standard-Weiterleitung, Smart Search oder Durchfallen zur Info-Seite).

Die `RedirectMiddleware` überspringt `/`, `/api/*` sowie die fest hinterlegte SPA-Routenliste (`/login`, `/rules`, `/global-rules`, `/settings`, `/import`, `/stats`, `/blocked-ips`, `/validate`).

> **Wichtig:** Wer eine neue Admin-Route in `app.routes.ts` ergänzt, muss sie zwingend auch im Array `SpaRoutes` in [`RedirectMiddleware.cs`](Broot.Redirect.API/Middleware/RedirectMiddleware.cs) eintragen — sonst behandelt die Applikation die neue Admin-Seite selbst als weiterzuleitende alte URL.

Zu beachten: Auf dem Info-Seiten-Pfad läuft das Matching **zweimal** — einmal in der Middleware (um über Auto-Redirect zu entscheiden) und einmal über `/api/redirect/resolve` für die SPA. Beide Male handelt es sich um reine Cache-Zugriffe, es ist also günstig; beim Debuggen sollte man es aber wissen.

---

## 5. Die Matching-Engine

[`RuleMatchingService.ResolveMatch`](Broot.Redirect.Core/Services/RuleMatchingService.cs) durchläuft drei Phasen und kehrt beim ersten Treffer zurück:

### Phase 1 — Wildcard, O(1)

Der Request-Pfad wird normalisiert (dekodiert, optional in Kleinbuchstaben, abschliessender Slash entfernt) und im Wildcard-Index nachgeschlagen. Der Index ist **allein auf den Pfad geschlüsselt** — die Query des Matchers ist nicht Teil des Schlüssels. Mehrere Wildcard-Regeln können sich daher denselben Pfad teilen und sich nur in ihrer Query unterscheiden; der Lookup liefert entsprechend eine **Kandidatenliste**.

Jeder Kandidat wird geprüft: Trägt sein Matcher eine Query, muss der Request diese erfüllen, sonst fällt er raus. Der Score beträgt `1000 + queryPairs × WeightQueryPair (+ BonusExactMatch bei exaktem Treffer)`; die Qualität ist 100, wenn die Anzahl Query-Paare exakt übereinstimmt, sonst 75.

Bleiben mehrere Kandidaten übrig, gewinnt in dieser Reihenfolge: höherer Score → mehr getroffene Query-Paare → längerer Matcher → älteres `CreatedAt`, zuletzt die Regel-ID.

### Phase 2 — Partial und Domain, lineare Suche

Alle Partial- und Domain-Regeln (vorsortiert, längster Matcher zuerst) werden in normalisierte Pfadsegmente und Query-Maps vorverarbeitet und dann bewertet:

- **Domain-Regeln** vergleichen den Hostnamen auf Gleichheit. Score `1000 + queryPairs × WeightQueryPair`.
- **Pfad-Regeln** schieben das Segment-Array der Regel über die Segmente des Requests, an jeder möglichen Startposition. Pro Segment gilt:
  - `*` oder `:param` → zählt als Wildcard.
  - `prefix*` → Präfix-Treffer, zählt als statischer Treffer und setzt das Flag für Teilsegment-Treffer.
  - Andernfalls ist exakte Zeichenkettengleichheit erforderlich.

  Score: `staticMatches × WeightPathSegment + queryPairs × WeightQueryPair + wildcards × PenaltyWildcard + (isExact ? BonusExactMatch : 0)`.

Das Query-Matching ist gerichtet: **jedes** Key/Value-Paar der Regel muss im Request vorhanden sein, der Request darf aber zusätzliche mitbringen (was die Qualität auf 75 senkt).

### Phase 3 — Regex, lineare Suche

Die vorkompilierten Regexes werden in Cache-Reihenfolge gegen die vollständige Request-URL geprüft. Der erste Treffer gewinnt, mit fixem Score 500 und Qualität 100. Eine `RegexMatchTimeoutException` wird verschluckt, die Engine geht zum nächsten Muster über.

### Auflösung von Gleichstand

Erzielen zwei Kandidaten denselben Score, entscheidet [`CompareToCandidate`](Broot.Redirect.Core/Services/RuleMatchingService.cs) in dieser Reihenfolge:

1. Höherer Score
2. Mehr getroffene statische Segmente
3. Mehr getroffene Query-Paare
4. **Weniger** Wildcards
5. Längerer Matcher (ausser der bisherige Beste ist ein exakter Treffer)
6. Älteres `CreatedAt`
7. Ordinaler Vergleich der Regel-ID — garantiert einen deterministischen Sieger

### Berechnung der Qualität

Wird nach der Auswahl des Siegers in `CalculateQuality` bestimmt:

- Domain-Regel → 100, bzw. 75 bei zusätzlichen Query-Parametern im Request.
- Pfad-Regel, die an einer Startposition ungleich null greift, oder weniger Segmente abdeckt als der Request hat, oder ein `prefix*`-Segment nutzt → **50**.
- Sonst 75 bei zusätzlichen Query-Parametern, andernfalls 100.

### Überlappungsprüfung von Matchern

Beim Anlegen oder Ändern einer Regel weist `RuleCacheService.FindOverlappingMatcher` Matcher zurück, die hierarchisch ein Pfadsegment-Präfix eines bestehenden Matchers sind (oder umgekehrt) — etwa `/a/b` im Konflikt mit `/a/b/c`. Gleich lange Matcher und Regex-Regeln werden übersprungen. Die API antwortet mit `409 Conflict`, `code: "MATCHER_CONFLICT"` und der konfliktierenden Regel; das Admin-UI zeigt dies in einem Dialog an. Exakte Duplikate ergeben `400`.

---

## 6. Die URL-Transformations-Pipeline

[`UrlTransformService.ResolveTargetUrlCore`](Broot.Redirect.Core/Services/UrlTransformService.cs) wendet die folgenden Schritte in **genau dieser Reihenfolge** an:

```
1. Basis-Auflösung          nach RedirectType (Partial / Wildcard / Domain / Fallback)
2. Regel-Suchen/Ersetzen    rule.SearchAndReplace, in Listenreihenfolge
3. Globale Regeln           alle GlobalRules, nach Priority aufsteigend
4. Query-Strategie          ForwardQueryParams  ODER  DiscardQueryParams + KeptQueryParams
5. Statische Query-Params   rule.StaticQueryParams werden angehängt
```

Die Reihenfolge ist relevant und eine häufige Überraschungsquelle — globale Regeln laufen **nach** den regelspezifischen Ersetzungen, und statische Parameter werden immer zuletzt angehängt.

### Basis-Auflösung nach Typ

- **Partial** — bei einem Domain-Matcher wird das Ziel zu `targetBase + originalPath`. Bei einem Pfad-Matcher wird der Matcher im (URL-dekodierten, Gross-/Kleinschreibung ignorierenden) alten Pfad lokalisiert und durch das Ziel ersetzt; alles davor und danach bleibt erhalten. Wird der Matcher überhaupt nicht gefunden, ist das Resultat schlicht `cleanDomain + "/" + target`.
- **Wildcard** — der abschliessende `*` des Matchers wird entfernt, der verbleibende Suffix des alten Pfads wird an das Ziel angehängt. Der Suffix wird dabei in Pfad, Query und Fragment zerlegt und an der jeweils richtigen Stelle der Ziel-URL eingefügt: Der Pfadanteil kommt vor eine allfällige Query des Ziels, Query-Anteile werden zusammengeführt statt aneinandergehängt. Prozentkodierungen aus der Quelle bleiben erhalten, obwohl der Abgleich auf dekodiertem Text läuft. Absolute Ziele (`http…`) werden unverändert übernommen, relative Ziele mit der Standard-Domain präfixiert.
- **Domain** — Schema und Host der Original-URL werden per Regex durch die Ziel-Domain ersetzt, Pfad und Query bleiben erhalten. `http://` wird auf `https://` angehoben.

### Suchen und Ersetzen

Sowohl regelspezifische als auch globale Ersetzungen arbeiten **literal, nicht als Regex** — der Suchbegriff läuft zuerst durch `Regex.Escape`. `CaseSensitive` steuert `RegexOptions.IgnoreCase`. Ein fehlerhaftes Muster wird abgefangen und die URL unverändert zurückgegeben; ein fehlerhafter Eintrag degradiert also still, statt eine Exception zu werfen.

### Umgang mit der Query

Die drei Schalter greifen wie folgt ineinander:

| Schalter | Ergebnis |
|---|---|
| `ForwardQueryParams = true` | Die ursprüngliche Query wird vollständig angehängt. Hat Vorrang vor allem anderen. Damit sie nicht doppelt erscheint, wird sie bereits in der Basis-Auflösung aus dem Pfad entfernt und erst in diesem Schritt wieder angefügt. |
| `DiscardQueryParams = true` + nicht leere `KeptQueryParams` | Nur Parameter, die auf die Regex-Positivliste passen, werden übernommen. Jeder Eintrag hat ein `KeyPattern`, optional ein `ValuePattern`, optional einen `TargetKey` (Umbenennung) sowie `SkipEncoding`. Jeder Quellparameter wird höchstens einmal verwendet. |
| `DiscardQueryParams = true`, keine Kept-Params | Query wird vollständig verworfen. |
| Keiner von beiden | Die Query bleibt so, wie sie die Basis-Auflösung erzeugt hat. |

`StaticQueryParams` werden unabhängig davon angehängt, nach allen oben genannten Schritten.

### Tracing

`ResolveTargetUrlWithTrace` liefert dasselbe Resultat plus eine `List<UrlTraceStep>`, die jeden Schritt festhält, der die URL tatsächlich verändert hat. Darauf basiert die Seite **Validieren** im Admin-UI — das Mittel der Wahl, um zu debuggen, warum eine Regel eine unerwartete URL erzeugt.

---

## 7. Caching

**Alle Lesepfade laufen über den Arbeitsspeicher; Table Storage wird nur bei Schreibzugriffen und beim Start berührt.** Genau das macht den heissen Weiterleitungspfad schnell.

[`RuleCacheService`](Broot.Redirect.Infrastructure/Cache/RuleCacheService.cs) unterhält vier Strukturen, die gemeinsam unter einem `ReaderWriterLockSlim` neu aufgebaut werden:

| Struktur | Zweck |
|---|---|
| `_rulesById` | `ConcurrentDictionary<Guid, RedirectRule>` — die Quelle der Wahrheit. |
| `_wildcardIndex` | `Dictionary<string, List<RedirectRule>>` — O(1)-Zugriff, geschlüsselt auf den reinen Pfad. Regeln mit gleichem Pfad und unterschiedlicher Query landen in derselben Liste. |
| `_partialAndDomainRules` | `List<RedirectRule>`, absteigend nach Matcher-Länge sortiert. |
| `_regexRules` | Vorkompilierte `Regex`-/Regel-Paare. Ungültige Muster werden geloggt und übersprungen, nicht geworfen. |

Das Schreibmuster ist überall **zuerst Storage, dann Cache** — z. B. `await _repository.CreateAsync(rule); _cacheService.AddRule(rule);`. Wirft der Storage-Zugriff eine Exception, bleibt der Cache unberührt und damit konsistent.

Der `CacheWarmupService` läuft beim Start: Er stellt sicher, dass alle vier Tabellen existieren, lädt die Regeln, legt Standard-`AppSettings` an, falls der Datensatz fehlt, und lädt die globalen Regeln. `IsWarmedUp` wird über `/api/health` nach aussen gegeben.

> **Einschränkung bei der Skalierung:** Der Cache ist instanzlokal und es gibt keinen Invalidierungskanal zwischen Instanzen. Bei **mehr als einer Replica** landet die Regeländerung eines Admins nur in derjenigen Instanz, die den Request bedient hat; die übrigen liefern weiterhin veraltete Regeln aus, bis sie neu starten. Die Applikation ist als **Single-Instance** zu betreiben, solange kein Invalidierungsmechanismus ergänzt wird.

---

## 8. Datenmodell und Storage

Azure Table Storage, vier Tabellen. Nur der Name der Regeltabelle ist konfigurierbar, die anderen drei sind in ihren Repositories fest verdrahtet.

| Tabelle | PartitionKey | RowKey | Anmerkungen |
|---|---|---|---|
| `RedirectRules` (konfigurierbar über `AzureTableStorage__TableName`) | `"rule"` — konstant | Regel-GUID (Format `N`) | Einzelne Partition. Komplexe Felder (`KeptQueryParams`, `StaticQueryParams`, `SearchAndReplace`) werden als JSON-Strings abgelegt. |
| `AppSettings` | `"Settings"` | `"default"` | Ein einziger Datensatz. Wird beim ersten Start mit Standardwerten angelegt. |
| `GlobalRules` | konstant | Regel-GUID | Kleine Tabelle, vollständig gecacht. |
| `Tracking` | `yyyy-MM-dd` (Datum des Eintrags) | Eintrags-GUID | Nach Datum partitioniert, für effiziente Bereichsabfragen und günstiges Löschen abgelaufener Daten. Kein Cache — schreiblastig, wird selten gelesen. |

Das Mapping zwischen Entity und Domain-Modell liegt in den `*Entity.cs`-Klassen (`FromDomainModel` / `ToDomainModel`) und ist durch [`EntityMappingTests`](Broot.Redirect.Tests/Infrastructure/Persistence/EntityMappingTests.cs) abgedeckt.

Die Einzel-Partition für Regeln ist Absicht — die ganze Tabelle wird beim Start einmal geladen, Parallelität über Partitionen bringt also nichts, und eine einzelne Partition macht `GetAllAsync` zu einem sauberen Scan. Sie begrenzt den Schreibdurchsatz, was bei einem von Hand gepflegten Regelwerk unproblematisch ist.

Tracking-Abfragen filtern über einen PartitionKey-Bereich (`PartitionKey ge '<from>' and le '<to>'`) und erledigen die restliche Filterung, Sortierung und Paginierung im Arbeitsspeicher. Für die Datenmengen, auf die diese Applikation ausgelegt ist, ist das vertretbar — es ist aber der erste Punkt, der wehtut, wenn das Tracking-Volumen deutlich wächst.

---

## 9. Sicherheit

### Authentifizierung

Ein einziges gemeinsames Admin-Passwort, keine Benutzerkonten. `POST /api/auth/login` vergleicht SHA-256-Hashes mittels `CryptographicOperations.FixedTimeEquals` (laufzeitkonstant). Bei Erfolg wird ein ASP.NET-Core-Session-Cookie `admin_session` ausgestellt — `HttpOnly`, `SameSite=Lax`, in Produktion `Secure`, Leerlauf-Timeout `SessionTimeoutDays` (Standard 7).

> Verglichen wird der Hash eines Klartextwerts aus der Konfiguration — das ist kein Passwort-*Hashing* im Sinne sicherer Speicherung (kein Salt, keine KDF). Für ein geteiltes Betriebspasswort aus einer Umgebungsvariable bzw. dem Key Vault ist das angemessen; es ist aber nicht mit dem Umgang mit Benutzer-Credentials zu verwechseln.

### Autorisierung

[`AdminSessionMiddleware`](Broot.Redirect.API/Middleware/AdminSessionMiddleware.cs) setzt die Authentifizierung über **Pfadpräfixe** durch und antwortet mit `403` und einem JSON-Body, wenn das Session-Flag fehlt:

| Pfad | Geschützt |
|---|---|
| `/api/rules*`, `/api/global-rules*`, `/api/stats*` | Ja, alle Methoden |
| `/api/settings` | Nur `PUT` — `GET` ist öffentlich (die Info-Seite braucht die Texte) |
| `/api/auth/status`, `/api/auth/logout`, `/api/auth/blocked-ips*` | Ja |
| `/api/auth/login`, `/api/track`, `/api/feedback`, `/api/redirect/resolve`, `/api/health` | Öffentlich |

Es handelt sich um eine Positivliste von Pfaden, nicht um attributbasierte Autorisierung. **Neue Admin-Endpunkte sind standardmässig öffentlich** — beim Anlegen eines Controllers ist dessen Präfix in `RequiresAuthentication` zu ergänzen.

### CSRF

[`CsrfProtectionMiddleware`](Broot.Redirect.API/Middleware/CsrfProtectionMiddleware.cs) prüft unsichere Methoden (alles ausser GET/HEAD/OPTIONS) auf `/api/*`: Der `Origin`-Header muss zum `Host` des Requests passen; fehlt er, wird `Referer` geprüft; fehlen beide, wird der Request mit `403` abgewiesen. Es gibt kein Token — es ist eine Header-Origin-Validierung, die funktioniert, weil die SPA same-origin ausgeliefert wird.

Das bedeutet: **Nicht-Browser-Clients (curl, Postman, Skripte) müssen einen zum Host passenden `Origin`-Header senden**, sonst scheitert jedes POST/PUT/DELETE mit 403.

### Rate Limiting

[`RateLimitMiddleware`](Broot.Redirect.API/Middleware/RateLimitingMiddleware.cs) — festes Zeitfenster, pro IP, im Arbeitsspeicher, drei Stufen:

| Stufe | Pfade | Standard |
|---|---|---|
| Tracking | `POST /api/track`, `POST /api/feedback` | 300 / min |
| Admin | `/api/rules*`, `/api/global-rules*`, `/api/stats*`, `PUT /api/settings` | 60 / min |
| Global | alles Übrige unter `/api/` | 300 / min |

Responses tragen `X-RateLimit-Limit`, `-Remaining`, `-Reset`; bei `429` kommt `Retry-After` dazu. Die Zähler sind statisch und prozesslokal, werden also bei einem Neustart zurückgesetzt und nicht über Instanzen hinweg geteilt.

### Brute-Force-Schutz

[`BruteForceProtectionService`](Broot.Redirect.API/Services/BruteForceProtectionService.cs) — nach `LoginMaxAttempts` (5) Fehlversuchen wird eine IP für `LoginBlockDurationMinutes` (1440 = 24 h) gesperrt. Admins können IPs über die Seite **Blockierte IPs** auflisten, manuell sperren und entsperren. Der Zustand liegt nur im Arbeitsspeicher und geht beim Neustart verloren.

### Weiteres

- `UseForwardedHeaders` ist aktiviert, damit Client-IPs hinter dem Azure Ingress korrekt sind. **Achtung:** Rate Limiting und Brute-Force-Schutz verwenden `context.Connection.RemoteIpAddress`, nicht den weitergereichten Header.
- Alle `/api/*`-Responses werden mit `Cache-Control: no-store` ausgeliefert.
- Der Hinweistext der Info-Seite wird über [`LinkifyPipe`](Broot.Redirect.Client/src/app/shared/pipes/linkify.pipe.ts) gerendert. Diese maskiert *zuerst* den gesamten String als HTML und wandelt erst danach `http(s)`-URLs in Anker um, bevor sie Angulars Sanitizer umgeht. Diese Reihenfolge muss bei Änderungen erhalten bleiben.

---

## 10. API-Referenz

Alle Endpunkte liefern camelCase-JSON. Enums werden als camelCase-Strings serialisiert.

### Öffentlich

| Methode | Pfad | Beschreibung |
|---|---|---|
| `GET` | `/api/health` | Status, Version, Uptime, Anzahl Regeln, Prüfungen von Table Storage und Cache. `200` gesund, `503` ungesund. |
| `GET` | `/api/settings` | Aktuelle Laufzeit-Einstellungen (wird von der Info-Seite benötigt). |
| `GET` | `/api/redirect/resolve?path=…` | Löst einen Pfad zu Regel und Ziel-URL auf. Liefert Match-Qualität, Stufe und Informationen zum Smart-Search-Fallback. `404`, wenn nichts greift. |
| `POST` | `/api/track` | Erfasst einen Aufruf. Liefert die Tracking-ID für einen späteren Feedback-Aufruf zurück. |
| `POST` | `/api/feedback` | Hängt `OK`/`NOK`-Feedback und optional eine vorgeschlagene URL an einen Tracking-Eintrag. |
| `POST` | `/api/auth/login` | `{ password }` → setzt das Session-Cookie. `401` falsches Passwort, `429` gesperrt. |

### Regeln (Admin)

| Methode | Pfad | Beschreibung |
|---|---|---|
| `GET` | `/api/rules?page=&limit=&search=&sortBy=&sortOrder=` | Paginierte Liste. `limit` maximal 500, Standard 50. Wird aus dem Cache bedient. |
| `GET` | `/api/rules/{id}` | Einzelne Regel. |
| `POST` | `/api/rules` | Anlegen. `400` bei doppeltem Matcher, `409` `MATCHER_CONFLICT` bei hierarchischer Überlappung. |
| `PUT` | `/api/rules/{id}` | Teilaktualisierung — Felder mit `null` behalten ihren bisherigen Wert. `Source` bleibt erhalten; `RedirectType` ändert sich nur bei expliziter Angabe. |
| `DELETE` | `/api/rules/{id}` | Löscht eine Regel. |
| `DELETE` | `/api/rules/bulk` | Body `{ ids: [...] }`. Liefert `{ deleted, notFound }`. |
| `DELETE` | `/api/rules/all` | Startet einen asynchronen Job. Liefert `{ jobId, total }`. |
| `GET` | `/api/rules/jobs/{jobId}` | Fortschritt eines Jobs abfragen. Jobs verfallen 5 Minuten nach Abschluss. |
| `POST` | `/api/rules/import/preview` | Parst einen Upload und klassifiziert jeden Eintrag als `new` / `update` / `unchanged` / `invalid`. Vorschau auf 1000 Einträge begrenzt. |
| `POST` | `/api/rules/import` | Startet einen asynchronen Import-Job. Liefert `{ jobId, total }`. |
| `GET` | `/api/rules/export?format=json\|csv\|xlsx` | Datei-Download. |
| `POST` | `/api/rules/validate` | Body `{ urls: [...] }`, maximal 500. Liefert pro URL das Match-Resultat plus einen vollständigen Transformations-Trace. |

Lang laufende Operationen (Import, Alles löschen) verwenden das Muster **Fire-and-Forget-Job plus Polling**: Der Endpunkt liefert sofort eine `jobId`, die Arbeit läuft in einem Background Task weiter, und der Client fragt `/api/rules/jobs/{jobId}` ab. Der Job-Zustand liegt in einem statischen In-Memory-Dictionary und überlebt keinen Neustart.

### Globale Regeln (Admin)

`GET`, `POST` auf `/api/global-rules`; `GET`, `PUT`, `DELETE` auf `/api/global-rules/{id}`. Einfaches CRUD.

### Einstellungen

| Methode | Pfad | Beschreibung |
|---|---|---|
| `GET` | `/api/settings` | Öffentlich. |
| `PUT` | `/api/settings` | Admin. Teil-Merge — nur Felder ungleich `null` werden übernommen. Validiert `NoMatchBehavior` und `PopupMode`. |

### Statistiken (Admin)

| Methode | Pfad | Beschreibung |
|---|---|---|
| `GET` | `/api/stats?timeRange=24h\|7d\|all` | Summen, Trefferquote, Feedback-Aufschlüsselung, Top-10-Regeln. |
| `GET` | `/api/stats/entries?page=&limit=&search=&qualityMin=&qualityMax=&feedbackType=&ruleId=` | Paginierte Rohdaten. `limit` maximal 200. |
| `GET` | `/api/stats/entries/export?format=csv\|json` | Export mit denselben Filtern. |
| `GET` | `/api/stats/trend?days=30&aggregation=day\|week\|month` | Feedback-Zahlen pro Zeitraum für das Trend-Diagramm. |
| `DELETE` | `/api/stats/all` | Löscht sämtliche Tracking-Daten. |

### Auth (Admin)

`GET /api/auth/status`, `POST /api/auth/logout` sowie `GET`/`POST`/`DELETE` auf `/api/auth/blocked-ips` (zusätzlich `DELETE /api/auth/blocked-ips/{ip}`).

Die Swagger-UI ist unter `/swagger` verfügbar — **nur in Development**.

---

## 11. Frontend

Angular 19, Standalone Components, Signals für den Zustand, lazy geladene Routen. Keine UI-Komponentenbibliothek — reines CSS pro Komponente. Die einzigen Laufzeitabhängigkeiten sind Angular selbst, RxJS und zone.js.

### Routen

| Route | Komponente | Zugriff |
|---|---|---|
| `/` und `**` | `InfoPageComponent` | Öffentlich — die Wildcard-Route sorgt dafür, dass jede alte URL die Info-Seite rendert. |
| `/login` | `LoginComponent` | Öffentlich |
| `/rules` | `RulesListComponent` (+ `RuleModalComponent`) | Admin |
| `/global-rules` | `GlobalRulesComponent` | Admin |
| `/settings` | `SettingsComponent` | Admin |
| `/import` | `ImportComponent` | Admin |
| `/stats` | `StatsComponent` | Admin |
| `/blocked-ips` | `BlockedIpsComponent` | Admin |
| `/validate` | `ValidateComponent` | Admin |

### Info-Seite

[`InfoPageComponent`](Broot.Redirect.Client/src/app/features/info-page/info-page.component.ts) ist das öffentliche Gesicht der Applikation. Beim Laden liest sie den Pfad aus `window.location` (unter Entfernung des Base-Href), holt die Einstellungen, ruft `/api/redirect/resolve` auf und stellt dar:

- Alte und neue URL mit den im Admin-UI konfigurierten Beschriftungen
- Eine Schaltfläche zum Kopieren und eine zum Öffnen in einem neuen Tab
- Eine Qualitätsanzeige (grün/gelb/rot) mit dem passenden Erklärungstext — über `ShowLinkQualityGauge` ausblendbar
- Regelspezifischen oder globalen Hinweistext, mit klickbar gemachten URLs
- Optional eine Feedback-Umfrage (`OK`/`NOK`) nach dem ersten Kopieren/Öffnen, mit optionalem Freitextschritt zum Vorschlagen der korrekten URL

`PopupMode` schaltet zwischen `inline` (Vergleich sofort sichtbar) und `active` (hinter einem schliessbaren Dialog).

Ein dezentes Zahnrad-Symbol prüft den Auth-Status und öffnet entweder das Admin-Panel oder ein Passwort-Modal — so gelangen Admins von einer öffentlichen URL aus ins Panel.

### Admin-Seiten

- **Regeln** — paginierte, durchsuchbare und sortierbare Tabelle mit verstellbaren Spaltenbreiten, Mehrfachauswahl für das Massenlöschen sowie ein Modal-Editor, der sämtliche Regelfelder abdeckt, inklusive der Listen für Query-Parameter und Suchen/Ersetzen.
- **Validieren** — eine Liste von URLs einfügen und pro URL die getroffene Regel, den Score, die Qualität und einen Schritt-für-Schritt-Trace der Transformation erhalten. Das wichtigste Debugging-Werkzeug.
- **Import** — Datei-Upload mit Vorschauschritt (Zahlen zu neu/aktualisiert/unverändert/ungültig) vor dem Übernehmen, dazu ein Fortschrittsbalken auf Basis des Job-Pollings.
- **Statistiken** — Kennzahlen-Kacheln, Zufriedenheits-Trenddiagramm und eine filterbare Eintragstabelle mit Export.
- **Einstellungen** — alle Laufzeit-Einstellungen inklusive sämtlicher Texte der Info-Seite.
- **Globale Regeln** und **Blockierte IPs** — einfache CRUD-Tabellen.

### Erwähnenswerte Frontend-Details

- `authGuard` gibt aktuell **bedingungslos `true` zurück**; die Absicherung der Admin-Routen erfolgt serverseitig.
- `authInterceptor` fängt `403`-Responses ab und navigiert nach `/login`, wobei die Login- und Status-Endpunkte ausgenommen sind, um Schleifen zu vermeiden. Faktisch setzt dies die Weiterleitung zum Login durch.
- Die Applikationsversion wird beim Docker-Build als `<meta name="app-version">` in `index.html` injiziert (`__APP_VERSION__` wird per `sed` ersetzt), mit `/api/health` als Fallback für die Entwicklung.
- `proxy.conf.js` leitet `/api` während `ng serve` an `https://localhost:7233` weiter.

---

## 12. Import / Export

[`RuleImportExportService`](Broot.Redirect.API/Services/RuleImportExportService.cs) verarbeitet CSV (CsvHelper), XLSX (ClosedXML) und JSON.

**Die Spaltenzuordnung ist tolerant** — jedes Feld akzeptiert mehrere Schreibweisen der Kopfzeile, auch deutsche. Exporte aus anderen Werkzeugen lassen sich daher oft ohne Nachbearbeitung importieren:

| Feld | Akzeptierte Spaltenüberschriften |
|---|---|
| Matcher | `Matcher`, `matcher`, `Quelle`, `Source` |
| Ziel-URL | `Target URL`, `targetUrl`, `TargetUrl`, `Ziel`, `Target` |
| Typ | `Type`, `redirectType`, `RedirectType`, `Typ` |
| Info | `Info`, `infoText`, `InfoText`, `Beschreibung` |
| Auto Redirect | `Auto Redirect`, `autoRedirect`, `AutoRedirect`, `Automatisch` |
| … | (vollständige Liste siehe `ColumnMapping`) |

Die Listenfelder (`KeptQueryParams`, `StaticQueryParams`, `SearchAndReplace`) werden als **JSON-String in einer einzelnen Zelle** hin- und zurückgeschrieben.

Verhalten beim Import:

- Einträge werden bestehenden Regeln **zuerst über die ID, dann über den Matcher** zugeordnet — ein erneuter Import eines Exports aktualisiert also, statt zu duplizieren. Der Matcher-Abgleich läuft über eine kanonisierte Form (`CanonicalizeMatcher`: prozentkodiert, getrimmt, ohne überflüssigen Schluss-Slash), damit unterschiedliche Schreibweisen derselben URL zusammenfinden.
- **Der Import ist idempotent:** Deckt sich ein Eintrag inhaltlich vollständig mit der bestehenden Regel, wird er als `unchanged` gezählt und übersprungen — es gibt keinen Schreibvorgang. Verglichen werden Matcher, Ziel-URL, Typ, Info-Text, die drei Query-Schalter sowie die Listen `KeptQueryParams`, `StaticQueryParams` und `SearchAndReplace`.
- Der Matcher-Index wird **während des Laufs** fortgeschrieben. Kommt derselbe Matcher mehrfach in einer Datei vor, greift ab dem zweiten Vorkommen die bereits angelegte Regel, statt ein Duplikat zu erzeugen.
- Das `CreatedAt` einer bestehenden Regel bleibt bei einer Aktualisierung erhalten, ebenso deren `Source`. Nur **neu** angelegte Regeln erhalten `Source = Import`; eine manuell erstellte Regel bleibt also `Manual`, auch wenn sie per Import aktualisiert wird.
- Existiert die Regel bereits, gewinnt deren ID — eine abweichende `Id` in der Importdatei wird dann ignoriert.
- Matcher werden normalisiert (getrimmt, abschliessender Slash entfernt, ausser der Matcher endet auf `*`), und Pfadsegmente werden prozentkodiert, unter Erhalt der Struktur.
- Jeder Eintrag wird einzeln validiert; Fehlschläge landen in der `Errors`-Liste des Jobs, die übrigen Einträge werden dennoch importiert.
- CSV-Werte werden beim Export gegen Formel-Injection abgesichert und beim Import wieder zurückgewandelt.

---

## 13. Lokale Entwicklung

### Voraussetzungen

- .NET 8 SDK
- Node.js 22
- [Azurite](https://learn.microsoft.com/en-us/azure/storage/common/storage-use-azurite) (Azure-Storage-Emulator) oder Docker

### Variante 1: Docker Compose (empfohlen)

```bash
docker-compose up
```

Startet die Applikation auf `http://localhost:8080` sowie einen Azurite-Container. Weitere Einrichtung ist nicht nötig.

### Variante 2: Komponenten einzeln starten

Azurite starten (sofern nicht Docker verwendet wird):

```bash
azurite --tableHost 127.0.0.1 --tablePort 10002
```

API starten:

```bash
dotnet run --project Broot.Redirect.API
```

Die API läuft auf `https://localhost:7233`.

Angular-Abhängigkeiten installieren und Dev-Server starten:

```bash
npm install --prefix Broot.Redirect.Client
```

```bash
npm start --prefix Broot.Redirect.Client
```

Das Frontend läuft auf `http://localhost:4200` und leitet `/api` an das Backend weiter.

> In `AzureTableStorage__ConnectionString` wird ein lokaler Table-Storage-Connection-String benötigt (Umgebungsvariable oder User Secrets). Der Entwicklungswert für Azurite lautet:
> `DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;TableEndpoint=http://127.0.0.1:10002/devstoreaccount1;`

### Standard-Login

Das Standard-Admin-Passwort lautet `Password1`; solange es in Verwendung ist, gibt die Applikation beim Start eine Warnung aus. Änderbar über `SmartRedirect__AdminPassword`.

### Ein Feature ergänzen — wo was hingehört

| Aufgabe | Diese Stellen anpassen |
|---|---|
| Neues Regelfeld | `RedirectRule` → `RedirectRuleEntity` (inkl. Mapping) → `CreateRuleRequest`/`UpdateRuleRequest` → `RulesController` → `RuleImportExportService` (CSV-/XLSX-Spalten) → Angular-Modell und `rule-modal.component` |
| Neue Laufzeit-Einstellung | `AppSettings` → `AppSettingsEntity` (inkl. Mapping) → `UpdateSettingsRequest` → Merge im `SettingsController` → Angular-`AppSettings`-Modell und `settings.component` |
| Neue Admin-Seite | `app.routes.ts` **und** `SpaRoutes` in `RedirectMiddleware.cs` **und** `RequiresAuthentication` in `AdminSessionMiddleware.cs` (falls die Seite ein eigenes API-Präfix hat) |
| Matching-Verhalten ändern | `RuleMatchingService` in Core — seiteneffektfrei und intensiv unit-getestet; Fälle in `RuleMatchingServiceTests` ergänzen |

---

## 14. Tests

624 Testmethoden (`[Fact]` und `[Theory]`) über Unit- und Integrationstests hinweg.

```bash
dotnet test
```

- **Framework:** xUnit, mit NSubstitute für Mocks und FluentAssertions für Zusicherungen.
- **Coverage:** AltCover ist im Testprojekt verdrahtet und schreibt `Broot.Redirect.Tests/coverage.xml`. `Program.cs` und Fremdassemblies werden herausgefiltert, `[ExcludeFromCodeCoverage]` wird berücksichtigt. Die CI rendert eine Markdown-Zusammenfassung in die Job Summary.
- **Integrationstests** ([`Broot.Redirect.Tests/Integration/`](Broot.Redirect.Tests/Integration/)) laufen über `AzuriteFixture` gegen eine echte Azurite-Instanz auf `127.0.0.1:10002`, in der xUnit-Collection `"Azurite"`. Tabellen werden pro Test angelegt und beim Dispose gelöscht. **Ohne laufendes Azurite schlagen diese Tests fehl** — vorher Azurite starten (oder `docker-compose up azurite`).
- Frontend-Tests sind aufgesetzt (Karma/Jasmine über `ng test`), es existieren jedoch keine Spec-Dateien.

Die Teststruktur spiegelt den Quellbaum: `Tests/Core/Services/`, `Tests/API/Controllers/`, `Tests/API/Middleware/`, `Tests/Infrastructure/Cache/`, `Tests/Infrastructure/Persistence/`, `Tests/Integration/`.

---

## 15. Konfigurationsreferenz

Die Konfiguration folgt den ASP.NET-Core-Konventionen. Jeder Wert aus `appsettings.json` lässt sich über die Doppelunterstrich-Schreibweise überschreiben (z. B. `SmartRedirect__AdminPassword`).

**Alles in diesem Abschnitt erfordert einen Neustart.** Zur Laufzeit editierbare Einstellungen liegen im Admin-Panel, nicht hier.

### Grundeinstellungen (`SmartRedirect__*`)

| Variable | Beschreibung | Standard |
|---|---|---|
| `SmartRedirect__AdminPassword` | Passwort für das Admin-Panel (Vergleich über SHA256) | `Password1` |
| `SmartRedirect__SessionTimeoutDays` | Leerlauf-Timeout der Admin-Session in Tagen | `7` |
| `SmartRedirect__TrackingRetentionDays` | Aufbewahrungsdauer der Tracking-Daten in Tagen | `30` |

### URL-Matching (`SmartRedirect__*`)

| Variable | Beschreibung | Standard |
|---|---|---|
| `SmartRedirect__CaseSensitivePath` | Pfadvergleich unter Beachtung der Gross-/Kleinschreibung | `false` |
| `SmartRedirect__CaseSensitiveQuery` | Query-Parameter-Vergleich unter Beachtung der Gross-/Kleinschreibung | `false` |
| `SmartRedirect__TrailingSlashPolicy` | `ignore` oder `strict` | `ignore` |
| `SmartRedirect__RegexMatchTimeoutSeconds` | Timeout für den Regex-Musterabgleich | `1` |

### Match-Scoring (`SmartRedirect__*`)

| Variable | Beschreibung | Standard |
|---|---|---|
| `SmartRedirect__WeightPathSegment` | Punkte pro getroffenem statischem Pfadsegment | `10` |
| `SmartRedirect__WeightQueryPair` | Punkte pro getroffenem Query-Paar | `5` |
| `SmartRedirect__PenaltyWildcard` | Punktedelta pro Wildcard-Segment — wird **addiert, nicht abgezogen** | `1` |
| `SmartRedirect__BonusExactMatch` | Bonuspunkte für einen exakten Treffer | `50` |

### Rate Limiting (`SmartRedirect__*`)

| Variable | Beschreibung | Standard |
|---|---|---|
| `SmartRedirect__RateLimitGlobalMax` | Maximale Requests pro Zeitfenster (allgemein) | `300` |
| `SmartRedirect__RateLimitTrackingMax` | Maximale Requests pro Zeitfenster (Tracking-Endpunkte) | `300` |
| `SmartRedirect__RateLimitAdminMax` | Maximale Requests pro Zeitfenster (Admin-Endpunkte) | `60` |
| `SmartRedirect__RateLimitWindowSeconds` | Länge des Zeitfensters in Sekunden | `60` |

### Brute-Force-Schutz (`SmartRedirect__*`)

| Variable | Beschreibung | Standard |
|---|---|---|
| `SmartRedirect__LoginMaxAttempts` | Fehlversuche beim Login bis zur Sperrung | `5` |
| `SmartRedirect__LoginBlockDurationMinutes` | Sperrdauer nach Überschreiten der Fehlversuche | `1440` (24 h) |

### Azure Table Storage (`AzureTableStorage__*`)

| Variable | Beschreibung | Standard |
|---|---|---|
| `AzureTableStorage__ConnectionString` | Connection String für Azure Table Storage | leer — **muss gesetzt werden** |
| `AzureTableStorage__TableName` | Tabellenname für die Weiterleitungsregeln | `RedirectRules` |

Die Tabellennamen `AppSettings`, `GlobalRules` und `Tracking` sind fest verdrahtet.

### Telemetrie

| Variable | Beschreibung | Standard |
|---|---|---|
| `APPLICATIONINSIGHTS__CONNECTIONSTRING` | Connection String für Application Insights (optional) | keiner (Telemetrie deaktiviert) |

### ASP.NET Core

| Variable | Beschreibung | Standard |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Development`, `Staging` oder `Production` | `Development` |
| `ASPNETCORE_HTTP_PORTS` | Port-Binding für HTTP | `8080` |

---

## 16. Deployment

Das Dockerfile besteht aus drei Stufen: Node 22 baut die Angular-App, das .NET-8-SDK publiziert die API, und das Runtime-Image kopiert das Angular-Build-Ergebnis nach `wwwroot/`. Das Resultat ist ein einzelner Container, der Port 8080 exponiert. `APP_VERSION` wird als Build-Argument übergeben und landet sowohl in der `InformationalVersion` der Assembly als auch im Versions-Meta-Tag der SPA.

### Azure Container Apps (empfohlen)

1. **Ressourcen anlegen:**

   ```bash
   az group create --name broot-redirect-rg --location westeurope
   ```

   ```bash
   az storage account create --name brootredirectstore --resource-group broot-redirect-rg --sku Standard_LRS --kind StorageV2
   ```

   ```bash
   az containerapp env create --name broot-redirect-env --resource-group broot-redirect-rg --location westeurope
   ```

2. **Connection String des Storage Accounts auslesen:**

   ```bash
   az storage account show-connection-string --name brootredirectstore --resource-group broot-redirect-rg --query connectionString -o tsv
   ```

3. **Container deployen** (die CI-Pipeline pusht die Images nach `ghcr.io`):

   ```bash
   az containerapp create --name broot-redirect --resource-group broot-redirect-rg --environment broot-redirect-env --image ghcr.io/<your-repo>/broot.redirect:latest --target-port 8080 --ingress external --min-replicas 1 --max-replicas 1 --env-vars ASPNETCORE_ENVIRONMENT=Production SmartRedirect__AdminPassword=<your-password> AzureTableStorage__ConnectionString=<connection-string>
   ```

   **`--max-replicas 1` fixieren.** Der Regel-Cache ist instanzlokal und wird zwischen Instanzen nicht invalidiert; ein Ausskalieren liefert veraltete Regeln aus.

### Azure App Service

1. **App Service anlegen:**

   ```bash
   az appservice plan create --name broot-redirect-plan --resource-group broot-redirect-rg --sku B1 --is-linux
   ```

   ```bash
   az webapp create --name broot-redirect --resource-group broot-redirect-rg --plan broot-redirect-plan --container-image-name ghcr.io/<your-repo>/broot.redirect:latest
   ```

2. **Umgebungsvariablen konfigurieren:**

   ```bash
   az webapp config appsettings set --name broot-redirect --resource-group broot-redirect-rg --settings ASPNETCORE_ENVIRONMENT=Production SmartRedirect__AdminPassword=<your-password> AzureTableStorage__ConnectionString=<connection-string> WEBSITES_PORT=8080
   ```

### Checkliste für die Produktion

- `SmartRedirect__AdminPassword` auf einen starken Wert setzen (idealerweise aus dem Key Vault). Solange der Standardwert aktiv ist, wird beim Start eine Warnung geloggt.
- `ASPNETCORE_ENVIRONMENT=Production` setzen — deaktiviert Swagger und erzwingt `Secure`-Session-Cookies.
- `AzureTableStorage__ConnectionString` auf einen echten Storage Account zeigen lassen.
- Anzahl Replicas auf **1** belassen.
- Optional `APPLICATIONINSIGHTS__CONNECTIONSTRING` für das Monitoring setzen.
- Den Health Probe auf `/api/health` richten — der Endpunkt liefert `503`, solange der Cache aufwärmt oder Table Storage nicht erreichbar ist.
- Vor dem Go-live `AppSettings.DefaultNewDomain` im Admin-Panel setzen; der initiale Standardwert ist `https://new.example.com`.

---

## 17. CI/CD und Versionierung

[`.github/workflows`](.github/workflows) — bei Push auf `main` und bei manuellem Auslösen:

1. **test** — restore, build, `dotnet test` gegen einen Azurite-Service-Container, dazu eine AltCover-Coverage-Zusammenfassung in der GitHub Job Summary.
2. **build-and-push** — ermittelt die Version (verwendet ein bestehendes Tag auf `HEAD` wieder, andernfalls Bump über `mathieudutour/github-tag-action` mit `default_bump: false`, sodass **die Version nur bei einer entsprechenden Commit-Message hochgezählt wird**, gemäss Conventional-Commit-Regeln), meldet sich an `ghcr.io` an und baut und pusht das Image, getaggt mit Commit-SHA, Version und `latest`.

Bisher veröffentlichte Tags: `v1.0.0` … `v1.1.2`.

Da `default_bump` auf `false` steht, erzeugen Commits ohne Conventional-Commit-Präfix (`feat:`, `fix:` usw.) kein neues Versions-Tag — das Image wird trotzdem unter seinem SHA gepusht.
