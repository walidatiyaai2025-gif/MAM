# P12 Bilingual Localization Completeness Audit

**Scope:** repository-controlled Windows Desktop and Web product UI  
**Phase:** P12 — Production Readiness & Handover  
**Status:** IMPLEMENTED — CI / exact-main validation required before closure  
**Base main:** `959a2824c62871313ea8cab620329b715582fcc8`

## Audit objective

Verify that switching the product to Arabic produces a genuinely Arabic, RTL user experience rather than merely changing layout direction while leaving English UI chrome behind. The audit covers visible labels, navigation, state headings, validation/error/degraded/loading/empty treatments, buttons, tooltips, placeholders, accessibility names/labels, administration/protection/operations surfaces and legacy strings emitted by earlier phase modules.

## Confirmed gap on the audited base

The previous acceptance suite proved RTL/LTR direction and rendered both languages, but it did not prove translation completeness. The audit found repository-controlled English text still visible in Arabic mode across both Windows and Web, including examples in:

- Dashboard metrics and demo rows;
- Asset Details metadata/protection/preview labels;
- ingest/upload/capture explanatory text;
- Processing Queue status text;
- Loading / Empty / API error / Permission denied / Degraded headings;
- Backup Protection metrics and action feedback;
- Administration policy/user/audit chrome;
- Operations/DR metric notes, queue labels and dependency headings;
- Web placeholders, `aria-label`, `title` and `alt` attributes;
- selected Desktop accessibility names/tooltips.

Therefore the pre-audit base must **not** be described as translation-complete.

## Remediation

### Windows Desktop

`src/MAM.Desktop/MainWindow.Localization.cs` introduces a centralized Arabic localization guard for legacy repository-controlled UI chrome. It runs after dynamic layout updates so phase-specific asynchronous surfaces are covered. English values are restored when switching back to English.

The guard is deliberately constrained to exact product strings and anchored system patterns. It does **not** translate operator-entered catalog/metadata values or arbitrary authoritative data.

### Web

`src/MAM.Web/wwwroot/ui-localization.js` is loaded after all phase UI modules. It localizes repository-controlled visible text plus `placeholder`, `aria-label`, `title` and `alt` attributes, observes asynchronous DOM changes, and restores English when switching back.

The Web guard likewise avoids `script`, `style`, `code` and `pre` content so canonical technical data and diagnostics are not rewritten.

### CI regression gate

`MAM.P01.UiAcceptance.Checks` now requires both localization guards, verifies the Web guard loads after all phase modules, requires asynchronous DOM coverage and accessibility/input attribute coverage, and requires the Arabic localization path across every phase UI module.

## Intentional invariants / non-translation scope

These are not translation defects:

- canonical identifiers and protocols such as `API`, `SQL`, `SHA-256`, `UTC`, IDs and GUIDs;
- `SecretRef`, codec/profile keys, content types and other machine-facing identifiers;
- user/operator-entered titles, tags, categories and metadata, which must remain exactly as authored;
- authoritative technical values returned as data rather than UI chrome.

Human-readable labels and messages surrounding those values remain bilingual.

## Closure gate

This audit can be marked CLOSED only after:

1. solution build succeeds with the Desktop localization guard;
2. P01 UI contract acceptance succeeds with the new completeness checks;
3. rendered Desktop Arabic RTL / English LTR acceptance succeeds;
4. rendered Web Arabic RTL / English LTR acceptance succeeds;
5. retained P09/P10/P11 regressions remain green;
6. the same checks are green on exact `main` after merge.

No owner/site P12 production requirement is changed to PASS by this repository audit.
