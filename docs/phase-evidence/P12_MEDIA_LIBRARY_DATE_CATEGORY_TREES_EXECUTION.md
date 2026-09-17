# P12 Media Library Date & Category Trees — Execution / Change Record

**Status:** CLOSURE CANDIDATE / EXACT-HEAD CI REQUIRED  
**Authoritative branch:** `worker/media-library-date-category-trees`  
**Baseline main:** `f9be6079fd33f9b95738ee80ce6845128c85774e`  
**Implementation head before this evidence update:** `febc957de47108dda8e99ecd8c93a9b4534557b1`

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
- 2026-09-17 — SQL Server migration `0013_p12_media_library_date_category_trees.sql` added and made batch-safe for clean install/upgrade execution.
- 2026-09-17 — Media Library application contracts, API endpoints and dual SQL Server/Offline Demo SQLite organization store completed.
- 2026-09-17 — Web Media Library trees and Media Details organization editor completed in English/Arabic with RTL/LTR-aware presentation.
- 2026-09-17 — Dedicated Offline Demo acceptance project and `p133-media-library.yml` exact-head closure workflow added.

## Implementation evidence

### Data and storage

- SQL Server migration adds/backfills immutable `UploadedAtUtc`, nullable `ProductionDate`, structured category rows, stable bilingual Uncategorized, legacy category preservation and default assignment trigger.
- Offline Demo uses SQLite-equivalent schema evolution, category migration/defaulting, upload-date backfill, persistence and restart-safe behavior.
- Production date remains nullable and distinct from legacy `EventDate`.

### API, security and audit

- `GET /api/v1/media-library/snapshot` returns only media visible to the current role/capability set plus authoritative categories.
- `GET /api/v1/media-library/assets/{assetId}` enforces view permission before returning details.
- `PUT /api/v1/media-library/assets/{assetId}/organization` enforces edit permission, expected-version optimistic concurrency, category validation, authoritative persistence/reread and audit evidence.
- Upload date is never accepted as a client mutation field.
- Stale versions fail with `409 concurrency_conflict`; unknown categories fail closed.

### Web UX

- Media Library provides first-class trees by Upload Date, Actual Production Date and Category.
- Production-date NULL values remain discoverable under `No production date / بدون تاريخ إنتاج` without fabricated values.
- Category changes support drag/drop, right-click context menu and a standard keyboard-focusable select/save path.
- Media Details shows read-only Upload Date, optional/clearable Actual Production Date and authoritative Category dropdown.
- Success is shown only after persistence plus authoritative reread; permission/conflict/failure states remain truthful.
- Asset navigation preserves application language state and returns to Media Library without a full-page language reset.

### Dedicated acceptance

`tests/MAM.P133.MediaLibraryAcceptance.Checks` validates Offline Demo behavior including:

- historical upload-date backfill;
- nullable production date;
- legacy category preservation;
- structured category assignment;
- immutable upload date across edits;
- optimistic-concurrency rejection;
- unknown-category rejection;
- clearing production date to NULL;
- protected bilingual Uncategorized fallback;
- new-asset default organization behavior;
- audit evidence only on successful mutations;
- persistence across a fresh SQLite provider/database instance.

`.github/workflows/p133-media-library.yml` adds exact-head build, Offline Demo acceptance, Web UI syntax/contract checks and SQL migration invariants in addition to the repository's existing regression/setup workflows.

## Convergence state before final merge

- Live `main` remained at baseline `f9be6079fd33f9b95738ee80ce6845128c85774e` when the implementation head was reconciled; the initiative branch was ahead and not behind.
- PR #58 is the single integration PR and is technically mergeable/clean, but remains Draft until this documentation update receives fresh exact-head green CI including the new P133 gate.
- No success is claimed merely from technical GitHub mergeability; final merge requires all exact-head checks green on the documentation head.
- `UNPUSHED_WORK=NONE` for the work represented by this branch.

## Final closure evidence

Pending only the fresh exact-head workflow pass triggered by this evidence update, followed by a normal expected-head merge and post-merge exact-`main` verification. If any required workflow is red, the initiative remains open and the root cause must be repaired before merge.
