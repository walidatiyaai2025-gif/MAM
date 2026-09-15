# P12.11 Full Web UI/UX and Bilingual Closure

**Phase:** P12 — Production Readiness & Handover  
**Scope:** repository-controlled Web experience  
**Version:** `0.12.14-p12.11`  
**Branch:** `worker/p131-full-uiux-closure`  
**Status:** IMPLEMENTED — PR and exact-main acceptance pending

## Objective

Apply the approved Diwan Al Amiri Navy/Gold visual language consistently across the live Web product, close route and language-state defects, and prove that every reachable Web surface works in Arabic RTL and English LTR without replacing authoritative API data with fabricated UI values.

## Implemented closure

- A final shared visual layer normalizes page leads, cards, panels, forms, tables, tabs, status treatments, buttons, dialogs and responsive behavior without changing Central API, authorization or storage contracts.
- Sidebar order and active state are reconciled after asynchronous identity/capability loading. Administration children remain in one disclosure group and duplicate route entries remain suppressed.
- Same-document hash navigation now renders the requested route and updates the heading and `aria-current` state.
- The language action updates `lang` in the URL and preserves route, asset, search, filter, collection, tag, page, view, sort, tab and searched-state hash parameters, plus unrelated query parameters.
- An early bootstrap captures the original deep link before historical route wrappers can normalize it.
- Landing and login are now first-class bilingual surfaces. Login errors, instructions, field labels, status text and return paths are localized; the selected language survives authentication navigation.
- Loading, empty, error, permission-denied and degraded treatments share one hierarchy. Recoverable error/degraded content has an explicit retry action.
- Logical CSS properties, focus-visible treatment, semantic status/alert roles, accessible disclosure state, input/button names and reduced-motion handling are enforced by the closure layer.
- Responsive behavior is explicitly verified at 1920, 1440, 1366, 1024 and 390 CSS pixels. Mobile uses a focusable menu action, dismissible scrim, single-column content and no horizontal page overflow.

## Complete Web route inventory

| Surface | Route | Arabic RTL | English LTR |
|---|---|---:|---:|
| App | Dashboard (`dashboard`) | PASS | PASS |
| App | Media Library (`library`) | PASS | PASS |
| App | Asset Details (`asset`) | PASS | PASS |
| App | Curation Actions (`curation-actions`) | PASS | PASS |
| App | New Ingest (`ingest`) | PASS | PASS |
| App | Add Media (`upload`) | PASS | PASS |
| App | Processing Queue (`queue`) | PASS | PASS |
| App | Reports (`reports`) | PASS | PASS |
| App | Backup Protection (`protection`) | PASS | PASS |
| App | Administration (`admin`) | PASS | PASS |
| App | System Settings (`settings`) | PASS | PASS |
| App | Categories (`categories`) | PASS | PASS |
| App | Reference Library (`references`) | PASS | PASS |
| App | Media Permissions (`mediaPermissions`) | PASS | PASS |
| App | Management Actions (`admin-actions`) | PASS | PASS |
| App | Content Search (`search`) | PASS | PASS |
| App | My Permissions (`myPermissions`) | PASS | PASS |
| Standalone | Landing (`/`, `/landing`) | PASS | PASS |
| Standalone | Secure Login (`/auth/login`) | PASS | PASS |

The server has no separate repository-controlled 403/404 template. Authorization denials and missing/unavailable records are rendered through the shared in-app state treatments on the relevant route.

## Rendered evidence

The Web rendered acceptance suite now produces:

- paired Arabic/English 1440px evidence for Dashboard, Media Library, Asset Details, Add Media, Reports, Administration and Content Search;
- 1920px, 1366px and 1024px Dashboard evidence in both directions;
- 390px mobile Dashboard evidence in both directions;
- bilingual Landing desktop and Login mobile evidence;
- `route-language-matrix.csv` containing the complete route/language audit result.

Every render asserts the exact CSS viewport, expected `lang`/`dir`, expected route where applicable, meaningful visible content, valid PNG dimensions and zero horizontal document overflow.

## Functional and regression evidence

- `MAM.P01.UiAcceptance.Checks` verifies script ordering, complete route/state inventories, standalone localization, logical properties, responsive breakpoints and accessibility hooks.
- `MAM.P01.WebVisualAcceptance.Checks` verifies every route in both languages, the standalone pages, mixed-language markers, deep-link language preservation and same-document sidebar navigation.
- Existing functional-button, legacy-action and phase regression gates remain authoritative for repository-controlled actions. Destructive production mutations are not simulated by visual acceptance.
- Generated screenshots use actual runtime responses. When Central API dependencies are absent, the UI proves its real degraded/error/empty contract instead of showing invented assets, users, storage values or audit rows.

## Production boundary

This closes repository-controlled Web UI/UX implementation only after its PR and exact-main gates are green. It does **not** mark production DNS/TLS, SQL/storage topology, physical capture hardware, production identity binding, code signing, target-site performance, final device UAT or authorized go-live approval as PASS. P12 remains ACTIVE until those owner/site requirements have real traceable evidence.

`UNPUSHED_WORK=NONE`
