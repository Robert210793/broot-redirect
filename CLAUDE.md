# broot-redirect — CLAUDE.md

## Projekt-Platzhalter

| Platzhalter | Wert |
|-------------|------|
| `{PROJECT_NAME}` | `broot-redirect` |
| `{REQUIREMENTS_DIR}` | `docs/requirements` |
| `{TECHNICAL_DIR}` | `docs/technical` |
| `{USER_DOCS_DIR}` | `docs/user-guide` |
| `{ARCHITECTURE_FILE}` | `README.md` (Kapitel 3–9) |
| `{ADR_DIR}` | `docs/technical/adr` |
| `{LEGACY_DIR}` | `docs/legacy` |
| `{SRC_DIR}` | `.` (Repo-Root, kein `src/`-Ordner) |
| `{BACKEND_DIR}` | `Broot.Redirect.API`, `Broot.Redirect.Core`, `Broot.Redirect.Infrastructure` |
| `{FRONTEND_DIR}` | `Broot.Redirect.Client` (Angular), `Broot.Redirect.SPFx` (SPFx-WebPart für die QM-Pro-Site) |
| `{NAMESPACE}` | `Broot.Redirect` |
| `{CODE_PREFIX}` | — (kein Präfix; der bestehende Code verwendet keines, z. B. `RedirectRule`) |
| `{QUEUE_NAME}` | — (kein Messaging) |
| `{ARCHITECTURE_OWNER}` | `Robert Fodor` |

---

## Projektübersicht

**broot-redirect** leitet alte URLs migrierter SharePoint-Dokumente anhand administrierbarer Regeln auf die neuen URLs weiter — entweder direkt (HTTP 302) oder über eine Info-Seite, die auf den veralteten Link hinweist. Regeln, Texte und Verhalten sind zur Laufzeit über ein Admin-Panel bearbeitbar.

### Stack
- **Backend**: .NET 8 / ASP.NET Core, Azure Table Storage, ein einzelner Container → `Broot.Redirect.API/`, `Broot.Redirect.Core/`, `Broot.Redirect.Infrastructure/`
- **Frontend**: Angular 19 (Standalone Components, Signals), wird nach `wwwroot/` gebaut und von der API ausgeliefert → `Broot.Redirect.Client/`
- **SPFx**: SharePoint Framework, React, Fluent UI v8, PnPjs v4, Jest; eigenes `.sppkg` für den App Catalog der QM-Pro-Site → `Broot.Redirect.SPFx/` (US-13.002, ADR-003)
- **Tests**: xUnit, NSubstitute, FluentAssertions, Azurite-Integrationstests → `Broot.Redirect.Tests/`
- **Anforderungen**: User Stories & Epics → `docs/requirements/`

---

## Repo-Struktur

```
broot-redirect/
├── CLAUDE.md
├── README.md                          # Umfassende technische Dokumentation (Architektur, Matching, API)
├── apm.yml                            # APM-Abhängigkeiten (broot-ai-core, broot-ai-dotnet)
├── docs/
│   └── requirements/                  # User Stories & Epics
│       └── README.md                  # User Story Index
├── Broot.Redirect.API/                # Controller, Middleware, DTOs, Auth, Import/Export
├── Broot.Redirect.Core/               # Domain-Modelle, Interfaces, Matching- und Transformationslogik (kein I/O)
├── Broot.Redirect.Infrastructure/     # Table-Storage-Repositories, In-Memory-Caches, Background Services
├── Broot.Redirect.Client/             # Angular-SPA (Admin-Panel und öffentliche Info-Seite)
├── Broot.Redirect.SPFx/               # SPFx-Solution (WebPart «Redirect Regeln» für die QM-Pro-Site, eigenes .sppkg)
└── Broot.Redirect.Tests/              # Unit- und Integrationstests
```

---

## Fachliche Referenzquellen

Bei Unklarheiten über das gewünschte Verhalten — in dieser Reihenfolge:

