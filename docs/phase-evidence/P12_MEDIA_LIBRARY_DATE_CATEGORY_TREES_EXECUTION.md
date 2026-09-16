# P12 Media Library Date & Category Trees — Execution / Change Record

**Status:** ACTIVE / NOT MERGEABLE  
**Authoritative branch:** `worker/media-library-date-category-trees`  
**Baseline main:** `f9be6079fd33f9b95738ee80ce6845128c85774e`

## Requested product changes

1. Add an automatic immutable media upload date assigned by the system.
2. Add nullable Actual Production Date; blank means NULL and remains blank until explicitly supplied.
3. Add Media Library tree view by Upload Date: year → month → day → media.
4. Add Media Library tree view by Actual Production Date with a deliberate null/no-date node.
5. Replace free-text authoritative category assignment with structured categories and seed permanent bilingual Uncategorized / غير مصنف.
6. Default unclassified media to Uncategorized.
7. Show Category as a dropdown in Media Details.
8. Add Media Library category tree and permit authorized reclassification by drag/drop, right-click context menu, and keyboard-accessible alternative.
9. Selecting media in any tree navigates to authoritative Media Details.
10. Maintain SQL Server + Offline Demo SQLite parity, security, audit, bilingual RTL/LTR, responsive UX and existing MAM functionality.
11. Keep the initiative isolated until fully closed; no partial merge to main.

## Baseline findings

- `main` baseline is merge commit `f9be6079fd33f9b95738ee80ce6845128c85774e`.
- At initiative start there were no open pull requests requiring recovery/integration.
- `MediaAsset.CreatedAtUtc` exists and is system-created, but no explicit immutable Upload Date contract exists.
- Current P05 metadata contains nullable `EventDate`; it is not automatically reinterpreted as Actual Production Date.
- Current P05 category is free text (`MamAssetMetadata.Category` / `CategoryNormalized`), not an authoritative structured category entity.
- SQL Server and Offline Demo SQLite both require compatible implementation.

## Decisions

### D1 — Explicit upload date

Introduce an explicit authoritative Upload Date field and backfill existing data from the asset's existing authoritative creation timestamp. New asset creation assigns it server-side. It is display-only to clients and excluded from normal metadata update payloads.

### D2 — Production date remains semantically distinct from EventDate

Introduce nullable Actual Production Date as a separate field. Do not copy `EventDate` into it automatically. This prevents silent semantic corruption where EventDate represented a different business event.

### D3 — Structured categories

Create a structured category catalog with stable IDs/keys. Preserve existing non-empty category strings by migrating them into deterministic structured category rows and linking assets. Empty/missing category assignments resolve to the seeded Uncategorized category.

### D4 — Uncategorized is a protected system invariant

Seed bilingual Uncategorized / غير مصنف idempotently in SQL Server and SQLite. It is the default/fallback category and cannot be removed in a way that violates classification invariants.

### D5 — One merge-locked initiative

All legitimate implementation/evidence from helper worker branches must converge into the authoritative initiative branch. Only one final integration PR may target `main`, and it remains Draft/DO NOT MERGE until the charter Full Closure Gate is complete on one exact head.

## Change log

- 2026-09-16 — Initiative created from exact `main` baseline `f9be6079fd33f9b95738ee80ce6845128c85774e`.
- 2026-09-16 — Governance charter added; initiative marked merge-locked.
- 2026-09-16 — Baseline schema/contracts inspected; current free-text category and `EventDate` semantics documented.

## Implementation evidence

Pending. Record exact commits, migrations, API contracts, UI files, acceptance scripts, workflow runs and reconciliation here as implementation closes.

## Final closure evidence

Pending. This document must not claim closure until the charter gate, exact-head CI, normal expected-head merge and post-merge exact-main validation are complete.
