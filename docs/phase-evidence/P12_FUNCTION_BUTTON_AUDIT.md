# P12 Functional Controls & Button Wiring Audit

**Phase:** P12 — Production Readiness & Handover  
**Scope:** repository-controlled Windows Desktop and responsive Web product UI  
**Status:** IMPLEMENTED — PR/exact-main validation pending  
**Audited base:** `66277e8c7e473ed40718c2acb2cf1927d3fd349e`

## Audit rule

A user-operable capability is not accepted merely because an API/service method exists. It must have a discoverable UI entry point, an explicit control when an action is required, a real handler wired to the Central API/application capability, and a visible success/failure/degraded/permission result.

Read-only views may load when their navigation item is selected; they do not require a redundant action button. Background worker internals and production/site OWNER_LAST evidence are intentionally outside the button requirement.

## Findings on the audited base

The audited base contained real functional-control gaps:

1. **Backup Protection shared the Administration route on both clients** and could race P08 Administration; it also hooked Asset Details, competing with processing/curation rendering.
2. **Desktop processing omitted the PDF inline/document profile** although the application contract exposed `pdf-inline-v1` and Web already exposed it.
3. **Desktop processing enqueue/retry paths contained silent exception handling**, so a click could fail with no user-visible result.
4. **Curation exposed bulk metadata and remove-from-collection APIs without clear product controls.**
5. **Administration exposed user/role and bilingual dictionary mutations without clear product controls**; Desktop also lacked explicit policy save/validate and audit export actions.
6. Several actions existed but did not make the API result sufficiently explicit for an operator audit.

These findings are treated as audit failures until the implementation and regression gates are green.

## Remediation matrix

| Capability | Windows Desktop | Web | Execution boundary |
|---|---|---|---|
| Create catalog asset | Existing Create button | Existing Create button | Central API catalog |
| Choose file + start/resume upload | Existing explicit controls | Existing explicit controls | Central API resumable upload |
| Technical inspection | Explicit button | Explicit button | Processing job queue |
| Video proxy | Explicit media-aware button | Explicit media-aware button | Processing job queue |
| Image preview | Explicit media-aware button | Explicit media-aware button | Processing job queue |
| Audio preview | Explicit media-aware button | Explicit media-aware button | Processing job queue |
| PDF document inspection/inline preview | **Added Desktop PDF action** | Existing/retained PDF action and inline preview | Processing API, server-mediated bytes |
| Retry failed processing job | Explicit Retry + visible result | Explicit Retry + HTTP result check | Processing API |
| Search/filter library | Navigation/search controls | Navigation/search controls | Curation search |
| Edit metadata | Existing Save metadata action | Existing Save metadata action | Curation API |
| Archive / Restore | Existing explicit action | Existing explicit action | Curation API |
| Create collection | Existing explicit action | Existing explicit action | Curation API |
| Add asset to collection | Existing + action-center control | Existing + action-center control | Curation API |
| Remove asset from collection | **Added explicit control** | **Added explicit control** | Curation API |
| Bulk metadata | **Added Curation Actions page** | **Added Curation Actions page** | Curation bulk API |
| Backup Protection page | **Added dedicated nav route** | **Added dedicated nav route** | Protection API |
| Queue pending backup copies | Explicit action | Explicit action | Protection API |
| Queue integrity recheck | Explicit action | Explicit action | Protection API |
| Look up asset protection state | **Added explicit asset-ID lookup** | **Added explicit asset-ID lookup** | Protection API |
| Tape capture preflight | Existing explicit action | N/A — Windows-only by design | Windows capture provider |
| Start recording | Existing explicit action | N/A — Windows-only by design | Windows capture provider |
| Refresh capture status | Existing explicit action | N/A — Windows-only by design | Windows capture provider |
| Stop/finalize/upload capture | Existing explicit action | N/A — Windows-only by design | Capture + Central API upload |
| Policy validate / reference test / save | **Added Desktop Management Actions** | Existing Administration controls | Administration API |
| User/role authorization record | **Added explicit new/save controls** | **Added explicit new/save controls** | Administration API |
| Bilingual dictionaries | **Added load/new/save controls** | **Added load/new/save controls** | Administration API |
| Audit CSV export | **Added explicit save action** | Existing Export CSV action | Administration API |
| Operations/DR state | Navigation view | Navigation view | Operations API |
| Diagnostics bundle | Existing explicit action | Existing explicit action | Operations diagnostics API |
| Arabic / English | Existing language control | Existing language control | Client presentation only |

## Separate-route correction

Backup Protection is now a first-class `protection` route rather than sharing `admin` or `asset`. This prevents asynchronous render races and gives operators an unambiguous destination for backup/protection functions.

`Curation Actions` and `Management Actions` are explicit audit-added entry points for user-operable capabilities that previously existed only in service/API contracts or as incomplete read-only surfaces.

## No-button-by-design classification

The following are deliberately not product buttons:

- processing worker `LeaseNextAsync`, `HeartbeatAsync`, and `ProcessAsync`;
- queue workers and automated backup/processing execution loops;
- health/list/detail GETs that are loaded by selecting their product page;
- internal SHA-256 verification, durable lease management and recovery bookkeeping;
- P12 owner/site evidence such as real DNS/TLS approval, production credentials, physical hardware certification, code-signing key custody and stakeholder go-live authorization.

Adding buttons for those internal/external responsibilities would be misleading and would weaken the architecture or security boundary.

## Regression gate

`MAM.P01.UiAcceptance.Checks` now includes a module-level functional-control audit that fails before the normal UI acceptance program if:

- Protection returns to the shared Administration/Asset route;
- a built-in user-operable processing profile loses its UI control;
- processing Retry stops checking/displaying the real result;
- bulk metadata or remove-from-collection loses its controls;
- user/role or bilingual dictionary mutations lose their controls;
- the action-center script is omitted or loaded after the localization guard;
- Windows Tape Capture loses any of its core lifecycle controls.

## P12 boundary

This audit closes repository-controlled functional discoverability/wiring only after PR and exact-main acceptance are green. It does **not** represent production/site OWNER_LAST dependencies as PASS and does not declare `PRODUCTION_READY` or `GO_LIVE_APPROVED`.

`UNPUSHED_WORK=NONE`