1. **`docs/requirements/`** — User Stories beschreiben das Soll-Verhalten. Die Stories der Bestandsaufnahme wurden nachträglich aus dem Code abgeleitet und dokumentieren das Ist-Verhalten zum Zeitpunkt der Erfassung.
2. **`README.md`** — Architektur, Matching-Engine, Transformations-Pipeline, Sicherheit, API-Referenz
3. **Backend** — API-Contracts, Datenmodelle und Fachbegriffe im Code
4. **`Broot.Redirect.Client/`** — erwartete Daten und Abbildung der Fachbegriffe in der UI

---

## Architektur-Übersicht

Details in `README.md`, Kapitel 3 (Architektur) bis 9 (Sicherheit).

```
  Benutzer / Admin
        │
        ▼
  Middleware-Pipeline: RateLimit · CSRF · Session · AdminSession
        │
        ├─► RedirectMiddleware ──► 302 auf neue URL  |  Fallback: Angular-SPA (Info-Seite)
        └─► REST-Controller /api/*
                 │
                 ▼
           Core: RuleMatchingService · UrlTransformService · SmartSearchService
                 │
                 ▼
           In-Memory-Caches (Regeln, Einstellungen, globale Regeln) ── Warmup beim Start
                 │
                 ▼
           Azure Table Storage: RedirectRules · AppSettings · GlobalRules · Tracking
```

### Goldene Regeln

1. **`docs/requirements/` ist die Wahrheit** — User Stories beschreiben das Soll-Verhalten
2. **API-Contracts** — Änderungen immer mit den Erwartungen aller Konsumenten (Angular-Client, Import/Export-Formate) abgleichen
3. **Lesen aus dem Cache, Schreiben zuerst in den Storage** — Lesezugriffe enden im In-Memory-Cache; Schreibmuster ist immer «zuerst Storage, dann Cache»
4. **Single-Instance** — die Caches sind instanzlokal und werden nicht zwischen Instanzen invalidiert; Betrieb mit genau einer Replica
5. **Neue Admin-Seiten und -Endpunkte** — Route in `app.routes.ts` **und** `SpaRoutes` in `RedirectMiddleware.cs`; neues API-Präfix **zwingend** in `RequiresAuthentication` der `AdminSessionMiddleware` (sonst öffentlich)
6. **Sprachen** — Code-Artefakte (Kommentare, Klassen, Methoden, Variablen) in **Englisch**; alles ausserhalb des Codes (User Stories, Git-Commits, Dokumentation, Fachbegriffe) in **Deutsch**. Benutzersichtbare Texte und Validierungsmeldungen sind deutsch.
7. **Keine Code-Kommentare** — Claude schreibt grundsätzlich keine Kommentare im Code. Ausnahmen werden manuell vom User ergänzt oder von Claude nur auf dessen explizite Anweisung geschrieben. Abweichende Regeln für ein einzelnes Paket (z.B. dotnet-Kommentare für Interfaces, etc.) werden in der entsprechenden Paket-Instruktion festgelegt.
8. **Schweizer Orthographie** — `ss` statt `ß`; Umlaute ausgeschrieben (`ä`, `ö`, `ü`)
9. **Stil** — kein `Sie`-Form, kein `wir`-Form; neutraler, sachlicher Stil in der dritten Person

---

## Projektkontext & Rahmenbedingungen

| Bereich | Constraint | Begründung |
|---------|-----------|------------|
| Source Control | Azure DevOps (`beetrootag/Solutions/broot-redirect`) ist die Quelle der Wahrheit | Jeder Push auf `main` wird per Azure Pipeline nach GitHub gespiegelt |
| CI/CD-Pipelines | Azure Pipelines (Spiegelung), GitHub Actions (Test, Versionierung, Docker-Build nach `ghcr.io`) | Bestehendes Setup |
| Versionierung | Version-Tag nur bei `feat`/`fix`/`perf` (Conventional Commits, `default_bump: false`) | Siehe `.github/workflows/build.yaml` |
| IaC | keines — Ressourcen per `az`-CLI angelegt (siehe `README.md`, Kapitel 16) | Bestehendes Setup |
| Cloud | Azure (Container Apps oder App Service, Table Storage, optional Application Insights) | Bestehendes Setup |
| Laufzeit | .NET 8, Node.js 22, Angular 19 | Bestehendes Setup |

---

## Git-Workflow

Siehe APM-Instruktion `GIT-TRUNK-BASED-WORKFLOW` (im Kontext geladen).
